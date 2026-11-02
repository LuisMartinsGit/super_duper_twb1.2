using UnityEngine;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Marks the part of a building prefab that IS its footprint — the base
    /// slab the model stands on. The footprint fit
    /// (<see cref="PresentationSpawnSystem.ComputeFootprintFitForSize(GameObject, float, float, out Vector3)"/>)
    /// measures only the renderers under this object, so a tower, a flag pole
    /// or a target that pokes past the slab no longer shrinks the whole
    /// building and shifts it off its cells (2026-11-01: the Archery Range's
    /// west towers overhang its 20 x 20 m plinth by 1.9 m, so the Range drew
    /// 15 % smaller than the Garrison standing on the same plinth).
    /// <para>
    /// Convention: a child GameObject named "Footprint" carrying this marker,
    /// holding the plinth pieces (the Synty castle floor stones). Without one
    /// the whole model is measured, as before.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootprintBounds : MonoBehaviour
    {
    }
}
