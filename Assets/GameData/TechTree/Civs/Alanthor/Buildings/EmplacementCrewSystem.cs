// EmplacementCrewSystem.cs
// Keeps an emplacement's ENGINE and its PLATFORM in step
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
//
// Four rules, and nothing else:
//   1. a finished platform that has never raised an engine raises one at
//      once — the first engine comes with the mount;
//   2. an engine that dies leaves the platform EMPTY. There is no free
//      rebuild (removed 2026-09-25): the owner pays for a Replace Equipment
//      order (CommandRouter.ReplaceEquipmentDirect), which starts
//      EmplacementCrew.Restore;
//   3. a running restore counts down and raises the engine when it ends;
//   4. an engine whose platform is gone dies with it — the position is what
//      was killed, not just the machine standing on it.
//
// Set-level: both emplacements use it, so it sits one level above their
// folders. Structural work (spawning / destroying) runs after the query
// loops close, never inside them.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct EmplacementCrewSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EmplacementCrew>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // ── Platforms: raise the first engine, run paid restores ─────
            var toRaise = new List<Entity>();
            foreach (var (crew, entity) in SystemAPI
                         .Query<RefRW<EmplacementCrew>>()
                         .WithAll<EmplacementTag>()
                         .WithNone<UnderConstruction>()
                         .WithEntityAccess())
            {
                ref var c = ref crew.ValueRW;

                if (c.Engine != Entity.Null && !em.Exists(c.Engine))
                {
                    // The engine was killed off the platform: it stays empty
                    // until its owner pays to replace the equipment.
                    c.Engine = Entity.Null;
                }
                if (c.Engine != Entity.Null) continue;

                if (c.Raised == 0)
                {
                    // The mount's own engine — raised the moment it lands.
                    toRaise.Add(entity);
                    continue;
                }

                if (c.Restore > 0f)
                {
                    c.Restore -= dt;
                    if (c.Restore > 0f) continue;
                    c.Restore = 0f;
                    toRaise.Add(entity);
                }
                // Otherwise: empty, waiting for Replace Equipment.
            }

            // ── Engines whose platform is gone ───────────────────────────
            var orphans = new List<Entity>();
            foreach (var (on, entity) in SystemAPI
                         .Query<RefRO<EmplacedOn>>()
                         .WithAll<EmplacedEngineTag>()
                         .WithEntityAccess())
            {
                var platform = on.ValueRO.Emplacement;
                if (platform == Entity.Null || !em.Exists(platform))
                    orphans.Add(entity);
            }

            for (int i = 0; i < orphans.Count; i++)
                if (em.Exists(orphans[i])) em.DestroyEntity(orphans[i]);

            for (int i = 0; i < toRaise.Count; i++)
            {
                var platform = toRaise[i];
                if (!em.Exists(platform) || !em.HasComponent<EmplacementCrew>(platform)) continue;
                var c = em.GetComponentData<EmplacementCrew>(platform);
                if (c.Engine != Entity.Null && em.Exists(c.Engine)) continue;

                var faction = em.HasComponent<FactionTag>(platform)
                    ? em.GetComponentData<FactionTag>(platform).Value : Faction.Blue;
                float3 at = em.GetComponentData<LocalTransform>(platform).Position;
                at.y += c.MountHeight;

                Entity engine = Raise(em, c.EngineId.ToString(), at, faction);
                if (engine == Entity.Null) continue;

                em.AddComponentData(engine, new EmplacedOn { Emplacement = platform });
                c.Engine = engine;
                c.Raised = 1;
                c.Restore = 0f;
                c.RestoreTime = 0f;
                em.SetComponentData(platform, c);
            }
        }

        /// <summary>The two engines are spawned here and nowhere else — they
        /// are never trained, so they are deliberately absent from
        /// UnitFactory's recipe table.</summary>
        private static Entity Raise(EntityManager em, string engineId, float3 at, Faction faction)
        {
            switch (engineId)
            {
                case EmplacedBallista.Id:  return EmplacedBallista.Create(em, at, faction);
                case EmplacedTrebuchet.Id: return EmplacedTrebuchet.Create(em, at, faction);
                default: return Entity.Null;
            }
        }
    }
}
