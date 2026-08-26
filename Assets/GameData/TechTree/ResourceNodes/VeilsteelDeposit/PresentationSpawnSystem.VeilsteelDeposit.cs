// PresentationSpawnSystem.VeilsteelDeposit.cs
// Veilsteel "Sharp Crystals" node visual — steel-blue tinted gem cluster at
// landmark scale. Co-located with the resource per the TechTree convention.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Presentation;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Entities;

using TheWaningBorder.Core;
public partial class PresentationSpawnSystem
{
    /// <summary>
    /// Visual for the Veilsteel "Sharp Crystals" map resource (single 1500-unit
    /// node). Reuses the Shatter Stone gem-cluster prefab but tinted pale
    /// steel-blue so it reads as a different resource than veilstone, scaled up
    /// to landmark size (one node carries a whole patch's worth of value).
    /// </summary>
    private GameObject CreateProceduralVeilsteelDeposit(Vector3 center, Entity entity)
    {
        if (_outcroppingPrefabs == null)
        {
            _outcroppingPrefabs = new GameObject[VeilstoneOutcroppingPrefabPaths.Length];
            for (int i = 0; i < VeilstoneOutcroppingPrefabPaths.Length; i++)
                _outcroppingPrefabs[i] = Resources.Load<GameObject>(VeilstoneOutcroppingPrefabPaths[i]);
        }

        var prefab = _outcroppingPrefabs[Mathf.Abs(entity.Index) % _outcroppingPrefabs.Length];
        GameObject root;
        if (prefab != null)
        {
            root = Instantiate(prefab, center, Quaternion.Euler(0f, (entity.Index * 47) % 360f, 0f));
            StripThirdPartyControllers(root);

            // Steel-blue tint distinguishes sharp crystals from the purple
            // veilstone gems that share the same source prefab.
            var tint = new Color(0.55f, 0.85f, 0.95f, 1f);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    else if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", tint * 0.6f);
                }
            }
        }
        else
        {
            root = new GameObject($"VeilsteelDeposit_{entity.Index}_missing");
            root.transform.position = center;
        }

        root.name = $"VeilsteelDeposit_{entity.Index}";
        AttachVeilsteelDepositSelectionAndAnimator(root, entity);
        return root;
    }

    /// <summary>
    /// Selection + animator wiring for the veilsteel node. Its own method
    /// rather than a call into the veilstone partial: the two nodes share the
    /// gem-cluster PREFAB (a deliberate asset reuse — the cache lives in
    /// VeilstoneOutcropping, per CLAUDE.md), but they are different entities
    /// and their presentation should be readable and changeable apart.
    /// </summary>
    private void AttachVeilsteelDepositSelectionAndAnimator(GameObject root, Entity entity)
    {
        var scaleTag = root.GetComponent<ProceduralScaleTag>();
        if (scaleTag == null) scaleTag = root.AddComponent<ProceduralScaleTag>();
        scaleTag.BaseScale = VeilsteelDepositVisualBaseScale;

        // Click target = the build cell the node occupies, as a cube — same
        // rule as iron and veilstone. The node's ECS scale is already sized so
        // the gem cluster spans exactly one cell, so this matches the art too.
        // docs/Design/Build_Grid.md
        FitCellBoxCollider(root, CellColliderScaleFor(entity, VeilsteelDepositVisualBaseScale));

        var entityRef = root.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Shared animator: generic to any OreNode-backed visual (no per-tick
        // work, just fires Shatter() on the death handoff) — iron uses it too.
        var anim = root.GetComponent<VeilstoneOutcroppingCrystalAnimator>();
        if (anim == null) anim = root.AddComponent<VeilstoneOutcroppingCrystalAnimator>();
    }

    /// <summary>Prefab-to-world scale for the veilsteel node's visual. Equal to
    /// the veilstone outcropping's because both instantiate the same
    /// gem-cluster prefab — kept as its own named constant so the veilsteel
    /// node can be re-arted without touching veilstone.</summary>
    internal const float VeilsteelDepositVisualBaseScale = VeilstoneOutcroppingVisualBaseScale;
}
