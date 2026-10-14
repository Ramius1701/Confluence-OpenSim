# In-world testing checklist

Everything here is built, and most of it is deployed, but has **not yet been confirmed in a real region with a real
viewer**. Unit tests and a clean start-up prove the code loads and does what the tests ask; they do not prove what a
resident sees. This list is the gap.

Anyone running Confluence can help: pick an item, try it, and report back.

## How to report

Open a GitHub issue (or comment on an existing one) titled `In-world test: <item number> <short name>` with:

- **Result:** pass / fail / partly.
- **Viewer** and version (Firestorm 7.x, Cool VL Viewer, ...).
- **Region setup:** physics engine, script engine, database (MySQL/MariaDB, PostgreSQL, SQLite), grid or standalone.
- **What you did** (the script, if one was used - paste it) and **what happened**.
- For a failure, the region log lines around the time (`bin/OpenSim.log`), with any IPs, names and passwords removed.

When an item passes, it moves out of this file into `FEATURES.md` as confirmed.

Items marked **(known gap)** describe behaviour we already know is incomplete; report only if it differs from what
is written.

---

## 1. Combat and damage (YEngine)

Needs a parcel with damage enabled (About Land > Options > Safe unchecked) and an avatar wearing a scripted attachment
that has `on_damage`, `final_damage` and `on_death` handlers.

- [ ] 1.1 A Combat2 `llDamage(avatar, amount, type)` on an avatar fires `on_damage` in the avatar's attachments,
      then `final_damage`; `llDetectedDamage` reports the amount, type and original amount.
- [ ] 1.2 `llAdjustDamage` inside `on_damage` changes the damage actually applied (health drops by the adjusted
      amount, `final_damage` shows it).
- [ ] 1.3 Several fast hits arrive grouped into one `on_damage` with several detected entries, not one event each.
- [ ] 1.4 Health reaching zero fires `on_death`; the avatar is sent home / respawns and health is reset.
- [ ] 1.5 Collision damage (a physical object with `llSetDamage` hitting an avatar) goes through the same events.
- [ ] 1.6 `llDetectedRezzer` reports the object that rezzed the damaging object.
- [ ] 1.7 Object health: `llDamage` on an object with `PRIM_HEALTH` set lowers it; `llGetHealth` on the object
      reports it.
- [ ] 1.8 **(known gap)** `osCauseDamage`, `osCauseHealing` and `osSetHealth` change health but raise no
      `on_damage` / `final_damage`.
- [ ] 1.9 Team Combat (`[TeamCombatModule] Enabled = true`): a team member who dies respawns at the team's combat
      point, cannot teleport out while in combat, and regenerates health at the configured rate.

## 2. Prim and rez parameters

- [ ] 2.1 `PRIM_DAMAGE`, `PRIM_HEALTH` and `PRIM_SIT_FLAGS` set with `llSetLinkPrimitiveParamsFast` read back the
      same from `llGetLinkPrimitiveParams`, including on a prim someone is sitting on.
- [ ] 2.2 `llGetObjectDetails` with `OBJECT_HEALTH`, `OBJECT_DAMAGE` and `OBJECT_DAMAGE_TYPE`, on an object and on an
      avatar.
- [ ] 2.3 `llRezObjectWithParams` with `REZ_DAMAGE` and `REZ_DAMAGE_TYPE`: the rezzed object carries both.
- [ ] 2.4 `llRezObjectWithParams` with `REZ_LOCK_AXES` set on Z only: the rezzed physical object cannot rotate
      around Z.
- [ ] 2.5 `llSetSitFlags` / `llSetLinkSitFlags`: each flag (e.g. no-stand, allow-unsit) behaves as on Second Life.
- [ ] 2.6 A zero-offset sit target set with `PRIM_SIT_TARGET, TRUE, ZERO_VECTOR, ...` still works after a region
      restart, and after saving and loading an OAR.

## 3. Returning objects

Needs a parcel you own, a second account's objects on it, and a script whose owner is granted
`PERMISSION_RETURN_OBJECTS` (`llRequestPermissions(llGetOwner(), PERMISSION_RETURN_OBJECTS)`).

- [ ] 3.1 The permission dialog appears; a grant from anyone other than the land owner is refused
      (`ERR_RUNTIME_PERMISSIONS`).
- [ ] 3.2 `llReturnObjectsByOwner(owner, OBJECT_RETURN_PARCEL)` returns that owner's objects on this parcel only.
- [ ] 3.3 `OBJECT_RETURN_PARCEL_OWNER` returns them from every parcel in the region the script owner owns, and not
      from anyone else's.
- [ ] 3.4 `OBJECT_RETURN_REGION` works for the estate owner or a manager, and returns `ERR_PARCEL_PERMISSIONS` for
      anyone else.
- [ ] 3.5 `llReturnObjectsByID` on a mix of objects on your land and on someone else's returns only yours.
- [ ] 3.6 Returned objects arrive in the owner's **Lost and Found** folder (they are not deleted).
- [ ] 3.7 The parcel owner's, estate owner's and managers' own objects are never returned.
- [ ] 3.8 Group-owned land: an owner of the group can return objects on it.
- [ ] 3.9 The hourly throttle returns `ERR_THROTTLED` once the limit is reached.

## 4. Scripts under service outages

- [ ] 4.1 With a region up, restart Robust. Scripts using groups (a group titler), `llRequestAgentData`, mute lists,
      offline IM or Experiences keep running: the region log shows warnings, not "Script must be Reset".
- [ ] 4.2 **(known gap)** If a script does stop with "Script must be Reset to re-enable", note which function it was
      in (the message itself is being reworded).

