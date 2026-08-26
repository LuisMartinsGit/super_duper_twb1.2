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
//   Worker → BUILD ONLY, but a real fighter. Feraldis has no use for a
//            gatherer: its income is what Plunderers steal, and ore comes
//            from Mines that need no workers. So the mining half is stripped
//            and the combat half is made to actually mean something.
//   Scout  → loses the huge scout-sight (and its settle ramp) and gains an
//            EAGLE that circles it carrying its own vision.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Entities;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisCultureRetrofitSystem : SystemBase
    {
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
                .WithAll<CanBuild, FactionTag, MinerTag>()
                .WithNone<FeraldisWorkerTag>()
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
            StampPlunderPurses(em);
        }

        /// <summary>
        /// EVERY FERALDIS WARRIOR PLUNDERS (canon 2026-08-07). The purse is
        /// the single switch FeraldisPlunderSystem reads, so stamping it here
        /// enlists the whole army into the raid economy — the compensation for
        /// cutting raider throughput from 22 s to 60 s.
        ///
        /// Combat classes only: Workers already build and fight, and paying
        /// them to raid as well would just rebuild the free-body problem this
        /// rebalance is trying to remove.
        /// </summary>
        private void StampPlunderPurses(EntityManager em)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FeraldisUnitTag>(),
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var targets = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<PlunderPurse>(ents[i])) continue;
                var c = tags[i].Class;
                if (c != UnitClass.Melee && c != UnitClass.Ranged && c != UnitClass.Siege) continue;
                if (em.HasComponent<CanBuild>(ents[i])) continue;   // workers stay builders
                if (CultureConfig.GetCompletedCulture(em, facs[i].Value) != Cultures.Feraldis) continue;
                targets.Add(ents[i]);
            }

            for (int i = 0; i < targets.Length; i++)
                em.AddComponentData(targets[i], new PlunderPurse());

            targets.Dispose();
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

                // Build only — drop the gatherer half outright.
                if (em.HasComponent<MinerState>(e)) em.RemoveComponent<MinerState>(e);
                em.RemoveComponent<MinerTag>(e);

                // ...and make the fighter half real. Ordered onto an enemy a
                // Feraldis worker is light infantry, not a victim. It still
                // does NOT auto-acquire (TargetingSystem excludes CanBuild),
                // which is deliberate — builders that wandered off to fight
                // on their own would be a disaster.
                if (em.HasComponent<Health>(e))
                {
                    var h = em.GetComponentData<Health>(e);
                    if (h.Max < FeraldisWorkerHP)
                    {
                        int missing = h.Max - h.Value;
                        h.Max = FeraldisWorkerHP;
                        h.Value = h.Max - missing;
                        em.SetComponentData(e, h);
                    }
                }
                if (em.HasComponent<Damage>(e))
                    em.SetComponentData(e, new Damage { Value = FeraldisWorkerDamage });
                if (!em.HasComponent<AttackCooldown>(e))
                    em.AddComponentData(e, new AttackCooldown
                    {
                        Cooldown = FeraldisWorkerAttackCooldown, Timer = 0f
                    });
                if (!em.HasComponent<Target>(e))
                    em.AddComponentData(e, new Target { Value = Entity.Null });
                if (!em.HasComponent<Defense>(e))
                    em.AddComponentData(e, new Defense { Melee = 1, Ranged = 0, Siege = 0, Magic = 0 });

                // ...and make them fight like soldiers. Dropping
                // PassiveWorkerTag is what actually enlists them: it is the
                // component TargetingSystem's auto-acquire and
                // return-to-guard passes exclude. A Feraldis Worker holds
                // ground and engages like any other unit, and still builds.
                if (em.HasComponent<PassiveWorkerTag>(e))
                    em.RemoveComponent<PassiveWorkerTag>(e);

                em.AddComponent<FeraldisWorkerTag>(e);
                em.AddComponent<FeraldisUnitTag>(e);
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
                    em.AddComponentData(e, new HasEagle { Eagle = bird });
                }
            }

            targets.Dispose();
            spots.Dispose();
            owners.Dispose();
        }
    }
}
