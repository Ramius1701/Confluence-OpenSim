# Security hardening guide

Operator notes for the control-plane hardening in this repository (work
originally prompted by a responsible disclosure against stock OpenSimulator,
ported and adapted from OpenSim-Tranquillity's `security/control-plane-hardening`
branch). Read this before upgrading a production grid. For the feature list see
`FEATURES.md`; for how it was built and what went wrong along the way see
`PROJECT_LOG.md`.

## Control-plane trust

Some HTTP endpoints exist for servers to talk to each other, but are served on
the same public ports as viewer and Hypergrid traffic. The endpoints listed
below now only accept callers that are one of your own servers.

**Nothing needs configuring for a grid whose regions and Robust run on one
machine** - including behind a home router. These are trusted automatically:

- loopback;
- this machine's own network interfaces and their default gateways (a router's
  NAT loopback rewrites a same-box call's source to one of these);
- whatever `[Const] BaseHostname` resolves to.

Only set `ControlPlaneTrustedHosts` when Robust or other regions run on
**different machines**. Put it in `[Network]` (or `[Security]`) of every region
and of Robust:

```ini
[Network]
    ControlPlaneTrustedHosts = "203.0.113.10, region2.example.com"
```

Entries may be IPv4/IPv6 addresses, hostnames, or URLs, separated by commas,
semicolons, pipes or whitespace. Hostnames are resolved once at startup - restart
the process if an address changes. List the real *outbound* address of each
region host and each Robust host. Do not list public client networks, foreign
grids or broad ranges.

### Protected endpoints

