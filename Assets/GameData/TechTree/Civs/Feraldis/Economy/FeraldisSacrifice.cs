// FeraldisSacrifice.cs
// Melt Down (the Great Forge), sacrifice (the Blood Altar) and field
// sacrifice (a slave, anywhere). docs/Design/Age_1_Feraldis.md §4.3, §5;
// docs/Design/Religion.md §7.
//
// 2026-10-13: BOTH sacrifices pay religion points — drawing blood is the
// Feraldis religion, Altar or no Altar (a curse-kill requirement soft-locked
// a Feraldis player with no RP). A sacrifice at the Altar, or a field
// sacrifice within altarSacrificeRadius of one, pays altarSacrificeMultiplier
// times as much (FeraldisEconomy.asset). Enemy kills pay too
// (FeraldisRaidIncomeSystem).

using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Influence;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    public static class FeraldisSacrifice
    {
        /// <summary>
        /// A unit killed by its own side's ritual: no kill credit (the last
        /// enemy to scratch it must not be paid pillage or XP for it), then the
        /// ordinary death — corpse, blood splat — through DeathSystem.
        /// </summary>
        public static void KillOwn(EntityManager em, Entity unit)
        {
            if (em.HasComponent<LastDamagedByFaction>(unit))
                em.SetComponentEnabled<LastDamagedByFaction>(unit, false);
            if (!em.HasComponent<Health>(unit)) { em.DestroyEntity(unit); return; }
            var hp = em.GetComponentData<Health>(unit);
            hp.Value = 0;
            em.SetComponentData(unit, hp);
        }

        /// <summary>The unit's training cost, from its stamped id.</summary>
        public static Cost UnitCost(EntityManager em, Entity unit)
        {
            if (!em.HasComponent<UnitTypeId>(unit)) return default;
            return UnitCosts.Get(em.GetComponentData<UnitTypeId>(unit).Value.ToString());
        }

        /// <summary>Melt a soldier at the Forge: a share of its cost back, scaled by its HP fraction (§4.3).</summary>
        public static void MeltDown(EntityManager em, Entity unit, Entity forge)
        {
            if (!FeraldisOrders.CanMelt(em, unit)) return;
            var faction = em.GetComponentData<FactionTag>(unit).Value;
            float hpFrac = 1f;
            if (em.HasComponent<Health>(unit))
            {
                var h = em.GetComponentData<Health>(unit);
                hpFrac = h.Max > 0 ? math.saturate(h.Value / (float)h.Max) : 0f;
            }
            float share = FeraldisEconomy.Cfg.meltRefundShare * hpFrac;
            var c = UnitCost(em, unit);
            var refund = Cost.Of(
                supplies: Mathf.FloorToInt(c.Supplies * share),
                iron: Mathf.FloorToInt(c.Iron * share),
                veilstone: Mathf.FloorToInt(c.Veilstone * share),
                veilsteel: Mathf.FloorToInt(c.Veilsteel * share));
            if (!refund.IsZero) FactionEconomy.Add(em, faction, refund, IncomeSource.Refund);
            KillOwn(em, unit);
        }

        /// <summary>
        /// Religion pts a sacrifice of this unit pays (§5, Religion.md §7):
        /// its whole cost x sacrificePtsPerCost, at least 1, multiplied by
        /// altarSacrificeMultiplier when it is made at the Altar.
        /// </summary>
        public static int SacrificePoints(EntityManager em, Entity unit, bool atAltar)
        {
            var cfg = FeraldisEconomy.Cfg;
            var c = UnitCost(em, unit);
            int total = c.Supplies + c.Iron + c.Veilstone + c.Veilsteel;
            int pts = Mathf.Max(1, Mathf.RoundToInt(total * cfg.sacrificePtsPerCost));
            if (atAltar) pts = Mathf.Max(pts, Mathf.RoundToInt(pts * cfg.altarSacrificeMultiplier));
            return pts;
        }

        /// <summary>
        /// Bank drawn-blood pts for a Feraldis faction through the escalating
        /// RP ladder (FactionReligionPointsHelper.AddKillPoints) — no Altar
        /// or Temple involved — and tell its player about a new RP.
        /// </summary>
        public static int PayBloodPoints(EntityManager em, Faction faction, int pts)
        {
            int gained = FactionReligionPointsHelper.AddKillPoints(em, faction, pts);
            if (gained > 0 && faction == GameSettings.LocalPlayerFaction)
                SimSignals.Notify(string.Format(
                    TheWaningBorder.Core.Localization.Loc.T("+{0} Religion Point — blood drawn"), gained));
            return gained;
        }

        /// <summary>Sacrifice at the Blood Altar: religion points scaled by cost, at the Altar's
        /// multiplier, through the RP ladder (§5).</summary>
        public static void AltarSacrifice(EntityManager em, Entity unit, Entity altar)
        {
            if (!FeraldisOrders.CanSacrifice(em, unit)) return;
            var faction = em.GetComponentData<FactionTag>(unit).Value;
            PayBloodPoints(em, faction, SacrificePoints(em, unit, atAltar: true));
            KillOwn(em, unit);
        }

        static readonly ComponentType[] QT_Altars =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_Altars;

        /// <summary>True when <paramref name="p"/> lies within altarSacrificeRadius of one of
        /// <paramref name="faction"/>'s finished, standing Blood Altars.</summary>
        public static bool NearOwnAltar(EntityManager em, Faction faction, Vector3 p)
        {
            float r = FeraldisEconomy.Cfg.altarSacrificeRadius;
            if (r <= 0f) return false;
            var q = QC_Altars.Get(em, QT_Altars);
            if (q.IsEmpty) return false;
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            float r2 = r * r;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<Health>(ents[i]) && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                float dx = xfs[i].Position.x - p.x, dz = xfs[i].Position.z - p.z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>A slave sacrificed where it stands: a blood pool, and religion points —
        /// at the Altar's multiplier when it stands within the Altar's radius (§5).</summary>
        public static void FieldSacrifice(EntityManager em, Entity slave)
        {
            if (!em.HasComponent<SlaveTag>(slave) || !em.HasComponent<LocalTransform>(slave)) return;
            var cfg = FeraldisEconomy.Cfg;
            Vector3 p = em.GetComponentData<LocalTransform>(slave).Position;
            if (em.HasComponent<FactionTag>(slave))
            {
                var faction = em.GetComponentData<FactionTag>(slave).Value;
                PayBloodPoints(em, faction, SacrificePoints(em, slave, NearOwnAltar(em, faction, p)));
            }
            BloodMap.AddBlood(p, cfg.fieldSacrificeBlood);
            int ring = Mathf.Max(0, cfg.fieldSacrificeRing);
            for (int i = 0; i < ring; i++)
            {
                float a = i * (2f * Mathf.PI / ring);
                BloodMap.AddBlood(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f, cfg.fieldSacrificeBlood);
            }
            KillOwn(em, slave);
        }
    }
}
