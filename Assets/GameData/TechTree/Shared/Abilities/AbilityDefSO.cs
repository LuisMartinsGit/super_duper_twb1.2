// AbilityDefSO.cs
// One ScriptableObject per ability — the Inspector-editable form of an
// AbilityCard, plus the ability's presentation slots (icon, VFX prefab).
// Assets live in an Abilities/<Ability>/ folder under whatever OWNS the
// ability -- the unit that casts it (Scout/Abilities/ScoutSight), or the
// building whose research grants it (RoyalStable/Abilities/WarHorn). Sect
// powers stay JSON-backed for now. They are aggregated by AbilityCatalogSO,
// which AbilityCatalog loads at runtime.
// Generated/refreshed from the code seed by
// Waning Border > Tech Tree > Generate Ability SOs.

using System;
using UnityEngine;

namespace TheWaningBorder.Abilities
{
    [CreateAssetMenu(fileName = "Ability", menuName = "Waning Border/Ability", order = 3)]
    public class AbilityDefSO : ScriptableObject
    {
        [Serializable]
        public struct EffectEntry
        {
            public AbilityEffectKind kind;
            public float value;
        }

        public string abilityName;
        public AbilityActivation activation;
        public AbilityTargeting targeting;
        public AbilityAffects affects;
        public float castTime;
        public float duration;
        public float cooldown;
        public float radius;
        public float range;
        public EffectEntry[] effects;
        public string[] aftermath;

        /// <summary>Hero level this unlocks at; 1 = always available.
        /// docs/Design/Heroes.md §2.</summary>
        public int unlocksAtLevel = 1;

        public Sprite icon;
        public GameObject vfxPrefab;

        public AbilityCard ToCard()
        {
            var fx = new AbilityEffect[effects != null ? effects.Length : 0];
            for (int i = 0; i < fx.Length; i++)
                fx[i] = new AbilityEffect(effects[i].kind, effects[i].value);
            return new AbilityCard
            {
                Name = abilityName,
                Activation = activation,
                Targeting = targeting,
                Affects = affects,
                CastTime = castTime,
                Duration = duration,
                Cooldown = cooldown,
                Radius = radius,
                Range = range,
                Effects = fx,
                Aftermath = (aftermath != null && aftermath.Length > 0) ? aftermath : null,
                UnlocksAtLevel = unlocksAtLevel < 1 ? 1 : unlocksAtLevel,
            };
        }
    }
}
