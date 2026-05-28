// NavMeshManager.cs
//
// Single source of navmesh pathing. Exposes RequestPath / SnapToNavMesh
// (wrapping NavMesh.CalculatePath / SamplePosition) and runs in one of two
// modes, chosen at startup from whether the scene is procedural:
//
//  - Procedural maps (no hand-authored terrain): runtime-bakes the navmesh
//    from the procedural Terrain on startup, then incrementally re-bakes
//    whenever the ECS building/obstacle set changes.
//
//  - Hand-crafted maps (a baked NavMeshSurface ships in the scene, e.g. a
//    MapMagic terrain): adopts that pre-baked navmesh as-is (no runtime
//    bake — re-baking only knows the Terrain heightmap + ECS boxes, so it
//    would flatten the surface over hand-placed cliffs/props and units
//    would walk through them). Dynamic buildings carve the pre-baked
//    surface via NavMeshObstacle components instead of re-baking.
//
// Architecture (procedural mode):
//  - Terrain: NavMeshBuildSource of shape Terrain. Single source covering
//    the whole procedural terrain.
//  - Buildings: one Box source per BuildingTag entity, sized from
//    BuildingSize (or Radius for legacy buildings). Synced every
//    RebuildInterval seconds; rebuilt only when the source set changes.
//  - Walls / wall instances are included since they carry BuildingTag.
//  - Forests / rocks are NOT yet integrated — left to a follow-up.
//
// Implementation notes:
//  - Uses NavMeshBuilder.UpdateNavMeshDataAsync so the bake doesn't
//    block the main thread on big rebuilds.
//  - Holds one shared NavMeshDataInstance for the whole world; we don't
//    yet split into tiles. Per-tile incremental updates can be a later
//    optimisation.
//  - Default agent (GetSettingsByID(0)): radius 0.5 m, height 2 m, slope
//    45°, step 0.4 m — matches our typical units.
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

        // Internal record per known building so we can detect set changes
        // without rebuilding every tick.
        private struct BuildingRecord
        {
            public Vector3 Position;
            public Vector2 Size;
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

        // External (hand-crafted) map mode. When the scene ships its own
        // baked navmesh (NavMeshSurface / Navigation-static bake on a
        // hand-authored MapMagic terrain), we MUST NOT runtime-bake over it:
        // the runtime bake only knows the Terrain heightmap + ECS building
        // boxes, so it flattens the navmesh over hand-placed cliffs / props
        // and units walk straight through them. Instead we use the scene's
        // pre-baked navmesh as-is and carve dynamic buildings into it with
        // NavMeshObstacle components (cheaper than a full re-bake and it
        // respects the author's bake settings). Procedural maps keep the
        // runtime-bake path unchanged.
        private bool _external;
        private readonly Dictionary<Entity, GameObject> _carvers =
            new Dictionary<Entity, GameObject>(64);
        private readonly List<Entity> _removeScratch = new List<Entity>(16);

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
        }

        private void OnDestroy()
        {
            if (_instance.valid) _instance.Remove();
            if (_data != null) Object.DestroyImmediate(_data);
            foreach (var kvp in _carvers)
                if (kvp.Value != null) Destroy(kvp.Value);
            _carvers.Clear();
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            // Wait one frame so GameBootstrap has finished deciding whether to
            // create a ProceduralTerrain (procedural map) or leave the scene's
            // hand-authored terrain alone. Its presence is the authoritative
            // procedural/non-procedural signal — GameBootstrap only spawns one
            // for procedural maps and calls MarkExternalTerrainReady otherwise.
            yield return null;

            _external = Object.FindFirstObjectByType<
                TheWaningBorder.World.Terrain.ProceduralTerrain>() == null;

            if (_external)
                yield return StartCoroutine(InitialAttachExternal());
            else
                yield return StartCoroutine(InitialBake());
        }

        // Non-procedural map: adopt the scene's pre-baked navmesh instead of
        // building our own. Wait until that navmesh is registered (the
        // NavMeshSurface enables it on scene load) and flip IsBaked so the
        // path pipeline (NavMeshPathRequestSystem) starts running against it.
        // Logs verbosely because every common failure here (no surface,
        // wrong agent type) presents identically in-game: units ignore the
        // navmesh and walk in straight lines.
        private IEnumerator InitialAttachExternal()
        {
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Debug.Log($"[NavMeshManager] Hand-crafted map '{sceneName}': adopting the scene's " +
                      "pre-baked NavMesh (runtime re-bake disabled; buildings carve via NavMeshObstacle).");

            // The query API (RequestPath / SnapToNavMesh) uses the DEFAULT
            // agent (id 0). The scene's NavMeshSurface MUST be baked for that
            // same agent or every query silently fails. Dump the agent table.
            int agentCount = NavMesh.GetSettingsCount();
            for (int i = 0; i < agentCount; i++)
            {
                var s = NavMesh.GetSettingsByIndex(i);
                Debug.Log($"[NavMeshManager] agentType[{i}] id={s.agentTypeID} " +
                          $"radius={s.agentRadius} height={s.agentHeight} slope={s.agentSlope} climb={s.agentClimb}");
            }

            float waited = 0f;
            int triCount;
            while ((triCount = NavMesh.CalculateTriangulation().indices.Length / 3) == 0)
            {
                waited += Time.deltaTime;
                if (waited > 30f)
                {
                    Debug.LogError(
                        $"[NavMeshManager] No baked NavMesh loaded for '{sceneName}' after 30s. " +
                        "Add a NavMeshSurface to the terrain (Agent Type = Humanoid), bake it, " +
                        "and make sure the GameObject is active. Pathfinding is DISABLED — units " +
                        "move in straight lines through every obstacle.");
                    yield break;
                }
                yield return null;
            }

            _isBaked = true;
            Debug.Log($"[NavMeshManager] NavMesh loaded for '{sceneName}': {triCount} triangles.");

            // Probe: can the DEFAULT agent actually query this navmesh? If the
            // surface was baked for a non-default agent type, triangulation
            // exists but SamplePosition (default agent) finds nothing — so
            // every RequestPath returns false and units silently direct-line.
            var terrain = Object.FindFirstObjectByType<UnityEngine.Terrain>();
            if (terrain != null && terrain.terrainData != null)
            {
                var centre = terrain.transform.position + (Vector3)terrain.terrainData.size * 0.5f;
                centre.y = terrain.transform.position.y; // sample near ground, not mid-air
                bool hit = NavMesh.SamplePosition(centre, out _, 200f, NavMesh.AllAreas);
                if (!hit)
                    Debug.LogError(
                        $"[NavMeshManager] NavMesh has {triCount} tris but the DEFAULT agent " +
                        "can't sample it (SamplePosition failed near map centre). The scene's " +
                        "NavMeshSurface is almost certainly baked for a NON-default Agent Type. " +
                        "Open the NavMeshSurface, set Agent Type = 'Humanoid', and re-bake — the " +
                        "path queries use the default agent and ignore other agent types.");
                else
                    Debug.Log("[NavMeshManager] Default-agent SamplePosition OK near map centre — " +
                              "path queries should resolve. If units still ignore the navmesh, the " +
                              "issue is path acceptance, not the surface.");
            }
        }

        private IEnumerator InitialBake()
        {
            // Wait for the procedural Terrain to exist AND for its async
            // heightmap generation to finish. Baking against the empty
            // (all-zero) heightmap would produce a flat navmesh that's
            // out of sync with the eventual eroded terrain.
            UnityEngine.Terrain terrain = null;
            float waited = 0f;
            while (terrain == null ||
                   !TheWaningBorder.World.Terrain.ProceduralTerrain.IsGenerationComplete)
            {
                terrain = Object.FindFirstObjectByType<UnityEngine.Terrain>();
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

            // First-time data + register.
            _data = new NavMeshData(_settings.agentTypeID);
            _instance = NavMesh.AddNavMeshData(_data);

            // Initial bake. Async so we don't hitch the first frame.
            _isBaking = true;
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(_data, _settings, _sources, _bounds);
            while (!op.isDone) yield return null;
            _isBaking = false;
            _isBaked = true;
        }

        private void Update()
        {
            if (!_isBaked || _isBaking) return;

            _rebuildTimer += Time.deltaTime;
            if (_rebuildTimer < RebuildInterval) return;
            _rebuildTimer = 0f;

            if (_external)
            {
                // Pre-baked map: carve buildings with NavMeshObstacles, no bake.
                SyncCarvers();
            }
            else if (SyncBuildings())
            {
                StartCoroutine(Rebuild());
            }
        }

        // Walks the ECS world for the obstacle set (buildings + ObstacleTag
        // map features) and returns one BuildingRecord per entity. Shared by
        // both the runtime-bake path (SyncBuildings) and the pre-baked carving
        // path (SyncCarvers). Returns null if the ECS world isn't ready yet.
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
                // Wall gates are walk-through-able. Skip them from the
                // navmesh stamp so units can path through ally gates.
                // (The friend/foe gate logic in WallGatePassabilitySystem
                // is gameplay, not pathing — combat handles enemies on
                // contact. A per-faction navmesh layer would let us close
                // the gate for enemies cleanly; that's a future tile-set
                // extension.)
                if (em.HasComponent<WallGateTag>(e)) continue;
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

                current[e] = new BuildingRecord
                {
                    Position = new Vector3(t.Position.x, t.Position.y, t.Position.z),
                    Size = sz,
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
                        || prev.Size != kvp.Value.Size)
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
                var rec = kvp.Value;
                _sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(
                        rec.Position + new Vector3(0f, BuildingSourceHeight * 0.5f, 0f),
                        Quaternion.identity,
                        Vector3.one),
                    size = new Vector3(rec.Size.x, BuildingSourceHeight, rec.Size.y),
                    area = 0,
                });
            }
            return true;
        }

        private IEnumerator Rebuild()
        {
            _isBaking = true;
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(_data, _settings, _sources, _bounds);
            while (!op.isDone) yield return null;
            _isBaking = false;
        }

        // Pre-baked-map carving: reconcile the live building/obstacle set
        // against a pool of NavMeshObstacle GameObjects. Each carver cuts a
        // box-shaped hole in the scene's pre-baked navmesh so units path
        // around player-built structures and map obstacles — without ever
        // re-baking the (author-tuned) surface.
        private void SyncCarvers()
        {
            var current = BuildCurrentRecords();
            if (current == null) return;

            // Remove carvers whose entity no longer exists.
            _removeScratch.Clear();
            foreach (var kvp in _carvers)
                if (!current.ContainsKey(kvp.Key)) _removeScratch.Add(kvp.Key);
            for (int i = 0; i < _removeScratch.Count; i++)
            {
                var key = _removeScratch[i];
                if (_carvers.TryGetValue(key, out var go) && go != null) Destroy(go);
                _carvers.Remove(key);
            }

            // Add new carvers; update any that moved or resized.
            foreach (var kvp in current)
            {
                if (_carvers.TryGetValue(kvp.Key, out var go) && go != null)
                {
                    if (!_knownBuildings.TryGetValue(kvp.Key, out var prev)
                        || (prev.Position - kvp.Value.Position).sqrMagnitude > 0.01f
                        || prev.Size != kvp.Value.Size)
                    {
                        go.transform.position = kvp.Value.Position;
                        var obs = go.GetComponent<NavMeshObstacle>();
                        if (obs != null)
                            obs.size = new Vector3(kvp.Value.Size.x, BuildingSourceHeight, kvp.Value.Size.y);
                    }
                }
                else
                {
                    _carvers[kvp.Key] = CreateCarver(kvp.Value);
                }
            }

            _knownBuildings = current;
        }

        private GameObject CreateCarver(BuildingRecord rec)
        {
            var go = new GameObject("NavCarver");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = rec.Position;

            var obs = go.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            // Buildings are stationary once placed; carveOnlyStationary lets
            // Unity defer the (cheap) carve until the obstacle settles and
            // skips per-frame work afterwards.
            obs.carveOnlyStationary = true;
            obs.carving = true;
            obs.center = Vector3.zero;
            // Box spans BuildingSourceHeight tall, centred on the footprint, so
            // it overlaps the navmesh surface regardless of small height noise.
            obs.size = new Vector3(rec.Size.x, BuildingSourceHeight, rec.Size.y);
            return go;
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
