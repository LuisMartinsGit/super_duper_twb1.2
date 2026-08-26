using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Terrain;

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
        /// node-to-node hops each &lt;= this distance. It is a single ADJACENCY
        /// hop, not a patch radius — the flood covers a patch of any size, one
        /// neighbour at a time.
        ///
        /// 3 m covers both ways two nodes can be adjacent on the 2 m build grid
        /// (2.0 m orthogonal, 2.83 m diagonal) and nothing further. It was 12 m,
        /// sized for the old scattered layout where a patch's nodes could sit
        /// ~10 m apart; against gapless patches that hop was long enough to
        /// stride from one patch clean into the next, so the flood merged
        /// visibly separate patches and a click on one could send workers to
        /// the other — the reported "override that assigns workers to a
        /// different resource than the one selected".
        /// </summary>
        public const float PatchClusterRadius = 3f;

        private const int PresentationID = 301;

        // Reference amount at which a node reads "full" and fills its cell.
        private const float RefAmount = 80f;

        /// <summary>
        /// ECS scale at which the gem-cluster prefab exactly spans one 2 m
        /// build cell, given the presentation layer's own base scale.
        /// Build-grid rework: a node occupies one cell, so it is sized to that
        /// cell rather than growing without bound with its deposit.
        /// docs/Design/Build_Grid.md
        /// </summary>
        private const float CellFillScale =
            BuildGrid.CellSize / PresentationSpawnSystem.VeilstoneOutcroppingVisualBaseScale;

        /// <summary>How far a nearly-empty node shrinks inside its cell. Keeps
        /// the "this one is picked over" read without ever leaving the cell.
        /// </summary>
        private const float MinCellFraction = 0.7f;

        /// <summary>Visual scale applied to LocalTransform from the veilstone
        /// amount. Clamped so a node NEVER exceeds its single build cell — the
        /// old formula grew with the deposit and let big nodes overhang cells
        /// they did not block.</summary>
        public static float ComputeScale(int veilstoneAmount)
        {
            float a = math.max(1f, veilstoneAmount);
            float t = math.clamp(math.pow(a / RefAmount, 1f / 3f), MinCellFraction, 1f);
            return CellFillScale * t;
        }

        /// <summary>
        /// Sim radius. Fixed at half a build cell regardless of deposit size:
        /// the node blocks exactly its cell, so the circle the placement
        /// validator and obstacle avoidance test must match that cell, not the
        /// visual. The old value scaled with the deposit and bottomed out near
        /// 0.28 m — far smaller than the 3x3 m the nav field was blocking.
        /// </summary>
        public static float ComputeRadius(int veilstoneAmount) => BuildGrid.HalfCell;

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
            // One node, one build cell, snapped to its centre.
            // docs/Design/Build_Grid.md
            position = BuildGrid.SnapToCellCentre(position);

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

            // Block the cell on the legacy passability grid too. Veilstone was
            // the ONE node type that never did this — it carved the nav cost
            // field but stayed passable here, so placement validation and
            // steering both thought the ground was free.
            PassabilityGrid.Instance?.BlockObstacle(position, BuildGrid.HalfCell);

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, int veilstoneAmount)
        {
            // Same single-cell snap as the EntityManager path above.
            position = BuildGrid.SnapToCellCentre(position);

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

            // Mirror the EntityManager path's passability block. Safe to do
            // eagerly: the grid is keyed by world position, not by entity, so
            // it does not need to wait for ECB playback.
            PassabilityGrid.Instance?.BlockObstacle(position, BuildGrid.HalfCell);

            return entity;
        }
    }
}
