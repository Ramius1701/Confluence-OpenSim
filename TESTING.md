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
- [ ] 1.8 `osCauseDamage` and `osCauseHealing` raise `on_damage` / `final_damage` like `llDamage` (healing as
      negative damage), and `osCauseDamage` to zero kills once. `osSetHealth` sets health directly, with no events.
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
- [ ] 2.7 Vehicle record: a vehicle set up with `VEHICLE_TYPE_SAILBOAT` / `VEHICLE_TYPE_MOTORCYCLE`, the InWorldz
      parameters (11001-11006), `VEHICLE_LINEAR_WIND_EFFICIENCY` / `ANGULAR_WIND_EFFICIENCY`, a per-axis
      `llSetVehicleVectorParam(VEHICLE_LINEAR_MOTOR_TIMESCALE, ...)` or a motor decay above 120 s keeps those settings
      after take and re-rez, shift-copy and a region restart (the engine still caps a decay at 120 s). The InWorldz
      names are Phlox constants; a YEngine script uses the numbers (10001, 10002, 11001-11006, 12001, 12002).
- [ ] 2.8 Grab-drag a physical object (Ctrl-drag, outside the Build floater): it follows the cursor smoothly, can be
      lifted, stops swinging when the mouse stops, and drops when released; a part of a linkset drags the whole linkset
      by the point that was grabbed. A script's `llMoveToTarget` / `llSetHoverHeight` running before the drag resumes
      after it. A sat-on object, a vehicle or `STATUS_BLOCK_GRAB` refuse the drag.
- [ ] 2.9 Rotate a physical object (Ctrl-Shift drag): it turns to the rotation shown, without overshooting or spinning
      on after the release; a script's `llRotLookAt` / `llTargetOmega` resumes after it.
- [ ] 2.10 An attachment with `llSetBuoyancy(1.0)` makes the wearer float, `llSetHoverHeight` holds them at a height and
      `llStopHover` ends it; detaching the attachment ends both, and they survive a teleport inside the region.
- [ ] 2.11 Unlinking a part from a moving physical linkset (`llBreakLink` while it flies or spins): the part carries on
      with the speed it had instead of stopping dead.
- [ ] 2.12 `llGetMass` on an avatar just after standing up from a seat or a teleport inside the region returns the
      avatar's mass, not 0; rubber prims bounce as on Second Life (restitution 0.9).
- [ ] 2.13 ZephyrWind (`[Wind] wind_plugin = ZephyrWind` on a test region): the wind (viewer's wind display, a
      sailboat or a flag script using `llWind`) is stronger aloft than at the ground, follows the terrain, and changes
      through the region's day; a Phlox `iwSetWind` by an estate manager holds a cell's wind until reset.

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
- [ ] 4.2 While Robust is down, a script's call that needs it drops that one event with a debug-channel note ("a grid
      service did not answer ... the script keeps running"); the script's next event runs normally. If a script
      still stops with "Script must be Reset to re-enable" during an outage, report the function and the log lines.
- [ ] 4.3 With Robust down, an IAR save or load from the region console that asks for a password reports a failed
      password, not an exception.

## 5. Other script functions (ported, never tried in-world)

- [ ] 5.1 `llSignRSA` / `llVerifyRSA` with a PEM key pair: a signature made by one verifies with the other.
- [ ] 5.2 `llSetSculptAnim` on a sculpted prim animates it (shown through the texture-animation block); it threw
      on every call until 2026-10-14.
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
- [ ] 7.10 Load an InWorldz/Halcyon OAR (`load oar --merge` on a test region): projector lights project their texture,
      normal and specular maps show (Firestorm with Advanced Lighting on), parts that were physics shape None are walk-through
      - and all three are still there after a region restart.
- [ ] 7.7 A sculpted prim with Invert or Mirror ticked (Build > Features of a sculpt) collides as it looks, on
      BulletSim and Jolt; two copies of one sculpt map, one mirrored, each collide with their own shape.
- [ ] 7.8 On BulletSim, a small physical mesh (under 0.2 m) rezzed after a non-physical copy of the same mesh still
      collides with its mesh, not a box.
- [ ] 7.9 `llCastRay` with `RC_MAX_HITS` above 16 returns more than 16 hits through a row of prims, and with
      `RC_REJECT_TYPES, RC_REJECT_HOSTGROUP` the caster's own linkset doesn't use up `RC_MAX_HITS, 1`.
