// SpellDefinition.cs
// Static data definitions for active spells.
// Location: Assets/Scripts/Economy/SpellDefinition.cs
//
// task-063 phase 1: the 12 old sect spell registrations were deleted because
// they referenced removed sect IDs (Sect_Renewal, Sect_Antiquity, ...). The
// type itself, SpellDatabase shell, and SpellCastSystem hooks remain so
// Phase 2 can wire the new per-sect Active Power lever (one active per
// adopted sect) without rebuilding the data plumbing.

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
        /// <summary>Unique spell identifier (e.g., "Spell_RestorationWave")</summary>
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
    /// Static database of active spells. Empty in Phase 1 — Phase 2 will
    /// populate one entry per adopted sect (the sect's "Active Power" lever).
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

            // task-063 phase 1: registrations for the 12 old sects deleted.
            // Phase 2 will register one Active Power per new-roster sect here
            // (Antiquity / Renewal / Fortitude / Reclamation / Silence / Justice
            // / Veneration / Witness / War / Ash / Ruin / Wrath).
        }

        // ReSharper disable once UnusedMember.Local
        private static void Register(SpellDefinition spell)
        {
            _spellsBySect[spell.SectId] = spell;
            _spellsById[spell.Id] = spell;
        }
    }
}
