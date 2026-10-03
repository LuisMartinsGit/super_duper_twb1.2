// SectLeverEffects.cs
// Per-sect data tables for the Building / Unit / Active-Power levers
// (task-063 phase 5). The Passive lever has its own per-sect ECS systems
// (SectFortitudeHpSystem, SectVenerationFervorSystem, etc.) because each
// passive has its own bespoke trigger and side effects. The other three
// levers are implemented uniformly:
//
//   - Building lever: RETIRED. There is no chapel aura (docs/Design/Sects.md
//     section 1); AuraOf returns no aura for every sect.
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
        RaiseTower,         // Renewal "Raise Anew": raise a permanent Renewal structure
                            // (Magnitude picks Tower / Fortification / Fortress)
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

        // ── Antiquity / Witness canon kinds (docs/Design/Sects.md, 2026-09-08)
        // The pass that gave the two intel sects something that actually
        // touches the enemy. Both used to be pure reveal.
        AttainderStrike,    // Antiquity "Writ of Attainder": damage scaled by how many of
                            // YOUR units each enemy has killed (Magnitude = per kill,
                            // Secondary = the floor a killer-of-nothing still takes)
        SpyNetwork,         // Witness "Spy Network": turn ONE enemy unit into an unwitting
                            // eye; it spreads to enemies that linger near it
        Blind,              // Witness "Blinding Glare": enemies lose all vision
                            // (Magnitude >= 1 also locks their abilities)
        RevealedStrike,     // Witness "Nowhere to Hide": damage every enemy you can
                            // currently SEE, anywhere on the map — reach is not a radius
    }

    public struct SectActivePowerSpec
    {
        public SectActivePowerKind Kind;
        public float Radius;
        public float Magnitude;       // damage / heal / armor amount
        public float Duration;        // seconds (where applicable)
        public float Cooldown;        // base cooldown — Phase 4 reduces with level

        /// <summary>
        /// A SECOND number, for the handful of powers that genuinely need two
        /// and would otherwise smuggle one through <see cref="Duration"/>.
        /// Its meaning is the power's own and is documented at the spec that
        /// sets it — Writ of Attainder's damage floor, Spy Network's cascade
        /// radius. Zero for every power that needs only one.
        /// </summary>
        public float Secondary;

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

        // Backing pair so a spec that never names a type reads as Magic —
        // a plain enum field would default to Melee (0), which no spell is.
        private DamageType _damageType;
        private bool _damageTypeSet;

        /// <summary>
        /// Armor column this power's damage is measured against, routed
        /// through SpellDamage.Apply. Magic unless the design names another
        /// (Justice's Sentence is True damage, docs/Design/Sects.md).
        /// docs/Design/Spells.md.
        /// </summary>
        public DamageType DamageType
        {
            get => _damageTypeSet ? _damageType : DamageType.Magic;
            set { _damageType = value; _damageTypeSet = true; }
        }

        /// <summary>
        /// The damage number this power deals per victim, for the kinds that
        /// deal a flat amount (it is carried in <see cref="Magnitude"/>; for
        /// Writ of Attainder it is the amount PER KILL). 0 for everything else,
        /// including Unmake (a fraction of current HP) and Spite (a pool).
        /// Read-only on purpose: the tables keep authoring Magnitude.
        /// </summary>
        public float Damage => Kind switch
        {
            SectActivePowerKind.SmiteCircle     => Magnitude,
            SectActivePowerKind.RevealedStrike  => Magnitude,
            SectActivePowerKind.AttainderStrike => Magnitude,
            _                                   => 0f,
        };
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

        // There is no global cooldown scale. Until 2026-09-27 every authored
        // cooldown was multiplied by 0.5 (CooldownScale), so a table saying
        // 240 charged 120. That halving is folded into the authored numbers:
        // what a spec says is what a player without the Shardroot waits
        // (docs/Design/Spells.md sections 5 and 8.2).

        /// <summary>Cooldown scale with the Shardroot enshrined for the sect:
        /// "all sect power cooldowns reduced by 30%"
        /// (docs/Design/Curse_And_Shardroot.md).</summary>
        public const float ShardrootCooldownScale = 0.7f;

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
            spec = CanonActiveFeraldis(sectId, slot, level);
            if (spec.Kind != SectActivePowerKind.None) return spec;
            return CanonActiveRunai(sectId, slot, level);
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
            float magnitude = 0f, float duration = 0f, float cooldown = 90f,
            float secondary = 0f)
            => new SectActivePowerSpec
            {
                Kind        = kind,
                Reach       = reach,
                Radius      = SectRadii.Metres(reach),
                Magnitude   = magnitude,
                Duration    = duration,
                Cooldown    = cooldown,
                Secondary   = secondary,
                Name        = name,
                Description = description,
            };

        /// <summary>
        /// Magnitude multiplier per level. Lv I = 1.00, Lv II = 1.5,
        /// Lv III = 2.0 -- the level rule of the spell ladder
        /// (docs/Design/Spells.md section 8.3, SpellLadder). Cooldowns do NOT
        /// scale with level: they are flat per power (section 8.2).
        /// </summary>
        public static float LevelScalar(byte level) => level switch
        {
            2 => 1.5f,
            3 => 2.0f,
            _ => 1.0f,
        };

        /// <summary>
        /// Chapel aura for a sect: none, for every sect. docs/Design/Sects.md
        /// section 1: "There is no chapel aura" -- a sect projects no passive
        /// area effect unless its Passive or Research says so. Radius 0 is the
        /// "no aura" signal SectBuildingLeverSystem already skips, so that
        /// system is a no-op until it is deleted.
        /// </summary>
        public static SectAuraSpec AuraOf(string sectId) => default;

        public static SectUnitLeverSpec UnitOf(string sectId)
        {
            // Class indices match the UnitClass enum (Melee 0..Scout 7).
            switch (sectId)
            {
                case SectConfig.Antiquity:   return new SectUnitLeverSpec { AppliesToClass = -1, DamageMultiplier = 1.04f };
                case SectConfig.Renewal:     return new SectUnitLeverSpec { AppliesToClass = -1, HpMultiplier = 1.05f };
                case SectConfig.Fortitude:   return new SectUnitLeverSpec { AppliesToClass = 0,  ArmorBonus = 3 };  // melee +armor
                case SectConfig.Reclamation: return new SectUnitLeverSpec { AppliesToClass = 6,  ArmorBonus = 5 };  // workers +armor
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

        public static SectActivePowerSpec ActiveOf(string sectId) => ActiveOf(sectId, 1);

        /// <summary>
        /// Canon lookup: which of the sect's three actives (slot, 1-based) at
        /// which power level (1-3, earned by adoption timing). Sects that have
        /// been cut over to docs/Design/Sects.md answer from CanonActive; the
        /// rest fall through to the legacy table below, which treats the slot
        /// as the level and ignores the level argument.
        /// </summary>
        public static SectActivePowerSpec ActiveOf(string sectId, int slot, int level)
        {
            var canon = CanonActive(sectId, slot, level);
            if (canon.Kind != SectActivePowerKind.None) return canon;
            return ActiveOf(sectId, slot);
        }

        /// <summary>
        /// One active by SLOT. Canon sects answer from their tables at level I;
        /// the six sects still on the legacy table answer from LegacyActive.
        /// </summary>
        public static SectActivePowerSpec ActiveOf(string sectId, int tier)
        {
            var canon = CanonActive(sectId, tier, 1);
            if (canon.Kind != SectActivePowerKind.None) return canon;
            return LegacyActive(sectId, tier);
        }

        /// <summary>
        /// The six sects not yet cut over to their canon kits (Silence,
        /// Justice, Veneration, Ash, Ruin, Wrath) each have ONE implemented
        /// power, and a legacy sect unlocks its slots one Temple level at a
        /// time -- so slot 1 / 2 / 3 is that power at level I / II / III.
        /// Justice is the exception: Eye of the Law I, Sentence I, and
        /// Sentence III (named Final Sentence).
        ///
        /// Every figure comes off the spell ladder (SpellLadder,
        /// docs/Design/Spells.md sections 8 and 9.3): canon radii, flat
        /// cooldowns by band, per-victim damage by reach x level. The canon
        /// sects' old rows that used to sit here were unreachable (CanonActive
        /// answers first) and are gone.
        /// </summary>
        private static SectActivePowerSpec LegacyActive(string sectId, int tier)
        {
            int lv = tier < 1 ? 1 : (tier > 3 ? 3 : tier);
            switch (sectId)
            {
                // Whisper-Wind: reach grows, the +20% holds (docs/Design/Sects.md).
                case SectConfig.Silence:
                    return lv switch
                    {
                        1 => Spec(SectActivePowerKind.SpeedCircle, SectRadius.Small, "Whisper-Wind",
                                  "Allies in a small area move 20% faster for 8s.",
                                  magnitude: 1.20f, duration: 8f, cooldown: SpellLadder.SectTacticalCooldown),
                        2 => Spec(SectActivePowerKind.SpeedCircle, SectRadius.Medium, "Whisper-Wind",
                                  "Allies in a medium area move 20% faster for 12s.",
                                  magnitude: 1.20f, duration: 12f, cooldown: SpellLadder.SectTacticalCooldown),
                        _ => Spec(SectActivePowerKind.SpeedCircle, SectRadius.Large, "Whisper-Wind",
                                  "Allies in a large area move 20% faster for 12s.",
                                  magnitude: 1.20f, duration: 12f, cooldown: SpellLadder.SectTacticalCooldown),
                    };

                // Justice: Eye of the Law I, then Sentence -- TRUE damage
                // (docs/Design/Sects.md) -- at its level I and level III figures.
                case SectConfig.Justice:
                    return lv switch
                    {
                        1 => Spec(SectActivePowerKind.RevealCircle, SectRadius.Medium, "Eye of the Law",
                                  "Reveal a medium area for 10s.",
                                  duration: 10f, cooldown: SpellLadder.SectTacticalCooldown),
                        2 => AsTrue(Spec(SectActivePowerKind.SmiteCircle, SectRadius.Single, "Sentence",
                                  "One enemy takes 120 true damage after a 3s telegraph.",
                                  magnitude: SpellLadder.Damage(SectRadius.Single, 1),
                                  cooldown: SpellLadder.SectDamageCooldown)),
                        _ => AsTrue(Spec(SectActivePowerKind.SmiteCircle, SectRadius.Medium, "Final Sentence",
                                  "Enemies in a medium area take 80 true damage after a 3s telegraph.",
                                  magnitude: SpellLadder.Damage(SectRadius.Medium, 3),
                                  cooldown: SpellLadder.SectDamageCooldown)),
                    };

                // Litany (docs/Design/Sects.md): +20% small 10s, medium 15s,
                // then large 15s at +50%.
                case SectConfig.Veneration:
                    return lv switch
                    {
                        1 => Spec(SectActivePowerKind.DamageCircle, SectRadius.Small, "Litany",
                                  "Allies in a small area deal +20% damage for 10s.",
                                  magnitude: 1.20f, duration: 10f, cooldown: SpellLadder.SectTacticalCooldown),
                        2 => Spec(SectActivePowerKind.DamageCircle, SectRadius.Medium, "Litany",
                                  "Allies in a medium area deal +20% damage for 15s.",
                                  magnitude: 1.20f, duration: 15f, cooldown: SpellLadder.SectTacticalCooldown),
                        _ => Spec(SectActivePowerKind.DamageCircle, SectRadius.Large, "Litany",
                                  "Allies in a large area deal +50% damage for 15s.",
                                  magnitude: 1.50f, duration: 15f, cooldown: SpellLadder.SectTacticalCooldown),
                    };

                // Pyre: a damage zone. Magnitude is DPS, the zone rule (band
                // over SpellLadder.ZoneSeconds), which holds at 6 as the reach
                // grows. Fire is ownerless (docs/Design/Fire.md section 3).
                case SectConfig.Ash:
                    return lv switch
                    {
                        1 => Spec(SectActivePowerKind.BurningCircle, SectRadius.Small, "Pyre",
                                  "Ignite a small area for 15s: 6 damage per second to anyone standing in it.",
                                  magnitude: SpellLadder.ZoneDps(SectRadius.Small, 1), duration: 15f,
                                  cooldown: SpellLadder.SectDamageCooldown),
                        2 => Spec(SectActivePowerKind.BurningCircle, SectRadius.Medium, "Pyre",
                                  "Ignite a medium area for 30s: 6 damage per second to anyone standing in it.",
                                  magnitude: SpellLadder.ZoneDps(SectRadius.Medium, 2), duration: 30f,
                                  cooldown: SpellLadder.SectDamageCooldown),
                        _ => Spec(SectActivePowerKind.BurningCircle, SectRadius.Large, "Pyre",
                                  "Ignite a large area for 30s: 6 damage per second to anyone standing in it.",
                                  magnitude: SpellLadder.ZoneDps(SectRadius.Large, 3), duration: 30f,
                                  cooldown: SpellLadder.SectDamageCooldown),
                    };

                // Unmake (docs/Design/Sects.md): ONE building, never an area.
                // Magnitude is the FRACTION of that building's CURRENT hp it
                // loses -- UnmakeFraction x the level multiplier. The small
                // radius is only the search range for "the nearest enemy
                // building to the cast point"; III adds the small-area splash
                // (ApplyUnmake).
                case SectConfig.Ruin:
                    return lv switch
                    {
                        1 => AsTrue(Spec(SectActivePowerKind.UnmakeBuilding, SectRadius.Small, "Unmake",
                                  "The nearest enemy building in a small area loses 40% of its current HP after a 3s telegraph.",
                                  magnitude: UnmakeFraction * LevelScalar(1), cooldown: SpellLadder.SectDamageCooldown)),
                        2 => AsTrue(Spec(SectActivePowerKind.UnmakeBuilding, SectRadius.Small, "Unmake",
                                  "The nearest enemy building in a small area loses 60% of its current HP after a 3s telegraph.",
                                  magnitude: UnmakeFraction * LevelScalar(2), cooldown: SpellLadder.SectDamageCooldown)),
                        _ => AsTrue(Spec(SectActivePowerKind.UnmakeBuilding, SectRadius.Small, "Unmake",
                                  "The nearest enemy building in a small area loses 80% of its current HP; other buildings in a small area around it lose 25%.",
                                  magnitude: UnmakeFraction * LevelScalar(3), cooldown: SpellLadder.SectDamageCooldown)),
                    };

                // Spite: the level scales the AREA only. Magnitude is the
                // per-head CAP -- the conditional cap, 2x the band -- so a
                // lone veteran pays its account up to 120, not all at once.
                case SectConfig.Wrath:
                    return lv switch
                    {
                        1 => AsTrue(Spec(SectActivePowerKind.SpitePool, SectRadius.Small, "Spite",
                                  "Enemies in a small area pool the damage they have dealt this match and split it back over themselves, at most 120 each.",
                                  magnitude: SpellLadder.ConditionalCap(SectRadius.Small, 1),
                                  cooldown: SpellLadder.SectDamageCooldown)),
                        2 => AsTrue(Spec(SectActivePowerKind.SpitePool, SectRadius.Medium, "Spite",
                                  "Enemies in a medium area pool the damage they have dealt and split it back, at most 120 each.",
                                  magnitude: SpellLadder.ConditionalCap(SectRadius.Medium, 2),
                                  cooldown: SpellLadder.SectDamageCooldown)),
                        _ => AsTrue(Spec(SectActivePowerKind.SpitePool, SectRadius.Large, "Spite",
                                  "Enemies in a large area pool the damage they have dealt and split it back, at most 120 each.",
                                  magnitude: SpellLadder.ConditionalCap(SectRadius.Large, 3),
                                  cooldown: SpellLadder.SectDamageCooldown)),
                    };

                default:
                    return default; // Kind = None -- unknown sect id
            }
        }

        /// <summary>Unmake's level-I fraction of current HP; the level
        /// multiplier makes it 40 / 60 / 80 %.</summary>
        public const float UnmakeFraction = 0.40f;

        /// <summary>Unmake III's splash, as a fraction of each splashed
        /// building's current HP. Its radius is the canon Small.</summary>
        public const float UnmakeSplashFraction = 0.25f;

        /// <summary>Writ of Attainder bills at most this many kills per victim
        /// (the ladder's conditional cap: 30 per kill x 4 = 120 = 2x the band).</summary>
        public const int AttainderMaxKills = 4;

        private static SectActivePowerSpec AsTrue(SectActivePowerSpec spec)
        {
            spec.DamageType = DamageType.True;
            return spec;
        }
    }
}
