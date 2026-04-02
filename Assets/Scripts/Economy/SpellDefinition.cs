// SpellDefinition.cs
// Static data definitions for all 12 sect spells
// Location: Assets/Scripts/Economy/SpellDefinition.cs

using System.Collections.Generic;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Data definition for a single active spell.
    /// Spells are unlocked when a faction adopts a sect.
    /// Cast by selecting the spell and clicking a target location.
    /// </summary>
    public class SpellDefinition
    {
        /// <summary>Unique spell identifier (e.g., "Spell_RepairLevies")</summary>
        public string Id;

        /// <summary>Sect ID that unlocks this spell (e.g., "Sect_Renewal")</summary>
        public string SectId;

        /// <summary>Display name shown in UI</summary>
        public string Name;

        /// <summary>Base cooldown in seconds</summary>
        public float Cooldown;

        /// <summary>Maximum cast range from caster (0 = global)</summary>
        public float Range;

        /// <summary>Area of effect radius (0 = single target)</summary>
        public float AreaRadius;

        /// <summary>Duration of the effect in seconds (0 = instant)</summary>
        public float Duration;

        /// <summary>
        /// Effect category: Heal, Buff, Debuff, Damage, Vision, Invulnerable, Disable
        /// </summary>
        public string EffectType;

        /// <summary>Primary numeric effect value (HP healed, armor bonus, etc.)</summary>
        public float EffectValue;

        /// <summary>Secondary effect value (e.g., speed multiplier for BattleFervor)</summary>
        public float SecondaryValue;

        /// <summary>Short description shown in tooltip</summary>
        public string Description;

        /// <summary>Whether this spell targets friendly entities (true) or enemy (false)</summary>
        public bool TargetsFriendly;
    }

    /// <summary>
    /// Static database of all 12 sect spells.
    /// One spell per sect, unlocked on adoption.
    /// </summary>
    public static class SpellDatabase
    {
        private static readonly Dictionary<string, SpellDefinition> _spellsBySect = new();
        private static readonly Dictionary<string, SpellDefinition> _spellsById = new();
        private static bool _initialized;

        /// <summary>
        /// Get the spell definition for a sect. Returns null if sect has no spell.
        /// </summary>
        public static SpellDefinition GetSpellForSect(string sectId)
        {
            EnsureInitialized();
            return _spellsBySect.TryGetValue(sectId, out var spell) ? spell : null;
        }

        /// <summary>
        /// Get a spell definition by its spell ID.
        /// </summary>
        public static SpellDefinition GetSpell(string spellId)
        {
            EnsureInitialized();
            return _spellsById.TryGetValue(spellId, out var spell) ? spell : null;
        }

        /// <summary>
        /// Get all spell definitions.
        /// </summary>
        public static IReadOnlyCollection<SpellDefinition> GetAllSpells()
        {
            EnsureInitialized();
            return _spellsBySect.Values;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // ── Alanthor Sects ──

            Register(new SpellDefinition
            {
                Id = "Spell_RestorationWave",
                SectId = SectConfig.Renewal,
                Name = "Restoration Wave",
                Cooldown = 90f,
                Range = 0f, // Global
                AreaRadius = 0f,
                Duration = 0f, // Instant
                EffectType = "Heal",
                EffectValue = 100f, // 100 HP to units, 200 HP to buildings (handled in code)
                SecondaryValue = 200f,
                Description = "Heal all friendly units by 100 HP and buildings by 200 HP globally",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_ArcaneBombardment",
                SectId = SectConfig.Antiquity,
                Name = "Arcane Bombardment",
                Cooldown = 120f,
                Range = 50f,
                AreaRadius = 20f,
                Duration = 8f,
                EffectType = "Damage",
                EffectValue = 50f, // 50 damage per bolt
                SecondaryValue = 8f, // 8 bolts
                Description = "Rain 8 arcane bolts over 8s, each dealing 50 magic damage in a 3u area",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_Earthquake",
                SectId = SectConfig.LivingStone,
                Name = "Earthquake",
                Cooldown = 150f,
                Range = 50f,
                AreaRadius = 25f,
                Duration = 0f, // Instant
                EffectType = "Damage",
                EffectValue = 300f, // 300 siege damage to buildings (600 to walls)
                SecondaryValue = 2f, // 2 second stun
                Description = "Deal 300 siege damage to enemy buildings (600 to walls), stun ground units for 2s",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_VeilOfShadows",
                SectId = SectConfig.VeiledMemory,
                Name = "Veil of Shadows",
                Cooldown = 100f,
                Range = 0f, // Global
                AreaRadius = 0f,
                Duration = 15f,
                EffectType = "Buff",
                EffectValue = 0f,
                SecondaryValue = 0f,
                Description = "All friendly units become invisible for 15s",
                TargetsFriendly = true
            });

            // ── Runai Sects ──

            Register(new SpellDefinition
            {
                Id = "Spell_SummonCaravanGuard",
                SectId = SectConfig.StillFlame,
                Name = "Summon Caravan Guard",
                Cooldown = 80f,
                Range = 50f,
                AreaRadius = 0f,
                Duration = 60f,
                EffectType = "Summon",
                EffectValue = 5f, // 5 units
                SecondaryValue = 60f, // 60s lifetime
                Description = "Summon 5 Flame Wardens at target that last 60s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_GoldenTribute",
                SectId = SectConfig.QuietVault,
                Name = "Golden Tribute",
                Cooldown = 70f,
                Range = 0f, // Global
                AreaRadius = 0f,
                Duration = 30f,
                EffectType = "Buff",
                EffectValue = 500f, // 500 supplies
                SecondaryValue = 200f, // 200 iron
                Description = "Instantly gain 500 Supplies + 200 Iron + 100 Crystal; production boost for 30s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_ArcaneStorm",
                SectId = SectConfig.MirrorRite,
                Name = "Arcane Storm",
                Cooldown = 130f,
                Range = 50f,
                AreaRadius = 20f,
                Duration = 8f,
                EffectType = "Damage",
                EffectValue = 30f, // 30 damage per bolt
                SecondaryValue = 16f, // 16 bolts
                Description = "Lightning storm: 16 bolts over 8s, each dealing 30 magic + 10 splash damage",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_Dominate",
                SectId = SectConfig.ShardJudgment,
                Name = "Dominate",
                Cooldown = 110f,
                Range = 40f,
                AreaRadius = 0f,
                Duration = 30f,
                EffectType = "Disable",
                EffectValue = 0f,
                SecondaryValue = 30f, // 30s duration
                Description = "Mind-control nearest enemy unit for 30s",
                TargetsFriendly = false
            });

            // ── Feraldis Sects ──

            Register(new SpellDefinition
            {
                Id = "Spell_Firestorm",
                SectId = SectConfig.EmberAsh,
                Name = "Firestorm",
                Cooldown = 100f,
                Range = 50f,
                AreaRadius = 15f,
                Duration = 10f,
                EffectType = "Damage",
                EffectValue = 10f, // 10 DPS
                SecondaryValue = 20f, // 20s burn visual persistence
                Description = "Create burning ground dealing 10 DPS for 10s in a 15u area",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_SummonWarHost",
                SectId = SectConfig.HollowBrand,
                Name = "Summon War Host",
                Cooldown = 120f,
                Range = 50f,
                AreaRadius = 0f,
                Duration = 60f,
                EffectType = "Summon",
                EffectValue = 5f, // 5 units (3 Brandbreaker + 2 Ashblade)
                SecondaryValue = 60f, // 60s lifetime
                Description = "Summon 3 Brandbreakers + 2 Ashblades at target that last 60s",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_ChainLightning",
                SectId = SectConfig.FlamewroughtChains,
                Name = "Chain Lightning",
                Cooldown = 90f,
                Range = 50f,
                AreaRadius = 0f,
                Duration = 0f, // Instant
                EffectType = "Damage",
                EffectValue = 80f, // 80 primary damage
                SecondaryValue = 40f, // 40 chain damage
                Description = "Strike target for 80 damage, chains to 5 nearby enemies for 40 each",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_Annihilation",
                SectId = SectConfig.UnmakersGrasp,
                Name = "Annihilation",
                Cooldown = 180f,
                Range = 50f,
                AreaRadius = 0f,
                Duration = 0f, // Instant
                EffectType = "Damage",
                EffectValue = 500f, // 500 true damage
                SecondaryValue = 0f,
                Description = "Deal 500 true damage to target; instantly destroys Crystal sub-nodes",
                TargetsFriendly = false
            });
        }

        private static void Register(SpellDefinition spell)
        {
            _spellsBySect[spell.SectId] = spell;
            _spellsById[spell.Id] = spell;
        }
    }
}
