using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Sect
{
    /// <summary>
    /// Numbers for <see cref="ReliquaryHelper"/>, the Reliquary's three
    /// building actives. The asset is ReliquaryHelper.asset, beside the class.
    /// On the spell ladder (docs/Design/Spells.md 8-9): every cooldown is the
    /// building-active band, every radius a canon radius.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Sect/ReliquaryHelper",
                     fileName = "ReliquaryHelper")]
    public sealed class ReliquaryHelperConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Scry (reveal a ground point) cooldown, seconds.</summary>
        public float scryCooldown;
        /// <summary>Scry reveal radius (m).</summary>
        public float scryRadius;
        /// <summary>Scry reveal duration (s).</summary>
        public float scryDuration;

        /// <summary>Lockout (freeze enemy cooldown recovery) cooldown, seconds.</summary>
        public float lockoutCooldown;
        /// <summary>Lockout radius (m).</summary>
        public float lockoutRadius;
        /// <summary>Lockout duration (s).</summary>
        public float lockoutDuration;

        /// <summary>Vision (reveal around the Reliquary) cooldown, seconds.</summary>
        public float visionCooldown;
        /// <summary>Vision reveal radius (m).</summary>
        public float visionRadius;
        /// <summary>Vision reveal duration (s).</summary>
        public float visionDuration;

        /// <summary>Cooldown multiplier once the Reliquary's building lever is
        /// at level III (0.7 = the "-30% base cooldowns" of the spec).</summary>
        public float level3CooldownScale;
    }
}