| Endpoint | Where | Untrusted caller gets |
|---|---|---|
| `POST /agent/...` (create agent - **grid login uses this**) | region | 403 |
| `POST /object/...` (object crossing/creation) | region | 403 |
| `POST /region/...` (neighbour hello) | region | 404 (deliberately, so scanners can't confirm it exists) |
| `POST /friends` (inter-region friendship state) | region | 403 |
| privileged instant-message dialogs (forced/silent teleport, god kick) | region, Robust | delivery refused |
| Hypergrid groups `POSTGROUP`, `ADDNOTICE` | Robust | 403 |
| Hypergrid logout, unless the session is a real tracked one | Robust | `false` result |

Any request carrying an `X-SecondLife-Shard` header is refused on these paths
even from a trusted address, so an in-world `llHTTPRequest` cannot use a trusted
region as a proxy. Query, update and delete on `/agent/` and `/object/` keep their
protocol behavior (Hypergrid teleport handoff needs them).

### Diagnosing a refusal

Every refusal logs a warning naming the address to add, at most once a minute per
source address and endpoint:

```
[CONTROL PLANE ACCESS]: Refusing POST /agent from 203.0.113.7: source address is not a trusted control-plane host. ...
```

At startup each process logs `N trusted control-plane addresses`; set the log
level to Debug to see the list.

- **Login fails with "Service request failed: [403]"** - credentials were fine;
  the login service (Robust) could not create your root agent on the destination
  region because the region did not trust Robust's address. On a single machine
  this should not happen; on several, add Robust's outbound address to that
  region's `ControlPlaneTrustedHosts`.
- **Teleport or region crossing fails with 403** - add the *source* region's
  outbound address to the *destination* region.
- **"Exception on DoHelloNeighbourCall ... 404"** - the first region named is the
  HTTP destination; add the *other* region's outbound address to it.
- **A third-party service that POSTs straight to `/agent/` or `/object/`** gets 403
  until its stable outbound address is listed. Confirm the exact endpoint and
  addresses with the provider first. Services using normal viewer login, CAPS,
  inventory and asset APIs are unaffected (the bundled marketplace delivers via the
  inventory service and its own separately-authenticated endpoints).

## Reverse proxies and client IPs

`X-Forwarded-For` is honored only when the direct TCP peer is a trusted proxy:
`[Network] XFFTrustedProxies` (bar-separated addresses, hostnames or CIDR ranges, in
OpenSim.ini and Robust.ini; OpenSimulator's option since 2026-10). Unset, Confluence
trusts this machine only (127.0.0.0/8 and ::1), so a proxy on the same host keeps
working; `none` trusts no proxy. From any other peer the header is removed and the
socket address is used, so a forged header cannot fake a trusted or unblocked source.
A proxy on another machine must be listed there, or it is seen as the proxy's address.

From a trusted proxy the chain is read from the right: the proxy appends the address it saw, and the first
address that is not itself a trusted proxy is the client, so nothing the client wrote to the left of it counts.
An entry there that is not an address makes the client unknown (an address no check trusts), never the proxy's own
address. Several `X-Forwarded-For` headers are one chain, in order. The RFC 7239 `Forwarded` header is not read:
proxies such as Apache pass it on as the client sent it. Header entries are parsed as addresses only (no DNS).
(2026-10-19: before this the left-most, client-written entry was believed from a loopback proxy, so a forged
`X-Forwarded-For: 192.168.1.1` or `Forwarded: for=...` sent to a site fronted by a proxy on the same machine could
claim this machine's or its gateway's address and pass the control-plane checks. Update; and on Apache, until then,
`RequestHeader unset X-Forwarded-For early` and `RequestHeader unset Forwarded early` in the virtual host strip the
client's own copies before the proxy adds its own.)

## Encrypted (HTTPS) logins

**Why it matters.** A viewer logs in by sending the resident's name and an MD5 hash of the password (`$1$...`).
Over plain `http://` both travel unencrypted, and the hash is as good as the password: anyone who captures it can
log in as that resident without ever learning the password. The risk is on networks the resident doesn't control
(public Wi-Fi, hotels, a compromised router); a login from the grid's own machine never leaves it. Second Life
accepts only https logins. Aurora Viewer 0.9.14 and later (and Confluence Viewer when it takes that change) ask
once per server before sending a login to a plain `http://` address that is not on the same PC; other OpenSim
viewers log in over http without asking, and all current ones can also log in over https.

**Offering it.** Robust's login handler answers on its public port (`[Const] PublicPort`, 8002 by default). The
simplest way to add https is a reverse proxy that already serves the grid's host name with a valid certificate
(Apache, nginx, Caddy), forwarding to that port. Robust answers an XML-RPC login on any path, so the proxy can
forward the whole site or a path of it. Keep the plain port open: residents who saved it keep working. See
"Reverse proxies and client IPs" above so the login sees each resident's own address. Robust's own SSL listener
works too, but needs its certificate in PFX form after every renewal.

**What to advertise.** `[GridInfoService] login` in Robust.HG.ini (or Robust.ini) is the address `get_grid_info`
hands viewers, and a viewer copies it when the grid is added and when it refreshes the grid info, replacing an
address the user typed. So:

- Advertise `http://<host>:<PublicPort>/` (the common choice) and residents who want encryption type
  `https://<host>/`; a viewer that refreshes the grid info puts the http address back.
- Advertise `https://<host>/` and every new resident logs in encrypted, through the proxy; their logins then
  depend on the proxy being up.

Hypergrid is not affected either way: it uses `[GridInfoService] gatekeeper` (and the `[Hypergrid]`
GatekeeperURI), which stay on the public port. The WebUI's `hop://` links use the gatekeeper too (since
2026-11-26); its password-reset and verify links use the login address.

**The usual mistake.** `https://` with the plain port (`https://<host>:8002/`) cannot work: the port speaks
http, so the encrypted handshake fails before any request is sent, and nothing reaches Robust's or the proxy's
log. Aurora shows it as "network error: error sending request". Use `https://<host>/` (the proxy's port 443) or
`http://<host>:8002/`, never the two mixed.

## Hypergrid behavior

- **Returning home** to this grid over Hypergrid (not a fresh login) is allowed only
  when a travel session already exists for that circuit, belongs to the same
  user, and presents exactly the `ServiceSessionID` token this grid minted the
  last time it legitimately sent that avatar out - the same token check used to
  authenticate every inbound Hypergrid agent, now also checked against the travel
  row's own agent id rather than just "some session is travelling home." A grid
  that never genuinely hosted the avatar can't produce the token; a captured
  request can't be replayed after the real one lands, since the token is rotated
  on every hop and the rotated value is carried back through every region the
  trip passes through. A launch that's already on this grid, or a call with no
  travel session, is also refused. Only a password login - which never arrives
  this way - is exempt. `/homeagent` itself is never treated as a login, even
  from an address that matches the login server's, closing a path that let a
  caller skip every one of these checks by claiming to be the login server.
- **A UDP circuit's source address** can optionally be required to match the one
  recorded when the agent was authorised (`RejectCircuitIPMismatch`, default
  `false` - opt-in, since the failure mode reads as a network fault rather than
  a policy decision). Private, loopback and link-local addresses are always
  exempt, since NAT and local proxies legitimately change the source address.
- **Egress filtering**: caller-supplied Hypergrid HomeURI/gatekeeper URLs are
  refused before any outbound verification or agent transfer if they resolve to
  loopback, private, link-local, CGNAT, unique-local, multicast or reserved
  addresses. Your own gatekeeper stays allowed. The check validates DNS at request
  time only - it does not pin the address for the connection or constrain
  redirects - so keep firewall egress rules in place as well.
- A foreign duplicate-presence claim only displaces an existing foreign session
  when the stored session's home matches the claimed HomeURI.
- Hypergrid friendship deletion and presence notifications require an exact
  friend UUID and the complete shared secret.

## Defaults and logging

- The OpenID connector is commented out in `Robust.ini.example` and
  `Robust.HG.ini.example`. It is a legacy OpenID 2.0 identity provider, is not used by
  viewer login, and has no rate limiting. Enable it only if you deliberately run a
  consumer site that needs it, and then also set `[LoginService] OpenIDServerURL`.
- Reusable web login keys are no longer logged, and neither is the Hypergrid
  service token.
- Map-tile requests always release their process-wide lock; one bad request can no
  longer wedge every later tile request.

## Gloebit add-on

Transaction callbacks now require a random per-transaction key, and OAuth linking
validates a persisted one-shot `state`. Back up the database first: the migrations
add `CallbackKey` (`GloebitTransactions`) and `PendingAuthState` (`GloebitUsers`).

## Profile JSON-RPC gate

The sensitive profile methods are refused unless the caller is a trusted control-plane
host: private notes (read and write), preferences/email, profile and interests writes,
picks and classifieds writes and deletes, and user-data writes. A refusal answers
"Method not found", so a scanner learns nothing about which methods exist, and the
log gets one throttled `[CONTROL PLANE ACCESS] Refusing JSON-RPC ...` line per
source and method per minute. Public profile *reads* (properties, picks, classifieds,
interests, image assets) stay open, so profiles remain viewable from other grids.

The calls are made by each region's profile module for the viewer, not by viewers, so
on a normal grid the callers are your own region processes and are covered by the same
trusted-host discovery as the rest of this page. Requests carrying the in-world-script
HTTP marker are refused even from a trusted address.

**Trade-off:** a resident of this grid visiting *another* grid has that foreign region
call your profile server, from an address you do not trust. Their private notes and
preferences are unavailable, and profile edits made there do not save, until they are
back on a trusted region. This matches upstream Tranquillity's choice.

## PostgreSQL group search injection

On PostgreSQL, the in-world group search pasted the resident's search text directly into its SQL
(`PGSQLGroupsData.RetrieveGroups`), so anyone able to search for groups could run their own SQL against
the groups database. The MySQL backend escaped the text; PostgreSQL did not, and the "parameter" it
passed was the finished query. The text is now a real bound parameter. The SQLite backend (new) does the
same. If you ran Confluence or stock OpenSimulator on PostgreSQL with Groups enabled, update; nothing in
your configuration changes.

Two related PostgreSQL group bugs were fixed at the same time: an empty group search (and the admin
groups overview built on it) failed with a SQL error, and the two-week clean-up of old group invitations
and notices never ran on PostgreSQL 12 or newer (it used a column type that release removed), so they
accumulated without limit.

## Scripted outbound HTTP

Three gaps let a script reach addresses `[Network] OutboundDisallowForUserScripts` is meant to block,
found reviewing a same-day upstream Tranquillity fix and confirmed present here before porting:

- `llSendRemoteData`'s destination was never checked against the outbound filter at all - only
  `llHTTPRequest` was. A script could reach any address, including your own internal network, through
  `llSendRemoteData` alone.
- The `Image` dynamic-texture draw command (`llSetPrimitiveParams`/`osSetDynamicTextureURL`-style texture
  rendering) fetched its URL with no filter check either, and followed redirects automatically with no
  check on the redirect target.
- `HTTP_MIMETYPE` on `llHTTPRequest` was written into the outgoing `Content-Type` header with no
  validation - a line break in that one parameter could inject arbitrary header lines.

All three are fixed: `llSendRemoteData` and the `Image` draw command now go through the same
`OutboundUrlFilter` `llHTTPRequest` already used, including every redirect hop, not just the first URL;
`HTTP_MIMETYPE` is validated as a real media type before being sent, and an invalid one refuses the
request instead of sending it. No configuration changes - the same `[Network]
OutboundDisallowForUserScripts`/`OutboundDisallowForUserScriptsExcept` keys already cover all three paths
now. Replace the binaries and restart (scripts using these three paths get the same "disallowed by
filter" error `llHTTPRequest` already gives for a blocked destination).

Later (2026-11-08): OpenSimulator master commit 7a5950df3e stopped trimming the entries of these lists, so an
entry written with spaces around the `|` (`10.0.0.0/8 | 192.168.0.0/16`) no longer matched, and every range after
the first was let through. Confluence trims them again (a test covers it). If you run upstream OpenSimulator from
after that commit, write the lists without spaces until it is fixed there.

## SQLite stores and authentication tokens

From Tranquillity #376 (2026-11-08): the SQLite authentication, grid-user and user-account stores built some of
their SQL from the values they were given; they now pass them as query parameters. The authentication handler
accepts a token only if it is a UUID, so a crafted token string never reaches a store. Only grids on SQLite were
exposed; nothing in the configuration changes.

## Listening address

From Tranquillity #412 (2026-11-18), adapted: the HTTPS listeners (a region's SSL main server and out-of-band
listener, Robust's `https_main` and `https_listener`) bound to every interface whatever the configuration said, and
so did servers that modules open later (scripted URLs, remote admin on its own port, ConsolePort). They now follow
the address the process is given: a region's `[Network] http_listener_address`, Robust's `[Network] address`
(Tranquillity named a new key, `bind_ip_address`; we keep the keys this tree has had since Bill Blight's 2019
commit `e8e91167db`, which reached us through a donor - opensim-master has no listening-address setting at all). `127.0.0.1` keeps a process to this machine.
A value that is not an IP address is reported at startup instead of silently meaning every interface. Unset, as on
Casperia, nothing changes.

## Group powers and land sales

From Tranquillity #407 and #405 (2026-11-18). Groups (upstream OpenSim behaviour, unchanged here until then): a
member's abilities came only from the role whose title they showed, so a second role's abilities did nothing and
switching titles changed what a member could do. The service also stored any role as the shown title without
checking the member held it: a stock viewer offers only held roles, but a modified viewer or a crafted request could
pick the Owners role and get owner powers. Powers are now the union of the roles held, owners hold every power,
a title can be shown only from a role held, nobody ejects an owner, and only owners assign the Owners role. Land: a
parcel whose sale was cancelled in the viewer stayed buyable for nothing while "sell objects with land" was ticked;
a purchase for a group needed nothing of the buyer; a failed payment still handed the parcel over; a deed could go
to a group other than the parcel's. All closed. Gloebit's own after-sale check is unchanged (vendored as is), so a
purchase *for a group* on a Gloebit region fails that check until Gloebit takes the same change.

## Deployment checklist

1. Back up the database (Gloebit migrations).
2. On a multi-machine grid, list every region and Robust outbound address in
   `ControlPlaneTrustedHosts` on every host.
3. Replace the binaries, then restart Robust **and** every region - a running
   process keeps the old assemblies loaded.
4. Test: login, teleport between regions, region crossing, object crossing,
   friends, and Hypergrid travel. Watch the logs for `Refusing` lines.
5. Keep service ports behind firewall rules as well as the application allowlist.
6. Tell users that Hypergrid return-home now requires a fresh login.
