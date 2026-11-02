// PresentationSpawnSystem.FootprintFit.cs
// Scales a building's visual so it fills its build-grid footprint.
// Canonical spec: docs/Design/Build_Grid.md

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public partial class PresentationSpawnSystem : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════
    // THE LADDER'S TOP LEVEL SETS THE SCALE (2026-10-30)
    //
    // Developer: "the barracks building parts that are common to all phases
    // don't get resized on level up — the last level prefab dictates
    // footprint and scale." Each level used to be fitted to the footprint on
    // its OWN bounds, so a wider L3 was drawn smaller than L1 and the walls
    // the two share shrank on the level-up. Now everything a ladder shows —
    // every level, and the ghost, the plan and the rising site, which show a
    // level — takes the SCALE AND THE PIVOT OFFSET measured on the TOP
    // level's prefab: one scale and one placement for the whole ladder, so
    // a piece authored at the same local coordinates in every level stands
    // at exactly the same spot through every level-up. This requires the
    // levels to be authored in ONE frame (the Garrison's are, since
    // 2026-10-30: a corner at the origin, x in [-15, 0], floor at y = 0);
    // a level authored elsewhere in its root's space would be displaced by
    // the difference. A building with no ladder (an Age 0 form, a ladderless
    // culture look) is measured on the model it shows, as before.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Fit and offset measured per reference prefab and footprint, once.</summary>
    private static readonly Dictionary<(int prefab, float w, float h), (float fit, Vector3 offset)> _ladderFit = new();

    /// <summary>
    /// The prefab whose bounds set the fit for everything <paramref name="ladderId"/>
    /// shows in <paramref name="culture"/>: its highest level's, or
    /// <paramref name="shown"/> itself when the building has no ladder there.
    /// </summary>
    public static GameObject ResolveFitReference(string ladderId, byte culture, GameObject shown)
    {
        if (culture == Cultures.None || string.IsNullOrEmpty(ladderId)) return shown;
        // A level that carries an authored footprint marker (FootprintBounds,
        // the "Footprint" plinth group) is authored in the set's frame and
        // outranks one that does not, highest such level first — so while a
        // ladder is being re-authored level by level (the House, 2026-11-02:
        // L1 redone on the 10 m plinth, L2-L3 still the old 4 m art) the new
        // levels are not scaled by the old top level's bounds. With no marker
        // anywhere the highest level with a prefab rules, as before.
        GameObject top = null;
        for (int l = 3; l >= 1; l--)
        {
            if (!TechCatalog.TryGetBuildingLevel(culture, ladderId, l, out var lvl) || lvl == null || lvl.prefab == null) continue;
            if (lvl.prefab.GetComponentInChildren<TheWaningBorder.Rendering.FootprintBounds>(true) != null) return lvl.prefab;
            if (top == null) top = lvl.prefab;
        }
        return top != null ? top : shown;
    }

    /// <summary>
    /// The root scale a building view takes — the ladder's reference prefab
    /// (<see cref="ResolveFitReference"/>): its authored root scale times its
    /// footprint fit — and the pivot offset that stands the model on the
    /// entity (ProceduralScaleTag.BaseOffset), both measured on that
    /// reference. <paramref name="instance"/> is the live view, measured
    /// directly when it IS the reference (a building with no ladder).
    /// </summary>
    public static float ComputeBuildingViewScale(GameObject instance, GameObject shownPrefab, string ladderId, byte culture,
        float width, float height, out float authoredScale, out Vector3 centreOffset)
    {
        var reference = ResolveFitReference(ladderId, culture, shownPrefab);
        if (reference == null || reference == shownPrefab)
        {
            authoredScale = AuthoredScaleOf(shownPrefab);
            return authoredScale * ComputeFootprintFitScoped(instance, null, width, height, out centreOffset);
        }

        authoredScale = AuthoredScaleOf(reference);
        var key = (reference.GetInstanceID(), width, height);
        if (!_ladderFit.TryGetValue(key, out var fit))
        {
            // Measured once, on a throwaway instance well out of view.
            var probe = Instantiate(reference, new Vector3(0f, -4096f, 0f), Quaternion.identity);
            probe.SetActive(true);
            fit.fit = ComputeFootprintFitScoped(probe, null, width, height, out fit.offset);
            DestroyImmediate(probe);
            _ladderFit[key] = fit;
        }
        centreOffset = fit.offset;
        return authoredScale * fit.fit;
    }

    /// <summary>
    /// <see cref="ComputeBuildingViewScale(GameObject, GameObject, string, byte, float, float, out float, out Vector3)"/>
    /// for an entity's view: its ladder (a plan's id, else the upgrade ladder
    /// of its tags) and its faction's completed culture, fitted to its
    /// BuildingSize. Just the authored scale, no offset, for an entity with
    /// no footprint.
    /// </summary>
    public static float ComputeBuildingViewScale(EntityManager em, Entity entity, GameObject instance, GameObject shownPrefab,
        out float authoredScale, out Vector3 centreOffset)
    {
        authoredScale = AuthoredScaleOf(shownPrefab);
        centreOffset = Vector3.zero;
        if (instance == null || entity == Entity.Null || !em.Exists(entity)) return authoredScale;
        if (!em.HasComponent<BuildingSize>(entity)) return authoredScale;

        string ladder = em.HasComponent<PlannedBuilding>(entity)
            ? em.GetComponentData<PlannedBuilding>(entity).BuildingId.ToString()
            : TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, entity);
        byte culture = em.HasComponent<FactionTag>(entity)
            ? CultureConfig.GetCompletedCulture(em, em.GetComponentData<FactionTag>(entity).Value)
            : Cultures.None;
        var size = em.GetComponentData<BuildingSize>(entity);
        return ComputeBuildingViewScale(instance, shownPrefab, ladder, culture, size.Width, size.Height,
                                        out authoredScale, out centreOffset);
    }

    /// <summary>A prefab's authored root scale (the mean of its three axes).</summary>
    public static float AuthoredScaleOf(GameObject prefab)
    {
        if (prefab == null) return 1f;
        var ps = prefab.transform.localScale;
        return (ps.x + ps.y + ps.z) / 3f;
    }

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
    /// (Vault of Almierra, Border LargeNode, the wall set) construct
    /// their geometry at explicit sizes and must not be second-guessed.
    /// </summary>
    private float ComputeFootprintFit(GameObject goInst, Entity entity)
        => ComputeFootprintFit(goInst, entity, out _);

    private float ComputeFootprintFit(GameObject goInst, Entity entity, out Vector3 centreOffset)
    {
        centreOffset = Vector3.zero;
        if (goInst == null || entity == Entity.Null) return 1f;
        if (!IsBuildingLike(entity)) return 1f;
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
    /// <see cref="TheWaningBorder.Rendering.ProceduralScaleTag.BaseOffset"/>
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

        var tag = viewRoot.GetComponent<TheWaningBorder.Rendering.ProceduralScaleTag>();
        if (tag == null)
            tag = viewRoot.AddComponent<TheWaningBorder.Rendering.ProceduralScaleTag>();
        if (tag.AuthoredScale <= 0.001f) tag.AuthoredScale = 1f;

        GameObject scope = null;
        var variant = viewRoot.GetComponent<TheWaningBorder.Rendering.BuildingVariantVisual>();
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

        // An authored footprint marker (the plinth group) outranks the whole
        // model: parts that overhang the slab must not shrink or shift it.
        var marker = (renderScope != null ? renderScope : goInst)
            .GetComponentInChildren<TheWaningBorder.Rendering.FootprintBounds>(includeInactive: false);
        if (marker != null) renderScope = marker.gameObject;

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
        // Y is the pivot -> LOWEST-POINT delta (2026-09-30): every building's
        // model stands on the ground, whatever its export pivot. The new house
        // FBX came out of Maya with its root 1.2 m above the floor and spawned
        // with the plinth and yard wall buried; a model authored with its base
        // at the pivot measures 0 here and does not move.
        if (any)
        {
            Vector3 c = combined.center - root.position;
            centreOffset = new Vector3(c.x, combined.min.y - root.position.y, c.z);
        }

        root.localScale = keepScale;
        root.rotation = keepRot;

        if (!any) return false;

        boundsX = combined.size.x;
        boundsZ = combined.size.z;
        return boundsX > 0.001f && boundsZ > 0.001f;
    }
}
