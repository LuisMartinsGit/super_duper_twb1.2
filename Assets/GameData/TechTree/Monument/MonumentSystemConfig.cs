using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Tunables for monuments (docs/Design/Monuments_And_Petriarchy.md). The asset
    /// is MonumentSystem.asset, beside MonumentSystem.cs. No field initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Monument System",
                     fileName = "MonumentSystem")]
    public sealed class MonumentSystemConfig : ScriptableObject, IComponentConfig
    {
        static MonumentSystemConfig _i;
        public static MonumentSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<MonumentSystemConfig>());

        /// <summary>The ring's radius, in metres.</summary>
        public float captureRadius;
        /// <summary>Seconds a unit must stay inside the ring to take the monument.</summary>
        public int captureSeconds;
        /// <summary>A held monument pays one religion point every this-many seconds.</summary>
        public float secondsPerPoint;

        public int exploreSupplies;
        public int exploreIron;
        public int exploreVeilstone;
        /// <summary>The pool of six: TechDefSO ids, granted rather than researched.</summary>
        public string[] buffTechIds;

        /// <summary>Metres in front of the monument the Shardroot lands.</summary>
        public float shardrootOffset;

        public float outcropClearance;
        public int placementRings;
        public float placementRingStep;
        public float selectionRadius;

        public GameObject statuePrefab;
        public float statueScale;
        public float lightHeight;
        public float lightRange;
        public float lightIntensity;
        /// <summary>Ring and light before anyone holds it.</summary>
        public Color neutralColor;
        public float ringWidth;
        public float ringLift;
        public int ringSegments;
        public float ringGlow;
        public float arcWidthScale;
        public float arcGlowScale;
    }
}
