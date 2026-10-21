// PresentationSpawnSystem.Walls.cs
// Alanthor wall procedural generation (hubs, segments, instances, towers, gates)
// Compact-wall rework (2026-08-09): walls are solid curtain walls — a 1 m-thick
// masonry line with a crenellated crown at ~2.6 m. The walkable deck, ramps and
// garrison geometry of the 2026-05-29 rampart rework are gone from the visuals;
// sim-side wall-top layer contracts (LayeredMoveSystem / WallGarrisonSystem)
// are untouched and keep compiling against their own constants.

using System.Collections.Generic;
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
    // ALANTHOR WALL VISUALS — every piece is the art in WallModuleArt.asset
    // Local frame for instances/towers/gates: +Z runs ALONG the wall,
    // +X is the OUTER (enemy) face, -X is the INNER (friendly) face. Hubs use
    // identity rotation (they are omnidirectional connection points).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The scale the wall set is drawn at: the curtain module's, so every
    /// piece shares one world scale. Null when there is no curtain art, which
    /// leaves each piece to fit its own footprint as before.
    /// </summary>
    private static float? WallArtScale(byte tier, byte culture = Cultures.None)
    {
        var curtain = WallModuleArt.For(tier, culture);
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

    /// <summary>Wall parts already reported missing (part, level, culture).</summary>
    private static readonly HashSet<(WallModuleArt.WallPart, byte, byte)> _missingWallArt
        = new HashSet<(WallModuleArt.WallPart, byte, byte)>();

    /// <summary>
    /// A wall piece with no art in WallModuleArt.asset for its level: a DATA
    /// bug, never drawn procedurally (docs/Design/Art_Direction.md § 13 — the
    /// presentation takes its models only from the SOs). Logged once per part
    /// and level; the piece keeps its pick box so it can still be selected.
    /// </summary>
    private GameObject MissingWallArt(GameObject root, Entity entity, WallModuleArt.WallPart part,
        byte tier, byte culture, Vector3 pick)
    {
        if (_missingWallArt.Add((part, tier, culture)))
            Debug.LogError($"[Wall] no art for {part} at wall level {tier} (culture {culture}) - fill its slot " +
                           "in WallModuleArt.asset (Waning Border > Art > Bind Building Art).");
        if (root.GetComponent<Collider>() == null)
        {
            var col = root.AddComponent<BoxCollider>();
            col.size = pick;
            col.center = Vector3.up * (pick.y * 0.5f);
        }
        if (root.GetComponent<EntityReference>() == null)
            root.AddComponent<EntityReference>().Entity = entity;
        if (_em.HasComponent<LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<LocalTransform>(entity).Rotation;
        return root;
    }

    /// <summary>
    /// Drop the level-authored art for a single-piece wall part (the wall
    /// tower, the emplacement bastions) under <paramref name="root"/>, with its
    /// pick box, owner colour and the entity's facing. With
    /// <paramref name="deckY"/>, the art is shifted so its "Deck" marker lands
    /// at that height. False when the part has no art at this level.
    /// </summary>
    private bool TryAuthoredWallPiece(GameObject root, Entity entity, WallModuleArt.WallPart part,
        float fitLength, string name, float? deckY)
    {
        byte tier = WallArtTierOf(entity, out byte culture);
        var art = WallModuleArt.ForTier(part, tier, fitLength, orientAlongZ: true,
                                        explicitScale: WallArtScale(tier, culture), culture: culture);
        var go = WallModuleArt.Instantiate(art, root.transform, name);
        if (go == null) return false;

        if (deckY.HasValue)
        {
            Transform deck = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "Deck") { deck = t; break; }
            if (deck != null)
            {
                float dy = deckY.Value - (deck.position.y - root.transform.position.y);
                go.transform.localPosition += new Vector3(0f, dy, 0f);
            }
            else Debug.LogWarning($"[Wall] {name} art has no 'Deck' marker — the engine may float.");
        }

        ApplyWallOwnerColor(go, entity);
        float top = Mathf.Max(2.6f, art.Height + (deckY.HasValue ? 0.3f : 0f));
        var col = root.AddComponent<BoxCollider>();
        col.size = new Vector3(PickThick(entity) + 1.4f, top, fitLength + 0.1f);
        col.center = Vector3.up * (top * 0.5f);
        var er = root.AddComponent<EntityReference>();
        er.Entity = entity;
        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        return true;
    }

    /// <summary>
    /// The placement ghost of a wall HUB: the hub art a new hub of this kind
    /// would draw for <paramref name="faction"/> now — its art level and
    /// culture set, at the curtain's scale, exactly as CreateProceduralWallHub
    /// draws the real one. Null (logged) when the slot is empty.
    /// </summary>
    public static GameObject CreateWallHubPreview(EntityManager em, Faction faction, bool palisade)
    {
        byte tier = TheWaningBorder.Entities.WallTiers.ArtLevelFor(em, faction, palisade);
        byte culture = CultureConfig.GetCompletedCulture(em, faction);
        var art = WallModuleArt.ForTier(WallModuleArt.WallPart.Hub, tier,
            TheWaningBorder.Entities.AlanthorWall.HubWidth, orientAlongZ: false,
            explicitScale: WallArtScale(tier, culture), culture: culture);
        if (art == null)
        {
            if (_missingWallArt.Add((WallModuleArt.WallPart.Hub, tier, culture)))
                Debug.LogError($"[Wall] no art for Hub at wall level {tier} (culture {culture}) - fill its slot " +
                               "in WallModuleArt.asset (Waning Border > Art > Bind Building Art).");
            return null;
        }
        var root = new GameObject("WallHubPreview");
        WallModuleArt.Instantiate(art, root.transform, "Hub");
        return root;
    }

    /// <summary>The wall level this piece is clad at. Everything the wall
    /// draws branches on it — docs/Design/Age_1_Alanthor.md § The three wall
    /// levels.</summary>
    private byte WallTierOf(Entity entity)
        => TheWaningBorder.Entities.WallTiers.Of(_em, entity);

    /// <summary>
    /// The level and owner culture a piece's AUTHORED ART is picked by
    /// (WallModuleArt.asset: the level ladder, or the culture's own set from
    /// level 1). Differs from <see cref="WallTierOf"/> for a palisade raised
    /// to its faction's cultured level — the Feraldis Wooden Wall keeps sim
    /// level 0 and draws its culture set at level 1
    /// (WallTiers.ArtLevelOf). The procedural fallbacks keep branching on
    /// <see cref="WallTierOf"/>, so a missing culture set never turns a
    /// palisade to stone.
    /// </summary>
    private byte WallArtTierOf(Entity entity, out byte culture)
    {
        culture = TheWaningBorder.Entities.WallTiers.ArtCultureOf(_em, entity);
        return TheWaningBorder.Entities.WallTiers.ArtLevelOf(_em, entity);
    }

    /// <summary>How thick a piece's pick box is: the wall's own depth — a
    /// 1 m palisade, a 4 m stone wall (docs/Design/Age_1_Alanthor.md § The
    /// stone wall) — so a click anywhere on the walk selects it.</summary>
    private float PickThick(Entity entity)
        => TheWaningBorder.Entities.AlanthorWall.DepthOf(
               TheWaningBorder.Entities.AlanthorWall.IsPalisade(_em, entity));

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

    /// <summary>
    /// A DRAWN wall's segment (docs/Design/Age_1_Alanthor.md § Drawing walls):
    /// no modules — one continuous mesh swept along the curve, owned by a
    /// WallCurveVisual that raises it with construction and opens breaches.
    /// No collider: clicks fall through to the cells' pick boxes beneath.
    /// </summary>
    private GameObject CreateProceduralCurvedWall(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallCurve_{entity.Index}");
        // The mesh is baked in world space through the root's worldToLocal
        // matrix AT BUILD TIME, and Init builds it immediately. So the root
        // must already stand where SyncView is about to put it (the segment's
        // midpoint, turned by the building +180) — it used to start at the
        // origin with identity rotation, and the SyncTransforms pass later the
        // same frame then carried the world-space mesh off to the midpoint and
        // spun it 180 degrees: the curtain flashed at a wrong spot until the
        // visual's next poll noticed the move. Same pose SyncView writes.
        if (_em.HasComponent<LocalTransform>(entity))
        {
            var lt = _em.GetComponentData<LocalTransform>(entity);
            Vector3 p = lt.Position;
            p.y = global::TheWaningBorder.World.Terrain.TerrainUtility.GetSurfaceHeight(p.x, p.z, lt.Position.y);
            root.transform.SetPositionAndRotation(p, VisualRotation(entity, lt.Rotation));
            root.transform.localScale = Vector3.one * lt.Scale;
        }
        else
        {
            root.transform.position = Vector3.zero;
        }
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
        boxCol.size = new Vector3(PickThick(entity) + 0.3f, CrownTop, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * (CrownTop * 0.5f);
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;
        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        return root;
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
        byte artTier = WallArtTierOf(entity, out byte artCulture);
        var art = WallModuleArt.For(artTier, artCulture);
        var module = WallModuleArt.Instantiate(art, root.transform, "Module");
        if (module != null)
        {
            // Same seam-closing overlap the baked curtain uses.
            var ms = module.transform.localScale;
            ms.z *= 1f + TheWaningBorder.Entities.AlanthorWall.ModuleOverlap;
            module.transform.localScale = ms;

            var artCol = root.AddComponent<BoxCollider>();
            artCol.size = new Vector3(PickThick(entity) + 0.3f, Mathf.Max(1f, art.Height), ModuleLen + 0.1f);
            artCol.center = Vector3.up * (Mathf.Max(1f, art.Height) * 0.5f);
            var artRef = root.AddComponent<EntityReference>();
            artRef.Entity = entity;
            if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
                root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
            return root;
        }

        return MissingWallArt(root, entity, WallModuleArt.WallPart.Curtain, artTier, artCulture,
            new Vector3(PickThick(entity) + 0.3f, CrownTop, ModuleLen + 0.1f));
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

        // Authored bastion for the faction's wall level wins. Its "Deck"
        // marker is lifted to EmplacementDeckHeight — the SAME number the
        // engine's simulated Y is built from — so the engine stands on it.
        if (TryAuthoredWallPiece(root, entity,
                trebuchet ? WallModuleArt.WallPart.TrebuchetMount : WallModuleArt.WallPart.BallistaMount,
                ModuleLen, trebuchet ? "TrebuchetMount" : "BallistaMount",
                deckY: TheWaningBorder.Entities.AlanthorWall.EmplacementDeckHeight))
            return root;

        byte mountTier = WallArtTierOf(entity, out byte mountCulture);
        return MissingWallArt(root, entity,
            trebuchet ? WallModuleArt.WallPart.TrebuchetMount : WallModuleArt.WallPart.BallistaMount,
            mountTier, mountCulture, new Vector3(PickThick(entity) + 1.4f, 4.3f, ModuleLen + 0.1f));
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
        ForgetView(entity);
    }

    /// <summary>
    /// Drop the entity's view bookkeeping so the spawn pass builds it again.
    /// The spawn query is REACTIVE (it skips everything whose
    /// PresentationViewSpawned bit is on), so the bit must be cleared too —
    /// without it a respawned piece was never spawned again and simply
    /// vanished. Not structural: an enable bit, like FlushViewTags' own write.
    /// </summary>
    private void ForgetView(Entity entity)
    {
        _spawnedEntities.Remove(entity);
        _viewSync.Remove(entity);   // cached components belonged to the old view
        if (_em.Exists(entity) && _em.HasComponent<PresentationViewSpawned>(entity))
            _em.SetComponentEnabled<PresentationViewSpawned>(entity, false);
    }

    // ── Re-clad with the level-up wave (2026-10-20) ───────────────────
    //
    // A wall level, a palisade's cultured level and a tower / gate / engine
    // conversion re-clad the piece by rebuilding its view. Every other
    // building's level-up dissolves the old look into the new one
    // (BuildingDissolveTransition) with the gold flourish
    // (BuildingLevelUpEffect); walls just popped. The OLD view is kept here
    // until the spawn pass has built the new one — a curtain segment builds
    // its mesh a frame or more later (WallCurveVisual's rebuild budget) —
    // and the wave then runs between the two.

    private struct PendingReclad
    {
        public GameObject Old;
        public float Deadline;
        public bool Flourish;
    }

    private readonly Dictionary<Entity, PendingReclad> _pendingReclads = new();
    private readonly List<Entity> _recladScratch = new();

    /// <summary>Seconds the old view waits for its replacement before it
    /// is simply removed (spawn budget, a piece that died meanwhile).</summary>
    private const float RecladWaitSeconds = 3f;

    /// <summary>
    /// <see cref="ForceRespawn"/>, with the level-up dissolve from the old
    /// view to the new one. <paramref name="flourish"/> adds the gold burst
    /// — meant for the pieces that read as structures (hubs, towers, gates),
    /// not every curtain segment of a faction-wide re-clad.
    /// </summary>
    public void ForceRespawnWithTransition(Entity entity, bool flourish)
    {
        GameObject oldGo = null;
        if (EntityViewManager.Instance != null &&
            EntityViewManager.Instance.TryGetView(entity, out oldGo) && oldGo != null)
            EntityViewManager.Instance.UnregisterView(entity);

        // A second re-clad before the first finished: the older view goes.
        if (_pendingReclads.TryGetValue(entity, out var prior))
        {
            if (prior.Old != null) Destroy(prior.Old);
            _pendingReclads.Remove(entity);
        }

        if (oldGo != null)
        {
            // The old curtain must hold its OLD look while it waits: left
            // running it would see the new level and re-clad itself first.
            foreach (var curve in oldGo.GetComponentsInChildren<WallCurveVisual>())
                curve.enabled = false;
            if (HasDrawnMesh(oldGo))
                _pendingReclads[entity] = new PendingReclad
                {
                    Old = oldGo,
                    Deadline = Time.time + RecladWaitSeconds,
                    Flourish = flourish,
                };
            else Destroy(oldGo);   // nothing visible to dissolve
        }
        ForgetView(entity);
    }

    /// <summary>Start the wave for every re-clad whose new view is ready.
    /// Runs after the spawn pass, each frame; free when nothing is pending.</summary>
    private void PumpPendingReclads()
    {
        if (_pendingReclads.Count == 0) return;
        _recladScratch.Clear();
        _recladScratch.AddRange(_pendingReclads.Keys);
        for (int i = 0; i < _recladScratch.Count; i++)
        {
            var e = _recladScratch[i];
            var p = _pendingReclads[e];
            if (p.Old == null) { _pendingReclads.Remove(e); continue; }

            GameObject newGo = null;
            bool ready = _em.Exists(e)
                && EntityViewManager.Instance != null
                && EntityViewManager.Instance.TryGetView(e, out newGo) && newGo != null
                && HasDrawnMesh(newGo);
            if (!ready)
            {
                if (!_em.Exists(e) || Time.time > p.Deadline)
                {
                    Destroy(p.Old);
                    _pendingReclads.Remove(e);
                }
                continue;
            }

            Color accent = _em.HasComponent<FactionTag>(e)
                ? FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value)
                : new Color(1f, 0.85f, 0.45f);
            TheWaningBorder.Rendering.BuildingDissolveTransition.Begin(p.Old, newGo, 1.5f, accent);
            if (p.Flourish) TheWaningBorder.Rendering.BuildingLevelUpEffect.Spawn(newGo, accent);
            _pendingReclads.Remove(e);
        }
    }

    /// <summary>True when the view draws something — a curtain segment's
    /// mesh is built after its GameObject, and an empty view has nothing to
    /// dissolve.</summary>
    private static bool HasDrawnMesh(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: false);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r is SkinnedMeshRenderer) return true;
            if (r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0) return true;
            }
        }
        return false;
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
        byte hubArtTier = WallArtTierOf(entity, out byte hubCulture);
        var hubArt = WallModuleArt.ForTier(WallModuleArt.WallPart.Hub, hubArtTier,
            TheWaningBorder.Entities.AlanthorWall.HubWidth, orientAlongZ: false,
            explicitScale: WallArtScale(hubArtTier, hubCulture), culture: hubCulture);
        var hubGo = WallModuleArt.Instantiate(hubArt, root.transform, "Hub");
        if (hubGo != null)
        {
            ApplyWallOwnerColor(hubGo, entity);
            float top = Mathf.Max(2f, hubArt.Height);
            var hubCol = root.AddComponent<BoxCollider>();
            hubCol.size = new Vector3(TheWaningBorder.Entities.AlanthorWall.HubWidth, top,
                                      TheWaningBorder.Entities.AlanthorWall.HubWidth);
            hubCol.center = Vector3.up * (top * 0.5f);
            var hubRef = root.AddComponent<EntityReference>();
            hubRef.Entity = entity;
            return root;
        }

        return MissingWallArt(root, entity, WallModuleArt.WallPart.Hub, hubArtTier, hubCulture,
            new Vector3(TheWaningBorder.Entities.AlanthorWall.HubWidth, 4f, TheWaningBorder.Entities.AlanthorWall.HubWidth));
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

        // Authored wall-tower art for the faction's wall level wins, at the
        // curtain's scale and turned along the wall like a module.
        if (TryAuthoredWallPiece(root, entity, WallModuleArt.WallPart.Tower, ModuleLen, "Tower",
                                 deckY: null))
            return root;

        byte towerTier = WallArtTierOf(entity, out byte towerCulture);
        return MissingWallArt(root, entity, WallModuleArt.WallPart.Tower, towerTier, towerCulture,
            new Vector3(PickThick(entity) + 1.4f, 7f, ModuleLen + 0.1f));
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
        else BuildWallGateFlankModule(root, entity);

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
        // Every gatehouse is FITTED to the span (2026-10-11): the curtain's
        // swept mesh leaves exactly WallGateSpan open for it (WallOpenings),
        // so a gatehouse drawn at any other length showed a sliver of
        // missing wall at each side. The timber gate used to keep the
        // curtain's scale, and was the one that did.
        byte gateTier = WallArtTierOf(entity, out byte gateCulture);
        var gateArt = WallModuleArt.ForTier(WallModuleArt.WallPart.Gate, gateTier,
            span, orientAlongZ: true,
            culture: gateCulture);
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

        MissingWallArt(root, entity, WallModuleArt.WallPart.Gate, gateTier, gateCulture,
            new Vector3(WallThick + 0.6f, 4f, span));
    }

    /// <summary>Flanking gate module: a solid gatehouse bastion, taller than the
    /// curtain, stepping the silhouette up toward the central arch.</summary>
    private void BuildWallGateFlankModule(GameObject root, Entity entity)
    {
        byte tier = WallArtTierOf(entity, out byte culture);
        var art = WallModuleArt.For(tier, culture);
        if (WallModuleArt.Instantiate(art, root.transform, "Flank") == null)
        {
            MissingWallArt(root, entity, WallModuleArt.WallPart.Curtain, tier, culture,
                new Vector3(WallThick + 0.6f, 3.8f, ModuleLen + 0.1f));
            return;
        }
        ApplyWallOwnerColor(root, entity);
        var boxCol = root.AddComponent<BoxCollider>();
        float top = Mathf.Max(3f, art.Height);
        boxCol.size = new Vector3(WallThick + 0.6f, top, ModuleLen + 0.1f);
        boxCol.center = Vector3.up * (top * 0.5f);
    }
}
