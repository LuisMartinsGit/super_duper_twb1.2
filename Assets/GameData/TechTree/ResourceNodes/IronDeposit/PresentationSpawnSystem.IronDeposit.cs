// PresentationSpawnSystem.IronDeposit.cs
// Iron deposit visual: Shatter Stone MetalOre wrapper prefabs with the
// legacy procedural mesh as fallback. Co-located with the resource per the
// TechTree convention. Partial of PresentationSpawnSystem.

using System;
using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Presentation;
using TheWaningBorder.World.Terrain;

using TheWaningBorder.Core;
public partial class PresentationSpawnSystem
{
    // NV3D Shatter Stone (Metal Ores) wrapper prefabs — same pattern as the
    // outcropping veilstone: variants of MetalOre_3a/3b with Rigidbody/SphereCollider
    // stripped, OreNode swapped to VeilstoneOutcroppingOreNode (reused — the subclass is
    // generic to any Shatter Stone mineable), drops/respawn off. Loaded
    // lazily on first iron-deposit spawn.
    private static GameObject[] _ironDepositPrefabs;
    private static readonly string[] IronDepositPrefabPaths =
    {
        "Prefabs/Iron/P_IronDeposit_3a",
        "Prefabs/Iron/P_IronDeposit_3b",
    };

    /// <summary>Visual size multiplier applied on top of the ECS scale.</summary>
    private const float IronDepositVisualBaseScale = 3f;

    /// <summary>
    /// Spawn an iron deposit GameObject using one of the Shatter Stone
    /// MetalOre prefab variants (3a / 3b). Variant is keyed on entity.Index
    /// so it's deterministic across networked clients. The animator handles
    /// the depletion shatter via the death-handoff in
    /// PresentationSpawnSystem.CleanupDestroyedEntities. Falls back to the
    /// legacy procedural mesh only if the Resources prefabs aren't found.
    /// </summary>
    private GameObject CreateProceduralIronDeposit(Vector3 center, Entity entity)
    {
        if (_ironDepositPrefabs == null)
        {
            _ironDepositPrefabs = new GameObject[IronDepositPrefabPaths.Length];
            for (int i = 0; i < IronDepositPrefabPaths.Length; i++)
                _ironDepositPrefabs[i] = Resources.Load<GameObject>(IronDepositPrefabPaths[i]);
        }

        int variantIdx = Mathf.Abs(entity.Index) % _ironDepositPrefabs.Length;
        var prefab = _ironDepositPrefabs[variantIdx];

        GameObject root;
        if (prefab != null)
        {
            // Randomise yaw per-deposit (kept from the previous procedural
            // path) so adjacent deposits don't look stamped.
            var rot = Quaternion.Euler(0f, (entity.Index * 47) % 360f, 0f);
            root = Instantiate(prefab, center, rot);
            root.name = $"IronDeposit_{entity.Index}";
        }
        else
        {
            // Resources prefab missing — fall back so the deposit still
            // renders something rather than nothing.
            root = CreateLegacyIronDepositMesh(center, entity);
        }

        AttachIronSelectionAndAnimator(root, entity);
        return root;
    }

    private static void AttachIronSelectionAndAnimator(GameObject root, Entity entity)
    {
        // SyncTransforms multiplies LocalTransform.Scale by BaseScale, so
        // the visual ends up at IronDepositVisualBaseScale × ECS scale.
        var scaleTag = root.GetComponent<ProceduralScaleTag>();
        if (scaleTag == null) scaleTag = root.AddComponent<ProceduralScaleTag>();
        scaleTag.BaseScale = IronDepositVisualBaseScale;

        // Click target = the 2 m build cell the deposit occupies, as a cube.
        // At the legacy 3x3 it reached half a metre past the node on every
        // side, and right-click snapped to the deposit from well outside it
        // ("too eager to assign to a resource node").
        //
        // Now via the shared helper rather than a hand-rolled box, so every
        // resource node is sized the same way AND stray colliders on the
        // wrapper prefab are cleared — the old GetComponent/AddComponent pair
        // only touched a root BoxCollider and left any child collider live to
        // swallow clicks. Iron's ECS scale is always 1, so the visual's world
        // scale is just the base scale. docs/Design/Build_Grid.md
        FitCellBoxCollider(root, IronDepositVisualBaseScale);

        var entityRef = root.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Reuse VeilstoneOutcroppingCrystalAnimator — it's generic to any OreNode-backed
        // visual: no per-tick work, just fires Shatter() on death-handoff.
        var anim = root.GetComponent<VeilstoneOutcroppingCrystalAnimator>();
        if (anim == null) anim = root.AddComponent<VeilstoneOutcroppingCrystalAnimator>();
    }

    // Legacy procedural sphere-cluster — kept only as fallback if the
    // authored prefab is missing. Not the default path any more.
    private GameObject CreateLegacyIronDepositMesh(Vector3 center, Entity entity)
    {
        var root = new GameObject($"IronDeposit_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 54321);
        int rockCount = rng.Next(3, 6);

        var ironDark = new Color(0.25f, 0.22f, 0.20f);
        var ironRusty = new Color(0.55f, 0.30f, 0.15f);
        var ironLight = new Color(0.40f, 0.35f, 0.30f);

        for (int i = 0; i < rockCount; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(rng.NextDouble() * 1.2f);
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            float rockSize = 0.6f + (float)rng.NextDouble() * 1.0f;
            float rockY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            Vector3 rockBase = new Vector3(offsetX, rockY - center.y, offsetZ);

            var ore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ore.name = $"IronOre_{i}";
            ore.transform.SetParent(root.transform, false);
            ore.transform.localPosition = rockBase + Vector3.up * (rockSize * 0.25f);
            float sx = rockSize * (0.7f + (float)rng.NextDouble() * 0.6f);
            float sy = rockSize * (0.5f + (float)rng.NextDouble() * 0.4f);
            float sz = rockSize * (0.7f + (float)rng.NextDouble() * 0.6f);
            ore.transform.localScale = new Vector3(sx, sy, sz);
            ore.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 15f - 7.5f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 10f - 5f);
            var oreRenderer = ore.GetComponent<Renderer>();
            if (oreRenderer != null)
            {
                float variation = (float)rng.NextDouble();
                Color baseColor = Color.Lerp(ironDark, ironLight, variation * 0.5f);
                baseColor = Color.Lerp(baseColor, ironRusty, (float)rng.NextDouble() * 0.45f);
                ProceduralMaterialHelper.SetProperties(oreRenderer, baseColor, metallic: 0.4f, smoothness: 0.3f);
            }
            var oreCol = ore.GetComponent<Collider>();
            if (oreCol != null) Destroy(oreCol);
        }

        // One build cell, same reasoning as the prefab path above. This root is
        // bare (no ProceduralScaleTag applied yet), so world scale is 1 here —
        // AttachIronSelectionAndAnimator re-fits it against the base scale
        // immediately after.
        FitCellBoxCollider(root, 1f);
        return root;
    }
}
