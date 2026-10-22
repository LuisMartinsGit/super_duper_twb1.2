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
//      at Remaining = 0 until room frees — BUT (2026-10-11) the queue does
//      not stall behind it: the first research or level-up queued after it
//      is moved to the head and runs, and no unit trains until that is done.
//      The held unit is marked Trained, so when it is back at the head it
//      only waits for room; it never trains twice.
//
// PERF (2026-10-15), both result-identical:
//   * Idle buildings with an empty queue (most of them, late game) are
//     skipped by a chunk-level read of ProductionState + the queue length,
//     instead of an Exists / HasComponent / HasBuffer / GetComponentData /
//     GetBuffer round per building per tick. The survivors are visited in
//     exactly the order ToEntityArray gave (chunk order).
//   * A finished unit held at the population cap asks for room every tick;
//     each ask copied the whole population query twice. The population rows
//     are now read once per update and re-read after anything that can move
//     them (a level-up completing) — spawns are deferred past the loop, so
//     nothing else inside it changes a FactionPopulation.

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

            /// <summary>Hero revival level (0 = not a revival). Carried from
            /// the queue item, which recorded what the player paid for —
            /// docs/Design/Heroes.md §4.</summary>
            public byte HeroLevel;
        }

        private EntityTypeHandle _entityH;
        private ComponentTypeHandle<ProductionState> _stateH;
        private BufferTypeHandle<ProductionQueueItem> _queueH;

        static readonly ComponentType[] QT_Population =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<TheWaningBorder.Economy.FactionPopulation>(),
        };
        static CachedEntityQuery QC_Population;

        // Population rows for this update (TrainingSystem.HasPopulationCapacity's
        // input), in query order; invalid until first needed.
        private static FactionTag[] _popFac = System.Array.Empty<FactionTag>();
        private static TheWaningBorder.Economy.FactionPopulation[] _popRows =
            System.Array.Empty<TheWaningBorder.Economy.FactionPopulation>();
        private static int _popCount;
        private static bool _popValid;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProductionState>();
            _entityH = state.GetEntityTypeHandle();
            _stateH = state.GetComponentTypeHandle<ProductionState>(true);
            _queueH = state.GetBufferTypeHandle<ProductionQueueItem>(true);
        }

        /// <summary>Exactly TrainingSystem.HasPopulationCapacity, on rows read
        /// once per update (re-read after <see cref="_popValid"/> is cleared).</summary>
        private static bool HasPopulationCapacity(EntityManager em, Faction faction,
            int requiredPop, int extraSpawnedThisTick)
        {
            if (!_popValid)
            {
                var q = QC_Population.Get(em, QT_Population);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var pops = q.ToComponentDataArray<TheWaningBorder.Economy.FactionPopulation>(Allocator.Temp);
                if (_popFac.Length < facs.Length)
                {
                    _popFac = new FactionTag[facs.Length];
                    _popRows = new TheWaningBorder.Economy.FactionPopulation[facs.Length];
                }
                for (int i = 0; i < facs.Length; i++) { _popFac[i] = facs[i]; _popRows[i] = pops[i]; }
                _popCount = facs.Length;
                _popValid = true;
            }
            for (int i = 0; i < _popCount; i++)
            {
                if (_popFac[i].Value != faction) continue;
                return _popRows[i].Current + extraSpawnedThisTick + requiredPop <= _popRows[i].Max;
            }
            return true;
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
            _popValid = false;

            // Only buildings with something to do: busy, or a non-empty
            // queue. An idle building with an empty queue was a no-op below
            // (StartHead returns on an empty queue), so dropping it here
            // changes nothing; the rest keep their chunk order.
            _entityH.Update(ref state);
            _stateH.Update(ref state);
            _queueH.Update(ref state);
            query.CompleteDependency();
            var ents = new NativeList<Entity>(Allocator.Temp);
            using (var chunks = query.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    if (!chunk.Has(ref _queueH)) continue;   // HasBuffer failed for every row
                    var ce = chunk.GetNativeArray(_entityH);
                    var cs = chunk.GetNativeArray(ref _stateH);
                    var cq = chunk.GetBufferAccessor(ref _queueH);
                    for (int i = 0; i < chunk.Count; i++)
                        if (cs[i].Busy != 0 || cq[i].Length > 0) ents.Add(ce[i]);
                }
            }

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
                        TrainingSystem.SpawnUnit(em, s.Building, unitId, s.HeroLevel);
                }

            if (completed != null)
                foreach (var (faction, techId) in completed)
                    researchState.CompleteResearch(faction, techId);
            ents.Dispose();
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
                    return !TransientState.Active<SectShutdown>(em, e);
                case ProductionKind.Train:
                    return !TransientState.Active<SectShutdown>(em, e) && !em.HasComponent<AgeUpState>(e);
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
                    // A building on cursed ground works slower (Territory_Claims.md §6.9.9).
                    researchTime /= TheWaningBorder.Systems.Border.CursedGroundDecay.WorkSpeed(em, e);

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
                    float trainTime = TrainingSystem.TrainDuration(em, e, unitId, faction, item.Level);
                    Begin(em, e, ref ps, trainTime);
                    if (item.Trained != 0)
                    {
                        // Trained before a research overtook it: it is done,
                        // and only needs room to leave (shown as complete).
                        ps.Remaining = 0f;
                        em.SetComponentData(e, ps);
                    }
                    return;
                }

                default: // BuildingUpgrade
                {
                    // The FACTION's level for this type (2026-10-19 — levels
                    // are faction-wide per type, FactionBuildingLevels).
                    byte current = TheWaningBorder.Systems.Buildings.FactionBuildingLevels.LevelFor(em, e);

                    // The level moved while this item waited — an age-up
                    // auto-level, a scenario promotion, or (an old save) the
                    // same level finished on another building of the type. Applying it anyway
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
                        em, em.GetComponentData<FactionTag>(e).Value,
                        TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, e),
                        item.Level);

                    Begin(em, e, ref ps, duration);

                    // The gate every other system reads. Added here rather
                    // than at queue time so a building that is merely WAITING
                    // to upgrade can still shoot.
                    TransientState.Set(em, e, new BuildingUpgrading
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
                TransientState.Clear<BuildingUpgrading>(em, e);
                return;
            }

            var item = queue[0];
            if (!HeadMayRun(em, e, item.Kind)) return;   // frozen where it stands

            // Overtime (Prosperity) runs a building's clock faster.
            ps.Remaining -= dt * TheWaningBorder.Systems.Sect.AlanthorSectEconomy.ProductionSpeed(em, e);

            // One clock: the upgrade's own component follows this timer rather
            // than keeping a second one. FloatingHealthBars and the actions
            // grid's radial sweep both read it.
            if (item.Kind == ProductionKind.BuildingUpgrade
                && TransientState.Active<BuildingUpgrading>(em, e))
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
                        // No room. The unit is finished: mark it, and let the
                        // first research / level-up behind it go first.
                        if (PromoteNonUnitPastHeldUnit(em, e))
                        {
                            Idle(em, e, ref ps);   // StartHead runs it next tick
                            return;
                        }
                        // Nothing to run instead. Hold here — Busy stays 1,
                        // the item stays at the head, paid for — and try again
                        // next tick.
                        ps.Remaining = 0f;
                        em.SetComponentData(e, ps);
                        return;
                    }
                    // TryReleaseTrained popped the item(s) itself.
                    Idle(em, e, ref ps);
                    return;

                default: // BuildingUpgrade
                    // Structural: may add a Barracks' ranged attack. Applies
                    // the level to EVERY building of the type the faction
                    // owns (FactionBuildingLevels), so the buffer is
                    // re-fetched below.
                    TheWaningBorder.Systems.Buildings.BuildingUpgradeSystem
                        .CompleteUpgrade(em, e, item.Level);
                    _popValid = false;   // a level can move a population cap
                    TransientState.Clear<BuildingUpgrading>(em, e);
                    break;
            }

            em.GetBuffer<ProductionQueueItem>(e).RemoveAt(0);
            Idle(em, e, ref ps);
        }

        /// <summary>
        /// The head is a finished unit the population cap will not admit.
        /// Mark it Trained and move the first research or level-up queued
        /// behind it to the head, so the building keeps working; every unit
        /// waits until that item is done (it is the head now). False — and
        /// nothing moved — when no such item is queued, or it may not run
        /// right now (Heavy Bureaucracy halts research).
        /// </summary>
        private static bool PromoteNonUnitPastHeldUnit(EntityManager em, Entity e)
        {
            var queue = em.GetBuffer<ProductionQueueItem>(e);
            if (queue.Length < 2 || queue[0].Kind != ProductionKind.Train) return false;

            var head = queue[0];
            head.Trained = 1;
            queue[0] = head;

            for (int j = 1; j < queue.Length; j++)
            {
                var item = queue[j];
                if (item.Kind == ProductionKind.Train) continue;
                if (!HeadMayRun(em, e, item.Kind)) return false;
                queue.RemoveAt(j);
                queue.Insert(0, item);
                return true;
            }
            return false;
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
            byte headLevel = queue[0].Level;   // hero revival level, 0 otherwise
            byte secondLevel = 0;
            int count = TrainingSystem.SpawnCount(em, e, faction, unitId);
            int requiredPop = PopulationHelper.GetUnitPopulationCost(unitId) * count;

            int released = 1;
            string secondId = null;
            int secondCount = 0;
            if (SectResearchEffects.ConcurrentTrainingSlots(faction) > 1
                && queue.Length > 1 && queue[1].Kind == ProductionKind.Train)
            {
                secondId = queue[1].Id.ToString();
                secondLevel = queue[1].Level;
                secondCount = TrainingSystem.SpawnCount(em, e, faction, secondId);
                requiredPop += PopulationHelper.GetUnitPopulationCost(secondId) * secondCount;
                released = 2;
            }

            int extra = 0;
            popThisTick?.TryGetValue(faction, out extra);
            if (!HasPopulationCapacity(em, faction, requiredPop, extra))
            {
                NoticePopulationHold(em, faction);
                return false;
            }

            queue.RemoveAt(0);
            if (released > 1) queue.RemoveAt(0);

            spawns ??= new List<PendingSpawn>();
            spawns.Add(new PendingSpawn
            {
                Building = e, UnitId = new FixedString64Bytes(unitId), Count = count,
                HeroLevel = headLevel,
            });
            if (released > 1)
                spawns.Add(new PendingSpawn
                {
                    Building = e, UnitId = new FixedString64Bytes(secondId), Count = secondCount,
                    HeroLevel = secondLevel,
                });

            (popThisTick ??= new Dictionary<Faction, int>())[faction] = extra + requiredPop;
            return true;
        }

        /// <summary>Real time of the next population-hold notice.</summary>
        private static float _nextPopNotice;

        /// <summary>
        /// A finished unit held at the door because the population cap is
        /// full used to wait in silence. Tell the LOCAL player, at most every
        /// 15 real seconds. Presentation only — no sim state changes.
        /// </summary>
        private static void NoticePopulationHold(EntityManager em, Faction faction)
        {
            if (faction != GameSettings.LocalPlayerFaction || GameSettings.IsSpectating) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextPopNotice) return;
            _nextPopNotice = now + 15f;
            string counts = "";
            if (FactionEconomy.TryGetBank(em, faction, out var bank) && em.HasComponent<FactionPopulation>(bank))
            {
                var pop = em.GetComponentData<FactionPopulation>(bank);
                counts = " (" + pop.Current + "/" + pop.Max + ")";
            }
            SimSignals.NotifyError(string.Format(
                TheWaningBorder.Core.Localization.Loc.T("Training waits: population cap reached{0} - build more housing"),
                counts));
        }
    }
}
