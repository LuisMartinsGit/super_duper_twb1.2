// PresentationSpawnSystem.Smelter.cs
// Procedural forge visual for the Alanthor Smelter — co-located with the
// entity per the TechTree convention. Partial of PresentationSpawnSystem.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Presentation;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Entities;

using TheWaningBorder.Core;
public partial class PresentationSpawnSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // ALANTHOR SMELTER (FORGE) PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a large forge: stone-block plinth with a tile-roofed forge hall,
    /// a tall main stack plus a smaller side flue, two glowing furnace mouths,
    /// a heavy iron-banded anvil with hammer, a quench barrel, and a bellows.
    /// </summary>
    private GameObject CreateProceduralSmelter(Vector3 center, Entity entity)
    {
        var root = new GameObject($"Forge_{entity.Index}");
        root.transform.position = center;

        // Palette — slightly cooler stone, warmer roof, bright embers.
        var stone      = new Color(0.46f, 0.40f, 0.34f);
        var stoneDark  = new Color(0.32f, 0.28f, 0.24f);
        var roofTile   = new Color(0.55f, 0.30f, 0.20f);
        var beam       = new Color(0.30f, 0.20f, 0.13f);
        var iron       = new Color(0.18f, 0.17f, 0.16f);
        var brass      = new Color(0.66f, 0.50f, 0.20f);
        var leather    = new Color(0.42f, 0.26f, 0.16f);
        var embers     = new Color(0.95f, 0.45f, 0.10f);
        var water      = new Color(0.25f, 0.32f, 0.42f);

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        System.Func<PrimitiveType, string, Vector3, Vector3, Quaternion, Color, float, float, bool, GameObject>
        Make = (type, name, lp, ls, lr, color, metal, smooth, glow) =>
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = lp;
            go.transform.localRotation = lr;
            go.transform.localScale = ls;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(shader);
                r.material.color = color;
                if (r.material.HasProperty("_Metallic"))   r.material.SetFloat("_Metallic", metal);
                if (r.material.HasProperty("_Smoothness")) r.material.SetFloat("_Smoothness", smooth);
                if (glow && r.material.HasProperty("_EmissionColor"))
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", color * 1.6f);
                }
            }
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            return go;
        };

        // Plinth + dirt-darker apron at the front working area.
        Make(PrimitiveType.Cube, "Plinth", new Vector3(0f, 0.20f, 0f),
            new Vector3(4.6f, 0.40f, 4.0f), Quaternion.identity, stoneDark, 0.05f, 0.15f, false);
        Make(PrimitiveType.Cube, "Apron",  new Vector3(0f, 0.06f, 1.95f),
            new Vector3(4.4f, 0.05f, 1.6f), Quaternion.identity, stoneDark * 0.85f, 0.05f, 0.10f, false);

        // Main forge hall body (the furnace block).
        Make(PrimitiveType.Cube, "Hall",   new Vector3(0f, 1.65f, -0.6f),
            new Vector3(4.0f, 2.40f, 2.4f), Quaternion.identity, stone, 0.10f, 0.15f, false);
        // Stone course / belt running around the hall.
        Make(PrimitiveType.Cube, "Course", new Vector3(0f, 1.35f, -0.6f),
            new Vector3(4.10f, 0.10f, 2.50f), Quaternion.identity, stoneDark, 0.10f, 0.15f, false);

        // Pitched tile roof — two angled slabs meeting at the ridge.
        Make(PrimitiveType.Cube, "RoofL",  new Vector3(-1.10f, 3.30f, -0.6f),
            new Vector3(2.40f, 0.18f, 2.80f), Quaternion.Euler(0f, 0f,  18f), roofTile, 0.05f, 0.20f, false);
        Make(PrimitiveType.Cube, "RoofR",  new Vector3( 1.10f, 3.30f, -0.6f),
            new Vector3(2.40f, 0.18f, 2.80f), Quaternion.Euler(0f, 0f, -18f), roofTile, 0.05f, 0.20f, false);
        Make(PrimitiveType.Cube, "Ridge",  new Vector3(0f, 3.65f, -0.6f),
            new Vector3(0.30f, 0.10f, 2.85f), Quaternion.identity, stoneDark, 0.10f, 0.20f, false);
        // Front beam under the roof eaves — exposed timber.
        Make(PrimitiveType.Cube, "Eave",   new Vector3(0f, 2.95f, 0.65f),
            new Vector3(4.20f, 0.18f, 0.18f), Quaternion.identity, beam, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cube, "EaveBack", new Vector3(0f, 2.95f, -1.85f),
            new Vector3(4.20f, 0.18f, 0.18f), Quaternion.identity, beam, 0.05f, 0.10f, false);

        // Main chimney stack — wide stone block with capstone on top.
        Make(PrimitiveType.Cube,     "StackBase", new Vector3(-0.95f, 4.10f, -0.6f),
            new Vector3(0.95f, 1.30f, 0.95f), Quaternion.identity, stone, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cube,     "StackUpper", new Vector3(-0.95f, 5.20f, -0.6f),
            new Vector3(0.80f, 0.90f, 0.80f), Quaternion.identity, stoneDark, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cube,     "StackCap", new Vector3(-0.95f, 5.75f, -0.6f),
            new Vector3(1.00f, 0.10f, 1.00f), Quaternion.identity, stone, 0.05f, 0.20f, false);
        Make(PrimitiveType.Cylinder, "StackPipe", new Vector3(-0.95f, 5.95f, -0.6f),
            new Vector3(0.40f, 0.30f, 0.40f), Quaternion.identity, iron, 0.50f, 0.30f, false);

        // Smaller secondary flue on the other side.
        Make(PrimitiveType.Cylinder, "FlueA", new Vector3(1.25f, 4.25f, -0.6f),
            new Vector3(0.45f, 1.10f, 0.45f), Quaternion.identity, iron, 0.45f, 0.30f, false);
        Make(PrimitiveType.Cylinder, "FlueCap", new Vector3(1.25f, 5.50f, -0.6f),
            new Vector3(0.55f, 0.10f, 0.55f), Quaternion.identity, iron * 0.8f, 0.50f, 0.30f, false);

        // Twin furnace mouths on the front face — bright emissive embers.
        for (int i = 0; i < 2; i++)
        {
            float mx = (i == 0 ? -1.05f : 1.05f);
            // Stone arch frame around each opening.
            Make(PrimitiveType.Cube, $"ArchTop_{i}", new Vector3(mx, 1.90f, 0.62f),
                new Vector3(1.30f, 0.20f, 0.10f), Quaternion.identity, stoneDark, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cube, $"ArchL_{i}", new Vector3(mx - 0.55f, 1.30f, 0.62f),
                new Vector3(0.20f, 1.40f, 0.10f), Quaternion.identity, stoneDark, 0.05f, 0.15f, false);
            Make(PrimitiveType.Cube, $"ArchR_{i}", new Vector3(mx + 0.55f, 1.30f, 0.62f),
                new Vector3(0.20f, 1.40f, 0.10f), Quaternion.identity, stoneDark, 0.05f, 0.15f, false);
            // Glowing furnace mouth.
            Make(PrimitiveType.Cube, $"Mouth_{i}", new Vector3(mx, 1.20f, 0.66f),
                new Vector3(0.95f, 1.20f, 0.06f), Quaternion.identity, embers, 0.0f, 0.05f, true);
        }

        // Iron-banded anvil on its stump in the front working area.
        Make(PrimitiveType.Cylinder, "AnvilStump", new Vector3(-0.9f, 0.55f, 1.85f),
            new Vector3(0.55f, 0.55f, 0.55f), Quaternion.identity, beam, 0.05f, 0.15f, false);
        Make(PrimitiveType.Cube, "AnvilBody", new Vector3(-0.9f, 1.05f, 1.85f),
            new Vector3(0.85f, 0.30f, 0.40f), Quaternion.identity, iron, 0.85f, 0.50f, false);
        Make(PrimitiveType.Cube, "AnvilHorn", new Vector3(-0.45f, 1.05f, 1.85f),
            new Vector3(0.45f, 0.20f, 0.30f), Quaternion.identity, iron, 0.85f, 0.50f, false);
        Make(PrimitiveType.Cube, "AnvilWaist", new Vector3(-0.9f, 0.85f, 1.85f),
            new Vector3(0.55f, 0.15f, 0.30f), Quaternion.identity, iron * 0.85f, 0.85f, 0.50f, false);
        // Hammer leaning on the anvil.
        Make(PrimitiveType.Cylinder, "HammerHaft", new Vector3(-0.55f, 1.30f, 1.85f),
            new Vector3(0.05f, 0.45f, 0.05f), Quaternion.Euler(0f, 0f, 35f), beam, 0.10f, 0.20f, false);
        Make(PrimitiveType.Cube, "HammerHead", new Vector3(-0.30f, 1.55f, 1.85f),
            new Vector3(0.18f, 0.18f, 0.30f), Quaternion.identity, iron, 0.85f, 0.50f, false);

        // Quench barrel (water-filled wooden cask).
        Make(PrimitiveType.Cylinder, "QuenchStaves", new Vector3(0.95f, 0.85f, 1.85f),
            new Vector3(0.70f, 0.55f, 0.70f), Quaternion.identity, beam, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cylinder, "QuenchHoopT", new Vector3(0.95f, 1.30f, 1.85f),
            new Vector3(0.74f, 0.04f, 0.74f), Quaternion.identity, iron, 0.50f, 0.40f, false);
        Make(PrimitiveType.Cylinder, "QuenchHoopB", new Vector3(0.95f, 0.50f, 1.85f),
            new Vector3(0.74f, 0.04f, 0.74f), Quaternion.identity, iron, 0.50f, 0.40f, false);
        Make(PrimitiveType.Cylinder, "QuenchWater", new Vector3(0.95f, 1.36f, 1.85f),
            new Vector3(0.62f, 0.02f, 0.62f), Quaternion.identity, water, 0.10f, 0.85f, false);

        // Side bellows: triangular wood + leather, brass nozzle pointing into the furnace.
        Make(PrimitiveType.Cube, "BellowsTop", new Vector3(2.55f, 1.60f, -0.10f),
            new Vector3(0.12f, 0.20f, 1.30f), Quaternion.Euler(0f, 0f, -8f), beam, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cube, "BellowsBag", new Vector3(2.55f, 1.30f, -0.10f),
            new Vector3(0.10f, 0.55f, 1.20f), Quaternion.Euler(0f, 0f, -3f), leather, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cube, "BellowsBot", new Vector3(2.55f, 1.00f, -0.10f),
            new Vector3(0.12f, 0.20f, 1.10f), Quaternion.identity, beam, 0.05f, 0.10f, false);
        Make(PrimitiveType.Cylinder, "BellowsNozzle", new Vector3(2.20f, 1.30f, 0.55f),
            new Vector3(0.08f, 0.40f, 0.08f), Quaternion.Euler(0f, 0f, 90f), brass, 0.80f, 0.50f, false);

        // Single collider for selection / placement bounds — fitted to meshes.
        FitSelectionCollider(root);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HALL PROCEDURAL GENERATION (Age 1 — Ancient Ruins + Settler Construction)
    // ═══════════════════════════════════════════════════════════════════════

}
