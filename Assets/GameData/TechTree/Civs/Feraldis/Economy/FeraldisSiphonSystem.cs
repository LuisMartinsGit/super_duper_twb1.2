// FeraldisSiphonSystem.cs
// SIPHON (docs/Design/Age_1_Feraldis.md §3.5, 2026-10-12): while a Feraldis
// faction is hitting an enemy extractor (Gatherer's Hut, Mine, Veilstone
// Mine), Trading Outpost or capital, supplies drain from that enemy's bank to
// the raider's — never below zero, and only what was actually taken is paid.
//
// A hit is seen as a drop in the building's HP with a Feraldis last damager;
// the building then counts as raided for siphonWindowSeconds. Supersedes the
// FeraldisLowHpRewardSystem, which MINTED supplies for damaging any building
// below 25 % HP.

using System.Collections.Generic;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Economy;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    /// <summary>A building currently being raided by a Feraldis faction.</summary>
    public struct SiphonRaid : IComponentData
    {
        public Faction Raider;
        public float SecondsLeft;
        /// <summary>Fractional supplies owed but not yet whole.</summary>
        public float Carry;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisSiphonSystem : SystemBase
    {
        private const float Interval = 0.5f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Buildings;

        private readonly Dictionary<Entity, int> _lastHp = new Dictionary<Entity, int>();
        /// <summary>Per-match state must reset with the match (a second match
        /// in the same process reuses entity indices — SimCadence.Epoch).</summary>
        private int _epoch = -1;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Interval)) return;
            var em = EntityManager;
            var cfg = FeraldisEconomy.Cfg;
            if (_epoch != SimCadence.Epoch) { _lastHp.Clear(); _epoch = SimCadence.Epoch; }

            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var seen = new HashSet<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                var b = ents[i];
                seen.Add(b);
                if (!IsSiphonTarget(em, b)) continue;

                int now = hps[i].Value;
                bool hit = _lastHp.TryGetValue(b, out int prev) && now < prev && now > 0;
                _lastHp[b] = now;

                var owner = facs[i].Value;
                if (hit && TransientState.Active<LastDamagedByFaction>(em, b))
                {
                    var raider = em.GetComponentData<LastDamagedByFaction>(b).Value;
                    if (raider != owner && raider != Faction.Border && owner != Faction.Border
                        && Alliances.AreHostile(raider, owner) && FeraldisEconomy.IsFeraldis(em, raider))
                    {
                        var raid = em.HasComponent<SiphonRaid>(b) ? em.GetComponentData<SiphonRaid>(b) : default;
                        raid.Raider = raider;
                        raid.SecondsLeft = cfg.siphonWindowSeconds;
                        if (em.HasComponent<SiphonRaid>(b)) em.SetComponentData(b, raid);
                        else em.AddComponentData(b, raid);
                    }
                }

                if (!em.HasComponent<SiphonRaid>(b)) continue;
                var r = em.GetComponentData<SiphonRaid>(b);
                r.SecondsLeft -= Interval;
                if (r.SecondsLeft <= 0f) { em.RemoveComponent<SiphonRaid>(b); continue; }

                r.Carry += cfg.siphonPerSecond * Interval;
                int want = Mathf.FloorToInt(r.Carry);
                if (want > 0 && FactionEconomy.TryGetResources(em, owner, out var bank))
                {
                    int take = Mathf.Min(want, bank.Supplies);
                    if (take > 0 && FactionEconomy.Spend(em, owner, Cost.Of(supplies: take), SpendCategory.Other))
                        FactionEconomy.Add(em, r.Raider, Cost.Of(supplies: take), IncomeSource.Loot);
                    r.Carry -= want;
                }
                em.SetComponentData(b, r);
            }

            // Forget buildings that no longer exist.
            if (_lastHp.Count > seen.Count + 64)
            {
                var gone = new List<Entity>();
                foreach (var k in _lastHp.Keys) if (!seen.Contains(k)) gone.Add(k);
                foreach (var k in gone) _lastHp.Remove(k);
            }
        }

        /// <summary>Economic buildings a raid drains: extractors, Trading Outposts and the capital.</summary>
        public static bool IsSiphonTarget(EntityManager em, Entity b)
            => em.HasComponent<GathererHutTag>(b) || em.HasComponent<MineTag>(b)
               || em.HasComponent<VeilstoneMineTag>(b) || em.HasComponent<TradingOutpostTag>(b)
               || em.HasComponent<HallTag>(b);
    }
}
