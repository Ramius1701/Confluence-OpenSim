# Phlox in Confluence: where it comes from and what differs

Phlox, the second script engine (LSL, OSSL and SLua compiled to bytecode and run on a stack VM whose running state is
saved), is Tranquillity's port of InWorldz Halcyon's Phlox, imported from `tranquillity/develop` at **`ab855c0009`**
(2026-10-10). Their files keep their licence headers: Halcyon's Phlox core is Apache 2.0, the engine adapter BSD;
Tranquillity's added files carry MPL-2.0 or no header (their repository's licence). The operator documentation is
theirs, unchanged: `doc/PhloxSetup.md`, `doc/PhloxSLua.md`, `doc/PhloxKnownDefects.md`,
`doc/PhloxYEngineConsolidation.md`, `doc/Phlox-compiler-correctness.md` (their `Source/` paths refer to the table below).

| Here | Tranquillity |
|---|---|
| `OpenSim/Region/ScriptEngine/InWorldz.Phlox` | `Source/InWorldz.Phlox` |
| `OpenSim/Region/ScriptEngine/Phlox.ScriptEngine` | `Source/Phlox.ScriptEngine` |
| `Tests/InWorldz.Phlox.Tests`, `Tests/PhloxCompileProbe`, `Tests/SluaProofRunner` | the same |
| `bin/OpenSimDefaults.ini` `[InWorldz.Phlox]` | `Source/OpenSim.Server.RegionServer/AppData/OpenSimDefaults.ini` |

## How it runs here

- **Off by default.** `[InWorldz.Phlox] Enabled = false`, and `[Startup] DefaultScriptEngine` stays `YEngine`
  (Tranquillity ships Phlox enabled and as the default). With it enabled, both engines run in every region; a script
  runs on the engine its first line names (`//YEngine:`, `//InWorldz.Phlox:`, `//InWorldz.Phlox:slua`), else on the
  default.
- Registered with Mono.Addins by `ConfluenceRegistration.cs` (Tranquillity discovers modules by reflection).
- Logging: Phlox's `ILogger` calls go through `OpenSim.Framework.LoggerProvider` to log4net.
- While enabled, Phlox enforces the parcel No Scripts rules for its own scripts (`IParcelScriptPolicyEngine`); YEngine's
  scripts keep the core's check unchanged.

## Confluence changes

Confluence-only files: `Phlox.ScriptEngine/ConfluenceRegistration.cs`, `Phlox.ScriptEngine/CONFLUENCE.md`, and the five
`.csproj` files (hand-maintained, outside `OpenSim.sln`, built into `bin\`; theirs rely on Tranquillity's
`Directory.Build.props` and `Library\`). `InWorldz.Phlox.csproj` takes C5, log4net, Nini and libomv from `bin\`;
`Phlox.ScriptEngine.csproj` leaves out `LinksetDataPhloxExtensions.cs` (see below).

Edits inside their files, each marked `[Confluence: ...]`:
- `Phlox.ScriptEngine/LSLSystemAPI.cs`: linkset data uses the scene's limit (`Scene.m_LinkSetDataLimit`, the one YEngine
  uses) and upstream's `LinksetData(int limit)` constructor. Confluence's `LinksetData` is upstream OpenSim's, which
  already has the method names and SL return codes Phlox was written against, so Tranquillity's adapter
  (`LinksetDataPhloxExtensions.cs`) is not needed.
- `Phlox.ScriptEngine/PhloxEngine.cs`: casts `DetectParams.Damage`/`OriginalDamage` to float (Confluence keeps them as
  double for YEngine's own damage events).
- `Tests/InWorldz.Phlox.Tests/ScriptExecutionTests.cs`, `SlConstantsTests.cs`: source paths in Confluence's layout.

## Core hooks ported for it

Phlox needs these Tranquillity core changes; each is marked `(Tranquillity #NNN)` in the code. Without Phlox enabled
they change nothing a script or user can see, except the DEBUG_CHANNEL filter in #212.

| PR | What |
|---|---|
| #183 | Top Scripts (estate tools) asks every script engine of the region and merges per object |
| #212 | `DamageEntry`, `ScenePresence.ApplyDamage` with `OnAvatarDamage`/`OnAvatarDamageApplied` (collision damage goes through it; same amounts); `SitFlagsStored`; `SendPropertiesToAllClients`; `scriptEvents` bits for on_damage/final_damage/on_death/game_control (45-48 here: `bot_update` holds 44); `DetectParams.AdjustDamage`; cache-only `IUserAccountCacheModule.Get`; object DEBUG_CHANNEL chat goes to the object's owner only |
| #217 | `IParcelScriptPolicyEngine`; parcel tracking of objects with `OnGroupCrossedToNewParcel` and `OnObjectOwnerOrGroupChanged`; `CanRunScript` told whether the script's engine enforces parcel rules (`OnRunScriptWithEngine`) |
| #218 | `ScenePresence.HasScriptControls`, `OnScriptControlsReleased` |
| #224 | `IWorldComm.OnMessageDelivered`, `IFriendsModule.IsFriendInService`, `IProfileModule.GetUserPreferences` |
| #243 | `OnExperiencePermissionsRevoked`; blocking an Experience ends every grant to its scripts |
| #269 | `OnScriptPermissionsRemoved`; `Scene.MoveTaskInventoryItemsAllOrNone` (all-or-nothing task inventory gives) |
| #272 | `OnGroupBeginInTransit` / `OnGroupEndInTransit` |
| `IHttpServiceRequest` | completed HTTP requests read through an interface (`HttpRequestClass` keeps its fields) |

#221 (script responses offered to every engine of the region) and #279 were already in Confluence. The #212
VectorRender changes do not apply (Confluence's renderer differs).

**Two damage paths.** Confluence's YEngine has its own damage events (`LSL_Api` "Combat2": llDamage posts on_damage,
waits for llAdjustDamage, then final_damage/on_death). Phlox uses the core's `ApplyDamage`. Collision damage now goes
through `ApplyDamage`, so Phlox scripts see it; YEngine's llDamage does not, so Phlox scripts do not see YEngine's
scripted damage, nor YEngine scripts Phlox's. Unifying them is open work. (Note: YEngine has no event codes for
on_damage/final_damage/on_death, so it drops the events Combat2 posts to its own scripts; separate, open.)

## Tests

`dotnet test Tests\InWorldz.Phlox.Tests -c Release`: 40 of their 225 test files build here; **3035 passed, 2 skipped,
0 failed** (2026-10-12). The other 185 drive a test scene through Tranquillity's `OpenSim.Tests.Common`; Confluence's
`OpenSim/Tests/Common` is commented out of `prebuild.xml`, as upstream, so they are left out of the build (listed in the
csproj) until that harness exists. `Tests\SluaProofRunner` (`dotnet run -c Release` there): **60 PASS, 0 DIVERGENCE,
3 GAP**, as on Tranquillity.
