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
        FreezeCooldowns,    // Antiquity "Recall the Codex": halt enemy cooldown recovery in circle

        // ── Canon kinds (docs/Design/Sects.md, 2026-08-12) ──────────────────
        // Added for the Alanthor rewrite. The eight non-Alanthor sects still
        // run on the kinds above until their own pass lands.
        BuildingShutdown,   // Antiquity "Heavy Bureaucracy": buildings stop training/research/output
        HostileConversion,  // Antiquity "Sew Disorder": units turn hostile to everything
        HealCirclePercent,  // Renewal "Hands of Plenty": heal a FRACTION of max HP, optional regen tail
        RaiseTower,         // Renewal "Raise Anew": conjure Watch Towers (Magnitude = tower level)
        DeathWard,          // Renewal "Second Wind": cannot drop below 1 HP
        Veil,               // Fortitude "Stoneveil": invisible, untargetable, cannot interact, faster
        BuildingHpBuff,     // Fortitude "Bulwark": +100% building HP, Lv III adds melee reflect
        Invulnerable,       // Fortitude "Immovable" III: immune to all damage
        NodeOverYield,      // Reclamation "Harvest the Veil": a resource node over-yields on a tick
        InfluenceBurst,     // Reclamation "Cleanse": pump player influence into the area
        UnmakeBuilding,     // Ruin "Unmake": ONE enemy building loses Magnitude (0..1) of its CURRENT hp
        SpitePool,          // Wrath "Spite": pool the damage enemies in the area have dealt, split it back over them
        CurseWard,          // Reclamation "Veil-Touched": immunity to curse damage

        // ── Feraldis / War canon kinds (docs/Design/Sects.md, 2026-08-18) ────
        BloodRain,          // War "Blood Rain": blood pool + MAP-WIDE haste + MAP-WIDE spell lockout
        TrainingBoon,       // War "Call to Arms": military buildings train cheaper (and faster at Lv III)
        DamageArmorCircle,  // War "Bloodfury" III: damage buff AND flat armor in one buff
    }

    public struct SectActivePowerSpec
    {
        public SectActivePowerKind Kind;
        public float Radius;
        public float Magnitude;       // damage / heal / armor amount
        public float Duration;        // seconds (where applicable)
        public float Cooldown;        // base cooldown — Phase 4 reduces with level

        /// <summary>Which of the four canon radii this power uses. Set by the
        /// canon tables; the eight pre-canon sects leave it at Single and are
        /// read through <see cref="Radius"/> as before.</summary>
        public SectRadius Reach;

        /// <summary>Display name. Canon powers carry their own name because a
        /// sect now has THREE distinct actives, not one power at three tiers.</summary>
        public string Name;

        /// <summary>Player-facing hover text for this exact slot and level.</summary>
        public string Description;

        /// <summary>True once this spec came from the canon table rather than
        /// the legacy per-sect fallback. Lets the UI show real names instead of
        /// the old "Locked" placeholder without guessing.</summary>
        public bool IsCanon => Kind != SectActivePowerKind.None && Name != null;
    }

    /// <summary>
    /// Per-sect lookup tables + level-scaling helper.
    /// </summary>
    public static partial class SectLeverEffects
    {
        /// <summary>Number of active powers every sect has. Design constant
        /// (docs/Design/Sects.md section 1).</summary>
        public const int ActiveSlots = 3;

        /// <summary>Duration value meaning "does not expire" — Sew Disorder III
        /// (until killed) and Raise Anew III (until destroyed).</summary>
        public const float Permanent = 0f;

        /// <summary>
        /// Canon active for one (sect, slot, level). Slot is 1-based to match
        /// the three UI buttons; level is 1-based (I / II / III). Each cluster
        /// that has been cut over to docs/Design/Sects.md contributes a table;
        /// a sect no table claims returns Kind = None, which is the signal for
        /// ActiveOf to fall back to the legacy tier table.
        /// </summary>
        public static SectActivePowerSpec CanonActive(string sectId, int slot, int level)
        {
            if (slot < 1) slot = 1; else if (slot > ActiveSlots) slot = ActiveSlots;
            if (level < 1) level = 1; else if (level > 3) level = 3;

            var spec = CanonActiveAlanthor(sectId, slot, level);
            if (spec.Kind != SectActivePowerKind.None) return spec;
            return CanonActiveFeraldis(sectId, slot, level);
        }

        /// <summary>
        /// True once a sect answers from a canon table. Canon sects hand the
        /// player all THREE of their actives on adoption — only the power
        /// LEVEL rides Temple upgrades (docs/Design/Sects.md sections 1 and 3)
        /// — whereas a legacy sect still unlocks its slots one Temple level at
        /// a time. SectActivePowerHelper.UnlockedTier is the one consumer that
        /// has to tell them apart.
        /// </summary>
        public static bool IsCanonSect(string sectId)
            => CanonActive(sectId, 1, 1).Kind != SectActivePowerKind.None;

        internal static SectActivePowerSpec Spec(
            SectActivePowerKind kind, SectRadius reach, string name, string description,
            float magnitude = 0f, float duration = 0f, float cooldown = 90f)
            => new SectActivePowerSpec
            {
                Kind        = kind,
                Reach       = reach,
                Radius      = SectRadii.Metres(reach),
                Magnitude   = magnitude,
                Duration    = duration,
                Cooldown    = cooldown,
                Name        = name,
                Description = description,
            };

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
            // Cut-over sects answer from canon; the table below is the legacy
            // fallback for the eight that have not had their pass yet.
            var canon = CanonActive(sectId, 1, 1);
            if (canon.Kind != SectActivePowerKind.None) return canon;

            switch (sectId)
            {
                // Recall the Codex (spec): AoE freeze of enemy attack/ability
                // cooldown recovery. Duration rides Magnitude so the level
                // scalar stretches it (10s / 15s / 20s at Lv I/II/III);
                // Lv III's extra "+50% current cooldowns" surge is handled
                // at dispatch.
                case SectConfig.Antiquity:   return new SectActivePowerSpec { Kind = SectActivePowerKind.FreezeCooldowns, Radius = 10f, Magnitude = 10f, Cooldown = 300f };
                case SectConfig.Renewal:     return new SectActivePowerSpec { Kind = SectActivePowerKind.HealCircle,    Radius = 8f,  Magnitude = 50f, Cooldown = 90f };
                case SectConfig.Fortitude:   return new SectActivePowerSpec { Kind = SectActivePowerKind.ArmorCircle,   Radius = 8f,  Magnitude = 5f,  Duration = 12f, Cooldown = 120f };
                case SectConfig.Reclamation: return new SectActivePowerSpec { Kind = SectActivePowerKind.HealCircle,    Radius = 6f,  Magnitude = 30f, Cooldown = 75f };
                case SectConfig.Silence:     return new SectActivePowerSpec { Kind = SectActivePowerKind.SpeedCircle,   Radius = 8f,  Magnitude = 1.20f, Duration = 8f, Cooldown = 90f };
                // Tiered actives (design 2026-07-05): tier 1 is the UTILITY
                // skill; the aggressive skills moved to tiers 2/3 (see the
                // ActiveOf(sectId, tier) overload below).
                case SectConfig.Justice:     return new SectActivePowerSpec { Kind = SectActivePowerKind.RevealCircle,  Radius = 14f, Duration = 10f, Cooldown = 60f };
                case SectConfig.Veneration:  return new SectActivePowerSpec { Kind = SectActivePowerKind.DamageCircle,  Radius = 8f,  Magnitude = 1.20f, Duration = 10f, Cooldown = 120f };
                case SectConfig.Witness:     return new SectActivePowerSpec { Kind = SectActivePowerKind.RevealCircle,  Radius = 16f, Duration = 12f, Cooldown = 75f };
                case SectConfig.War:         return new SectActivePowerSpec { Kind = SectActivePowerKind.SpeedCircle,   Radius = 8f,  Magnitude = 1.30f, Duration = 8f,  Cooldown = 75f };
                case SectConfig.Ash:         return new SectActivePowerSpec { Kind = SectActivePowerKind.BurningCircle, Radius = 6f,  Magnitude = 8f,  Duration = 6f,  Cooldown = 120f };
                // Unmake I — one building, half its current hp. Radius is the
                // search range, NOT a blast: exactly one building is ever hit.
                case SectConfig.Ruin:        return new SectActivePowerSpec { Kind = SectActivePowerKind.UnmakeBuilding, Radius = 8f, Magnitude = 0.50f, Cooldown = 150f };
                // Spite I — small area.
                case SectConfig.Wrath:       return new SectActivePowerSpec { Kind = SectActivePowerKind.SpitePool,      Radius = 6f, Cooldown = 120f };
                default: return default;
            }
        }

        /// <summary>
        /// Tiered actives (design 2026-07-05). Temple level unlocks tiers:
        ///   Tier 1 — utility (available on adoption)          → ActiveOf(sectId)
        ///   Tier 2 — economy / buff / aggressive second skill (temple Lv 2)
        ///   Tier 3 — ultimate: devastating offensive          (temple Lv 3)
        /// Only the playable sects (Justice / Renewal / War) have tiers 2-3;
        /// everything else returns Kind = None above tier 1.
        /// </summary>
        /// <summary>
        /// Canon lookup: which of the sect's three actives (slot, 1-based) at
        /// which power level (1-3, earned by adoption timing). Sects that have
        /// been cut over to docs/Design/Sects.md answer from CanonActive; the
        /// rest fall through to the legacy tier table below, which treats the
        /// slot as the old "tier" and ignores the level.
        /// </summary>
        public static SectActivePowerSpec ActiveOf(string sectId, int slot, int level)
        {
            var canon = CanonActive(sectId, slot, level);
            if (canon.Kind != SectActivePowerKind.None) return canon;
            return ActiveOf(sectId, slot);
        }

        public static SectActivePowerSpec ActiveOf(string sectId, int tier)
        {
            // Canon sects answer at every slot, including slot 1 — the legacy
            // "tier 1 is the sect's only power" shortcut does not apply to them.
            var canon = CanonActive(sectId, tier, 1);
            if (canon.Kind != SectActivePowerKind.None) return canon;

            if (tier <= 1) return ActiveOf(sectId);
            switch (sectId)
            {
                case SectConfig.Justice:
                    // T2 "Sentence" — focused smite. T3 "Final Sentence" —
                    // massive smite (spec: heavier windup handled globally).
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.SmiteCircle, Radius = 6f,  Magnitude = 60f,  Cooldown = 120f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.SmiteCircle, Radius = 10f, Magnitude = 150f, Cooldown = 240f };
                case SectConfig.Renewal:
                    // T2 "Mason's Blessing" — armor buff. T3 "Reckoning of the
                    // Rebuilt" — heavy smite.
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.ArmorCircle, Radius = 8f,  Magnitude = 3f,   Duration = 12f, Cooldown = 120f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.SmiteCircle, Radius = 9f,  Magnitude = 120f, Cooldown = 240f };
                case SectConfig.War:
                    // T2 "Bloodfury" — damage buff. T3 "Annihilation" —
                    // devastating smite.
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.DamageCircle, Radius = 8f,  Magnitude = 1.25f, Duration = 8f, Cooldown = 120f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.SmiteCircle,  Radius = 10f, Magnitude = 140f, Cooldown = 240f };

                // ── The other nine sects (2026-08-12) ────────────────────────
                // These returned Kind = None above tier 1, so every chapel but
                // Justice / Renewal / War showed "Locked" on its Active lever
                // at temple Lv 2-3 and the power silently refused to fire.
                // task-063 canon gives ALL twelve sects an Active at Lv I/II/III,
                // so the gap was an unfinished implementation, not a design call.
                //
                // Each sect escalates its OWN tier-1 kind along the direction its
                // canonical entry describes — bigger effect, longer duration,
                // shorter cooldown — so no new SectActivePowerKind (and no engine
                // work) is needed; the dispatcher already handles all nine kinds.

                case SectConfig.Antiquity:   // Recall the Codex: 10s → 15s → 20s freeze
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.FreezeCooldowns, Radius = 12f, Magnitude = 15f, Cooldown = 240f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.FreezeCooldowns, Radius = 14f, Magnitude = 20f, Cooldown = 180f };

                case SectConfig.Fortitude:   // Stoneveil: longer, heavier ward
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.ArmorCircle, Radius = 9f,  Magnitude = 8f,  Duration = 15f, Cooldown = 100f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.ArmorCircle, Radius = 10f, Magnitude = 12f, Duration = 18f, Cooldown = 80f };

                case SectConfig.Reclamation: // Harvest the Veil: bigger restorative burst
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.HealCircle, Radius = 8f,  Magnitude = 55f, Cooldown = 65f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.HealCircle, Radius = 10f, Magnitude = 85f, Cooldown = 55f };

                case SectConfig.Silence:     // Whisper-Wind → Entomb tempo: faster, longer
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.SpeedCircle, Radius = 9f,  Magnitude = 1.30f, Duration = 10f, Cooldown = 75f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.SpeedCircle, Radius = 10f, Magnitude = 1.40f, Duration = 12f, Cooldown = 60f };

                case SectConfig.Veneration:  // Crystal Communion: +25% → +35% → +50% damage
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.DamageCircle, Radius = 9f,  Magnitude = 1.35f, Duration = 12f, Cooldown = 100f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.DamageCircle, Radius = 10f, Magnitude = 1.50f, Duration = 14f, Cooldown = 80f };

                case SectConfig.Witness:     // Foresight: wider and longer reveal
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.RevealCircle, Radius = 22f, Duration = 15f, Cooldown = 60f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.RevealCircle, Radius = 28f, Duration = 20f, Cooldown = 50f };

                case SectConfig.Ash:         // Pyre: 15s → 30s → 45s of burning ground
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.BurningCircle, Radius = 7f, Magnitude = 11f, Duration = 9f,  Cooldown = 100f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.BurningCircle, Radius = 8f, Magnitude = 14f, Duration = 12f, Cooldown = 80f };

                // Unmake (docs/Design/Sects.md): ONE building, never an area.
                // Magnitude is the FRACTION of that building's CURRENT hp it
                // loses — 50% / 75% / 90%. Radius is only the search range for
                // "the nearest enemy building to the cast point"; III adds the
                // 25% splash to other buildings, handled by the executor.
                case SectConfig.Ruin:
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.UnmakeBuilding, Radius = 9f,  Magnitude = 0.75f, Cooldown = 125f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.UnmakeBuilding, Radius = 10f, Magnitude = 0.90f, Cooldown = 100f };

                // Spite: the level scales the AREA only (small / medium /
                // large). The arithmetic — pool every enemy's damage dealt,
                // split it back over them — is identical at every level.
                case SectConfig.Wrath:
                    return tier == 2
                        ? new SectActivePowerSpec { Kind = SectActivePowerKind.SpitePool, Radius = 9f,  Cooldown = 100f }
                        : new SectActivePowerSpec { Kind = SectActivePowerKind.SpitePool, Radius = 13f, Cooldown = 80f };

                default:
                    return default; // Kind = None — unknown sect id
            }
        }
    }
}
