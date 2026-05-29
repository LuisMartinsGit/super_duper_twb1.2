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
            yield return null;

            // The scene's pre-baked NavMeshSurface baked the flat water plane
            // (Y is constant, ~6–15 m below the terrain surface) instead of the
            // hilly terrain — its 10° agent slope rejected the terrain, leaving
            // only the flat water as "walkable". A flat navmesh has no holes, so
            // CalculatePath returns straight lines and units never route around
            // anything. So we ignore that surface and BUILD the navmesh at
            // runtime from the Unity Terrain heightmap, which is guaranteed to
            // drape the terrain. Re-bakes on building/obstacle changes via the
            // SyncBuildings path (so _external stays false).
            _external = false;
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

            // Probe whether the DEFAULT agent (id 0) can query this navmesh.
            // IMPORTANT: probe at an actual navmesh VERTEX, not the map centre.
            // The centre can sit in a non-walkable gap (e.g. a steep area the
            // agent's Max Slope excludes), giving a false "can't sample" even
            // when the agent type is correct. Sampling at a known vertex only
            // fails if the agent genuinely can't read this navmesh.
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices != null && tri.vertices.Length > 0)
            {
                var min = tri.vertices[0];
                var max = tri.vertices[0];
                for (int i = 1; i < tri.vertices.Length; i++)
                {
                    min = Vector3.Min(min, tri.vertices[i]);
                    max = Vector3.Max(max, tri.vertices[i]);
                }
                var span = max - min;

                var vertex = tri.vertices[tri.vertices.Length / 2];
                bool hit = NavMesh.SamplePosition(vertex, out _, 5f, NavMesh.AllAreas);
                if (hit)
                    Debug.Log($"[NavMeshManager] Default agent CAN sample the navmesh (probed a real " +
                              $"vertex) — agent type is correct. Coverage span ≈ {span.x:F0}×{span.z:F0} m " +
                              $"over {triCount} tris. If units still ignore it the navmesh is too sparse: " +
                              "raise the agent's Max Slope (10° is very low — try 30–45°) and/or lower " +
                              "Radius, then re-bake the NavMeshSurface.");
                else
                    Debug.LogError($"[NavMeshManager] Default agent CANNOT sample even a known navmesh " +
                                   $"vertex ({triCount} tris) — genuine agent-type mismatch. Set the " +
                                   "NavMeshSurface Agent Type to the project's id-0 (Humanoid) agent and re-bake.");
            }

            // Hard numbers: wait for units to spawn, then measure how much of the
            // terrain the navmesh actually covers and how many live units are
            // standing ON it. Distinguishes "navmesh doesn't cover where units
            // are" (a bake problem) from "units ignore a good navmesh" (a mover
            // problem) — no more guessing from screenshots.
            yield return new WaitForSeconds(3f);
            ReportCoverage();
        }

        private void ReportCoverage()
        {
            var terrain = TheWaningBorder.World.Terrain.TerrainUtility.GetActiveTerrain();
            if (terrain == null || terrain.terrainData == null) return;
            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;

            const int N = 60;
            int near = 0, far = 0, total = 0;
            double sumNavY = 0, sumTerrY = 0; int ySamples = 0;
            for (int gz = 0; gz < N; gz++)
            for (int gx = 0; gx < N; gx++)
            {
                float wx = origin.x + (gx + 0.5f) / N * size.x;
                float wz = origin.z + (gz + 0.5f) / N * size.z;
                float wy = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(wx, wz);
                total++;
                // Tight check: is there navmesh AT the terrain surface here?
                if (NavMesh.SamplePosition(new Vector3(wx, wy, wz), out _, 3f, NavMesh.AllAreas)) near++;
                // Loose check: is there navmesh in this XZ column at ANY height?
                if (NavMesh.SamplePosition(new Vector3(wx, wy, wz), out var farHit, 200f, NavMesh.AllAreas))
                {
                    far++;
                    sumNavY += farHit.position.y;
                    sumTerrY += wy;
                    ySamples++;
                }
            }
            Debug.Log($"[NavMeshManager] COVERAGE: {near}/{total} points have navmesh at the terrain surface (±3m); " +
                      $"{far}/{total} have navmesh in the XZ column at ANY height. " +
                      (ySamples > 0
                        ? $"Avg navmesh Y = {sumNavY / ySamples:F1} vs terrain Y = {sumTerrY / ySamples:F1} " +
                          $"(navmesh is {((sumNavY - sumTerrY) / ySamples):F1} m off the terrain surface on average)."
                        : "No navmesh found in any column."));

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            int onMesh = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                if (NavMesh.SamplePosition(new Vector3(p.x, p.y, p.z), out _, 3f, NavMesh.AllAreas)) onMesh++;
            }
            if (ents.Length > 0 && onMesh == 0)
                Debug.LogError($"[NavMeshManager] UNITS: 0/{ents.Length} are on the navmesh — every unit is " +
                               "OFF it. The navmesh does not cover where units stand, so nothing can confine " +
                               "or path them. This is a BAKE-COVERAGE problem, not a mover problem.");
            else
                Debug.Log($"[NavMeshManager] UNITS: {onMesh}/{ents.Length} are on the navmesh.");
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
            Debug.Log($"[NavMeshManager] Runtime-baked navmesh from terrain '{terrain.name}' " +
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
            Debug.Log($"[NavMeshManager] Battalion navmesh baked (agent type {_battalionAgentTypeId}, radius {BattalionAgentRadius} m).");
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
            // Keep the battalion navmesh in sync with the same source set.
            if (_battalionBaked)
            {
                var bop = NavMeshBuilder.UpdateNavMeshDataAsync(_battalionData, _battalionSettings, _sources, _bounds);
                while (!bop.isDone) yield return null;
            }
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
