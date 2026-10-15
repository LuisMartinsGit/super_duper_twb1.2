// FeraldisActions.cs
// The action-panel buttons of the Feraldis buildings that run orders rather
// than training (docs/Design/Age_1_Feraldis.md §4, §6.2):
//
//   Great Forge  one button per bay (click: next recipe), and Add Bay —
//                or, with every bay bought, Upgrade Bay (the lowest one)
//   War Totem    its tier / blood readout and the four rites
//
// Every click is a CommandRouter.IssueFeraldisOrder — the order travels as a
// lockstep command and the executor re-validates it on every peer.

using System.Collections.Generic;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Feraldis;
using TheWaningBorder.UI.Common;
using Unity.Entities;

namespace TheWaningBorder.UI.Data
{
    public static class FeraldisActions
    {
        private const string BayPrefix = "ForgeBay_";
        private const string AddBayId = "ForgeAddBay";
        private const string UpgradeBayPrefix = "ForgeUpgrade_";
        private const string RitePrefix = "TotemRite_";

        /// <summary>The Forge's buttons, or null when <paramref name="e"/> is not an owned, finished Great Forge.</summary>
        public static List<ActionButton> ForgeButtons(EntityManager em, Entity e)
        {
            if (!em.HasComponent<GreatForgeTag>(e) || !em.HasBuffer<ForgeBay>(e)) return null;
            if (em.HasComponent<UnderConstruction>(e) || !Owned(em, e)) return null;
            var me = GameSettings.LocalPlayerFaction;
            var list = new List<ActionButton>();
            var bays = em.GetBuffer<ForgeBay>(e, true);
            int lowest = -1;
            for (int i = 0; i < bays.Length; i++)
            {
                var bay = bays[i];
                GreatForgeSystem.PerMinute(em, e, i, out var spend, out var earn);
                var next = (ForgeRecipe)(((int)bay.Recipe + 1) % 4);
                list.Add(new ActionButton
                {
                    Id = BayPrefix + i,
                    Label = string.Format(Loc.T("Bay {0}: {1}"), i + 1, Loc.T(GreatForge.RecipeName(bay.Recipe)))
                            + " " + RomanLevel(bay.Level),
                    Tooltip = string.Format(Loc.T("Bay {0}, level {1}"), i + 1, bay.Level) + "\n"
                              + (bay.Recipe == ForgeRecipe.Hold ? Loc.T("Idle — spends and makes nothing.")
                                  : PerMinute(spend, "-") + "  ->  " + PerMinute(earn, "+") + " " + Loc.T("per minute"))
                              + "\n" + string.Format(Loc.T("Click: switch to {0}."), Loc.T(GreatForge.RecipeName(next)))
                              + (bay.Recipe == ForgeRecipe.Soulforge ? "\n" + Loc.T("Feeds on the slaves crewing the Forge.") : ""),
                    Enabled = true,
                    CanAfford = true,
                });
                if (bay.Level < 3 && (lowest < 0 || bay.Level < bays[lowest].Level)) lowest = i;
            }

            if (GreatForge.CanAddBay(em, e))
            {
                var cost = GreatForge.NextBayCost(em, e);
                list.Add(new ActionButton
                {
                    Id = AddBayId, Label = Loc.T("Add Bay"), Cost = cost,
                    Tooltip = Loc.T("Build another bay — each one costs more than the last."),
                    Enabled = true, CanAfford = FactionEconomy.CanAfford(em, me, cost),
                });
            }
            else if (lowest >= 0 && GreatForge.TryUpgradeCost(em, e, lowest, out var up))
            {
                list.Add(new ActionButton
                {
                    Id = UpgradeBayPrefix + lowest,
                    Label = string.Format(Loc.T("Upgrade Bay {0}"), lowest + 1), Cost = up,
                    Tooltip = Loc.T("Raise the bay's level: it works faster."),
                    Enabled = true, CanAfford = FactionEconomy.CanAfford(em, me, up),
                });
            }
            return list;
        }

        /// <summary>The totem's rite buttons, or null when <paramref name="e"/> is not an owned, finished War Totem.</summary>
        public static List<ActionButton> TotemButtons(EntityManager em, Entity e)
        {
            if (!em.HasComponent<WarTotemTag>(e) || em.HasComponent<UnderConstruction>(e) || !Owned(em, e)) return null;
            if (!em.HasComponent<TotemLifetime>(e) || !em.HasComponent<TotemFervor>(e)) return new List<ActionButton>();
            var life = em.GetComponentData<TotemLifetime>(e);
            float stored = em.GetComponentData<TotemFervor>(e).Value;
            string state = string.Format(Loc.T("{0} — tier {1}. Stored blood {2:0}, drunk in all {3:0}."),
                Loc.T(WarTotemRites.TierName(life.Tier)), life.Tier, stored, life.Lifetime);
            var list = new List<ActionButton>();
            for (int r = 0; r < 4; r++)
            {
                var rite = (TotemRite)r;
                bool tierOk = life.Tier >= WarTotemRites.MinTier(rite);
                bool running = life.RiteRemaining > 0f && life.ActiveRite == rite;
                string tip = Loc.T(WarTotemRites.RiteName(rite)) + "\n" + Loc.T(RiteText(rite)) + "\n"
                             + string.Format(Loc.T("Costs {0:0} stored blood."), WarTotemRites.Cost(rite)) + "\n" + state;
                if (!tierOk) tip += "\n" + string.Format(Loc.T("Needs a tier {0} totem."), WarTotemRites.MinTier(rite));
                else if (running) tip += "\n" + Loc.T("Running.");
                else if (life.RiteCooldown > 0f) tip += "\n" + string.Format(Loc.T("Ready in {0:0} s."), life.RiteCooldown);
                list.Add(new ActionButton
                {
                    Id = RitePrefix + r,
                    Label = running ? "> " + Loc.T(WarTotemRites.RiteName(rite)) : Loc.T(WarTotemRites.RiteName(rite)),
                    Tooltip = tip,
                    Enabled = WarTotemRites.CanBegin(em, e, rite),
                    CanAfford = stored >= WarTotemRites.Cost(rite),
                });
            }
            return list;
        }

