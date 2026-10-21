# Jolt in Confluence: where it comes from and what differs

The Jolt physics module, backend and vehicle controller are JohnLegionH's work on Tranquillity, first on branch
`lane/jolt` (remote `tranquillity-johnlegionh`), imported at `fbd824abeb` (2026-10-11), and since merged into
Tranquillity's `develop` (#329, #334, #338-#344, 2026-10-19). Confluence is synced to **develop `7c74cb6bed`**
(2026-10-21; for the module this is lane/jolt's head), plus two PRs develop has not merged yet: #359 (metrics at Debug
level, `RepeatLimiter`, `[Jolt] MetricsLogIntervalSeconds`) and #351's `MaximumLinearVelocity` (2026-10-22). When
develop merges them they drop out of the next sync's three-way merge as already present. Their files keep their licence headers: most are MPL-2.0
(Legion Builds); the vehicle controller carries the InWorldz Halcyon notice. The operator documentation is
`doc/JoltPhysics.md` (theirs; Confluence's only edits put direct quotes from the Second Life wiki into our own words,
with the page's link).

| Here | lane/jolt |
|---|---|
| `OpenSim/Region/PhysicsModules/Jolt` | `Source/OpenSim.Region.PhysicsModules.Jolt` |
| `OpenSim/Region/PhysicsModules/Jolt.Backend` | `Source/OpenSim.Region.PhysicsModules.Jolt.Backend` |
| `OpenSim/Region/PhysicsModules/Jolt.Vehicles` | `Source/OpenSim.Region.PhysicsModules.Jolt.Vehicles` |
| `OpenSim/Region/PhysicsModules/Jolt.Diagnostics` | `Source/OpenSim.Region.PhysicsModules.Jolt.Diagnostics` |
| `OpenSim/Region/PhysicsModules/SharedBase/IMesherShapeData.cs`, `PrimProfileRings.cs` | the same names in `Source/OpenSim.Region.PhysicsModules.SharedBase` (#325) |
| `bin/OpenSimDefaults.ini` `[Jolt]` | `Source/OpenSim.Server.RegionServer/AppData/RegionServerDefaults.ini` `[Jolt]` (copied whole) |

## Confluence changes

Confluence-only files:
- `Jolt/ConfluenceRegistration.cs`: registers the module with Mono.Addins and forwards to `JoltModule` (replaces their
  `PluginRegistration.cs`, which is kept but not compiled).
- `Jolt/ConfluenceLogging.cs`: a `LoggerProvider` in the module's namespace that sends their `ILogger` calls to log4net.
- `Jolt/JoltScene.MeshAssetRetry.cs`, `Jolt/JoltPrim.MeshAssetRetry.cs`: fetch an unfetched sculpt/mesh asset and
  re-cook the prim (lane/jolt leaves it a bounding box).
- The four `.csproj` files (hand-maintained; `runprebuild` adds them to the generated `OpenSim.sln`; their projects rely on Tranquillity's
  `Directory.Build.props`). `Jolt.Diagnostics` holds the `jolt` test console commands, which the module loads only with
  `[Jolt] TestCommands = true` (`JoltDiagnosticsLoader.cs`); `Jolt.Diagnostics/ConfluenceAssemblyInfo.cs` lets its tests
  see its internals.
- Meshmerizer (`Meshing/Meshmerizer/Meshmerizer.cs`, `PrimMesher.cs`): #325's `IMesherShapeData` (a prim's profile rings
  and path, which Jolt's prim hull cells read), ported by hand into Confluence's mesher.

Edits inside their files, each marked `[Confluence: ...]`:
- `Jolt.Backend/JoltPhysicsBackend.cs`:
  - JoltPhysicsSharp 2.23.0 API: impulses as a float span; character contacts as one `CharacterContact`, whose
    `ContactNormal` is **negated** (Jolt 5.5+ passes it pointing towards the character; 5.4 passed it away);
    `PushCharacterCharacterContact` takes a nullable character.
  - Teardown: after the system's native destroy, the three layer-filter wrappers are disowned (`Disown`) before
    `Dispose`. On 2.22+ they own their handles and the system destroy already deleted them (heap corruption otherwise).
  - No stick-to-floor for a flying avatar (`stick = !hovering && !rec.Flying && gz < 0f`).
  - Build warnings (2026-10-13): `BodyLockRead/Write.Body!` where the lock `Succeeded` (2.23 annotates `Body` nullable),
    and `string?` for the allocator-site fields that start null. No behaviour change.
- `Jolt.Backend/JoltNative.cs`: `PackageVersion` 1.1.0 and the 1.1.0 stock hashes; their patched builds (made from
  1.0.4's joltc) are not listed.
- `Jolt/JoltScene.cs`: two one-line hooks for the mesh-asset retry.
- `Jolt.Vehicles/VehicleController.cs`: `Vector3`/`Quaternion.Normalize(in x)` (Confluence's libomv takes them as
  `ref readonly`; a build warning otherwise).

Confluence runs JoltPhysicsSharp **2.23.0** with JoltPhysics.Native **1.1.0** (Jolt Physics v5.6.0). lane/jolt pins
2.19.1 / 1.0.4 for its patched joltc, which lets several regions in one process step in parallel. On Confluence's stock
native a process with several regions runs them on one job pool, taking turns: safe, just not parallel. A grid owner who
needs parallel stepping would need a patched joltc rebuilt for 1.1.0 (lane/jolt's `native/joltc` recipe, not carried here).

Core support from Tranquillity #270 (`1162fc6dda`) is ported separately: `PhysicsActor.SetConstantForce`,
`ScenePresence.SetConstantForce`, the `AgentData` constant-force fields, and `llSetForce` from an attachment in
`LSL_Api.cs`.

## Tests

develop's layout, with Confluence project files (`Tests/OpenSim.Region.PhysicsModules.Jolt*`: the module, Backend,
Vehicles, Diagnostics, Harness and the three Scenarios projects, plus `Tests/JoltTestShared` and
`Tests/JoltPhysicsHarness`). Run each with `dotnet test <project> -c Release`. Test edits are marked `[Confluence]`:
Confluence source paths, the stock-only native record (patched cases use a synthetic record), the flat `bin` layout,
the boat preset (Confluence's core saves the documented boat flags), and the ten-box tower at 22.5 Hz, which on the
1.1.0 native sways just above Jolt's 0.03 m/s sleep threshold and is run at 0.035; the documented airplane's ground roll at 11 Hz with one physics step per heartbeat (13.71 m/s against 14.90; left out, as `PhysicsStepRate` ships at 45); and the InWorldz mouselook turn, whose car rests up to 1.1 degrees short of the camera inside the 2.8 degree dead zone (checked at 1.25 degrees); a convex ring dropped on a pole, which tips off its top at 2.04 m (checked from 1.9; it still
never threads); and a physical mesh scaled to 4 mm thick, whose hulls Jolt 5.6's hull builder refuses, so it is a 4 mm
bounding box. The first four are contact, where the 1.1.0 and 1.0.4 solvers differ; one test's expected text named
1.0.4 and now names the package version. Not compiled: `NotSelectedTests.cs`
and `DiagnosticsAssemblyTests` (Tranquillity plugin discovery), `CrossingTargetsArrivalTests` and
`DragCrossingArrivalTests` (#337, not taken), `MeshAssetFetchTests` and `SculptFlagsTests` (CoreJ2K / SkiaSharp image
decoding, which Confluence does not ship; one sculpt case in `PrimHullWiringTests` is behind `CONFLUENCE_COREJ2K`).

The harness reads `RegionServerDefaults.ini` beside it, a copy of `bin/OpenSimDefaults.ini` made when the harness is
built: rebuild the test projects after changing `[Jolt]` there. Harness baselines are
`ci/baselines/stock-1.1.0-win-x64` (develop's `stock-1.0.4-*` folders are for its native and are not kept), recorded from
`ci/runs.txt`; check with
`dotnet Tests/JoltPhysicsHarness/bin/Release/net10.0/JoltPhysicsHarness.dll --check-baseline <outroot> --baselines Tests/JoltPhysicsHarness/ci/baselines`
after one `--out <outroot>/<run>` per run. Re-recorded 2026-10-21 after the develop sync: two runs a minute apart give
the same rows (bar the timed `ray_us_per_cast`); against develop's win-x64 1.0.4 rows every run agrees up to the first
contact between bodies (the head-on crash meets at 2.727 s and 19.246 m/s in both) and differs after it, in the native
solver.

## Syncing a newer develop

For each file, three-way merge with the last sync as the base (normalise line endings first, or every line conflicts):

    git show 7c74cb6bed:Source/OpenSim.Region.PhysicsModules.Jolt/JoltScene.cs > base.cs
    git show tranquillity/develop:Source/OpenSim.Region.PhysicsModules.Jolt/JoltScene.cs > theirs.cs
    git merge-file OpenSim/Region/PhysicsModules/Jolt/JoltScene.cs base.cs theirs.cs

Then update the commit above. Check new files, whether they bumped JoltPhysicsSharp (keep Confluence's 1.1.0 lines in
`JoltNative.cs`: a merge that takes theirs makes every native test fail with JoltNativeException), copy their
`[Jolt]` section into `bin/OpenSimDefaults.ini`, rebuild the test projects, and re-record the harness baselines.
