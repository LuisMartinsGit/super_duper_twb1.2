// CurseTerritorySystem.ArmyConversion.cs
// THE CURSE TAKES AN ARMY (Territory_Claims.md § 6.10, 2026-10-18). Every
// `armyConversionSeconds` the curse picks a random player that has an army,
// seizes that player's largest body of soldiers (up to
// `armyConversionMaxUnits`, heroes never) and turns it on a random OTHER
// player at once, as an attack wave.
//
//   * the units become the curse's (FactionTag -> Border), keep every upgrade
//     they had (research is written into a unit's own stats when it lands,
//     TechEffectSystem), and leave their old owner's control and population;
//   * they are VISIBLE TO EVERY PLAYER wherever they go (CurseRevealed: fog
//     and minimap), so the seized army is never a hidden threat;
//   * they march as a curse attack wave (CurseLivingMember RoleWave): the
//     wave shepherd re-targets them and sends them home like any wave.
//
// This is the curse's power, distinct from the Sect of Antiquity's Friendly
// Fire (which never changes a unit's owner).
//
// Lockstep: runs on the curse's check clock; draws from the seeded _rng a
// fixed two values per slot whether or not a conversion happens; walks units
// in a sorted, peer-stable order (entity index).

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data.Border;
using TheWaningBorder.World.Regions;

/// <summary>A unit the curse seized from a player (§ 6.10): visible to every
/// player wherever it is (FogOfWarSystem, the minimap).</summary>
public struct CurseRevealed : IComponentData { }

/// <summary>Who a curse-seized unit belonged to (§ 6.10).</summary>
public struct CurseConverted : IComponentData
{
    public Faction From;
}

namespace TheWaningBorder.Systems.Border
{
    public partial class CurseTerritorySystem
    {
        private double _nextConversionAt = -1.0;
        private int _conversionsDone;

        private static readonly ComponentType[] QT_ConvertCandidates =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.Exclude<BuildingTag>(),
            ComponentType.Exclude<HeroLevel>(),
            ComponentType.Exclude<NotControllableTag>(),
        };
        private static CachedEntityQuery QC_ConvertCandidates;

        private readonly List<(Entity e, float3 p)>[] _convByFaction = NewConvLists();
        private readonly List<int> _convComponent = new();
        private readonly List<Entity> _convPicked = new();

        private static List<(Entity e, float3 p)>[] NewConvLists()
        {
            var a = new List<(Entity e, float3 p)>[PlayerSlots];
            for (int i = 0; i < a.Length; i++) a[i] = new List<(Entity e, float3 p)>();
            return a;
        }

        private void ResetArmyConversion()
        {
            _nextConversionAt = -1.0;
            _conversionsDone = 0;
        }

