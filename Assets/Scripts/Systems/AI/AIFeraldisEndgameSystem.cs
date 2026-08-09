// AIFeraldisEndgameSystem.cs
// The Feraldis late game, mirroring AIAlanthorEndgameSystem's shape.
//
// Alanthor's endgame is "fortify and purify". Feraldis's is the opposite:
// build the raiding economy, then CRACK WELLS OPEN and smash them. Destroy
// every well at once and the match is won outright (NodeVictorySystem
// already awards that instantly for a Feraldis destroyer).
//
// Phases per think tick:
//   1. Strategy latch (HasAgedUp, pressure flip)
//   2. Economy spine   — Raider Camps are the whole Feraldis income
//   3. Age-2 buildings — Pasture / Thrower Camp / Temple
//   4. Temple leveling — the Corruptor needs Temple Lv 3
//   5. THE VERB       — train a Corruptor, send it at a well, escort it,
//                        and commit the army onto a cracked well
//
// It deliberately does NOT duplicate SimpleAISystem's job (workers, basic
// army, research) — that keeps running underneath, exactly as it does for
// Alanthor.
//
// Location: Assets/Scripts/Systems/AI/AIFeraldisEndgameSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    /// <summary>Per-brain think throttle. Its OWN component — sharing
    /// AIAlanthorTickState would have the two endgame systems stealing each
    /// other's ticks in a mixed match.</summary>
    public struct AIFeraldisTickState : IComponentData
    {
        public float NextThinkTime;

        /// <summary>Game time the AI first wanted to send its Corruptor but
        /// had no escort. 0 = not currently holding. Drives the patience
        /// timeout in TryRunTheVerb so a hold can never be permanent.</summary>
        public float CorruptorHeldSince;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        private const float ThinkInterval = 5f;

        /// <summary>Raider Camps the AI wants standing — its entire economy.</summary>
        private const int TargetRaiderCamps = 4;

        /// <summary>Army it wants before committing to a well assault.</summary>
        private const int AssaultArmySize = 12;

        /// <summary>How far from the well the escort gathers.</summary>
        private const float AssaultRange = 200f;

        /// <summary>Units that must be moving on the well before the AI will
        /// commit a Corruptor to the walk. Below this it keeps the ritualist
        /// home rather than feeding it to the curse.</summary>
        private const int MinEscortBeforeDispatch = 4;

        /// <summary>
        /// How long the AI will wait for that escort before going anyway.
        ///
        /// The escort gate was written when a lone ritualist crossing a map at
        /// 60-90 % curse died on the way. It has since become an ABSOLUTE
        /// block: the 2026-08-07 skirmish had Blue sit on `escort 0/4` for the
        /// last three minutes of the match with military 0 — an army it was
        /// never going to have, guarding a walk that is no longer dangerous
        /// (well dormancy holds that map at 1.6 % curse, so the route is
        /// empty). Waiting forever for an escort is strictly worse than an
        /// unescorted attempt on an uncontested well.
        ///
        /// So the gate becomes patience, not a veto: prefer an escort, but
        /// after this long, go.
        /// </summary>
        private const float MaxEscortWaitSeconds = 45f;

        /// <summary>Radius (m) of the escort ring around a well. Comfortably
        /// outside CorruptCancelRange (10 m) so the screen can never jostle
        /// its own ritualist out of its channel, while staying tight enough to
        /// intercept the defenders the well spawns at it.</summary>
        private const float EscortStandoffRadius = 14f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Host authority only — the same guard the Alanthor sibling uses.
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;

            var em = state.EntityManager;
            float now = (float)SystemAPI.Time.ElapsedTime;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Snapshot brains before any structural change.
            var brainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AIBrain>());
            using var brains = brainQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < brains.Length; i++)
            {
                var brainEntity = brains[i];
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;
                var faction = brain.Owner;

                // Throttle, staggered per faction so eight AIs don't all
                // think on the same frame.
                if (!em.HasComponent<AIFeraldisTickState>(brainEntity))
                {
                    em.AddComponentData(brainEntity, new AIFeraldisTickState
                    {
                        NextThinkTime = now + ThinkInterval + (int)faction * (ThinkInterval / 8f)
                    });
                    continue;
                }
                var tick = em.GetComponentData<AIFeraldisTickState>(brainEntity);
                if (now < tick.NextThinkTime) continue;
                tick.NextThinkTime = now + ThinkInterval;
                em.SetComponentData(brainEntity, tick);

                // Culture + era gate — the fork point from the Alanthor sibling.
                if (!TryGetHall(em, faction, out float3 hallPos, out byte culture)) continue;
                if (culture != Cultures.Feraldis) continue;
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;
                if (!em.HasComponent<FactionEra>(bank)) continue;
                if (em.GetComponentData<FactionEra>(bank).Value < 2) continue;

                if (em.HasComponent<AIStrategyState>(brainEntity))
                {
                    var ss = em.GetComponentData<AIStrategyState>(brainEntity);
                    if (ss.HasAgedUp == 0) { ss.HasAgedUp = 1; em.SetComponentData(brainEntity, ss); }
                }

                ConscriptSurplusWorkers(em, faction, hallPos);
                TryBuildMine(em, faction, hallPos);
                TryBuildEconomy(em, faction, hallPos);
                TryPlantTotem(em, faction, hallPos);
                TryBuildAge2(em, faction, hallPos);
                TryLevelTemple(em, faction);
                TryAdoptSect(em, faction);
                TryRunTheVerb(em, brainEntity, faction, hallPos, now);
            }

            sw.Stop();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                "AIFeraldisEndgame", sw.Elapsed.TotalMilliseconds);
        }

        // ---------------------------------------------------------------

        private static bool TryGetHall(EntityManager em, Faction faction,
            out float3 hallPos, out byte culture)
        {
            hallPos = default; culture = Cultures.None;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                culture = em.GetComponentData<FactionProgress>(ents[i]).Culture;
                hallPos = em.GetComponentData<LocalTransform>(ents[i]).Position;
                return true;
            }
            return false;
        }

        /// <summary>Builders a Feraldis AI keeps back for base expansion.
        /// Everyone else is a soldier.</summary>
        private const int KeepBuilders = 2;

        /// <summary>
        /// THE AGE-UP REWARD. Feraldis Workers cannot gather, so every worker
        /// beyond a small build crew is dead weight standing in the base —
        /// which is exactly what the 2026-08-05 match showed: yards full of
        /// idle workers and `military 0`.
        ///
        /// At age-up that surplus becomes a FREE ARMY instead. They are only
        /// light infantry (110 HP / 9 dmg), individually much worse than a
        /// trained Spearman, but they arrive instantly and for nothing — a
        /// weak rush force that is far better than the nothing Feraldis
        /// otherwise has the moment it ages up.
        ///
        /// They keep CanBuild, so they can still be pulled back to build; and
        /// FeraldisCultureRetrofitSystem has already stripped
        /// PassiveWorkerTag, so they hold ground and fight on their own.
        /// </summary>
        private static void ConscriptSurplusWorkers(EntityManager em, Faction faction, float3 hallPos)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FeraldisWorkerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);

            // Collect this faction's workers that are not already soldiering.
            var idle = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<ConscriptedTag>(ents[i])) continue;
                idle.Add(ents[i]);
            }

            // Leave the build crew behind.
            int conscript = idle.Length - KeepBuilders;
            if (conscript <= 0) { idle.Dispose(); return; }

            // Send them at whatever the AI is already fighting — the threat
            // hint if there is one, otherwise simply outward from the Hall so
            // they stop loitering in the base.
            float3 rally = hallPos;
            var sk = FindSharedKnowledge(em, faction);
            if (sk.EnemyLastSeenTime > 0f) rally = sk.EnemyLastKnownPosition;

            int sent = 0;
            for (int i = 0; i < idle.Length && sent < conscript; i++)
            {
                var w = idle[i];
                // Never conscript one that is mid-build.
                if (em.HasComponent<BuildCommand>(w)) continue;
                em.AddComponent<ConscriptedTag>(w);
                AttackMoveCommandHelper.Execute(em, w, rally);
                sent++;
            }
            if (sent > 0)
                AILogger.Log(faction, "MILITARY",
                    $"conscripted {sent} surplus Worker(s) as light infantry");
            idle.Dispose();
        }

        private static AISharedKnowledge FindSharedKnowledge(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<AISharedKnowledge>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<AIBrain>(ents[i]).Owner == faction)
                    return em.GetComponentData<AISharedKnowledge>(ents[i]);
            return default;
        }

        /// <summary>
        /// MINES ARE THE ONLY FERALDIS ORE. Feraldis Workers cannot gather,
        /// so without a Mine the faction's iron and veilstone stay at zero
        /// forever — which is exactly what the 2026-08-05 match showed: both
        /// Feraldis AIs ended on 13k-23k supplies and 0-1 iron, unable to
        /// afford a single building. This runs FIRST for that reason.
        /// </summary>
        private static void TryBuildMine(EntityManager em, Faction faction, float3 hallPos)
        {
            const int TargetMines = 2;
            if (CountFactionWith<MineTag>(em, faction) >= TargetMines) return;
            if (!BuildCosts.TryGet("Mine", out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;

            // A Mine only pays out next to ore, so search from the patches
            // rather than ringing the Hall.
            if (!TryFindOrePatch(em, hallPos, out float3 patch)) return;

            var size = BuildingSizeConfig.GetSize("Mine");
            if (!TryFindSpot(em, patch, size, 4f, 14f, out float3 pos)) return;
            if (!FactionEconomy.Spend(em, faction, cost)) return;
            bool queued = CommandRouter.IssuePlaceBuilding(em, "Mine", pos, faction,
                out Entity site, CommandSource.AI);
            if (queued)
            {
                DispatchBuilders(em, faction, Entity.Null, "Mine", pos);
                AILogger.Log(faction, "BUILDING", $"Mine queued on ore at ({pos.x:0},{pos.z:0})");
                return;
            }
            if (site == Entity.Null) { FactionEconomy.Add(em, faction, cost); return; }
            if (DispatchBuilders(em, faction, site, "Mine", pos) == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(site);
                return;
            }
            AILogger.Log(faction, "BUILDING", $"Mine placed on ore at ({pos.x:0},{pos.z:0})");
        }

        /// <summary>
        /// Nearest ore patch, IRON FIRST.
        ///
        /// The first version took whichever node was nearest of either kind,
        /// and the Mine diagnostic caught the result immediately: both of
        /// Blue's Mines reported "0 iron + 10 veilstone node(s) in range".
        /// They worked — just not on the resource the faction was starving
        /// for. Iron gates every building and unit; veilstone gates far less.
        /// So iron patches win outright, and veilstone is only a fallback
        /// when there is no reachable iron at all.
        /// </summary>
        private static bool TryFindOrePatch(EntityManager em, float3 hallPos, out float3 patch)
        {
            patch = default;

            if (TryNearestOf<IronMineTag>(em, hallPos, out patch)) return true;
            return TryNearestOf<VeilstoneOutcroppingTag>(em, hallPos, out patch);
        }

        private static bool TryNearestOf<T>(EntityManager em, float3 from, out float3 pos)
            where T : unmanaged, IComponentData
        {
            pos = default;
            float best = float.MaxValue;
            bool found = false;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                float d = math.distancesq(xfs[i].Position, from);
                if (d < best) { best = d; pos = xfs[i].Position; found = true; }
            }
            return found;
        }

        /// <summary>
        /// War Totems are Feraldis's ONLY territory — its ordinary buildings
        /// project no influence at all. The match log showed both Feraldis
        /// AIs at 0.0-2.1 % influence and the curse at 47 %: with no totems
        /// they had no curse suppression anywhere outside the Hall ring.
        /// Totems can only be planted on blood, so this looks for a pool.
        /// </summary>
        private static void TryPlantTotem(EntityManager em, Faction faction, float3 hallPos)
        {
            const int TargetTotems = 3;
            if (CountFactionWith<WarTotemTag>(em, faction) >= TargetTotems) return;
            if (!BuildCosts.TryGet("Feraldis_WarTotem", out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (!TheWaningBorder.Influence.BloodMap.Ready) return;

            if (!TryFindBloodSpot(em, faction, hallPos, out float3 spot)) return;

            var size = BuildingSizeConfig.GetSize("Feraldis_WarTotem");
            if (!BuildCommandHelper.IsValidBuildPosition(em, spot, size)) return;
            if (!FactionEconomy.Spend(em, faction, cost)) return;
            bool tQueued = CommandRouter.IssuePlaceBuilding(em, "Feraldis_WarTotem", spot, faction,
                out Entity site, CommandSource.AI);
            if (tQueued)
            {
                DispatchBuilders(em, faction, Entity.Null, "Feraldis_WarTotem", spot);
                AILogger.Log(faction, "BUILDING", $"War Totem queued on blood at ({spot.x:0},{spot.z:0})");
                return;
            }
            if (site == Entity.Null) { FactionEconomy.Add(em, faction, cost); return; }
            if (DispatchBuilders(em, faction, site, "Feraldis_WarTotem", spot) == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(site);
                return;
            }
            AILogger.Log(faction, "BUILDING", $"War Totem planted on blood at ({spot.x:0},{spot.z:0})");
        }

        /// <summary>
        /// Strongest blood ANYWHERE ON THE MAP, scanned off the blood grid
        /// itself rather than by ringing the Hall.
        ///
        /// The ring-the-Hall version never once planted a totem across five
        /// playtests, and the reason is structural: blood accumulates where
        /// fighting happens, fighting happens away from home, and blood
        /// inside your own influence FADES. So the one place guaranteed not
        /// to have a pool is the ground around your Hall. Reading the grid
        /// directly finds the battlefield instead.
        /// </summary>
        private static bool TryFindBloodSpot(EntityManager em, Faction faction,
            float3 hallPos, out float3 spot)
        {
            spot = default;
            if (!TheWaningBorder.Influence.PlayerInfluenceMap.Ready) return false;

            // Existing totems, so we don't stack. The 2026-08-06 match planted
            // TWENTY-SIX totems on the single cell (83,195): the scan returns
            // the global blood maximum, that maximum does not move, and every
            // totem placed there died before its builders arrived — so the
            // count never rose, and the AI re-placed on the same corpse pile
            // forever.
            var existing = new NativeList<float3>(Allocator.Temp);
            var tq = em.CreateEntityQuery(
                ComponentType.ReadOnly<WarTotemTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var ents = tq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                    if (em.GetComponentData<FactionTag>(ents[i]).Value == faction)
                        existing.Add(em.GetComponentData<LocalTransform>(ents[i]).Position);

            float best = TheWaningBorder.Core.Config.FeraldisConstants.TotemPlacementBloodThreshold;
            bool found = false;

            var wMin = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
            var wSize = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
            int res = TheWaningBorder.Influence.BloodMap.Resolution;
            float cellW = wSize.x / res;
            float cellH = wSize.y / res;

            // Row-major walk = deterministic tie-breaking for lockstep.
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float b = TheWaningBorder.Influence.BloodMap.CellValue(x, y);
                    if (b <= best) continue;

                    float wx = wMin.x + (x + 0.5f) * cellW;
                    float wz = wMin.y + (y + 0.5f) * cellH;

                    // Never re-plant on top of a totem we already have.
                    bool crowded = false;
                    for (int e = 0; e < existing.Length && !crowded; e++)
                    {
                        float ex = existing[e].x - wx, ez = existing[e].z - wz;
                        crowded = ex * ex + ez * ez < MinTotemSpacing * MinTotemSpacing;
                    }
                    if (crowded) continue;

                    best = b;
                    spot = new float3(wx, TerrainUtility.GetHeight(wx, wz), wz);
                    found = true;
                }
            }
            existing.Dispose();
            return found;
        }

        /// <summary>Totems must tile new ground, not stack on the bloodiest
        /// cell. Comfortably wider than a totem's own burn radius.</summary>
        private const float MinTotemSpacing = 36f;

        /// <summary>
        /// Adopt a sect. Two bugs made the first version spam
        /// "adopting sect War" 80 times in one match without ever adopting:
        ///   1. It used bare ids ("War"), but the real ids are PREFIXED
        ///      ("Sect_War" — SectConfig.War), so nothing ever matched.
        ///   2. It called only CommandRouter.IssueSectAdoption, which is the
        ///      REPLICATION STAMP. The thing that actually performs an
        ///      adoption is SectAdoption.TryStartAdoption; without it
        ///      IsAdopted stayed false forever and the AI retried every tick.
        /// Priority follows the Feraldis cluster.
        /// </summary>
        private static void TryAdoptSect(EntityManager em, Faction faction)
        {
            Entity temple = Entity.Null;
            var tq = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var ents = tq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                    if (em.GetComponentData<FactionTag>(ents[i]).Value == faction)
                    { temple = ents[i]; break; }
            if (temple == Entity.Null) return;

            for (int i = 0; i < FeraldisSectPriority.Length; i++)
            {
                string sectId = FeraldisSectPriority[i];
                if (SectQuery.IsAdopted(em, faction, sectId)) continue;
                if (!BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)) continue;

                if (!FactionEconomy.TryGetResources(em, faction, out var res)) return;
                if (res.Supplies < chapelCost.Supplies + ChapelReserveSupplies) return;
                if (res.Veilstone < chapelCost.Veilstone + ChapelReserveVeilstone) return;

                var result = SectAdoption.TryStartAdoption(em, faction, sectId, chapelCost, temple);
                if (result == SectAdoptionResult.Ok)
                {
                    CommandRouter.IssueSectAdoption(em, temple, sectId, -1, 30f, CommandSource.AI);
                    AILogger.Log(faction, "STRATEGY", $"adopting sect {sectId}");
                    return;
                }
                if (result == SectAdoptionResult.NotEnoughRP) return;   // wait for RP
                // slot full / already adopted -> try the next priority
            }
        }

        private const int ChapelReserveSupplies = 100;
        private const int ChapelReserveVeilstone = 40;

        /// <summary>Feraldis sect cluster, in preference order. IDs are the
        /// PREFIXED SectConfig constants — bare names silently match nothing.</summary>
        private static readonly string[] FeraldisSectPriority =
        {
            SectConfig.War,     // smite + elite unit (implemented kit)
            SectConfig.Ash,
            SectConfig.Ruin,
            SectConfig.Wrath,
        };

        /// <summary>
        /// Raider Camps ARE the Feraldis economy — its huts gather nothing,
        /// so camp count is the only thing that scales income. Keep building
        /// Gatherer's Huts; they convert to camps automatically.
        /// </summary>
        private static void TryBuildEconomy(EntityManager em, Faction faction, float3 hallPos)
        {
            int camps = CountFactionWith<RaiderCampTag>(em, faction);
            if (camps >= TargetRaiderCamps) return;
            TryPlace(em, faction, "GatherersHut", hallPos, 24f, 90f,
                AIBudgetCategory.EconomyExpansion);
        }

        private static void TryBuildAge2(EntityManager em, Faction faction, float3 hallPos)
        {
            // NOTE: no early `return` between these. An earlier version bailed
            // out after the first attempt, so a Thrower Camp that could not be
            // placed (ring full of the AI's own Gatherer's Huts) silently
            // blocked the Pasture and the Temple behind it for the whole
            // match. Each one now gets its own shot every tick.
            //
            // Thrower Camp first — the match log was 17 x "floor unit Archer
            // has no trainer", i.e. the AI wanted ranged units for 30 minutes
            // and had nowhere to train them. The ring is widened well past
            // the hut belt for the same reason.
            if (CountFactionWith<ArcheryRangeTag>(em, faction) < 1)
                TryPlace(em, faction, "ArcheryRange", hallPos, 14f, 80f,
                    AIBudgetCategory.Military);

            if (CountFactionWith<PastureTag>(em, faction) < 1)
                TryPlace(em, faction, "Feraldis_Pasture", hallPos, 14f, 80f,
                    AIBudgetCategory.Military);

            if (CountFactionWith<TempleOfRidanTag>(em, faction) < 1)
                TryPlace(em, faction, "TempleOfRidan", hallPos, 14f, 80f,
                    AIBudgetCategory.Advancement);
        }

        /// <summary>The Corruptor is gated at Temple Lv 3, so the Temple has
        /// to climb before the verb is even available.</summary>
        private static void TryLevelTemple(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<TempleLevel>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                int lv = em.GetComponentData<TempleLevel>(ents[i]).Level;
                if (lv >= 3) return;
                CommandRouter.IssueTempleUpgrade(em, ents[i], CommandSource.AI);
                return;
            }
        }

        // ---------------------------------------------------------------
        // THE VERB — crack a well, then smash it.
        // ---------------------------------------------------------------
        private static void TryRunTheVerb(EntityManager em, Entity brainEntity,
            Faction faction, float3 hallPos, float now)
        {
            // 1. A well already cracked open? Everything goes at it NOW —
            //    the window is short and the curse is spawning defenders.
            if (TryFindCorruptedWell(em, out Entity cracked, out float3 crackedPos))
            {
                CommitArmy(em, faction, cracked, crackedPos, attackTheWell: true);
                return;
            }

            // 2. Otherwise: find an idle Corruptor and send it at a well.
            Entity corruptor = Entity.Null;
            bool anyCorruptor = false;
            var cq = em.CreateEntityQuery(
                ComponentType.ReadOnly<CorruptorTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var ents = cq.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                    anyCorruptor = true;
                    if (em.HasComponent<RitualState>(ents[i])) continue;
                    if (em.HasComponent<CorruptCommand>(ents[i])) continue;
                    corruptor = ents[i];
                    break;
                }
            }

            // 3. No Corruptor at all -> train one (once; the queue check is
            //    the anti-money-furnace guard the Alanthor sibling uses too).
            if (!anyCorruptor)
            {
                if (!IsUnitQueued(em, faction, "Feraldis_Iconoclast"))
                    TryQueueAtTemple(em, faction, "Feraldis_Iconoclast");
                return;
            }
            if (corruptor == Entity.Null) return;   // busy channelling

            if (!TryPickWell(em, faction, hallPos, out Entity well, out float3 wellPos)) return;

            // ESCORT FIRST, THEN THE RITUALIST. The 2026-08-06 match trained
            // NINE Corruptors and dispatched them 34 times over 19 minutes
            // without landing a single corruption — they were walking alone
            // at 3.2 speed across a map that was 61 % curse, and dying before
            // arrival. The army now moves out first so the lane is contested
            // by the time the 300-supply ritualist follows it.
            int escort = CommitArmy(em, faction, well, wellPos, attackTheWell: false);

            // Prefer an escort — but never wait forever for one. See
            // MaxEscortWaitSeconds: an AI that cannot field four spare units
            // used to sit on "escort 0/4" until the match ended, which is a
            // guaranteed loss on a map whose only victory path is the verb.
            var tick = em.GetComponentData<AIFeraldisTickState>(brainEntity);
            bool escortReady = escort >= MinEscortBeforeDispatch;

            if (!escortReady)
            {
                if (tick.CorruptorHeldSince <= 0f)
                {
                    tick.CorruptorHeldSince = now;
                    em.SetComponentData(brainEntity, tick);
                }

                float waited = now - tick.CorruptorHeldSince;
                if (waited < MaxEscortWaitSeconds)
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Corruptor held: escort {escort}/{MinEscortBeforeDispatch} " +
                        $"({waited:0}s of {MaxEscortWaitSeconds:0}s)");
                    return;
                }

                AILogger.Log(faction, "STRATEGY",
                    $"Corruptor dispatched UNESCORTED after {waited:0}s waiting on an escort " +
                    $"that never came (escort {escort}) — an unescorted try beats never trying");
            }

            // Reset the patience clock: either we have an escort now, or we
            // just spent it. The next hold starts fresh.
            if (tick.CorruptorHeldSince > 0f)
            {
                tick.CorruptorHeldSince = 0f;
                em.SetComponentData(brainEntity, tick);
            }

            CommandRouter.IssueCorrupt(em, corruptor, well, CommandSource.AI);
            if (escortReady)
                AILogger.Log(faction, "STRATEGY",
                    $"Corruptor dispatched to well at ({wellPos.x:0},{wellPos.z:0}) with escort {escort}");
        }

        private static bool TryFindCorruptedWell(EntityManager em, out Entity well, out float3 pos)
        {
            well = Entity.Null; pos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<WellCorrupted>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            if (ents.Length == 0) return false;
            well = ents[0];
            pos = em.GetComponentData<LocalTransform>(well).Position;
            return true;
        }

        /// <summary>Nearest living well this faction has actually revealed.</summary>
        private static bool TryPickWell(EntityManager em, Faction faction, float3 hallPos,
            out Entity best, out float3 bestPos)
        {
            best = Entity.Null; bestPos = default;
            float bestD = float.MaxValue;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<BorderNodeState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var s = em.GetComponentData<BorderNodeState>(e).State;
                // A dead well needs no corrupting; it is already counted.
                if (s == NodeState.Destroyed) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<ActiveRitualOnNode>(e)) continue;
                if (em.HasComponent<WellCorrupted>(e)) continue;

                var p = em.GetComponentData<LocalTransform>(e).Position;
                if (fog != null && !fog.IsRevealed(faction, new UnityEngine.Vector3(p.x, 0f, p.z)))
                    continue;

                float d = math.distancesq(p, hallPos);
                if (d < bestD) { bestD = d; best = e; bestPos = p; }
            }
            return best != Entity.Null;
        }

        /// <summary>
        /// Send the standing army to the well. When the well is already
        /// cracked they get an explicit ATTACK order on it — wells are never
        /// auto-acquired unless corrupted, and an explicit order is also the
        /// only path CommandRouter allows against a well.
        /// </summary>
        private static int CommitArmy(EntityManager em, Faction faction,
            Entity well, float3 wellPos, bool attackTheWell)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);

            int sent = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                var u = ents[i];
                if (em.GetComponentData<FactionTag>(u).Value != faction) continue;

                var cls = em.GetComponentData<UnitTag>(u).Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged && cls != UnitClass.Siege)
                    continue;
                // Never drag the Corruptor itself, or uncontrollable raiders.
                if (em.HasComponent<CorruptorTag>(u)) continue;
                if (em.HasComponent<NotControllableTag>(u)) continue;
                if (em.HasComponent<UserMoveOrder>(u)) continue;

                var p = em.GetComponentData<LocalTransform>(u).Position;
                if (math.distance(p, wellPos) > AssaultRange) continue;

                if (attackTheWell)
                {
                    CommandRouter.IssueAttack(em, u, well, CommandSource.AI);
                }
                else
                {
                    // STAND OFF — do not pile onto the well itself.
                    //
                    // Sending every escort to the exact wellPos is what has
                    // been killing this AI's own verb. A channelling ritualist
                    // sits with DesiredDestination.Has = 0, and SteeringSystem
                    // keeps separation "at full strength so units still push
                    // apart inside the cluster" — so a dozen escorts
                    // converging on the ritualist's tile shove it radially
                    // outward, every 5 s re-commit ratcheting it further, with
                    // nothing pulling it back. Past CorruptCancelRange (10 m)
                    // the channel breaks and the whole approach restarts.
                    //
                    // The 2026-08-07 8-player match measured it cleanly across
                    // 73 dispatches: mean gap between re-dispatches was 18.5 s
                    // at escort 12+ (63 samples, channel never survived its
                    // 40 s), 35.2 s at escort 8-11, and 123 s at escort < 8 —
                    // i.e. the verb only ever landed once the bodyguard got
                    // thin enough to stop trampling it.
                    //
                    // A ring at EscortStandoffRadius is also just the correct
                    // bodyguard shape: a screen intercepts what comes at the
                    // ritualist instead of standing on top of it.
                    float ang = (sent / (float)AssaultArmySize) * 2f * math.PI;
                    float3 slot = wellPos + new float3(
                        math.cos(ang) * EscortStandoffRadius, 0f,
                        math.sin(ang) * EscortStandoffRadius);
                    TheWaningBorder.Core.Commands.Types.AttackMoveCommandHelper.Execute(em, u, slot);
                }

                if (++sent >= AssaultArmySize) break;
            }
            return sent;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static int CountFactionWith<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == faction) n++;
            return n;
        }

        private static bool IsUnitQueued(EntityManager em, Faction faction, string unitId)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (!em.HasBuffer<TrainQueueItem>(ents[i])) continue;
                var buf = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].UnitId.ToString() == unitId) return true;
            }
            return false;
        }

        private static void TryQueueAtTemple(EntityManager em, Faction faction, string unitId)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, ents[i], unitId, out _, out _)) return;
                CommandRouter.IssueTrain(em, ents[i], unitId, CommandSource.AI);
                AILogger.Log(faction, "MILITARY", $"{unitId} queued at Temple");
                return;
            }
        }

        /// <summary>
        /// Place a building and get builders onto it.
        ///
        /// CommandRouter.IssuePlaceBuilding has an inverted-looking contract
        /// and getting it wrong is silent: it returns TRUE when the placement
        /// was QUEUED FOR LOCKSTEP (nothing exists locally yet, `building` is
        /// Null) and FALSE when it was CREATED IMMEDIATELY in single player
        /// (`building` is the real entity). An earlier version of this method
        /// treated false as failure and returned — so in single player every
        /// Mine, Totem, Thrower Camp and Pasture WAS created and then
        /// instantly abandoned with no builders, sitting at 1 HP under
        /// construction forever. That is why three matches in a row showed a
        /// Feraldis AI with zero iron and no military buildings.
        ///
        /// The caller also has to spend up front and REFUND if no builder is
        /// available, or the AI silently leaks its bank into foundations
        /// nobody will ever finish.
        /// </summary>
        private static void TryPlace(EntityManager em, Faction faction, string buildingId,
            float3 anchor, float rmin, float rmax, AIBudgetCategory cat)
        {
            if (!BuildCosts.TryGet(buildingId, out var cost)) return;
            if (!AIBudget.CanSpend(faction, cat, cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;

            var size = BuildingSizeConfig.GetSize(buildingId);
            if (!TryFindSpot(em, anchor, size, rmin, rmax, out float3 pos))
            {
                AILogger.Log(faction, "BUILDING", $"{buildingId}: no valid spot {rmin:0}-{rmax:0}m from anchor");
                return;
            }

            if (!FactionEconomy.Spend(em, faction, cost)) return;
            AIBudget.RecordSpend(faction, cat, cost);

            bool queued = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queued)
            {
                // Lockstep will build it; send builders at the position.
                DispatchBuilders(em, faction, Entity.Null, buildingId, pos);
                AILogger.Log(faction, "BUILDING", $"{buildingId} queued at ({pos.x:0},{pos.z:0})");
                return;
            }

            if (building == Entity.Null)
            {
                FactionEconomy.Add(em, faction, cost);   // refund
                return;
            }

            int dispatched = DispatchBuilders(em, faction, building, buildingId, pos);
            if (dispatched == 0)
            {
                // Nobody to build it — undo rather than leave a permanent
                // 1 HP foundation blocking the count check forever.
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                AILogger.Log(faction, "BUILDING", $"{buildingId}: no idle builder, cancelled");
                return;
            }
            AILogger.Log(faction, "BUILDING", $"{buildingId} placed at ({pos.x:0},{pos.z:0})");
        }

        private static bool TryFindSpot(EntityManager em, float3 anchor, int2 size,
            float rmin, float rmax, out float3 pos)
        {
            pos = default;
            for (float r = rmin; r <= rmax; r += 6f)
            {
                for (int a = 0; a < 12; a++)
                {
                    float ang = a * (math.PI * 2f / 12f);
                    float x = anchor.x + math.cos(ang) * r;
                    float z = anchor.z + math.sin(ang) * r;
                    var c = new float3(x, TerrainUtility.GetHeight(x, z), z);
                    if (!BuildCommandHelper.IsValidBuildPosition(em, c, size)) continue;
                    pos = c;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Send up to two builders. Returns how many were sent so
        /// the caller can refund a placement nobody can finish.</summary>
        private static int DispatchBuilders(EntityManager em, Faction faction,
            Entity site, string buildingId, float3 sitePos)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            int sent = 0;
            for (int i = 0; i < ents.Length && sent < 2; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                CommandRouter.IssueBuild(em, ents[i], site, buildingId, sitePos, CommandSource.AI);
                sent++;
            }
            return sent;
        }
    }
}