        private const string SanctuaryTradeId = "SanctuaryTrade";

        /// <summary>The Runai Sanctuary's RP-for-veilsteel trade (Age_1_Runai.md §5.1),
        /// or null when <paramref name="e"/> is not an owned, finished Sanctuary.
        /// It rides the same order channel as the Feraldis buttons.</summary>
        public static List<ActionButton> SanctuaryButtons(EntityManager em, Entity e)
        {
            if (!em.HasComponent<RunaiSanctuaryTag>(e) || em.HasComponent<UnderConstruction>(e) || !Owned(em, e)) return null;
            var cfg = TheWaningBorder.Runai.RunaiSanctuarySystem.Cfg;
            int have = FactionReligionPointsHelper.GetBalance(em, GameSettings.LocalPlayerFaction);
            return new List<ActionButton>
            {
                new ActionButton
                {
                    Id = SanctuaryTradeId,
                    Label = Loc.T("Trade for Veilsteel"),
                    Tooltip = string.Format(Loc.T("Spend {0} religion points for {1} veilsteel. You have {2}."),
                                            cfg.rpPerTrade, cfg.veilsteelPerTrade, have),
                    Enabled = true,
                    CanAfford = TheWaningBorder.Runai.RunaiSanctuarySystem.CanTrade(em, e),
                },
            };
        }

        /// <summary>Run a Feraldis button. True when the id was one of ours.</summary>
        public static bool TryExecute(EntityManager em, Entity e, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id == SanctuaryTradeId)
            {
                CommandRouter.IssueFeraldisOrder(em, e, Entity.Null, FeraldisOp.SanctuaryTrade);
                return true;
            }
            if (id.StartsWith(BayPrefix, System.StringComparison.Ordinal)
                && int.TryParse(id.Substring(BayPrefix.Length), out int bay))
            {
                if (!em.HasBuffer<ForgeBay>(e)) return true;
                var bays = em.GetBuffer<ForgeBay>(e, true);
                if (bay < 0 || bay >= bays.Length) return true;
                int next = ((int)bays[bay].Recipe + 1) % 4;
                CommandRouter.IssueFeraldisOrder(em, e, Entity.Null, FeraldisOp.ForgeSetRecipe, bay * 8 + next);
                return true;
            }
            if (id == AddBayId)
            {
                CommandRouter.IssueFeraldisOrder(em, e, Entity.Null, FeraldisOp.ForgeAddBay);
                return true;
            }
            if (id.StartsWith(UpgradeBayPrefix, System.StringComparison.Ordinal)
                && int.TryParse(id.Substring(UpgradeBayPrefix.Length), out int ub))
            {
                CommandRouter.IssueFeraldisOrder(em, e, Entity.Null, FeraldisOp.ForgeUpgradeBay, ub);
                return true;
            }
            if (id.StartsWith(RitePrefix, System.StringComparison.Ordinal)
                && int.TryParse(id.Substring(RitePrefix.Length), out int rite))
            {
                CommandRouter.IssueFeraldisOrder(em, e, Entity.Null, FeraldisOp.TotemRite, rite);
                return true;
            }
            return false;
        }

        private static string RiteText(TotemRite r) => r switch
        {
            TotemRite.Bloodlust => "Allies in range attack faster.",
            TotemRite.Hunger => "Allies in range heal over time.",
            TotemRite.RedMist => "Allies in range cannot drop below 1 HP.",
            _ => "The whole radius counts as blood.",
        };

        private static bool Owned(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e) && em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction;

        private static string RomanLevel(byte l) => l switch { 2 => "II", 3 => "III", _ => "I" };

        private static string PerMinute(Cost c, string sign)
        {
            var parts = new List<string>();
            if (c.Supplies > 0) parts.Add(sign + c.Supplies + " " + Loc.T("supplies"));
            if (c.Iron > 0) parts.Add(sign + c.Iron + " " + Loc.T("iron"));
            if (c.Veilstone > 0) parts.Add(sign + c.Veilstone + " " + Loc.T("veilstone"));
            if (c.Veilsteel > 0) parts.Add(sign + c.Veilsteel + " " + Loc.T("veilsteel"));
            return parts.Count == 0 ? Loc.T("nothing") : string.Join(", ", parts);
        }
    }
}
