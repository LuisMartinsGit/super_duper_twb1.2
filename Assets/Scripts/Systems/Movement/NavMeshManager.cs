// NavMeshManager.cs
// Tier-4 / "Option B" navmesh migration — PR1 foundation.
//
// Owns the runtime-built navmesh: bakes from the procedural Terrain on
// startup, then incrementally re-bakes whenever the ECS building set
// changes. Exposes RequestPath for callers — wraps NavMesh.CalculatePath.
//
// PR1 scope: foundation only. The existing nav stack (PassabilityGrid /
// FlowField* / AStar* / MovementSystem flow consumption) is unchanged.
// Future PRs flip GameSettings.UseNavMesh and replace the consumers.
//
// Architecture:
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
        }

        private void OnDestroy()
        {
            if (_instance.valid) _instance.Remove();
            if (_data != null) Object.DestroyImmediate(_data);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            StartCoroutine(InitialBake());
        }

        private IEnumerator InitialBake()
        {
            // Wait for the procedural Terrain to exist — it's spawned by
            // ProceduralTerrain.Awake but generation is asynchronous.
            UnityEngine.Terrain terrain = null;
            float waited = 0f;
            while (terrain == null)
            {
                terrain = Object.FindFirstObjectByType<UnityEngine.Terrain>();
                if (terrain != null && terrain.terrainData != null) break;
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

            if (SyncBuildings())
                StartCoroutine(Rebuild());
        }

        // Walks the ECS world for BuildingTag entities and reconciles them
        // against _knownBuildings. Returns true if the source set changed
        // (any building added, removed, or moved meaningfully).
        private bool SyncBuildings()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            var em = world.EntityManager;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            // Build the new known set.
            var current = new Dictionary<Entity, BuildingRecord>(entities.Length);
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

                current[e] = new BuildingRecord
                {
                    Position = new Vector3(t.Position.x, t.Position.y, t.Position.z),
                    Size = sz,
                };
            }

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
            if (!NavMesh.SamplePosition(from, out var fromHit, 5f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var toHit, 5f, NavMesh.AllAreas)) return false;
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
