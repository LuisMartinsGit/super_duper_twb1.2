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
        var root = new GameObject($"Forest_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 12345);
        int treeCount = rng.Next(20, 31); // Dense forest: 20-30 trees

        // Colors
        var trunkBrown = new Color(0.35f, 0.22f, 0.10f);
        var canopyDarkGreen = new Color(0.15f, 0.35f, 0.10f);
        var canopyLightGreen = new Color(0.25f, 0.50f, 0.15f);

        // Ground foliage colors (fallen leaves / forest floor)
        var foliageDark = new Color(0.20f, 0.28f, 0.08f);
        var foliageLight = new Color(0.30f, 0.22f, 0.10f);

        // Shared materials
        var litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Forest floor: scattered ground cover patches (uses separate RNG to not desync tree positions)
        var groundRng = new System.Random(entity.Index + 99999);
        int patchCount = groundRng.Next(8, 15);
        for (int p = 0; p < patchCount; p++)
        {
            float pAngle = (float)(groundRng.NextDouble() * Mathf.PI * 2f);
            float pDist = (float)(groundRng.NextDouble() * radius * 0.75f);
            float px = Mathf.Cos(pAngle) * pDist;
            float pz = Mathf.Sin(pAngle) * pDist;
            float py = TerrainUtility.GetHeight(center.x + px, center.z + pz) - center.y;

            float patchSize = 1.5f + (float)groundRng.NextDouble() * 2.5f;
            float patchRot = (float)groundRng.NextDouble() * 360f;
            float colorT = (float)groundRng.NextDouble();

            var patch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            patch.name = $"GroundPatch_{p}";
            patch.transform.SetParent(root.transform, false);
            patch.transform.localPosition = new Vector3(px, py + 0.05f, pz);
            patch.transform.localRotation = Quaternion.Euler(90f, patchRot, 0f);
            patch.transform.localScale = new Vector3(patchSize, patchSize, 1f);

            var patchRenderer = patch.GetComponent<Renderer>();
            if (patchRenderer != null)
            {
                // Fix #203: use shared material + MaterialPropertyBlock
                ProceduralMaterialHelper.SetColor(patchRenderer, Color.Lerp(foliageDark, foliageLight, colorT));
                patchRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var patchCol = patch.GetComponent<Collider>();
            if (patchCol != null) Destroy(patchCol);
        }

        for (int i = 0; i < treeCount; i++)
        {
            // Random position tightly packed within radius
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(rng.NextDouble() * radius * 0.65f); // Tighter packing
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            float treeHeight = 2.5f + (float)rng.NextDouble() * 3f;
            float trunkRadius = 0.12f + (float)rng.NextDouble() * 0.08f;
            float canopyRadius = 0.7f + (float)rng.NextDouble() * 0.5f;

            // Get terrain height at tree position
            float treeY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            Vector3 treeBase = new Vector3(offsetX, treeY - center.y, offsetZ);

            // Trunk (cylinder)
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = $"Trunk_{i}";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = treeBase + Vector3.up * (treeHeight * 0.35f);
            trunk.transform.localScale = new Vector3(trunkRadius * 2f, treeHeight * 0.4f, trunkRadius * 2f);
            var trunkRenderer = trunk.GetComponent<Renderer>();
            if (trunkRenderer != null)
            {
                // Fix #203: shared material + MPB
                ProceduralMaterialHelper.SetColor(trunkRenderer, trunkBrown);
            }
            // Remove trunk collider (individual tree ECS entities handle collision)
            var trunkCol = trunk.GetComponent<Collider>();
            if (trunkCol != null) Destroy(trunkCol);

            // Canopy (sphere)
            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = $"Canopy_{i}";
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localPosition = treeBase + Vector3.up * (treeHeight * 0.65f);
            canopy.transform.localScale = Vector3.one * canopyRadius * 2f;
            var canopyRenderer = canopy.GetComponent<Renderer>();
            if (canopyRenderer != null)
            {
                // Fix #203: shared material + MPB
                float greenVariation = (float)rng.NextDouble();
                ProceduralMaterialHelper.SetColor(canopyRenderer, Color.Lerp(canopyDarkGreen, canopyLightGreen, greenVariation));
            }
            // Remove canopy collider
            var canopyCol = canopy.GetComponent<Collider>();
            if (canopyCol != null) Destroy(canopyCol);
        }

        // Add a single large collider for the whole forest (selection/raycasting)
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(radius * 2f, 6f, radius * 2f);
        boxCol.center = Vector3.up * 3f;

        // Add EntityReference
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

    /// <summary>
    /// Create an iron deposit: a cluster of dark metallic rocks with reddish-brown iron ore veins.
    /// </summary>
    private GameObject CreateProceduralIronDeposit(Vector3 center, Entity entity)
    {
        var root = new GameObject($"IronDeposit_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 54321);
        int rockCount = rng.Next(3, 6);

        // Iron ore colors — dark grey with rusty orange veins
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

            // Iron rock (sphere for smoother ore look)
            var ore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ore.name = $"IronOre_{i}";
            ore.transform.SetParent(root.transform, false);
            ore.transform.localPosition = rockBase + Vector3.up * (rockSize * 0.25f);

            // Squash slightly for natural boulder shape
            float sx = rockSize * (0.7f + (float)rng.NextDouble() * 0.6f);
            float sy = rockSize * (0.5f + (float)rng.NextDouble() * 0.4f);
            float sz = rockSize * (0.7f + (float)rng.NextDouble() * 0.6f);
            ore.transform.localScale = new Vector3(sx, sy, sz);

            ore.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 15f - 7.5f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 10f - 5f
            );

            var oreRenderer = ore.GetComponent<Renderer>();
            if (oreRenderer != null)
            {
                // Fix #203: shared material + MPB (metallic sheen preserved via SetProperties)
                float variation = (float)rng.NextDouble();
                Color baseColor = Color.Lerp(ironDark, ironLight, variation * 0.5f);
                baseColor = Color.Lerp(baseColor, ironRusty, (float)rng.NextDouble() * 0.45f);
                ProceduralMaterialHelper.SetProperties(oreRenderer, baseColor, metallic: 0.4f, smoothness: 0.3f);
            }

            // Remove individual colliders
            var oreCol = ore.GetComponent<Collider>();
            if (oreCol != null) Destroy(oreCol);
        }

        // Add a single collider for the deposit
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(3f, 2f, 3f);
        boxCol.center = Vector3.up * 1f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

}
