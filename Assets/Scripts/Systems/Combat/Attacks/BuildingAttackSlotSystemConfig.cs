using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Tuning numbers for <see cref="BuildingAttackSlotSystem"/>. The asset is
    /// BuildingAttackSlotSystem.asset, beside BuildingAttackSlotSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Simulation data — every lockstep peer loads the same asset.
    /// docs/Design/Navigation_And_Formations.md § 4.1 Attacking a building.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/BuildingAttackSlotSystem",
                     fileName = "BuildingAttackSlotSystem")]
    public sealed class BuildingAttackSlotSystemConfig : ScriptableObject, IComponentConfig
    {
        static BuildingAttackSlotSystemConfig _i;
        /// <summary>The one instance, for the systems that read it.</summary>
        public static BuildingAttackSlotSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<BuildingAttackSlotSystemConfig>());

        /// <summary>Seconds between slot allocations. A slot freed by a death
        /// or a departure is handed on at the next allocation.</summary>
        public float reslotIntervalSeconds;

        /// <summary>Metres outside the footprint edge at which a front slot
        /// stands (inside melee reach).</summary>
        public float frontStandOff;

        /// <summary>Metres between neighbouring slots along a ring. Keep it at
        /// or above the steering separation radius, or slotted units shove
        /// each other off their slots.</summary>
        public float slotSpacing;

        /// <summary>Two claimants' radii, summed and multiplied by this, is
        /// the least distance between their slots — a wide unit (cavalry,
        /// a ram) takes its slot and keeps the neighbours free.</summary>
        public float bodyClearanceFactor;

        /// <summary>Metres from its slot point within which a unit counts as
        /// standing on it (a front unit swings, a waiting unit holds).</summary>
        public float slotArriveTolerance;

        /// <summary>Metres behind the front ring at which the waiting ring
        /// stands. Keep it above the steering separation radius so a waiting
        /// unit never pushes a front-liner.</summary>
        public float waitRingOffset;

        /// <summary>Depth in metres of the band behind the waiting ring where
        /// units with no slot on either ring stop and stand.</summary>
        public float overflowBandDepth;

        /// <summary>Step in metres for walking a slot that lands on impassable
        /// ground outward until it stands on legal ground.</summary>
        public float standableSearchStep;

        /// <summary>The furthest, in metres, a slot is walked outward before
        /// it is dropped as unusable.</summary>
        public float standableSearchMax;

        /// <summary>Upper bound on slots per ring (a very large building).</summary>
        public int maxSlotsPerRing;
    }
}
