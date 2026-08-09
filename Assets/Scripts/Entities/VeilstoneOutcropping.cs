// File: Assets/Scripts/Entities/VeilstoneOutcropping.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstone node — static mineable entity that yields veilstone. Spawned when a
    /// veilstone creature dies or when starter patches are seeded at game start.
    /// Visual radius and LocalTransform.Scale are derived from the veilstone amount
    /// (cube-root scaling so resource volume tracks the value).
    ///
    /// VeilstoneOutcroppings placed within <see cref="MergeRadius"/> of an existing non-depleted
    /// outcropping merge into one carrying the summed veilstone value (see
    /// <see cref="CreateOrMerge"/>) — prevents flicker-spam when an entity's
    /// death animation lingers and keeps the gathered field tidy.
    /// </summary>
    public static class VeilstoneOutcropping
    {
        public const int DefaultVeilstone = 300;
        public const float MergeRadius = 4f;

        /// <summary>
        /// Two outcroppings belong to the same patch if reachable by a chain of
        /// node-to-node hops each &lt;= this distance. Sized to cluster all nodes
        /// inside a scattered patch (5 m spread, max pairwise ~10 m) while
        /// leaving room so adjacent patches stay separate. Used by
        /// GatheringSystem's closest-node-in-patch retarget.
        /// </summary>
        public const float PatchClusterRadius = 12f;

        private const int PresentationID = 301;

        // Reference: 80 veilstone → scale 1.0 / radius 0.6 (matches starter patch).
        private const float RefAmount = 80f;
        private const float BaseRadius = 0.6f;
        private const float MinScale = 0.6f;
        private const float MaxScale = 4f;

        /// <summary>Visual scale applied to LocalTransform from the veilstone amount.</summary>
        /// <summary>Global visual shrink (2026-08-03): the crystal prefab
        /// read far too large raw — nodes render at 0.35 of it. (A brief
        /// 0.07 "20 %" experiment same day made them unreadably small and
        /// was reverted; the size that actually needed cutting was the WELL
        /// landmark, now BaseScale 8.)</summary>
        public const float SizeMult = 0.35f;

        public static float ComputeScale(int veilstoneAmount)
        {
            float a = math.max(1f, veilstoneAmount);
            return SizeMult * math.clamp(math.pow(a / RefAmount, 1f / 3f), MinScale, MaxScale);
        }

        /// <summary>Collider/selection radius from the veilstone amount.</summary>
        public static float ComputeRadius(int veilstoneAmount) => BaseRadius * ComputeScale(veilstoneAmount);

        /// <summary>
        /// Create a outcropping, or merge into an existing non-depleted outcropping within
        /// <see cref="MergeRadius"/>. On merge the existing outcropping is destroyed and
        /// a new one is created at its position carrying the summed veilstone value
        /// (and the recomputed visual scale). Returns Entity.Null if the cap is hit
        /// for new creations; merges always proceed since they don't grow node count.
        /// </summary>
        public static Entity CreateOrMerge(EntityManager em, float3 position, int veilstoneAmount, int maxNodes = int.MaxValue)
        {
            if (veilstoneAmount <= 0) return Entity.Null;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<VeilstoneOutcroppingState>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var states = query.ToComponentDataArray<VeilstoneOutcroppingState>(Allocator.Temp);

            float mergeSqr = MergeRadius * MergeRadius;
            for (int i = 0; i < entities.Length; i++)
            {
                if (states[i].Depleted != 0) continue;
                float2 a = new float2(transforms[i].Position.x, transforms[i].Position.z);
                float2 b = new float2(position.x, position.z);
                if (math.distancesq(a, b) <= mergeSqr)
                {
                    int totalVeilstone = states[i].RemainingVeilstone + veilstoneAmount;
                    float3 mergedPos = transforms[i].Position;
                    em.DestroyEntity(entities[i]);
                    return Create(em, mergedPos, totalVeilstone);
                }
            }

            if (entities.Length >= maxNodes) return Entity.Null;
            return Create(em, position, veilstoneAmount);
        }

        public static Entity Create(EntityManager em, float3 position) => Create(em, position, DefaultVeilstone);

        public static Entity Create(EntityCommandBuffer ecb, float3 position) => Create(ecb, position, DefaultVeilstone);

        public static Entity Create(EntityManager em, float3 position, int veilstoneAmount)
        {
            float scale = ComputeScale(veilstoneAmount);
            float radius = ComputeRadius(veilstoneAmount);

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(VeilstoneOutcroppingTag),
                typeof(VeilstoneOutcroppingState),
                typeof(Radius),
                // ObstacleTag so NavMeshManager carves the outcropping into the
                // navmesh — without this, workers walk *through* veilstone
                // patches in straight lines (iron deposits have always
                // carved). Matches the iron-deposit treatment in
                // IronDepositBootstrap.CreateIronDepositEntity. The
                // corridor-exhausted guard in MovementSystem stops workers
                // from direct-lining into the cluster centre once carved.
                typeof(ObstacleTag)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, scale));
            em.SetComponentData(entity, new VeilstoneOutcroppingState
            {
                RemainingVeilstone = veilstoneAmount,
                MaxVeilstone = veilstoneAmount,
                Depleted = 0
            });
            em.SetComponentData(entity, new Radius { Value = radius });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, int veilstoneAmount)
        {
            float scale = ComputeScale(veilstoneAmount);
            float radius = ComputeRadius(veilstoneAmount);

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, scale));
            ecb.AddComponent<VeilstoneOutcroppingTag>(entity);
            ecb.AddComponent(entity, new VeilstoneOutcroppingState
            {
                RemainingVeilstone = veilstoneAmount,
                MaxVeilstone = veilstoneAmount,
                Depleted = 0
            });
            ecb.AddComponent(entity, new Radius { Value = radius });
            // Mirror the EntityManager path above — see comment there for
            // why outcroppings carry ObstacleTag.
            ecb.AddComponent<ObstacleTag>(entity);

            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }
    }
}
