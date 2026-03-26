// SpellState.cs
// Tracks spell cooldowns per faction
// Location: Assets/Scripts/Economy/SpellState.cs

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// MonoBehaviour singleton that tracks spell cooldowns for each faction.
    /// Updated every frame to tick down cooldowns.
    /// Queried by SpellPanel for UI display and SpellCastSystem for cast validation.
    /// </summary>
    public class SpellState : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════

        public static SpellState Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DATA
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cooldown timers per faction. Key = (int)Faction, Value = (spellId -> remainingSeconds).
        /// Only contains entries for spells currently on cooldown.
        /// </summary>
        private readonly Dictionary<int, Dictionary<string, float>> _cooldownsByFaction = new();

        // ═══════════════════════════════════════════════════════════════════
        // UPDATE
        // ═══════════════════════════════════════════════════════════════════

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Tick down all active cooldowns
            var keysToRemove = new List<string>();

            foreach (var factionKvp in _cooldownsByFaction)
            {
                keysToRemove.Clear();
                var cooldowns = factionKvp.Value;

                foreach (var spellKvp in cooldowns)
                {
                    float remaining = spellKvp.Value - dt;
                    if (remaining <= 0f)
                        keysToRemove.Add(spellKvp.Key);
                    else
                        cooldowns[spellKvp.Key] = remaining;
                }

                foreach (var key in keysToRemove)
                    cooldowns.Remove(key);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if a spell is currently on cooldown for a faction.
        /// </summary>
        public bool IsOnCooldown(Faction faction, string spellId)
        {
            int key = (int)faction;
            return _cooldownsByFaction.TryGetValue(key, out var cooldowns) &&
                   cooldowns.ContainsKey(spellId);
        }

        /// <summary>
        /// Get the remaining cooldown time for a spell. Returns 0 if not on cooldown.
        /// </summary>
        public float GetCooldownRemaining(Faction faction, string spellId)
        {
            int key = (int)faction;
            if (_cooldownsByFaction.TryGetValue(key, out var cooldowns) &&
                cooldowns.TryGetValue(spellId, out float remaining))
                return remaining;
            return 0f;
        }

        /// <summary>
        /// Start a cooldown for a spell. Duration is the base cooldown, modified by
        /// sect tech SpellCooldownReduction if applicable.
        /// </summary>
        public void StartCooldown(Faction faction, string spellId, float duration)
        {
            // Apply spell cooldown reduction from sect techs
            var sectState = FactionSectState.Instance;
            if (sectState != null)
            {
                var mults = sectState.GetMultipliers(faction);
                if (mults.SpellCooldownReduction > 0f)
                    duration *= (1f - mults.SpellCooldownReduction);
            }

            int key = (int)faction;
            if (!_cooldownsByFaction.TryGetValue(key, out var cooldowns))
            {
                cooldowns = new Dictionary<string, float>();
                _cooldownsByFaction[key] = cooldowns;
            }

            cooldowns[spellId] = duration;
        }

        /// <summary>
        /// Get the effective cooldown for a spell definition (with tech reductions applied).
        /// Used by UI to show total cooldown time.
        /// </summary>
        public float GetEffectiveCooldown(Faction faction, SpellDefinition spell)
        {
            float cd = spell.Cooldown;
            var sectState = FactionSectState.Instance;
            if (sectState != null)
            {
                var mults = sectState.GetMultipliers(faction);
                if (mults.SpellCooldownReduction > 0f)
                    cd *= (1f - mults.SpellCooldownReduction);
            }
            return cd;
        }

        /// <summary>
        /// Reset all cooldowns (for new game).
        /// </summary>
        public void ResetAll()
        {
            _cooldownsByFaction.Clear();
        }
    }
}
