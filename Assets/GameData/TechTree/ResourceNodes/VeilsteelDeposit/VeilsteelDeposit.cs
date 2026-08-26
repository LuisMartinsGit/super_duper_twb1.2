// VeilsteelDeposit.cs
// Factory for the Veilsteel map resource node — the "Veilsteel Mine".
//
// Co-located with the rest of the node's code per the TechTree convention
// (CLAUDE.md: the factory, the entity's components, its single-entity systems
// and visuals all live in the entity's folder). This mirrors
// VeilstoneOutcropping.cs; before it existed, the creation code was private
// inside VeilsteelDepositBootstrap, so the node was the one resource type with
// no callable factory — nothing outside the bootstrap could spawn one.
//
// Unlike veilstone (patches of many nodes) a veilsteel deposit is a SINGLE
// node per marker holding the marker's full amount (design default 1500) —
// docs/Design/Overview.md. Mining behaviour is iron's: the node carries
// VeilsteelDepositTag + the shared IronDepositState, and MiningSystem credits
// Veilsteel instead of Iron off MinerState.GatheringResource == 2.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static class VeilsteelDeposit
    {
        /// <summary>Presentation id — must match PresentationSpawnSystem's
        /// dispatch table.</summary>
        public const int PresentationID = 403;

        /// <summary>Sharp-crystals payload when no marker authored one.</summary>
        public const int DefaultAmount = 1500;

        /// <summary>
        /// ECS scale at which the shared gem-cluster prefab spans exactly one
        /// 2 m build cell. Left at scale 1 the prefab rendered at its full x6
        /// base scale — roughly three times the ground the node occupies — and
        /// because the click collider is fitted to the node's CELL, right-click
        /// snapped to it from several metres outside. docs/Design/Build_Grid.md
        /// </summary>
        public static float ComputeScale()
            => BuildGrid.CellSize / PresentationSpawnSystem.VeilsteelDepositVisualBaseScale;

        /// <summary>Sim radius: half a build cell. The node blocks exactly its
        /// own cell, so the circle the placement validator and obstacle
        /// avoidance test must be that cell — not the visual.</summary>
        public static float ComputeRadius() => BuildGrid.HalfCell;

        public static Entity Create(EntityManager em, float3 position, int amount)
        {
            // One deposit, one build cell, snapped to its centre.
            // docs/Design/Build_Grid.md
            position = BuildGrid.SnapToCellCentre(position);

            var entity = em.CreateEntity(
                typeof(VeilsteelDepositTag),
                // ObstacleTag — units route around the node on the passability
                // grid, same treatment as iron and veilstone.
                typeof(ObstacleTag),
                typeof(IronDepositState),   // shared deposit state — see VeilsteelDepositTag docs
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, ComputeScale()));
            em.SetComponentData(entity, new Radius { Value = ComputeRadius() });
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, new IronDepositState
            {
                RemainingIron = amount,
                InitialIron = amount,
                Depleted = 0
            });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            PassabilityGrid.Instance?.BlockObstacle(position, BuildGrid.HalfCell);

            return entity;
        }

        /// <summary>ECB overload, for parity with the other node factories —
        /// spawning from inside a system that cannot make structural changes
        /// directly.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, int amount)
        {
            position = BuildGrid.SnapToCellCentre(position);

            var entity = ecb.CreateEntity();
            ecb.AddComponent<VeilsteelDepositTag>(entity);
            ecb.AddComponent<ObstacleTag>(entity);
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, ComputeScale()));
            ecb.AddComponent(entity, new Radius { Value = ComputeRadius() });
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, new IronDepositState
            {
                RemainingIron = amount,
                InitialIron = amount,
                Depleted = 0
            });
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            PassabilityGrid.Instance?.BlockObstacle(position, BuildGrid.HalfCell);

            return entity;
        }
    }
}
