// ReligionPurchases.cs
// WHAT RELIGION POINTS BUY after the chapel (docs/Design/Religion.md §1.1,
// §3.1, 2026-09-29).
//
//   Tithe          resources -> 1 RP, dearer each time (at the Temple)
//   UnlockB        the sect's power B (unlockSecondRp)
//   UnlockWildcard the sect's wildcard (unlockWildcardRp) - B and the
//                  wildcard are independent, either order (2026-10-15)
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
        // 1 was the retired sequential UnlockActive; never reuse it (replays).
        ChapelLevel = 2,
        UnlockB = 3,
        UnlockWildcard = 4,
        // Monuments_And_Petriarchy.md: 5-8 Petriarchy support, 9-11 the army,
        // 12 exploring ruins (the sectId carries the monument's territory).
        SupportSupplies = 5,
        SupportIron = 6,
        SupportVeilstone = 7,
        SupportVeilsteel = 8,
        ArmySmall = 9,
        ArmyMedium = 10,
        ArmyLarge = 11,
        ExploreRuins = 12,
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
                    // The Tithe is sold AT the Temple (Religion.md 1.1).
                    if (!SectQuery.HasStandingTemple(em, faction)) return false;
                    return FactionEconomy.CanAfford(em, faction,
                        FactionReligionPointsHelper.TitheCost(em, faction));

                case ReligionPurchaseKind.SupportSupplies:
                case ReligionPurchaseKind.SupportIron:
                case ReligionPurchaseKind.SupportVeilstone:
                case ReligionPurchaseKind.SupportVeilsteel:
                    if (!SectQuery.HasStandingTemple(em, faction)) return false;
                    rpCost = 1;
                    return FactionReligionPointsHelper.CanAfford(em, faction, rpCost);

                case ReligionPurchaseKind.ArmySmall:
                case ReligionPurchaseKind.ArmyMedium:
                case ReligionPurchaseKind.ArmyLarge:
                    if (!SectQuery.HasStandingTemple(em, faction)) return false;
                    if (PetriarchyArmy.Roster().Count == 0) return false;
                    rpCost = ArmyPrice(kind);
                    return FactionReligionPointsHelper.CanAfford(em, faction, rpCost);

                case ReligionPurchaseKind.ExploreRuins:
                    return int.TryParse(sectId, out int territory)
                        && TheWaningBorder.Entities.Monument.CanExplore(em, faction, territory);

                case ReligionPurchaseKind.UnlockB:
                case ReligionPurchaseKind.UnlockWildcard:
                {
                    if (!TryGetSect(em, faction, sectId, out _, out _, out var sect)) return false;
                    if (sect.IsSlotUnlocked(SlotOf(kind))) return false;
                    rpCost = kind == ReligionPurchaseKind.UnlockB ? cfg.unlockSecondRp : cfg.unlockWildcardRp;
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

            if (kind == ReligionPurchaseKind.ExploreRuins)
                return int.TryParse(sectId, out int territory)
                    && TheWaningBorder.Entities.Monument.TryExplore(em, faction, territory);

            if (kind >= ReligionPurchaseKind.SupportSupplies && kind <= ReligionPurchaseKind.SupportVeilsteel)
            {
                if (!FactionReligionPointsHelper.TrySpend(em, faction, rpCost)) return false;
                FactionEconomy.Add(em, faction, SupportPays(kind));
                return true;
            }

            if (kind >= ReligionPurchaseKind.ArmySmall && kind <= ReligionPurchaseKind.ArmyLarge)
            {
                if (!FactionReligionPointsHelper.TrySpend(em, faction, rpCost)) return false;
                if (PetriarchyArmy.Summon(em, faction, ArmySize(kind)) > 0) return true;
                FactionReligionPointsHelper.Refund(em, faction, rpCost);
                return false;
            }

            if (!TryGetSect(em, faction, sectId, out var bank, out int idx, out var sect)) return false;
            if (!FactionReligionPointsHelper.TrySpend(em, faction, rpCost)) return false;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            if (kind == ReligionPurchaseKind.UnlockB || kind == ReligionPurchaseKind.UnlockWildcard)
                sect.UnlockMask = (byte)(sect.UnlockMask | 1 | (1 << (SlotOf(kind) - 1)));
            else
                sect.PowerLevel = (byte)((sect.PowerLevel < 1 ? 1 : sect.PowerLevel) + 1);
            state.Set(idx, sect);
            em.SetComponentData(bank, state);
            return true;
        }

        /// <summary>What one RP of Petriarchy support pays.</summary>
        public static TheWaningBorder.Core.Cost SupportPays(ReligionPurchaseKind kind)
        {
            var cfg = FactionReligionPointsHelper.Cfg;
            return kind switch
            {
                ReligionPurchaseKind.SupportSupplies => TheWaningBorder.Core.Cost.Of(supplies: cfg.supportSupplies),
                ReligionPurchaseKind.SupportIron => TheWaningBorder.Core.Cost.Of(iron: cfg.supportIron),
                ReligionPurchaseKind.SupportVeilstone => TheWaningBorder.Core.Cost.Of(veilstone: cfg.supportVeilstone),
                ReligionPurchaseKind.SupportVeilsteel => TheWaningBorder.Core.Cost.Of(veilsteel: cfg.supportVeilsteel),
                _ => default,
            };
        }

        public static int ArmyPrice(ReligionPurchaseKind kind)
        {
            var cfg = FactionReligionPointsHelper.Cfg;
            return kind == ReligionPurchaseKind.ArmyLarge ? cfg.armyLargeRp
                : kind == ReligionPurchaseKind.ArmyMedium ? cfg.armyMediumRp : cfg.armySmallRp;
        }

        public static int ArmySize(ReligionPurchaseKind kind)
        {
            var cfg = FactionReligionPointsHelper.Cfg;
            return kind == ReligionPurchaseKind.ArmyLarge ? cfg.armyLargeUnits
                : kind == ReligionPurchaseKind.ArmyMedium ? cfg.armyMediumUnits : cfg.armySmallUnits;
        }

        /// <summary>The active slot (2 = B, 3 = wildcard) an unlock kind buys.</summary>
        public static int SlotOf(ReligionPurchaseKind kind)
            => kind == ReligionPurchaseKind.UnlockWildcard ? 3 : kind == ReligionPurchaseKind.UnlockB ? 2 : 0;

        /// <summary>The unlock kind that buys active <paramref name="slot"/> (2 or 3).</summary>
        public static ReligionPurchaseKind UnlockKindFor(int slot)
            => slot == 3 ? ReligionPurchaseKind.UnlockWildcard : ReligionPurchaseKind.UnlockB;

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
    }
}
