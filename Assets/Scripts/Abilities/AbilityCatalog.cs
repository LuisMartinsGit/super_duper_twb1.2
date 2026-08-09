// AbilityCatalog.cs
// The data-driven ability library. Seeded in code (like TechTree.json seeds
// techs) so it works with no editor asset generation. These 8 cards mirror the
// ability cards authored in tools/calculator/techtree.json.
//
// Units reference abilities by their STABLE catalog index (see UnitAbilities);
// AbilitySystems look the card up here. Adding an ability = add a card. If it
// reuses existing AbilityEffectKinds it needs no new system code.

using System.Collections.Generic;

namespace TheWaningBorder.Abilities
{
    public static class AbilityCatalog
    {
        // NOTE: order is the stable index contract. Append only; do not reorder.
        private static readonly AbilityCard[] _cards =
        {
            // 0 — King's Call (King Lexor leadership aura)
            new AbilityCard {
                Name = "King's Call", Activation = AbilityActivation.Passive,
                Targeting = AbilityTargeting.Aura, Affects = AbilityAffects.AlliedCulture,
                CastTime = 0f, Duration = -1f, Radius = 15f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.AttackPct, 15f),
                    new AbilityEffect(AbilityEffectKind.ArmorPct, 15f),
                    new AbilityEffect(AbilityEffectKind.ChargeBonusFlat, 20f),
                },
                Aftermath = null,
            },
            // 1 — Liquid Courage (King Lexor active)
            new AbilityCard {
                Name = "Liquid Courage", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 0f, Duration = 10f, Radius = 0f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.DamageTakenPct, -90f), // 90% damage reduction
                    new AbilityEffect(AbilityEffectKind.AttackPct, 30f),
                },
                Aftermath = new[] { "Veilshift Withdrawal", "Life Cling" },
            },
            // 2 — Veilshift Withdrawal (aftermath drawback)
            new AbilityCard {
                Name = "Veilshift Withdrawal", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 0f, Duration = 5f, Radius = 0f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.MoveSpeedPct, -50f),
                    new AbilityEffect(AbilityEffectKind.SelfDoTPctOverDuration, 50f), // 50% max HP over 5s
                },
                Aftermath = null,
            },
            // 3 — Life Cling (aftermath safety net)
            new AbilityCard {
                Name = "Life Cling", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 0f, Duration = 5f, Radius = 0f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.HpFloor, 1f),
                },
                Aftermath = null,
            },
            // 4 — Automate Facility (Ledger active)
            new AbilityCard {
                Name = "Automate Facility", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SingleTarget, Affects = AbilityAffects.EconomicBuildings,
                CastTime = 6f, Duration = 30f, Radius = 0f, Range = 5f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.ResourceYieldPct, 30f),
                },
                Aftermath = new[] { "Under Automation" },
            },
            // 5 — Under Automation (lockout on the automated building)
            new AbilityCard {
                Name = "Under Automation", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 0f, Duration = 60f, Radius = 0f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.NoAutomation, 1f),
                },
                Aftermath = null,
            },
            // 6 — Use Celestar (Scout active reveal). Reuses the sect RevealCircle
            // power's reveal mechanism (SectActivePowerHelper.SpawnReveal). No max
            // range on the aim (Range 0 = unlimited), but it has a cooldown.
            new AbilityCard {
                Name = "Use Celestar", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.Self,
                CastTime = 5f, Duration = 15f, Cooldown = 30f, Radius = 10f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.RevealFog, 10f),
                },
                Aftermath = null,
            },
            // 7 — Scout Sight (Scout passive)
            new AbilityCard {
                Name = "Scout Sight", Activation = AbilityActivation.Passive,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 0f, Duration = -1f, Radius = 0f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.LosRampWhileStill, 1f),
                },
                Aftermath = null,
            },
            // 8 — War Horn (Royal Stable tech). Allied cavalry in radius get a
            // one-shot +50% on their NEXT charge; the window lasts 20 s or until
            // the charge lands, whichever comes first.
            new AbilityCard {
                Name = "War Horn", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.AlliedCavalry,
                CastTime = 0f, Duration = 20f, Cooldown = 60f, Radius = 20f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.ChargeDamagePct, 50f),
                },
                Aftermath = null,
            },
            // 9 — Full Gallop (Royal Stable tech). Allied cavalry sprint: +40%
            // move speed for 8 s, but they cannot attack during the burst.
            new AbilityCard {
                Name = "Full Gallop", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.AlliedCavalry,
                CastTime = 0f, Duration = 8f, Cooldown = 75f, Radius = 20f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.MoveSpeedPct, 40f),
                    new AbilityEffect(AbilityEffectKind.DisarmWhileBuffed, 1f),
                },
                Aftermath = null,
            },
        };

        private static Dictionary<string, int> _indexByName;

        public static int Count => _cards.Length;

        public static AbilityCard Get(int index)
            => (index >= 0 && index < _cards.Length) ? _cards[index] : null;

        public static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (_indexByName == null)
            {
                _indexByName = new Dictionary<string, int>(_cards.Length);
                for (int i = 0; i < _cards.Length; i++) _indexByName[_cards[i].Name] = i;
            }
            return _indexByName.TryGetValue(name, out var idx) ? idx : -1;
        }

        public static AbilityCard Get(string name)
        {
            int i = IndexOf(name);
            return i >= 0 ? _cards[i] : null;
        }
    }
}
