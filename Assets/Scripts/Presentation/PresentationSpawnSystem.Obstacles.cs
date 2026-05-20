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

    // Cached prefab — loaded once per session. Resources.Load is cheap on
    // subsequent calls but holding the reference avoids the dictionary
    // lookup per spawn.
    private static GameObject _ironDepositPrefab;

    /// <summary>
    /// Spawn an iron deposit GameObject from the authored prefab at
    /// Resources/Prefabs/Nature/IronDeposit, scaled +30 % from the prefab's
    /// authored size. The prefab is the canonical visual now — the older
    /// procedural sphere-cluster fallback runs only when the prefab is
    /// missing from the build.
    /// </summary>
    private GameObject CreateProceduralIronDeposit(Vector3 center, Entity entity)
    {
        if (_ironDepositPrefab == null)
            _ironDepositPrefab = Resources.Load<GameObject>("Prefabs/Nature/IronDeposit");

        GameObject root;
        if (_ironDepositPrefab != null)
        {
            root = Instantiate(_ironDepositPrefab, center, Quaternion.Euler(0f, (entity.Index * 47) % 360f, 0f));
            root.name = $"IronDeposit_{entity.Index}";
            // +30 % over the prefab's authored scale.
            root.transform.localScale *= 1.3f;

            // Ensure a collider so units physically stop. The authored
            // prefab may or may not have one — add a capsule sized to the
            // deposit's grounded bounds if missing.
            if (root.GetComponentInChildren<Collider>() == null)
            {
                var caps = root.AddComponent<CapsuleCollider>();
                caps.radius = 1.0f * 1.3f;
                caps.height = 2.0f * 1.3f;
                caps.center = Vector3.up * 1f;
            }
        }
        else
        {
            // Fallback: original procedural sphere cluster, kept so missing
            // prefab still renders SOMETHING rather than an empty deposit.
            root = CreateLegacyIronDepositMesh(center, entity);
        }

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
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
