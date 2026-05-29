// NavMeshManager.cs
//
// Single source of navmesh pathing. Exposes RequestPath / RequestPathBattalion
// / SnapToNavMesh (wrapping NavMesh.CalculatePath / SamplePosition).
//
// Runtime-bakes the navmesh from the active Unity Terrain heightmap on startup
// (RemoveAllNavMeshData first, so any stale scene NavMeshSurface is discarded —
// a pre-baked surface tends to capture the wrong / a flat off-terrain tile),
// then incrementally re-bakes whenever the ECS building/obstacle set changes.
//
// Two navmeshes are maintained from the same sources:
//  - Unit navmesh (default agent type 0): radius ~ unit size.
//  - Battalion navmesh (runtime-created agent type, wider radius = battalion
//    half-width) for battalion leaders, so a formation routes around gaps too
//    narrow to hold it.
//
// Architecture:
//  - Terrain: NavMeshBuildSource of shape Terrain covering the whole map.
//  - Buildings/obstacles: one Box source per BuildingTag/ObstacleTag entity,
//    sized from BuildingSize (or Radius). Synced every RebuildInterval; rebuilt
//    only when the source set changes.
//  - Walls / wall instances are included since they carry BuildingTag.
//
// Implementation notes:
//  - Uses NavMeshBuilder.UpdateNavMeshDataAsync so the bake doesn't block the
//    main thread on big rebuilds.
//  - Agent slope is tightened to 30° and climb to 0.3 m so steep terrain bakes
//    as impassable (holes the path routes around).
//
// Location: Assets/Scripts/Systems/Movement/NavMeshManager.cs

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Systems.Movement
{
    public class NavMeshManager : MonoBehaviour
    {
        public static NavMeshManager Instance { get; private set; }

        // Re-sync the building set on this cadence. 2 Hz is plenty for an
        // RTS — buildings spawn / die every few seconds at most.
        private const float RebuildInterval = 0.5f;

        // How tall to make the box source for each building, in world units.
        // Just needs to be taller than agent height (2 m) and the agent's
        // climb (0.4 m) so the navmesh treats it as a real obstacle.
        private const float BuildingSourceHeight = 5f;

        // How a building contributes geometry to the bake.
        //  Generic   — axis-aligned full-height box (acts as an obstacle: tall
        //              walkable box whose steep sides the slope budget rejects).
        //  WallBody  — oriented solid box up to the deck height; its flat top
        //              becomes the walkable rampart deck. Sides stay cliffs.
        //  WallDeck  — gate: a thin walkable slab at deck height only, so the
        //              ground tunnel underneath stays open (pass-through).
        private enum SourceKind : byte { Generic, WallBody, WallDeck }

        // Walkable-rampart navmesh constants (mirror PresentationSpawnSystem.Walls
        // + AlanthorWall). The ramp slope (atan(DeckHeight/RampRun) ≈ 26.6°) sits
        // under _settings.agentSlope (30°) so the bake treats it as walkable.
        private const float WallDeckHeight = 4f;
        private const float WallW_NM       = 9f; // wall width across (matches AlanthorWall.WallWidth)
        private const float WallRampRun    = 8f;
        private const float WallRampWidth  = 3f;
        private const float WallRampDeckX  = 4f; // deck-edge X where the ramp tops out (inner side)

        // Internal record per known building so we can detect set changes
        // without rebuilding every tick.
        private struct BuildingRecord
        {
            public Vector3 Position;
            public Vector2 Size;
            public Quaternion Rotation;
            public float Height;
            public SourceKind Kind;
            public bool RampHost; // hub/tower/gate → also emit a ramp up to the deck
        }

        private NavMeshData _data;
        private NavMeshDataInstance _instance;
        private NavMeshBuildSettings _settings;
        private List<NavMeshBuildSource> _sources;
        private Dictionary<Entity, BuildingRecord> _knownBuildings;
        private Bounds _bounds;
        private bool _isBaked;
        private bool _isBaking;
        private float _rebuildTimer;
        private bool _dirty;

        // Battalion-leader pathing. A SECOND navmesh, baked from the same
        // sources but for a WIDER agent (radius = battalion half-width) under
        // its own runtime-created agent type. The leader queries this mesh so
        // its route keeps enough clearance for the whole formation and routes
        // around gaps too narrow to hold the battalion; members just follow the
        // leader. Radius is fixed at bake time (Unity bakes erosion per
        // navmesh, not per entity), so all battalions share this value — size
        // it to the widest formation. Standard formation is 5 columns × 1.5 m
        // spacing → half-width 3 m; +~0.5 m member body ≈ 3.5 m.
        private const float BattalionAgentRadius = 3.5f;
        private NavMeshData _battalionData;
        private NavMeshDataInstance _battalionInstance;
        private NavMeshBuildSettings _battalionSettings;
        private int _battalionAgentTypeId;
        private bool _battalionSettingsCreated;
        private bool _battalionBaked;

        public bool IsBaked => _isBaked;
        public bool IsBaking => _isBaking;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            _sources = new List<NavMeshBuildSource>(64);
            _knownBuildings = new Dictionary<Entity, BuildingRecord>(64);
            _settings = NavMesh.GetSettingsByID(0);
            // Default agent slope is 45° — that lets units walk onto the
            // shoulders of mountains since the ridge noise often stays under
            // tan(45°)=1.0 along their flanks. Tighten to 30° so mountains
            // bake as impassable in the NavMesh (matches the slope budgets
            // used by ProceduralHeightmap for playable regions).
            _settings.agentSlope = 30f;
            // Step height — keep low so a 0.5m ridge isn't auto-climbed.
            _settings.agentClimb = 0.3f;

            // Runtime agent type for battalion-leader pathing (wider agent).
            // CreateSettings registers a fresh agent type + ID with no editor
            // setup; we override its radius to the battalion half-width and
            // mirror the unit agent's height/slope/climb.
            _battalionSettings = NavMesh.CreateSettings();
            _battalionAgentTypeId = _battalionSettings.agentTypeID;
            _battalionSettings.agentRadius = BattalionAgentRadius;
            _battalionSettings.agentHeight = _settings.agentHeight;
            _battalionSettings.agentSlope = _settings.agentSlope;
            _battalionSettings.agentClimb = _settings.agentClimb;
            _battalionSettingsCreated = true;
        }

        private void OnDestroy()
        {
            if (_instance.valid) _instance.Remove();
            if (_data != null) Object.DestroyImmediate(_data);
            if (_battalionInstance.valid) _battalionInstance.Remove();
            if (_battalionData != null) Object.DestroyImmediate(_battalionData);
            if (_battalionSettingsCreated)
            {
                try { NavMesh.RemoveSettings(_battalionAgentTypeId); } catch { /* already gone */ }
                _battalionSettingsCreated = false;
            }
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            yield return null;

            // Build the navmesh at runtime from the Unity Terrain heightmap so it
            // drapes the terrain. We deliberately ignore any pre-baked
            // NavMeshSurface in the scene — it captured a flat off-terrain tile
            // (constant Y, no holes), which makes CalculatePath return straight
            // lines. Re-bakes on building/obstacle changes via the SyncBuildings
            // path; RemoveAllNavMeshData (in InitialBake) clears the stale surface.
            yield return StartCoroutine(InitialBake());
        }

        private IEnumerator InitialBake()
        {
            // Wait for the procedural Terrain to exist AND for its async
            // heightmap generation to finish. Baking against the empty
            // (all-zero) heightmap would produce a flat navmesh that's
            // out of sync with the eventual eroded terrain.
            // Use the ACTIVE terrain (the playable "Main Terrain"), not
            // FindFirstObjectByType — the scene has extra terrain tiles outside
            // the map, and FindFirstObjectByType returns an arbitrary one. The
            // wrong tile is exactly what the broken pre-baked NavMeshSurface
            // captured. GameBootstrap already resolves activeTerrain to "Main
            // Terrain", and TerrainUtility/GetHeight use the same.
            UnityEngine.Terrain terrain = null;
            float waited = 0f;
            while (terrain == null ||
                   !TheWaningBorder.World.Terrain.ProceduralTerrain.IsGenerationComplete)
            {
                terrain = TheWaningBorder.World.Terrain.TerrainUtility.GetActiveTerrain();
                if (terrain != null && terrain.terrainData != null &&
                    TheWaningBorder.World.Terrain.ProceduralTerrain.IsGenerationComplete) break;
                terrain = null;
                waited += Time.deltaTime;
                if (waited > 30f)
                {
                    Debug.LogError("[NavMeshManager] Timed out waiting for Terrain after 30s.");
                    yield break;
                }
                yield return null;
            }

            // Terrain source covers the whole procedural map.
            _sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Terrain,
                sourceObject = terrain.terrainData,
                transform = Matrix4x4.TRS(terrain.transform.position, Quaternion.identity, Vector3.one),
                size = terrain.terrainData.size,
                area = 0,
            });

            // Scene-level static obstacles (cliffs, rocks, hand-placed props).
            // Each NavMeshStaticObstacle feeds its MeshFilter mesh in as a Mesh
            // source — the bake routes around the exact silhouette rather than
            // a box approximation. Static at runtime: collected once here, not
            // re-synced in Update (cliffs are scene decoration, not gameplay
            // entities that move).
            var statics = Object.FindObjectsByType<NavMeshStaticObstacle>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < statics.Length; i++)
            {
                var so = statics[i];
                var mf = so.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                _sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = mf.sharedMesh,
                    transform = so.transform.localToWorldMatrix,
                    area = 0, // Walkable tag; the bake naturally excludes the
                              // steep faces (>30° slope budget) so the wall
                              // becomes impassable while flat top surfaces
                              // stay walkable — matches how box-source
                              // buildings act as obstacles.
                });
            }

            // Bounds = the terrain AABB plus a small lift so building boxes
            // stick out of the top.
            var size = terrain.terrainData.size;
            size.y = Mathf.Max(size.y, BuildingSourceHeight + 1f);
            _bounds = new Bounds(
                terrain.transform.position + size * 0.5f,
                size);

            // The scene's pre-baked NavMeshSurface registered a (broken) flat
            // navmesh on the water plane at scene load. Remove ALL existing
            // navmesh data so only our terrain-draped bake remains — otherwise
            // the two coexist and CalculatePath / SamplePosition may snap units
            // to the flat one.
            NavMesh.RemoveAllNavMeshData();

            // First-time data + register.
            _data = new NavMeshData(_settings.agentTypeID);
            _instance = NavMesh.AddNavMeshData(_data);

            // Initial bake. Async so we don't hitch the first frame.
            _isBaking = true;
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(_data, _settings, _sources, _bounds);
            while (!op.isDone) yield return null;
            _isBaking = false;
            _isBaked = true;

            int triCount = NavMesh.CalculateTriangulation().indices.Length / 3;
            TWBLog.Log($"[NavMeshManager] Runtime-baked navmesh from terrain '{terrain.name}' " +
                      $"(agent radius {_settings.agentRadius}, slope {_settings.agentSlope}°, " +
                      $"climb {_settings.agentClimb}) → {triCount} tris draped on the terrain.");

            // Second navmesh for battalion-leader pathing: same sources/bounds,
            // wider agent radius so a formation routes around gaps it can't fit.
            _battalionData = new NavMeshData(_battalionSettings.agentTypeID);
            _battalionInstance = NavMesh.AddNavMeshData(_battalionData);
            _isBaking = true;
            var bop = NavMeshBuilder.UpdateNavMeshDataAsync(_battalionData, _battalionSettings, _sources, _bounds);
            while (!bop.isDone) yield return null;
            _isBaking = false;
            _battalionBaked = true;
            TWBLog.Log($"[NavMeshManager] Battalion navmesh baked (agent type {_battalionAgentTypeId}, radius {BattalionAgentRadius} m).");
        }

        private void Update()
        {
            if (!_isBaked || _isBaking) return;

            _rebuildTimer += Time.deltaTime;
            if (_rebuildTimer < RebuildInterval) return;
            _rebuildTimer = 0f;

            // Re-bake (unit + battalion navmeshes) when the building/obstacle set changes.
            if (SyncBuildings())
                StartCoroutine(Rebuild());
        }

        // Walks the ECS world for the obstacle set (buildings + ObstacleTag
        // map features) and returns one BuildingRecord per entity, consumed by
        // SyncBuildings to drive the re-bake. Returns null if the ECS world
        // isn't ready yet.
        private Dictionary<Entity, BuildingRecord> BuildCurrentRecords()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;
            var em = world.EntityManager;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            // Obstacles (ObstacleTag) — static map features that should
            // carve the navmesh just like buildings do: iron deposits,
            // forest impassable discs, etc. They're treated identically
            // for navmesh baking — both feed Box sources at their footprint.
            var obstacleQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<ObstacleTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Radius>());
            using var obstacleEntities = obstacleQuery.ToEntityArray(Allocator.Temp);

            // Build the new known set.
            var current = new Dictionary<Entity, BuildingRecord>(entities.Length + obstacleEntities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var t = em.GetComponentData<LocalTransform>(e);
                Vector2 sz;
                if (em.HasComponent<BuildingSize>(e))
                {
                    var bs = em.GetComponentData<BuildingSize>(e);
                    sz = new Vector2(bs.Width, bs.Height);
                }
                else if (em.HasComponent<Radius>(e))
                {
                    var r = em.GetComponentData<Radius>(e).Value * 2f; // diameter ≈ box edge
                    sz = new Vector2(r, r);
                }
                else
                {
                    sz = new Vector2(1f, 1f);
                }

                // Walkable-rampart classification. Walls become oriented walkable
                // decks at WallDeckHeight (their flat top is the walkway); hubs/
                // towers/gates also host a ramp up to the deck. Gates emit only a
                // thin deck slab so the ground tunnel underneath stays passable.
                var kind = SourceKind.Generic;
                float height = BuildingSourceHeight;
                bool rampHost = false;
                var rot = Quaternion.identity;
                if (em.HasComponent<WallTag>(e))
                {
                    rot = (Quaternion)t.Rotation;
                    height = WallDeckHeight;
                    bool isGate  = em.HasComponent<WallGateTag>(e);
                    bool isHub   = em.HasComponent<WallHubTag>(e);
                    bool isTower = em.HasComponent<WallTowerTag>(e);
                    kind = isGate ? SourceKind.WallDeck : SourceKind.WallBody;
                    rampHost = isGate || isHub || isTower;
                    // Hub deck is square and at least the full wall width so the
                    // adjacent segment decks meet across it.
                    if (isHub) sz = new Vector2(WallW_NM, WallW_NM);
                }

                current[e] = new BuildingRecord
                {
                    Position = new Vector3(t.Position.x, t.Position.y, t.Position.z),
                    Size = sz,
                    Rotation = rot,
                    Height = height,
                    Kind = kind,
                    RampHost = rampHost,
                };
            }

            // Fold in ObstacleTag entities (iron deposits, forest discs).
            // Each one feeds a Box source sized from its Radius × 2.
            for (int i = 0; i < obstacleEntities.Length; i++)
            {
                var e = obstacleEntities[i];
                if (current.ContainsKey(e)) continue; // BuildingTag already covered it
                var t = em.GetComponentData<LocalTransform>(e);
                var r = em.GetComponentData<Radius>(e).Value;
                if (r <= 0f) r = 0.5f;
                float edge = r * 2f;
                current[e] = new BuildingRecord
                {
                    Position = new Vector3(t.Position.x, t.Position.y, t.Position.z),
                    Size = new Vector2(edge, edge),
                    Rotation = Quaternion.identity,
                    Height = BuildingSourceHeight,
                    Kind = SourceKind.Generic,
                    RampHost = false,
                };
            }

            return current;
        }

        // Walks the ECS world for BuildingTag entities and reconciles them
        // against _knownBuildings. Returns true if the source set changed
        // (any building added, removed, or moved meaningfully).
        private bool SyncBuildings()
        {
            var current = BuildCurrentRecords();
            if (current == null) return false;

            // Detect any change vs known set.
            bool changed = current.Count != _knownBuildings.Count;
            if (!changed)
            {
                foreach (var kvp in current)
                {
                    if (!_knownBuildings.TryGetValue(kvp.Key, out var prev)
                        || (prev.Position - kvp.Value.Position).sqrMagnitude > 0.01f
                        || prev.Size != kvp.Value.Size
                        || prev.Kind != kvp.Value.Kind          // e.g. instance→tower / segment→gate
                        || prev.RampHost != kvp.Value.RampHost
                        || Quaternion.Angle(prev.Rotation, kvp.Value.Rotation) > 1f)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed) return false;

            _knownBuildings = current;

            // Rebuild the source list: 1 terrain source (kept) + N building boxes.
            // Terrain source is at index 0; clear after that and re-add.
            if (_sources.Count > 1) _sources.RemoveRange(1, _sources.Count - 1);
            foreach (var kvp in current)
            {
                AddBuildingSources(kvp.Value);
            }
            return true;
        }

        // Emits the navmesh source(s) for one building record. Generic buildings
        // get a single axis-aligned obstacle box; wall pieces get an oriented
        // walkable deck (+ a ramp for hubs/towers/gates) so units can march on top
        // and climb up/down — see docs/Design/Age_1_Alanthor.md § Walkable Ramparts.
        private void AddBuildingSources(BuildingRecord rec)
        {
            switch (rec.Kind)
            {
                case SourceKind.Generic:
                    _sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(
                            rec.Position + new Vector3(0f, rec.Height * 0.5f, 0f),
                            Quaternion.identity, Vector3.one),
                        size = new Vector3(rec.Size.x, rec.Height, rec.Size.y),
                        area = 0,
                    });
                    break;

                case SourceKind.WallBody:
                    // Oriented solid box up to the deck height; flat top = walkway,
                    // sides stay cliffs (rejected by the 30° slope budget). Walls
                    // only yaw, so the deck stays level (rotation about Y).
                    _sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(
                            rec.Position + new Vector3(0f, rec.Height * 0.5f, 0f),
                            rec.Rotation, Vector3.one),
                        size = new Vector3(rec.Size.x, rec.Height, rec.Size.y),
                        area = 0,
                    });
                    break;

                case SourceKind.WallDeck:
                    // Gate: thin walkable slab at deck height only; the ground tunnel
                    // underneath stays open so units pass through the gate.
                    const float slabT = 0.5f;
                    _sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(
                            rec.Position + new Vector3(0f, rec.Height - slabT * 0.5f, 0f),
                            rec.Rotation, Vector3.one),
                        size = new Vector3(rec.Size.x, slabT, rec.Size.y),
                        area = 0,
                    });
                    break;
            }

            // No ramp source: wall-top access is via doors (WallDoorAccessSystem
            // teleports units between a structure's ground and deck doors), so the
            // deck is intentionally its own navmesh island, unreachable on foot
            // except through a door. (rec.RampHost is retained for diagnostics.)
        }

        // Tilted walkable slab from the ground up to the deck rim on the inner
        // (-X) face — the "stairs". Mirrors PresentationSpawnSystem.Walls.AddWallRamp
        // so the navmesh ramp sits under the visual one.
        private void AddRampSource(BuildingRecord rec)
        {
            Vector3 deckEdge  = new Vector3(-WallRampDeckX, WallDeckHeight, 0f);
            Vector3 groundEnd = deckEdge + new Vector3(-WallRampRun, -WallDeckHeight, 0f);
            Vector3 fwd       = (deckEdge - groundEnd).normalized; // up-slope
            float slabLen     = (deckEdge - groundEnd).magnitude;
            Vector3 midLocal  = (deckEdge + groundEnd) * 0.5f;
            Quaternion slabRotLocal = Quaternion.LookRotation(fwd, Vector3.up);

            _sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(
                    rec.Position + rec.Rotation * midLocal,
                    rec.Rotation * slabRotLocal,
                    Vector3.one),
                size = new Vector3(WallRampWidth, 0.3f, slabLen),
                area = 0,
            });
        }

        private IEnumerator Rebuild()
        {
            _isBaking = true;
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(_data, _settings, _sources, _bounds);
            while (!op.isDone) yield return null;
            // Keep the battalion navmesh in sync with the same source set.
            if (_battalionBaked)
            {
                var bop = NavMeshBuilder.UpdateNavMeshDataAsync(_battalionData, _battalionSettings, _sources, _bounds);
                while (!bop.isDone) yield return null;
            }
            _isBaking = false;
        }

        // ──────────────────────────────────────────────────────────────────
        // PUBLIC QUERY API
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Compute a path from <paramref name="from"/> to <paramref name="to"/>
        /// on the active navmesh. Fills <paramref name="path"/> and returns true
        /// on success. False if the navmesh isn't baked yet, no path exists,
        /// or one of the endpoints isn't on the navmesh.
        /// </summary>
        public bool RequestPath(Vector3 from, Vector3 to, NavMeshPath path)
        {
            if (!_isBaked || path == null) return false;
            // Source snap stays tight (5 m) — the unit's own position is
            // almost always already on or adjacent to the navmesh; a wider
            // search here would just round-trip extra cells. The
            // *destination* search is widened to 20 m so move-to / gather
            // orders against a goal that's inside an impassable obstacle
            // (e.g. a resource deposit at the centre of an iron / crystal
            // patch where the cluster radius can exceed 7 m) still resolve
            // to the nearest navmesh-walkable point on the cluster edge,
            // instead of returning a failed path that drops MovementSystem
            // into direct-line steering through the obstacle.
            if (!NavMesh.SamplePosition(from, out var fromHit, 5f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var toHit, 20f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path);
        }

        /// <summary>True once the wider battalion navmesh has been baked.</summary>
        public bool HasBattalionMesh => _battalionBaked;

        /// <summary>
        /// Compute a path for a battalion LEADER on the wider battalion navmesh
        /// (agent radius = battalion half-width), so the route keeps clearance
        /// for the whole formation. Returns false (caller falls back to the
        /// unit navmesh via <see cref="RequestPath"/>) if the battalion mesh
        /// isn't ready. The agent-type filter selects the battalion navmesh;
        /// CalculatePath maps the endpoints onto it internally (SamplePosition
        /// can't target a non-default agent type, so we don't pre-snap here).
        /// </summary>
        public bool RequestPathBattalion(Vector3 from, Vector3 to, NavMeshPath path)
        {
            if (!_battalionBaked || path == null) return false;
            var filter = new NavMeshQueryFilter
            {
                agentTypeID = _battalionAgentTypeId,
                areaMask = NavMesh.AllAreas,
            };
            return NavMesh.CalculatePath(from, to, filter, path);
        }

        /// <summary>
        /// Snap a world position to the nearest valid navmesh location within
        /// <paramref name="searchRadius"/>. Returns the input position if not
        /// baked or no hit found.
        /// </summary>
        public Vector3 SnapToNavMesh(Vector3 position, float searchRadius = 5f)
        {
            if (!_isBaked) return position;
            return NavMesh.SamplePosition(position, out var hit, searchRadius, NavMesh.AllAreas)
                ? hit.position
                : position;
        }
    }
}
