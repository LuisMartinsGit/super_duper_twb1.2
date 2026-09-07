// ProductionQueueSystem.cs
// ONE clock per building. A unit, a technology or a level-up: whatever is at
// the head of the building's production queue runs, and nothing behind it
// starts until it is done (ProductionQueueComponents).
//
// This was ResearchSystem, and it ticked research only. Level-ups were ticked
// by BuildingUpgradeSystem off a BuildingUpgrading component stamped directly
// onto the building with no queue in front of it; training was TrainingSystem
// on its own TrainQueueItem buffer, and the Feraldis Longhouse had a third,
// BatchTrainingSystem. Three timers, three orderings, three displays — and a
// research queued at a Barracks ran in parallel with its training and showed
// up nowhere the training did. There is one of each now: BuildingUpgradeSystem
// keeps only the code that APPLIES a level, TrainingSystem only the code that
// prices a unit's time and spawns it.
//
// Flow, per building:
//   1. A command enqueues an item and pays for it (CommandRouter).
//   2. Idle + queue non-empty -> start the head: work out its duration and
//      stamp ProductionState.{Busy, Remaining, Total}. An upgrade ALSO gets
//      BuildingUpgrading, which is what stops it shooting while it works
//      (BuildingCombatSystem is WithNone on it) and what the world bar reads.
//   3. Tick. An upgrade's BuildingUpgrading.Progress is mirrored from the same
//      timer, so there is still exactly one clock.
//   4. Complete -> spawn the unit / mark the tech researched / apply the
//      level; pop the head. A unit the population cap will not admit holds
//      at Remaining = 0 until room frees.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Training;

namespace TheWaningBorder.Systems.Production
{
    // NOTE: No [BurstCompile] — uses managed types (TechCatalog,
    // FactionResearchState, string, Debug.Log).
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ProductionQueueSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every call
        // and never releases it. See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] QT_Production =
        {
            ComponentType.ReadOnly<ProductionState>(),
        };
        static CachedEntityQuery QC_Production;

        #endregion

        /// <summary>A unit whose training finished this tick. Spawned after
        /// the loop, because a spawn is a structural change.</summary>
        private struct PendingSpawn
        {
            public Entity Building;
            public FixedString64Bytes UnitId;
            public int Count;
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProductionState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var researchState = FactionResearchState.Instance;
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot: starting and finishing items makes STRUCTURAL changes
            // (BuildingUpgrading added/removed, a Barracks gaining its attack,
            // Feraldis raiders spawning), which a live query may not be
            // iterating over.
            var query = QC_Production.Get(em, QT_Production);
            using var ents = query.ToEntityArray(Allocator.Temp);

            // CompleteResearch fires OnTechCompleted, whose handlers
            // (TechEffectSystem) make structural changes of their own — so
            // completions are deferred past the whole loop, exactly as
            // ResearchSystem deferred them. Spawns are deferred for the same
            // reason, exactly as TrainingSystem deferred them.
            List<(Faction faction, string techId)> completed = null;
            List<PendingSpawn> spawns = null;
            // Population released by spawns THIS tick, per faction, so two
            // buildings finishing together cannot both squeeze past the cap.
            Dictionary<Faction, int> popThisTick = null;

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!em.Exists(e)) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (!em.HasBuffer<ProductionQueueItem>(e)) continue;

                var ps = em.GetComponentData<ProductionState>(e);

