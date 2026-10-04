// CurseTerritorySystem.Waves.cs
// CURSE ATTACK WAVES — docs/Design/Territory_Claims.md §6.8 (2026-10-04).
//
// The living curse defends (§6.7); on top of that it now attacks on a clock.
// Every `waveIntervalSeconds` (the first at `firstWaveSeconds`)
// `waveDraftFraction` of the curse's garrison units is DRAFTED into a wave —
// nearest the target first, never below garrisonMinPerNode a node — and the
// wave marches on a player (2026-10-04: "all spawn as garrison, then waves
// are 30% of that"):
//
//   TickAttackWaves      picks the target (nearest living player to the
//                        curse, rotated fairly via waveTargetDistanceSlack;
//                        the Shardroot holder while anyone holds it), the
//                        objective (the player's nearest production building
//                        in their nearest territory), drafts the wave and
//                        sends it as one formation attack-move. Fewer than
//                        waveMinSize to draft: the slot is skipped.
//   ShepherdAttackWaves  re-targets an idle wave inside the target territory,
//                        and after waveDurationSeconds, or below
//                        waveRetreatFraction of its size, turns it home: its
//                        units become garrison of the nearest curse node and
//                        the §6.7 leash walks them back.
//
// Waves raise nothing, so the cap (§6.8, CurseUnitCap) touches them only
// through the garrisons they are drafted from.
//
// DETERMINISM: as the rest of this system — in-sim on every peer, sorted
// walks, entity-index tie-breaks, and the one `_rng` draw per wave slot is
// made on every peer whatever the branch.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data.Border;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Border
{
    public partial class CurseTerritorySystem
    {
        /// <summary>CurseLivingMember.Role of an attack-wave unit (§6.8).</summary>
        private const byte RoleWave = 3;

        private sealed class AttackWaveState
        {
            public int Number;          // 1-based, for the log
            public int Home;            // territory of the origin node
            public Faction Target;
            public int Territory;       // the target territory it fights in
            public float3 Objective;
            public int Size;            // units it set out with
            public double StartedAt;
            public double NextThinkAt;
            public bool HuntsHolder;    // §6.6: aimed at the Shardroot holder
        }

        private readonly Dictionary<int, AttackWaveState> _attackWaves = new();
        private double _nextAttackWaveAt = -1.0;
        private int _attackWavesSent;
        private Faction _lastWaveTarget = Faction.Border;

        private void ResetAttackWaves()
        {
            _attackWaves.Clear();
            _nextAttackWaveAt = -1.0;
            _attackWavesSent = 0;
            _lastWaveTarget = Faction.Border;
        }

        // ── snapshots ───────────────────────────────────────────────────────

        private struct PlayerBuilding
        {
            public Entity E;
            public Faction F;
            public float3 P;
            public bool Production;   // trains units (carries a RallyPoint)
        }

        private readonly List<PlayerBuilding> _playerBuildings = new();
        private readonly List<(int t, float3 p)> _allNodes = new();
        private readonly List<int> _scratchNodeTerritories = new();

        /// <summary>Every standing building of a player faction (0..7), in
        /// entity order.</summary>
        private void SnapshotPlayerBuildings(EntityManager em)
        {
            _playerBuildings.Clear();
            var q = QueryFacXf<BuildingTag>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var f = facs[i].Value;
                if ((byte)f >= (byte)Faction.Border) continue;
                if (!Alliances.AreHostile(Faction.Border, f)) continue;
                if (em.HasComponent<Health>(ents[i]) && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                _playerBuildings.Add(new PlayerBuilding
                {
                    E = ents[i], F = f, P = xfs[i].Position,
                    Production = em.HasComponent<RallyPoint>(ents[i]),
                });
            }
            _playerBuildings.Sort((a, b) => a.E.Index.CompareTo(b.E.Index));
        }

        /// <summary>Every live curse node, by territory index then entity
        /// order (the order SyncHoldings filled _nodesByTerritory in).</summary>
        private void SnapshotAllNodes()
        {
            _allNodes.Clear();
            _scratchNodeTerritories.Clear();
            foreach (var kv in _nodesByTerritory)
                if (kv.Value.Count > 0) _scratchNodeTerritories.Add(kv.Key);
            _scratchNodeTerritories.Sort();
            for (int i = 0; i < _scratchNodeTerritories.Count; i++)
            {
                int t = _scratchNodeTerritories[i];
                var list = _nodesByTerritory[t];
                for (int k = 0; k < list.Count; k++) _allNodes.Add((t, list[k]));
            }
        }

        /// <summary>Index into _allNodes of the node nearest <paramref name="p"/>
        /// (first strictly nearest in that order), or -1 when there is none.</summary>
        private int NearestCurseNode(float3 p)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _allNodes.Count; i++)
            {
                float d = Distance2(_allNodes[i].p, p);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The target faction's building to strike in territory
        /// <paramref name="territory"/>: its production building nearest
        /// <paramref name="from"/> for preference, else its nearest building
        /// there. -1 when it has none in that territory.</summary>
        private int ObjectiveIn(Faction f, int territory, float3 from)
        {
            int bestProd = -1, bestAny = -1;
            float dProd = float.MaxValue, dAny = float.MaxValue;
            for (int i = 0; i < _playerBuildings.Count; i++)
            {
                var b = _playerBuildings[i];
                if (b.F != f) continue;
                if (RegionMap.NearestRegion(b.P.x, b.P.z) != territory) continue;
                float d = Distance2(b.P, from);
                if (d < dAny) { dAny = d; bestAny = i; }
                if (b.Production && d < dProd) { dProd = d; bestProd = i; }
            }
            return bestProd >= 0 ? bestProd : bestAny;
        }

        /// <summary>The faction's building nearest <paramref name="from"/>,
        /// anywhere, or -1.</summary>
        private int NearestBuildingOf(Faction f, float3 from)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _playerBuildings.Count; i++)
            {
                if (_playerBuildings[i].F != f) continue;
                float d = Distance2(_playerBuildings[i].P, from);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ── forming a wave ──────────────────────────────────────────────────

        private readonly List<(Faction f, float d2, int building, int node)> _waveCandidates = new();
        private readonly List<int> _waveAlternates = new();

        /// <summary>
        /// One attack-wave slot (§6.8). Runs AFTER TickGarrisons and the
        /// expansion dispatch in the same check: it drafts from the garrisons
        /// TickGarrisons just topped up, after any party has taken its share.
        /// </summary>
        private void TickAttackWaves(EntityManager em, double now, BorderSettingsSO s)
        {
            if (_nextAttackWaveAt < 0.0) _nextAttackWaveAt = s.firstWaveSeconds;
            if (now < _nextAttackWaveAt) return;
            // The clock runs on whatever happens below: a slot that cannot
            // send is skipped, not banked.
            _nextAttackWaveAt = now + s.waveIntervalSeconds;

            // Always draw, so the RNG stream is the same on every peer
            // whichever branch below is taken.
            float draw = _rng.NextFloat();

            SnapshotAllNodes();
            if (_allNodes.Count == 0)
            {
                UnityEngine.Debug.Log("[CurseTerritory] WAVE skipped — the curse holds no node to raise it from.");
                return;
            }
            SnapshotPlayerBuildings(em);

            Faction target;
            int originNode;
            int territory;
            float3 objective;
            bool huntsHolder = TryShardrootHolder(em, out var holder, out float3 holderPos, out int holderTerritory);
            if (huntsHolder)
            {
                // §6.6: every offensive curse force goes for the holder.
                target = holder;
                objective = holderPos;
                territory = holderTerritory;
                originNode = NearestCurseNode(holderPos);
            }
            else
            {
                // Per living player: its building nearest any curse node.
                _waveCandidates.Clear();
                for (int fi = 0; fi < (int)Faction.Border; fi++)
                {
                    var f = (Faction)fi;
                    float bestD = float.MaxValue; int bestB = -1, bestN = -1;
                    for (int b = 0; b < _playerBuildings.Count; b++)
                    {
                        if (_playerBuildings[b].F != f) continue;
                        int n = NearestCurseNode(_playerBuildings[b].P);
                        float d = Distance2(_allNodes[n].p, _playerBuildings[b].P);
                        if (d < bestD) { bestD = d; bestB = b; bestN = n; }
                    }
                    if (bestB >= 0) _waveCandidates.Add((f, bestD, bestB, bestN));
                }
                if (_waveCandidates.Count == 0)
                {
                    UnityEngine.Debug.Log("[CurseTerritory] WAVE skipped — no living player to march on.");
                    return;
                }

                int pick = 0;
                for (int i = 1; i < _waveCandidates.Count; i++)
                    if (_waveCandidates[i].d2 < _waveCandidates[pick].d2) pick = i;

                // FAIR ROTATION: the nearest player was the last one hit, and
                // someone else is nearly as close — the wave goes to them.
                if (_waveCandidates[pick].f == _lastWaveTarget)
                {
                    float slack = math.max(1f, s.waveTargetDistanceSlack);
                    float limit2 = _waveCandidates[pick].d2 * slack * slack;
                    _waveAlternates.Clear();
                    for (int i = 0; i < _waveCandidates.Count; i++)
                        if (i != pick && _waveCandidates[i].d2 <= limit2) _waveAlternates.Add(i);
                    if (_waveAlternates.Count > 0)
                        pick = _waveAlternates[math.min(_waveAlternates.Count - 1,
                                                        (int)(draw * _waveAlternates.Count))];
                }

                var c = _waveCandidates[pick];
                target = c.f;
                originNode = c.node;
                var near = _playerBuildings[c.building].P;
                territory = RegionMap.NearestRegion(near.x, near.z);
                int obj = ObjectiveIn(target, territory, _allNodes[originNode].p);
                objective = obj >= 0 ? _playerBuildings[obj].P : near;
            }
            if (originNode < 0) return;

            int home = _allNodes[originNode].t;
            float3 origin = _allNodes[originNode].p;

            // Size: waveDraftFraction of every garrison unit the curse has,
            // drafted nearest the target first (guard posts by distance, then
            // entity index), never below garrisonMinPerNode a node. Nothing
            // is spawned. Too few to draft: the slot is skipped.
            int garrison = CountGarrisonUnits(em);
            int want = (int)math.floor(garrison * math.saturate(s.waveDraftFraction));
            int count = want >= s.waveMinSize
                ? DraftFromGarrisons(em, s, objective, -1, want, _scratchWave) : 0;
            if (count < math.max(1, s.waveMinSize))
            {
                UnityEngine.Debug.Log($"[CurseTerritory] WAVE skipped — {count} of {want} drafted from " +
                    $"{garrison} garrison units, under waveMinSize {s.waveMinSize}.");
                _scratchWave.Clear();
                return;
            }

            int party = _nextPartyId++;
            // Each unit keeps its home territory; the guard post is reset
            // when the wave turns back (SendAttackWaveHome).
            Enlist(em, _scratchWave, RoleWave, party, -1, origin);
            _attackWavesSent++;
            _lastWaveTarget = target;

            _attackWaves[party] = new AttackWaveState
            {
                Number = _attackWavesSent, Home = home, Target = target, Territory = territory,
                Objective = objective, Size = count, StartedAt = now,
                NextThinkAt = now + WaveThinkSeconds, HuntsHolder = huntsHolder,
            };
            TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                em, _scratchWave, objective, FormationShape.Box, attackMove: true);

            SimSignals.Ping(origin, SimPingKind.Curse, 10f);
            SimSignals.Ping(objective, SimPingKind.Curse, 12f, big: true);
            if (target == GameSettings.LocalPlayerFaction)
                SimSignals.Notify(Loc.T("A curse wave marches on your lands!"));
            UnityEngine.Debug.Log($"[CurseTerritory] WAVE {_attackWavesSent} — {count} units " +
                $"(wanted {want} of {garrison} garrison) drafted nearest territory {home} ({RegionMap.NameOf(home)}) " +
                $"and march on {target}{(huntsHolder ? " (Shardroot holder)" : "")} in territory {territory} " +
                $"({RegionMap.NameOf(territory)}) at ({objective.x:F0},{objective.z:F0}); " +
                $"curse units {CurseUnitCap.Live(em)}/{CurseUnitCap.Max}; next in {s.waveIntervalSeconds:F0}s.");
        }

        // ── shepherding a wave ──────────────────────────────────────────────

        private readonly List<int> _scratchWaveIds = new();

        /// <summary>
        /// Each wave's think (every WaveThinkSeconds): fight while anyone is
        /// fighting; when idle, re-target inside the target territory; turn
        /// home on time, on losses, or when the target has nothing left.
        /// Reads the roster ShepherdLiving already snapshotted.
        /// </summary>
        private void ShepherdAttackWaves(EntityManager em, double now, BorderSettingsSO s,
                                         NativeArray<Entity> ents,
                                         NativeArray<CurseLivingMember> members,
                                         NativeArray<LocalTransform> xfs,
                                         bool holderKnown, float3 holderAt)
        {
            if (_attackWaves.Count == 0) return;

            _scratchWaveIds.Clear();
            foreach (var kv in _attackWaves) _scratchWaveIds.Add(kv.Key);
            _scratchWaveIds.Sort();

            bool snapped = false;
            for (int k = 0; k < _scratchWaveIds.Count; k++)
            {
                int party = _scratchWaveIds[k];
                var ws = _attackWaves[party];
                if (now < ws.NextThinkAt) continue;
                ws.NextThinkAt = now + WaveThinkSeconds;

                _scratchWave.Clear();
                float3 centre = float3.zero;
                int fighting = 0, moving = 0;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (members[i].Party != party || members[i].Role != RoleWave) continue;
                    var e = ents[i];
                    _scratchWave.Add(e);
                    centre += xfs[i].Position;
                    if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) fighting++;
                    if (em.HasComponent<DesiredDestination>(e) && em.GetComponentData<DesiredDestination>(e).Has != 0) moving++;
                }
                if (_scratchWave.Count == 0)
                {
                    UnityEngine.Debug.Log($"[CurseTerritory] WAVE {ws.Number} destroyed by {ws.Target}.");
                    _attackWaves.Remove(party);
                    continue;
                }
                centre /= _scratchWave.Count;

                // Time is up, or the wave is broken: home.
                if (now - ws.StartedAt >= s.waveDurationSeconds)
                {
                    SendAttackWaveHome(em, party, ws, centre, "its time is up");
                    continue;
                }
                if (_scratchWave.Count < ws.Size * s.waveRetreatFraction)
                {
                    SendAttackWaveHome(em, party, ws, centre,
                        $"it is broken ({_scratchWave.Count}/{ws.Size} left)");
                    continue;
                }

                // A unit mid-fight is left to it.
                if (fighting > 0) continue;

                // The holder hunt tracks the holder; a lapsed hunt falls back
                // to the holder's own ground like any other wave.
                if (ws.HuntsHolder && !holderKnown) ws.HuntsHolder = false;
                if (ws.HuntsHolder)
                {
                    if (moving > 0 && Distance2(holderAt, ws.Objective) <= WaveRetargetDistance * WaveRetargetDistance)
                        continue;
                    ws.Objective = holderAt;
                    ws.Territory = RegionMap.NearestRegion(holderAt.x, holderAt.z);
                    TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                        em, _scratchWave, ws.Objective, FormationShape.Box, attackMove: true);
                    continue;
                }

                // Still marching on its objective: let it arrive.
                if (moving > 0) continue;

                // Idle: the nearest hostile unit in the target territory,
                // else the target's nearest building there, else the target's
                // nearest building anywhere (whose territory becomes the one).
                if (!snapped) { SnapshotPlayerBuildings(em); snapped = true; }
                float3 next = default; bool found = false;
                if (_hostilesByTerritory.TryGetValue(ws.Territory, out var intruders))
                {
                    float bestD = float.MaxValue;
                    for (int i = 0; i < intruders.Count; i++)
                    {
                        float d = Distance2(intruders[i], centre);
                        if (d < bestD) { bestD = d; next = intruders[i]; found = true; }
                    }
                }
                if (!found)
                {
                    int b = ObjectiveIn(ws.Target, ws.Territory, centre);
                    if (b < 0)
                    {
                        b = NearestBuildingOf(ws.Target, centre);
                        if (b >= 0)
                            ws.Territory = RegionMap.NearestRegion(_playerBuildings[b].P.x, _playerBuildings[b].P.z);
                    }
                    if (b >= 0) { next = _playerBuildings[b].P; found = true; }
                }
                if (!found)
                {
                    SendAttackWaveHome(em, party, ws, centre, $"{ws.Target} has nothing left standing");
                    continue;
                }
                ws.Objective = next;
                TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                    em, _scratchWave, next, FormationShape.Box, attackMove: true);
            }
        }

        /// <summary>The wave turns back (§6.8): its units become garrison of
        /// the curse node nearest them at once, and the §6.7 leash walks them
        /// there — a fight past guardLeashRadius is dropped.</summary>
        private void SendAttackWaveHome(EntityManager em, int party, AttackWaveState ws,
                                        float3 centre, string why)
        {
            SnapshotAllNodes();
            int n = NearestCurseNode(centre);
            int home = n >= 0 ? _allNodes[n].t : ws.Home;
            float3 guard = n >= 0 ? _allNodes[n].p : WaveOrigin(em, ws.Home);

            for (int i = 0; i < _scratchWave.Count; i++)
            {
                var e = _scratchWave[i];
                var m = em.GetComponentData<CurseLivingMember>(e);
                m.Home = home; m.Role = RoleGarrison; m.Party = -1; m.Guard = guard;
                em.SetComponentData(e, m);
                TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute(em, e, guard);
            }
            _attackWaves.Remove(party);
            UnityEngine.Debug.Log($"[CurseTerritory] WAVE {ws.Number} turns home — {why}; " +
                $"{_scratchWave.Count} unit(s) rejoin the garrison of territory {home} ({RegionMap.NameOf(home)}).");
        }
    }
}
