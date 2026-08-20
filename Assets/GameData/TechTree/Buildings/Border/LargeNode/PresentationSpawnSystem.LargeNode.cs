// PresentationSpawnSystem.LargeNode.cs
// Visuals for the curse Large node (BorderMainNode, the corner well):
// authored gem-cluster well prefab with procedural crystal-spire fallback,
// plus the shared crystal-spire/material helpers. Co-located with the
// entity per the TechTree convention. Partial of PresentationSpawnSystem.

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
    /// <summary>
    /// Well (Border main node) visual: the same Shatter Stone gem-cluster
    /// prefab the mineable veilstone uses, scaled to landmark size. Keeps a
    /// fitted selection collider + EntityReference so the well stays
    /// attackable and ritual-targetable. Returns null if the prefab
    /// resources are missing (caller falls back to the procedural spires).
    /// </summary>
    private GameObject CreateWellFromVeilstonePrefab(Vector3 center, Entity entity)
    {
        if (_outcroppingPrefabs == null)
        {
            _outcroppingPrefabs = new GameObject[VeilstoneOutcroppingPrefabPaths.Length];
            for (int i = 0; i < VeilstoneOutcroppingPrefabPaths.Length; i++)
                _outcroppingPrefabs[i] = Resources.Load<GameObject>(VeilstoneOutcroppingPrefabPaths[i]);
        }

        var prefab = _outcroppingPrefabs[Mathf.Abs(entity.Index) % _outcroppingPrefabs.Length];
        if (prefab == null) return null;

        var root = Instantiate(prefab, center,
            Quaternion.Euler(0f, (entity.Index * 61) % 360f, 0f));
        StripThirdPartyControllers(root);
        root.name = $"BorderWell_{entity.Index}";

        // Landmark size. The well's ECS LocalTransform.Scale is 1, so
        // BaseScale alone sets world size — it still sits far above every
        // deposit so the well reads as the eruption point of the sheet.
        // Halved from 16 (2026-08-03 playtest: wells read HUUUGE next to
        // the shrunken resource nodes).
        var scaleTag = root.GetComponent<ProceduralScaleTag>();
        if (scaleTag == null) scaleTag = root.AddComponent<ProceduralScaleTag>();
        scaleTag.BaseScale = 8f;

        FitSelectionCollider(root, minWorldXZ: 1.5f, minWorldY: 1.5f);

        var entityRef = root.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
    }


    /// <summary>
    /// Create a procedural visual for veilstone nodes (buildings) and veilstone units.
    /// Uses crystalline shapes with purple/violet tones and emission glow.
    /// </summary>
    private GameObject CreateProceduralCrystalEntity(Vector3 center, int presentationId, Entity entity)
    {
        // Curse & Shardroot canon: WELLS wear the veilstone gem-cluster
        // prefab at landmark scale — the well IS a veilstone formation
        // (it's where the Shardroot hides), same material the player mines.
        // Falls through to the procedural spires only if the prefab
        // resources are missing.
        if (presentationId == 310)
        {
            var wellGo = CreateWellFromVeilstonePrefab(center, entity);
            if (wellGo != null) return wellGo;
        }

        // Only the Large node (310) routes here — Border units (320+) use
        // authored prefabs and never reach this builder. (The old procedural
        // crystal-humanoid unit visual was dead code and is deleted.)
        var root = new GameObject($"BorderMainNode_{entity.Index}");
        root.transform.position = center;

        // Veilstone color palette
        var crystalCore = GetCrystalColor(presentationId);
        var crystalGlow = crystalCore * 1.4f;
        crystalGlow.a = 1f;

        CreateBorderNodeVisual(root, presentationId, crystalCore, crystalGlow, entity);

        // Selection collider fitted to the crystal visual (Veilstingers were
        // notoriously hard to click with the old fixed 1x1.5x1 box).
        FitSelectionCollider(root, minWorldXZ: 1.1f, minWorldY: 1.2f);

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Get the base veilstone color for a given presentation ID.
    /// Different sub-node types use different color accents.
    /// </summary>
    private static Color GetCrystalColor(int presentationId)
    {
        return presentationId switch
        {
            310 => new Color(0.55f, 0.15f, 0.70f),  // Main node: deep purple
            320 => new Color(0.45f, 0.20f, 0.60f),  // Crystalling: purple
            321 => new Color(0.35f, 0.15f, 0.55f),  // Veilstinger: dark purple
            322 => new Color(0.60f, 0.25f, 0.65f),  // Godsplinter: bright violet
            _ => new Color(0.50f, 0.15f, 0.60f)     // Default veilstone purple
        };
    }

    /// <summary>
    /// Create a veilstone node/building visual: a cluster of tall, jagged spires
    /// (procedurally faceted meshes — flat shaded, polygonal sides, taper to
    /// an irregular apex). Purple base fading to dark green tips with a white
    /// point light at the core.
    /// </summary>
    private void CreateBorderNodeVisual(GameObject root, int presentationId, Color coreColor, Color glowColor, Entity entity)
    {
        float scale = presentationId == 310 ? 1.5f : 1f; // Main node is 50% larger

        // --- Per-function colour palette derived from coreColor ---
        var purpleBase = new Color(coreColor.r * 0.7f, coreColor.g * 0.5f, coreColor.b * 0.8f, 0.70f);
        var greenTip   = new Color(coreColor.r * 0.15f, coreColor.g * 0.6f, coreColor.b * 0.25f, 0.55f);
        var emissionPurple = coreColor * 0.7f;

        var rng = new System.Random(entity.Index + presentationId);

        // --- Central dominant spire (tall jagged spike) ---
        float mainHeight = 4.2f * scale;
        var mainCrystal = BuildCrystalSpire(
            "MainCrystal",
            height: mainHeight,
            baseRadius: 0.55f * scale,
            sides: 6,
            rings: 4,
            jaggedness: 0.30f,
            rng: rng);
        mainCrystal.transform.SetParent(root.transform, false);
        mainCrystal.transform.localPosition = Vector3.zero;
        mainCrystal.transform.localRotation = Quaternion.Euler(
            (float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 360f,
            (float)rng.NextDouble() * 6f - 3f);
        ApplyCrystalMaterial(mainCrystal, purpleBase, greenTip, emissionPurple, tipBlend: 0.65f);

        // --- Secondary spires (leaning outward) ---
        int secondaryCount = presentationId == 310 ? 4 : 2;
        for (int i = 0; i < secondaryCount; i++)
        {
            float angle = (i / (float)secondaryCount) * 360f + (float)rng.NextDouble() * 40f;
            float dist  = (0.55f + (float)rng.NextDouble() * 0.35f) * scale;
            float h     = (2.2f + (float)rng.NextDouble() * 1.4f) * scale;

            var veilstone = BuildCrystalSpire(
                $"Crystal_{i}",
                height: h,
                baseRadius: (0.30f + (float)rng.NextDouble() * 0.15f) * scale,
                sides: 5,
                rings: 3,
                jaggedness: 0.35f,
                rng: rng);
            veilstone.transform.SetParent(root.transform, false);
            float px = Mathf.Cos(angle * Mathf.Deg2Rad) * dist;
            float pz = Mathf.Sin(angle * Mathf.Deg2Rad) * dist;
            veilstone.transform.localPosition = new Vector3(px, 0f, pz);
            // Lean outward — apex tilts away from the cluster center.
            float lean = 12f + (float)rng.NextDouble() * 18f;
            veilstone.transform.localRotation = Quaternion.Euler(
                Mathf.Cos(angle * Mathf.Deg2Rad) * lean,
                angle + (float)rng.NextDouble() * 30f,
                Mathf.Sin(angle * Mathf.Deg2Rad) * lean);

            float tipT = 0.4f + (float)rng.NextDouble() * 0.4f;
            ApplyCrystalMaterial(veilstone, purpleBase, greenTip, emissionPurple, tipBlend: tipT);
        }

        // --- Small jagged shards at the base ---
        int nubCount = presentationId == 310 ? 6 : 3;
        for (int i = 0; i < nubCount; i++)
        {
            float angle = (float)rng.NextDouble() * 360f;
            float dist  = (0.4f + (float)rng.NextDouble() * 0.9f) * scale;
            float nubH  = (0.6f + (float)rng.NextDouble() * 0.7f) * scale;

            var nub = BuildCrystalSpire(
                $"Nub_{i}",
                height: nubH,
                baseRadius: (0.16f + (float)rng.NextDouble() * 0.10f) * scale,
                sides: 4,
                rings: 2,
                jaggedness: 0.40f,
                rng: rng);
            nub.transform.SetParent(root.transform, false);
            float px = Mathf.Cos(angle * Mathf.Deg2Rad) * dist;
            float pz = Mathf.Sin(angle * Mathf.Deg2Rad) * dist;
            nub.transform.localPosition = new Vector3(px, 0f, pz);
            nub.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 30f - 15f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 30f - 15f);

            // Nubs are more purple (base region), less green blend
            ApplyCrystalMaterial(nub, purpleBase, greenTip, emissionPurple, tipBlend: 0.15f);
        }

        // (Ground-stain base disc removed — the border-ground splat painted
        // by BorderSpreadSystem now provides the dark stain underneath.)

        // --- White point light inside the veilstone cluster ---
        var lightObj = new GameObject("CrystalCoreLight");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = Vector3.up * (1.0f * scale);
        var pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = Color.white;
        pointLight.intensity = presentationId == 310 ? 1.8f : 1.2f;
        pointLight.range = 3.5f * scale;
        pointLight.shadows = LightShadows.None;

        // --- Ambient border particle drift (task-111 phase 3) ---
        // Only the main veilstone node (PresentationID 310) gets ambient motes.
        // Sub-nodes (Resource/Enforcement/Suppression/etc.) are visual accents
        // and don't drive border spread, so they don't need their own particle
        // system. The particle GO is parented to the node root so it cleans
        // up automatically when the entity is destroyed.
        if (presentationId == 310)
        {
            var particleGo = ProceduralBorderParticleGenerator.Create(
                root.transform.position, entity, _em);
            if (particleGo != null)
            {
                particleGo.transform.SetParent(root.transform, worldPositionStays: false);
                particleGo.transform.localPosition = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// Build a faceted veilstone-spire GameObject with MeshFilter + MeshRenderer
    /// attached. Polygonal cross-section taper from a base ring to a single
    /// apex at the top, with each ring vertex randomly perturbed for jagged
    /// edges. Vertices are split per-triangle so URP/Lit flat-shades each
    /// facet (no smoothing across edges) — the shape reads as crystalline
    /// rather than rounded.
    ///
    /// Parameters:
    ///   sides      — polygonal divisions around the vertical axis (4-8 looks best).
    ///   rings      — vertical ring stages between base and apex (2-5).
    ///   jaggedness — 0..1 random radial perturbation factor per vertex.
    /// </summary>
    private static GameObject BuildCrystalSpire(string name, float height, float baseRadius,
        int sides, int rings, float jaggedness, System.Random rng)
    {
        if (sides < 3) sides = 3;
        if (rings < 1) rings = 1;

        var smoothVerts = new List<Vector3>();
        var smoothTris = new List<int>();

        // Build vertical rings, base (r=0) → near-apex (r=rings).
        var ringIdx = new int[rings + 1, sides];
        for (int r = 0; r <= rings; r++)
        {
            float t = r / (float)rings;
            float y = height * t;
            // Quadratic taper: stays full near the base, narrows aggressively
            // near the top so the silhouette reads as a spire, not a cone.
            float radius = baseRadius * (1f - 0.85f * (t * t));
            // Twist each ring so opposing facets don't form long flat strips.
            float ringTwist = (float)rng.NextDouble() * (Mathf.PI / sides);
            for (int s = 0; s < sides; s++)
            {
                float angle = ringTwist + s * (Mathf.PI * 2f / sides);
                float jag = 1f + ((float)rng.NextDouble() - 0.5f) * jaggedness;
                float ax = Mathf.Cos(angle) * radius * jag;
                float az = Mathf.Sin(angle) * radius * jag;
                ringIdx[r, s] = smoothVerts.Count;
                smoothVerts.Add(new Vector3(ax, y, az));
            }
        }

        // Apex point — slightly off-center for asymmetry.
        int apexIdx = smoothVerts.Count;
        smoothVerts.Add(new Vector3(
            ((float)rng.NextDouble() - 0.5f) * baseRadius * 0.20f,
            height * 1.04f,
            ((float)rng.NextDouble() - 0.5f) * baseRadius * 0.20f));

        // Side quads between rings (two triangles each).
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < sides; s++)
            {
                int s2 = (s + 1) % sides;
                int a = ringIdx[r, s];
                int b = ringIdx[r, s2];
                int c = ringIdx[r + 1, s];
                int d = ringIdx[r + 1, s2];
                smoothTris.Add(a); smoothTris.Add(c); smoothTris.Add(b);
                smoothTris.Add(b); smoothTris.Add(c); smoothTris.Add(d);
            }
        }

        // Cap to apex from the top ring.
        int topRing = rings;
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            smoothTris.Add(ringIdx[topRing, s]);
            smoothTris.Add(apexIdx);
            smoothTris.Add(ringIdx[topRing, s2]);
        }

        // Bottom cap (closed base disc) so the spire isn't see-through from
        // below when the camera tilts low. Triangle fan around the centroid.
        int baseCenterIdx = smoothVerts.Count;
        smoothVerts.Add(new Vector3(0f, 0f, 0f));
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            smoothTris.Add(baseCenterIdx);
            smoothTris.Add(ringIdx[0, s2]);
            smoothTris.Add(ringIdx[0, s]);
        }

        // Flat-shade by giving every triangle its own three vertices. Each
        // face then gets a unique normal and the spire reads as faceted.
        var srcVerts = smoothVerts.ToArray();
        var srcTris = smoothTris.ToArray();
        var flatVerts = new Vector3[srcTris.Length];
        var flatTris = new int[srcTris.Length];
        for (int i = 0; i < srcTris.Length; i++)
        {
            flatVerts[i] = srcVerts[srcTris[i]];
            flatTris[i] = i;
        }

        var mesh = new Mesh { name = $"{name}_SpireMesh" };
        mesh.vertices = flatVerts;
        mesh.triangles = flatTris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        return go;
    }

    /// <summary>
    /// Apply a translucent, reflective crystalline material with purple-to-green gradient
    /// and dim purple emission.
    /// <paramref name="tipBlend"/> controls how much green-tip color is mixed in (0 = pure base, 1 = pure tip).
    /// </summary>
    private static void ApplyCrystalMaterial(GameObject go, Color baseColor, Color tipColor,
        Color emissionColor, float tipBlend)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);

        // Blend base purple → dark green tip
        Color blended = Color.Lerp(baseColor, tipColor, tipBlend);

        // --- URP Transparent surface type ---
        // _Surface: 0 = Opaque, 1 = Transparent
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        // _Blend: 0 = Alpha
        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 0f);
        // Render queue for transparent
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");

        // Source / destination blend for alpha transparency
        if (mat.HasProperty("_SrcBlend"))
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlend"))
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);

        // Base color with alpha for translucency
        mat.color = blended;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", blended);

        // Highly reflective and refractant surface
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0.85f);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.95f);

        // IOR-like refraction boost (URP Lit doesn't have true IOR, but high smoothness +
        // specular highlights on transparent surface gives a convincing glass/veilstone look)
        if (mat.HasProperty("_SpecularHighlights"))
            mat.SetFloat("_SpecularHighlights", 1f);
        if (mat.HasProperty("_EnvironmentReflections"))
            mat.SetFloat("_EnvironmentReflections", 1f);

        // Dim purple emission
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emissionColor * 0.35f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        renderer.material = mat;
    }
}
