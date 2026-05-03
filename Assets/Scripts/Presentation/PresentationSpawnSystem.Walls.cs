// PresentationSpawnSystem.Walls.cs
// Alanthor wall procedural generation (hubs, segments, instances, towers, gates)
// Extracted from PresentationSpawnSystem.cs — Fix #204

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.Input;          // EntityReference
using TheWaningBorder.Presentation;   // EntityViewManager

public partial class PresentationSpawnSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // ALANTHOR WALL PROCEDURAL GENERATION
    // Aesthetic: white-stone Alanthor masonry — courses of cut stone, marble
    // accents, crenellations, cyan-tinted night lights, faction stripe banner.
    // ═══════════════════════════════════════════════════════════════════════

    // Shared palette for all wall pieces.
    private static readonly Color WallStone     = new Color(0.78f, 0.76f, 0.72f);   // light limestone
    private static readonly Color WallStoneDark = new Color(0.52f, 0.50f, 0.46f);   // course shadow line
    private static readonly Color WallMarble    = new Color(0.92f, 0.92f, 0.90f);   // capital / band
    private static readonly Color WallIron      = new Color(0.30f, 0.30f, 0.34f);   // gate iron
    private static readonly Color WallCyan      = new Color(0.30f, 0.78f, 0.85f);   // Alanthor accent
    private static readonly Color WallWood      = new Color(0.42f, 0.28f, 0.16f);   // gate door

    private static GameObject WallPrim(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 localScale, Color color, float metallic = 0f, float smoothness = 0.3f)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.color = color;
            if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            r.material = mat;
        }
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        return go;
    }

    private static GameObject WallPrimRot(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 localScale, Quaternion localRot, Color color, float metallic = 0f, float smoothness = 0.3f)
    {
        var go = WallPrim(type, name, parent, localPos, localScale, color, metallic, smoothness);
        go.transform.localRotation = localRot;
        return go;
    }

    private static void AddWallNightLight(Transform parent, Vector3 localPos, float intensity, float range)
    {
        var lightGo = new GameObject("WallLight");
        lightGo.transform.SetParent(parent, false);
        lightGo.transform.localPosition = localPos;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = WallCyan;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        // Emissive bulb so the lamp reads in daylight too.
        var bulb = WallPrim(PrimitiveType.Sphere, "Bulb", lightGo.transform,
            Vector3.zero, Vector3.one * 0.18f, WallCyan);
        var rend = bulb.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = rend.material;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", WallCyan * 1.8f);
            }
        }
    }

    /// <summary>
    /// Wall hub: tower-like connection point. Stone drum on a marble plinth, with
    /// a banded marble cap, eight crenellation merlons, and a faction-coloured
    /// banner (Stripe_*) on the side.
    /// </summary>
    private GameObject CreateProceduralWallHub(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallHub_{entity.Index}");
        root.transform.position = center;

        // Plinth (marble) — slightly wider than the drum so it reads as a base.
        WallPrim(PrimitiveType.Cylinder, "Plinth", root.transform,
            new Vector3(0f, 0.20f, 0f), new Vector3(1.55f, 0.20f, 1.55f),
            WallMarble, smoothness: 0.5f);

        // Stone drum — three stacked sections give visible courses.
        WallPrim(PrimitiveType.Cylinder, "DrumLow", root.transform,
            new Vector3(0f, 0.85f, 0f), new Vector3(1.20f, 0.55f, 1.20f), WallStone);
        WallPrim(PrimitiveType.Cylinder, "DrumCourseLine1", root.transform,
            new Vector3(0f, 1.42f, 0f), new Vector3(1.24f, 0.04f, 1.24f), WallStoneDark);
        WallPrim(PrimitiveType.Cylinder, "DrumMid", root.transform,
            new Vector3(0f, 2.00f, 0f), new Vector3(1.20f, 0.55f, 1.20f), WallStone);
        WallPrim(PrimitiveType.Cylinder, "DrumCourseLine2", root.transform,
            new Vector3(0f, 2.58f, 0f), new Vector3(1.24f, 0.04f, 1.24f), WallStoneDark);
        WallPrim(PrimitiveType.Cylinder, "DrumHigh", root.transform,
            new Vector3(0f, 3.10f, 0f), new Vector3(1.20f, 0.50f, 1.20f), WallStone);

        // Marble cap and band so the silhouette reads as Alanthor masonry.
        WallPrim(PrimitiveType.Cylinder, "Cap", root.transform,
            new Vector3(0f, 3.65f, 0f), new Vector3(1.40f, 0.10f, 1.40f),
            WallMarble, smoothness: 0.5f);

        // Eight merlons around the rim (crenellations).
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            float ox = Mathf.Cos(a) * 0.62f;
            float oz = Mathf.Sin(a) * 0.62f;
            WallPrimRot(PrimitiveType.Cube, $"Merlon_{i}", root.transform,
                new Vector3(ox, 3.95f, oz), new Vector3(0.30f, 0.45f, 0.30f),
                Quaternion.Euler(0f, i * 45f, 0f), WallStone);
        }

        // Faction stripe banner hanging on the front face.
        // ApplyFactionColor will tint anything named "Stripe_*" with the player colour.
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0f, 2.10f, 0.62f),
            new Vector3(0.55f, 1.30f, 0.04f), Color.white);

        // A small cyan night light at the cap.
        AddWallNightLight(root.transform, new Vector3(0f, 4.10f, 0f), 1.4f, 7f);

        // Single collider on root.
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.7f, 4.4f, 1.7f);
        boxCol.center = Vector3.up * 2.0f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
    }

    /// <summary>
    /// Wall segment connecting two hubs. The instance entities spawned along the
    /// segment carry the visible masonry; the segment itself is rendered as an
    /// almost-flat curb so it reads as a foundation under the row.
    /// </summary>
    private GameObject CreateProceduralWallSegment(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallSegment_{entity.Index}");
        root.transform.position = center;

        // Length from WallConnection.
        float length = 5f;
        if (_em.HasComponent<WallConnection>(entity))
        {
            var conn = _em.GetComponentData<WallConnection>(entity);
            if (_em.Exists(conn.HubA) && _em.Exists(conn.HubB) &&
                _em.HasComponent<Unity.Transforms.LocalTransform>(conn.HubA) &&
                _em.HasComponent<Unity.Transforms.LocalTransform>(conn.HubB))
            {
                var posA = _em.GetComponentData<Unity.Transforms.LocalTransform>(conn.HubA).Position;
                var posB = _em.GetComponentData<Unity.Transforms.LocalTransform>(conn.HubB).Position;
                length = math.distance(new float2(posA.x, posA.z), new float2(posB.x, posB.z));
            }
        }

        // Foundation curb under the row of instances (low and slightly oversized).
        WallPrim(PrimitiveType.Cube, "Foundation_Curb", root.transform,
            new Vector3(0f, 0.10f, 0f), new Vector3(1.10f, 0.20f, length + 0.10f),
            WallStoneDark);

        // Thin marble plinth course visible above the curb.
        WallPrim(PrimitiveType.Cube, "PlinthBand", root.transform,
            new Vector3(0f, 0.30f, 0f), new Vector3(0.95f, 0.10f, length + 0.05f),
            WallMarble, smoothness: 0.4f);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.10f, 0.40f, length + 0.10f);
        boxCol.center = Vector3.up * 0.20f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    public void ForceRespawn(Entity entity)
    {
        if (EntityViewManager.Instance != null &&
            EntityViewManager.Instance.TryGetView(entity, out var oldGo) && oldGo != null)
        {
            EntityViewManager.Instance.UnregisterView(entity);
            Destroy(oldGo);
        }
        _spawnedEntities.Remove(entity);
    }

    /// <summary>
    /// Wall instance: a 2m-long, ~3m-tall stone module that tiles seamlessly
    /// with neighbours at AlanthorWall.InstanceSpacing = 2m. Stacked stone
    /// courses with alternating x-offsets, marble capstone, two pairs of
    /// merlons, arrow slits, and a faction-stripe pennant.
    /// </summary>
    private GameObject CreateProceduralWallInstance(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallInstance_{entity.Index}");
        root.transform.position = center;

        const float WallLen = 2.0f; // matches AlanthorWall.InstanceSpacing

        // Three stone courses — alternating x-offsets to look like masonry blocks.
        WallPrim(PrimitiveType.Cube, "Course1", root.transform,
            new Vector3(-0.05f, 0.45f, 0f), new Vector3(0.55f, 0.85f, WallLen), WallStone);
        WallPrim(PrimitiveType.Cube, "Course2", root.transform,
            new Vector3( 0.05f, 1.30f, 0f), new Vector3(0.55f, 0.85f, WallLen), WallStone);
        WallPrim(PrimitiveType.Cube, "Course3", root.transform,
            new Vector3(-0.05f, 2.15f, 0f), new Vector3(0.55f, 0.85f, WallLen), WallStone);

        // Course shadow lines (horizontal seams).
        WallPrim(PrimitiveType.Cube, "Seam1", root.transform,
            new Vector3(0f, 0.90f, 0f), new Vector3(0.62f, 0.04f, WallLen + 0.04f), WallStoneDark);
        WallPrim(PrimitiveType.Cube, "Seam2", root.transform,
            new Vector3(0f, 1.75f, 0f), new Vector3(0.62f, 0.04f, WallLen + 0.04f), WallStoneDark);

        // Marble capstone running the full length.
        WallPrim(PrimitiveType.Cube, "Capstone", root.transform,
            new Vector3(0f, 2.65f, 0f), new Vector3(0.70f, 0.18f, WallLen + 0.05f),
            WallMarble, smoothness: 0.5f);

        // Four merlons on top of the capstone (two pairs along the wall axis).
        for (int i = 0; i < 2; i++)
        {
            float zz = (i == 0 ? -0.55f : 0.55f);
            WallPrim(PrimitiveType.Cube, $"Merlon_F_{i}", root.transform,
                new Vector3(-0.15f, 2.92f, zz), new Vector3(0.38f, 0.40f, 0.30f), WallStone);
            WallPrim(PrimitiveType.Cube, $"Merlon_B_{i}", root.transform,
                new Vector3( 0.15f, 2.92f, zz), new Vector3(0.38f, 0.40f, 0.30f), WallStone);
        }

        // Two arrow slits along the wall length on the front face.
        WallPrim(PrimitiveType.Cube, "ArrowSlit_A", root.transform,
            new Vector3(0.30f, 1.55f, -0.50f), new Vector3(0.05f, 0.45f, 0.10f), WallStoneDark);
        WallPrim(PrimitiveType.Cube, "ArrowSlit_B", root.transform,
            new Vector3(0.30f, 1.55f,  0.50f), new Vector3(0.05f, 0.45f, 0.10f), WallStoneDark);

        // Small pennant strip (faction stripe) centred on the front face.
        WallPrim(PrimitiveType.Cube, "Stripe_Pennant", root.transform,
            new Vector3(0.32f, 1.10f, 0f), new Vector3(0.04f, 0.50f, 0.30f), Color.white);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(0.80f, 3.20f, WallLen + 0.10f);
        boxCol.center = Vector3.up * 1.55f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    /// <summary>
    /// Wall tower: taller crenellated drum on a marble plinth, with archer ports,
    /// 8 merlons and a banner.
    /// </summary>
    private GameObject CreateProceduralWallTower(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallTower_{entity.Index}");
        root.transform.position = center;

        // Plinth.
        WallPrim(PrimitiveType.Cylinder, "Plinth", root.transform,
            new Vector3(0f, 0.20f, 0f), new Vector3(2.0f, 0.20f, 2.0f),
            WallMarble, smoothness: 0.5f);

        // Wall stubs poking out either side along the wall axis (Z) so the
        // tower visually connects to adjacent regular wall instances. The stub
        // matches a standard wall instance's stone-course look but extends only
        // halfway out so it overlaps the neighbour's edge.
        for (int side = -1; side <= 1; side += 2)
        {
            string s = side < 0 ? "S" : "N";
            float z0 = side * 0.60f; // start just outside the drum
            float zLen = 1.10f;       // reach into the neighbour instance
            float cz = z0 + side * (zLen * 0.5f);
            WallPrim(PrimitiveType.Cube, $"Stub_{s}_C1", root.transform,
                new Vector3(-0.05f, 0.55f, cz), new Vector3(0.55f, 1.05f, zLen), WallStone);
            WallPrim(PrimitiveType.Cube, $"Stub_{s}_C2", root.transform,
                new Vector3( 0.05f, 1.60f, cz), new Vector3(0.55f, 1.05f, zLen), WallStone);
            WallPrim(PrimitiveType.Cube, $"Stub_{s}_Cap", root.transform,
                new Vector3(0f, 2.65f, cz), new Vector3(0.70f, 0.18f, zLen + 0.05f),
                WallMarble, smoothness: 0.5f);
            WallPrim(PrimitiveType.Cube, $"Stub_{s}_Seam", root.transform,
                new Vector3(0f, 1.10f, cz), new Vector3(0.62f, 0.04f, zLen + 0.04f), WallStoneDark);
        }

        // Stone drum in two visible courses.
        WallPrim(PrimitiveType.Cylinder, "TowerLow", root.transform,
            new Vector3(0f, 1.20f, 0f), new Vector3(1.60f, 0.95f, 1.60f), WallStone);
        WallPrim(PrimitiveType.Cylinder, "TowerCourseLine", root.transform,
            new Vector3(0f, 2.18f, 0f), new Vector3(1.64f, 0.05f, 1.64f), WallStoneDark);
        WallPrim(PrimitiveType.Cylinder, "TowerHigh", root.transform,
            new Vector3(0f, 3.20f, 0f), new Vector3(1.60f, 0.95f, 1.60f), WallStone);

        // 4 archer slits (cardinal directions, on the upper drum).
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"ArrowSlit_{i}", root.transform,
                new Vector3(Mathf.Cos(a) * 0.85f, 3.20f, Mathf.Sin(a) * 0.85f),
                new Vector3(0.10f, 0.55f, 0.10f),
                Quaternion.Euler(0f, i * 90f, 0f), WallStoneDark);
        }

        // Marble cap platform.
        WallPrim(PrimitiveType.Cylinder, "Cap", root.transform,
            new Vector3(0f, 4.20f, 0f), new Vector3(2.05f, 0.18f, 2.05f),
            WallMarble, smoothness: 0.5f);
        // Iron platform deck on top of the cap.
        WallPrim(PrimitiveType.Cylinder, "Deck", root.transform,
            new Vector3(0f, 4.42f, 0f), new Vector3(1.85f, 0.06f, 1.85f),
            WallIron, metallic: 0.5f);

        // 8 crenellation merlons around the cap.
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"Merlon_{i}", root.transform,
                new Vector3(Mathf.Cos(a) * 0.95f, 4.75f, Mathf.Sin(a) * 0.95f),
                new Vector3(0.40f, 0.50f, 0.40f),
                Quaternion.Euler(0f, i * 45f, 0f), WallStone);
        }

        // Flag pole + faction stripe pennant on top.
        WallPrim(PrimitiveType.Cylinder, "FlagPole", root.transform,
            new Vector3(0f, 5.40f, 0f), new Vector3(0.06f, 0.60f, 0.06f), WallIron, metallic: 0.5f);
        WallPrim(PrimitiveType.Cube, "Stripe_Pennant", root.transform,
            new Vector3(0.30f, 5.70f, 0f), new Vector3(0.55f, 0.30f, 0.04f), Color.white);

        // Big faction banner on the front face.
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0f, 2.40f, 0.85f), new Vector3(0.65f, 1.40f, 0.04f), Color.white);

        // Cyan apex light.
        AddWallNightLight(root.transform, new Vector3(0f, 5.10f, 0f), 2.2f, 9f);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(2.2f, 5.6f, 2.2f);
        boxCol.center = Vector3.up * 2.7f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
    }

    /// <summary>
    /// Wall gate: two stone pillars on a marble plinth with a wooden gate door,
    /// iron-banded lintel, crenellated parapet on top, hanging faction banner.
    ///
    /// Wall axis is the entity's local +Z (LookRotationSafe of dirFlat). Pillars
    /// straddle that axis so they integrate with the wall row, and the door and
    /// archway open across the wall (local X) — i.e. traffic passes perpendicular
    /// to the wall, walking from one side to the other through the gate.
    /// </summary>
    private GameObject CreateProceduralWallGate(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallGate_{entity.Index}");
        root.transform.position = center;

        // Marble plinth — narrow across the wall (X), spanning along it (Z) so it
        // matches the footprint of a wall instance.
        WallPrim(PrimitiveType.Cube, "Plinth", root.transform,
            new Vector3(0f, 0.10f, 0f), new Vector3(1.20f, 0.20f, 2.20f),
            WallMarble, smoothness: 0.5f);

        // Two stone pillars in three courses each, set at the ENDS of the wall
        // slot (along Z) so they continue the wall row instead of sticking out
        // perpendicular to it.
        for (int side = -1; side <= 1; side += 2)
        {
            string s = side < 0 ? "L" : "R";
            float z = 0.85f * side;
            WallPrim(PrimitiveType.Cube, $"Pillar{s}_C1", root.transform,
                new Vector3(0f, 0.85f, z), new Vector3(0.85f, 1.10f, 0.55f), WallStone);
            WallPrim(PrimitiveType.Cube, $"Pillar{s}_Seam1", root.transform,
                new Vector3(0f, 1.42f, z), new Vector3(0.90f, 0.04f, 0.60f), WallStoneDark);
            WallPrim(PrimitiveType.Cube, $"Pillar{s}_C2", root.transform,
                new Vector3(0f, 2.00f, z), new Vector3(0.85f, 1.10f, 0.55f), WallStone);
            WallPrim(PrimitiveType.Cube, $"Pillar{s}_Seam2", root.transform,
                new Vector3(0f, 2.58f, z), new Vector3(0.90f, 0.04f, 0.60f), WallStoneDark);
            WallPrim(PrimitiveType.Cube, $"Pillar{s}_C3", root.transform,
                new Vector3(0f, 3.10f, z), new Vector3(0.85f, 1.00f, 0.55f), WallStone);
        }

        // Wooden gate door spanning the opening between the pillars. Thin in X
        // (so it acts as the wall's barrier in that direction) and tall in Y.
        WallPrim(PrimitiveType.Cube, "GateDoor", root.transform,
            new Vector3(0f, 1.55f, 0f), new Vector3(0.20f, 2.80f, 1.10f), WallWood, smoothness: 0.2f);
        // Iron studs on the gate (4 + 4, on each face).
        for (int i = 0; i < 4; i++)
        {
            float yy = 0.55f + i * 0.65f;
            WallPrim(PrimitiveType.Sphere, $"Stud_F_{i}", root.transform,
                new Vector3(0.12f, yy, -0.35f), new Vector3(0.10f, 0.10f, 0.10f),
                WallIron, metallic: 0.7f);
            WallPrim(PrimitiveType.Sphere, $"Stud_B_{i}", root.transform,
                new Vector3(0.12f, yy,  0.35f), new Vector3(0.10f, 0.10f, 0.10f),
                WallIron, metallic: 0.7f);
        }

        // Iron-banded stone lintel above the gate, oriented along the wall (Z).
        WallPrim(PrimitiveType.Cube, "Lintel", root.transform,
            new Vector3(0f, 3.30f, 0f), new Vector3(0.95f, 0.40f, 2.30f),
            WallStone);
        WallPrim(PrimitiveType.Cube, "LintelIronF", root.transform,
            new Vector3( 0.50f, 3.30f, 0f), new Vector3(0.06f, 0.30f, 2.30f),
            WallIron, metallic: 0.6f);
        WallPrim(PrimitiveType.Cube, "LintelIronB", root.transform,
            new Vector3(-0.50f, 3.30f, 0f), new Vector3(0.06f, 0.30f, 2.30f),
            WallIron, metallic: 0.6f);

        // Crenellated parapet on top of the lintel (5 merlons spaced along Z).
        for (int i = 0; i < 5; i++)
        {
            float zz = -0.92f + i * 0.46f;
            WallPrim(PrimitiveType.Cube, $"Merlon_{i}", root.transform,
                new Vector3(0f, 3.80f, zz), new Vector3(0.85f, 0.45f, 0.30f), WallStone);
        }

        // Hanging faction banner on the front face (perpendicular to wall axis).
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0.51f, 2.55f, 0f), new Vector3(0.04f, 1.20f, 0.80f), Color.white);

        // Two cyan flank lights at the pillar tops so the gate reads at night.
        AddWallNightLight(root.transform, new Vector3(0.55f, 3.30f, -0.85f), 1.6f, 6f);
        AddWallNightLight(root.transform, new Vector3(0.55f, 3.30f,  0.85f), 1.6f, 6f);

        // Collider on root — slim in X (wall depth) and long in Z (wall axis).
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.10f, 4.30f, 2.30f);
        boxCol.center = Vector3.up * 2.0f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }
}
