using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// What every faction's bank starts with, per lobby starting-resources
    /// level (GameSettings.StartingResources, 2026-10-16). The asset is
    /// EconomyBootstrap.asset, beside EconomyBootstrap.cs. No field
    /// initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Economy/EconomyBootstrap",
                     fileName = "EconomyBootstrap")]
    public sealed class EconomyBootstrapConfig : ScriptableObject, IComponentConfig
    {
        static EconomyBootstrapConfig _i;
        public static EconomyBootstrapConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<EconomyBootstrapConfig>());

        [System.Serializable]
        public struct StartLevel
        {
            public int supplies;
            public int iron;
            public int veilstone;
            public int veilsteel;
            /// <summary>Religion points in the bank at the first tick.</summary>
            public int religionPoints;
        }

        public StartLevel low;
        public StartLevel normal;
        public StartLevel high;
        public StartLevel max;

        public StartLevel For(StartingResourcesLevel level) => level switch
        {
            StartingResourcesLevel.Low => low,
            StartingResourcesLevel.High => high,
            StartingResourcesLevel.Max => max,
            _ => normal,
        };
    }
}
