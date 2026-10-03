// SectInfo.cs
// Display-time descriptions for the 12 sects. Used by the Religion HUD and the
// Sect Choice popup to show:
//   • Lore  — a single sentence of flavour for the picker header.
//   • Passive  — what the sect's passive lever does (level-agnostic summary).
//   • ActivePower  — what the active ability does, with Lv-I numbers.
//   • Building  — what the sect's building does (there is no chapel aura).
//   • Unit  — what the unit lever bonus is, with Lv-I numbers.
//   • Technology  — what a chapel-upgrade chain (P/B/U/A Lv I→II→III) means
//                   thematically for the sect.
//
// Numbers are pulled from SectLeverEffects so renumbering the design only
// touches one file. We surface Lv-I values in the popup; the Religion HUD
// lever tooltips use SectLeverEffects.LevelScalar to project the current
// level's number.
//
// LOCALIZATION: this is the display boundary for the whole sect corpus.
// Every string returned here — including the canon Name/Description specs
// authored in SectLeverEffects.Alanthor.cs, which stay English at the data
// layer — passes through Loc.T exactly once, on the way out. The Portuguese
// table lives in Loc.Pt.Sects.cs.

using System.Text;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.Economy
{
    public static class SectInfo
    {
        // ─────────────────────────────────────────────────────────────────
        // LORE — one-sentence flavour for the popup header.
        // ─────────────────────────────────────────────────────────────────
        public static string Lore(string sectId) => Loc.T(sectId switch
        {
            SectConfig.Antiquity   => "Keepers of every name that fell to the Border; the dead tally the living's enemies.",
            SectConfig.Renewal     => "Healers in stone — they teach walls to forget the blows they took.",
            SectConfig.Fortitude   => "Their hymns weigh more than mortar. Where they pray, the keep does not break.",
            SectConfig.Reclamation => "They wrest tools, mines, and dying soldiers back from the Veilstone's grasp.",
            SectConfig.Silence     => "A doctrine of stillness — to stand is to be unkillable.",
            SectConfig.Justice     => "Every wound your soldiers take is debt; their gods collect at sword-point.",
            SectConfig.Veneration  => "Each kill is a hymn. Each hymn makes the next blow strike truer.",
            SectConfig.Witness     => "They see what others miss — the road ahead, the trap, the spy.",
            SectConfig.War         => "The forge-faith. Their barracks burn brighter and empty faster.",
            SectConfig.Ash         => "They embrace dying. Where their dead fall, the earth keeps burning.",
            SectConfig.Ruin        => "They love what is broken. Walls fall easier when they sing.",
            SectConfig.Wrath       => "The wounded fight harder under their banner — bleeding makes them dangerous.",
            _                      => "An unknown sect.",
        });

        // ─────────────────────────────────────────────────────────────────
        // PASSIVE — sect's faction-wide passive lever (level-agnostic).
        // ─────────────────────────────────────────────────────────────────
        public static string PassiveDescription(string sectId) => Loc.T(sectId switch
        {
            SectConfig.Antiquity   => "Tally of the Lost — your units gain +dmg for each unit-type they have killed in this match.",
            SectConfig.Renewal     => "Hands That Mend — your buildings auto-repair when out of combat.",
            SectConfig.Fortitude   => "Veiled Stone — your walls and towers gain bonus HP.",
            SectConfig.Reclamation => "Border-Hardened — your units take less damage from Border sources.",
            SectConfig.Silence     => "Steadfast Vigil — your units gain armor while holding position.",
            SectConfig.Justice     => "Marked for Sentence — any unit that kills one of yours takes bonus damage from your army.",
            SectConfig.Veneration  => "Fervor — your unit kills grant a stacking damage and attack-rate buff.",
            SectConfig.Witness     => "All-Seeing — your Scout units gain extended vision.",
            SectConfig.War         => "Forged in Battle — your military units cost 10% less and train 20% faster.",
            SectConfig.Ash         => "Pyre's Promise — your units leave a burning patch on death.",
            SectConfig.Ruin        => "Profane Hands — your units deal bonus damage vs buildings and refund cost when one falls to them.",
            SectConfig.Wrath       => "Spite of the Forsaken — your wounded units deal more damage the lower their HP.",
            _                      => "—",
        });

        // ─────────────────────────────────────────────────────────────────
        // ACTIVE POWER — sect's targetable cooldown ability.
        // Numbers shown at Lv I; in-game effect scales via SectLeverEffects.LevelScalar.
        // ─────────────────────────────────────────────────────────────────
        public static string ActivePowerDescription(string sectId)
        {
            // A canon sect describes its own first active. Without this the
            // tier-less overload kept answering from the legacy switch below,
            // which still advertises retired powers - War's entry there is the
            // old move-speed "War March", a power that no longer exists.
            var canonFirst = SectLeverEffects.ActiveOf(sectId, 1, 1);
            if (canonFirst.Description != null)
            {
                return Loc.T(canonFirst.Description) + "  " +
                       string.Format(Loc.T("Reach: {0}. Cooldown: {1}s."),
                           Loc.T(SectRadii.Label(canonFirst.Reach)),
                           BaseCooldown(canonFirst.Cooldown));
            }

            var spec = SectLeverEffects.ActiveOf(sectId);
            if (spec.Kind == SectActivePowerKind.None) return "—";

            string radius    = spec.Radius.ToString("0");
            string magnitude = spec.Magnitude.ToString("0");
            string magX      = spec.Magnitude.ToString("0.0");
            string duration  = spec.Duration.ToString("0");

            string body = sectId switch
            {
                SectConfig.Antiquity   => string.Format(Loc.T("Recall the Codex — enemy attack & ability cooldowns in a {0}m circle stop recovering for {1}s (Lv III also inflates their current cooldowns +50%)."), radius, magnitude),
                SectConfig.Renewal     => string.Format(Loc.T("Heal Circle — restores {0} HP to all allied units within {1}m."), magnitude, radius),
                SectConfig.Fortitude   => string.Format(Loc.T("Bulwark — allied units in {0}m gain +{1} armor for {2}s."), radius, magnitude, duration),
                SectConfig.Reclamation => string.Format(Loc.T("Reclaim Vigour — heals allied units in a {0}m radius for {1} HP."), radius, magnitude),
                SectConfig.Silence     => string.Format(Loc.T("Whisper-Wind — allied units in {0}m gain x{1} move-speed for {2}s."), radius, magX, duration),
                SectConfig.Justice     => string.Format(Loc.T("Eye of the Law — reveals fog of war in a {0}m circle for {1}s."), radius, duration),
                SectConfig.Veneration  => string.Format(Loc.T("Litany — allied units in {0}m gain x{1} damage for {2}s."), radius, magX, duration),
                SectConfig.Witness     => string.Format(Loc.T("All-Seeing Gaze — reveals fog of war in a {0}m radius for {1}s."), radius, duration),
                SectConfig.War         => string.Format(Loc.T("War March — allied units in {0}m gain x{1} move speed for {2}s."), radius, magX, duration),
                SectConfig.Ash         => string.Format(Loc.T("Burning Ground — covers a {0}m circle in flame, dealing {1} dmg/s for {2}s."), radius, magnitude, duration),
                SectConfig.Ruin        => string.Format(Loc.T("Unmake — the nearest enemy building within {0}m loses {1}% of its current HP. One building only."), radius, (spec.Magnitude * 100f).ToString("0")),
                SectConfig.Wrath       => string.Format(Loc.T("Spite — enemies within {0}m pool the damage they have dealt this match, and the pool is split back over them."), radius),
                _                      => "—",
            };
            return body + "  " + string.Format(Loc.T("Cooldown: {0}s."), BaseCooldown(spec.Cooldown));
        }

        /// <summary>
        /// The cooldown a player without Shardroot or Shrine upgrades actually
        /// waits: the authored number, since there is no hidden global scale.
        /// These texts have no faction to ask; the Religion panel shows the
        /// fully effective number (SectActivePowerHelper.EffectiveCooldown).
        /// docs/Design/Spells.md section 5.
        /// </summary>
        public static string BaseCooldown(float specCooldown)
            => specCooldown.ToString("0");

        /// <summary>Player-facing name of a damage type ("magic", "true", ...).</summary>
        public static string DamageTypeName(DamageType t) => t switch
        {
            DamageType.Melee  => Loc.T("melee"),
            DamageType.Ranged => Loc.T("ranged"),
            DamageType.Siege  => Loc.T("siege"),
            DamageType.True   => Loc.T("true"),
            _                 => Loc.T("magic"),
        };

        /// <summary>I / II / III for a power level.</summary>
        public static string Roman(int level) => level >= 3 ? "III" : level == 2 ? "II" : "I";

        // ─────────────────────────────────────────────────────────────────
        // TIERED ACTIVES (design 2026-07-05) — name + description per tier.
        // Tier 1 = utility (adoption); tier 2 unlocks at temple Lv 2; tier 3
        // (the ultimate) at temple Lv 3. Only playable sects have tiers 2-3.
        // ─────────────────────────────────────────────────────────────────
        public static string ActiveName(string sectId, int tier)
        {
            // A canon sect names its own three actives (docs/Design/Sects.md).
            // The legacy table below only answers for sects not yet cut over —
            // and it is where the literal "Locked" came from, which is what a
            // pre-canon sect showed on tiers 2 and 3.
            var canon = SectLeverEffects.ActiveOf(sectId, tier, 1);
            if (canon.Name != null) return Loc.T(canon.Name);
            return Loc.T(LegacyActiveName(sectId, tier));
        }

        private static string LegacyActiveName(string sectId, int tier) => (sectId, tier) switch
        {
            (SectConfig.Justice, 1) => "Eye of the Law",
            (SectConfig.Justice, 2) => "Sentence",
            (SectConfig.Justice, 3) => "Final Sentence",
            (SectConfig.Renewal, 1) => "Heal Circle",
            (SectConfig.Renewal, 2) => "Mason's Blessing",
            (SectConfig.Renewal, 3) => "Reckoning of the Rebuilt",
            (SectConfig.War, 1)     => "War March",
            (SectConfig.War, 2)     => "Bloodfury",
            (SectConfig.War, 3)     => "Annihilation",

            // The other nine sects (2026-08-12). Tier names follow each sect's
            // canonical Active in task-063; tiers 2-3 previously fell through to
            // "Locked" because they had no spec — see SectLeverEffects.ActiveOf.
            (SectConfig.Antiquity, 1)   => "Recall the Codex",
            (SectConfig.Antiquity, 2)   => "Deepen the Codex",
            (SectConfig.Antiquity, 3)   => "Seal the Codex",
            (SectConfig.Fortitude, 1)   => "Bulwark",
            (SectConfig.Fortitude, 2)   => "Stoneveil",
            (SectConfig.Fortitude, 3)   => "Unbroken Oath",
            (SectConfig.Reclamation, 1) => "Reclaim Vigour",
            (SectConfig.Reclamation, 2) => "Harvest the Veil",
            (SectConfig.Reclamation, 3) => "Greater Harvest",
            (SectConfig.Silence, 1)     => "Whisper-Wind",
            (SectConfig.Silence, 2)     => "Hush",
            (SectConfig.Silence, 3)     => "Entomb",
            (SectConfig.Veneration, 1)  => "Litany",
            (SectConfig.Veneration, 2)  => "Crystal Communion",
            (SectConfig.Veneration, 3)  => "Greater Communion",
            // Canon since 2026-09-08 — the three actives are named by the
            // spec itself; these rows only matter to the legacy tier readout.
            (SectConfig.Witness, 1)     => "Spy Network",
            (SectConfig.Witness, 2)     => "Blinding Glare",
            (SectConfig.Witness, 3)     => "Nowhere to Hide",
            (SectConfig.Ash, 1)         => "Burning Ground",
            (SectConfig.Ash, 2)         => "Pyre",
            (SectConfig.Ash, 3)         => "Ashfall",
            (SectConfig.Ruin, 1)        => "Profane Strike",
            (SectConfig.Ruin, 2)        => "Unmake",
            (SectConfig.Ruin, 3)        => "Undoing",
            (SectConfig.Wrath, 1)       => "Spawn Pyre",
            (SectConfig.Wrath, 2)       => "Wrathfire",
            (SectConfig.Wrath, 3)       => "Final Hour",

            _                       => tier <= 1 ? "Active Power" : "Locked",
        };

        public static string ActivePowerDescription(string sectId, int tier)
            => ActivePowerDescription(sectId, tier, 1);

        /// <summary>
        /// Hover text for one active at one power level. Level comes from
        /// adoption timing (docs/Design/Sects.md section 3), so the same button
        /// reads differently for an early adopter than for a late one.
        /// </summary>
        public static string ActivePowerDescription(string sectId, int tier, int level)
        {
            var canonSpec = SectLeverEffects.ActiveOf(sectId, tier, level);
            if (canonSpec.Description != null)
            {
                return Loc.T(canonSpec.Description) + "  " +
                       string.Format(Loc.T("Reach: {0}. Cooldown: {1}s."),
                           Loc.T(SectRadii.Label(canonSpec.Reach)),
                           BaseCooldown(canonSpec.Cooldown));
            }

            if (tier <= 1) return ActivePowerDescription(sectId);
            var spec = SectLeverEffects.ActiveOf(sectId, tier);
            if (spec.Kind == SectActivePowerKind.None) return "—";

            string radius    = spec.Radius.ToString("0");
            string magnitude = spec.Magnitude.ToString("0");
            string magX      = spec.Magnitude.ToString("0.0");
            string duration  = spec.Duration.ToString("0");

            string body = (sectId, tier) switch
            {
                (SectConfig.Justice, 2) => string.Format(Loc.T("Sentence — burst {0} divine damage in a {1}m circle."), magnitude, radius),
                (SectConfig.Justice, 3) => string.Format(Loc.T("Final Sentence — massive {0} divine damage in a {1}m circle. The ultimate verdict."), magnitude, radius),
                (SectConfig.Renewal, 2) => string.Format(Loc.T("Mason's Blessing — allied units in {0}m gain +{1} armor for {2}s."), radius, magnitude, duration),
                (SectConfig.Renewal, 3) => string.Format(Loc.T("Reckoning of the Rebuilt — {0} crushing damage to enemies in a {1}m circle."), magnitude, radius),
                (SectConfig.War, 2)     => string.Format(Loc.T("Bloodfury — allied units in {0}m gain x{1} damage for {2}s."), radius, magX, duration),
                (SectConfig.War, 3)     => string.Format(Loc.T("Annihilation — {0} devastating damage to everything hostile in a {1}m circle."), magnitude, radius),
                _                       => "—",
            };
            return body + "  " + string.Format(Loc.T("Cooldown: {0}s."), BaseCooldown(spec.Cooldown));
        }

        // ─────────────────────────────────────────────────────────────────
        // UNIQUE BUILDING — the sect building (no chapel aura).
        // ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// The sect's building — one per sect, capped at 5 per faction, where
        /// that sect's unit is trained and its research is bought
        /// (docs/Design/Sects.md section 1). There is no chapel aura; every
        /// sect describes its building.
        /// </summary>
        public static string BuildingDescription(string sectId)
        {
            switch (sectId)
            {
                case SectConfig.Antiquity:
                    return Loc.T("Reliquary — a vaulted archive. Every one standing shortens your " +
                           "sect-power cooldowns a little. Limit 5. Trains the Lorekeeper, " +
                           "researches Royal Index.");
                case SectConfig.Renewal:
                    return Loc.T("Mending Hall — an open-sided infirmary. Damaged units that walk " +
                           "inside heal over time. Limit 5. Trains the Scar Guard, researches " +
                           "Field Hospital.");
                case SectConfig.Fortitude:
                    return Loc.T("Stonehold — a squat windowless blockhouse with the highest HP of " +
                           "any non-Hall structure; it blocks pathing like a wall. Limit 5. " +
                           "Trains the Stone Warden, researches Deep Foundations.");
                case SectConfig.Reclamation:
                    return Loc.T("Veilworks — a smelter for cursed matter, and the only building " +
                           "that may be raised ON cursed ground. Limit 5. Trains the Golem " +
                           "Autark, researches Warden's Ledger.");
                case SectConfig.War:
                    return Loc.T("Muster Yard — a stockade of training posts and armourers' " +
                           "racks. Every per-battalion upgrade you apply anywhere in the " +
                           "faction costs 50% less; the discount does not stack. Limit 5. " +
                           "Trains the Warbreaker, researches Endless Muster.");
            }

            // There is no chapel aura (docs/Design/Sects.md section 1), so a
            // sect with no bespoke case above describes its building from the
            // design text rather than composing an aura that no longer exists.
            switch (sectId)
            {
                case SectConfig.Silence:
                    return Loc.T("Hush Vault — a sunken stone cell. Enemy sect powers cast within " +
                           "its footprint cost their caster extra cooldown. Limit 5.");
                case SectConfig.Justice:
                    return Loc.T("Tribunal — a raised court platform. Marked enemies that die " +
                           "anywhere on the map refund a little of its research cost. Limit 5.");
                case SectConfig.Veneration:
                    return Loc.T("Choir Hall — a resonating hall. Friendly units passing through " +
                           "gain a short Fervor bonus. Limit 5.");
                case SectConfig.Witness:
                    return Loc.T("Glass Spire — a thin mirrored tower that sees further than any " +
                           "other building. Cannot be built inside another Spire's sight. Limit 5.");
                case SectConfig.Ash:
                    return Loc.T("Ash Pyre — a permanently burning pyre. Enemies adjacent to it " +
                           "take burn damage. Limit 5.");
                case SectConfig.Ruin:
                    return Loc.T("Ruinworks — a scaffold of breaking-tools. Siege units built " +
                           "while it stands deal extra damage to structures. Limit 5.");
                case SectConfig.Wrath:
                    return Loc.T("Chain Altar — an altar strung with iron links. Enemies killed " +
                           "near it feed a faction-wide damage stack that decays. Limit 5.");
                default:
                    return "—";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // UNIT — passive bonus to a unit class (or all units).
        // ─────────────────────────────────────────────────────────────────
        public static string UnitDescription(string sectId)
        {
            var unit = SectLeverEffects.UnitOf(sectId);

            // The whole subject phrase is one key per class: Portuguese needs
            // the article/possessive to agree in gender with the noun, which a
            // "Your {0} gain" template cannot do.
            string lead = unit.AppliesToClass switch
            {
                -1 => Loc.T("Your all units gain "),
                 0 => Loc.T("Your melee units gain "),
                 1 => Loc.T("Your ranged units gain "),
                 2 => Loc.T("Your siege units gain "),
                 6 => Loc.T("Your workers gain "),
                 7 => Loc.T("Your scouts gain "),
                 _ => Loc.T("Your select units gain "),
            };

            var sb = new StringBuilder();
            sb.Append(lead);
            bool any = false;
            if (unit.DamageMultiplier > 1.001f) { sb.Append(string.Format(Loc.T("+{0}% damage"), ((unit.DamageMultiplier - 1f) * 100f).ToString("0"))); any = true; }
            if (unit.ArmorBonus > 0)            { if (any) sb.Append(", "); sb.Append(string.Format(Loc.T("+{0} armor"), unit.ArmorBonus)); any = true; }
            if (unit.HpMultiplier > 1.001f)     { if (any) sb.Append(", "); sb.Append(string.Format(Loc.T("+{0}% HP"), ((unit.HpMultiplier - 1f) * 100f).ToString("0"))); any = true; }
            if (!any) sb.Append(Loc.T("a minor blessing"));
            sb.Append(".");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        // TECHNOLOGY — describes the lever-upgrade path (P / B / U / A,
        // each Lv I → II → III) thematically. Numbers from LevelScalar:
        // Lv I = 1.0×, Lv II = 1.5×, Lv III = 2.0×.
        // ─────────────────────────────────────────────────────────────────
        public static string TechnologyDescription(string sectId)
        {
            string flavour = Loc.T(sectId switch
            {
                SectConfig.Antiquity   => "More names in the tally — stronger relics, sharper memory.",
                SectConfig.Renewal     => "Deeper communion — walls knit faster, men return from death.",
                SectConfig.Fortitude   => "Heavier hymns — stone grows thicker, melee endure longer.",
                SectConfig.Reclamation => "Wider claim — your workers and tools shrug off Border-rot.",
                SectConfig.Silence     => "Longer vigil — archers strike harder while still.",
                SectConfig.Justice     => "Harsher verdict — sentence falls heavier, faster.",
                SectConfig.Veneration  => "Higher fervor — kills bless your army with deeper rage.",
                SectConfig.Witness     => "Farther sight — scouts see the whole map's edges.",
                SectConfig.War         => "Hotter forges — barracks pay less and pour out faster.",
                SectConfig.Ash         => "Brighter pyres — corpses burn longer and wider.",
                SectConfig.Ruin        => "Sharper hands — siege strips faction walls to dust.",
                SectConfig.Wrath       => "Deeper spite — the bleeding wound deals the killing blow.",
                _                      => "Deeper devotion improves every lever this sect grants.",
            });
            return string.Format(Loc.T("At the chapel you can spend RP + resources to upgrade Passive (P), Building (B), Unit bonus (U), and Active power (A) — each I → II → III, scaling effects to 1.5× and 2.0× of the listed Lv I numbers.  {0}"), flavour);
        }

        // ─────────────────────────────────────────────────────────────────
        // SHORT NAME — strip the "Sect_" prefix for display.
        // ─────────────────────────────────────────────────────────────────
        public static string ShortName(string sectId)
        {
            if (string.IsNullOrEmpty(sectId)) return "?";
            const string p = "Sect_";
            return sectId.StartsWith(p) ? Loc.T(sectId.Substring(p.Length)) : sectId;
        }
    }
}
