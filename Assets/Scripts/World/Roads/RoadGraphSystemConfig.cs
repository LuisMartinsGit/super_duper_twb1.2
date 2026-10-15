using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World.Roads
{
    /// <summary>
    /// The tunables of the simulation road graph (docs/Design/Roads.md §0).
    /// The asset is RoadGraphSystem.asset beside <see cref="RoadGraphSystem"/>.
    /// No field initialisers: the values live in the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/World/RoadGraphSystem",
                     fileName = "RoadGraphSystem")]
    public sealed class RoadGraphSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Two routes leaving a node closer than this angle share a fork.</summary>
        public float forkAngleDegrees;
        /// <summary>The fork sits this fraction of the shorter route from the node.</summary>
        public float forkDistanceFraction;
        /// <summary>Junctions closer than this merge into one crossroads.</summary>
        public float minJunctionSpacing;
        /// <summary>A junction stays at least this far from every site.</summary>
        public float minJunctionClearance;
        /// <summary>The most roads meeting at one intersection.</summary>
        public int maxJunctionDegree;
        /// <summary>Sideways bend of a road, as a fraction of its length.</summary>
        public float winding;
        /// <summary>A walker within this distance of a waypoint moves on to the next.</summary>
        public float reachDistance;
        /// <summary>Extra tolerance on a waypoint the walker only passes through
        /// (bend, junction, site on the way): it turns early, the path stays fluid.</summary>
        public float passRadius;
        /// <summary>Clearance around a resource node: the walker aims this far from its centre.</summary>
        public float nodeRadius;
    }
}
