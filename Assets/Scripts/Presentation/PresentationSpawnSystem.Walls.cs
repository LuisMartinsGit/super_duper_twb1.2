// PresentationSpawnSystem.Walls.cs
// Alanthor wall procedural generation (hubs, segments, instances, towers, gates)
// Walkable-rampart rework (2026-05-29): walls are battalion-wide ramparts with a
// flat deck at y=4, crenellated parapets, and external ramp "stairs" up to the
// deck on hubs / towers / gates. See docs/Design/Age_1_Alanthor.md
// § Walkable Ramparts, Stairs & Garrison.

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
    // ALANTHOR WALL PROCEDURAL GENERATION (walkable ramparts)
    // Local frame for instances/segments/towers/gates: +Z runs ALONG the wall,
    // +X is the OUTER (enemy) face, -X is the INNER (friendly) face. Hubs use
    // identity rotation (they are omnidirectional connection points).
    // ═══════════════════════════════════════════════════════════════════════

    // Shared palette for all wall pieces.
    private static readonly Color WallStone     = new Color(0.78f, 0.76f, 0.72f);   // light limestone
    private static readonly Color WallStoneDark = new Color(0.52f, 0.50f, 0.46f);   // course shadow line
    private static readonly Color WallMarble    = new Color(0.92f, 0.92f, 0.90f);   // capital / band / deck
    private static readonly Color WallIron      = new Color(0.30f, 0.30f, 0.34f);   // gate iron
    private static readonly Color WallCyan      = new Color(0.30f, 0.78f, 0.85f);   // Alanthor accent
    private static readonly Color WallWood      = new Color(0.42f, 0.28f, 0.16f);   // gate door

    // Rampart cross-section (meters) — canonical values from the design doc.
    private const float WallW          = 9f;    // total width across the wall (X)
    private const float DeckTop        = 4f;    // walkable deck surface (units stand here)
    private const float DeckThickness  = 0.4f;
    private const float BodyTop        = DeckTop - DeckThickness; // solid masonry up to 3.6
    private const float DeckWalkHalf   = 4f;    // walkable half-width (8 m deck)
    private const float OuterParapetTop = 5.4f;
    private const float InnerParapetTop = 4.9f;
    private const float ModuleLen      = 4f;    // along Z (matches AlanthorWall.InstanceSpacing)
    private const float RampWidth      = 3f;
    // 8 m run at 4 m rise → ~26.6° slope, under the navmesh agentSlope budget (30°)
    // so the bake (W2) treats the ramp as walkable rather than a cliff.
    private const float RampRun        = 8f;

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
    /// External straight ramp ("stairs") from the ground up to the deck, leaning
    /// against a structure's inner face. <paramref name="deckEdgeLocal"/> is the
    /// top of the ramp at the deck rim (y≈DeckTop); the ramp runs outward along
    /// <paramref name="outwardDirLocal"/> for RampRun meters down to the ground.
    /// Built as a single continuous slab so the navmesh bake (W2) can climb it.
    /// </summary>
    private void AddWallRamp(Transform parent, Vector3 deckEdgeLocal, Vector3 outwardDirLocal)
    {
        outwardDirLocal = outwardDirLocal.normalized;
        Vector3 groundEnd = deckEdgeLocal + outwardDirLocal * RampRun;
        groundEnd.y = 0f;

        Vector3 upSlope = (deckEdgeLocal - groundEnd);          // from ground up to deck
        float slabLen = upSlope.magnitude;
        Vector3 fwd = upSlope.normalized;
        Vector3 mid = (deckEdgeLocal + groundEnd) * 0.5f;

        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        // Cube length is along local +Z (fwd = up-slope), width along local X.
        WallPrimRot(PrimitiveType.Cube, "RampSlab", parent, mid,
            new Vector3(RampWidth, 0.30f, slabLen), rot, WallStone, smoothness: 0.2f);

        // Step ridges on top of the slab for a stair read (purely visual).
        int steps = 7;
        for (int i = 0; i < steps; i++)
        {
            float t = (i + 0.5f) / steps;
            Vector3 sp = Vector3.Lerp(groundEnd, deckEdgeLocal, t) + Vector3.up * 0.16f;
            WallPrimRot(PrimitiveType.Cube, $"Step_{i}", parent, sp,
                new Vector3(RampWidth, 0.10f, 0.35f), rot, WallStoneDark);
        }

        // Low side rails so the ramp reads as enclosed stairs.
        Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized * (RampWidth * 0.5f);
        WallPrimRot(PrimitiveType.Cube, "RampRailA", parent, mid + side + Vector3.up * 0.35f,
            new Vector3(0.18f, 0.7f, slabLen), rot, WallStone);
        WallPrimRot(PrimitiveType.Cube, "RampRailB", parent, mid - side + Vector3.up * 0.35f,
            new Vector3(0.18f, 0.7f, slabLen), rot, WallStone);
    }

    /// <summary>Outer (crenellated) + inner (waist-high) parapet running the full
    /// module length along Z, plus the marble deck slab. Shared by instance/gate/
    /// tower decks so the walkway reads continuously.</summary>
    private void AddDeckAndParapets(Transform parent, float lengthZ, bool outerCrenellations)
    {
        // Marble walkable deck slab.
        WallPrim(PrimitiveType.Cube, "Deck", parent,
            new Vector3(0f, BodyTop + DeckThickness * 0.5f, 0f),
            new Vector3(DeckWalkHalf * 2f + 0.4f, DeckThickness, lengthZ + 0.05f),
            WallMarble, smoothness: 0.45f);

        // Outer parapet base (+X edge).
        WallPrim(PrimitiveType.Cube, "OuterParapet", parent,
            new Vector3(DeckWalkHalf + 0.25f, DeckTop + 0.5f, 0f),
            new Vector3(0.5f, 1.0f, lengthZ), WallStone);

        // Inner parapet (waist-high, -X edge) so units don't read as falling off.
        WallPrim(PrimitiveType.Cube, "InnerParapet", parent,
            new Vector3(-(DeckWalkHalf + 0.20f), DeckTop + (InnerParapetTop - DeckTop) * 0.5f, 0f),
            new Vector3(0.4f, InnerParapetTop - DeckTop, lengthZ), WallStone);

        if (outerCrenellations)
        {
            int merlons = Mathf.Max(2, Mathf.RoundToInt(lengthZ / 1.3f));
            for (int i = 0; i < merlons; i++)
            {
                float zz = -lengthZ * 0.5f + lengthZ * (i + 0.5f) / merlons;
                WallPrim(PrimitiveType.Cube, $"Merlon_{i}", parent,
                    new Vector3(DeckWalkHalf + 0.30f, OuterParapetTop - 0.25f, zz),
                    new Vector3(0.55f, 0.55f, 0.6f), WallStone);
            }
        }
    }

    /// <summary>
    /// Wall instance: one 4 m-long rampart module, 9 m wide, with a solid stone
    /// body up to y=3.6, a marble walk-deck at y=4, an outer crenellated parapet
    /// and an inner waist-high parapet. Tiles seamlessly with neighbours at
    /// AlanthorWall.InstanceSpacing (= 4 m).
    /// </summary>
    private GameObject CreateProceduralWallInstance(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallInstance_{entity.Index}");
        root.transform.position = center;

        // Solid masonry body (full width, up to the deck underside).
        WallPrim(PrimitiveType.Cube, "Body", root.transform,
            new Vector3(0f, BodyTop * 0.5f, 0f),
            new Vector3(WallW, BodyTop, ModuleLen + 0.05f), WallStone);

        // Two course shadow lines on the outer + inner faces.
        WallPrim(PrimitiveType.Cube, "Course1", root.transform,
            new Vector3(0f, 1.2f, 0f), new Vector3(WallW + 0.06f, 0.06f, ModuleLen + 0.08f), WallStoneDark);
        WallPrim(PrimitiveType.Cube, "Course2", root.transform,
            new Vector3(0f, 2.4f, 0f), new Vector3(WallW + 0.06f, 0.06f, ModuleLen + 0.08f), WallStoneDark);

        AddDeckAndParapets(root.transform, ModuleLen, outerCrenellations: true);

        // Arrow slits in the outer parapet.
        WallPrim(PrimitiveType.Cube, "ArrowSlit_A", root.transform,
            new Vector3(DeckWalkHalf + 0.45f, DeckTop + 0.5f, -ModuleLen * 0.25f),
            new Vector3(0.10f, 0.45f, 0.10f), WallStoneDark);
        WallPrim(PrimitiveType.Cube, "ArrowSlit_B", root.transform,
            new Vector3(DeckWalkHalf + 0.45f, DeckTop + 0.5f, ModuleLen * 0.25f),
            new Vector3(0.10f, 0.45f, 0.10f), WallStoneDark);

        // Faction stripe banner on the outer face.
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(WallW * 0.5f + 0.03f, 1.9f, 0f),
            new Vector3(0.04f, 1.4f, 0.9f), Color.white);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(WallW, OuterParapetTop, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * (OuterParapetTop * 0.5f);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    /// <summary>
    /// Wall segment: data-only graph edge. The instances carry the visible
    /// masonry; the segment renders only a thin foundation curb so any sub-pixel
    /// gap under the module row still reads as stone.
    /// </summary>
    private GameObject CreateProceduralWallSegment(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallSegment_{entity.Index}");
        root.transform.position = center;

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

        WallPrim(PrimitiveType.Cube, "Foundation_Curb", root.transform,
            new Vector3(0f, 0.08f, 0f), new Vector3(WallW + 0.10f, 0.16f, length + 0.10f),
            WallStoneDark);

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
    /// Wall hub: a 9 m-wide drum keep that joins segments from any direction. The
    /// hub top is a full-width deck at y=4 (so adjacent segment decks meet across
    /// it); a central crenellated keep rises above the deck; an external ramp on
    /// the inner side (-X) climbs from the ground to the deck.
    /// </summary>
    private GameObject CreateProceduralWallHub(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallHub_{entity.Index}");
        root.transform.position = center;

        // Marble plinth.
        WallPrim(PrimitiveType.Cylinder, "Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f), new Vector3(WallW + 0.4f, 0.30f, WallW + 0.4f),
            WallMarble, smoothness: 0.5f);

        // Solid drum body up to the deck underside.
        WallPrim(PrimitiveType.Cylinder, "Drum", root.transform,
            new Vector3(0f, BodyTop * 0.5f + 0.2f, 0f),
            new Vector3(WallW, BodyTop, WallW), WallStone);
        WallPrim(PrimitiveType.Cylinder, "DrumCourse", root.transform,
            new Vector3(0f, 2.2f, 0f), new Vector3(WallW + 0.05f, 0.06f, WallW + 0.05f), WallStoneDark);

        // Full-width walkable deck (square so it meets segment decks on any side).
        WallPrim(PrimitiveType.Cube, "Deck", root.transform,
            new Vector3(0f, BodyTop + DeckThickness * 0.5f, 0f),
            new Vector3(WallW, DeckThickness, WallW), WallMarble, smoothness: 0.45f);

        // Central crenellated keep above the deck (units walk the ring around it).
        WallPrim(PrimitiveType.Cylinder, "Keep", root.transform,
            new Vector3(0f, DeckTop + 0.9f, 0f), new Vector3(3.4f, 1.8f, 3.4f), WallStone);
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"Merlon_{i}", root.transform,
                new Vector3(Mathf.Cos(a) * 1.9f, DeckTop + 1.9f, Mathf.Sin(a) * 1.9f),
                new Vector3(0.45f, 0.5f, 0.45f), Quaternion.Euler(0f, i * 45f, 0f), WallStone);
        }

        // External ramp up to the deck on the -X (inner) side.
        AddWallRamp(root.transform, new Vector3(-(WallW * 0.5f - 0.5f), DeckTop, 0f), new Vector3(-1f, 0f, 0f));

        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0f, DeckTop + 0.9f, 1.75f), new Vector3(0.7f, 1.3f, 0.04f), Color.white);
        AddWallNightLight(root.transform, new Vector3(0f, DeckTop + 2.2f, 0f), 1.8f, 9f);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(WallW, DeckTop + 2.2f, WallW);
        boxCol.center = Vector3.up * ((DeckTop + 2.2f) * 0.5f);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
    }

    /// <summary>
    /// Wall tower: a converted instance — same 9 m rampart body + deck, but with a
    /// taller crenellated archer turret rising above the deck on the outer half,
    /// and an inner-side ramp up to the deck.
    /// </summary>
    private GameObject CreateProceduralWallTower(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallTower_{entity.Index}");
        root.transform.position = center;

        WallPrim(PrimitiveType.Cube, "Body", root.transform,
            new Vector3(0f, BodyTop * 0.5f, 0f),
            new Vector3(WallW, BodyTop, ModuleLen + 0.05f), WallStone);

        AddDeckAndParapets(root.transform, ModuleLen, outerCrenellations: false);

        // Archer turret on the outer half of the deck.
        WallPrim(PrimitiveType.Cylinder, "Turret", root.transform,
            new Vector3(DeckWalkHalf * 0.45f, DeckTop + 1.1f, 0f),
            new Vector3(2.6f, 2.2f, 2.6f), WallStone);
        WallPrim(PrimitiveType.Cylinder, "TurretCap", root.transform,
            new Vector3(DeckWalkHalf * 0.45f, DeckTop + 2.3f, 0f),
            new Vector3(2.9f, 0.16f, 2.9f), WallMarble, smoothness: 0.5f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"Merlon_{i}", root.transform,
                new Vector3(DeckWalkHalf * 0.45f + Mathf.Cos(a) * 1.4f, DeckTop + 2.6f, Mathf.Sin(a) * 1.4f),
                new Vector3(0.4f, 0.5f, 0.4f), Quaternion.Euler(0f, i * 45f, 0f), WallStone);
        }
        // Archer slits around the turret.
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"Slit_{i}", root.transform,
                new Vector3(DeckWalkHalf * 0.45f + Mathf.Cos(a) * 1.35f, DeckTop + 1.1f, Mathf.Sin(a) * 1.35f),
                new Vector3(0.1f, 0.6f, 0.1f), Quaternion.Euler(0f, i * 90f, 0f), WallStoneDark);
        }

        AddWallRamp(root.transform, new Vector3(-(DeckWalkHalf), DeckTop, 0f), new Vector3(-1f, 0f, 0f));

        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(WallW * 0.5f + 0.03f, 2.3f, 0f), new Vector3(0.04f, 1.5f, 0.9f), Color.white);
        AddWallNightLight(root.transform, new Vector3(DeckWalkHalf * 0.45f, DeckTop + 2.9f, 0f), 2.0f, 9f);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(WallW, DeckTop + 2.9f, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * ((DeckTop + 2.9f) * 0.5f);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    /// <summary>
    /// Wall gate: a rampart module with a ground-level tunnel cut through the wall
    /// (units pass across X, from one side to the other). The deck bridges over the
    /// tunnel at y=4 so the rampart walk stays continuous; a wooden gate door on
    /// the outer mouth bars hostiles; an inner-side ramp climbs to the deck.
    /// </summary>
    private GameObject CreateProceduralWallGate(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallGate_{entity.Index}");
        root.transform.position = center;

        const float jambHalf = ModuleLen * 0.5f; // gate spans one module along Z
        const float openHalfZ = 1.6f;            // tunnel opening half-length along Z

        // Tunnel jambs at the Z ends (solid body that the deck rests on).
        for (int side = -1; side <= 1; side += 2)
        {
            float cz = side * (jambHalf + openHalfZ) * 0.5f;
            float lz = jambHalf - openHalfZ;
            if (lz < 0.4f) lz = 0.4f;
            WallPrim(PrimitiveType.Cube, side < 0 ? "JambL" : "JambR", root.transform,
                new Vector3(0f, BodyTop * 0.5f, cz),
                new Vector3(WallW, BodyTop, lz + 0.05f), WallStone);
        }

        // Lintel beam over the opening (supports the deck across the tunnel mouth).
        WallPrim(PrimitiveType.Cube, "Lintel", root.transform,
            new Vector3(0f, BodyTop - 0.4f, 0f), new Vector3(WallW, 0.8f, openHalfZ * 2f), WallStone);

        // Deck bridges the full module over the tunnel.
        AddDeckAndParapets(root.transform, ModuleLen, outerCrenellations: true);

        // Wooden gate door on the OUTER mouth of the tunnel (blocks hostiles).
        WallPrim(PrimitiveType.Cube, "GateDoor", root.transform,
            new Vector3(WallW * 0.5f - 0.4f, 1.5f, 0f),
            new Vector3(0.25f, 3.0f, openHalfZ * 2f - 0.2f), WallWood, smoothness: 0.2f);
        for (int i = 0; i < 4; i++)
        {
            float yy = 0.6f + i * 0.7f;
            WallPrim(PrimitiveType.Sphere, $"Stud_{i}", root.transform,
                new Vector3(WallW * 0.5f - 0.27f, yy, 0f), Vector3.one * 0.12f, WallIron, metallic: 0.7f);
        }

        // Inner-side ramp up to the deck (local z=0 to match the navmesh ramp source).
        AddWallRamp(root.transform, new Vector3(-(DeckWalkHalf), DeckTop, 0f), new Vector3(-1f, 0f, 0f));

        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(WallW * 0.5f + 0.03f, 2.3f, 0f), new Vector3(0.04f, 1.4f, 0.9f), Color.white);
        AddWallNightLight(root.transform, new Vector3(WallW * 0.5f - 0.4f, DeckTop + 1.0f, 0f), 1.6f, 7f);

        // Collider: omit the tunnel volume so unit selection/raycast doesn't block
        // the passage — two jamb colliders instead of one big box.
        for (int side = -1; side <= 1; side += 2)
        {
            float cz = side * (jambHalf + openHalfZ) * 0.5f;
            float lz = jambHalf - openHalfZ; if (lz < 0.4f) lz = 0.4f;
            var jc = new GameObject(side < 0 ? "ColL" : "ColR");
            jc.transform.SetParent(root.transform, false);
            jc.transform.localPosition = new Vector3(0f, OuterParapetTop * 0.5f, cz);
            var bc = jc.AddComponent<BoxCollider>();
            bc.size = new Vector3(WallW, OuterParapetTop, lz + 0.05f);
        }

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }
}
