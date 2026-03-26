// File: Assets/Scripts/Systems/Creatures/CursedGroundRecessionSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// When a crystal node (main or sub) is destroyed, its cursed ground tiles
    /// begin receding. Tiles are removed gradually over 60 seconds, with random
    /// staggering so the curse visually dissolves rather than vanishing at once.
    ///
    /// Phase 1: Detect orphaned tiles (OwnerNode no longer exists) and tag them
    ///          with CursedGroundReceding.
    /// Phase 2: Count down receding tiles. When a tile's timer expires, unpaint
    ///          the curse texture from the terrain and destroy the entity.
    ///
    /// Uses SystemBase (managed) because terrain unpainting requires access to
    /// the managed ProceduralTerrain singleton.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CrystalSpreadSystem))]
    public partial class CursedGroundRecessionSystem : SystemBase
    {
        /// <summary>Total time for all cursed ground to fully recede (seconds).</summary>
        private const float RecessionDuration = 60f;

        protected override void OnCreate()
        {
            RequireForUpdate<CursedGroundTag>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // --- Phase 1: Detect orphaned cursed ground tiles ---
            // If OwnerNode entity no longer exists, the parent node was destroyed.
            // Tag these tiles for recession with random stagger over RecessionDuration.
            foreach (var (ownerNode, transform, entity) in SystemAPI
                .Query<RefRO<OwnerNode>, RefRO<LocalTransform>>()
                .WithAll<CursedGroundTag>()
                .WithNone<CursedGroundReceding>()
                .WithEntityAccess())
            {
                Entity owner = ownerNode.ValueRO.Value;

                // Owner still alive — skip
                if (owner != Entity.Null && em.Exists(owner) && em.HasComponent<CrystalNode>(owner))
                    continue;

                // Owner is dead — this tile is orphaned.
                // Stagger destruction across the full recession duration using a
                // deterministic hash so tiles don't all vanish in the same frame.
                uint hash = (uint)entity.Index;
                hash ^= hash >> 13;
                hash *= 0x5bd1e995;
                hash ^= hash >> 15;
                float randomFactor = (hash & 0xFFFF) / 65535f; // 0..1

                float timeRemaining = randomFactor * RecessionDuration;

                ecb.AddComponent(entity, new CursedGroundReceding
                {
                    TimeRemaining = timeRemaining
                });
            }

            // --- Phase 2: Count down receding tiles and destroy when expired ---
            foreach (var (receding, transform, radius, entity) in SystemAPI
                .Query<RefRW<CursedGroundReceding>, RefRO<LocalTransform>, RefRO<Radius>>()
                .WithAll<CursedGroundTag>()
                .WithEntityAccess())
            {
                receding.ValueRW.TimeRemaining -= dt;

                if (receding.ValueRO.TimeRemaining <= 0f)
                {
                    // Unpaint the curse texture from terrain before destroying
                    var pos = transform.ValueRO.Position;
                    float r = radius.ValueRO.Value;
                    if (ProceduralTerrain.Instance != null)
                    {
                        ProceduralTerrain.Instance.UnpaintCursedGround(pos.x, pos.z, r);
                    }

                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
