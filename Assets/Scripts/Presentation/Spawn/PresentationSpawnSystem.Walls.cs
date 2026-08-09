// PresentationSpawnSystem.Walls.cs
// Alanthor wall procedural generation (hubs, segments, instances, towers, gates)
// Compact-wall rework (2026-08-09): walls are solid curtain walls — a 1 m-thick
// masonry line with a crenellated crown at ~2.6 m. The walkable deck, ramps and
// garrison geometry of the 2026-05-29 rampart rework are gone from the visuals;
// sim-side wall-top layer contracts (LayeredMoveSystem / WallGarrisonSystem)
// are untouched and keep compiling against their own constants.

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
    // ALANTHOR WALL PROCEDURAL GENERATION (compact curtain walls)
    // Local frame for instances/towers/gates: +Z runs ALONG the wall,
    // +X is the OUTER (enemy) face, -X is the INNER (friendly) face. Hubs use
    // identity rotation (they are omnidirectional connection points).
    // ═══════════════════════════════════════════════════════════════════════

    // Shared palette for all wall pieces.
    private static readonly Color WallStone     = new Color(0.78f, 0.76f, 0.72f);   // light limestone
    private static readonly Color WallStoneDark = new Color(0.52f, 0.50f, 0.46f);   // course shadow line
    private static readonly Color WallMarble    = new Color(0.92f, 0.92f, 0.90f);   // coping / caps
    private static readonly Color WallIron      = new Color(0.30f, 0.30f, 0.34f);   // gate iron
    private static readonly Color WallBrass     = new Color(0.72f, 0.55f, 0.24f);   // finial accents
    private static readonly Color WallCyan      = new Color(0.30f, 0.78f, 0.85f);   // Alanthor accent
    private static readonly Color WallWood      = new Color(0.42f, 0.28f, 0.16f);   // gate door
    private static readonly Color WallEmber     = new Color(1.00f, 0.45f, 0.12f);   // tower brazier

    // Compact curtain-wall cross-section (meters). Values mirror
    // AlanthorWall.WallWidth / WallHeight / InstanceSpacing / HubWidth.
    private const float WallThick  = 1f;    // masonry thickness across the wall (X)
    private const float BodyTop    = 2.0f;  // solid masonry top
    private const float CrownTop   = 2.6f;  // merlon crown top (= AlanthorWall.WallHeight)
    private const float ModuleLen  = 3f;    // along Z (= AlanthorWall.InstanceSpacing)
    private const float HubSize    = 3f;    // hub footprint (= AlanthorWall.HubWidth)

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
        MakeWallPartEmissive(bulb, WallCyan, 1.8f);
    }

    /// <summary>Enables the _EMISSION keyword on an already-created wall part.
    /// Palette discipline: at most 1-2 emissive accents per wall visual.</summary>
    private static void MakeWallPartEmissive(GameObject part, Color color, float intensity)
    {
        var rend = part.GetComponent<Renderer>();
        if (rend == null) return;
        var mat = rend.material;
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * intensity);
        }
    }

    /// <summary>Crenellated merlon row along Z at the given crown base height.
    /// One merlon per meter (module rhythm), inset from the ends so the pattern
    /// continues seamlessly across 3 m module boundaries.</summary>
    private void AddWallMerlonRow(Transform parent, string prefix, float lengthZ,
        float crownBaseY, float width, Color color, System.Random rng = null)
    {
        int merlons = Mathf.Max(2, Mathf.RoundToInt(lengthZ));
        for (int i = 0; i < merlons; i++)
        {
            float zz = -lengthZ * 0.5f + lengthZ * (i + 0.5f) / merlons;
            float h = 0.42f;
            Quaternion rot = Quaternion.identity;
            if (rng != null)
            {
                h += ((float)rng.NextDouble() * 2f - 1f) * 0.06f;
                rot = Quaternion.Euler(
                    ((float)rng.NextDouble() * 2f - 1f) * 1.2f, 0f,
                    ((float)rng.NextDouble() * 2f - 1f) * 1.2f);
            }
            WallPrimRot(PrimitiveType.Cube, $"{prefix}_{(char)('A' + i)}", parent,
                new Vector3(0f, crownBaseY + h * 0.5f, zz),
                new Vector3(width, h, 0.6f), rot, color);
        }
    }

    /// <summary>
    /// Wall instance: one 3 m-long, 1 m-thick solid curtain module. Base plinth,
    /// masonry body with a course shadow line, overhanging coping stones and a
    /// merlon crown to ~2.6 m. Deterministic per-instance jitter (entity.Index)
    /// keeps long runs from reading as extruded copies while the plinth / body /
    /// coping cross-section stays exact so modules tile seamlessly along Z at
    /// AlanthorWall.InstanceSpacing (= 3 m).
    /// </summary>
    private GameObject CreateProceduralWallInstance(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallInstance_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index);
        float bodyShade = (float)rng.NextDouble() * 0.12f;
        Color bodyTint = Color.Lerp(WallStone, WallStoneDark, bodyShade);

        // Base plinth (slightly proud of the body).
        WallPrim(PrimitiveType.Cube, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f),
            new Vector3(WallThick + 0.30f, 0.30f, ModuleLen + 0.05f), WallStoneDark);

        // Solid masonry body up to the coping underside.
        WallPrim(PrimitiveType.Cube, "2_Body", root.transform,
            new Vector3(0f, 0.30f + (BodyTop - 0.30f) * 0.5f, 0f),
            new Vector3(WallThick, BodyTop - 0.30f, ModuleLen + 0.05f), bodyTint);

        // Course shadow line.
        WallPrim(PrimitiveType.Cube, "3_Course", root.transform,
            new Vector3(0f, 1.05f, 0f),
            new Vector3(WallThick + 0.06f, 0.05f, ModuleLen + 0.08f), WallStoneDark);

        // Overhanging coping stones.
        WallPrim(PrimitiveType.Cube, "4_Coping", root.transform,
            new Vector3(0f, BodyTop + 0.075f, 0f),
            new Vector3(WallThick + 0.26f, 0.15f, ModuleLen + 0.05f), WallMarble, smoothness: 0.45f);

        // Merlon crown to ~2.6 m (jittered heights / tilts, exact positions).
        AddWallMerlonRow(root.transform, "5_Merlon", ModuleLen,
            BodyTop + 0.15f, 0.80f, bodyTint, rng);

        // Faction stripe banner on the outer face.
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(WallThick * 0.5f + 0.03f, 1.45f, 0f),
            new Vector3(0.05f, 0.70f, 0.50f), Color.white);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(WallThick + 0.3f, CrownTop, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * (CrownTop * 0.5f);

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
            new Vector3(0f, 0.08f, 0f), new Vector3(WallThick + 0.20f, 0.16f, length + 0.10f),
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
    /// Wall hub: a squat 3x3 square bastion that joins curtain segments from any
    /// direction. Corner buttresses with marble caps, an overhanging crown ledge
    /// with a crenellated ring, and a faction banner on a central pole.
    /// Omnidirectional — identity rotation.
    /// </summary>
    private GameObject CreateProceduralWallHub(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallHub_{entity.Index}");
        root.transform.position = center;

        // Base plinth.
        WallPrim(PrimitiveType.Cube, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f), new Vector3(HubSize + 0.50f, 0.30f, HubSize + 0.50f),
            WallStoneDark);

        // Solid bastion body.
        WallPrim(PrimitiveType.Cube, "2_Bastion", root.transform,
            new Vector3(0f, 1.65f, 0f), new Vector3(HubSize, 2.70f, HubSize), WallStone);

        // Course shadow band.
        WallPrim(PrimitiveType.Cube, "3_Course", root.transform,
            new Vector3(0f, 1.10f, 0f), new Vector3(HubSize + 0.08f, 0.06f, HubSize + 0.08f),
            WallStoneDark);

        // Corner buttresses, slightly proud, with marble caps.
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            char id = (char)('A' + i);
            WallPrim(PrimitiveType.Cube, $"4_Buttress_{id}", root.transform,
                new Vector3(sx * 1.42f, 1.30f, sz * 1.42f),
                new Vector3(0.60f, 2.00f, 0.60f), WallStone);
            WallPrim(PrimitiveType.Cube, $"4_ButtressCap_{id}", root.transform,
                new Vector3(sx * 1.42f, 2.36f, sz * 1.42f),
                new Vector3(0.70f, 0.12f, 0.70f), WallMarble, smoothness: 0.45f);
        }

        // Overhanging crown ledge.
        WallPrim(PrimitiveType.Cube, "5_Crown", root.transform,
            new Vector3(0f, 3.10f, 0f), new Vector3(HubSize + 0.40f, 0.20f, HubSize + 0.40f),
            WallMarble, smoothness: 0.45f);

        // Crenellated ring: edge midpoints + corners.
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            float rad = (i % 2 == 0) ? 1.40f : 1.40f * 1.4142f; // corners pushed to the diagonal
            WallPrimRot(PrimitiveType.Cube, $"6_Merlon_{(char)('A' + i)}", root.transform,
                new Vector3(Mathf.Cos(a) * rad, 3.45f, Mathf.Sin(a) * rad),
                new Vector3(0.55f, 0.50f, 0.55f), Quaternion.Euler(0f, i * 45f, 0f), WallStone);
        }

        // Central banner pole with brass finial and the faction banner cloth.
        WallPrim(PrimitiveType.Cylinder, "7_BannerPole", root.transform,
            new Vector3(0f, 3.95f, 0f), new Vector3(0.08f, 0.75f, 0.08f),
            WallIron, metallic: 0.6f, smoothness: 0.45f);
        WallPrim(PrimitiveType.Sphere, "7_Finial", root.transform,
            new Vector3(0f, 4.75f, 0f), Vector3.one * 0.16f,
            WallBrass, metallic: 0.85f, smoothness: 0.6f);
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0f, 4.10f, 0.38f), new Vector3(0.06f, 0.90f, 0.60f), Color.white);

        AddWallNightLight(root.transform, new Vector3(0f, 4.0f, 0f), 1.6f, 8f);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(HubSize, 3.7f, HubSize);
        boxCol.center = Vector3.up * (3.7f * 0.5f);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
    }

    /// <summary>
    /// Wall tower: a converted instance — the curtain module base continues the
    /// wall line, with a slender round turret rising to ~7 m (~2x hub height).
    /// Corbelled foot, two course bands, arrow slits, marble crown ledge with a
    /// merlon ring, and a brazier ember accent at the top.
    /// </summary>
    private GameObject CreateProceduralWallTower(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallTower_{entity.Index}");
        root.transform.position = center;

        // Curtain-continuity base (slightly bolder than a plain module).
        WallPrim(PrimitiveType.Cube, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f), new Vector3(WallThick + 0.70f, 0.30f, ModuleLen + 0.05f),
            WallStoneDark);
        WallPrim(PrimitiveType.Cube, "2_Base", root.transform,
            new Vector3(0f, 0.30f + (BodyTop - 0.30f) * 0.5f, 0f),
            new Vector3(WallThick + 0.30f, BodyTop - 0.30f, ModuleLen + 0.05f), WallStone);
        WallPrim(PrimitiveType.Cube, "3_Coping", root.transform,
            new Vector3(0f, BodyTop + 0.075f, 0f),
            new Vector3(WallThick + 0.56f, 0.15f, ModuleLen + 0.05f), WallMarble, smoothness: 0.45f);

        // Corbelled foot easing the curtain into the turret.
        WallPrim(PrimitiveType.Cylinder, "3_TurretFoot", root.transform,
            new Vector3(0f, 2.35f, 0f), new Vector3(2.05f, 0.45f, 2.05f), WallStoneDark);

        // Slender turret shaft.
        WallPrim(PrimitiveType.Cylinder, "4_Turret", root.transform,
            new Vector3(0f, 3.20f, 0f), new Vector3(1.80f, 3.20f, 1.80f), WallStone);

        // Course bands on the shaft.
        WallPrim(PrimitiveType.Cylinder, "5_Band_A", root.transform,
            new Vector3(0f, 3.40f, 0f), new Vector3(1.84f, 0.035f, 1.84f), WallStoneDark);
        WallPrim(PrimitiveType.Cylinder, "5_Band_B", root.transform,
            new Vector3(0f, 5.00f, 0f), new Vector3(1.84f, 0.035f, 1.84f), WallStoneDark);

        // Arrow slits: outer face and both along-wall faces, plus one high outer.
        for (int i = 0; i < 3; i++)
        {
            float a = (i == 0 ? 0f : (i == 1 ? 90f : 270f));
            float rad = a * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"5_Slit_{(char)('A' + i)}", root.transform,
                new Vector3(Mathf.Cos(rad) * 0.88f, 3.90f, Mathf.Sin(rad) * 0.88f),
                new Vector3(0.09f, 0.55f, 0.18f), Quaternion.Euler(0f, -a, 0f), WallStoneDark);
        }
        WallPrimRot(PrimitiveType.Cube, "5_Slit_D", root.transform,
            new Vector3(0.88f, 5.30f, 0f),
            new Vector3(0.09f, 0.55f, 0.18f), Quaternion.identity, WallStoneDark);

        // Crown ledge + merlon ring.
        WallPrim(PrimitiveType.Cylinder, "6_CrownLedge", root.transform,
            new Vector3(0f, 6.45f, 0f), new Vector3(2.25f, 0.11f, 2.25f), WallMarble, smoothness: 0.5f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            WallPrimRot(PrimitiveType.Cube, $"7_Merlon_{(char)('A' + i)}", root.transform,
                new Vector3(Mathf.Cos(a) * 0.98f, 6.80f, Mathf.Sin(a) * 0.98f),
                new Vector3(0.42f, 0.48f, 0.42f), Quaternion.Euler(0f, -i * 60f, 0f), WallStone);
        }

        // Brazier ember accent (the tower's single emissive).
        WallPrim(PrimitiveType.Cylinder, "7_BrazierBowl", root.transform,
            new Vector3(0f, 6.62f, 0f), new Vector3(0.55f, 0.10f, 0.55f),
            WallIron, metallic: 0.7f, smoothness: 0.4f);
        var ember = WallPrim(PrimitiveType.Sphere, "Ember", root.transform,
            new Vector3(0f, 6.80f, 0f), Vector3.one * 0.30f, WallEmber);
        MakeWallPartEmissive(ember, WallEmber, 2.0f);
        var emberLightGo = new GameObject("BrazierLight");
        emberLightGo.transform.SetParent(root.transform, false);
        emberLightGo.transform.localPosition = new Vector3(0f, 6.95f, 0f);
        var emberLight = emberLightGo.AddComponent<Light>();
        emberLight.type = LightType.Point;
        emberLight.color = WallEmber;
        emberLight.intensity = 1.8f;
        emberLight.range = 8f;
        emberLight.shadows = LightShadows.None;

        // Faction stripe banner hung on the outer shaft face.
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0.93f, 4.30f, 0f), new Vector3(0.05f, 1.10f, 0.60f), Color.white);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(2.0f, 7.05f, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * (7.05f * 0.5f);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    /// <summary>
    /// Wall gate: a Convert-to-Gate region replaces AlanthorWall.GateRegionSpan
    /// (= 3) contiguous instances; every member renders this pid. The region
    /// LEADER (WallGateGroup.Leader == entity) becomes the arched gateway module:
    /// jamb piers, an arch-ring facade, iron-banded double door leaves, a
    /// portcullis hint above them, and faction pennants on the crown. NON-leader
    /// members become solid flanking gatehouse bastions. The legacy
    /// single-instance gate (no WallGateGroup) renders the arch module.
    /// Units pass across X through the leader's opening.
    /// </summary>
    private GameObject CreateProceduralWallGate(Vector3 center, Entity entity)
    {
        bool isLeader = true;
        if (_em.HasComponent<WallGateGroup>(entity))
            isLeader = _em.GetComponentData<WallGateGroup>(entity).Leader == entity;

        var root = new GameObject($"WallGate_{entity.Index}");
        root.transform.position = center;

        if (isLeader)
            BuildWallGateArchModule(root);
        else
            BuildWallGateFlankModule(root);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    /// <summary>Centre gate module: the arched gateway with the doors.</summary>
    private void BuildWallGateArchModule(GameObject root)
    {
        const float openHalfZ = 0.9f;  // passage half-width along Z (1.8 m opening)
        const float pierW     = WallThick + 0.30f;

        // Jamb piers at the Z ends, rising above the curtain crown.
        for (int side = -1; side <= 1; side += 2)
        {
            WallPrim(PrimitiveType.Cube, side < 0 ? "1_JambL" : "1_JambR", root.transform,
                new Vector3(0f, 1.60f, side * (openHalfZ + 0.30f)),
                new Vector3(pierW, 3.20f, ModuleLen * 0.5f - openHalfZ + 0.05f), WallStone);
        }

        // Arch ring: thin proud disc on the outer facade over the opening.
        WallPrimRot(PrimitiveType.Cylinder, "2_ArchRing", root.transform,
            new Vector3(pierW * 0.5f + 0.06f, 2.50f, 0f),
            new Vector3(2.20f, 0.06f, 2.20f), Quaternion.Euler(0f, 0f, 90f), WallStoneDark);

        // Lintel band bridging the piers over the arch.
        WallPrim(PrimitiveType.Cube, "3_Lintel", root.transform,
            new Vector3(0f, 3.30f, 0f), new Vector3(pierW + 0.05f, 0.80f, ModuleLen + 0.05f),
            WallStone);

        // Coping + merlon crown above the lintel.
        WallPrim(PrimitiveType.Cube, "4_Cap", root.transform,
            new Vector3(0f, 3.775f, 0f), new Vector3(pierW + 0.26f, 0.15f, ModuleLen + 0.05f),
            WallMarble, smoothness: 0.45f);
        AddWallMerlonRow(root.transform, "5_Merlon", ModuleLen, 3.85f, 0.85f, WallStone);

        // Pennant poles atop the jambs; cloths are faction-tinted Stripe parts.
        for (int side = -1; side <= 1; side += 2)
        {
            string id = side < 0 ? "L" : "R";
            WallPrim(PrimitiveType.Cylinder, $"6_PennantPole_{id}", root.transform,
                new Vector3(0f, 4.35f, side * (openHalfZ + 0.30f)),
                new Vector3(0.05f, 0.50f, 0.05f), WallIron, metallic: 0.6f, smoothness: 0.45f);
            WallPrim(PrimitiveType.Cube, $"Stripe_Pennant_{id}", root.transform,
                new Vector3(0f, 4.62f, side * (openHalfZ + 0.30f) - side * 0.30f),
                new Vector3(0.04f, 0.30f, 0.45f), Color.white);
        }

        // Iron-banded double door leaves on the outer half of the passage
        // (unnumbered: doors rise as the final construction step).
        for (int side = -1; side <= 1; side += 2)
        {
            string id = side < 0 ? "L" : "R";
            WallPrim(PrimitiveType.Cube, $"GateDoor{id}", root.transform,
                new Vector3(0.42f, 1.20f, side * 0.46f),
                new Vector3(0.16f, 2.30f, 0.88f), WallWood, smoothness: 0.2f);
            WallPrim(PrimitiveType.Cube, $"DoorBandLow{id}", root.transform,
                new Vector3(0.52f, 0.75f, side * 0.46f),
                new Vector3(0.05f, 0.09f, 0.80f), WallIron, metallic: 0.8f, smoothness: 0.5f);
            WallPrim(PrimitiveType.Cube, $"DoorBandHigh{id}", root.transform,
                new Vector3(0.52f, 1.70f, side * 0.46f),
                new Vector3(0.05f, 0.09f, 0.80f), WallIron, metallic: 0.8f, smoothness: 0.5f);
        }

        // Portcullis hint: half-lowered iron bars behind the doors.
        for (int i = 0; i < 4; i++)
        {
            float zz = -0.6f + i * 0.4f;
            WallPrim(PrimitiveType.Cylinder, $"Portcullis{(char)('A' + i)}", root.transform,
                new Vector3(-0.30f, 2.45f, zz), new Vector3(0.06f, 0.50f, 0.06f),
                WallIron, metallic: 0.85f, smoothness: 0.5f);
        }
        WallPrimRot(PrimitiveType.Cylinder, "PortcullisBar", root.transform,
            new Vector3(-0.30f, 2.15f, 0f), new Vector3(0.05f, 0.95f, 0.05f),
            Quaternion.Euler(90f, 0f, 0f), WallIron, metallic: 0.85f, smoothness: 0.5f);

        // Lantern over the outer arch mouth (the gate's single emissive).
        AddWallNightLight(root.transform, new Vector3(pierW * 0.5f + 0.35f, 3.0f, 0f), 1.5f, 7f);

        // Colliders: jambs only, so selection/raycast never blocks the passage.
        for (int side = -1; side <= 1; side += 2)
        {
            var jc = new GameObject(side < 0 ? "ColL" : "ColR");
            jc.transform.SetParent(root.transform, false);
            jc.transform.localPosition = new Vector3(0f, 2.15f, side * (openHalfZ + 0.30f));
            var bc = jc.AddComponent<BoxCollider>();
            bc.size = new Vector3(pierW + 0.1f, 4.30f, ModuleLen * 0.5f - openHalfZ + 0.10f);
        }
    }

    /// <summary>Flanking gate module: a solid gatehouse bastion, taller than the
    /// curtain, stepping the silhouette up toward the central arch.</summary>
    private void BuildWallGateFlankModule(GameObject root)
    {
        WallPrim(PrimitiveType.Cube, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f), new Vector3(WallThick + 0.60f, 0.30f, ModuleLen + 0.05f),
            WallStoneDark);
        WallPrim(PrimitiveType.Cube, "2_Body", root.transform,
            new Vector3(0f, 1.75f, 0f), new Vector3(WallThick + 0.30f, 2.90f, ModuleLen + 0.05f),
            WallStone);
        WallPrim(PrimitiveType.Cube, "3_Course", root.transform,
            new Vector3(0f, 1.05f, 0f), new Vector3(WallThick + 0.36f, 0.05f, ModuleLen + 0.08f),
            WallStoneDark);
        WallPrim(PrimitiveType.Cube, "3_Slit", root.transform,
            new Vector3((WallThick + 0.30f) * 0.5f + 0.03f, 2.20f, 0f),
            new Vector3(0.10f, 0.50f, 0.16f), WallStoneDark);
        WallPrim(PrimitiveType.Cube, "4_Cap", root.transform,
            new Vector3(0f, 3.275f, 0f), new Vector3(WallThick + 0.56f, 0.15f, ModuleLen + 0.05f),
            WallMarble, smoothness: 0.45f);
        AddWallMerlonRow(root.transform, "5_Merlon", ModuleLen, 3.35f, 0.85f, WallStone);

        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3((WallThick + 0.30f) * 0.5f + 0.03f, 1.60f, 0f),
            new Vector3(0.05f, 0.90f, 0.55f), Color.white);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(WallThick + 0.6f, 3.80f, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * (3.80f * 0.5f);
    }
}