## 5. Other script functions (ported, never tried in-world)

- [ ] 5.1 `llSignRSA` / `llVerifyRSA` with a PEM key pair: a signature made by one verifies with the other.
- [ ] 5.2 `llSetSculptAnim` on a sculpted prim animates it (shown through the texture-animation block).
- [ ] 5.3 `llSetLinkRenderMaterial` and `llSetLinkGLTFOverrides` on a PBR material: the override shows in a PBR
      viewer and survives a region restart.
- [ ] 5.4 Region / parcel environment: `llGetEnvironment`, `llSetEnvironment`, and `llReplaceEnvironment` with a
      specific track number, seen by a second viewer on the parcel.
- [ ] 5.5 Pathfinding: `llCreateCharacter` then `llNavigateTo`, `llWanderWithin`, `llPatrolPoints`, `llPursue`,
      `llEvade`, `llFleeFrom`; `path_update` events arrive. The Pathfinding floater opens and lists linksets and
      characters (the navmesh view stays empty - **known gap**).
- [ ] 5.6 NPCs: `osNpcFollow` / `osNpcStopFollow`, `osNpcAddTag`, `osNpcGetNPCsWithTag`, `osNpcDeleteNPCsWithTag`.
- [ ] 5.7 `osMakeScript`, `osSetRot`, `llCastRayV3`.
- [ ] 5.8 `llGiveInventory` to an avatar: the IM carries a working SLURL.
- [ ] 5.9 Experiences: grant an Experience on one object, then touch a second object of the same Experience - no
      second prompt, also after a relog and a region crossing.
- [ ] 5.10 A Hypergrid teleport to another grid and back with a scripted attachment worn: the scripts keep running.

## 6. Bots

Needs `[NPC] Enabled = true` and the bot functions enabled.

- [ ] 6.1 `botMessageLinked` reaches a script worn in the bot's outfit as `link_message`.
- [ ] 6.2 Move timeout: a path the bot cannot finish ends with the timeout update.
- [ ] 6.3 `botFollowAvatar` and `botWanderWithin`.
- [ ] 6.4 Respawn key: a bot that dies or is removed respawns under the expected key.
- [ ] 6.5 A persisted bot survives a region restart.

## 7. Content and assets

- [ ] 7.1 Upload a mesh with textures from Firestorm: the faces show the textures, and no zero-byte texture assets
      are created.
- [ ] 7.2 Load an IAR containing an empty file: it is skipped with a warning in the log.
- [ ] 7.3 Dynamic textures (`osSetDynamicTextureData`, `osDrawText`) on a prim, after a region restart.
- [ ] 7.4 BulletSim: a physical prim smaller than 0.2 m on each axis collides with its real shape, not a box.
- [ ] 7.5 ubODE: a twisted torus / tube / ring collides with the twist it shows.
- [ ] 7.6 OAR import with `--lookup-aliases`: creators and owners resolve through user aliases.

## 8. Viewer features

- [ ] 8.1 Place Profile (a landmark or the Places floater) loads instead of staying on "Loading...".
- [ ] 8.2 Search: the classic Directory floater's Groups tab finds groups (with `[Search] Module =
      "ConfluenceSearchModule"`), and Places, Land for Sale, Events and Classifieds return results.
- [ ] 8.3 Display names: changing one, then trying again inside the 7-day cooldown shows the correct notification.
- [ ] 8.4 Abuse report with a screenshot on a region using SSL caps: the report and screenshot arrive.
- [ ] 8.5 The weather addon's aurora effect is visible in a viewer.

## 9. Land, money and admin

- [ ] 9.1 Land auction: `land auction start`, bids from two avatars, `land auction end` - the winner is charged and
      receives the parcel.
- [ ] 9.2 Admin WebUI, Store orders: cancelling a real (test) order.
- [ ] 9.3 Grid-wide login toggle in `/admin/settings`: residents are refused with the configured message, admins
      still get in.
- [ ] 9.4 Hypergrid export: an object without Export permission cannot be taken to another grid; estate deny-access
      settings refuse a Hypergrid visitor.

## 10. Other databases

Casperia runs MariaDB. On a grid using **SQLite** or **PostgreSQL**:

- [ ] 10.1 Create a group in-world, invite and join a second avatar, send a group notice.
- [ ] 10.2 Send an offline IM and receive it on the next login.
- [ ] 10.3 Buy an item from the Marketplace in the WebUI.

## 11. Optional engines (off by default)

Try these on a test region first.

- [ ] 11.1 **Jolt physics** (`[Startup] physics = Jolt`, `meshing = Meshmerizer`): walking on mesh floors and
      stairs from an OAR, slopes, physical prims, a vehicle, sitting and standing, a region crossing on foot and in
      a vehicle. Remove any older Jolt DLLs from the install before enabling it.
- [ ] 11.2 **Phlox scripts** (`[InWorldz.Phlox] Enabled = true`): scripts whose first line is `//InWorldz.Phlox:`
      compile and run; their state survives a region restart; YEngine scripts in the same region are unaffected.
- [ ] 11.3 Phlox SLua: a script starting `//InWorldz.Phlox:slua` runs.
- [ ] 11.4 Damage across engines: a Phlox `llDamage` raises the events in a YEngine attachment, and the reverse.

## 12. Open bugs that need a live reproduction

- [ ] 12.1 Map "Find" for your own region's name fails while standing at region coordinates (128, 128) near the
      ground. Does it happen on your grid? Which viewer?
- [ ] 12.2 An avatar left as a cloud after login or teleport, cleared by Rebake Textures. When it happens, note the
      time and region and keep that region's log before restarting it.
