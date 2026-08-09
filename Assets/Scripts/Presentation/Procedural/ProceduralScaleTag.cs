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
    }
}
