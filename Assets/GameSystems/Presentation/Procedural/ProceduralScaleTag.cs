// ProceduralScaleTag.cs
// Part of: Presentation/
//
// Stores a spawned visual's authored base scale so PresentationSpawnSystem.SyncTransforms
// can multiply it by the ECS LocalTransform.Scale each frame. Originally lived inside the
// (now-deleted) ProceduralUnitGenerator; kept under the same name/namespace because it is
// also used by the prefab/primitive spawn path and several presentation systems.

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Attached to a spawned visual GameObject to store its base scale.
    /// PresentationSpawnSystem.SyncTransforms multiplies this by ECS LocalTransform.Scale.
    /// </summary>
    public class ProceduralScaleTag : MonoBehaviour
    {
        public float BaseScale = 1f;

        /// <summary>
        /// The prefab's authored root scale WITHOUT the footprint fit —
        /// BaseScale = AuthoredScale x fit. Stored separately so the fit can
        /// be re-derived when an in-place variant switch changes which art is
        /// visible (PresentationSpawnSystem.RefitVariantView).
        /// </summary>
        public float AuthoredScale = 1f;

        /// <summary>
        /// Pivot-to-bounds-centre offset in root-local units (XZ only),
        /// measured at scale 1 / rotation identity by the footprint fit.
        /// SyncTransforms subtracts it (rotated, scaled) from the entity
        /// position so the SCALED mesh centre — not the prefab's pivot —
        /// lands on the entity. Zero for units and procedural builders.
        /// </summary>
        public Vector3 BaseOffset = Vector3.zero;
    }
}
