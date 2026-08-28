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

        [Tooltip("Stable ability name — IndexOf lookups and Aftermath chains key off this.")]
        public string abilityName;
        public AbilityActivation activation;
        public AbilityTargeting targeting;
        public AbilityAffects affects;
        [Tooltip("Seconds before effects apply (0 = instant).")]
        public float castTime;
        [Tooltip("Seconds the effect lasts (-1 = permanent / always-on passive).")]
        public float duration;
        [Tooltip("Seconds before recast (0 = auto: castTime + duration + 1).")]
        public float cooldown;
        [Tooltip("World units (Aura/Area).")]
        public float radius;
        [Tooltip("Cast range (SingleTarget/Area; 0 = centred on self / unlimited).")]
        public float range;
        public EffectEntry[] effects;
        [Tooltip("Ability names auto-cast when this one ends.")]
        public string[] aftermath;

        [Header("Presentation (authoring slots)")]
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
            };
        }
    }
}
