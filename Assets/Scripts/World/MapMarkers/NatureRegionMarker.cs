// NatureRegionMarker.cs
// An impassable stand of nature — forest, thicket, rock field.
//
// Design: docs/Design/Territory_And_Nature.md §3 (passability) and §8
// (authoring). The map is authored ONCE in its mint natural state; the
// per-owner look is a substitution handled by the terrain shader overlays,
// not by placing four variants of this marker.
//
// Why this exists at all: forests were supposed to block movement and stopped
// doing so. Procedural obstacle scatter was removed with procedural maps, and
// ObstacleBootstrap.ForestPositions has been empty on every hand-authored map
// since — so nothing called PassabilityGrid.BlockObstacle for vegetation, while
// the shipped localization kept telling players "Forests block sight and
// movement." NatureRegionBootstrap reads these markers and makes that true.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class NatureRegionMarker : MapMarker
    {
        /// <summary>What kind of stand this is. Purely descriptive today —
        /// every kind blocks identically (Territory_And_Nature.md §3: "a nature
        /// region is impassable in every state"). It exists so the visual pass
        /// can pick a species set without re-authoring the map.</summary>
        public enum NatureKind { Forest, Rocks, Thicket }

        [Tooltip("Descriptive only — all kinds block identically. Drives which " +
                 "vegetation set the visual pass draws from.")]
        public NatureKind Kind = NatureKind.Forest;

        [Tooltip("World-space radius of the impassable disc. Overlap several " +
                 "markers to build a non-circular stand — discs are what " +
                 "PassabilityGrid.BlockObstacle takes.")]
        [Min(1f)] public float Radius = 20f;

        protected override float GizmoRadius => Radius;

        protected override Color GizmoColor => Kind switch
        {
            NatureKind.Rocks => new Color(0.55f, 0.52f, 0.48f),
            NatureKind.Thicket => new Color(0.35f, 0.55f, 0.25f),
            _ => new Color(0.18f, 0.55f, 0.22f),
        };

        protected override string GizmoLabel => $"{Kind} — r{Radius:0}m (impassable)";
    }
}
