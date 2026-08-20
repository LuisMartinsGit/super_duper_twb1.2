// PresentationSpawnSystem.VeilstoneOutcropping.cs
// Veilstone outcropping (mineable crystal crop) visual: gem-cluster wrapper
// prefab cache + loot-pile spawn + selection/animator tail. The prefab cache
// is also read by the Large-node well and veilsteel-deposit visuals.
// Co-located with the resource per the TechTree convention.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Presentation;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Entities;

public partial class PresentationSpawnSystem
{
    // ─── Shatter Stone wrapper prefabs (P_VeilstoneOutcropping_GemA/B/C) ──────────────
    // Loaded lazily on first outcropping spawn. Each is a copy of the matching
    // NV3D P_Gem4_* with Rigidbody/SphereCollider stripped and the OreNode
    // component swapped to VeilstoneOutcroppingOreNode. Variant choice is keyed on
    // entity.Index so the same visual is picked on every networked client
    // (entity creation order is deterministic under lockstep). Index is not
    // stable across editor session restarts — if a save/load system is
    // added later it must persist the chosen variant per-outcropping.
    private static GameObject[] _outcroppingPrefabs;
    private static readonly string[] VeilstoneOutcroppingPrefabPaths =
    {
        "Prefabs/Veilstone/P_VeilstoneOutcropping_GemA",
        "Prefabs/Veilstone/P_VeilstoneOutcropping_GemB",
        "Prefabs/Veilstone/P_VeilstoneOutcropping_GemC",
    };

    /// <summary>
    /// Spawns the visual for a outcropping / veilstone-node ECS entity using one of
    /// the Shatter Stone gem-cluster prefab variants. Adds a BoxCollider for
    /// click selection (sized to match the previous procedural pile so
    /// selection ergonomics carry over), an EntityReference for raycasting,
    /// and a VeilstoneOutcroppingCrystalAnimator that drives wobble/shatter from
    /// ECS mining events.
    /// </summary>
    private GameObject CreateProceduralVeilstoneOutcroppingLoot(Vector3 center, Entity entity)
    {
        if (_outcroppingPrefabs == null)
        {
            _outcroppingPrefabs = new GameObject[VeilstoneOutcroppingPrefabPaths.Length];
            for (int i = 0; i < VeilstoneOutcroppingPrefabPaths.Length; i++)
            {
                _outcroppingPrefabs[i] = Resources.Load<GameObject>(VeilstoneOutcroppingPrefabPaths[i]);
            }
        }

        int variantIdx = Mathf.Abs(entity.Index) % _outcroppingPrefabs.Length;
        var prefab = _outcroppingPrefabs[variantIdx];
        if (prefab == null)
        {
            // Resource missing — fall back to a bare GameObject so the
            // entity still has a selection target rather than throwing.
            var fallback = new GameObject($"VeilstoneLoot_{entity.Index}_missing");
            fallback.transform.position = center;
            AttachVeilstoneOutcroppingSelectionAndAnimator(fallback, entity,
                CellColliderScaleFor(entity, VeilstoneOutcroppingVisualBaseScale));
            return fallback;
        }

        var root = Instantiate(prefab, center, Quaternion.identity);
        StripThirdPartyControllers(root);
        root.name = $"VeilstoneLoot_{entity.Index}";

        AttachVeilstoneOutcroppingSelectionAndAnimator(root, entity,
            CellColliderScaleFor(entity, VeilstoneOutcroppingVisualBaseScale));
        return root;
    }


    /// <summary>Visual size multiplier applied on top of the ECS amount-driven scale.</summary>
    /// <summary>Prefab-to-world scale for the gem cluster. Public because the
    /// node factory sizes its ECS scale against it so a full node fills
    /// exactly one 2 m build cell. docs/Design/Build_Grid.md</summary>
    internal const float VeilstoneOutcroppingVisualBaseScale = 6f;

    /// <param name="cellColliderWorldScale">
    /// The visual's eventual uniform world scale (ECS scale × base scale), so
    /// the build-cell cube collider can be sized in local units at spawn —
    /// before SyncTransforms has applied that scale to the transform.
    /// docs/Design/Build_Grid.md
    /// </param>
    private static void AttachVeilstoneOutcroppingSelectionAndAnimator(GameObject root, Entity entity,
        float cellColliderWorldScale)
    {
        // Make the cluster ~3× the bare Shatter Stone authoring size. SyncTransforms
        // multiplies the ECS LocalTransform.Scale by ProceduralScaleTag.BaseScale,
        // so this scales every outcropping uniformly while preserving the amount-driven
        // size variation (VeilstoneOutcropping.MinScale=0.6 .. MaxScale=4.0).
        var scaleTag = root.GetComponent<ProceduralScaleTag>();
        if (scaleTag == null) scaleTag = root.AddComponent<ProceduralScaleTag>();
        scaleTag.BaseScale = VeilstoneOutcroppingVisualBaseScale;

        // Selection collider. A resource node owns exactly one build cell, so
        // its click target is that CELL — a 2 m cube — not a box shrink-wrapped
        // to the gem art. The fitted box under-filled the cell wherever the
        // clump was narrow and overhung it wherever a spar stuck out, so
        // clicking the visible ground of a node could miss it while clicking
        // the neighbouring cell could hit it.
        FitCellBoxCollider(root, cellColliderWorldScale);

        var entityRef = root.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        var anim = root.GetComponent<TheWaningBorder.Presentation.VeilstoneOutcroppingCrystalAnimator>();
        if (anim == null) anim = root.AddComponent<TheWaningBorder.Presentation.VeilstoneOutcroppingCrystalAnimator>();
    }

}
