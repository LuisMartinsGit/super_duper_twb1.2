// SectLeverEffects.cs
// Per-sect data tables for the Building / Unit / Active-Power levers
// (task-063 phase 5). The Passive lever has its own per-sect ECS systems
// (SectFortitudeHpSystem, SectVenerationFervorSystem, etc.) because each
// passive has its own bespoke trigger and side effects. The other three
// levers are implemented uniformly:
//
//   - Building lever: chapel emits a faction-wide aura to allied units in
//     range of the chapel. Each sect's aura is a SpellBuff parameter set
//     (damage / armor / speed / reflect) plus a flat HP regen.
//
//   - Unit lever: per-faction passive bonus applied to a designated
//     UnitClass when the lever is at Lv 1+. Stat-bump only — no new
//     entity types or ability adds at this phase.
//
//   - Active Power lever: a per-sect triggered ability with a cooldown.
//     The kind enum dispatches to a small switch in SectActivePowerSystem.
//
// All values scale Lv I → II → III by a per-axis multiplier exposed via
// LevelScalar, so callers don't have to maintain three tables per sect.
//
// Location: Assets/Scripts/Economy/SectLeverEffects.cs

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Aura emitted by a sect's chapel (Building lever). Fields map directly
    /// onto SpellBuff so the existing combat-pipeline readers consume them.
    /// </summary>
    public struct SectAuraSpec
    {
        public float Radius;
        public float DamageMultiplier;  // 1.0 = no change
        public int   ArmorBonus;        // flat add to defense
        public float SpeedMultiplier;   // 1.0 = no change (move-speed plumbing partial)
        public float DamageReflect;     // 0..1 fraction
        public int   HpRegenPerSecond;  // applied by SectBuildingLeverSystem
    }

    /// <summary>
    /// Per-sect Unit-lever bonus — read at combat sites by stat consumers.
    /// Class -1 means "all units of the faction".
    /// </summary>
    public struct SectUnitLeverSpec
    {
        public int   AppliesToClass;    // UnitClass cast to int, or -1 for all
        public float DamageMultiplier;  // 1.0 = no change
        public int   ArmorBonus;        // flat
        public float HpMultiplier;      // 1.0 = no change (applied at spawn / first-hit stamp)
    }

    /// <summary>
    /// Discriminator for active-power dispatch.
    /// </summary>
    public enum SectActivePowerKind : byte
    {
        None = 0,
        SmiteCircle,        // burst damage in circle (default for offensive sects)
        HealCircle,         // heal allied units
        ArmorCircle,        // grant armor SpellBuff
        DamageCircle,       // grant damage SpellBuff
        SpeedCircle,        // grant speed SpellBuff
        BurningCircle,      // spawn BurningGround tiles
        RevealCircle,       // FoW reveal area
        SpawnPyre,          // spawn one BurningGround at center
    }

    public struct SectActivePowerSpec
    {
        public SectActivePowerKind Kind;
        public float Radius;
        public float Magnitude;       // damage / heal / armor amount
        public float Duration;        // seconds (where applicable)
        public float Cooldown;        // base cooldown — Phase 4 reduces with level
    }

    /// <summary>
    /// Per-sect lookup tables + level-scaling helper.
    /// </summary>
    public static class SectLeverEffects
    {
        /// <summary>
        /// Magnitude multiplier per lever level. Lv I = 1.00, Lv II = 1.5,
        /// Lv III = 2.0 — applied by the consuming systems on the relevant
        /// axes (damage / armor / regen). Cooldowns scale inversely.
        /// </summary>
        public static float LevelScalar(byte level) => level switch
        {
            2 => 1.5f,
            3 => 2.0f,
            _ => 1.0f,
        };

        public static SectAuraSpec AuraOf(string sectId)
        {
            // Default: small benign aura. Per-sect overrides below.
            switch (sectId)
            {
                case SectConfig.Antiquity:
                    return new SectAuraSpec { Radius = 8f, DamageMultiplier = 1.05f };
                case SectConfig.Renewal:
                    return new SectAuraSpec { Radius = 10f, HpRegenPerSecond = 1 };
                case SectConfig.Fortitude:
                    return new SectAuraSpec { Radius = 8f, ArmorBonus = 2 };
                case SectConfig.Reclamation:
                    return new SectAuraSpec { Radius = 8f, ArmorBonus = 1, HpRegenPerSecond = 1 };
                case SectConfig.Silence:
                    return new SectAuraSpec { Radius = 8f, ArmorBonus = 3 };
                case SectConfig.Justice:
                    return new SectAuraSpec { Radius = 9f, DamageMultiplier = 1.05f, ArmorBonus = 1 };
                case SectConfig.Veneration:
                    return new SectAuraSpec { Radius = 8f, DamageMultiplier = 1.05f };
                case SectConfig.Witness:
                    return new SectAuraSpec { Radius = 12f, DamageMultiplier = 1.03f };
                case SectConfig.War:
                    return new SectAuraSpec { Radius = 8f, DamageMultiplier = 1.08f };
                case SectConfig.Ash:
                    return new SectAuraSpec { Radius = 6f, DamageReflect = 0.10f };
                case SectConfig.Ruin:
                    return new SectAuraSpec { Radius = 8f, DamageMultiplier = 1.06f };
                case SectConfig.Wrath:
                    return new SectAuraSpec { Radius = 7f, DamageMultiplier = 1.05f, DamageReflect = 0.05f };
                default:
                    return default;
            }
        }

        public static SectUnitLeverSpec UnitOf(string sectId)
        {
            // Class indices match the UnitClass enum (Melee 0..Scout 7).
            switch (sectId)
            {
                case SectConfig.Antiquity:   return new SectUnitLeverSpec { AppliesToClass = -1, DamageMultiplier = 1.04f };
                case SectConfig.Renewal:     return new SectUnitLeverSpec { AppliesToClass = -1, HpMultiplier = 1.05f };
                case SectConfig.Fortitude:   return new SectUnitLeverSpec { AppliesToClass = 0,  ArmorBonus = 3 };  // melee +armor
                case SectConfig.Reclamation: return new SectUnitLeverSpec { AppliesToClass = 6,  ArmorBonus = 5 };  // miners +armor
                case SectConfig.Silence:     return new SectUnitLeverSpec { AppliesToClass = 1,  DamageMultiplier = 1.06f }; // ranged
                case SectConfig.Justice:     return new SectUnitLeverSpec { AppliesToClass = -1, DamageMultiplier = 1.04f };
                case SectConfig.Veneration:  return new SectUnitLeverSpec { AppliesToClass = 0,  DamageMultiplier = 1.05f };
                case SectConfig.Witness:     return new SectUnitLeverSpec { AppliesToClass = 7,  HpMultiplier = 1.10f }; // scouts
                case SectConfig.War:         return new SectUnitLeverSpec { AppliesToClass = 0,  DamageMultiplier = 1.06f, ArmorBonus = 1 };
                case SectConfig.Ash:         return new SectUnitLeverSpec { AppliesToClass = -1, DamageMultiplier = 1.04f };
                case SectConfig.Ruin:        return new SectUnitLeverSpec { AppliesToClass = 2,  DamageMultiplier = 1.10f }; // siege
                case SectConfig.Wrath:       return new SectUnitLeverSpec { AppliesToClass = -1, DamageMultiplier = 1.05f };
                default: return default;
            }
        }

        public static SectActivePowerSpec ActiveOf(string sectId)
        {
            switch (sectId)
            {
                case SectConfig.Antiquity:   return new SectActivePowerSpec { Kind = SectActivePowerKind.RevealCircle,  Radius = 12f, Duration = 8f, Cooldown = 90f };
                case SectConfig.Renewal:     return new SectActivePowerSpec { Kind = SectActivePowerKind.HealCircle,    Radius = 8f,  Magnitude = 50f, Cooldown = 90f };
                case SectConfig.Fortitude:   return new SectActivePowerSpec { Kind = SectActivePowerKind.ArmorCircle,   Radius = 8f,  Magnitude = 5f,  Duration = 12f, Cooldown = 120f };
                case SectConfig.Reclamation: return new SectActivePowerSpec { Kind = SectActivePowerKind.HealCircle,    Radius = 6f,  Magnitude = 30f, Cooldown = 75f };
                case SectConfig.Silence:     return new SectActivePowerSpec { Kind = SectActivePowerKind.SpeedCircle,   Radius = 8f,  Magnitude = 1.20f, Duration = 8f, Cooldown = 90f };
                case SectConfig.Justice:     return new SectActivePowerSpec { Kind = SectActivePowerKind.SmiteCircle,   Radius = 6f,  Magnitude = 60f, Cooldown = 120f };
                case SectConfig.Veneration:  return new SectActivePowerSpec { Kind = SectActivePowerKind.DamageCircle,  Radius = 8f,  Magnitude = 1.20f, Duration = 10f, Cooldown = 120f };
                case SectConfig.Witness:     return new SectActivePowerSpec { Kind = SectActivePowerKind.RevealCircle,  Radius = 16f, Duration = 12f, Cooldown = 75f };
                case SectConfig.War:         return new SectActivePowerSpec { Kind = SectActivePowerKind.DamageCircle,  Radius = 8f,  Magnitude = 1.25f, Duration = 8f,  Cooldown = 120f };
                case SectConfig.Ash:         return new SectActivePowerSpec { Kind = SectActivePowerKind.BurningCircle, Radius = 6f,  Magnitude = 8f,  Duration = 6f,  Cooldown = 120f };
                case SectConfig.Ruin:        return new SectActivePowerSpec { Kind = SectActivePowerKind.SmiteCircle,   Radius = 8f,  Magnitude = 80f, Cooldown = 150f };
                case SectConfig.Wrath:       return new SectActivePowerSpec { Kind = SectActivePowerKind.SpawnPyre,     Radius = 4f,  Magnitude = 6f,  Duration = 8f,  Cooldown = 120f };
                default: return default;
            }
        }
    }
}
