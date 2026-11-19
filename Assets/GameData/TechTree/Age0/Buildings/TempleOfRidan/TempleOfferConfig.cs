using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// What the Temple of Ridan OFFERS, by age (<see cref="TempleOffer"/>,
    /// docs/Design/Age_0.md § Temple of Ridan, 2026-11-18). The asset is
    /// TempleOffer.asset, beside TempleOffer.cs.
    ///
    /// The Litharch is the Temple's <c>trains[]</c> and is not listed here.
    /// A Temple tech left out of both lists keeps its SO, its effects and its
    /// code, but nobody can research it at the Temple: the panel does not
    /// draw it, the AI sweep skips it and the research command refuses it.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Buildings/TempleOffer",
                     fileName = "TempleOffer")]
    public sealed class TempleOfferConfig : ScriptableObject, IComponentConfig
    {
        static TempleOfferConfig _i;
        public static TempleOfferConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<TempleOfferConfig>());

        /// <summary>Temple tech ids offered while the faction is in Age 0.</summary>
        public string[] age0Research;

        /// <summary>Temple tech ids offered once the faction has aged up.</summary>
        public string[] research;

        /// <summary>Adopted sects' units (those without a sect building of
        /// their own) train at the Temple once aged up.</summary>
        public bool sectUnits;

        /// <summary>The Petriarchy's Support (resources for a Religion Point)
        /// and Army purchases are sold at the Temple once aged up
        /// (Monuments_And_Petriarchy.md §5-§6).</summary>
        public bool petriarchy;
    }
}
