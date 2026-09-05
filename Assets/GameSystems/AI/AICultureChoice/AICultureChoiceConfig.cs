using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AICultureChoice"/>. The asset is AICultureChoice.asset,
    /// beside AICultureChoice.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AICultureChoice",
                     fileName = "AICultureChoice")]
    public sealed class AICultureChoiceConfig : ScriptableObject, IComponentConfig
    {
        static AICultureChoiceConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AICultureChoiceConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AICultureChoiceConfig>());

        /// <summary>Enemy military strength that reads as "they are the
        /// stronger army". Scored toward Alanthor — fortify and survive.</summary>
        public float enemyStrengthPivot;

        public float enemyStrengthWeight;

        /// <summary>Each scouted enemy Hall beyond the first is another
        /// economy to raid. Scored toward Feraldis.</summary>
        public float enemyBaseWeight;

        /// <summary>Scouted enemy ECONOMY (miners, eco buildings) with little
        /// military cover is the classic raid invitation.</summary>
        public float exposedEconomyWeight;

        /// <summary>Our own combat record. Winning fights says "keep
        /// fighting" (Feraldis); losing armies says "turtle" (Alanthor).</summary>
        public float successWeight;

        public float lossWeight;

        /// <summary>Being poor pushes toward the culture that STEALS its
        /// income rather than the one that gathers harder.</summary>
        public float povertyWeight;

        public int povertySuppliesFloor;

        /// <summary>Jitter amplitude, so identical situations still vary.</summary>
        public float seedJitter;
    }
}
