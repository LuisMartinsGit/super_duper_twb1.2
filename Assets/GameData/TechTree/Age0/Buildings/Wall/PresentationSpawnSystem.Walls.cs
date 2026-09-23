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
using TheWaningBorder.Core;
using TheWaningBorder.Rendering;   // EntityViewManager

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
    private static readonly Color WallSlate     = new Color(0.30f, 0.32f, 0.36f);   // hub dome (roofs are dark slate)
    // Level-1 palisade + level-3 reinforcement (docs/Design/Age_1_Alanthor.md
    // § The three wall levels).
    private static readonly Color WallTimber    = new Color(0.45f, 0.31f, 0.18f);   // split logs
    private static readonly Color WallTimberDk  = new Color(0.30f, 0.20f, 0.12f);   // sill / lashings
    private static readonly Color WallShingle   = new Color(0.33f, 0.28f, 0.24f);   // timber hub roof
    private static readonly Color WallSteel     = new Color(0.62f, 0.65f, 0.70f);   // the great shields

    /// <summary>
    /// The scale the wall set is drawn at: the curtain module's, so every
    /// piece shares one world scale. Null when there is no curtain art, which
    /// leaves each piece to fit its own footprint as before.
    /// </summary>
    private static float? WallArtScale(byte tier)
    {
        var curtain = WallModuleArt.For(tier);
        return curtain != null ? curtain.Scale : (float?)null;
    }

    /// <summary>
    /// Paint a wall piece's ownership parts in its owner's colour — the
    /// procedural `Stripe_*` prims and the authored model's marked parts
    /// alike. Every wall visual gets this; the swept curtain does its own,
    /// per sub-mesh, because it has no per-part renderers to tint.
    /// </summary>
    private void ApplyWallOwnerColor(GameObject go, Entity entity)
    {
        if (go == null || !_em.HasComponent<FactionTag>(entity)) return;
        var color = FactionColors.Get(_em.GetComponentData<FactionTag>(entity).Value);
        WallModuleArt.ApplyOwnerColor(go, color);
    }

    /// <summary>The wall level this piece is clad at. Everything the wall
    /// draws branches on it — docs/Design/Age_1_Alanthor.md § The three wall
    /// levels.</summary>
    private byte WallTierOf(Entity entity)
        => TheWaningBorder.Entities.WallTiers.Of(_em, entity);

    // Compact curtain-wall cross-section (meters). Values mirror
    // AlanthorWall.WallWidth / WallHeight / InstanceSpacing / HubWidth.
    private const float WallThick  = 1f;    // masonry thickness across the wall (X)
    private const float BodyTop    = 2.0f;  // solid masonry top
    private const float CrownTop   = 2.6f;  // merlon crown top (= AlanthorWall.WallHeight)
    private const float ModuleLen  = 3f;    // along Z (= AlanthorWall.InstanceSpacing)
    // The hub is drawn at its SIM radius, read from the one constant the sim
    // uses (one wall section; docs/Design/Build_Grid.md). It used to be a
    // private copy that never moved when the footprint changed: the curtain
    // mesh starts HubInset (= this radius) from the hub centre, so any drift
    // between the two shows as a gap of bare ground between wall and hub,
    // and the selection ring (which reads Radius) stops matching the drum.
    private const float HubR       = TheWaningBorder.Entities.AlanthorWall.HubRadius;
    private const float HubBodyTop = 3.4f;  // drum masonry top — clears the curtain crown
    private const float HubCrownY  = 3.5f;  // crown ledge centre

    // PERF (2026-08-13): this used to do `new Material(Shader.Find(...))` per
    // primitive. Every wall piece is 8-20 primitives, and a finished AI
    // perimeter is ~165 curtain modules per faction — so a walled-up match
    // built THOUSANDS of unique Material instances, each one its own
    // unbatchable draw call. That is the frame-time collapse in logs/Perf.log:
    // FRAME climbs from ~30 ms to ~150 ms as the AI's walls go up, while every
    // instrumented system stays flat at ~7 ms, because the cost is all in the
    // renderer, not in any system anyone had a stopwatch around.
    //
    // ProceduralMaterialHelper (Fix #203) already solved exactly this for the
    // rest of the procedural content — shared base material + per-renderer
    // MaterialPropertyBlock, so the whole wall batches. The wall builders were
    // simply never migrated onto it.
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
            ProceduralMaterialHelper.SetProperties(r, color, metallic, smoothness);
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

    // ── Realtime wall-light budget ───────────────────────────────────────
    // Every hub, gate and tower asked for its own realtime point light. A
    // finished perimeter is ~17 hubs + towers + gates PER FACTION, so a
    // three-way match lit 100+ dynamic point lights — all of them permanently
    // in the culling set, most of them off-screen and none of them load-
    // bearing for readability. The emissive bulb/ember carries the look on its
    // own; the actual Light is a bonus the nearest few pieces get.
    private const int MaxWallLights = 24;
    private static readonly System.Collections.Generic.List<Light> _wallLights
        = new System.Collections.Generic.List<Light>();

    /// <summary>Claim a slot in the realtime wall-light budget. Prunes lights
    /// whose wall has since been destroyed, so a rebuilt wall relights.
    /// Bounded work (list never exceeds <see cref="MaxWallLights"/> live
    /// entries) and only ever runs at wall-spawn time.</summary>
    private static bool TryClaimWallLight(Light l)
    {
        for (int i = _wallLights.Count - 1; i >= 0; i--)
            if (_wallLights[i] == null) _wallLights.RemoveAt(i);
        if (_wallLights.Count >= MaxWallLights) return false;
        _wallLights.Add(l);
        return true;
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
        if (!TryClaimWallLight(l)) Destroy(l);   // over budget: bulb only
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
        // `rend.material` INSTANTIATES a per-renderer material copy, which is
        // the same batching-killer WallPrim just stopped doing. Every accent
        // here is tinted the same colour it emits, so the shared emissive base
        // + MPB carries it. See the WallPrim comment.
        ProceduralMaterialHelper.SetEmissive(rend, color, color, intensity);
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
    /// A DRAWN wall's segment (docs/Design/Age_1_Alanthor.md § Drawing walls):
    /// no modules — one continuous mesh swept along the curve, owned by a
    /// WallCurveVisual that raises it with construction and opens breaches.
    /// No collider: clicks fall through to the cells' pick boxes beneath.
    /// </summary>
    private GameObject CreateProceduralCurvedWall(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallCurve_{entity.Index}");
        root.transform.position = Vector3.zero;     // the mesh is in world space
        var vis = root.AddComponent<WallCurveVisual>();
        vis.Segment = entity;
        vis.Init(_em);
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        return root;
    }

    /// <summary>
    /// A curved segment's cell: the module's pick box and EntityReference,
    /// nothing drawn — the segment's swept mesh is the wall. Keeps every
    /// click-on-the-wall flow (select, convert to gate / tower) identical.
    /// </summary>
    private GameObject CreateCurveCellPick(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallCell_{entity.Index}");
        root.transform.position = center;
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
    /// One manned position per occupant of a reinforced module
    /// (docs/Design/Age_1_Alanthor.md § Garrison slots). A garrisoned unit is
    /// absorbed by the wall rather than parked on it, so THIS is what the
    /// player sees of their men: a helmeted figure behind the parapet, one
    /// per filled slot. Built at spawn time — the module's view is respawned
    /// whenever its garrison changes.
    /// </summary>
    private void AddWallGarrisonFigures(Transform parent, Entity module)
    {
        int manned = TheWaningBorder.Entities.WallGarrison.OccupantCount(_em, module);
        for (int i = 0; i < manned && i < 2; i++)
        {
            float zz = (i == 0 ? -0.75f : 0.75f);
            // Behind the parapet on the INNER side, shoulders above the crown.
            WallPrim(PrimitiveType.Capsule, $"Garrison_Body_{i}", parent,
                new Vector3(-0.28f, BodyTop + 0.10f, zz),
                new Vector3(0.42f, 0.42f, 0.42f), WallStoneDark, smoothness: 0.2f);
            var helm = WallPrim(PrimitiveType.Sphere, $"Garrison_Helm_{i}", parent,
                new Vector3(-0.28f, BodyTop + 0.62f, zz),
                new Vector3(0.30f, 0.30f, 0.30f), WallSteel, metallic: 0.85f, smoothness: 0.6f);
            helm.name = $"Garrison_Helm_{i}";
            WallPrim(PrimitiveType.Cube, $"Stripe_Garrison_{i}", parent,
                new Vector3(-0.44f, BodyTop + 0.15f, zz),
                new Vector3(0.05f, 0.34f, 0.30f), Color.white);
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

        byte tier = WallTierOf(entity);

        // Authored art wins. A straight segment draws a GameObject per cell
        // (a curved one bakes them all into its own mesh — WallArtMesh), so
        // here the module is instantiated and normalised into the sim's frame
        // rather than baked. docs/Design/Age_1_Alanthor.md § The wall's art.
        var art = WallModuleArt.For(tier);
        var module = WallModuleArt.Instantiate(art, root.transform, "Module");
        if (module != null)
        {
            // Same seam-closing overlap the baked curtain uses.
            var ms = module.transform.localScale;
            ms.z *= 1f + TheWaningBorder.Entities.AlanthorWall.ModuleOverlap;
            module.transform.localScale = ms;

            var artCol = root.AddComponent<BoxCollider>();
            artCol.size = new Vector3(WallThick + 0.3f, Mathf.Max(1f, art.Height), ModuleLen + 0.1f);
            artCol.center = Vector3.up * (Mathf.Max(1f, art.Height) * 0.5f);
            var artRef = root.AddComponent<EntityReference>();
            artRef.Entity = entity;
            if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
                root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
            return root;
        }

        bool timber = tier <= TheWaningBorder.Entities.WallTiers.Palisade;
        bool reinforced = tier >= TheWaningBorder.Entities.WallTiers.Reinforced;

        var rng = new System.Random(entity.Index);
        float bodyShade = (float)rng.NextDouble() * 0.12f;
        Color bodyTint = timber
            ? Color.Lerp(WallTimber, WallTimberDk, bodyShade)
            : Color.Lerp(WallStone, WallStoneDark, bodyShade);
        Color trim = timber ? WallTimberDk : WallStoneDark;

        // Base plinth / timber sill (slightly proud of the body).
        WallPrim(PrimitiveType.Cube, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f),
            new Vector3(WallThick + 0.30f, 0.30f, ModuleLen + 0.05f), trim);

        // Solid body up to the coping underside.
        WallPrim(PrimitiveType.Cube, "2_Body", root.transform,
            new Vector3(0f, 0.30f + (BodyTop - 0.30f) * 0.5f, 0f),
            new Vector3(timber ? WallThick * 0.7f : WallThick, BodyTop - 0.30f, ModuleLen + 0.05f), bodyTint);

        // Course shadow line / lashing band.
        WallPrim(PrimitiveType.Cube, "3_Course", root.transform,
            new Vector3(0f, 1.05f, 0f),
            new Vector3(WallThick + 0.06f, 0.05f, ModuleLen + 0.08f), trim);

        // Overhanging coping stones — iron-banded at level 3.
        WallPrim(PrimitiveType.Cube, "4_Coping", root.transform,
            new Vector3(0f, BodyTop + 0.075f, 0f),
            new Vector3(WallThick + 0.26f, 0.15f, ModuleLen + 0.05f),
            reinforced ? WallIron : (timber ? WallTimberDk : WallMarble),
            metallic: reinforced ? 0.8f : 0f, smoothness: timber ? 0.2f : 0.45f);

        // Crown: merlons in stone, sharpened log tops in timber.
        if (timber)
        {
            int pickets = Mathf.Max(4, Mathf.RoundToInt(ModuleLen / 0.4f));
            for (int i = 0; i < pickets; i++)
            {
                float zz = -ModuleLen * 0.5f + ModuleLen * (i + 0.5f) / pickets;
                float h = 0.45f + ((float)rng.NextDouble() * 2f - 1f) * 0.07f;
                WallPrimRot(PrimitiveType.Cube, $"5_Picket_{i}", root.transform,
                    new Vector3(0f, BodyTop + 0.15f + h * 0.5f, zz),
                    new Vector3(WallThick * 0.62f, h, 0.30f),
                    Quaternion.Euler(((float)rng.NextDouble() * 2f - 1f) * 2f, 0f, 0f), bodyTint);
            }
        }
        else
        {
            AddWallMerlonRow(root.transform, "5_Merlon", ModuleLen,
                BodyTop + 0.15f, 0.80f, bodyTint, rng);
        }

        // Level 3: the great shields, hung on the OUTER face under the crown.
        if (reinforced)
        {
            for (int i = -1; i <= 1; i++)
            {
                WallPrim(PrimitiveType.Cube, $"6_Shield_{i + 1}", root.transform,
                    new Vector3(WallThick * 0.5f + 0.11f, 1.45f, i * 0.95f),
                    new Vector3(0.16f, 1.05f, 0.72f), WallSteel,
                    metallic: 0.9f, smoothness: 0.65f);
                WallPrim(PrimitiveType.Sphere, $"6_Boss_{i + 1}", root.transform,
                    new Vector3(WallThick * 0.5f + 0.20f, 1.45f, i * 0.95f),
                    new Vector3(0.20f, 0.20f, 0.20f), WallBrass,
                    metallic: 0.95f, smoothness: 0.7f);
            }
            AddWallGarrisonFigures(root.transform, entity);
        }

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
    /// A curtain module carrying a war engine (docs/Design/Age_1_Alanthor.md
    /// § Ballista and Trebuchet emplacements). The module is still wall — it
    /// keeps the curtain cross-section so the line reads unbroken — and gains
    /// a widened fighting top: corbels out to a planked deck, a timber
    /// mantlet on the outer edge and the engine's pintle ring. The ENGINE
    /// itself is a separate entity standing on that deck, so nothing here
    /// draws it.
    /// </summary>
    private GameObject CreateProceduralWallEmplacement(Vector3 center, Entity entity, bool trebuchet)
    {
        var root = new GameObject($"WallEmplacement_{entity.Index}");
        root.transform.position = center;

        byte tier = WallTierOf(entity);
        bool timber = tier <= TheWaningBorder.Entities.WallTiers.Palisade;
        Color shell = timber ? WallTimber : WallStone;
        Color trim = timber ? WallTimberDk : WallStoneDark;
        Color beam = WallTimberDk;
        // A trebuchet needs a bigger bed than a bolt thrower.
        float deckLen = trebuchet ? ModuleLen + 0.6f : ModuleLen + 0.1f;
        float deckWide = trebuchet ? 3.2f : 2.4f;
        float deckY = CrownTop + 0.30f;

        // Curtain below, unchanged in section so the wall line runs through.
        WallPrim(PrimitiveType.Cube, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f),
            new Vector3(WallThick + 0.40f, 0.30f, ModuleLen + 0.05f), trim);
        WallPrim(PrimitiveType.Cube, "2_Body", root.transform,
            new Vector3(0f, 0.30f + (BodyTop - 0.30f) * 0.5f, 0f),
            new Vector3(WallThick + 0.16f, BodyTop - 0.30f, ModuleLen + 0.05f), shell);
        WallPrim(PrimitiveType.Cube, "2_Course", root.transform,
            new Vector3(0f, 1.05f, 0f),
            new Vector3(WallThick + 0.22f, 0.05f, ModuleLen + 0.08f), trim);

        // Corbels flaring out to carry the deck.
        for (int i = -1; i <= 1; i++)
            for (int side = -1; side <= 1; side += 2)
                WallPrim(PrimitiveType.Cube, $"3_Corbel_{i + 1}{(side < 0 ? "L" : "R")}", root.transform,
                    new Vector3(side * (WallThick * 0.5f + 0.35f), BodyTop + 0.10f, i * (ModuleLen * 0.33f)),
                    new Vector3(0.75f, 0.22f, 0.30f), trim);

        // The deck itself.
        WallPrim(PrimitiveType.Cube, "3_Deck", root.transform,
            new Vector3(0f, deckY - 0.09f, 0f),
            new Vector3(deckWide, 0.18f, deckLen), beam, smoothness: 0.18f);
        for (int i = -2; i <= 2; i++)
            WallPrim(PrimitiveType.Cube, $"3_Joist_{i + 2}", root.transform,
                new Vector3(0f, deckY - 0.26f, i * (deckLen / 5.5f)),
                new Vector3(deckWide - 0.2f, 0.16f, 0.20f), beam);

        // Mantlet on the outer edge — what the crew shelters behind.
        for (int i = -1; i <= 1; i++)
            WallPrim(PrimitiveType.Cube, $"4_Mantlet_{i + 1}", root.transform,
                new Vector3(deckWide * 0.5f - 0.12f, deckY + 0.55f, i * (deckLen * 0.31f)),
                new Vector3(0.18f, 1.10f, deckLen * 0.28f), beam, smoothness: 0.2f);
        WallPrim(PrimitiveType.Cube, "4_MantletRail", root.transform,
            new Vector3(deckWide * 0.5f - 0.12f, deckY + 1.12f, 0f),
            new Vector3(0.24f, 0.12f, deckLen), WallIron, metallic: 0.7f, smoothness: 0.4f);

        // The pintle ring the engine's frame drops into.
        WallPrim(PrimitiveType.Cylinder, "4_Pintle", root.transform,
            new Vector3(0f, deckY + 0.05f, 0f),
            new Vector3(trebuchet ? 1.25f : 0.95f, 0.06f, trebuchet ? 1.25f : 0.95f),
            WallIron, metallic: 0.8f, smoothness: 0.45f);

        // Ammunition: bolts in a rack, or a pile of stones.
        if (trebuchet)
        {
            for (int i = 0; i < 3; i++)
                WallPrim(PrimitiveType.Sphere, $"4_Stone_{i}", root.transform,
                    new Vector3(-deckWide * 0.5f + 0.42f, deckY + 0.28f, -deckLen * 0.28f + i * 0.5f),
                    Vector3.one * 0.42f, WallStone);
        }
        else
        {
            WallPrim(PrimitiveType.Cube, "4_BoltRack", root.transform,
                new Vector3(-deckWide * 0.5f + 0.38f, deckY + 0.35f, deckLen * 0.22f),
                new Vector3(0.36f, 0.50f, 0.95f), beam);
        }

        // Faction accent on the mantlet.
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(deckWide * 0.5f + 0.02f, deckY + 0.55f, 0f),
            new Vector3(0.05f, 0.80f, 0.55f), Color.white);

        var boxCol = root.AddComponent<BoxCollider>();
        float top = deckY + 1.25f;
        boxCol.size = new Vector3(deckWide + 0.2f, top, deckLen + 0.1f);
        boxCol.center = Vector3.up * (top * 0.5f);

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
        _viewSync.Remove(entity);   // cached components belonged to the old view
    }

    /// <summary>
    /// Wall hub: a round tower with a dome, drawn at the hub's sim radius
    /// (<see cref="HubR"/> = one wall section) — the curtain mesh starts at
    /// AlanthorWall.HubInset, exactly this drum's rim. Plinth, drum with a
    /// course band, a corbelled crown ledge with a merlon ring, the dome
    /// with a brass finial and the faction banner. Omnidirectional —
    /// identity rotation.
    /// </summary>
    private GameObject CreateProceduralWallHub(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallHub_{entity.Index}");
        root.transform.position = center;

        // Authored hub art wins. It is drawn at the CURTAIN's scale, not
        // fitted to the hub's own footprint: the set is authored at one scale
        // and must be drawn at one scale. Fitting each piece to its footprint
        // stretched the hub by the ratio between its 4.2 m footprint and the
        // 3 m module, so hubs came out visibly bigger than the wall they
        // anchor. A hub is round, so it is not turned to face along the wall.
        // docs/Design/Age_1_Alanthor.md § The wall's art.
        var hubArt = WallModuleArt.ForPart(
            TheWaningBorder.Entities.AlanthorWall.HubPresentationID,
            TheWaningBorder.Entities.AlanthorWall.HubWidth, orientAlongZ: false,
            explicitScale: WallArtScale(WallTierOf(entity)));
        if (WallModuleArt.Instantiate(hubArt, root.transform, "Hub") != null)
        {
            float top = Mathf.Max(2f, hubArt.Height);
            var hubCol = root.AddComponent<BoxCollider>();
            hubCol.size = new Vector3(TheWaningBorder.Entities.AlanthorWall.HubWidth, top,
                                      TheWaningBorder.Entities.AlanthorWall.HubWidth);
            hubCol.center = Vector3.up * (top * 0.5f);
            var hubRef = root.AddComponent<EntityReference>();
            hubRef.Entity = entity;
            return root;
        }

        byte tier = WallTierOf(entity);
        bool timber = tier <= TheWaningBorder.Entities.WallTiers.Palisade;
        bool reinforced = tier >= TheWaningBorder.Entities.WallTiers.Reinforced;
        Color shell = timber ? WallTimber : WallStone;
        Color trim  = timber ? WallTimberDk : WallStoneDark;
        Color ledge = timber ? WallTimberDk : WallMarble;

        // Unity's cylinder primitive is 2 units tall at scale 1 (height =
        // scale.y * 2); its diameter is scale.x.
        float d = HubR * 2f;

        // Base plinth.
        WallPrim(PrimitiveType.Cylinder, "1_Plinth", root.transform,
            new Vector3(0f, 0.15f, 0f), new Vector3(d + 0.50f, 0.15f, d + 0.50f), trim);

        // The drum — rises past the curtain crown (CrownTop) so the hub
        // reads as the anchor the wall hangs off, not a bump in it.
        WallPrim(PrimitiveType.Cylinder, "2_Drum", root.transform,
            new Vector3(0f, HubBodyTop * 0.5f, 0f), new Vector3(d, HubBodyTop * 0.5f, d), shell);

        // Course shadow band at the curtain's own course height, so the line
        // runs on unbroken from wall to hub.
        WallPrim(PrimitiveType.Cylinder, "3_Course", root.transform,
            new Vector3(0f, 1.45f, 0f), new Vector3(d + 0.08f, 0.03f, d + 0.08f), trim);

        // Corbelled crown ledge, slightly proud of the drum.
        WallPrim(PrimitiveType.Cylinder, "4_Crown", root.transform,
            new Vector3(0f, HubCrownY, 0f), new Vector3(d + 0.50f, 0.10f, d + 0.50f),
            ledge, smoothness: timber ? 0.2f : 0.45f);

        // Crown ring: merlons in stone, a lashed picket rail in timber. One
        // per ~1.2 m of rim.
        int merlons = Mathf.Max(8, Mathf.RoundToInt(Mathf.PI * d / 1.2f));
        float ringR = HubR + 0.05f;
        float merlonY = HubCrownY + 0.10f + 0.25f;
        for (int i = 0; i < merlons; i++)
        {
            float a = i / (float)merlons * Mathf.PI * 2f;
            WallPrimRot(PrimitiveType.Cube, $"5_Merlon_{i}", root.transform,
                new Vector3(Mathf.Cos(a) * ringR, merlonY, Mathf.Sin(a) * ringR),
                timber ? new Vector3(0.22f, 0.58f, 0.22f) : new Vector3(0.45f, 0.50f, 0.55f),
                Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), shell);
        }

        // Level 3: iron bands strapping the drum.
        if (reinforced)
        {
            WallPrim(PrimitiveType.Cylinder, "3_IronBandLow", root.transform,
                new Vector3(0f, 0.85f, 0f), new Vector3(d + 0.14f, 0.07f, d + 0.14f),
                WallIron, metallic: 0.85f, smoothness: 0.5f);
            WallPrim(PrimitiveType.Cylinder, "3_IronBandHigh", root.transform,
                new Vector3(0f, 2.55f, 0f), new Vector3(d + 0.14f, 0.07f, d + 0.14f),
                WallIron, metallic: 0.85f, smoothness: 0.5f);
        }

        // The cap: a slate dome in stone, a conical shingle roof in timber —
        // a sphere / cone sunk to its base in the crown.
        float domeR = HubR - 0.35f;
        float domeH = domeR * (timber ? 1.25f : 0.75f);
        float domeBase = HubCrownY + 0.10f;
        if (timber)
        {
            // Unity has no cone primitive: a cylinder squeezed to a point is
            // not available either, so the shingle cap is a squat pyramid of
            // two crossed wedges — cheap, and it reads as a roof at RTS zoom.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                WallPrimRot(PrimitiveType.Cube, $"6_Roof_{i}", root.transform,
                    new Vector3(0f, domeBase + domeH * 0.45f, 0f),
                    new Vector3(domeR * 1.7f, domeH * 0.9f, 0.35f),
                    Quaternion.Euler(0f, a, 22f), WallShingle, smoothness: 0.2f);
            }
        }
        else
        {
            WallPrim(PrimitiveType.Sphere, "6_Dome", root.transform,
                new Vector3(0f, domeBase, 0f), new Vector3(domeR * 2f, domeH * 2f, domeR * 2f),
                WallSlate, smoothness: 0.35f);
        }

        // Finial and the faction banner at the dome's crown.
        float poleBase = domeBase + domeH;
        WallPrim(PrimitiveType.Cylinder, "7_BannerPole", root.transform,
            new Vector3(0f, poleBase + 0.80f, 0f), new Vector3(0.08f, 0.80f, 0.08f),
            WallIron, metallic: 0.6f, smoothness: 0.45f);
        WallPrim(PrimitiveType.Sphere, "7_Finial", root.transform,
            new Vector3(0f, poleBase + 1.65f, 0f), Vector3.one * 0.18f,
            WallBrass, metallic: 0.85f, smoothness: 0.6f);
        WallPrim(PrimitiveType.Cube, "Stripe_Banner", root.transform,
            new Vector3(0f, poleBase + 1.05f, 0.40f), new Vector3(0.06f, 0.90f, 0.65f), Color.white);

        AddWallNightLight(root.transform, new Vector3(0f, poleBase + 0.60f, 0f), 1.6f, 9f);

        // Pick box = the sim footprint (the drum's bounding square), so what
        // you click is what the contour rings and the passability grid blocks.
        float pickTop = poleBase + 0.40f;
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(d, pickTop, d);
        boxCol.center = Vector3.up * (pickTop * 0.5f);

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
        // Shares the wall-light budget — the emissive ember above already
        // reads as lit without a dynamic light behind it.
        if (!TryClaimWallLight(emberLight)) Destroy(emberLight);

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
    /// Wall gate: ONE structure three curtain modules wide
    /// (docs/Design/Age_1_Alanthor.md § The gate is one structure, three
    /// modules wide). The modules that stood either side of the converted one
    /// were destroyed, and the gatehouse's own masonry stands where they did:
    /// two solid bastion thirds with the archway between them, a lintel and
    /// crown over the top, and two door leaves on hinges that swing with the
    /// gate's open state. Cladding follows the faction's wall level, so a
    /// palisade gate is a timber barred gate and a reinforced one is banded
    /// in iron.
    ///
    /// Units pass across X through the centre third.
    /// </summary>
    private GameObject CreateProceduralWallGate(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallGate_{entity.Index}");
        root.transform.position = center;

        // A legacy multi-cell gate region (pre-2026-09-21 saves) still has
        // non-leader members; they keep rendering the old flanking bastion.
        bool isLeader = true;
        if (_em.HasComponent<WallGateGroup>(entity))
            isLeader = _em.GetComponentData<WallGateGroup>(entity).Leader == entity;

        if (isLeader) BuildWallGatehouse(root, entity);
        else BuildWallGateFlankModule(root);

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;

        return root;
    }

    /// <summary>The gatehouse proper — the whole span, in one piece.</summary>
    private void BuildWallGatehouse(GameObject root, Entity entity)
    {
        float span = TheWaningBorder.Entities.AlanthorWall.GateSpanMetres;
        if (_em.HasComponent<WallGateSpan>(entity))
            span = Mathf.Max(ModuleLen, _em.GetComponentData<WallGateSpan>(entity).Metres);

        // Authored gatehouse art wins, at the curtain's scale for the same
        // reason the hub is (see CreateProceduralWallHub).
        var gateArt = WallModuleArt.ForPart(
            TheWaningBorder.Entities.AlanthorWall.GatePresentationID, span, orientAlongZ: true,
            explicitScale: WallArtScale(WallTierOf(entity)));
        if (WallModuleArt.Instantiate(gateArt, root.transform, "Gatehouse") != null)
        {
            // The doors are part of the authored model: any child whose name
            // says "door" is a leaf, and the driver hinges it on its own outer
            // edge (see WallGateDoors). Wall_Gate.prefab names its two
            // DoorLeft / DoorRight.
            var artDoors = root.AddComponent<TheWaningBorder.Rendering.WallGateDoors>();
            artDoors.Gate = entity;
            int leaves = artDoors.BindDoors(root.transform);
            if (leaves == 0)
                Debug.LogWarning($"[Wall] gate art has no door leaves — name them " +
                                 "*Door* (e.g. DoorLeft / DoorRight) for them to swing.");
            artDoors.SnapToState();

            // Colliders on the flanks only, so a click never blocks the
            // passage — the same rule the procedural gatehouse follows.
            float top = Mathf.Max(3f, gateArt.Height);
            float third = span / 3f;
            for (int side = -1; side <= 1; side += 2)
            {
                var jc = new GameObject(side < 0 ? "ColL" : "ColR");
                jc.transform.SetParent(root.transform, false);
                jc.transform.localPosition = new Vector3(0f, top * 0.5f, side * third);
                var bc = jc.AddComponent<BoxCollider>();
                bc.size = new Vector3(WallThick + 0.6f, top, third);
            }
            return;
        }

        byte tier = WallTierOf(entity);
        bool timber = tier <= TheWaningBorder.Entities.WallTiers.Palisade;
        bool reinforced = tier >= TheWaningBorder.Entities.WallTiers.Reinforced;
        Color shell = timber ? WallTimber : WallStone;
        Color trim  = timber ? WallTimberDk : WallStoneDark;
        Color cap   = reinforced ? WallIron : (timber ? WallTimberDk : WallMarble);

        const float passageHalf = 1.5f;              // 3 m opening
        float bastionLen = Mathf.Max(0.6f, span * 0.5f - passageHalf);
        float pierW = WallThick + 0.45f;
        float bastionTop = 4.2f;
        float lintelY = 3.30f;

        // ── The two solid thirds ──
        for (int side = -1; side <= 1; side += 2)
        {
            string id = side < 0 ? "L" : "R";
            float zc = side * (passageHalf + bastionLen * 0.5f);

            WallPrim(PrimitiveType.Cube, $"1_Plinth{id}", root.transform,
                new Vector3(0f, 0.18f, zc),
                new Vector3(pierW + 0.35f, 0.36f, bastionLen + 0.05f), trim);
            WallPrim(PrimitiveType.Cube, $"2_Bastion{id}", root.transform,
                new Vector3(0f, 0.36f + (bastionTop - 0.36f) * 0.5f, zc),
                new Vector3(pierW, bastionTop - 0.36f, bastionLen + 0.05f), shell);
            WallPrim(PrimitiveType.Cube, $"3_Course{id}", root.transform,
                new Vector3(0f, 1.05f, zc),
                new Vector3(pierW + 0.08f, 0.06f, bastionLen + 0.08f), trim);
            // Arrow slit on the outer face of each bastion.
            WallPrim(PrimitiveType.Cube, $"3_Slit{id}", root.transform,
                new Vector3(pierW * 0.5f + 0.03f, 2.45f, zc),
                new Vector3(0.10f, 0.60f, 0.18f), trim);
            WallPrim(PrimitiveType.Cube, $"4_Cap{id}", root.transform,
                new Vector3(0f, bastionTop + 0.09f, zc),
                new Vector3(pierW + 0.30f, 0.18f, bastionLen + 0.05f), cap,
                metallic: reinforced ? 0.8f : 0f, smoothness: timber ? 0.2f : 0.45f);
            AddWallMerlonRow(root.transform, $"5_Merlon{id}", bastionLen,
                bastionTop + 0.18f, 0.85f, shell);

            if (reinforced)
                WallPrim(PrimitiveType.Cube, $"6_Shield{id}", root.transform,
                    new Vector3(pierW * 0.5f + 0.11f, 1.70f, zc),
                    new Vector3(0.16f, 1.15f, 0.80f), WallSteel, metallic: 0.9f, smoothness: 0.65f);
        }

        // ── The archway between them ──
        // Jamb posts framing the opening.
        for (int side = -1; side <= 1; side += 2)
        {
            WallPrim(PrimitiveType.Cube, side < 0 ? "1_JambL" : "1_JambR", root.transform,
                new Vector3(0f, lintelY * 0.5f, side * (passageHalf - 0.12f)),
                new Vector3(pierW + 0.12f, lintelY, 0.24f), trim);
        }
        // Arch ring on the outer facade over the opening.
        WallPrimRot(PrimitiveType.Cylinder, "2_ArchRing", root.transform,
            new Vector3(pierW * 0.5f + 0.06f, 2.60f, 0f),
            new Vector3(passageHalf * 2f + 0.5f, 0.06f, passageHalf * 2f + 0.5f),
            Quaternion.Euler(0f, 0f, 90f), trim);
        // Lintel bridging the bastions, and the crown over it.
        WallPrim(PrimitiveType.Cube, "3_Lintel", root.transform,
            new Vector3(0f, lintelY + 0.45f, 0f),
            new Vector3(pierW + 0.05f, 0.90f, passageHalf * 2f + 0.10f), shell);
        WallPrim(PrimitiveType.Cube, "4_LintelCap", root.transform,
            new Vector3(0f, lintelY + 0.99f, 0f),
            new Vector3(pierW + 0.30f, 0.18f, passageHalf * 2f + 0.10f), cap,
            metallic: reinforced ? 0.8f : 0f, smoothness: timber ? 0.2f : 0.45f);
        AddWallMerlonRow(root.transform, "5_MerlonC", passageHalf * 2f, lintelY + 1.08f, 0.85f, shell);

        // ── The moving part ──
        // Each leaf hangs off a hinge pivot at its jamb, so the swing is a
        // rotation about that post and not a slide.
        var doors = root.AddComponent<TheWaningBorder.Rendering.WallGateDoors>();
        doors.Gate = entity;
        float leafLen = passageHalf - 0.10f;
        for (int side = -1; side <= 1; side += 2)
        {
            string id = side < 0 ? "L" : "R";
            var hinge = new GameObject($"GateHinge{id}");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(0f, 0f, side * (passageHalf - 0.12f));

            // The leaf's own centre is half its length in from the hinge.
            WallPrim(PrimitiveType.Cube, $"GateDoor{id}", hinge.transform,
                new Vector3(0f, 1.55f, -side * leafLen * 0.5f),
                new Vector3(0.18f, 2.90f, leafLen), WallWood, smoothness: 0.2f);
            WallPrim(PrimitiveType.Cube, $"DoorBandLow{id}", hinge.transform,
                new Vector3(0.10f, 0.95f, -side * leafLen * 0.5f),
                new Vector3(0.06f, 0.12f, leafLen * 0.92f), WallIron, metallic: 0.85f, smoothness: 0.5f);
            WallPrim(PrimitiveType.Cube, $"DoorBandHigh{id}", hinge.transform,
                new Vector3(0.10f, 2.20f, -side * leafLen * 0.5f),
                new Vector3(0.06f, 0.12f, leafLen * 0.92f), WallIron, metallic: 0.85f, smoothness: 0.5f);
            WallPrim(PrimitiveType.Sphere, $"DoorRing{id}", hinge.transform,
                new Vector3(0.14f, 1.55f, -side * (leafLen - 0.25f)),
                Vector3.one * 0.16f, WallBrass, metallic: 0.9f, smoothness: 0.6f);

            // The procedural leaves are already hinged at their jamb, and
            // `side` is the end they hang from.
            doors.AddLeaf(hinge.transform, side);
        }
        doors.SnapToState();

        // Lantern over the outer arch mouth (the gate's single emissive).
        AddWallNightLight(root.transform, new Vector3(pierW * 0.5f + 0.35f, 3.05f, 0f), 1.5f, 7f);

        // Faction pennants on the bastion crowns.
        for (int side = -1; side <= 1; side += 2)
        {
            string id = side < 0 ? "L" : "R";
            float zc = side * (passageHalf + bastionLen * 0.5f);
            WallPrim(PrimitiveType.Cylinder, $"7_PennantPole_{id}", root.transform,
                new Vector3(0f, bastionTop + 0.90f, zc),
                new Vector3(0.06f, 0.60f, 0.06f), WallIron, metallic: 0.6f, smoothness: 0.45f);
            WallPrim(PrimitiveType.Cube, $"Stripe_Pennant_{id}", root.transform,
                new Vector3(0f, bastionTop + 1.20f, zc - side * 0.32f),
                new Vector3(0.05f, 0.40f, 0.50f), Color.white);
        }

        // Colliders: the two bastions only, so a click never blocks the
        // passage and units path through the middle third.
        for (int side = -1; side <= 1; side += 2)
        {
            float zc = side * (passageHalf + bastionLen * 0.5f);
            var jc = new GameObject(side < 0 ? "ColL" : "ColR");
            jc.transform.SetParent(root.transform, false);
            jc.transform.localPosition = new Vector3(0f, (bastionTop + 0.6f) * 0.5f, zc);
            var bc = jc.AddComponent<BoxCollider>();
            bc.size = new Vector3(pierW + 0.1f, bastionTop + 0.6f, bastionLen + 0.10f);
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
