using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Tuning numbers for <see cref="ShardboundKing"/> -- the Shardbound King's
    /// near-invincibility (Curse_And_Shardroot.md § 3.1b). The asset is
    /// ShardboundKing.asset, beside ShardboundKing.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Shardroot/ShardboundKing",
                     fileName = "ShardboundKing")]
    public sealed class ShardboundKingConfig : ScriptableObject, IComponentConfig
    {
        static ShardboundKingConfig _i;
        public static ShardboundKingConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<ShardboundKingConfig>());

        /// <summary>Fraction of every incoming hit the Shardbound King
        /// actually takes (0..1). A positive hit still lands at least 1.</summary>
        public float damageTakenFraction;

        /// <summary>Hit points regenerated per second, as a fraction of his
        /// max HP.</summary>
        public float regenPerSecondOfMax;

        /// <summary>The most he can lose in one second, as a fraction of his
        /// max HP, after damageTakenFraction -- however many strike him, so a
        /// swarm cannot overwhelm him. 0 turns the cap off.</summary>
        public float damageCapPerSecondOfMax;

        /// <summary>THE PRICE OF BINDING THE KING (§3.1c, 2026-10-09:
        /// "insanely expensive"), paid when the courier reaches the Hall. A
        /// faction that cannot pay cannot bind; its courier waits there.</summary>
        public int bindSupplies;
        public int bindIron;
        public int bindVeilstone;
        public int bindVeilsteel;

        /// <summary>The bounty paid to the faction that lands the killing
        /// blow on the Shardbound King (the "stop Sauron" moment).</summary>
        public int bountySupplies;
        public int bountyIron;
        public int bountyVeilstone;
        public int bountyVeilsteel;

        public TheWaningBorder.Core.Cost BindPrice =>
            TheWaningBorder.Core.Cost.Of(bindSupplies, bindIron, bindVeilstone, bindVeilsteel);
        public TheWaningBorder.Core.Cost Bounty =>
            TheWaningBorder.Core.Cost.Of(bountySupplies, bountyIron, bountyVeilstone, bountyVeilsteel);
    }
}
