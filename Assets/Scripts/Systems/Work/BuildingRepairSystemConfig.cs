using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Rules for <see cref="BuildingRepairSystem"/> (2026-11-17): what a
    /// repaired HP costs. Where a repairer stands and how near it must be are
    /// construction's rules (BuildingConstructionSystem.asset, 2026-11-18). The Worker's own repair
    /// speed is on its unit SO (Worker.asset repairSpeed). The asset is
    /// BuildingRepairSystem.asset, beside BuildingRepairSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Simulation data: every lockstep peer loads the same asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Work/BuildingRepairSystem",
                     fileName = "BuildingRepairSystem")]
    public sealed class BuildingRepairSystemConfig : ScriptableObject, IComponentConfig
    {
        static BuildingRepairSystemConfig _i;
        /// <summary>The one instance.</summary>
        public static BuildingRepairSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<BuildingRepairSystemConfig>());

        /// <summary>Share of the building's price that restoring ALL of its
        /// HP costs; paid as the HP goes in (Age of Empires II charges half).</summary>
        public float repairCostShare;
    }
}
