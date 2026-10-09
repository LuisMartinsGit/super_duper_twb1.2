using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Tuning numbers for <see cref="ShardrootEmpowerment"/> -- what an
    /// enshrined Shardroot gives every unit of the Temple's civilization
    /// (Curse_And_Shardroot.md § 3.1b). The asset is ShardrootEmpowerment.asset,
    /// beside ShardrootEmpowerment.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Shardroot/ShardrootEmpowerment",
                     fileName = "ShardrootEmpowerment")]
    public sealed class ShardrootEmpowermentConfig : ScriptableObject, IComponentConfig
    {
        static ShardrootEmpowermentConfig _i;
        public static ShardrootEmpowermentConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ShardrootEmpowermentConfig>());

        /// <summary>Extra damage dealt by every unit of the empowered faction,
        /// as a fraction (0.25 = +25 %).</summary>
        public float damageBonus;

        /// <summary>Share of incoming damage every unit of the empowered
        /// faction is spared (0..1; 0.2 = takes 20 % less).</summary>
        public float protection;

        /// <summary>THE PRICE OF ENSHRINING (§3.1c, 2026-10-09: "insanely
        /// expensive"), paid when the courier delivers. A faction that cannot
        /// pay cannot enshrine; its courier waits at the Temple.</summary>
        public int enshrineSupplies;
        public int enshrineIron;
        public int enshrineVeilstone;
        public int enshrineVeilsteel;

        /// <summary>The ascension countdown: sim seconds an enshrining Temple
        /// must stand for its faction to win the match. 0 = no ascension.</summary>
        public float ascensionSeconds;

        public TheWaningBorder.Core.Cost EnshrinePrice =>
            TheWaningBorder.Core.Cost.Of(enshrineSupplies, enshrineIron, enshrineVeilstone, enshrineVeilsteel);
    }
}
