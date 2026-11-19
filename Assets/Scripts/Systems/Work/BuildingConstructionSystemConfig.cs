using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Where builders stand and how near they must be to work (2026-11-18),
    /// shared by <see cref="BuildingConstructionSystem"/>,
    /// <see cref="BuildCommandSystem"/>, <see cref="PlannedBuildingSystem"/>
    /// and <see cref="BuildingRepairSystem"/> through <see cref="BuilderStand"/>.
    /// The asset is BuildingConstructionSystem.asset, beside
    /// BuildingConstructionSystem.cs. docs/Design/Age_0.md § Worker.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Simulation data: every lockstep peer loads the same asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Work/BuildingConstructionSystem",
                     fileName = "BuildingConstructionSystem")]
    public sealed class BuildingConstructionSystemConfig : ScriptableObject, IComponentConfig
    {
        static BuildingConstructionSystemConfig _i;
        /// <summary>The one instance.</summary>
        public static BuildingConstructionSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<BuildingConstructionSystemConfig>());

        /// <summary>Metres from the footprint's edge at which a worker walking
        /// to a Build order takes the job over (the plan breaks ground, the
        /// command becomes a build order). Not a working distance: the worker
        /// then walks on to its own spot.</summary>
        public float handoverRange;

        /// <summary>Metres between the worker's body (its Radius) and the
        /// footprint's edge at the spot it walks to.</summary>
        public float standGap;

        /// <summary>A worker builds or repairs only while its centre is no
        /// further than its Radius plus this from the footprint's edge.</summary>
        public float reachBeyondRadius;

        /// <summary>How far a worker's body may overlap the footprint and still
        /// work. Closer in than its Radius minus this - or inside - it first
        /// walks out to its spot.</summary>
        public float bodyOverlapTolerance;

        /// <summary>Metres between neighbouring spots round a footprint, so
        /// builders on one site do not share or shove over one spot.</summary>
        public float slotSpacing;

        /// <summary>A worker within <see cref="stallRadius"/> of its spot that
        /// gets no closer for this many seconds gives the spot up and takes the
        /// next free one (it cannot reach it, or something stands on it).</summary>
        public float stallSeconds;

        /// <summary>Metres from the spot inside which a stall counts. Further
        /// out the path may honestly lead away for a while.</summary>
        public float stallRadius;

        /// <summary>Metres closer to the spot that count as progress.</summary>
        public float stallMinProgress;
    }
}