- [ ] 7.6 OAR import with `--lookup-aliases`: creators and owners resolve through user aliases.
- [ ] 7.11 Upload a multi-part mesh whose parts have their own names (an Avastar or Blender export with named
      objects): each prim of the linkset keeps its part's name, not "Model#1", "Model#2".

## 8. Viewer features

- [ ] 8.1 Place Profile (a landmark or the Places floater) loads instead of staying on "Loading...".
- [ ] 8.2 Search: the classic Directory floater's Groups tab finds groups (with `[Search] Module =
      "ConfluenceSearchModule"`), and Places, Land for Sale, Events and Classifieds return results.
- [ ] 8.3 Display names: changing one, then trying again inside the 7-day cooldown shows the correct notification.
- [ ] 8.4 Abuse report with a screenshot on a region using SSL caps: the report and screenshot arrive.
- [ ] 8.5 The weather addon's aurora effect is visible in a viewer.
- [ ] 8.6 Weather per region: a region shows no weather until My Regions > Weather is set On (within about a minute);
      Off clears it and gives the sky back; "Hours each weather lasts" changes how often it moves on.
- [ ] 8.7 Weather sky: a storm darkens the sky for everyone in the region, someone arriving during it sees it too,
      the sun still moves; after sunny or clear the normal sky is back; a region restart during a storm comes back with
      the normal sky (no `weather clear` needed); a parcel with its own environment keeps it.
- [ ] 8.8 Weather sky at night (0.5.0): `weather storm` at midnight leaves the sky dark (darker, with fewer stars),
      not grey; at noon it darkens and greys the sky and adds haze; the change fades in over about two minutes, and
      `weather sunny` fades it back out. No puddle or snow patches appear on the ground any more.
- [ ] 8.9 Rain and snow around you (0.6.0): standing on the ground during `weather blizzard` (or snow, rain, storm)
      you are in it - flakes or drops fall past you and reach the ground all around, on a 1024 m region too; walking
      or flying a distance, it keeps up within a few seconds; under a roof or indoors there is none; standing on a
      raised platform it falls to the platform. Report your viewer's particle setting if it looks thin.

## 9. Land, money and admin

- [ ] 9.1 Land auction: `land auction start`, bids from two avatars, `land auction end` - the winner is charged and
      receives the parcel.
- [ ] 9.2 Admin WebUI, Store orders: cancelling a real (test) order.
- [ ] 9.3 Grid-wide login toggle in `/admin/settings`: residents are refused with the configured message, admins
      still get in.
- [ ] 9.4 Hypergrid export: an object without Export permission cannot be taken to another grid; estate deny-access
      settings refuse a Hypergrid visitor.

- [ ] 9.6 `[Startup] TimerResolutionMs = 1` on a Windows region: the log shows "Windows timer resolution 1 ms
      requested" with `Thread.Sleep(1)` near 1 ms, and the region's frame times (Statistics bar, sim FPS) are steadier;
      unset, nothing is logged and nothing changes.
- [ ] 9.7 Console `wind <plugin> <param> NaN` (or `Infinity`) is refused with "Invalid value" and the wind does not
      change; `osSetWindParam` with a non-finite value logs one warning and changes nothing.

- [ ] 9.5 Behind a reverse proxy on the same host (unset `XFFTrustedProxies`): login, the WebUI and region bans
      see each visitor's own address, not 127.0.0.1. With a proxy on another machine listed in `XFFTrustedProxies`,
      the same; with it not listed, everything comes from the proxy's address.

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
- [ ] 11.5 The functions added to Phlox 2026-10-14 behave as in YEngine: partnering from a ceremony object, avatar
      freeze and fly/walk flags, `osGiveLinkInventory[List]` to an avatar, NPC tags/follow/say-to/look-at,
      `osReplaceRegionEnvironment` / `osReplaceParcelEnvironment` / `osResetEnvironment`, `osReturnObject[s]`.

## 12. Open bugs that need a live reproduction

- [ ] 12.1 Map "Find" for your own region's name fails while standing at region coordinates (128, 128) near the
      ground. Does it happen on your grid? Which viewer?
- [ ] 12.2 An avatar left as a cloud after login or teleport, cleared by Rebake Textures. When it happens, note the
      time and region and keep that region's log before restarting it.
