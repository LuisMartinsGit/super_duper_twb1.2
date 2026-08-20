// PresentationSpawnSystem.FootprintFit.cs
// Scales a building's visual so it fills its build-grid footprint.
// Canonical spec: docs/Design/Build_Grid.md
// Location: Assets/GameData/TechTree/Presentation/Spawn/PresentationSpawnSystem.FootprintFit.cs

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public partial class PresentationSpawnSystem : MonoBehaviour
{
    /// <summary>
    /// Clamp range for the auto-fit factor. Art whose bounds are wildly off
    /// (an unbaked pivot, a stray far-away child renderer, an FX volume) would
    /// otherwise collapse or explode the building. Outside this band we assume
    /// the measurement is wrong, not the art, and leave the prefab alone.
    /// </summary>
    private const float MinFootprintFit = 0.25f;
    private const float MaxFootprintFit = 4.0f;

    /// <summary>
    /// Uniform scale that makes <paramref name="goInst"/> sit snugly inside the
    /// entity's <c>BuildingSize</c> footprint — the mesh touches its cell
    /// bounds on the tighter axis and never overhangs on either.
    ///
    /// Returns 1 (no change) for anything that is not a sized building, and
    /// for degenerate or out-of-band measurements. Fit is UNIFORM on purpose:
    /// scaling X and Z independently would shear the art on non-square
    /// footprints.
    ///
    /// Only the prefab-instantiation paths call this. Procedural builders
    /// (Smelter, Vault of Almierra, Border LargeNode, the wall set) construct
    /// their geometry at explicit sizes and must not be second-guessed.
    /// </summary>
    private float ComputeFootprintFit(GameObject goInst, Entity entity)
        => ComputeFootprintFit(goInst, entity, out _);

    private float ComputeFootprintFit(GameObject goInst, Entity entity, out Vector3 centreOffset)
    {
        centreOffset = Vector3.zero;
        if (goInst == null || entity == Entity.Null) return 1f;
        if (!_em.HasComponent<BuildingTag>(entity)) return 1f;
        if (!_em.HasComponent<BuildingSize>(entity)) return 1f;

        var size = _em.GetComponentData<BuildingSize>(entity);
        return ComputeFootprintFitForSize(goInst, size.Width, size.Height, out centreOffset);
    }

    /// <summary>
    /// Footprint fit from an explicit size in metres — the placement preview
    /// uses this with the same measurement the real spawn gets, so the ghost
    /// and the finished building render at the same scale.
    /// </summary>
    public static float ComputeFootprintFitForSize(GameObject goInst, float width, float height)
        => ComputeFootprintFitForSize(goInst, width, height, out _);

    /// <summary>
    /// As above, and also reports the art's pivot-to-bounds-centre offset in
    /// root-local units (XZ, measured at scale 1 / yaw 0). Stored on
    /// <see cref="TheWaningBorder.Presentation.ProceduralScaleTag.BaseOffset"/>
    /// so the view can be re-centred on the entity: the fit SCALES the root,
    /// and any pivot offset scales with it — which is how the footprint
    /// doubling turned slight pivot leans into visibly off-centre buildings.
    /// </summary>
    public static float ComputeFootprintFitForSize(GameObject goInst, float width, float height,
                                                   out Vector3 centreOffset)
        => ComputeFootprintFitScoped(goInst, null, width, height, out centreOffset);

    /// <summary>
    /// Re-measure the fit after an IN-PLACE variant switch
    /// (BuildingVariantVisual). The fit stored at spawn was measured against
    /// the Lv0 construction model; culture branches are different art at
    /// different authored sizes, so keeping the Lv0 fit re-creates the very
    /// scale bug the fit exists to prevent. Measures only the shown branch.
    /// </summary>
    public static void RefitVariantView(GameObject viewRoot, Entity entity, EntityManager em)
    {
        if (viewRoot == null || entity == Entity.Null || !em.Exists(entity)) return;
        if (!em.HasComponent<BuildingTag>(entity)) return;
        if (!em.HasComponent<BuildingSize>(entity)) return;

        var tag = viewRoot.GetComponent<TheWaningBorder.Presentation.ProceduralScaleTag>();
        if (tag == null)
            tag = viewRoot.AddComponent<TheWaningBorder.Presentation.ProceduralScaleTag>();
        if (tag.AuthoredScale <= 0.001f) tag.AuthoredScale = 1f;

        GameObject scope = null;
        var variant = viewRoot.GetComponent<TheWaningBorder.Presentation.BuildingVariantVisual>();
        if (variant != null && variant.ShownBranch != null)
            scope = variant.ShownBranch.gameObject;

        var size = em.GetComponentData<BuildingSize>(entity);
        float fit = ComputeFootprintFitScoped(viewRoot, scope, size.Width, size.Height,
                                              out Vector3 offset);
        tag.BaseScale = tag.AuthoredScale * fit;
        tag.BaseOffset = offset;
    }

    /// <summary>
    /// Core fit: bounds of <paramref name="renderScope"/> (the whole instance
    /// when null) measured with the ROOT at scale 1 / yaw 0, fitted inside
    /// width x height.
    /// </summary>
    private static float ComputeFootprintFitScoped(GameObject goInst, GameObject renderScope,
                                                   float width, float height,
                                                   out Vector3 centreOffset)
    {
        centreOffset = Vector3.zero;
        if (goInst == null || width <= 0f || height <= 0f) return 1f;

        if (!TryMeasureUnscaledFootprint(goInst, renderScope,
                                         out float boundsX, out float boundsZ,
                                         out centreOffset))
            return 1f;

        // Fit INSIDE the footprint: the smaller ratio wins, so the larger of
        // the two mesh axes is the one that lands on its cell edge.
        float fit = math.min(width / boundsX, height / boundsZ);

        // Out-of-band means the measurement is suspect (see the clamp
        // remarks) — distrust the offset from the same measurement.
        if (!(fit > 0f) || float.IsInfinity(fit)) { centreOffset = Vector3.zero; return 1f; }
        if (fit < MinFootprintFit || fit > MaxFootprintFit) { centreOffset = Vector3.zero; return 1f; }
        return fit;
    }

    /// <summary>
    /// Combined XZ renderer extent of the instance as if its root were at
    /// scale 1 and yaw 0. Measuring under the live transform would fold the
    /// authored scale and the rotation into the answer and make the fit
    /// depend on which way the building happens to face.
    /// </summary>
    private static bool TryMeasureUnscaledFootprint(GameObject goInst, GameObject renderScope,
                                                    out float boundsX, out float boundsZ,
                                                    out Vector3 centreOffset)
    {
        boundsX = boundsZ = 0f;
        centreOffset = Vector3.zero;

        var renderers = (renderScope != null ? renderScope : goInst)
            .GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers == null || renderers.Length == 0) return false;

        var root = goInst.transform;
        Vector3 keepScale = root.localScale;
        Quaternion keepRot = root.rotation;

        root.localScale = Vector3.one;
        root.rotation = Quaternion.identity;

        bool any = false;
        Bounds combined = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            // Particle / trail renderers have unstable, often huge bounds and
            // are decoration rather than body — they must not drive the fit.
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                continue;

            if (!any) { combined = r.bounds; any = true; }
            else combined.Encapsulate(r.bounds);
        }

        // Pivot -> bounds-centre delta, read while the root sits at scale 1 /
        // rotation identity, so the world-space delta IS the root-local one.
        // XZ only: vertical anchoring belongs to the terrain snap.
        if (any)
        {
            Vector3 c = combined.center - root.position;
            centreOffset = new Vector3(c.x, 0f, c.z);
        }

        root.localScale = keepScale;
        root.rotation = keepRot;

        if (!any) return false;

        boundsX = combined.size.x;
        boundsZ = combined.size.z;
        return boundsX > 0.001f && boundsZ > 0.001f;
    }
}
