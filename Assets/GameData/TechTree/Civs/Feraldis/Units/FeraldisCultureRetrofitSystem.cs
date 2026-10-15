// Turns shared Age 0 units into their Feraldis forms.
// Canon: docs/Design/Age_1_Feraldis.md.
//
// Workers and Scouts are trained from the SHARED Hall recipe — one factory,
// no culture parameter — so their Feraldis differences cannot live in the
// factory. This sweep applies them at runtime instead, and it is idempotent
// and CONTINUOUS on purpose: it must catch units that existed at age-up,
// units trained afterwards, and units restored from a save. (The same
// lesson the Raider Camp tagging learned the hard way — a one-shot pass at
// the age-up moment silently misses everything built later.)
//
//   Worker → SLAVE (2026-10-12, docs/Design/Age_1_Feraldis.md §2-3). It
//            stays a builder (WorkerTag / CanBuild kept) and gains SlaveTag:
//            it may staff an extractor or the Great Forge, feed the
//            Soulforge and be sacrificed. It leaves the army population — the
//            slave cap (Slave Pens) bounds it instead.
//   Scout  → loses the huge scout-sight (and its settle ramp) and gains an
//            EAGLE that circles it carrying its own vision.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using TheWaningBorder.Economy;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Entities;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisCultureRetrofitSystem : SystemBase
    {

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_FeraldisUnitTagUnitTagFactionTag =
        {
            ComponentType.ReadOnly<FeraldisUnitTag>(),
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_FeraldisUnitTagUnitTagFactionTag;

        #endregion
        private EntityQuery _workerQuery;
        private EntityQuery _scoutQuery;
        /// <summary>SimCadence, not a bare countdown — see SimCadence.cs. The
        /// sweep adds culture tags that change how a unit behaves, so two peers
        /// retrofitting on different ticks give the same unit different rules
        /// for as long as the offset lasts.</summary>
        private SimCadence.Periodic _cadence;

        /// <summary>Seconds between sweeps. Nothing here is urgent to the
        /// frame, and the queries touch every worker and scout in the world.</summary>
        private const float ScanInterval = 1f;

        protected override void OnCreate()
        {
            _workerQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CanBuild, FactionTag, WorkerTag>()
                .WithNone<SlaveTag>()
                .Build(this);

            _scoutQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ScoutSightState, FactionTag, LocalTransform>()
                .WithNone<FeraldisScoutTag>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, ScanInterval)) return;

            var em = EntityManager;
            RetrofitWorkers(em);
            RetrofitScouts(em);
        }


        private void RetrofitWorkers(EntityManager em)
        {
            if (_workerQuery.IsEmpty) return;
            using var ents = _workerQuery.ToEntityArray(Allocator.Temp);
            using var facs = _workerQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var targets = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (CultureConfig.GetCompletedCulture(em, facs[i].Value) == Cultures.Feraldis)
                    targets.Add(ents[i]);

            for (int i = 0; i < targets.Length; i++)
            {
                var e = targets[i];
                // The Worker becomes a Slave in place: same entity, same HP
                // fraction, still a builder. Its id and name follow, so tech
                // effects ("unit:Feraldis_Slave"), the roster and the HUD all
                // see a Slave.
                em.AddComponent<SlaveTag>(e);
                if (!em.HasComponent<FeraldisWorkerTag>(e)) em.AddComponent<FeraldisWorkerTag>(e);
                if (!em.HasComponent<FeraldisUnitTag>(e)) em.AddComponent<FeraldisUnitTag>(e);
                var id = new UnitTypeId { Value = TheWaningBorder.Feraldis.FeraldisEconomy.SlaveId };
                if (em.HasComponent<UnitTypeId>(e)) em.SetComponentData(e, id); else em.AddComponentData(e, id);
                var name = new DisplayName { Value = "Slave" };
                if (em.HasComponent<DisplayName>(e)) em.SetComponentData(e, name); else em.AddComponentData(e, name);
                // Slaves are bounded by the slave cap, not the army population.
                if (em.HasComponent<PopulationCost>(e))
                    em.SetComponentData(e, new PopulationCost { Amount = 0 });
                TheWaningBorder.Entities.FeraldisSlave.GrantFieldSacrifice(em, e);
            }
            targets.Dispose();
        }

        private void RetrofitScouts(EntityManager em)
        {
            if (_scoutQuery.IsEmpty) return;
            using var ents = _scoutQuery.ToEntityArray(Allocator.Temp);
            using var facs = _scoutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _scoutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var targets = new NativeList<Entity>(Allocator.Temp);
            var spots = new NativeList<Unity.Mathematics.float3>(Allocator.Temp);
            var owners = new NativeList<Faction>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (CultureConfig.GetCompletedCulture(em, facs[i].Value) != Cultures.Feraldis)
                    continue;
                targets.Add(ents[i]);
                spots.Add(xfs[i].Position);
                owners.Add(facs[i].Value);
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var e = targets[i];

                // Strip the scout-sight ramp — AbilityAuraSystem drives LOS
                // off ScoutSightState, so removing it is what actually ends
                // the bonus. Then clamp the base sight down to ordinary.
                em.RemoveComponent<ScoutSightState>(e);
                if (em.HasComponent<LineOfSight>(e))
                    em.SetComponentData(e, new LineOfSight { Radius = FeraldisScoutLos });

                em.AddComponent<FeraldisScoutTag>(e);
                em.AddComponent<FeraldisUnitTag>(e);

                if (!em.HasComponent<HasEagle>(e))
                {
                    var pos = spots[i];
                    pos.y += EagleHeight;
                    var bird = Eagle.Create(em, pos, owners[i], e);
                    TransientState.PreAddUnitSet(em, bird);
                    em.AddComponentData(e, new HasEagle { Eagle = bird });
                }
            }

            targets.Dispose();
            spots.Dispose();
            owners.Dispose();
        }
    }
}
