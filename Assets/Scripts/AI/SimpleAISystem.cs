// SimpleAISystem.cs
// Build-order driven AI for the Age-1 phase.
//
// One AIBrain entity per AI faction. Each think tick, the AI looks at the next
// step of its assigned build order and tries to issue it (queue a unit, place
// a building, queue a research, or trigger age-up). On success, it advances to
// the next step. On failure (resource shortfall, no idle builder, queue full),
// it waits for the next tick.
//
// Replaces the old AIBrain / Manager / Behavior multi-system architecture.
// Location: Assets/Scripts/AI/SimpleAISystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Per-faction build-order executor. Not Bursted (touches managed
    /// TechTreeDB / FactionResearchState / Debug.Log).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SimpleAISystem : SystemBase
    {
        // Bank thresholds before triggering AgeUp: cost + reserve buffer.
        // Set to 0: with the optimised build-orders the AI accumulates well
        // beyond the bare cost, but we shouldn't *gate* on it — earlier 500/
        // 200/100 reserves caused the AI to sit on enough resources for age-up
        // (cost ≈ 1000/200/150) and never trigger because the bank stalled
        // between cost and cost+reserve. Players reasonably expected the AI
        // to age up the moment it could afford. Reintroduce a small reserve
        // here only if the post-age-up economy noticeably stalls.
        private const int AgeUpReserveSupplies = 0;
        private const int AgeUpReserveIron     = 0;
        private const int AgeUpReserveCrystal  = 0;

        // How many items can pile up in a building's TrainQueueItem buffer
        // before the AI defers a queue-train step. Low enough that the AI
        // doesn't blindly stack 50 miners; high enough to keep Hall busy.
        private const int MaxTrainQueue = 5;

        // Build placement scan ring: how far from the Hall and at how many
        // angles we try before giving up on this tick. The min was bumped to
        // 10 m and max to 30 m so buildings have room to fan out around the
        // Hall without crowding it (and around each other — see spacing below).
        private const float BuildRingDistanceMin = 10f;
        private const float BuildRingDistanceMax = 30f;
        private const int BuildAngleSamples = 24;

        // 64-bit splitmix RNG seeded per-faction for placement angles + skip rolls.
        private uint _rngState = 0x12345678u;

        protected override void OnCreate()
        {
            RequireForUpdate<AIBrain>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            // Snapshot brains so we can mutate ECS state freely inside the loop.
            var brainsQuery = SystemAPI.QueryBuilder().WithAll<AIBrain, SimpleAIState>().Build();
            using var brainEntities = brainsQuery.ToEntityArray(Allocator.Temp);

            foreach (var brainEntity in brainEntities)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;

                var aiState = em.GetComponentData<SimpleAIState>(brainEntity);

                // Tick countdown.
                aiState.ThinkTimer -= dt;
                if (aiState.ThinkTimer > 0f)
                {
                    em.SetComponentData(brainEntity, aiState);
                    continue;
                }
                aiState.ThinkTimer = AISimpleDifficulty.GetThinkInterval(brain.Difficulty);

                // The AI owns miner tasking. Idle miners get explicit
                // GatherCommands — no auto-find anywhere. The crystal-vs-iron
                // split is whatever the build order set via SetCrystalTarget;
                // 0 (default) means iron-only.
                AssignIdleMiners(em, brain.Owner, aiState.CrystalMinerTarget, brain.Strategy);

                // Replace any military/miners that died since the build order
                // queued them. Runs before the next step so replacements take
                // priority on the train queue and resources.
                ReplaceLostUnits(em, brain.Owner, aiState);

                var buildOrder = AIBuildOrder.For(brain.Strategy);
                if (aiState.StepIndex >= buildOrder.Length)
                {
                    em.SetComponentData(brainEntity, aiState);
                    continue;
                }

                var step = buildOrder[aiState.StepIndex];

                // Easy difficulty may randomly skip optional steps (never Hut,
                // Barracks, Choice or AgeUp — those aren't marked Optional).
                float skipChance = AISimpleDifficulty.GetSkipChance(brain.Difficulty);
                if (step.Optional && skipChance > 0f && NextRandFloat01() < skipChance)
                {
                    aiState.StepIndex++;
                    em.SetComponentData(brainEntity, aiState);
                    continue;
                }

                bool issued = TryIssueStep(em, brain.Owner, step, ref aiState);
                if (issued)
                {
                    aiState.StepIndex++;
                }

                em.SetComponentData(brainEntity, aiState);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // STEP DISPATCH
        // ─────────────────────────────────────────────────────────────────

        private bool TryIssueStep(EntityManager em, Faction faction, BuildOrderStep step, ref SimpleAIState aiState)
        {
            return step.Kind switch
            {
                BuildStepKind.TrainUnit        => TryTrainUnitFromBuildOrder(em, faction, step.Id, ref aiState),
                BuildStepKind.BuildBuilding    => TryBuildBuilding(em, faction, step.Id),
                BuildStepKind.Research         => TryResearchTech(em, faction, step.Id),
                BuildStepKind.AgeUp            => TryAgeUp(em, faction, ref aiState),
                BuildStepKind.SetCrystalTarget => SetCrystalTarget(ref aiState, step.IntArg),
                BuildStepKind.LaunchAttack     => TryLaunchAttack(em, faction, step.IntArg),
                _                              => true,  // unknown step kind: skip silently
            };
        }

        /// <summary>
        /// Build-order Train wrapper: queues the unit and, on success, increments
        /// the matching Desired counter so ReplaceLostUnits knows the AI is now
        /// committed to having this unit alive. Replacement training calls
        /// TryTrainUnit directly so the counter doesn't double-bump.
        /// </summary>
        private static bool TryTrainUnitFromBuildOrder(
            EntityManager em, Faction faction, string unitId, ref SimpleAIState aiState)
        {
            if (!TryTrainUnit(em, faction, unitId)) return false;
            RegisterTrainedUnit(ref aiState, unitId);
            return true;
        }

        private static void RegisterTrainedUnit(ref SimpleAIState aiState, string unitId)
        {
            UnitClass cls = UnitFactory.GetUnitClass(unitId);
            if (IsCombatClass(cls))
            {
                aiState.DesiredMilitary++;
                aiState.LastMilitaryUnit = new FixedString64Bytes(unitId);
            }
            else if (cls == UnitClass.Miner)
            {
                aiState.DesiredMiners++;
            }
            // Builders/Scout/Support not auto-replaced for now — none of the
            // current build orders rely on them surviving in the same way.
        }

        /// <summary>
        /// Apply a SetCrystalTarget build-order step. Just clamps and writes the
        /// target on the AI brain's SimpleAIState — AssignIdleMiners reads it on
        /// the next think tick. Always succeeds so the build order advances.
        /// </summary>
        private static bool SetCrystalTarget(ref SimpleAIState aiState, int count)
        {
            // Clamp at the system cap (4) so a typo in a build order can't
            // request 50 crystal miners and starve iron entirely.
            aiState.CrystalMinerTarget = math.clamp(count, 0, MaxCrystalMiners);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // TRAIN UNIT
        // ─────────────────────────────────────────────────────────────────

        private static bool TryTrainUnit(EntityManager em, Faction faction, string unitId)
        {
            if (TechTreeDB.Instance == null) return false;
            if (!TechTreeDB.Instance.TryGetUnit(unitId, out var def) || def == null) return false;

            // Find the right training building for this unit.
            Entity trainer = FindTrainerForUnit(em, faction, unitId);
            if (trainer == Entity.Null) return false;

            // Don't queue into a building still under construction.
            if (em.HasComponent<UnderConstruction>(trainer)) return false;
            if (!em.HasBuffer<TrainQueueItem>(trainer)) return false;

            var queue = em.GetBuffer<TrainQueueItem>(trainer);
            if (queue.Length >= MaxTrainQueue) return false;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            if (!FactionEconomy.Spend(em, faction, cost)) return false;

            queue.Add(new TrainQueueItem { UnitId = new FixedString64Bytes(unitId) });
            return true;
        }

        private static Entity FindTrainerForUnit(EntityManager em, Faction faction, string unitId)
        {
            // Hall trains support units (Miner, Builder, Scout).
            // Barracks trains line troops (Swordsman, Archer).
            // TempleOfRidan trains the Litharch healer.
            switch (unitId)
            {
                case "Miner":
                case "Builder":
                case "Scout":
                    return FindFactionBuilding<HallTag>(em, faction);
                case "Swordsman":
                case "Archer":
                    return FindFactionBuilding<BarracksTag>(em, faction);
                case "Litharch":
                    return FindFactionBuilding<TempleTag>(em, faction);
                default:
                    return Entity.Null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // BUILD BUILDING
        // ─────────────────────────────────────────────────────────────────

        private bool TryBuildBuilding(EntityManager em, Faction faction, string buildingId)
        {
            if (TechTreeDB.Instance == null) return false;
            if (!TechTreeDB.Instance.TryGetBuilding(buildingId, out var def) || def == null) return false;

            // Choice-buildings are limited to one per faction.
            if (BuildingFactory.IsChoiceBuilding(buildingId))
            {
                var existing = BuildingFactory.GetFactionChoiceBuilding(em, faction);
                if (existing != null) return false;
            }

            // Need a Hall to anchor placement around.
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null) return false;
            if (!em.HasComponent<LocalTransform>(hall)) return false;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;

            int2 size = BuildingSizeConfig.GetSize(buildingId);
            if (!TryFindBuildPosition(em, hallPos, size, buildingId, out float3 pos)) return false;

            // Pre-flight: at least one idle builder must be available BEFORE we
            // spend the cost and place the foundation. Without this gate the
            // build-order step advanced on a successful placement even when zero
            // builders were dispatched, leaving an orphan UnderConstruction site
            // that never gained HP and a permanently stalled build queue (the
            // build order would never re-attempt the same step). (task-062 G-2)
            if (CountIdleBuilders(em, faction) == 0) return false;

            if (!FactionEconomy.Spend(em, faction, cost)) return false;

            // Same call site the human player's BuildCommandPanel ends up at.
            // Handles UnderConstruction + HP-1 + lockstep wiring.
            Entity building = CommandRouter.PlaceBuildingDirect(em, buildingId, pos, faction);
            if (building == Entity.Null) return false;

            // Dispatch idle builders to actually construct the thing — without
            // this the building is created with HP=1 and UnderConstruction but
            // never gains progress. The human player flow does the same step
            // explicitly via BuildCommandPanel.AssignBuildersToConstruction.
            int dispatched = DispatchBuildersTo(em, faction, building, buildingId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                // Race: a builder went busy between the pre-flight check and
                // dispatch. Refund cost + destroy the orphan foundation rather
                // than advancing the step on a stalled site.
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Count the faction's idle builders. Cheap O(N) snapshot used as a
        /// pre-flight gate so TryBuildBuilding doesn't spend resources on a
        /// foundation that no builder will ever pick up. (task-062 G-2)
        /// </summary>
        private static int CountIdleBuilders(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<BuildOrder>(ents[i])) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Find up to <paramref name="maxBuilders"/> idle builders of the given
        /// faction and issue BuildCommand on each, pointing at <paramref name="site"/>.
        /// Idle = has CanBuild but no current BuildOrder.
        /// </summary>
        /// <returns>Number of builders actually dispatched (0 = nobody available).</returns>
        private static int DispatchBuildersTo(
            EntityManager em, Faction faction, Entity site,
            string buildingId, float3 sitePos, int maxBuilders)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs  = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Collect idle builders + their distance² to the site.
            var idle = new System.Collections.Generic.List<BuilderCandidate>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var b = ents[i];
                if (em.HasComponent<BuildOrder>(b)) continue;     // already busy building
                float dx = xfs[i].Position.x - sitePos.x;
                float dz = xfs[i].Position.z - sitePos.z;
                idle.Add(new BuilderCandidate { Entity = b, DistSq = dx * dx + dz * dz });
            }

            // Sort by distance ascending, dispatch the nearest few.
            idle.Sort((a, c) => a.DistSq.CompareTo(c.DistSq));

            int dispatched = 0;
            for (int i = 0; i < idle.Count && dispatched < maxBuilders; i++)
            {
                CommandRouter.IssueBuild(em, idle[i].Entity, site, buildingId, sitePos);
                dispatched++;
            }
            return dispatched;
        }

        private struct BuilderCandidate
        {
            public Entity Entity;
            public float DistSq;
        }

        // Default: candidate must be ≥12 m from any existing building so the
        // AI leaves wide walkable corridors. Earlier 7 m was just enough that
        // unit pathing could squeeze through, but Gaussian-smoothed flow at
        // tight cell-corner thresholds would dither and units got stuck.
        // 12 m → ~6-9 m of clear corridor between most building footprints,
        // comfortably wider than any unit's collision radius.
        private const float MinBuildingSpacing = 12f;

        // GathererHut income falls off when their 15 m gather circles overlap
        // (production = unobstructed area). Two GHs need ≥2× the gather radius
        // between centres to keep their footprints disjoint. The previous AI
        // honoured this; this constant restores that behaviour.
        private const float MinGHutToGHutSpacing = 30f;

        private bool TryFindBuildPosition(EntityManager em, float3 anchor, int2 size, out float3 pos)
        {
            return TryFindBuildPosition(em, anchor, size, buildingId: null, out pos);
        }

        private bool TryFindBuildPosition(EntityManager em, float3 anchor, int2 size, string buildingId, out float3 pos)
        {
            // Snapshot existing buildings once per call. We need both positions
            // and "is GathererHut?" so we can apply the GH-vs-GH spacing rule.
            var bldgQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var bldgEntities  = bldgQuery.ToEntityArray(Allocator.Temp);
            using var bldgTransforms = bldgQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Pre-mark which existing buildings are GathererHuts so we can do
            // the 30 m check only against them when placing another GHut.
            // Managed bool[] sidesteps NativeArray's `using var` write-access
            // restriction and SimpleAISystem isn't Bursted, so it costs nothing.
            var bldgIsGHut = new bool[bldgEntities.Length];
            for (int i = 0; i < bldgEntities.Length; i++)
                bldgIsGHut[i] = em.HasComponent<GathererHutTag>(bldgEntities[i]);

            bool placingGHut = buildingId == "GatherersHut";
            float minSpacingSq      = MinBuildingSpacing      * MinBuildingSpacing;
            float minGHutSpacingSq  = MinGHutToGHutSpacing    * MinGHutToGHutSpacing;

            // Sample a ring of angles around the anchor at increasing radii.
            // GHs naturally need a wider ring to satisfy the 30 m spacing.
            float maxRadius = placingGHut ? BuildRingDistanceMax + 30f : BuildRingDistanceMax;
            for (float r = BuildRingDistanceMin; r <= maxRadius; r += 4f)
            {
                int angleStart = (int)(NextRandFloat01() * BuildAngleSamples);
                for (int i = 0; i < BuildAngleSamples; i++)
                {
                    int idx = (angleStart + i) % BuildAngleSamples;
                    float angle = (idx / (float)BuildAngleSamples) * math.PI * 2f;
                    float3 candidate = new float3(
                        anchor.x + math.cos(angle) * r,
                        0f,
                        anchor.z + math.sin(angle) * r);
                    candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                    if (TooCloseToExistingBuilding(
                            candidate, bldgTransforms, bldgIsGHut,
                            minSpacingSq, minGHutSpacingSq, placingGHut))
                        continue;

                    if (BuildCommandHelper.IsValidBuildPosition(em, candidate, size))
                    {
                        pos = candidate;
                        return true;
                    }
                }
            }
            pos = default;
            return false;
        }

        /// <summary>
        /// Per-pair spacing check. All buildings keep <paramref name="minDistSq"/>
        /// from each other; additionally, GathererHut→GathererHut placement uses
        /// <paramref name="minGHutDistSq"/> so their 15 m gather circles don't
        /// overlap (which halves their unobstructed-area-driven income).
        /// </summary>
        private static bool TooCloseToExistingBuilding(
            float3 candidate,
            NativeArray<LocalTransform> existing,
            bool[] existingIsGHut,
            float minDistSq,
            float minGHutDistSq,
            bool placingGHut)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                float dx = candidate.x - existing[i].Position.x;
                float dz = candidate.z - existing[i].Position.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < minDistSq) return true;
                if (placingGHut && existingIsGHut[i] && d2 < minGHutDistSq) return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // RESEARCH TECH
        // ─────────────────────────────────────────────────────────────────

        private static bool TryResearchTech(EntityManager em, Faction faction, string techId)
        {
            if (TechTreeDB.Instance == null) return false;
            if (!TechTreeDB.Instance.TryGetTechnology(techId, out var def) || def == null) return false;

            // Skip if already researched (or in flight) on this faction.
            var researchState = FactionResearchState.Instance;
            if (researchState != null && researchState.HasResearched(faction, techId)) return true;

            // BasicDrills/WoodenArmor research at the Barracks. Other techs
            // route through the Hall. We only ship Barracks techs in the
            // current build orders, but support both for forward-compat.
            string researchAt = string.IsNullOrEmpty(def.researchAt) ? "Hall" : def.researchAt;
            Entity bldg = researchAt switch
            {
                "Barracks" => FindFactionBuilding<BarracksTag>(em, faction),
                "Hall"     => FindFactionBuilding<HallTag>(em, faction),
                _          => Entity.Null,
            };
            if (bldg == Entity.Null) return false;
            if (em.HasComponent<UnderConstruction>(bldg)) return false;
            if (!em.HasBuffer<ResearchQueueItem>(bldg)) return false;

            var queue = em.GetBuffer<ResearchQueueItem>(bldg);
            if (queue.Length >= MaxTrainQueue) return false;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            if (!FactionEconomy.Spend(em, faction, cost)) return false;

            queue.Add(new ResearchQueueItem { TechId = new FixedString64Bytes(techId) });
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // AGE UP
        // ─────────────────────────────────────────────────────────────────

        private bool TryAgeUp(EntityManager em, Faction faction, ref SimpleAIState aiState)
        {
            if (aiState.AgeUpIssued != 0) return true; // already triggered, advance

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null) return false;

            // Need a choice building (Shrine / Vault / Keep / TempleOfRidan).
            if (!FactionHasChoiceBuilding(em, faction)) return false;

            // Wait for: cost + reserve. Matches the optimised build-order targets.
            var ageUpCost = CultureConfig.AgeUpCost;
            var target = new Cost
            {
                Supplies = ageUpCost.Supplies + AgeUpReserveSupplies,
                Iron     = ageUpCost.Iron     + AgeUpReserveIron,
                Crystal  = ageUpCost.Crystal  + AgeUpReserveCrystal,
            };
            if (!FactionEconomy.CanAfford(em, faction, target)) return false;
            if (!FactionEconomy.Spend(em, faction, ageUpCost)) return false;

            // Pick the strategy's preferred Age-2 culture.
            var brainEntity = FindBrainEntity(em, faction);
            byte culture = Cultures.None;
            if (brainEntity != Entity.Null)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                culture = AIBuildOrder.CultureFor(brain.Strategy, NextRandUint());
            }

            FactionColors.SetFactionCulture(faction, culture);

            float duration = CultureConfig.AgeUpDuration;
            em.AddComponentData(hall, new AgeUpState
            {
                Culture   = culture,
                Duration  = duration,
                Remaining = duration,
            });

            aiState.AgeUpIssued = 1;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // REPLACE LOST UNITS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-queue training for any military/miner units that died after the
        /// build order originally trained them. The deficit = DesiredX - (alive
        /// of that type + already queued of that type). Queues at most one
        /// replacement per category per think tick — replacements pile up over
        /// successive ticks rather than flooding the train queue or blowing
        /// the bank in one frame.
        ///
        /// We never decrement DesiredX. A dead unit just stops contributing to
        /// "alive" and the deficit appears naturally; once a replacement is
        /// queued and trained, alive catches back up and the deficit closes.
        /// </summary>
        private static void ReplaceLostUnits(EntityManager em, Faction faction, SimpleAIState aiState)
        {
            // Military deficit
            if (aiState.DesiredMilitary > 0 && !aiState.LastMilitaryUnit.IsEmpty)
            {
                int aliveMil = CountAliveMilitary(em, faction);
                int queuedMil = CountQueuedByPredicate(em, faction, isCombat: true);
                int deficit = aiState.DesiredMilitary - (aliveMil + queuedMil);
                if (deficit > 0)
                {
                    // TryTrainUnit (not the build-order wrapper) so DesiredMilitary
                    // doesn't double-count. Failure (queue full / can't afford) is
                    // silent — next tick will try again.
                    TryTrainUnit(em, faction, aiState.LastMilitaryUnit.ToString());
                }
            }

            // Miner deficit
            if (aiState.DesiredMiners > 0)
            {
                int aliveMin = CountAliveMiners(em, faction);
                int queuedMin = CountQueuedByPredicate(em, faction, isMiner: true);
                int deficit = aiState.DesiredMiners - (aliveMin + queuedMin);
                if (deficit > 0)
                {
                    TryTrainUnit(em, faction, "Miner");
                }
            }
        }

        /// <summary>
        /// Count military units of <paramref name="faction"/>: combat-class
        /// UnitTag, battalion leader OR loose unit (skip members so a 4-man
        /// battalion still counts as 1 toward DesiredMilitary, matching the
        /// "1 Train step = 1 entry" bookkeeping).
        /// </summary>
        private static int CountAliveMilitary(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                if (em.HasComponent<BattalionMemberData>(ents[i])) continue;
                n++;
            }
            return n;
        }

        private static int CountAliveMiners(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        /// <summary>
        /// Count items in this faction's training queues that match either the
        /// combat-class predicate or the miner predicate. Either flag may be
        /// set; both unset returns 0. Avoids walking the queues twice for
        /// callers that need both counts.
        /// </summary>
        private static int CountQueuedByPredicate(
            EntityManager em, Faction faction, bool isCombat = false, bool isMiner = false)
        {
            if (!isCombat && !isMiner) return 0;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<TrainQueueItem>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buffer = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buffer.Length; j++)
                {
                    string id = buffer[j].UnitId.ToString();
                    UnitClass cls = UnitFactory.GetUnitClass(id);
                    if (isCombat && IsCombatClass(cls)) n++;
                    else if (isMiner && cls == UnitClass.Miner) n++;
                }
            }
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // LAUNCH ATTACK
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Send all idle military (battalion leaders + loose units, not members)
        /// to attack-move toward the closest enemy economy target. Returns
        /// false (blocks the build order) until at least <paramref name="minUnits"/>
        /// idle units are available. The targeting priority is: enemy Miners
        /// first (mobile income), then GathererHuts (income buildings), then
        /// Halls (last resort — they're tankier and finish-the-game targets).
        /// </summary>
        private static bool TryLaunchAttack(EntityManager em, Faction faction, int minUnits)
        {
            // Need a Hall to know where the army is staging from (used as the
            // "origin" for picking the closest enemy). If no Hall exists we
            // can't pick a target meaningfully — fail silently.
            Entity myHall = FindFactionBuilding<HallTag>(em, faction);
            if (myHall == Entity.Null) return false;
            if (!em.HasComponent<LocalTransform>(myHall)) return false;
            float3 originPos = em.GetComponentData<LocalTransform>(myHall).Position;

            // Find idle military: any UnitTag with a combat class, this faction,
            // no active commands, not currently a battalion *member* (members
            // follow their leader; we issue to the leader only).
            var militaryQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = militaryQuery.ToEntityArray(Allocator.Temp);
            using var tags = militaryQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = militaryQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var idleMilitary = new System.Collections.Generic.List<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<BattalionMemberData>(e)) continue;       // leader-only
                if (em.HasComponent<UnderConstruction>(e)) continue;
                // Already on a mission or carrying out another order — leave alone.
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<MoveCommand>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                idleMilitary.Add(e);
            }

            if (idleMilitary.Count < minUnits) return false; // wait for the army

            Entity target = ChooseAttackTarget(em, faction, originPos);
            if (target == Entity.Null) return false; // no enemy reachable
            if (!em.HasComponent<LocalTransform>(target)) return false;
            float3 targetPos = em.GetComponentData<LocalTransform>(target).Position;

            for (int i = 0; i < idleMilitary.Count; i++)
                AttackMoveCommandHelper.Execute(em, idleMilitary[i], targetPos);

            return true;
        }

        private static bool IsCombatClass(UnitClass c)
        {
            return c == UnitClass.Melee || c == UnitClass.Ranged
                || c == UnitClass.Siege || c == UnitClass.Magic;
        }

        /// <summary>
        /// Pick the closest enemy economy target by priority:
        /// Miners → GathererHuts → Halls. Distance is measured from
        /// <paramref name="originPos"/> (the AI's Hall) so the army marches
        /// toward the nearest enemy first instead of crossing the map.
        /// </summary>
        private static Entity ChooseAttackTarget(EntityManager em, Faction myFaction, float3 originPos)
        {
            Entity t = FindClosestEnemyOf<MinerTag>(em, myFaction, originPos);
            if (t != Entity.Null) return t;
            t = FindClosestEnemyOf<GathererHutTag>(em, myFaction, originPos);
            if (t != Entity.Null) return t;
            return FindClosestEnemyOf<HallTag>(em, myFaction, originPos);
        }

        private static Entity FindClosestEnemyOf<TTag>(
            EntityManager em, Faction myFaction, float3 originPos)
            where TTag : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == myFaction) continue;
                // Skip targets still under construction (Halls only — others
                // wouldn't have UnderConstruction). Easier to detect by checking
                // the component than to add a separate query exclusion.
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                float dx = xfs[i].Position.x - originPos.x;
                float dz = xfs[i].Position.z - originPos.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        // ─────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────

        private static Entity FindFactionBuilding<TTag>(EntityManager em, Faction faction)
            where TTag : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                // Skip buildings still under construction unless caller checks itself.
                return entities[i];
            }
            return Entity.Null;
        }

        private static bool FactionHasChoiceBuilding(EntityManager em, Faction faction)
        {
            // Choice buildings carry ChoiceBuildingTag (set by BuildingFactory for
            // ShrineOfAhridan / VaultOfAlmierra / FiendstoneKeep). The AI age-up
            // gate must require a COMPLETED choice building — the canonical
            // helper that excludes UnderConstruction is
            // GetCompletedFactionChoiceBuilding. (Player + AI gates were both
            // counting under-construction choice buildings before the fix.)
            var existing = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction);
            if (existing != null) return true;

            // Also accept a completed TempleOfRidan even though it isn't a
            // "choice" building per ChoiceBuildingIds.
            Entity temple = FindFactionBuilding<TempleTag>(em, faction);
            return temple != Entity.Null && !em.HasComponent<UnderConstruction>(temple);
        }

        private static Entity FindBrainEntity(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) return entities[i];
            }
            return Entity.Null;
        }

        private static Cost ToCost(CostBlock block)
        {
            if (block == null) return default;
            return new Cost
            {
                Supplies  = block.Supplies,
                Iron      = block.Iron,
                Crystal   = block.Crystal,
                Veilsteel = block.Veilsteel,
                Glow      = block.Glow,
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // MINER TASKING
        // ─────────────────────────────────────────────────────────────────

        // Hard upper bound on the per-strategy SetCrystalTarget value. Bumped
        // to 16 so the runtime 50/50 floor (totalMiners / 2) isn't crushed by
        // an old clamp from when crystal miners were treated as a niche.
        private const int MaxCrystalMiners = 16;

        /// <summary>
        /// Issue explicit GatherCommands to every idle AI miner. Iron and crystal
        /// are separate flows: the AI counts current crystal miners and, while
        /// under the effective target, sends new idle miners to cadavers; the
        /// rest go to iron.
        ///
        /// Default effective target = <c>max(buildOrderTarget, totalMiners / 2)</c>.
        /// The build-order SetCrystalTarget normally acts as a FLOOR — strategies
        /// can front-load crystal demand (e.g. TechBoom asking for 2 with only
        /// 4 miners) and the steady-state allocation is 50/50 because crystal
        /// is just as important as iron for age-up + tech.
        ///
        /// EXCEPTION: military-rush strategies (Rush) treat their SetCrystalTarget
        /// as an explicit CAP, not a floor. The 50/50 floor would otherwise
        /// override Rush's `SetCrystalTarget(1)` (only "enough crystal for
        /// Shrine + age-up") and starve early military production. (task-062 G-1)
        ///
        /// Auto-find is fully removed from MiningSystem and CrystalMiningSystem
        /// for AI factions — every miner movement is the result of a command
        /// issued here (or the LOS-based after-depletion routing inside the
        /// mining systems, which is intentional player UX).
        /// </summary>
        private static void AssignIdleMiners(EntityManager em, Faction faction, int targetCrystal, AIStrategy strategy)
        {
            // Defensive clamp: SetCrystalTarget already clamps writes, but a
            // bootstrap that left CrystalMinerTarget at default still produces
            // a sane non-negative value here.
            targetCrystal = math.clamp(targetCrystal, 0, MaxCrystalMiners);
            // Find this faction's dropoff (Hall first, then any GathererHut).
            Entity dropoff = FindFactionBuilding<HallTag>(em, faction);
            if (dropoff == Entity.Null)
                dropoff = FindFactionBuilding<GathererHutTag>(em, faction);
            if (dropoff == Entity.Null) return; // can't gather without a dropoff

            // Snapshot all non-depleted iron deposits and cadavers. We do per-
            // miner nearest selection below so miners spread across multiple
            // deposits instead of all converging on one.
            var ironQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ironEnts = ironQuery.ToEntityArray(Allocator.Temp);
            using var ironStates = ironQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var ironTransforms = ironQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var cadaverQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<CadaverState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var cadaverEnts = cadaverQuery.ToEntityArray(Allocator.Temp);
            using var cadaverStates = cadaverQuery.ToComponentDataArray<CadaverState>(Allocator.Temp);
            using var cadaverTransforms = cadaverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            bool anyIron = HasAnyIron(ironStates);
            bool anyCadaver = HasAnyCadaver(cadaverStates);
            if (!anyIron && !anyCadaver) return;

            // Snapshot this faction's miners.
            var minerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<MinerState>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var minerEntities = minerQuery.ToEntityArray(Allocator.Temp);
            using var minerStates = minerQuery.ToComponentDataArray<MinerState>(Allocator.Temp);
            using var minerFactions = minerQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var minerTransforms = minerQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int totalMiners = 0;
            int crystalMiners = 0;
            var idleMiners = new System.Collections.Generic.List<(Entity ent, float3 pos)>();

            for (int i = 0; i < minerEntities.Length; i++)
            {
                if (minerFactions[i].Value != faction) continue;
                totalMiners++;
                var ms = minerStates[i];
                if (ms.GatheringResource == 1) crystalMiners++;
                // Idle = not currently moving/mining/returning AND not already
                // commanded (no GatherCommand pending). Skipping miners that
                // already hold a GatherCommand prevents reissuing every tick.
                if (ms.State == MinerWorkState.Idle
                    && !em.HasComponent<GatherCommand>(minerEntities[i])
                    && !em.HasComponent<UserMoveOrder>(minerEntities[i]))
                    idleMiners.Add((minerEntities[i], minerTransforms[i].Position));
            }

            if (idleMiners.Count == 0) return;

            // 50/50 floor: at minimum, half the workforce should be on crystal
            // when cadavers are reachable. The build-order target only wins if
            // it asks for MORE crystal than 50/50 (early front-loading). This
            // replaces the previous cap-driven allocation where the AI sat at
            // 1-3 crystal miners regardless of army size and starved on crystal.
            //
            // Rush opts out of the floor — it sets SetCrystalTarget(1) on
            // purpose ("just enough for Shrine + age-up") and folding it into
            // the 50/50 floor would push the AI to 4 crystal miners with 8
            // total, defeating the military-first design. (task-062 G-1)
            if (anyCadaver && strategy != AIStrategy.Rush)
                targetCrystal = math.max(targetCrystal, totalMiners / 2);

            for (int i = 0; i < idleMiners.Count; i++)
            {
                var (miner, minerPos) = idleMiners[i];

                // Prefer crystal until the AI hits its target count, but only
                // if a cadaver is actually available. Otherwise send to iron.
                bool wantCrystal = crystalMiners < targetCrystal && anyCadaver;

                Entity target = wantCrystal
                    ? PickNearestCadaver(minerPos, cadaverEnts, cadaverStates, cadaverTransforms)
                    : PickNearestIron(minerPos, ironEnts, ironStates, ironTransforms);

                if (target == Entity.Null)
                {
                    // First-choice resource is gone (e.g. last cadaver depleted
                    // mid-loop). Try the other side once before giving up.
                    target = wantCrystal
                        ? PickNearestIron(minerPos, ironEnts, ironStates, ironTransforms)
                        : PickNearestCadaver(minerPos, cadaverEnts, cadaverStates, cadaverTransforms);
                    if (target == Entity.Null) continue;
                    wantCrystal = !wantCrystal;
                }

                GatherCommandHelper.Execute(em, miner, target, dropoff);
                if (wantCrystal) crystalMiners++;
            }
        }

        private static bool HasAnyIron(Unity.Collections.NativeArray<IronDepositState> states)
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i].Depleted == 0 && states[i].RemainingIron > 0) return true;
            return false;
        }

        private static bool HasAnyCadaver(Unity.Collections.NativeArray<CadaverState> states)
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i].Depleted == 0 && states[i].RemainingCrystal > 0) return true;
            return false;
        }

        private static Entity PickNearestIron(float3 from,
            Unity.Collections.NativeArray<Entity> ents,
            Unity.Collections.NativeArray<IronDepositState> states,
            Unity.Collections.NativeArray<LocalTransform> transforms)
        {
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingIron <= 0) continue;
                float dx = transforms[i].Position.x - from.x;
                float dz = transforms[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        private static Entity PickNearestCadaver(float3 from,
            Unity.Collections.NativeArray<Entity> ents,
            Unity.Collections.NativeArray<CadaverState> states,
            Unity.Collections.NativeArray<LocalTransform> transforms)
        {
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingCrystal <= 0) continue;
                float dx = transforms[i].Position.x - from.x;
                float dz = transforms[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        // ─────────────────────────────────────────────────────────────────
        // RNG (cheap splitmix; deterministic per-system but not per-faction)
        // ─────────────────────────────────────────────────────────────────

        private uint NextRandUint()
        {
            _rngState = unchecked(_rngState * 1103515245u + 12345u);
            return _rngState;
        }

        private float NextRandFloat01()
        {
            return (NextRandUint() & 0x00FFFFFF) / (float)0x01000000;
        }
    }
}
