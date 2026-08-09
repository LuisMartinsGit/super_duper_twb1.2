// File: Assets/GameData/TechTree/Buildings/Feraldis/RaiderCamp/RaiderCampSystem.cs
// Raider Camps produce Plunderers. Canon: docs/Design/Age_1_Feraldis.md.
//
// One Plunderer every CampSpawnInterval seconds, up to CampPlundererCap
// ALIVE per camp. The cap is per-camp rather than per-faction so that
// building more camps is the way to scale the Feraldis economy — which is
// the whole build-order pressure of the culture.
//
// Live raiders are counted by their PlundererOrigin back-reference. A camp
// whose raiders die refills immediately; a camp at cap idles its timer, so
// the first replacement arrives a full interval after a death rather than
// instantly.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RaiderCampSystem : SystemBase
    {
        private EntityQuery _plundererQuery;
        private EntityQuery _hutQuery;

        protected override void OnCreate()
        {
            // Gated on huts, NOT on RaiderCampTag: this system also has to
            // CREATE the first camp tag, and a Feraldis player who lost every
            // camp must be able to rebuild.
            RequireForUpdate<GathererHutTag>();
            // Corpses must NOT hold a slot: DeathSystem keeps a dead unit
            // around for a 2 s death animation, so counting them pinned the
            // camp at cap and stretched the real refill from 5 s to 7 s.
            _plundererQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlundererOrigin, PlundererTag>()
                .WithNone<DeathAnimationState>()
                .Build(this);
            _hutQuery = GetEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<FactionTag>());
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            TagNewHuts(em);

            // Live-raider tally per camp, rebuilt once per frame.
            using var origins = _plundererQuery.ToComponentDataArray<PlundererOrigin>(Allocator.Temp);
            var liveByCamp = new NativeHashMap<Entity, int>(64, Allocator.Temp);
            for (int i = 0; i < origins.Length; i++)
            {
                var c = origins[i].Camp;
                if (c == Entity.Null) continue;
                liveByCamp[c] = liveByCamp.TryGetValue(c, out int n) ? n + 1 : 1;
            }

            var spawnAt = new NativeList<float3>(Allocator.Temp);
            var spawnFor = new NativeList<Faction>(Allocator.Temp);
            var spawnCamp = new NativeList<Entity>(Allocator.Temp);

            foreach (var (camp, transform, faction, entity) in SystemAPI
                .Query<RefRW<RaiderCampTag>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                int live = liveByCamp.TryGetValue(entity, out int n) ? n : 0;
                if (live >= CampPlundererCap)
                {
                    // At cap: hold the timer primed so a death is answered
                    // one full interval later, not instantly.
                    camp.ValueRW.SpawnTimer = CampSpawnInterval;
                    continue;
                }

                camp.ValueRW.SpawnTimer -= dt;
                if (camp.ValueRO.SpawnTimer > 0f) continue;
                camp.ValueRW.SpawnTimer = CampSpawnInterval;

                var p = transform.ValueRO.Position;
                // Deterministic ring offset from the camp's own entity index —
                // no RNG, so lockstep stays in sync.
                float ang = (entity.Index * 37 + live * 71) % 360 * math.PI / 180f;
                float sx = p.x + math.cos(ang) * 3f;
                float sz = p.z + math.sin(ang) * 3f;

                spawnAt.Add(new float3(sx, TerrainUtility.GetHeight(sx, sz), sz));
                spawnFor.Add(faction.ValueRO.Value);
                spawnCamp.Add(entity);
            }

            // Post-loop: entity creation is a structural change.
            for (int i = 0; i < spawnAt.Length; i++)
            {
                var e = Plunderer.Create(em, spawnAt[i], spawnFor[i]);
                em.AddComponentData(e, new PlundererOrigin { Camp = spawnCamp[i] });
            }

            spawnAt.Dispose();
            spawnFor.Dispose();
            spawnCamp.Dispose();
            liveByCamp.Dispose();
        }

        /// <summary>
        /// Any Gatherer's Hut owned by a completed-Feraldis faction becomes a
        /// Raider Camp. Idempotent and continuous ON PURPOSE.
        ///
        /// AgeUpSystem tags the huts standing at the moment of the culture
        /// transition, but that alone left a hole big enough to break the
        /// culture: a hut built one second AFTER age-up stayed an ordinary
        /// Gatherer's Hut, so a Feraldis player got full gathering income,
        /// Survey drips and the influence doubling stacked on top of the
        /// Plunderer economy. Sweeping here closes that, and also self-heals
        /// across save/load and any future spawn path.
        /// </summary>
        private void TagNewHuts(EntityManager em)
        {
            using var ents = _hutQuery.ToEntityArray(Allocator.Temp);
            using var facs = _hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.HasComponent<RaiderCampTag>(e)) continue;
                if (CultureConfig.GetCompletedCulture(em, facs[i].Value) != Cultures.Feraldis)
                    continue;

                em.AddComponentData(e, new RaiderCampTag { SpawnTimer = CampSpawnInterval });
            }
        }
    }
}
