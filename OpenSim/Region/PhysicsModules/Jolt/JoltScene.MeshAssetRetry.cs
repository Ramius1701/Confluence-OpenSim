/*
 * Confluence-only file, not part of the Legion Jolt sources.
 *
 * Re-cooks a sculpt or mesh prim once its asset arrives. When the region loads, many mesh and sculpt assets are not
 * in the cache yet, so the mesher returns nothing and CookPrimShape falls back to a bounding box; lane/jolt leaves it
 * a box for good ("Bounding box for now"). On Starbase Andromeda's first Jolt boot (2026-09-09) that was 5,160 prims
 * colliding as boxes. As BulletSim does (BSShapes.VerifyMeshCreated), this requests the asset once, stores its data
 * on the prim's shape, and re-cooks every live prim using that shape on the step thread before the next step.
 *
 * Hooks in the Legion files: CookMeshShape calls RequestMeshAsset where the mesher returns null for a sculpt/mesh
 * prim, and DrainPendingActivation calls DrainPendingMeshRebuild first.
 */

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.PhysicsModules.SharedBase;

namespace OpenSim.Region.PhysicsModules.Jolt
{
    public sealed partial class JoltScene
    {
        // The texture already requested for each shape, so a failed fetch is not retried in a loop; a prim edited to a
        // different sculpt texture gets a fresh request. Weak keys: a shape that leaves the scene is forgotten.
        private readonly ConditionalWeakTable<PrimitiveBaseShape, StrongBox<UUID>> _meshAssetRequested = new();

        // Shapes whose asset has arrived, waiting for the step thread (native shape and body changes are not safe from
        // the asset service's callback thread).
        private readonly HashSet<PrimitiveBaseShape> _pendingMeshRebuild = new(ReferenceEqualityComparer.Instance);

        internal void RequestMeshAsset(PrimitiveBaseShape pbs)
        {
            if (pbs == null || !pbs.SculptEntry || pbs.SculptTexture.IsZero())
                return;
            UUID texture = pbs.SculptTexture;
            lock (_meshAssetRequested)
            {
                if (_meshAssetRequested.TryGetValue(pbs, out StrongBox<UUID> asked) && asked.Value == texture)
                    return;
                _meshAssetRequested.AddOrUpdate(pbs, new StrongBox<UUID>(texture));
            }
            RequestAssetDelegate request = RequestAssetMethod;
            if (request == null)
            {
                m_log.LogWarning($"{LogHeader} no RequestAssetMethod wired; a sculpt/mesh prim stays a bounding box.");
                return;
            }
            // No log line per request or success: a region load can request thousands. DrainPendingMeshRebuild logs
            // one summary per drain.
            request(texture, asset =>
            {
                if (asset == null || asset.ID != texture.ToString())
                {
                    m_log.LogDebug($"{LogHeader} sculpt/mesh asset {texture} did not arrive; the prim stays a bounding box.");
                    return;
                }
                pbs.SculptData = asset.Data;
                lock (_pendingMeshRebuild)
                    _pendingMeshRebuild.Add(pbs);
            });
        }

        private void DrainPendingMeshRebuild()
        {
            HashSet<PrimitiveBaseShape> arrived;
            lock (_pendingMeshRebuild)
            {
                if (_pendingMeshRebuild.Count == 0)
                    return;
                arrived = new HashSet<PrimitiveBaseShape>(_pendingMeshRebuild, ReferenceEqualityComparer.Instance);
                _pendingMeshRebuild.Clear();
            }

            var prims = new List<JoltPrim>();
            lock (_prims)
                foreach (JoltPrim p in _prims.Values)
                    if (p.PrimShape != null && arrived.Contains(p.PrimShape))
                        prims.Add(p);

            int exceptions = 0;
            var byKind = new Dictionary<string, int>();
            foreach (JoltPrim p in prims)
            {
                try
                {
                    p.RebuildAfterAssetFetch();
                    byKind.TryGetValue(p.ShapeKind, out int n);
                    byKind[p.ShapeKind] = n + 1;
                }
                catch (Exception e)
                {
                    exceptions++;
                    m_log.LogError($"{LogHeader} mesh-asset rebuild EXCEPTION for prim {p.LocalID}: {e}");
                }
            }
            string kinds = string.Join(", ", byKind.Select(kv => $"{kv.Value} {kv.Key}"));
            m_log.LogInformation($"{LogHeader} mesh-asset drain: {arrived.Count} sculpt/mesh assets arrived, {prims.Count} prims re-cooked" +
                                 $"{(kinds.Length > 0 ? $" ({kinds})" : "")}, {exceptions} exceptions.");
        }
    }
}
