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
using TheWaningBorder.Core;
using TheWaningBorder.Presentation;   // EntityViewManager

public partial class PresentationSpawnSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // PROCEDURAL OBSTACLE GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    // Realistic tree prefab variants (MapMagic demo Pine/Birch remapped to URP
    // materials by GameDataMaintenanceTool.BuildRealisticTrees). Loaded lazily
    // on first forest spawn.
    private static GameObject[] _forestTreePrefabs;
    private const string ForestTreeResourceFolder = "Prefabs/Nature/RealisticTrees";

    /// <summary>
    /// Create a forest cluster: realistic trees (pine/birch mix with foliage)
    /// scattered within the forest radius. Deterministic per entity so all
    /// lockstep clients see the same forest (visual-only either way).
    /// </summary>
    private GameObject CreateProceduralForest(Vector3 center, float radius, Entity entity)
    {
        var root = new GameObject($"Forest_{entity.Index}");
        root.transform.position = center;

        if (_forestTreePrefabs == null)
            _forestTreePrefabs = Resources.LoadAll<GameObject>(ForestTreeResourceFolder);

        if (_forestTreePrefabs.Length == 0)
        {
            // Tree variants not built (GameDataMaintenanceTool.BuildRealisticTrees
            // not run) — keep the old invisible-root behavior so cleanup and
            // minimap tracking still work.
            root.SetActive(false);
            return root;
        }

        var rng = new System.Random(entity.Index + 24680);
        int treeCount = Mathf.Clamp(Mathf.RoundToInt(radius * radius * 0.45f), 5, 40);

        for (int i = 0; i < treeCount; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(System.Math.Sqrt(rng.NextDouble()) * radius * 0.9f);
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            var prefab = _forestTreePrefabs[rng.Next(_forestTreePrefabs.Length)];
            var tree = Instantiate(prefab, root.transform, false);
            tree.name = $"Tree_{i}";

            float treeY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            tree.transform.localPosition = new Vector3(offsetX, treeY - center.y, offsetZ);
            tree.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360f), 0f);
            tree.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.5f);
        }

        // No collider: forest passability is handled by the ECS obstacle data,
        // matching the previous (invisible-root) behavior.
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
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

}
