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

            Register(new SpellDefinition
            {
                Id = "Spell_RepairLevies",
                SectId = SectConfig.Renewal,
                Name = "Repair Levies",
                Cooldown = 40f,
                Range = 30f,
                AreaRadius = 15f,
                Duration = 0f,
                EffectType = "Heal",
                EffectValue = 200f,
                SecondaryValue = 0f,
                Description = "Repair buildings in area by 200 HP",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_CrystalSurvey",
                SectId = SectConfig.Antiquity,
                Name = "Crystal Survey",
                Cooldown = 50f,
                Range = 0f, // Global
                AreaRadius = 0f,
                Duration = 30f,
                EffectType = "Vision",
                EffectValue = 0f,
                SecondaryValue = 0f,
                Description = "Reveal Crystal nodes on minimap for 30s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_BulwarkRise",
                SectId = SectConfig.LivingStone,
                Name = "Bulwark Rise",
                Cooldown = 60f,
                Range = 30f,
                AreaRadius = 15f,
                Duration = 20f,
                EffectType = "Buff",
                EffectValue = 3f, // +3 armor
                SecondaryValue = 0f,
                Description = "+3 armor to buildings in area for 20s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_Shroud",
                SectId = SectConfig.VeiledMemory,
                Name = "Shroud",
                Cooldown = 55f,
                Range = 30f,
                AreaRadius = 12f,
                Duration = 15f,
                EffectType = "Vision",
                EffectValue = 0f,
                SecondaryValue = 0f,
                Description = "Block enemy vision in area for 15s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_Embargo",
                SectId = SectConfig.StillFlame,
                Name = "Embargo",
                Cooldown = 60f,
                Range = 30f,
                AreaRadius = 20f,
                Duration = 20f,
                EffectType = "Disable",
                EffectValue = 0f,
                SecondaryValue = 0f,
                Description = "Disable enemy trade in area for 20s",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_LockdownVault",
                SectId = SectConfig.QuietVault,
                Name = "Lockdown Vault",
                Cooldown = 70f,
                Range = 20f,
                AreaRadius = 10f,
                Duration = 15f,
                EffectType = "Invulnerable",
                EffectValue = 0f,
                SecondaryValue = 0f,
                Description = "Buildings invulnerable for 15s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_ReflectiveWard",
                SectId = SectConfig.MirrorRite,
                Name = "Reflective Ward",
                Cooldown = 60f,
                Range = 25f,
                AreaRadius = 12f,
                Duration = 10f,
                EffectType = "Buff",
                EffectValue = 0f,
                SecondaryValue = 0.25f, // 25% reflect
                Description = "Reflect 25% damage for 10s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_EdictOfSeizure",
                SectId = SectConfig.ShardJudgment,
                Name = "Edict of Seizure",
                Cooldown = 65f,
                Range = 30f,
                AreaRadius = 15f,
                Duration = 10f,
                EffectType = "Debuff",
                EffectValue = 50f, // 50 Supplies drained over duration
                SecondaryValue = 0f,
                Description = "Drain 50 Supplies from enemy over 10s",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_BattleFervor",
                SectId = SectConfig.EmberAsh,
                Name = "Battle Fervor",
                Cooldown = 55f,
                Range = 25f,
                AreaRadius = 10f,
                Duration = 10f,
                EffectType = "Buff",
                EffectValue = 1.25f, // +25% damage
                SecondaryValue = 1.25f, // +25% speed
                Description = "+25% attack/speed for units in area for 10s",
                TargetsFriendly = true
            });

            Register(new SpellDefinition
            {
                Id = "Spell_ProfaneRally",
                SectId = SectConfig.HollowBrand,
                Name = "Profane Rally",
                Cooldown = 60f,
                Range = 25f,
                AreaRadius = 10f,
                Duration = 5f,
                EffectType = "Debuff",
                EffectValue = 0.30f, // 30% slow
                SecondaryValue = 0f,
                Description = "Slow enemies 30% for 5s",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_BindTheCore",
                SectId = SectConfig.FlamewroughtChains,
                Name = "Bind the Core",
                Cooldown = 90f,
                Range = 30f,
                AreaRadius = 0f,
                Duration = 15f,
                EffectType = "Disable",
                EffectValue = 0f,
                SecondaryValue = 0f,
                Description = "Pacify Crystal sub-node for 15s",
                TargetsFriendly = false
            });

            Register(new SpellDefinition
            {
                Id = "Spell_Unravel",
                SectId = SectConfig.UnmakersGrasp,
                Name = "Unravel",
                Cooldown = 80f,
                Range = 25f,
                AreaRadius = 0f,
                Duration = 0f,
                EffectType = "Damage",
                EffectValue = 300f, // 300 true damage
                SecondaryValue = 0f,
                Description = "Deal 300 true damage to Crystal entity",
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