                if (ps.Busy == 0) StartHead(em, e, ref ps, researchState);
                else Tick(em, e, ref ps, dt, ref completed, ref spawns, ref popThisTick);
            }

            if (spawns != null)
                for (int i = 0; i < spawns.Count; i++)
                {
                    var s = spawns[i];
                    if (!em.Exists(s.Building)) continue;
                    string unitId = s.UnitId.ToString();
                    for (int c = 0; c < s.Count; c++)
                        TrainingSystem.SpawnUnit(em, s.Building, unitId);
                }

            if (completed != null)
                foreach (var (faction, techId) in completed)
                    researchState.CompleteResearch(faction, techId);
        }

        // ── Gates ──────────────────────────────────────────────────────────

        /// <summary>
        /// Whether the head item may run right now. These were the WithNone
        /// filters of the two old systems, kept per KIND so the merge changes
        /// no rule: Heavy Bureaucracy (Antiquity) halts research AND training
        /// but not a level-up (docs/Design/Sects.md section 4); a Hall
        /// mid-age-up cannot train. A building that is merely gated holds its
        /// timer where it is.
        /// </summary>
        private static bool HeadMayRun(EntityManager em, Entity e, ProductionKind kind)
        {
            switch (kind)
            {
                case ProductionKind.Research:
                    return !em.HasComponent<SectShutdown>(e);
                case ProductionKind.Train:
                    return !em.HasComponent<SectShutdown>(e) && !em.HasComponent<AgeUpState>(e);
                default:
                    return true;
            }
        }

        // ── Start ──────────────────────────────────────────────────────────

        private static void StartHead(EntityManager em, Entity e, ref ProductionState ps,
            FactionResearchState researchState)
        {
            var queue = em.GetBuffer<ProductionQueueItem>(e);
            if (queue.Length == 0) return;

            var item = queue[0];
            if (!HeadMayRun(em, e, item.Kind)) return;

            var faction = em.HasComponent<FactionTag>(e)
                ? em.GetComponentData<FactionTag>(e).Value : default;

            switch (item.Kind)
            {
                case ProductionKind.Research:
                {
                    string techId = item.Id.ToString();

                    // Already researched (queued twice on two buildings), or
                    // gone from the catalog: drop it. No refund — a tech that
                    // completed elsewhere delivered what was paid for.
                    if (researchState != null && researchState.HasResearched(faction, techId))
                    {
                        queue.RemoveAt(0);
                        return;
                    }
                    if (!TechCatalog.TryGetTechnology(techId, out var techDef))
                    {
                        queue.RemoveAt(0);
                        return;
                    }

                    float researchTime = techDef.researchTime > 0 ? techDef.researchTime : 30f;

                    // Librarians' wing (Fiendstone Keep): all research 20%
                    // faster faction-wide.
                    if (ChoiceUpgradeQuery.FactionHasWing(em, faction, KeepWingType.Librarians))
                        researchTime /= KeepWingConfig.LibrariansResearchSpeed;

                    // Royal Index (Antiquity): all research 30% faster.
                    researchTime *= SectResearchEffects.ResearchTimeMultiplier(faction);

                    Begin(em, e, ref ps, researchTime);
                    return;
                }

                case ProductionKind.Train:
                {
                    string unitId = item.Id.ToString();
                    if (!TechCatalog.TryGetUnit(unitId, out _))
                    {
                        // Unknown unit — the same silent drop the old system
                        // did.
                        queue.RemoveAt(0);
                        return;
                    }
                    Begin(em, e, ref ps, TrainingSystem.TrainDuration(em, e, unitId, faction));
                    return;
                }

                default: // BuildingUpgrade
                {
                    byte current = em.HasComponent<BuildingUpgradeState>(e)
                        ? em.GetComponentData<BuildingUpgradeState>(e).Level : (byte)0;

                    // The level moved while this item waited — an age-up
                    // auto-level or a scenario promotion. Applying it anyway
                    // would skip or repeat a tier, so drop it and hand the
                    // money back at the price it was charged (that is what
                    // Level records).
                    if (item.Level != current + 1 || current >= BuildingUpgradeConfig.MaxLevel)
                    {
                        TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper
                            .RefundQueued(em, e, item.Level);
                        queue.RemoveAt(0);
                        return;
                    }

                    float duration = BuildingUpgradeConfig.GetUpgradeDuration(
                        TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, e),
                        item.Level);

                    Begin(em, e, ref ps, duration);

                    // The gate every other system reads. Added here rather
                    // than at queue time so a building that is merely WAITING
                    // to upgrade can still shoot.
                    em.AddComponentData(e, new BuildingUpgrading
                    {
                        Progress = 0f,
                        Total = duration,
                        TargetLevel = item.Level,
                    });
                    return;
                }
            }
        }

        private static void Begin(EntityManager em, Entity e, ref ProductionState ps, float duration)
        {
            ps.Busy = 1;
            ps.Remaining = duration;
            ps.Total = duration;
            em.SetComponentData(e, ps);
        }

        // ── Tick ───────────────────────────────────────────────────────────

        private static void Tick(EntityManager em, Entity e, ref ProductionState ps, float dt,
            ref List<(Faction, string)> completed, ref List<PendingSpawn> spawns,
            ref Dictionary<Faction, int> popThisTick)
        {
            var queue = em.GetBuffer<ProductionQueueItem>(e);
            if (queue.Length == 0)
            {
                // Busy with nothing queued — a cancel raced the tick. Reset
                // rather than counting a timer nobody will collect.
                Idle(em, e, ref ps);
                if (em.HasComponent<BuildingUpgrading>(e)) em.RemoveComponent<BuildingUpgrading>(e);
                return;
            }

            var item = queue[0];
            if (!HeadMayRun(em, e, item.Kind)) return;   // frozen where it stands

            ps.Remaining -= dt;

            // One clock: the upgrade's own component follows this timer rather
            // than keeping a second one. FloatingHealthBars and the actions
            // grid's radial sweep both read it.
            if (item.Kind == ProductionKind.BuildingUpgrade
                && em.HasComponent<BuildingUpgrading>(e))
            {
                var up = em.GetComponentData<BuildingUpgrading>(e);
                up.Progress = ps.Total - ps.Remaining;
                up.Total = ps.Total;
                em.SetComponentData(e, up);
            }

            if (ps.Remaining > 0f)
            {
                em.SetComponentData(e, ps);
                return;
            }

            // ── Complete ───────────────────────────────────────────────────
            var faction = em.HasComponent<FactionTag>(e)
                ? em.GetComponentData<FactionTag>(e).Value : default;

            switch (item.Kind)
            {
                case ProductionKind.Research:
                    (completed ??= new()).Add((faction, item.Id.ToString()));
                    break;

                case ProductionKind.Train:
                    if (!TryReleaseTrained(em, e, faction, ref spawns, ref popThisTick))
                    {
                        // No room. Hold here — Busy stays 1, the item stays
                        // at the head, paid for — and try again next tick.
                        ps.Remaining = 0f;
                        em.SetComponentData(e, ps);
                        return;
                    }
                    // TryReleaseTrained popped the item(s) itself.
                    Idle(em, e, ref ps);
                    return;

                default: // BuildingUpgrade
                    // Structural: may add a Barracks' ranged attack and spawn
                    // Feraldis raiders, so the buffer is re-fetched below.
                    TheWaningBorder.Systems.Buildings.BuildingUpgradeSystem
                        .CompleteUpgrade(em, e, item.Level);
                    if (em.HasComponent<BuildingUpgrading>(e)) em.RemoveComponent<BuildingUpgrading>(e);
                    break;
            }

            em.GetBuffer<ProductionQueueItem>(e).RemoveAt(0);
            Idle(em, e, ref ps);
        }

        private static void Idle(EntityManager em, Entity e, ref ProductionState ps)
        {
            ps.Busy = 0;
            ps.Remaining = 0f;
            ps.Total = 0f;
            em.SetComponentData(e, ps);
        }

        /// <summary>
        /// A finished unit leaves the queue if the faction has room for it.
        /// Endless Muster (War) gives the building two production lines, so a
        /// completed cycle releases the next queued UNIT alongside the first —
        /// a tech or a level-up behind it is not a second line and stays put.
        /// Both units have to fit, or neither is released: a half-satisfied
        /// cycle would leave the second one paid for and gone.
        /// </summary>
        private static bool TryReleaseTrained(EntityManager em, Entity e, Faction faction,
            ref List<PendingSpawn> spawns, ref Dictionary<Faction, int> popThisTick)
        {
            var queue = em.GetBuffer<ProductionQueueItem>(e);

            string unitId = queue[0].Id.ToString();
            int count = TrainingSystem.SpawnCount(em, e, faction, unitId);
            int requiredPop = PopulationHelper.GetUnitPopulationCost(unitId) * count;

            int released = 1;
            string secondId = null;
            int secondCount = 0;
            if (SectResearchEffects.ConcurrentTrainingSlots(faction) > 1
                && queue.Length > 1 && queue[1].Kind == ProductionKind.Train)
            {
                secondId = queue[1].Id.ToString();
                secondCount = TrainingSystem.SpawnCount(em, e, faction, secondId);
                requiredPop += PopulationHelper.GetUnitPopulationCost(secondId) * secondCount;
                released = 2;
            }

            int extra = 0;
            popThisTick?.TryGetValue(faction, out extra);
            if (!TrainingSystem.HasPopulationCapacity(em, faction, requiredPop, extra))
                return false;

            queue.RemoveAt(0);
            if (released > 1) queue.RemoveAt(0);

            spawns ??= new List<PendingSpawn>();
            spawns.Add(new PendingSpawn { Building = e, UnitId = new FixedString64Bytes(unitId), Count = count });
            if (released > 1)
                spawns.Add(new PendingSpawn { Building = e, UnitId = new FixedString64Bytes(secondId), Count = secondCount });

            (popThisTick ??= new Dictionary<Faction, int>())[faction] = extra + requiredPop;
            return true;
        }
    }
}
