// IconoclastAuraSystem.cs
// Spec refinement (Phase 2): Crystal nodes are UNTARGETABLE by default —
// nothing can pick them as an attack target. A Feraldis Iconoclast within
// IconoclastAuraRadius of a node strips the NodeUntargetable tag for that
// frame, exposing the node to normal combat damage. When no Iconoclast is
// in range, the tag is restored.
//
// This replaces NodeInvulnerabilitySystem's per-frame HP refund (which made
// the AI attack forever because targeting still succeeded). The Iconoclast
// is now an enabler unit — it does NOT attack the node itself; surrounding
// regular units do the damage.
//
// Location: Assets/Scripts/Systems/Crystal/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Crystal
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetingSystem))]
    public partial class IconoclastAuraSystem : SystemBase
    {
        private EntityQuery _iconoclastQuery;
        private EntityQuery _nodeQuery;

        protected override void OnCreate()
        {
            _iconoclastQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<IconoclastTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());

            _nodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Snapshot live Iconoclasts.
            using var icoEnts = _iconoclastQuery.ToEntityArray(Allocator.Temp);
            using var icoTransforms = _iconoclastQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var icoHealths = _iconoclastQuery.ToComponentDataArray<Health>(Allocator.Temp);

            using var nodeEnts = _nodeQuery.ToEntityArray(Allocator.Temp);
            using var nodeTransforms = _nodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int n = 0; n < nodeEnts.Length; n++)
            {
                Entity node = nodeEnts[n];

                // Dormant (Destroyed) nodes don't need un-targetability — they're
                // already at 0 HP and excluded by DeathSystem-skip patterns.
                if (em.HasComponent<NodeDormant>(node))
                {
                    if (em.HasComponent<NodeUntargetable>(node))
                        ecb.RemoveComponent<NodeUntargetable>(node);
                    continue;
                }

                var nodePos = nodeTransforms[n].Position;

                bool iconoclastNearby = false;
                for (int i = 0; i < icoEnts.Length; i++)
                {
                    if (icoHealths[i].Value <= 0) continue;
                    var ip = icoTransforms[i].Position;
                    float dxz = math.distance(
                        new float2(ip.x, ip.z),
                        new float2(nodePos.x, nodePos.z));
                    if (dxz <= IconoclastAuraRadius)
                    {
                        iconoclastNearby = true;
                        break;
                    }
                }

                bool hasTag = em.HasComponent<NodeUntargetable>(node);
                if (iconoclastNearby && hasTag)
                {
                    ecb.RemoveComponent<NodeUntargetable>(node);
                }
                else if (!iconoclastNearby && !hasTag)
                {
                    ecb.AddComponent<NodeUntargetable>(node);
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
