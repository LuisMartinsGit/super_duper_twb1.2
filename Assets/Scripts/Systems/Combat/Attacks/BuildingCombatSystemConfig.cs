using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Tuning numbers for <see cref="BuildingCombatSystem"/>. The asset is
    /// BuildingCombatSystem.asset, beside BuildingCombatSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Simulation data — every lockstep peer loads the same asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/BuildingCombatSystem",
                     fileName = "BuildingCombatSystem")]
    public sealed class BuildingCombatSystemConfig : ScriptableObject, IComponentConfig
    {
        static BuildingCombatSystemConfig _i;
        /// <summary>The one instance, for the ISystem that reads it.</summary>
        public static BuildingCombatSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<BuildingCombatSystemConfig>());

        /// <summary>Seconds of sim time a ready tower with nothing in range
        /// waits before scanning again. Without it an idle tower re-scanned
        /// every target in the world on every tick.</summary>
        public float noTargetRetryDelay;
    }
}
