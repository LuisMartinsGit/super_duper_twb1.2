// PresentationSpawnSystem.Obstacles.cs
// Procedural obstacle generation (forests, rocks, iron deposits)
// Extracted from PresentationSpawnSystem.cs — Fix #204

using System;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Input;          // EntityReference
using TheWaningBorder.Presentation;   // EntityViewManager

public partial class PresentationSpawnSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // PROCEDURAL OBSTACLE GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a forest cluster: several procedural trees (trunk + canopy) scattered within radius.
    /// </summary>
    private GameObject CreateProceduralForest(Vector3 center, float radius, Entity entity)
    {
        // Tree visuals are handled by ProceduralTerrain.PlaceTerrainTrees (Spruce_008 prefab).
        // This method returns an empty invisible root so PresentationSpawnSystem can track
        // the forest entity for cleanup on destroy, and so minimap tracking still works.
        var root = new GameObject($"Forest_{entity.Index}");
        root.transform.position = center;
        root.SetActive(false);
        return root;
    }

    /// <summary>
    /// Create a rock formation: several randomly rotated boulders scattered within radius.
    /// </summary>
    private GameObject CreateProceduralRockFormation(Vector3 center, float radius, Entity entity)
    {
        var root = new GameObject($"Rocks_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 67890);
        int rockCount = rng.Next(3, 6);

        // Colors
        var darkGrey = new Color(0.30f, 0.28f, 0.26f);
        var lightGrey = new Color(0.50f, 0.48f, 0.44f);
        var warmGrey = new Color(0.42f, 0.38f, 0.34f);

        for (int i = 0; i < rockCount; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(rng.NextDouble() * radius * 0.7f);
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            float rockSize = 1f + (float)rng.NextDouble() * 1.5f;

            // Get terrain height at rock position
            float rockY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            Vector3 rockBase = new Vector3(offsetX, rockY - center.y, offsetZ);

            // Boulder (stretched cube for angular look)
            var boulder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boulder.name = $"Boulder_{i}";
            boulder.transform.SetParent(root.transform, false);
            boulder.transform.localPosition = rockBase + Vector3.up * (rockSize * 0.3f);

            // Random squash/stretch for natural boulder shapes
            float sx = rockSize * (0.6f + (float)rng.NextDouble() * 0.8f);
            float sy = rockSize * (0.4f + (float)rng.NextDouble() * 0.6f);
            float sz = rockSize * (0.6f + (float)rng.NextDouble() * 0.8f);
            boulder.transform.localScale = new Vector3(sx, sy, sz);

            // Random rotation
            boulder.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 20f - 10f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 15f - 7.5f
            );

            var boulderRenderer = boulder.GetComponent<Renderer>();
            if (boulderRenderer != null)
            {
                // Fix #203: shared material + MPB
                float greyVariation = (float)rng.NextDouble();
                Color baseColor = Color.Lerp(darkGrey, lightGrey, greyVariation);
                baseColor = Color.Lerp(baseColor, warmGrey, (float)rng.NextDouble() * 0.3f);
                ProceduralMaterialHelper.SetColor(boulderRenderer, baseColor);
            }

            // Remove individual boulder colliders
            var boulderCol = boulder.GetComponent<Collider>();
            if (boulderCol != null) Destroy(boulderCol);
        }

        // Add a single collider for the whole formation
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(radius * 2f, 4f, radius * 2f);
        boxCol.center = Vector3.up * 2f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    // NV3D Shatter Stone (Metal Ores) wrapper prefabs — same pattern as the
    // cadaver crystals: variants of MetalOre_3a/3b with Rigidbody/SphereCollider
    // stripped, OreNode swapped to CadaverOreNode (reused — the subclass is
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

        // BoxCollider sized in WORLD units (3×2×3, centred y=1) — matches the
        // legacy procedural deposit's footprint. The root's localScale
        // applies on top, so we divide by BaseScale to get the intended
        // world-space dimensions after scaling.
        var boxCol = root.GetComponent<BoxCollider>();
        if (boxCol == null) boxCol = root.AddComponent<BoxCollider>();
        boxCol.size   = new Vector3(3f, 2f, 3f) / IronDepositVisualBaseScale;
        boxCol.center = new Vector3(0f, 1f, 0f) / IronDepositVisualBaseScale;

        var entityRef = root.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Reuse CadaverCrystalAnimator — it's generic to any OreNode-backed
        // visual: no per-tick work, just fires Shatter() on death-handoff.
        var anim = root.GetComponent<CadaverCrystalAnimator>();
        if (anim == null) anim = root.AddComponent<CadaverCrystalAnimator>();
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

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(3f, 2f, 3f);
        boxCol.center = Vector3.up * 1f;
        return root;
    }
}
