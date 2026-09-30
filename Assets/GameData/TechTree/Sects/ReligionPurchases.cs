// ReligionPurchases.cs
// WHAT RELIGION POINTS BUY after the chapel (docs/Design/Religion.md §1.1,
// §3.1, 2026-09-29).
//
//   Tithe          resources -> 1 RP, dearer each time (at the Temple)
//   UnlockActive   the sect's next active: the other counterpart (1 RP),
//                  then the wildcard (2 RP)
//   ChapelLevel    the chapel's level — the level of every power it has
//                  unlocked: II (2 RP), III (3 RP)
//
// One entry point for both sides: CanBuy is the issuing peer's check (and
// the UI's), TryBuy is the executor every lockstep peer runs at the same
// tick through CommandRouter.ReligionPurchaseDirect. TryBuy re-checks
// everything CanBuy does, so a stale or replayed purchase spends nothing.

using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public enum ReligionPurchaseKind : byte
    {
        Tithe = 0,
        UnlockActive = 1,
        ChapelLevel = 2,
    }

    public static class ReligionPurchases
    {
        /// <summary>
        /// Can <paramref name="faction"/> make this purchase now? Sets
        /// <paramref name="rpCost"/> to the RP it costs (0 for the Tithe,
        /// which is paid in resources — see
        /// <see cref="FactionReligionPointsHelper.TitheCost"/>).
        /// </summary>
        public static bool CanBuy(EntityManager em, Faction faction, ReligionPurchaseKind kind,
                                  string sectId, out int rpCost)
        {
            rpCost = 0;
            var cfg = FactionReligionPointsHelper.Cfg;
            switch (kind)
            {
                case ReligionPurchaseKind.Tithe:
                    if (!HasTemple(em, faction)) return false;
                    return FactionEconomy.CanAfford(em, faction,
                        FactionReligionPointsHelper.TitheCost(em, faction));

                case ReligionPurchaseKind.UnlockActive:
                {
                    if (!TryGetSect(em, faction, sectId, out _, out _, out var sect)) return false;
                    int unlocked = sect.UnlockedActives < 1 ? 1 : sect.UnlockedActives;
                    if (unlocked >= SectLeverEffects.ActiveSlots) return false;
                    rpCost = unlocked == 1 ? cfg.unlockSecondRp : cfg.unlockWildcardRp;
                    return FactionReligionPointsHelper.CanAfford(em, faction, rpCost);
                }

                case ReligionPurchaseKind.ChapelLevel:
                {
                    if (!TryGetSect(em, faction, sectId, out _, out _, out var sect)) return false;
                    int level = sect.PowerLevel < 1 ? 1 : sect.PowerLevel;
                    if (level >= 3) return false;
                    rpCost = level == 1 ? cfg.chapelLevel2Rp : cfg.chapelLevel3Rp;
                    return FactionReligionPointsHelper.CanAfford(em, faction, rpCost);
                }
            }
            return false;
        }

        /// <summary>Executor side. Returns true when the purchase landed.</summary>
        public static bool TryBuy(EntityManager em, Faction faction, ReligionPurchaseKind kind, string sectId)
        {
            if (!CanBuy(em, faction, kind, sectId, out int rpCost)) return false;

            if (kind == ReligionPurchaseKind.Tithe)
                return FactionReligionPointsHelper.TryBuyTithe(em, faction);

            if (!TryGetSect(em, faction, sectId, out var bank, out int idx, out var sect)) return false;
            if (!FactionReligionPointsHelper.TrySpend(em, faction, rpCost)) return false;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            if (kind == ReligionPurchaseKind.UnlockActive)
                sect.UnlockedActives = (byte)((sect.UnlockedActives < 1 ? 1 : sect.UnlockedActives) + 1);
            else
                sect.PowerLevel = (byte)((sect.PowerLevel < 1 ? 1 : sect.PowerLevel) + 1);
            state.Set(idx, sect);
            em.SetComponentData(bank, state);
            return true;
        }

        private static bool TryGetSect(EntityManager em, Faction faction, string sectId,
                                       out Entity bank, out int idx, out PerSectState sect)
        {
            sect = default;
            bank = Entity.Null;
            idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return false;
            if (!FactionEconomy.TryGetBank(em, faction, out bank)) return false;
            if (!em.HasComponent<SectAdoptionState>(bank)) return false;
            sect = em.GetComponentData<SectAdoptionState>(bank).Get(idx);
            return sect.IsAdopted;
        }

        private static bool HasTemple(EntityManager em, Faction faction)
            => TheWaningBorder.Entities.BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(em, faction) > 0;
    }
}
