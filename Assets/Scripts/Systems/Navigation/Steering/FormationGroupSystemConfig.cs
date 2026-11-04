using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Tuning numbers for <see cref="FormationGroupSystem"/>. The asset is
    /// FormationGroupSystem.asset, beside FormationGroupSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Navigation/FormationGroupSystem",
                     fileName = "FormationGroupSystem")]
    public sealed class FormationGroupSystemConfig : ScriptableObject, IComponentConfig
    {
        static FormationGroupSystemConfig _i;
        /// <summary>The one instance, for the unmanaged system that reads it.</summary>
        public static FormationGroupSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<FormationGroupSystemConfig>());

        /// <summary>
        /// Seconds a group lives on after its leader ARRIVES, for its rear
        /// ranks to settle into their slots. Each member detaches as soon as
        /// it is on its slot; whoever is still walking when this runs out is
        /// released to finish on its own. (docs/Design/Navigation_And_Formations.md §2.9)
        /// </summary>
        public float settleTimeoutSeconds;

        /// <summary>
        /// Metres the worst member->spot offset must shrink by to count as the
        /// group CLOSING UP, which resets the tether fuse. Measured on the same
        /// any-direction offset that makes the leader ease (§2.10).
        /// </summary>
        public float tetherProgressEpsilon;

        /// <summary>
        /// Fraction of the roster that may be out of rank fighting before the
        /// leader HOLDS for them. Strictly more than this fraction engaged
        /// stops the leader and pauses the fuse and stall counters; the
        /// settle timer after arrival is frozen while any member is engaged.
        /// </summary>
        public float engagedHoldFraction;

        /// <summary>
        /// Heading error (radians) above which a BLOCKED leader pivots in place
        /// toward the flow instead of counting the tick as a stall. Below it
        /// the leader steps along the flow direction, and only if that is
        /// blocked too does the stall count.
        /// </summary>
        public float stallHeadingToleranceRadians;

        /// <summary>
        /// Two soldiers within this many metres of each other when an order
        /// goes out march in the same formation (2026-11-04: soldiers going to
        /// the same place always travel in formation; a spread-out order splits
        /// into one formation per cluster).
        /// </summary>
        public float splitLinkRadius;

        /// <summary>A cluster of one or two within this many metres of a bigger
        /// one joins it instead of marching apart.</summary>
        public float splitMergeRadius;

        /// <summary>Most formations one order splits into; the smallest merge
        /// into their nearest neighbour past this.</summary>
        public int splitMaxFormations;

        /// <summary>An AI re-order to within this many metres of where a marching
        /// formation already goes changes nothing: the formation keeps marching.</summary>
        public float repeatOrderKeepRadius;
    }
}
