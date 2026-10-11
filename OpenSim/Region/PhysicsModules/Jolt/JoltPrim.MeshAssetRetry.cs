/*
 * Confluence-only file, not part of the Legion Jolt sources. The prim side of JoltScene.MeshAssetRetry.cs.
 */

using OpenSim.Framework;

namespace OpenSim.Region.PhysicsModules.Jolt
{
    internal sealed partial class JoltPrim
    {
        /// <summary>The shape this prim was cooked from; the mesh-asset retry matches arrived assets to prims by it.</summary>
        internal PrimitiveBaseShape PrimShape => _pbs;

        /// <summary>Re-cook after the sculpt/mesh asset arrived (step thread, from JoltScene.DrainPendingMeshRebuild).</summary>
        internal void RebuildAfterAssetFetch() => Rebuild();
    }
}
