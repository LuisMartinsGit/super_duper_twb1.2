// TrainingSystem.cs
// The unit-training half of the production queue: how long a unit takes at
// THIS building, how many come out, whether the faction has room for them,
// and the spawn itself. Called by ProductionQueueSystem when a Train item
// reaches the head and when it completes.
//
// NOT an ECS system any more (2026-09-07). It used to be one — the second
// clock beside ResearchSystem, iterating its own TrainQueueItem buffer with
// its own TrainingState — and BatchTrainingSystem was a third, ticking the
// Feraldis Longhouse alone. All three are one clock now
// (ProductionQueueSystem); this file keeps only what is specific to a unit
// coming out of a building. Same shape as BuildingUpgradeSystem after the
// level-ups merged.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Research;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Systems.Training
{
    public static class TrainingSystem
    {
        #region Cached queries

        static readonly ComponentType[] QT_Population =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionPopulation>(),
        };
        static CachedEntityQuery QC_Population;

        #endregion

        /// <summary>The Conscription tech and the effectsList stat that carries
        /// its training speed (Conscription.asset: TrainSpeed Pct 15).</summary>
        private const string ConscriptionTech = "Conscription";
        private const string TrainSpeedStat = "TrainSpeed";

        /// <summary>Units per completed Longhouse item.</summary>
        private const int LonghouseBatchSize = 5;

        /// <summary>The Longhouse trains its batch in 90% of one unit's time.</summary>
        private const float LonghouseBatchTimeMultiplier = 0.9f;

        // ── Duration ───────────────────────────────────────────────────────

        /// <summary>
        /// Seconds this building takes to train <paramref name="unitId"/>,
        /// with every modifier the old system applied: Feraldis' 1.75x (paid
        /// back by the 2x spawn), the War sect's speed ladder, Call to Arms,
        /// the building's level, Conscription at military buildings, King Lexor's
        /// fallen-hero surcharge, and the Longhouse batch rate.
        /// </summary>
        /// <param name="heroRevivalLevel">The level a fallen hero is coming back at,
        /// from the queue item, or 0 for any ordinary training. It pays the
        /// time surcharge (Heroes.md §5).</param>
        public static float TrainDuration(EntityManager em, Entity building, string unitId, Faction faction,
            byte heroRevivalLevel = 0)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var udef)) return 1f;
            float trainingTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;

            bool batch = em.HasComponent<BatchTrainingTag>(building);

            // Feraldis culture: 1.75x training time, compensated by the 2x
            // spawn. The Longhouse is compensated by its BATCH instead, so the
            // culture factor does not stack onto it.
            if (!batch && FactionColors.GetFactionCulture(faction) == Cultures.Feraldis && !IsSingleFeraldis(unitId))
                trainingTime *= 1.75f;

            // Sect of War: military units train -15/-25/-35% faster
            // (Lv I/II/III). (task-063 phase 2d / phase 4 scaling)
            var cls = UnitFactory.GetUnitClass(unitId);
            bool isMilitary = cls == UnitClass.Melee || cls == UnitClass.Ranged || cls == UnitClass.Siege;
            if (isMilitary)
            {
                byte warLevel = SectQuery.LevelOf(em, faction, SectConfig.War, SectLeverKind.Passive);
                if (warLevel > 0)
                    trainingTime *= WarSectCostHelper.TrainTimeMultiplierFor(warLevel);
            }

            // Call to Arms (War, Lv III): this building trains at double speed
            // while the boon stands. Applies to every unit it makes, not just
            // military - the power buffs the BUILDING, not the unit class.
            float boonSpeed = WarSectCostHelper.TrainingBoonSpeedMultiplier(em, building);
            if (boonSpeed > 1f) trainingTime /= boonSpeed;

            // A building on cursed ground works slower (Territory_Claims.md §6.9.9).
            trainingTime /= TheWaningBorder.Systems.Border.CursedGroundDecay.WorkSpeed(em, building);

            // The building's level: its level SO's trainTimeMultiplier, absolute
            // per level (2026-11-04: this read a code table, so the SO field did
            // nothing). The table stands in only for a level with no SO.
            if (em.HasComponent<BuildingUpgradeState>(building))
            {
                byte upLevel = em.GetComponentData<BuildingUpgradeState>(building).Level;
                var levelDef = upLevel == 0 ? null : TheWaningBorder.Core.Settings.BuildingUpgradeConfig.LevelDef(
                    em, faction, TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, building), upLevel);
                trainingTime *= levelDef != null && levelDef.trainTimeMultiplier > 0f
                    ? levelDef.trainTimeMultiplier
                    : TheWaningBorder.Core.Settings.BuildingUpgradeConfig.TrainTimeMultiplier[upLevel];
            }

            // Conscription (Barracks tech): its SO's TrainSpeed percent faster
            // training at EVERY MILITARY BUILDING (2026-11-04) — any building
            // but the capital, training a melee, ranged or siege unit. Time /
            // (1 + pct / 100). The number is the tech's data, so the tooltip
            // (which reads this method) and the simulation cannot disagree.
            if (isMilitary && !em.HasComponent<HallTag>(building))
            {
                var research = FactionResearchState.Instance;
                if (research != null && research.HasResearched(faction, ConscriptionTech))
                    trainingTime /= 1f + TechCatalog.TechEffect(ConscriptionTech, TrainSpeedStat) / 100f;
            }

            // A hero coming back at the level it fell at takes as much longer as
            // it costs more (docs/Design/Heroes.md §5). The level rides the queue
            // item, so the time cannot disagree with the price charged.
            if (heroRevivalLevel > 0)
                trainingTime *= TheWaningBorder.Economy.HeroProgressionConfig.RevivalMultiplier(heroRevivalLevel);

            // Longhouse: five units for 90% of one unit's time. The old
            // BatchTrainingSystem applied ONLY this factor and none of the
            // above — the Longhouse never felt the War sect, Call to Arms or
            // its own level, which read as an oversight rather than a rule.
            if (batch) trainingTime *= LonghouseBatchTimeMultiplier;

            return trainingTime;
        }

        // ── Output ─────────────────────────────────────────────────────────

        /// <summary>
        /// How many units one completed item releases: 5 at a Longhouse, 2
        /// for Feraldis (sect units excepted — they never doubled), else 1.
        /// </summary>
        public static int SpawnCount(EntityManager em, Entity building, Faction faction, string unitId)
        {
            if (em.HasComponent<BatchTrainingTag>(building)) return LonghouseBatchSize;
            bool isSectUnit = unitId.StartsWith("Sect_");
            return FactionColors.GetFactionCulture(faction) == Cultures.Feraldis && !isSectUnit
                   && !IsSingleFeraldis(unitId) ? 2 : 1;
        }

        /// <summary>
        /// Feraldis units that are NOT doubled (nor slowed): slaves (the slave
        /// cap counts them one by one), the Overseer and Harkon Fiend (one per
        /// faction). docs/Design/Age_1_Feraldis.md §3.2, §8.
        /// </summary>
        private static bool IsSingleFeraldis(string unitId)
            => unitId == "Feraldis_Slave" || unitId == "Feraldis_Overseer" || unitId == "Feraldis_HarkonFiend"
               || unitId == "Worker";

        // ── Population ─────────────────────────────────────────────────────

        /// <summary>
        /// Room for <paramref name="requiredPop"/> more, counting what other
        /// buildings already released THIS tick so two finishing together
        /// cannot both squeeze past the cap. No population tracking = allow.
        /// </summary>
        public static bool HasPopulationCapacity(EntityManager em, Faction faction,
            int requiredPop, int extraSpawnedThisTick)
        {
            var q = QC_Population.Get(em, QT_Population);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var pops = q.ToComponentDataArray<FactionPopulation>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                return pops[i].Current + extraSpawnedThisTick + requiredPop <= pops[i].Max;
            }
            return true;
        }

        // ── Spawn ──────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns one unit from its ID beside the building, facing the rally
        /// point, and sends it there. Cost was paid when the item was queued.
        /// Structural — call it outside any live query iteration.
        /// </summary>
        /// <param name="heroRevivalLevel">The level a revived hero returns
        /// at, or 0 for an ordinary spawn. docs/Design/Heroes.md §4.</param>
        public static void SpawnUnit(EntityManager em, Entity building, string unitId,
            byte heroRevivalLevel = 0)
        {
            var transform = em.GetComponentData<LocalTransform>(building);
            var faction = em.GetComponentData<FactionTag>(building).Value;

            // Always spawn near the building, then move to rally point
            // Spawn outside the building's inflated blocked footprint (BuildingSize cells +
            // 1 cell padding from PassabilityBuildingSync) with clearance for the unit.
            float buildingHalf = 2f;
            if (em.HasComponent<BuildingSize>(building))
            {
                var bs = em.GetComponentData<BuildingSize>(building);
                buildingHalf = math.max(bs.Width, bs.Height) * 0.5f;
            }

            // Rally read FIRST so the exit point can face it.
            // RallyPoint.TargetEntity is an optional follow-up target.
            // RallyPoint is lockstep-replicated, so steering the spawn by it
            // is deterministic across peers.
            float3 rallyTarget = float3.zero;
            bool hasRally = false;
            Entity rallyTargetEntity = Entity.Null;
            if (em.HasComponent<RallyPoint>(building))
            {
                var rally = em.GetComponentData<RallyPoint>(building);
                if (rally.Has != 0)
                {
                    rallyTarget = rally.Position;
                    hasRally = true;
                    rallyTargetEntity = rally.TargetEntity;
                }
            }

            // Footprint half + the 1-cell passability pad + unit clearance.
            // Was half + 4 on BOTH axes — sqrt(2) x (half + 4) metres out on
            // a fixed NE diagonal, which after the footprint doubling put
            // fresh units 11-17 m from their building. Exit faces the rally
            // point when one is set, +X otherwise.
            float exitOffset = buildingHalf + 2.5f;
            float3 exitDir = new float3(1f, 0f, 0f);
            if (hasRally)
            {
                float3 toRally = rallyTarget - transform.Position;
                toRally.y = 0f;
                if (math.lengthsq(toRally) > 0.01f)
                    exitDir = math.normalize(toRally);
            }
            float3 spawnPos = transform.Position + exitDir * exitOffset;

            // Find empty position near the building to avoid overlap
            float spawnRadius = 0.5f;
            float3 finalPos = SpawnPlacementHelper.FindEmptyPosition(
                spawnPos,
                spawnRadius,
                em,
                maxAttempts: 16
            );

            // All units spawn as individual entities via the centralized
            // UnitFactory. (Battalions removed — every trained unit is a
            // standalone, fully-pathfinding unit.)
            Entity unit = UnitFactory.Create(em, unitId, finalPos, faction);

            // Apply all completed tech effects to the newly spawned unit
            TechEffectSystem.ApplyCompletedTechEffects(em, unit, faction);
            // Alanthor combat passives (Charge / Shield Wall / Deploy Stakes /
            // Siege Screens) are stamped here so a freshly trained unit matches
            // the ones the research sweep already touched.
            TheWaningBorder.Abilities.AlanthorActiveHelper.ApplySpawnPassives(em, unit, faction, unitId);
            // A sect's HERO (docs/Design/Religion.md §4): hero levels, and the
            // sect's recruit latched so the next costs no RP.
            TheWaningBorder.Systems.Sect.SectHeroes.OnSpawned(em, unit, faction, unitId);

            // A hero's level is never reset (docs/Design/Heroes.md §5): it comes
            // back with the level it fell at and every point of XP. The factory
            // always stamps level 1, because it cannot know the record.
            if (heroRevivalLevel > 0 && em.HasComponent<HeroLevel>(unit))
            {
                byte lvl = TheWaningBorder.Economy.HeroProgressionConfig.Clamp(heroRevivalLevel);
                em.SetComponentData(unit, new HeroLevel { Value = lvl });
                if (em.HasComponent<HeroExperience>(unit))
                {
                    TheWaningBorder.Abilities.FactionHeroRecords.TryGet(em, faction, unitId, out _, out int xp);
                    em.SetComponentData(unit, new HeroExperience { Xp = xp });
                }
            }

            if (!hasRally) return;

            // Rallied at a resource node: its rally point is the node's CELL
            // CENTRE, which the node stamps impassable — walking a unit into
            // it is the orbit bug all over again. Aim beside the node instead.
            if (ResourceNodeQuery.IsGatherable(em, rallyTargetEntity)
                && TheWaningBorder.Systems.Work.MiningReach.TryGetMiningStand(
                    em, rallyTargetEntity, finalPos, out float3 beside))
            {
                rallyTarget = beside;
            }

            // RALLY SCATTER (jitter fix, 2026-07-12): every trainee used to
            // get the SAME exact rally point. Arrival needs 0.5 m of that
            // exact point, separation holds later arrivals ~1.5 m off the
            // unit already parked there — unsatisfiable, so trainee #2+
            // orbited the gather point forever. Give each unit its own slot
            // on a golden-angle spiral around the rally instead.
            // Deterministic: derived from the unit's NetworkId (assigned in
            // lockstep order).
            float3 slotTarget = rallyTarget;
            if (em.HasComponent<NetworkedEntity>(unit))
            {
                uint id = (uint)em.GetComponentData<NetworkedEntity>(unit).NetworkId;
                // Golden-angle spiral: ~2.4 rad per step, radius grows every
                // full turn — 1.5 m ring spacing matches the steering lattice
                // (SeparationRadius).
                float angle = id * 2.39996f;
                float radius = 1.5f + 1.5f * ((id % 9u) / 3u); // 1.5 / 3.0 / 4.5
                slotTarget += new float3(
                    math.cos(angle) * radius, 0f, math.sin(angle) * radius);
            }

            if (!em.HasComponent<DesiredDestination>(unit))
                em.AddComponentData(unit, new DesiredDestination { Position = slotTarget, Has = 1 });
            else
                em.SetComponentData(unit, new DesiredDestination { Position = slotTarget, Has = 1 });

            if (!TransientState.Active<GuardPoint>(em, unit))
                TransientState.Set(em, unit, new GuardPoint { Position = slotTarget, Has = 1 });
            else
                em.SetComponentData(unit, new GuardPoint { Position = slotTarget, Has = 1 });
        }
    }
}
