// SimpleAISystem.CurseClear.cs
// The idle army clears the curse off the ground next door
// (docs/Design/Game_AI.md § 5b, "The idle army clears the curse").
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-05, SunderedCrown in-editor match)
//
// Yellow (Hard) had killed Blue and Green and stood 193 units at home for
// the rest of the match: its claim rounds logged "no claimable territory
// next to Fortress-linked ground" — every neighbour was curse-held, and
// curse-held ground was a claim candidate only while an Alanthor army was
// short of veilstone (Yellow banked 6,500). Curse territory is LOCKED by its
// nodes (Territory_Claims.md §6), so standing on it takes nothing: the nodes
// have to die first. Nothing else sent the army there.
//
// Now, after the age-up, while the army has no wave out, no economy response
// and no Defend, and holds at least curseClearMinUnits idle soldiers above
// its standing floor, the surplus marches on the WEAKEST curse-held territory
// bordering its Fortress-linked ground — weakest by the curse's power at the
// known node nearest home (AIEngagement), nearest breaking ties — and only
// when it beats that power by curseClearPowerMargin. The sortie is a claim
// squad in curse-assault mode, so it walks node to node, and once the last
// node falls it turns into an ordinary claim on the freed ground.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        private readonly Dictionary<int, float> _nextCurseClear = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextCurseClearLog = new Dictionary<int, float>();

        // Host scratch (main thread only).
        private readonly List<(int Region, float3 Node, int Power, float Dist)> _curseClearCands
            = new List<(int, float3, int, float)>();
        private readonly List<Entity> _curseClearDraft = new List<Entity>();

        /// <summary>
        /// Send the idle army above the standing floor to clear the weakest
        /// curse-held territory next to Fortress-linked ground (see the file
        /// header). One sortie at a time; every curseClearInterval seconds.
        /// </summary>
        private void TryClearAdjacentCurse(EntityManager em, Faction faction, in SimpleAIState aiState,
            in AIDifficultyProfile profile, float now)
        {
            ResetEconomyDefenceIfNewMatch();
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            // THE ALL-IN (Game_AI.md § 6n): the idle army is the wave's.
            if (AllInArmed(faction)) return;
            int key = (int)faction;
            if (_nextCurseClear.TryGetValue(key, out float next) && now < next) return;
            _nextCurseClear[key] = now + math.max(1f, Cfg.curseClearInterval);

            // Claims open at age-up; a defence need or a wave out owns the army.
            if (!HasAgedUp(em, faction)) return;
            if (aiState.Posture == AIPosture.Defend) return;
            if (aiState.WaveActive != 0) return;
            if (EconResponsesOf(key).Count > 0) return;
            var squads = ClaimSquadsOf(key);
            for (int i = 0; i < squads.Count; i++)
                if (squads[i].CurseAssault || squads[i].HostileAssault) return;   // one assault at a time

            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return;
            float3 home = HomeAnchor(em, faction);

            // ── The curse-held neighbours, weakest first ──
            _curseClearCands.Clear();
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (TerritoryOwnership.OwnerOf(r) != TerritoryOwnership.Curse) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (!BordersFortressGround(faction, r, mine)) continue;
                if (_siteBlocked.TryGetValue((key, r), out float until) && now < until) continue;
                bool targeted = false;
                for (int s = 0; s < squads.Count && !targeted; s++) targeted = squads[s].Territory == r;
                if (targeted) continue;
                // Only a node it has SEEN (TryNearestCurseNodeIn, knownOnly).
                if (!TryNearestCurseNodeIn(em, r, true, faction, home, out float3 node)) continue;
                // The curse estimate, not the bare reading (CurseIntel.cs).
                int seenNow = AIEngagement.Assess(em, faction, node, Cfg.claimCurseAssessRadius).EnemyMobilePower;
                int power = CurseNodePowerEstimate(em, faction, node, Cfg.claimCurseAssessRadius, now, seenNow);
                _curseClearCands.Add((r, node, power, math.distance(node.xz, home.xz)));
            }
            if (_curseClearCands.Count == 0) return;
            _curseClearCands.Sort((a, b) =>
            {
                int c = a.Power.CompareTo(b.Power);
                if (c != 0) return c;
                c = a.Dist.CompareTo(b.Dist);
                return c != 0 ? c : a.Region.CompareTo(b.Region);
            });

            // ── The idle surplus above the standing floor ──
            BuildClaimPool(em, faction);
            int floor = StandingArmyFloor(faction, profile, aiState.DesiredMilitary);
            int canDraft = _claimPool.Count - floor;
            if (canDraft < math.max(1, Cfg.curseClearMinUnits)) return;

            var best = _curseClearCands[0];
            float margin = math.max(1.05f, Cfg.curseClearPowerMargin);
            int need = (int)math.ceil(best.Power * margin);
            int want = math.min(canDraft, math.max(Cfg.curseClearMinUnits, Cfg.claimCurseSquadMax));
            _curseClearDraft.Clear();
            int myPower = 0;
            // Nearest the node first; at least `want`, more while the margin
            // is not met, never past the surplus.
            while (_claimPool.Count > 0 && _curseClearDraft.Count < canDraft
                   && (_curseClearDraft.Count < want || myPower < need))
            {
                int pick = NearestInPool(em, best.Node);
                var e = _claimPool[pick];
                _claimPool.RemoveAt(pick);
                _curseClearDraft.Add(e);
                myPower += TacticalQuery.UnitStrength(em, e);
            }
            if (myPower < need)
            {
                if (!_nextCurseClearLog.TryGetValue(key, out float nl) || now >= nl)
                {
                    _nextCurseClearLog[key] = now + Cfg.claimLogInterval;
                    AILogger.Log(faction, "CURSE CLEAR",
                        $"held — {_curseClearDraft.Count} idle unit(s) above the floor bring power {myPower} " +
                        $"vs the weakest curse ground, {RegionMap.NameOf(best.Region)}, at {best.Power} " +
                        $"(needs x{margin:0.00})");
                }
                return;
            }

            var squad = new ClaimSquad
            {
                Territory = best.Region, Point = best.Node, StartedAt = now, CurseAssault = true,
            };
            for (int i = 0; i < _curseClearDraft.Count; i++)
            {
                squad.Members.Add(_curseClearDraft[i]);
                _claimSquadMembers.Add(_curseClearDraft[i]);
            }
            AICommon.IssueGroupOrder(em, squad.Members, best.Node, attackMove: true, Cfg.waveArrivedRadius);
            squad.OrderedPoint = best.Node;
            squad.NextReorderAt = now + ClaimReorderSeconds;
            squads.Add(squad);
            AILogger.Log(faction, "CURSE CLEAR",
                $"{squad.Members.Count} units -> territory {RegionMap.NameOf(best.Region)} " +
                $"(curse node at ({best.Node.x:0},{best.Node.z:0}), power {myPower} vs {best.Power})");
        }
    }
}