        private void TickArmyConversion(EntityManager em, double now, BorderSettingsSO s)
        {
            if (s.armyConversionSeconds <= 0f) return;
            if (_nextConversionAt < 0.0) _nextConversionAt = s.armyConversionSeconds;
            if (now < _nextConversionAt) return;
            _nextConversionAt = now + s.armyConversionSeconds;
            // two draws a slot, always (the stream other curse draws read
            // stays aligned on every peer whatever happens below)
            float rollVictim = _rng.NextFloat();
            float rollTarget = _rng.NextFloat();

            // ── every player's soldiers ──
            for (int f = 0; f < PlayerSlots; f++) _convByFaction[f].Clear();
            var q = QC_ConvertCandidates.Get(em, QT_ConvertCandidates);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var units = q.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var hps = q.ToComponentDataArray<Health>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= PlayerSlots || hps[i].Value <= 0) continue;
                    if (!Alliances.AreHostile(Faction.Border, facs[i].Value)) continue;   // at peace with the curse
                    var cls = units[i].Class;
                    if (cls == UnitClass.Worker || cls == UnitClass.Economy || cls == UnitClass.Scout) continue;
                    if (em.HasComponent<CaravanTag>(ents[i])) continue;
                    if (em.HasComponent<TheWaningBorder.Entities.ShardboundKing>(ents[i])) continue;
                    if (TransientState.Active<SectDisordered>(em, ents[i])) continue;
                    _convByFaction[f].Add((ents[i], xfs[i].Position));
                }
            }
            int minUnits = math.max(1, s.armyConversionMinUnits);
            var victims = new List<int>();
            for (int f = 0; f < PlayerSlots; f++)
            {
                _convByFaction[f].Sort((a, b) => a.e.Index.CompareTo(b.e.Index));
                if (_convByFaction[f].Count >= minUnits) victims.Add(f);
            }
            if (victims.Count == 0)
            {
                UnityEngine.Debug.Log("[CurseTerritory] ARMY CONVERSION skipped — no player has an army of " +
                                      $"{minUnits}+ soldiers.");
                return;
            }
            int victim = victims[math.min(victims.Count - 1, (int)(rollVictim * victims.Count))];

            // ── that player's largest body of soldiers ──
            var pool = _convByFaction[victim];
            LargestCluster(pool, math.max(1f, s.armyConversionLinkRadius), _convComponent);
            if (_convComponent.Count < minUnits)
            {
                UnityEngine.Debug.Log($"[CurseTerritory] ARMY CONVERSION skipped — {(Faction)victim}'s largest " +
                                      $"body is {_convComponent.Count} soldiers, under {minUnits}.");
                return;
            }
            float3 centre = float3.zero;
            for (int i = 0; i < _convComponent.Count; i++) centre += pool[_convComponent[i]].p;
            centre /= _convComponent.Count;
            _convComponent.Sort((a, b) =>
            {
                float da = math.distancesq(pool[a].p, centre), db = math.distancesq(pool[b].p, centre);
                return da != db ? da.CompareTo(db) : pool[a].e.Index.CompareTo(pool[b].e.Index);
            });
            int take = math.min(_convComponent.Count, math.max(1, s.armyConversionMaxUnits));

            // ── whom it marches on: a random OTHER player with buildings ──
            SnapshotPlayerBuildings(em);
            var targets = new List<int>();
            for (int i = 0; i < _playerBuildings.Count; i++)
            {
                int f = (int)_playerBuildings[i].F;
                if (f != victim && !targets.Contains(f)) targets.Add(f);
            }
            targets.Sort();
            int target = targets.Count > 0
                ? targets[math.min(targets.Count - 1, (int)(rollTarget * targets.Count))]
                : victim;   // alone with the curse: it turns on its old master
            float3 objective = centre;
            float best = float.MaxValue;
            for (int pass = 0; pass < 2 && best == float.MaxValue; pass++)
                for (int i = 0; i < _playerBuildings.Count; i++)
                {
                    var b = _playerBuildings[i];
                    if ((int)b.F != target || (pass == 0 && !b.Locking)) continue;
                    float d = math.distancesq(b.P, centre);
                    if (d < best) { best = d; objective = b.P; }
                }

            // ── seize it ──
            SnapshotAllNodes();
            int home = -1;
            if (_allNodes.Count > 0)
            {
                float bd = float.MaxValue;
                for (int i = 0; i < _allNodes.Count; i++)
                {
                    float d = math.distancesq(_allNodes[i].p, centre);
                    if (d < bd) { bd = d; home = _allNodes[i].t; }
                }
            }
            if (home < 0) home = RegionMap.Ready ? RegionMap.RegionAt(centre.x, centre.z) : 0;

            int party = _nextPartyId++;
            _convPicked.Clear();
            for (int i = 0; i < take; i++)
            {
                var e = pool[_convComponent[i]].e;
                var p = pool[_convComponent[i]].p;
                em.SetComponentData(e, new FactionTag { Value = Faction.Border });
                if (!em.HasComponent<BorderTag>(e)) em.AddComponent<BorderTag>(e);
                if (!em.HasComponent<CurseRevealed>(e)) em.AddComponent<CurseRevealed>(e);
                if (em.HasComponent<CurseConverted>(e)) em.SetComponentData(e, new CurseConverted { From = (Faction)victim });
                else em.AddComponentData(e, new CurseConverted { From = (Faction)victim });
                if (em.HasComponent<Target>(e)) em.SetComponentData(e, new Target { Value = Entity.Null });
                if (em.HasComponent<TheWaningBorder.Core.Commands.Types.AttackCommand>(e))
                    em.RemoveComponent<TheWaningBorder.Core.Commands.Types.AttackCommand>(e);
                var member = new CurseLivingMember { Home = home, Role = RoleWave, Party = party, Guard = p };
                if (em.HasComponent<CurseLivingMember>(e)) em.SetComponentData(e, member);
                else em.AddComponentData(e, member);
                _convPicked.Add(e);
            }
            Enlist(em, _convPicked, RoleWave, party, home, centre);
            _conversionsDone++;
            int territory = RegionMap.Ready ? RegionMap.RegionAt(objective.x, objective.z) : -1;
            _attackWaves[party] = new AttackWaveState
            {
                Number = _attackWavesSent + _conversionsDone, Home = home, Target = (Faction)target,
                Territory = territory, Objective = objective, Size = take, StartedAt = now,
                NextThinkAt = now + WaveThinkSeconds,
            };
            TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                em, _convPicked, objective, FormationShape.Box, attackMove: true);

            SimSignals.Ping(centre, SimPingKind.Curse, 15f, big: true);
            SimSignals.Ping(objective, SimPingKind.Curse, 15f, big: true);
            SimSignals.Announce(target == victim
                ? string.Format(Loc.T("The curse has seized {0} of {1}'s soldiers — they turn on their old master!"),
                    take, FactionColors.RichName((Faction)victim))
                : string.Format(Loc.T("The curse has seized {0} of {1}'s soldiers — they march on {2}!"),
                    take, FactionColors.RichName((Faction)victim), FactionColors.RichName((Faction)target)));
            UnityEngine.Debug.Log($"[CurseTerritory] ARMY CONVERSION {_conversionsDone} -- seized {take} of " +
                $"{(Faction)victim}'s {pool.Count} soldiers (body of {_convComponent.Count}) at " +
                $"({centre.x:F0},{centre.z:F0}); marching on {(Faction)target} at ({objective.x:F0},{objective.z:F0}); " +
                $"next in {s.armyConversionSeconds:F0}s.");
        }

        /// <summary>The largest group of units linked within
        /// <paramref name="link"/> metres of each other (indices into
        /// <paramref name="pool"/>), ties to the group found first (pool order).</summary>
        private static void LargestCluster(List<(Entity e, float3 p)> pool, float link, List<int> best)
        {
            best.Clear();
            int n = pool.Count;
            var seen = new bool[n];
            var current = new List<int>();
            float l2 = link * link;
            for (int s = 0; s < n; s++)
            {
                if (seen[s]) continue;
                current.Clear();
                current.Add(s); seen[s] = true;
                for (int head = 0; head < current.Count; head++)
                {
                    var p = pool[current[head]].p;
                    for (int j = 0; j < n; j++)
                    {
                        if (seen[j]) continue;
                        if (math.distancesq(p.xz, pool[j].p.xz) > l2) continue;
                        seen[j] = true; current.Add(j);
                    }
                }
                if (current.Count > best.Count) { best.Clear(); best.AddRange(current); }
            }
        }
    }
}
