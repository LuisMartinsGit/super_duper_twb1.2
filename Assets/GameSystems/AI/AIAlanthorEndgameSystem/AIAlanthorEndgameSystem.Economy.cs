// AIAlanthorEndgameSystem.Economy.cs
// Age-2 ladder, well purification (the Alanthor verb), temple/smelter levelling, expansion.
// Partial of AIAlanthorEndgameSystem.cs -- split 2026-08-12 for readability.

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
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_ScholarTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<ScholarTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_ScholarTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_BorderMainNodeTagBorderNodeStateLocalTransform =
        {
            ComponentType.ReadOnly<BorderMainNodeTag>(),
            ComponentType.ReadOnly<BorderNodeState>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_BorderMainNodeTagBorderNodeStateLocalTransform;

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_SmelterTagFactionTag =
        {
            ComponentType.ReadOnly<SmelterTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_SmelterTagFactionTag;

        #endregion

        // ──────────────────────────────────────────────────────────────────
        // 4. AGE-2 BUILDING LADDER + SMELTER LEVELLING
        // ──────────────────────────────────────────────────────────────────

        // Age-2 build ladder, priority-ordered. Temple leads: sect adoption
        // (chapel plots), Litharch training and the whole religious layer
        // hang off it. Then the veilsteel Smelter, then the military
        // production pair the armoured-unit pass trains from. (The Practice
        // Range is the LEVELED Archery Range now, not a placeable building.)
        private static readonly (string id, float rMin, float rMax)[] Age2Ladder =
        {
            ("TempleOfRidan",          16f, 26f),
            ("Alanthor_RoyalStable",   18f, 30f),
            ("Alanthor_SiegeYard",     20f, 32f),
        };

        // ──────────────────────────────────────────────────────────────────
        // 4b. WELL PURIFICATION (the Alanthor verb)
        // ──────────────────────────────────────────────────────────────────

        private static void TryPurifyWells(Faction faction, EntityManager em, float3 hallPos)
        {
            // Find a free Scholar (not already channeling / ordered).
            Entity scholar = Entity.Null;
            bool anyScholar = false;
            {
                var sq = QC_ScholarTagFactionTagLocalTransform.Get(em, QT_ScholarTagFactionTagLocalTransform);
                using var sEnts = sq.ToEntityArray(Allocator.Temp);
                using var sFacs = sq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < sEnts.Length; i++)
                {
                    if (sFacs[i].Value != faction) continue;
                    anyScholar = true;
                    if (em.HasComponent<RitualState>(sEnts[i])) continue;
                    if (em.HasComponent<PurifyCommand>(sEnts[i])) continue;
                    scholar = sEnts[i];
                    break;
                }
            }

            // No Scholar at all → train ONE at the Temple (the ladder builds
            // the Temple; TryQueueAt pre-flights queue space + cost). The
            // in-queue check is what stops the 5-Scholars-in-25-seconds
            // money furnace the 2026-08-04 logs caught — a Scholar takes
            // 68 s to train and every 5 s think tick was buying another.
            if (!anyScholar)
            {
                if (!AICommon.IsUnitQueued(em, faction, "Alanthor_Scholar"))
                    TryQueueAt<TempleOfRidanTag>(em, faction, "Alanthor_Scholar");
                return;
            }
            if (scholar == Entity.Null) return; // all Scholars busy

            // Claimable wells: Active or rubble, built, no ritual in progress,
            // and fog-honest (the AI only verbs wells it has revealed).
            // RANKED, not nearest-only (2026-09-26): the old pick looked at the
            // one well nearest the Hall and, when that well was garrisoned,
            // stalled there for the whole match while a clean well sat
            // unvisited. Score = distance from the Hall + a per-defender
            // penalty - a bonus when the well sits in (or beside) our own
            // territory. The best well that PASSES the rite gate is purified;
            // if none passes because of defenders, the best-scored defended
            // one is assaulted instead (docs/Design/Game_AI.md § 7c).
            var fogMgr = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            var nq = QC_BorderMainNodeTagBorderNodeStateLocalTransform.Get(em, QT_BorderMainNodeTagBorderNodeStateLocalTransform);
            using var nEnts = nq.ToEntityArray(Allocator.Temp);
            using var nStates = nq.ToComponentDataArray<BorderNodeState>(Allocator.Temp);
            using var nXfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var cands = new NativeList<WellCandidate>(nEnts.Length, Allocator.Temp);
            for (int i = 0; i < nEnts.Length; i++)
            {
                // Active wells AND Destroyed rubble are both purifiable
                // (PurificationRitualSystem only rejects Cleansed/Converted)
                // — consecrating a broken well before it rebuilds is the
                // cheapest hold Alanthor ever gets.
                bool rubble = nStates[i].State == NodeState.Destroyed;
                bool active = nStates[i].State == NodeState.Active
                    && !em.HasComponent<NodeDormant>(nEnts[i]);
                if (!active && !rubble) continue;
                if (em.HasComponent<UnderConstruction>(nEnts[i])) continue;
                if (em.HasComponent<ActiveRitualOnNode>(nEnts[i])) continue;
                var p = nXfs[i].Position;
                if (fogMgr != null && !fogMgr.IsRevealed(faction,
                        new UnityEngine.Vector3(p.x, 0f, p.z))) continue;

                float dist = math.distance(new float2(p.x, p.z), new float2(hallPos.x, hallPos.z));
                int defenders = AIEndgameCommon.CountWellDefenders(em, p);
                float score = dist + defenders * Cfg.wellDefenderPenalty - TerritoryBonus(faction, p);
                cands.Add(new WellCandidate { Well = nEnts[i], Pos = p, Score = score });
            }
            if (cands.Length == 0) { cands.Dispose(); return; }
            cands.Sort(new WellCandidate.ByScore());

            // THE RITE GATE (Curse_And_Shardroot.md 2.12, 2026-09-13). On
            // Hollow Table this loop sent a Scholar + 3 escorts at a well
            // garrisoned by eight curse units 51 times; every rite broke and
            // armed a Backlash, and the AI razed its own base with the waves
            // it summoned. No rite while the well is defended, erupting, or
            // on the retry clock, and never with a short escort. A defended
            // well is ASSAULTED instead, with real odds, and the rite follows
            // on a later think once the ground is clear.
            float simNow = TheWaningBorder.Core.SimClock.Now;
            int idleMil = AIEndgameCommon.CountIdleMilitary(em, faction);
            Entity best = Entity.Null;
            float3 wellPos = default;
            Entity assaultWell = Entity.Null;
            float3 assaultPos = default;
            int assaultDefenders = 0;
            string firstWhy = null;
            for (int c = 0; c < cands.Length; c++)
            {
                var cand = cands[c];
                if (AIEndgameCommon.RiteAllowed(em, faction, cand.Well, cand.Pos, simNow, idleMil,
                        Cfg.escortSize, out int defenders, out string why))
                {
                    best = cand.Well;
                    wellPos = cand.Pos;
                    break;
                }
                if (firstWhy == null) firstWhy = why;
                if (defenders > 0 && assaultWell == Entity.Null)
                {
                    assaultWell = cand.Well;
                    assaultPos = cand.Pos;
                    assaultDefenders = defenders;
                }
                // A short escort blocks EVERY well alike — stop looking.
                if (defenders == 0 && idleMil < Cfg.escortSize) break;
            }
            cands.Dispose();

            if (best == Entity.Null)
            {
                if (assaultWell != Entity.Null)
                    AIEndgameCommon.TryAssaultWell(em, faction, assaultPos, assaultDefenders);
                else
                    AILogger.Log(faction, "STRATEGY", $"Alanthor: rite held -- {firstWhy}");
                return;
            }

            CommandRouter.IssuePurify(em, scholar, best, CommandSource.AI);
            AILogger.Log(faction, "STRATEGY", "Alanthor: Scholar dispatched to purify a well");

            // ESCORT (2026-07-12): the army is the Scholar's BODYGUARD, not
            // the main force — plain waves at wells only fed the crystal
            // spread. Send up to EscortSize idle military attack-moving to
            // the well so they screen the channel; committed units are never
            // re-drafted (command follow-through).
            var eq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var eEnts = eq.ToEntityArray(Allocator.Temp);
            using var eTags = eq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var eFacs = eq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int sent = 0;
            for (int i = 0; i < eEnts.Length && sent < Cfg.escortSize; i++)
            {
                if (eFacs[i].Value != faction) continue;
                var cls = eTags[i].Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged
                    && cls != UnitClass.Siege) continue;
                Entity u = eEnts[i];
                if (em.HasComponent<UnderConstruction>(u)) continue;
                if (TransientState.Active<AttackCommand>(em, u)) continue;
                if (TransientState.Active<AttackMoveTag>(em, u)) continue;
                if (TransientState.Active<UserMoveOrder>(em, u)) continue;
                // STAND OFF — ring, not pile-on. Sending the whole escort to
                // the Scholar's own tile shoves it off the node: a channelling
                // ritualist has DesiredDestination.Has = 0 and SteeringSystem
                // keeps separation at full strength, so the bodyguard ratchets
                // its own charge past RitualCancelRange (20 m) and breaks the
                // 35 s channel. Measured on the Feraldis sibling in the
                // 2026-08-07 8-player match: mean 18.5 s between re-dispatches
                // at escort 12+, versus 123 s once the escort thinned out.
                float3 slot = AIEndgameCommon.EscortSlot(
                    wellPos, sent, Cfg.escortSize, AIEndgameCommon.EscortStandoffRadius);
                CommandRouter.IssueAttackMove(em, u, slot, CommandSource.AI);
                sent++;
            }
            if (sent > 0)
                AILogger.Log(faction, "STRATEGY",
                    $"Alanthor: {sent} escorts sent with the Scholar");
        }



        /// <summary>One purifiable well and its rank (lower is better).</summary>
        private struct WellCandidate
        {
            public Entity Well;
            public float3 Pos;
            public float Score;

            public struct ByScore : System.Collections.Generic.IComparer<WellCandidate>
            {
                // Ties broken on position bits, never on chunk order.
                public int Compare(WellCandidate a, WellCandidate b)
                {
                    if (a.Score != b.Score) return a.Score < b.Score ? -1 : 1;
                    if (a.Pos.x != b.Pos.x) return a.Pos.x < b.Pos.x ? -1 : 1;
                    if (a.Pos.z != b.Pos.z) return a.Pos.z < b.Pos.z ? -1 : 1;
                    return 0;
                }
            }
        }

        /// <summary>
        /// Metres knocked off a well's rank for our own ground: the full
        /// wellOwnedBonus when the well's territory is ours, wellAdjacentBonus
        /// when any probe on a wellAdjacencyProbeRadius ring around it lands in
        /// our territory (the well borders us), otherwise nothing.
        /// </summary>
        private static float TerritoryBonus(Faction faction, float3 p)
        {
            if (!TheWaningBorder.World.Regions.TerritoryOwnership.Ready) return 0f;
            int me = (int)faction;
            if (TheWaningBorder.World.Regions.TerritoryOwnership.OwnerAt(p.x, p.z) == me)
                return Cfg.wellOwnedBonus;
            float r = Cfg.wellAdjacencyProbeRadius;
            for (int k = 0; k < WellAdjacencyProbes; k++)
            {
                float ang = k * (math.PI * 2f / WellAdjacencyProbes);
                if (TheWaningBorder.World.Regions.TerritoryOwnership.OwnerAt(
                        p.x + math.cos(ang) * r, p.z + math.sin(ang) * r) == me)
                    return Cfg.wellAdjacentBonus;
            }
            return 0f;
        }

        /// <summary>Probe count on the adjacency ring — loop resolution, not a tunable.</summary>
        private const int WellAdjacencyProbes = 8;

        /// <summary>Level the Temple toward max — era progression, sect
        /// levers, and (at L3) the Holy Scholar all hang off it, and the
        /// Scholar is the faction's well verb, i.e. the victory path.
        /// Deliberately NOT budget-windowed (2026-08-11): the 500-1200
        /// supply single spends starved inside the Advancement window's
        /// weighted share — one L2 upgrade happened across four AIs in a
        /// 35-minute match, so no Temple ever hit L3, no Scholar ever
        /// trained, and no ritual was EVER attempted. Bank-affordability
        /// still gates. One attempt per think tick.</summary>
        private static void TryLevelTemple(Faction faction, EntityManager em)
            => AIEndgameCommon.TryLevelTemple(em, faction);

        /// <summary>Returns true while a ladder entry is still missing (an
        /// attempt was made this tick or is pending) — the expansion passes
        /// key off this so the core always outranks them.</summary>
        private static bool TryBuildAge2Ladder(Faction faction, EntityManager em, float3 hallPos)
        {
            // Veilsteel engine FIRST, independent of ladder progress. This used
            // to run only after the whole ladder stood, which log-provably
            // starved it: one unplaceable ladder entry (rings saturated by
            // gatherer huts) blocked Smelter levels for an entire match while
            // hut upgrades drained every shard of veilsteel the L1 output made.
            TryLevelSmelters(faction, em);

            for (int i = 0; i < Age2Ladder.Length; i++)
            {
                var (id, rMin, rMax) = Age2Ladder[i];
                if (CountFactionBuildings(em, faction, id) > 0) continue;
                // No Religion Point, no Temple (Religion.md §2) — skip it
                // rather than hold the Stable and Siege Yard behind an entry
                // that cannot be placed.
                if (id == "TempleOfRidan"
                    && !TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction,
                           TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp))
                    continue;
                TryBuildOnce(faction, em, hallPos, id, rMin, rMax);
                return true; // one ladder attempt per think tick, in order
            }
            return false; // ladder complete — expansion passes may run
        }

        /// <summary>Level EVERY Smelter (Forge) the faction owns toward L3,
        /// lowest level first, one upgrade attempt per think tick. The old
        /// pass took whichever Smelter the query returned first — with the
        /// build cap at 5 that left the rest of the fleet stuck at L1.
        /// UpgradeBuildingCommandHelper does the validation, cost check and
        /// spend; a NotUpgradeable / CannotAfford / AlreadyMaxLevel result
        /// simply means "not this tick".</summary>
        private static void TryLevelSmelters(Faction faction, EntityManager em)
        {
            var q = QC_SmelterTagFactionTag.Get(em, QT_SmelterTagFactionTag);
            using var ents = q.ToEntityArray(Allocator.Temp);
            Entity best = Entity.Null;
            int bestLevel = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                // Queued counts as busy — see AIBuildingUpgradeSystem.
                if (UpgradeBuildingCommandHelper.IsUpgradeQueued(em, ents[i])) continue;
                int lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : 0;
                if (lvl < bestLevel) { bestLevel = lvl; best = ents[i]; }
            }
            if (best == Entity.Null) return;

            var result = UpgradeBuildingCommandHelper.Execute(em, best, CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor: Smelter upgrade queued (L{bestLevel} -> L{bestLevel + 1})");
        }

        // ──────────────────────────────────────────────────────────────────
        // 4c/4d. EXPANSION TARGETS (endgame completeness)
        // ──────────────────────────────────────────────────────────────────



        /// <summary>Build Smelters toward the cap, one at a time (never a
        /// second foundation while one is under construction). Returns true
        /// when a foundation was placed or queued this tick.</summary>
        private static bool TryExpandSmelters(Faction faction, EntityManager em, float3 hallPos)
        {
            if (CountFactionBuildingsByTag<SmelterTag>(em, faction) >= Cfg.smelterTarget) return false;
            if (AnyFactionBuildingUnderConstruction<SmelterTag>(em, faction)) return false;
            return TryBuildOnce(faction, em, hallPos, "Alanthor_Smelter", 18f, 28f);
        }

        /// <summary>Build Huts toward the housing target, one at a time.
        /// Returns true when a foundation was placed or queued this tick.</summary>
        private static bool TryBuildHouses(Faction faction, EntityManager em, float3 hallPos)
        {
            if (CountFactionBuildingsByTag<HutTag>(em, faction) >= Cfg.houseTarget) return false;
            if (AnyFactionBuildingUnderConstruction<HutTag>(em, faction)) return false;
            if (BuildingFactory.AtFactionCap(em, faction, "Hut")) return false;
            // The house quarter: pack new Houses around the ones standing
            // (AICommon.TryHouseQuarterAnchor), the base ring only for the first.
            if (AICommon.TryHouseQuarterAnchor(em, faction, out float3 quarter, hallPos))
                return TryBuildOnce(faction, em, quarter, "Hut", 0f, 12f, flush: true);
            return TryBuildOnce(faction, em, hallPos, "Hut", 12f, 28f);
        }

        /// <summary>Returns true when the foundation was placed (or queued
        /// for lockstep) this tick — false on any pre-flight or placement
        /// failure (the cost is refunded on the rollback paths).</summary>
        private static bool TryBuildOnce(Faction faction, EntityManager em, float3 hallPos,
            string buildingId, float ringMin, float ringMax, bool flush = false)
        {
            if (!BuildCosts.Exists(buildingId)) return false;
            var cost = BuildCosts.For(em, faction, buildingId);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;

            // The Temple costs a Religion Point (docs/Design/Religion.md §2);
            // without one the executor refuses it, so do not try every think.
            if (buildingId == "TempleOfRidan"
                && !TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction,
                       TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp))
                return false;

            // Pre-flight: need an idle worker. Don't spend cost on a foundation
            // nobody will work on.
            if (AICommon.CountIdleWorkers(em, faction) == 0) return false;

            int2 size = BuildingSizeConfig.GetSize(buildingId);
            // The base rings clog up over a long match (gatherer huts tile the
            // ground around the hall). If the authored ring has no slot, retry
            // once at 1.6x the radius rather than silently stalling the ladder
            // forever — an outlying stable beats no stable.
            // A flush search (the House quarter) lets footprints touch.
            float3 pos;
            if (flush)
            {
                if (!AIEndgameCommon.TryFindBuildSpotRingGap(em, hallPos, size, ringMin, ringMax * 1.6f,
                        angleSamples: 24, radiusStep: 4f, seededStart: true, gap: 0f, out pos))
                    return false;
            }
            else if (!TryFindBuildPositionRing(em, hallPos, size, ringMin, ringMax, out pos)
                && !TryFindBuildPositionRing(em, hallPos, size, ringMax, ringMax * 1.6f, out pos))
                return false;

            // No AI-side Spend: PlaceBuildingDirect charges the cost on
            // every peer (docs/Multiplayer_LAN_Readiness.md).

            // Replicating entry point (audit F4) — PlaceBuildingDirect was
            // host-only. Queued case: dispatch at the position, null target;
            // workers auto-find the foundation on arrival.
            bool queuedPlacement = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queuedPlacement)
            {
                AICommon.DispatchWorkersTo(em, faction, Entity.Null, buildingId, pos, maxWorkers: 2);
                AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
                return true;
            }
            // Null = the executor rejected (cap or bank short) — nothing was
            // spent, so there is nothing to refund.
            if (building == Entity.Null) return false;

            int dispatched = AICommon.DispatchWorkersTo(em, faction, building, buildingId, pos, maxWorkers: 2);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return false;
            }
            AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
            return true;
        }
        /// <summary>Count this faction's buildings by marker tag (completed
        /// AND under construction — expansion targets are totals).</summary>
        private static int CountFactionBuildingsByTag<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = AIQueryCache.TagFaction<T>(em);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            // Plans count too (docs/Design/Planned_Buildings.md): an ordered
            // building whose worker is still walking is already decided.
            int count = TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, TheWaningBorder.Entities.PlannedBuildings.IdsFor<T>());
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) count++;
            return count;
        }

        /// <summary>True while any of this faction's buildings with the given
        /// marker tag is still under construction — the one-foundation-at-a-
        /// time gate for the expansion passes.</summary>
        private static bool AnyFactionBuildingUnderConstruction<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            // A plan is a site that has not broken ground yet.
            if (TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, TheWaningBorder.Entities.PlannedBuildings.IdsFor<T>()) > 0) return true;
            var query = AIQueryCache.TagFactionUnderConstruction<T>(em);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return true;
            return false;
        }
    }
}
