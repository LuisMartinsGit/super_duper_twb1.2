// SectInfo.cs
// Display-time descriptions for the 12 sects. Used by the Religion HUD and the
// Sect Choice popup to show:
//   • Lore  — a single sentence of flavour for the picker header.
//   • Passive  — what the sect's passive lever does (level-agnostic summary).
//   • ActivePower  — what the active ability does, with Lv-I numbers.
//   • Building  — what the chapel's aura does, with Lv-I numbers.
//   • Unit  — what the unit lever bonus is, with Lv-I numbers.
//   • Technology  — what a chapel-upgrade chain (P/B/U/A Lv I→II→III) means
//                   thematically for the sect.
//
// Numbers are pulled from SectLeverEffects so renumbering the design only
// touches one file. We surface Lv-I values in the popup; the Religion HUD
// lever tooltips use SectLeverEffects.LevelScalar to project the current
// level's number.
//
// Phase-5 polish may move this into SectDefinition ScriptableObjects so it
// can be localized + edited without recompiling.
//
// Location: Assets/Scripts/Economy/SectInfo.cs

using System.Text;

namespace TheWaningBorder.Economy
{
    public static class SectInfo
    {
        // ─────────────────────────────────────────────────────────────────
        // LORE — one-sentence flavour for the popup header.
        // ─────────────────────────────────────────────────────────────────
        public static string Lore(string sectId) => sectId switch
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
        };

        // ─────────────────────────────────────────────────────────────────
        // PASSIVE — sect's faction-wide passive lever (level-agnostic).
        // ─────────────────────────────────────────────────────────────────
        public static string PassiveDescription(string sectId) => sectId switch
        {
            SectConfig.Antiquity   => "Tally of the Lost — your units gain +dmg for each unit-type they have killed in this match.",
            SectConfig.Renewal     => "Hands That Mend — your buildings auto-repair when out of combat.",
            SectConfig.Fortitude   => "Veiled Stone — your walls and towers gain bonus HP.",
            SectConfig.Reclamation => "Border-Hardened — your units take less damage from Border sources.",
            SectConfig.Silence     => "Steadfast Vigil — your units gain armor while holding position.",
            SectConfig.Justice     => "Marked for Sentence — any unit that kills one of yours takes bonus damage from your army.",
            SectConfig.Veneration  => "Fervor — your unit kills grant a stacking damage and attack-rate buff.",
            SectConfig.Witness     => "All-Seeing — your Scout units gain extended vision.",
            SectConfig.War         => "Forged in Battle — your military units cost less and train faster.",
            SectConfig.Ash         => "Pyre's Promise — your units leave a burning patch on death.",
            SectConfig.Ruin        => "Profane Hands — your units deal bonus damage vs buildings and refund cost when one falls to them.",
            SectConfig.Wrath       => "Spite of the Forsaken — your wounded units deal more damage the lower their HP.",
            _                      => "—",
        };

        // ─────────────────────────────────────────────────────────────────
        // ACTIVE POWER — sect's targetable cooldown ability.
        // Numbers shown at Lv I; in-game effect scales via SectLeverEffects.LevelScalar.
        // ─────────────────────────────────────────────────────────────────
        public static string ActivePowerDescription(string sectId)
        {
            var spec = SectLeverEffects.ActiveOf(sectId);
            if (spec.Kind == SectActivePowerKind.None) return "—";

            string body = sectId switch
            {
                SectConfig.Antiquity   => $"Recall the Codex — enemy attack & ability cooldowns in a {spec.Radius:0}m circle stop recovering for {spec.Magnitude:0}s (Lv III also inflates their current cooldowns +50%).",
                SectConfig.Renewal     => $"Heal Circle — restores {spec.Magnitude:0} HP to all allied units within {spec.Radius:0}m.",
                SectConfig.Fortitude   => $"Bulwark — allied units in {spec.Radius:0}m gain +{spec.Magnitude:0} armor for {spec.Duration:0}s.",
                SectConfig.Reclamation => $"Reclaim Vigour — heals allied units in a {spec.Radius:0}m radius for {spec.Magnitude:0} HP.",
                SectConfig.Silence     => $"Whisper-Wind — allied units in {spec.Radius:0}m gain x{spec.Magnitude:0.0} move-speed for {spec.Duration:0}s.",
                SectConfig.Justice     => $"Eye of the Law — reveals fog of war in a {spec.Radius:0}m circle for {spec.Duration:0}s.",
                SectConfig.Veneration  => $"Litany — allied units in {spec.Radius:0}m gain x{spec.Magnitude:0.0} damage for {spec.Duration:0}s.",
                SectConfig.Witness     => $"All-Seeing Gaze — reveals fog of war in a {spec.Radius:0}m radius for {spec.Duration:0}s.",
                SectConfig.War         => $"War March — allied units in {spec.Radius:0}m gain x{spec.Magnitude:0.0} move speed for {spec.Duration:0}s.",
                SectConfig.Ash         => $"Burning Ground — covers a {spec.Radius:0}m circle in flame, dealing {spec.Magnitude:0} dmg/s for {spec.Duration:0}s.",
                SectConfig.Ruin        => $"Profane Strike — burst {spec.Magnitude:0} damage to everything in a {spec.Radius:0}m circle.",
                SectConfig.Wrath       => $"Spawn Pyre — drops a burning pillar at the target, scorching {spec.Radius:0}m for {spec.Duration:0}s.",
                _                      => "—",
            };
            return $"{body}  Cooldown: {spec.Cooldown:0}s.";
        }

