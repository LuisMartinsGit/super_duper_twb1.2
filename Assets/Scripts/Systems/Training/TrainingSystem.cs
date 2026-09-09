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

        /// <summary>Units per completed Longhouse item.</summary>
        private const int LonghouseBatchSize = 5;

        /// <summary>The Longhouse trains its batch in 90% of one unit's time.</summary>
        private const float LonghouseBatchTimeMultiplier = 0.9f;

        // ── Duration ───────────────────────────────────────────────────────

        /// <summary>
        /// Seconds this building takes to train <paramref name="unitId"/>,
        /// with every modifier the old system applied: Feraldis' 1.75x (paid
        /// back by the 2x spawn), the War sect's speed ladder, Call to Arms,
        /// the building's level, Conscription at the Barracks, King Lexor's
        /// respawn tax, and the Longhouse batch rate.
        /// </summary>
        /// <param name="heroRevivalLevel">The level a revived hero is coming
        /// back at, from the queue item, or 0 for any ordinary training.
        /// Only a FULL HONOURS revival — one returning him at the very level he
        /// died at — pays the time surcharge; Rally the Oath is deliberately
        /// priced at the hero's ordinary time, which is what makes it the
        /// cheap-and-quick option rather than merely the weaker one.</param>
        public static float TrainDuration(EntityManager em, Entity building, string unitId, Faction faction,
            byte heroRevivalLevel = 0)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var udef)) return 1f;
            float trainingTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;

            bool batch = em.HasComponent<BatchTrainingTag>(building);

            // Feraldis culture: 1.75x training time, compensated by the 2x
            // spawn. The Longhouse is compensated by its BATCH instead, so the
            // culture factor does not stack onto it.
            if (!batch && FactionColors.GetFactionCulture(faction) == Cultures.Feraldis)
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

            // Building upgrade: cultured Hall/Barracks train faster.
            // Multiplier is 1.0 at lvl 0 and shrinks per level.
            if (em.HasComponent<BuildingUpgradeState>(building))
            {
                byte upLevel = em.GetComponentData<BuildingUpgradeState>(building).Level;
                trainingTime *= TheWaningBorder.Core.Settings.BuildingUpgradeConfig
                    .TrainTimeMultiplier[upLevel];
            }

            // Conscription (Age 0 Barracks tech): +20% training speed at the
            // Barracks — time / 1.2.
            if (em.HasComponent<BarracksTag>(building))
            {
                var research = FactionResearchState.Instance;
                if (research != null && research.HasResearched(faction, "Conscription"))
                    trainingTime /= 1.2f;
            }

            // Reviving a hero AT the level he died at takes as much longer as
            // it costs more (docs/Design/Heroes.md §4).
            //
            // This replaced a flat +15 % per prior death that compounded
            // forever and scaled with nothing: losing a level-1 king and a
            // level-9 king were priced identically, and a player who had lost
            // three kings paid that tax on every future king regardless of
            // which revival they chose.
            //
            // The mode is DERIVED from the level on the queue item rather than
            // passed separately, so the time can never disagree with the price
            // that was actually charged: only a return at the exact level lost
            // is a Full Honours.
            if (heroRevivalLevel > 0
                && heroRevivalLevel == TheWaningBorder.Abilities.HeroRevival.DiedAtLevel(faction))
            {
                trainingTime *= TheWaningBorder.Abilities.HeroRevival.PriceMultiplier(
                    faction, TheWaningBorder.Abilities.HeroRevivalMode.FullHonours);
            }

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
            return FactionColors.GetFactionCulture(faction) == Cultures.Feraldis && !isSectUnit ? 2 : 1;
        }

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

            // A REVIVED hero comes back at the level the player paid for
            // (docs/Design/Heroes.md §4). The factory always stamps level 1,
            // because it cannot know which revival was bought.
            //
            // The XP is set to that level's FLOOR, not carried over: banked
            // progress past the level is gone. Otherwise Rally the Oath would
            // refund itself within one fight — come back three levels down and
            // climb straight back on experience you had already spent.
            if (heroRevivalLevel > 0 && em.HasComponent<HeroLevel>(unit))
            {
                byte lvl = TheWaningBorder.Economy.HeroProgressionConfig.Clamp(heroRevivalLevel);
                em.SetComponentData(unit, new HeroLevel { Value = lvl });
                if (em.HasComponent<HeroExperience>(unit))
                {
                    em.SetComponentData(unit, new HeroExperience
                    {
                        Xp = TheWaningBorder.Economy.HeroProgressionConfig.XpToReach(lvl)
                    });
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

            if (!em.HasComponent<GuardPoint>(unit))
                em.AddComponentData(unit, new GuardPoint { Position = slotTarget, Has = 1 });
            else
                em.SetComponentData(unit, new GuardPoint { Position = slotTarget, Has = 1 });
        }
    }
}
