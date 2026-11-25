// UnitWorldScale.cs
// HOW LARGE A UNIT IS DRAWN (2026-11-13).
//
// Developer: "units look too large" beside buildings whose prefab roots are
// scaled to their footprints - "resize all units by the same factor ... also
// rescale the selection rings, the spaces between members of the same
// formation, the camera zoom and the unit speed."
//
// One factor, on UnitWorldScale.asset, multiplies the view of EVERY unit
// (PresentationSpawnSystem's transform sync - whatever built the view: an SO
// prefab, a presentation-id prefab or a procedural rig) and the heights of
// what is drawn over a unit (health bar, damage numbers, shield bar, heal
// cross). Unit PREFABS stay at their authored size.
//
// What is NOT read from here, because it is game data or lives elsewhere:
//   speed, collision radius   on each unit SO (the selection ring follows the radius)
//   formation spacing         FormationMoveCommandHelper.Spacing, FormationInput.asset
//   crowd separation          SteeringSystem.SeparationRadius / UnitAvoidanceRadius,
//                             UnitIntegratorSystem.StopDistance / SeparationClearance
//   wall clearance            SteeringSystem.WallClearanceRadius (and the shorter
//                             FormationWallClearanceRadius for formation members)
//   formation tolerances      FormationGroup.ArriveDistance / CatchUpTriggerDistance /
//                             LeaderTetherDistance, FormationGroupSystem.asset tetherProgressEpsilon,
//                             FormationGroupSystem.InPlaceDistance / OutOfFormationLeaderSpeed,
//                             UnitIntegratorSystem.FormationStopDistance / ArriveSlowRadius /
//                             DefaultMoveSpeed, FormationMoveCommandHelper.PoseFitTolerance,
//                             StuckRedirectSystem.MinProgress, IdleFormUpSystem.DriftTolerance
//   camera distances          CameraController.asset
//   walk-cycle playback       UnitAnimationSync.asset (moveClipReferenceSpeed)
// Change the factor and those have to move with it.

using Unity.Entities;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    public static class UnitWorldScale
    {
        private static UnitWorldScaleConfig _cfg;
        private static UnitWorldScaleConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<UnitWorldScaleConfig>());

        /// <summary>The factor every unit view is drawn at.</summary>
        public static float Value => Cfg != null && Cfg.unitScale > 0.001f ? Cfg.unitScale : 1f;

        /// <summary>How much larger an elite is drawn: heroes and sect units
        /// (developer, 2026-11-25: "15~20% larger than the rest").</summary>
        public static float EliteValue => Cfg != null && Cfg.eliteScale > 0.001f ? Cfg.eliteScale : 1f;

        /// <summary>The factor this unit's view is drawn at.</summary>
        public static float For(EntityManager em, Entity unit)
        {
            if (em.HasComponent<HeroLevel>(unit)) return Value * EliteValue;
            if (em.HasComponent<PresentationId>(unit)
                && TechCatalog.TryGetUnitId(em.GetComponentData<PresentationId>(unit).Id, out var id)
                && id.StartsWith("Sect_", System.StringComparison.Ordinal)) return Value * EliteValue;
            return Value;
        }
    }
}