        // ─────────────────────────────────────────────────────────────────
        // TIERED ACTIVES (design 2026-07-05) — name + description per tier.
        // Tier 1 = utility (adoption); tier 2 unlocks at temple Lv 2; tier 3
        // (the ultimate) at temple Lv 3. Only playable sects have tiers 2-3.
        // ─────────────────────────────────────────────────────────────────
        public static string ActiveName(string sectId, int tier) => (sectId, tier) switch
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
            _                       => tier <= 1 ? "Active Power" : "Locked",
        };

        public static string ActivePowerDescription(string sectId, int tier)
        {
            if (tier <= 1) return ActivePowerDescription(sectId);
            var spec = SectLeverEffects.ActiveOf(sectId, tier);
            if (spec.Kind == SectActivePowerKind.None) return "—";

            string body = (sectId, tier) switch
            {
                (SectConfig.Justice, 2) => $"Sentence — burst {spec.Magnitude:0} divine damage in a {spec.Radius:0}m circle.",
                (SectConfig.Justice, 3) => $"Final Sentence — massive {spec.Magnitude:0} divine damage in a {spec.Radius:0}m circle. The ultimate verdict.",
                (SectConfig.Renewal, 2) => $"Mason's Blessing — allied units in {spec.Radius:0}m gain +{spec.Magnitude:0} armor for {spec.Duration:0}s.",
                (SectConfig.Renewal, 3) => $"Reckoning of the Rebuilt — {spec.Magnitude:0} crushing damage to enemies in a {spec.Radius:0}m circle.",
                (SectConfig.War, 2)     => $"Bloodfury — allied units in {spec.Radius:0}m gain x{spec.Magnitude:0.0} damage for {spec.Duration:0}s.",
                (SectConfig.War, 3)     => $"Annihilation — {spec.Magnitude:0} devastating damage to everything hostile in a {spec.Radius:0}m circle.",
                _                       => "—",
            };
            return $"{body}  Cooldown: {spec.Cooldown:0}s.";
        }

        // ─────────────────────────────────────────────────────────────────
        // UNIQUE BUILDING — Chapel of <Sect> and its aura.
        // ─────────────────────────────────────────────────────────────────
        public static string BuildingDescription(string sectId)
        {
            var aura = SectLeverEffects.AuraOf(sectId);
            var sb = new StringBuilder("Chapel of ");
            sb.Append(ShortName(sectId));
            sb.Append(" — projects an aura within ").Append(aura.Radius.ToString("0")).Append("m: ");

            bool any = false;
            if (aura.DamageMultiplier > 1.001f) { sb.Append("+").Append(((aura.DamageMultiplier - 1f) * 100f).ToString("0")).Append("% damage"); any = true; }
            if (aura.ArmorBonus > 0)            { if (any) sb.Append(", "); sb.Append("+").Append(aura.ArmorBonus).Append(" armor"); any = true; }
            if (aura.SpeedMultiplier > 1.001f)  { if (any) sb.Append(", "); sb.Append("+").Append(((aura.SpeedMultiplier - 1f) * 100f).ToString("0")).Append("% speed"); any = true; }
            if (aura.DamageReflect > 0.001f)    { if (any) sb.Append(", "); sb.Append((aura.DamageReflect * 100f).ToString("0")).Append("% reflect"); any = true; }
            if (aura.HpRegenPerSecond > 0)      { if (any) sb.Append(", "); sb.Append(aura.HpRegenPerSecond).Append(" HP/s regen"); any = true; }
            if (!any) sb.Append("a quiet sanctifying presence");

            sb.Append(" to allied units.");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        // UNIT — passive bonus to a unit class (or all units).
        // ─────────────────────────────────────────────────────────────────
        public static string UnitDescription(string sectId)
        {
            var unit = SectLeverEffects.UnitOf(sectId);
            string target = unit.AppliesToClass switch
            {
                -1 => "all units",
                 0 => "melee units",
                 1 => "ranged units",
                 2 => "siege units",
                 6 => "miners / workers",
                 7 => "scouts",
                 _ => "select units",
            };

            var sb = new StringBuilder();
            sb.Append("Your ").Append(target).Append(" gain ");
            bool any = false;
            if (unit.DamageMultiplier > 1.001f) { sb.Append("+").Append(((unit.DamageMultiplier - 1f) * 100f).ToString("0")).Append("% damage"); any = true; }
            if (unit.ArmorBonus > 0)            { if (any) sb.Append(", "); sb.Append("+").Append(unit.ArmorBonus).Append(" armor"); any = true; }
            if (unit.HpMultiplier > 1.001f)     { if (any) sb.Append(", "); sb.Append("+").Append(((unit.HpMultiplier - 1f) * 100f).ToString("0")).Append("% HP"); any = true; }
            if (!any) sb.Append("a minor blessing");
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
            string flavour = sectId switch
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
            };
            return $"At the chapel you can spend RP + resources to upgrade Passive (P), Building aura (B), Unit bonus (U), and Active power (A) — each I → II → III, scaling effects to 1.5× and 2.0× of the listed Lv I numbers.  {flavour}";
        }

        // ─────────────────────────────────────────────────────────────────
        // SHORT NAME — strip the "Sect_" prefix for display.
        // ─────────────────────────────────────────────────────────────────
        public static string ShortName(string sectId)
        {
            if (string.IsNullOrEmpty(sectId)) return "?";
            const string p = "Sect_";
            return sectId.StartsWith(p) ? sectId.Substring(p.Length) : sectId;
        }
    }
}
