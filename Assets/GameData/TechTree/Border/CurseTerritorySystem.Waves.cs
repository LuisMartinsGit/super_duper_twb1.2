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
//   TickAttackWaves      picks the target (2026-10-05: by STRENGTH — each
//                        living player's share blends army power and
//                        territories, accrues as credit, and the eligible
//                        player with the most credit takes the wave; a
//                        per-player cooldown after each wave; the Shardroot
//                        holder while anyone holds it), the objective (the
//                        player's nearest production building in their
//                        nearest territory), drafts the wave SIZED TO THE
//                        TARGET (waveSizeVsPower x its army power x its
//                        difficulty multiplier, waveMinSize..waveDraftFraction
//                        of the garrisons) and sends it as one formation
//                        attack-move. Fewer than waveMinSize: slot skipped.
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

        // ── fair targeting (Territory_Claims.md §6.8, 2026-10-05), one slot per player faction ──
        private const int PlayerSlots = (int)Faction.Border;
        /// <summary>Accrued wave share minus waves received (the schedule).</summary>
        private readonly float[] _waveCredit = new float[PlayerSlots];
        /// <summary>Sim time before which the faction may not be targeted.</summary>
        private readonly double[] _waveCooldownUntil = new double[PlayerSlots];
        private readonly bool[] _wsLiving = new bool[PlayerSlots];
        private readonly float[] _wsArmy = new float[PlayerSlots];
        private readonly int[] _wsTerr = new int[PlayerSlots];
        private readonly float[] _wsShare = new float[PlayerSlots];
        private const float WaveCreditClamp = 2f;

        private void ResetAttackWaves()
        {
            _attackWaves.Clear();
            _nextAttackWaveAt = -1.0;
            _attackWavesSent = 0;
            System.Array.Clear(_waveCredit, 0, PlayerSlots);
            System.Array.Clear(_waveCooldownUntil, 0, PlayerSlots);
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

            // One draw per slot, on every peer whichever branch below is
            // taken. The target pick no longer uses it (2026-10-05: the
            // credit schedule is deterministic by itself); it is kept so the
            // stream other curse draws read stays where it was.
            _rng.NextFloat();

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
            int living = ComputeWaveShares(em, s);
            bool huntsHolder = TryShardrootHolder(em, out var holder, out float3 holderPos, out int holderTerritory);
            if (huntsHolder)
            {
                // §6.6: every offensive curse force goes for the
                // holder — shares and cooldowns do not apply.
                target = holder;
                objective = holderPos;
                territory = holderTerritory;
                originNode = NearestCurseNode(holderPos);
            }
            else
            {
                if (living == 0)
                {
                    UnityEngine.Debug.Log("[CurseTerritory] WAVE skipped — no living player to march on.");
                    return;
                }
                LogWaveShares(now);

                // FAIR TARGETING (§6.8, 2026-10-05): the eligible player with
                // the most credit once this slot's share is added; faction
                // order breaks ties, the same on every peer.
                int pickF = -1; float best = float.MinValue;
                for (int f = 0; f < PlayerSlots; f++)
                {
                    if (!_wsLiving[f] || !WaveEligible((Faction)f, now)) continue;
                    float c = _waveCredit[f] + _wsShare[f];
                    if (c > best) { best = c; pickF = f; }
                }
                if (pickF < 0)
                {
                    UnityEngine.Debug.Log($"[CurseTerritory] WAVE skipped — every living player is under a wave " +
                        $"or cooling down (wavePlayerCooldownSeconds {s.wavePlayerCooldownSeconds:F0}).");
                    return;
                }
                target = (Faction)pickF;

                // Where: the target's building nearest any curse node, and
                // that node as the origin.
                float bestD = float.MaxValue; int bestB = -1, bestN = -1;
                for (int b = 0; b < _playerBuildings.Count; b++)
                {
                    if (_playerBuildings[b].F != target) continue;
                    int n = NearestCurseNode(_playerBuildings[b].P);
                    float d = Distance2(_allNodes[n].p, _playerBuildings[b].P);
                    if (d < bestD) { bestD = d; bestB = b; bestN = n; }
                }
                if (bestB < 0) return;   // unreachable: living means a building
                originNode = bestN;
                var near = _playerBuildings[bestB].P;
                territory = RegionMap.NearestRegion(near.x, near.z);
                int obj = ObjectiveIn(target, territory, _allNodes[originNode].p);
                objective = obj >= 0 ? _playerBuildings[obj].P : near;
            }
            if (originNode < 0) return;

            int home = _allNodes[originNode].t;
            float3 origin = _allNodes[originNode].p;

            // Size: at most waveDraftFraction of every garrison unit the
            // curse has, drafted nearest the target first (guard posts by
            // distance, then entity index), never below garrisonMinPerNode a
            // node. Nothing is spawned. SIZED TO THE TARGET (§6.8,
            // 2026-10-05): under that ceiling the draft stops once its power
            // meets waveSizeVsPower x the target's army power x the target's
            // difficulty multiplier — never under waveMinSize. The holder
            // hunt takes the whole ceiling.
            int garrison = CountGarrisonUnits(em);
            int want = (int)math.floor(garrison * math.saturate(s.waveDraftFraction));
            int ti = (int)target;
            bool playerSlot = ti >= 0 && ti < PlayerSlots;
            float targetPower = playerSlot ? _wsArmy[ti] : 0f;
            int difficulty = DifficultyOf(target);
            float diffMult = s.WaveSizeForDifficulty(difficulty);
            float budget = huntsHolder ? 0f : math.max(0f, s.waveSizeVsPower) * diffMult * targetPower;
            int minCount = math.max(1, s.waveMinSize);
            int count = want >= minCount
                ? DraftFromGarrisons(em, s, objective, -1, want, _scratchWave,
                                     minCount, huntsHolder ? 0f : math.max(budget, 0.001f))
                : 0;
            if (count < minCount)
            {
                UnityEngine.Debug.Log($"[CurseTerritory] WAVE skipped — {count} of {want} drafted from " +
                    $"{garrison} garrison units, under waveMinSize {s.waveMinSize} (target {target}).");
                _scratchWave.Clear();
                return;
            }
            float wavePower = _lastDraftPower;

            int party = _nextPartyId++;
            // Each unit keeps its home territory; the guard post is reset
            // when the wave turns back (SendAttackWaveHome).
            Enlist(em, _scratchWave, RoleWave, party, -1, origin);
            _attackWavesSent++;
            float share = playerSlot ? _wsShare[ti] : 0f;
            if (!huntsHolder && playerSlot)
            {
                // The schedule: every living player accrues its share, the
                // target pays one wave for it.
                for (int f = 0; f < PlayerSlots; f++)
                    if (_wsLiving[f]) _waveCredit[f] = math.min(WaveCreditClamp, _waveCredit[f] + _wsShare[f]);
                _waveCredit[ti] = math.max(-WaveCreditClamp, _waveCredit[ti] - 1f);
            }

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
            UnityEngine.Debug.Log($"[CurseTerritory] WAVE {_attackWavesSent} -> {target}" +
                $"{(huntsHolder ? " (Shardroot holder)" : "")} (share {share:F2}, size {count} vs power {targetPower:F0}; " +
                $"wave power {wavePower:F0}, budget {budget:F0} = {s.waveSizeVsPower:F2} x power x {DifficultyName(difficulty)} " +
                $"{diffMult:F2}; ceiling {want} of {garrison} garrison) — drafted nearest territory {home} " +
                $"({RegionMap.NameOf(home)}), marching on territory {territory} " +
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
                    StartWaveCooldown(ws.Target, now, s);
                    _attackWaves.Remove(party);
                    continue;
                }
                centre /= _scratchWave.Count;

                // Time is up, or the wave is broken: home.
                if (now - ws.StartedAt >= s.waveDurationSeconds)
                {
                    SendAttackWaveHome(em, now, s, party, ws, centre, "its time is up");
                    continue;
                }
                if (_scratchWave.Count < ws.Size * s.waveRetreatFraction)
                {
                    SendAttackWaveHome(em, now, s, party, ws, centre,
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
                    SendAttackWaveHome(em, now, s, party, ws, centre, $"{ws.Target} has nothing left standing");
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
        private void SendAttackWaveHome(EntityManager em, double now, BorderSettingsSO s,
                                        int party, AttackWaveState ws, float3 centre, string why)
        {
            StartWaveCooldown(ws.Target, now, s);
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

        // ── fair targeting helpers (Territory_Claims.md §6.8, 2026-10-05) ──

        /// <summary>The faction may not be targeted for
        /// wavePlayerCooldownSeconds after a wave against it ended.</summary>
        private void StartWaveCooldown(Faction f, double now, BorderSettingsSO s)
        {
            int i = (int)f;
            if (i < 0 || i >= PlayerSlots) return;
            _waveCooldownUntil[i] = now + math.max(0f, s.wavePlayerCooldownSeconds);
        }

        /// <summary>No wave is out against the faction and its cooldown is over.</summary>
        private bool WaveEligible(Faction f, double now)
        {
            int i = (int)f;
            if (i < 0 || i >= PlayerSlots || now < _waveCooldownUntil[i]) return false;
            foreach (var kv in _attackWaves)
                if (kv.Value.Target == f) return false;
            return true;
        }

        private static readonly ComponentType[] QT_PlayerArmy =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<Damage>(),
        };
        private static CachedEntityQuery QC_PlayerArmy;

        /// <summary>
        /// A unit's live combat power: the geometric mean of its damage per
        /// second and its current HP — the core of UnitPower's Combat number
        /// (docs/Design/Unit_Power.md), read off live components so wounds
        /// and upgrades count. The same scale for curse and player units, so
        /// a wave can be sized against an army (§6.8). 0 for a unit that
        /// deals no damage.
        /// </summary>
        private static float CombatPowerOf(EntityManager em, Entity e)
        {
            if (!em.HasComponent<Damage>(e) || !em.HasComponent<Health>(e)) return 0f;
            int dmg = em.GetComponentData<Damage>(e).Value;
            int hp = em.GetComponentData<Health>(e).Value;
            if (dmg <= 0 || hp <= 0) return 0f;
            float cd = em.HasComponent<AttackCooldown>(e) ? em.GetComponentData<AttackCooldown>(e).Cooldown : 1f;
            float dps = dmg / math.max(0.5f, cd);
            return math.sqrt(dps * hp);
        }

        /// <summary>
        /// Fills the per-player wave snapshot (§6.8, 2026-10-05): living (a
        /// standing building — reads _playerBuildings, so
        /// SnapshotPlayerBuildings runs first), army power (CombatPowerOf
        /// over its combat units), territories held, and the wave SHARE —
        /// strength (army share and territory share blended by
        /// waveShareTerritoryWeight) floored at waveShareFloor and
        /// renormalised. Returns the living count. Walks are in entity /
        /// faction order, so every peer agrees.
        /// </summary>
        private int ComputeWaveShares(EntityManager em, BorderSettingsSO s)
        {
            System.Array.Clear(_wsLiving, 0, PlayerSlots);
            System.Array.Clear(_wsArmy, 0, PlayerSlots);
            System.Array.Clear(_wsTerr, 0, PlayerSlots);
            System.Array.Clear(_wsShare, 0, PlayerSlots);

            for (int b = 0; b < _playerBuildings.Count; b++)
            {
                int f = (int)_playerBuildings[b].F;
                if (f >= 0 && f < PlayerSlots) _wsLiving[f] = true;
            }

            var q = QC_PlayerArmy.Get(em, QT_PlayerArmy);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp))
            {
                // Summed in entity order so the float totals are the same on
                // every peer.
                _scratchArmyOrder.Clear();
                for (int i = 0; i < ents.Length; i++) _scratchArmyOrder.Add((ents[i].Index, i));
                _scratchArmyOrder.Sort((a, b) => a.idx.CompareTo(b.idx));
                for (int k = 0; k < _scratchArmyOrder.Count; k++)
                {
                    int i = _scratchArmyOrder[k].i;
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= PlayerSlots) continue;
                    var cls = tags[i].Class;
                    if (cls == UnitClass.Scout || cls == UnitClass.Economy || cls == UnitClass.Worker) continue;
                    _wsArmy[f] += CombatPowerOf(em, ents[i]);
                }
            }

            for (int r = 0; r < RegionMap.Count; r++)
            {
                int o = TerritoryOwnership.OwnerOf(r);
                if (o >= 0 && o < PlayerSlots) _wsTerr[o]++;
            }

            int living = 0; float sumArmy = 0f; int sumTerr = 0;
            for (int f = 0; f < PlayerSlots; f++)
            {
                if (!_wsLiving[f]) { _waveCredit[f] = 0f; continue; }   // gone: its credit with it
                living++; sumArmy += _wsArmy[f]; sumTerr += _wsTerr[f];
            }
            if (living == 0) return 0;

            float w = math.saturate(s.waveShareTerritoryWeight);
            float floor = math.saturate(s.waveShareFloor);
            float even = 1f / living, total = 0f;
            for (int f = 0; f < PlayerSlots; f++)
            {
                if (!_wsLiving[f]) continue;
                float a = sumArmy > 0f ? _wsArmy[f] / sumArmy : even;
                float t = sumTerr > 0 ? (float)_wsTerr[f] / sumTerr : even;
                _wsShare[f] = math.max(floor, (1f - w) * a + w * t);
                total += _wsShare[f];
            }
            for (int f = 0; f < PlayerSlots; f++)
                if (_wsLiving[f]) _wsShare[f] = total > 0f ? _wsShare[f] / total : even;
            return living;
        }

        private readonly List<(int idx, int i)> _scratchArmyOrder = new();
        private readonly System.Text.StringBuilder _shareLog = new();

        /// <summary>One line per wave slot: every living player's share, army
        /// power, territories, credit, difficulty and whether it can be
        /// targeted.</summary>
        private void LogWaveShares(double now)
        {
            _shareLog.Clear();
            _shareLog.Append("[CurseTerritory] WAVE shares —");
            for (int f = 0; f < PlayerSlots; f++)
            {
                if (!_wsLiving[f]) continue;
                var fac = (Faction)f;
                _shareLog.Append(' ').Append(fac).Append(' ').Append(_wsShare[f].ToString("F2"))
                    .Append(" (army ").Append(_wsArmy[f].ToString("F0"))
                    .Append(", ").Append(_wsTerr[f]).Append(" terr, credit ")
                    .Append(_waveCredit[f].ToString("F2")).Append(", ").Append(DifficultyName(DifficultyOf(fac)));
                if (!WaveEligible(fac, now))
                    _shareLog.Append(now < _waveCooldownUntil[f]
                        ? $", cooling until {_waveCooldownUntil[f]:F0}s" : ", wave out");
                _shareLog.Append(')');
            }
            UnityEngine.Debug.Log(_shareLog.ToString());
        }

        /// <summary>The faction's lobby AI difficulty as a LobbyAIDifficulty
        /// index (0 Easy .. 3 Expert); a human, or a slot the lobby does not
        /// know, counts as Normal (§6.8). Read from the lobby config, which is
        /// the same on every peer — not from the AI's own state.</summary>
        private static int DifficultyOf(Faction f)
        {
            const int Normal = (int)TheWaningBorder.Core.Config.LobbyAIDifficulty.Normal;
            int i = (int)f;
            var slots = TheWaningBorder.Core.Config.LobbyConfig.Slots;
            if (slots == null || i < 0 || i >= slots.Length || slots[i] == null) return Normal;
            var slot = slots[i];
            bool ai = slot.Type == TheWaningBorder.Core.Config.SlotType.AI
                      || (GameSettings.IsObserver && slot.Type == TheWaningBorder.Core.Config.SlotType.Observer);
            return ai ? (int)slot.AIDifficulty : Normal;
        }

        private static string DifficultyName(int d)
            => ((TheWaningBorder.Core.Config.LobbyAIDifficulty)d).ToString();
    }
}
