// AbilityCatalog.cs
// The data-driven ability library. SO-first: Assets/Resources/AbilityCatalog.asset
// lists one AbilityDefSO per ability (each in an Abilities/ folder under its
// owning unit, building or sect), in index order, and the card table is built
// from those SOs. The code seed below is only the fallback for a missing
// asset (same contract as TechTreeCatalog) and MUST stay equal to the SOs --
// docs/Design/Spells.md.
//
// Units reference abilities by their STABLE catalog index (see UnitAbilities);
// AbilitySystems look the card up here. Adding an ability = add a card (and
// regenerate the SOs). If it reuses existing AbilityEffectKinds it needs no
// new system code.

using System.Collections.Generic;

namespace TheWaningBorder.Abilities
{
    public static class AbilityCatalog
    {
        // NOTE: order is the stable index contract. Append only; do not reorder.
        private static readonly AbilityCard[] _seedCards =
        {
            // NOTE: a card's index is its identity on the wire and in
            // UnitAbilities. Append new cards at the END of this array.
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
            // 1 — Liquid Courage (King Lexor active). A hero active: 45 s,
            // so 10 s of -90% damage taken is 22% uptime, under the 25% cap
            // for strong defense (docs/Design/Spells.md 8.2 / 8.4). It was an
            // auto cooldown (duration + 1 s): 91% uptime.
            new AbilityCard {
                Name = "Liquid Courage", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 0f, Duration = 10f, Cooldown = 45f, Radius = 0f, Range = 0f,
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
                // Unit tactical band (60 s) and the canon Small radius (8 m):
                // docs/Design/Spells.md 8.2 / 8.5.
                CastTime = 5f, Duration = 15f, Cooldown = 60f, Radius = 8f, Range = 0f,
                AimedAtPoint = true,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.RevealFog, 8f),
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
            // the charge lands, whichever comes first. Radii on the ability
            // cards are the canon four (docs/Design/Spells.md 8.5): the old
            // 20 m sat between Medium and Large and snaps to Medium.
            new AbilityCard {
                Name = "War Horn", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.AlliedCavalry,
                CastTime = 0f, Duration = 20f, Cooldown = 60f, Radius = 15f, Range = 0f,
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
                CastTime = 0f, Duration = 8f, Cooldown = 60f, Radius = 15f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.MoveSpeedPct, 40f),
                    new AbilityEffect(AbilityEffectKind.DisarmWhileBuffed, 1f),
                },
                Aftermath = null,
            },
            // 10 — Deploy Field Hospital (Litharch; unlocked by the Sect of
            // Renewal's "Field Hospital" research, bought at the Mending Hall).
            // Raises a temporary building that heals nearby allies and tears
            // itself down after two minutes.
            new AbilityCard {
                Name = "Deploy Field Hospital", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.SelfCast, Affects = AbilityAffects.Self,
                CastTime = 3f, Duration = 0f, Cooldown = 300f, Radius = 15f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.DeployFieldHospital, 1f),
                },
                Aftermath = null,
            },
            // 11 - Honour thy Pledge (King Lexor, unlocks at hero level 4).
            // "The oath runs both ways. Lexor calls it in."
            // Calls in a temporary army whose size, veteran rank and duration
            // all scale with the king's level. The card carries no numbers for
            // any of that: PledgeArmy reads them off the caster, so the ladder
            // lives in one place. docs/Design/Heroes.md section 3.
            new AbilityCard {
                Name = "Honour thy Pledge", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.Self,
                // Hero ultimate band, 120 s (docs/Design/Spells.md 8.2). The
                // 6 m is the spawn ring, not an area of effect.
                CastTime = 0f, Duration = 0f, Cooldown = 120f, Radius = 6f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.SummonPledgeArmy, 1f),
                },
                Aftermath = null,
                UnlocksAtLevel = 4,
            },
            // 12 — Choreographed Volleys (Archery Range tech). A ranged unit
            // calls the cadence and every allied ranged unit around it shoots
            // at double rate for 5 s, once a minute (unit tactical band).
            //
            // It was a faction-wide button on the Archery Range panel. The
            // trigger belongs to the line that fires it, so it is a unit active
            // now — which also gives the ability a PLACE, and makes seeding it
            // where the archers actually are a decision worth making.
            new AbilityCard {
                Name = "Choreographed Volleys", Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.AlliedRanged,
                CastTime = 0f, Duration = 5f, Cooldown = 60f, Radius = 15f, Range = 0f,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.FireRatePct, 100f),
                },
                Aftermath = null,
            },
            // 13 -- Shardbound Fury (King Lexor while he bears the Shardroot;
            // granted and withdrawn by ShardboundKingSystem, never authored on
            // the unit). Every enemy in the radius is hurled 4-10 m into the
            // air and slammed down; enemy buildings in it take heavy damage.
            // Numbers live in ShardboundFury.cs. Curse_And_Shardroot.md 3.1.
            new AbilityCard {
                Name = TheWaningBorder.Entities.ShardboundFury.AbilityName, Activation = AbilityActivation.Active,
                Targeting = AbilityTargeting.Area, Affects = AbilityAffects.Enemies,
                CastTime = 0f, Duration = 0f, Cooldown = TheWaningBorder.Entities.ShardboundFury.FuryCooldown,
                Radius = TheWaningBorder.Entities.ShardboundFury.FuryRadius, Range = 0f,
                // The slam each hurled unit takes on landing. Magic: the
                // design names no other type (docs/Design/Spells.md).
                Damage = TheWaningBorder.Entities.ShardboundFury.FurySlamDamage,
                DamageType = DamageType.Magic,
                Effects = new[] {
                    new AbilityEffect(AbilityEffectKind.ShardboundFury, 1f),
                },
                Aftermath = null,
            },
        };

        private static AbilityCard[] _resolved;
        private static Dictionary<string, int> _indexByName;

        /// <summary>
        /// The live card table: built from AbilityDefSO assets when the
        /// Resources catalog exists with entries, else the code seed.
        /// Resolved once; list order is the index contract either way.
        /// </summary>
        private static AbilityCard[] Cards
        {
            get
            {
                if (_resolved == null) _resolved = LoadFromSOs() ?? _seedCards;
                return _resolved;
            }
        }

        private static AbilityCard[] LoadFromSOs()
        {
            var catalog = UnityEngine.Resources.Load<AbilityCatalogSO>("AbilityCatalog");
            if (catalog == null || !catalog.HasEntries) return null;

            // The list ORDER is the index contract (UnitAbilities slots and the
            // lockstep wire carry indices), so a missing entry must not shift
            // every later card down by one — the old `continue` did exactly
            // that. A null slot falls back to the seed card at that index, and
            // a name that disagrees with the seed is reported loudly.
            var cards = new List<AbilityCard>(catalog.abilities.Count);
            for (int i = 0; i < catalog.abilities.Count; i++)
            {
                var so = catalog.abilities[i];
                if (so == null)
                {
                    UnityEngine.Debug.LogError($"[AbilityCatalog] entry {i} is empty; using the code seed for that index.");
                    if (i < _seedCards.Length) cards.Add(_seedCards[i]);
                    continue;
                }
                var card = so.ToCard();
                if (i < _seedCards.Length && card.Name != _seedCards[i].Name)
                    UnityEngine.Debug.LogError($"[AbilityCatalog] entry {i} is '{card.Name}' but the index contract says '{_seedCards[i].Name}'. Reorder Resources/AbilityCatalog.asset.");
                cards.Add(card);
            }
            return cards.Count > 0 ? cards.ToArray() : null;
        }

        public static int Count => Cards.Length;

        public static AbilityCard Get(int index)
            => (index >= 0 && index < Cards.Length) ? Cards[index] : null;

        public static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            if (_indexByName == null)
            {
                var cards = Cards;
                _indexByName = new Dictionary<string, int>(cards.Length);
                for (int i = 0; i < cards.Length; i++) _indexByName[cards[i].Name] = i;
            }
            return _indexByName.TryGetValue(name, out var idx) ? idx : -1;
        }

        public static AbilityCard Get(string name)
        {
            int i = IndexOf(name);
            return i >= 0 ? Cards[i] : null;
        }

        /// <summary>Every seed card, for the editor-side SO generator.</summary>
        public static AbilityCard[] SeedCards => _seedCards;
    }
}
