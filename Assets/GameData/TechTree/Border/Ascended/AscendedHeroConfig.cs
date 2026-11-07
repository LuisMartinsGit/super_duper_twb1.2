using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Tuning numbers for <see cref="AscendedHero"/> -- the Shardbound King's
    /// near-invincibility (Curse_And_Shardroot.md § 3.1b). The asset is
    /// AscendedHero.asset, beside AscendedHero.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Shardroot/AscendedHero",
                     fileName = "AscendedHero")]
    public sealed class AscendedHeroConfig : ScriptableObject, IComponentConfig
    {
        static AscendedHeroConfig _i;
        public static AscendedHeroConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AscendedHeroConfig>());

        /// <summary>Fraction of every incoming hit the Shardbound King
        /// actually takes (0..1). A positive hit still lands at least 1.</summary>
        public float damageTakenFraction;

        /// <summary>Hit points regenerated per second, as a fraction of his
        /// max HP.</summary>
        public float regenPerSecondOfMax;

        /// <summary>Seconds with no hit on him before he regenerates at all.</summary>
        public float outOfCombatSeconds;

        /// <summary>The most he can lose in one second, as a fraction of his
        /// max HP, after damageTakenFraction -- however many strike him, so a
        /// swarm cannot overwhelm him. 0 turns the cap off.</summary>
        public float damageCapPerSecondOfMax;

        /// <summary>The bounty paid to the faction that lands the killing
        /// blow on the Shardbound King (the "stop Sauron" moment).</summary>
        public int bountySupplies;
        public int bountyIron;
        public int bountyVeilstone;
        public int bountyVeilsteel;

        /// <summary>Shardbound Fury fires by itself when this many enemies stand
        /// inside its radius (Heroes.md §4).</summary>
        public int furyMinEnemies;
        public TheWaningBorder.Core.Cost Bounty =>
            TheWaningBorder.Core.Cost.Of(bountySupplies, bountyIron, bountyVeilstone, bountyVeilsteel);
    }
}
