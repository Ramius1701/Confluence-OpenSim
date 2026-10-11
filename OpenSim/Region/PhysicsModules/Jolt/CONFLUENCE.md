# Jolt in Confluence: where it comes from and what differs

The Jolt physics module, backend and vehicle controller are JohnLegionH's work on Tranquillity, branch
`lane/jolt` (remote `tranquillity-johnlegionh`), imported at **`fbd824abeb`** (2026-10-11). Their files keep their
licence headers: most are MPL-2.0 (Legion Builds); the vehicle controller carries the InWorldz Halcyon notice.
The operator documentation is `doc/JoltPhysics.md` (theirs, unchanged).

| Here | lane/jolt |
|---|---|
| `OpenSim/Region/PhysicsModules/Jolt` | `Source/OpenSim.Region.PhysicsModules.Jolt` |
| `OpenSim/Region/PhysicsModules/Jolt.Backend` | `Source/OpenSim.Region.PhysicsModules.Jolt.Backend` |
| `OpenSim/Region/PhysicsModules/Jolt.Vehicles` | `Source/OpenSim.Region.PhysicsModules.Jolt.Vehicles` |
| `bin/OpenSimDefaults.ini` `[Jolt]` | `Source/OpenSim.Server.RegionServer/AppData/OpenSimDefaults.ini` `[Jolt]` |

## Confluence changes

Confluence-only files:
- `Jolt/ConfluenceRegistration.cs`: registers the module with Mono.Addins and forwards to `JoltModule` (replaces their
  `PluginRegistration.cs`, which is kept but not compiled).
- `Jolt/ConfluenceLogging.cs`: a `LoggerProvider` in the module's namespace that sends their `ILogger` calls to log4net.
- `Jolt/JoltScene.MeshAssetRetry.cs`, `Jolt/JoltPrim.MeshAssetRetry.cs`: fetch an unfetched sculpt/mesh asset and
  re-cook the prim (lane/jolt leaves it a bounding box).
- The three `.csproj` files (hand-maintained, outside `OpenSim.sln`; their projects rely on Tranquillity's
  `Directory.Build.props`).

Edits inside their files, each marked `[Confluence: ...]`:
- `Jolt.Backend/JoltPhysicsBackend.cs`: JoltPhysicsSharp 2.23.0 API (impulses as a float span; character contacts as
  one `CharacterContact`); `PushCharacterCharacterContact` takes a nullable character.
- `Jolt.Backend/JoltNative.cs`: `PackageVersion` 1.1.0 and the 1.1.0 stock hashes; their patched builds (made from
  1.0.4's joltc) are not listed.
- `Jolt/JoltScene.cs`: two one-line hooks for the mesh-asset retry.

Confluence runs JoltPhysicsSharp **2.23.0** with JoltPhysics.Native **1.1.0** (Jolt Physics v5.6.0). lane/jolt pins
2.19.1 / 1.0.4 for its patched joltc, which only matters when several regions step in one process; Confluence runs one
region per process.

Core support from Tranquillity #270 (`1162fc6dda`) is ported separately: `PhysicsActor.SetConstantForce`,
`ScenePresence.SetConstantForce`, the `AgentData` constant-force fields, and `llSetForce` from an attachment in
`LSL_Api.cs`.

## Syncing a newer lane/jolt

For each file, three-way merge with the import as the base:

    git show fbd824abeb:Source/OpenSim.Region.PhysicsModules.Jolt/JoltScene.cs > base.cs
    git show tranquillity-johnlegionh/lane/jolt:Source/OpenSim.Region.PhysicsModules.Jolt/JoltScene.cs > theirs.cs
    git merge-file OpenSim/Region/PhysicsModules/Jolt/JoltScene.cs base.cs theirs.cs

Then update the commit above. Check new files, and whether they bumped JoltPhysicsSharp (the hashes in `JoltNative.cs`).
