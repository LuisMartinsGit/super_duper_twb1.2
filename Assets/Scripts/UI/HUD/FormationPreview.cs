// FormationPreview.cs
// Shows arrow markers at destination formation slots when selected units have
// an active move command.  Arrows point in the direction units will face.
// Positions are computed ONCE when the command is first detected and cached —
// they do not shift as units move.
// Uses object pooling to avoid per-frame allocation.
// Location: Assets/Scripts/UI/HUD/FormationPreview.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    [DefaultExecutionOrder(910)]
    public class FormationPreview : MonoBehaviour
    {
        [Header("Arrow Appearance")]
        [SerializeField] private float arrowLength = 0.6f;
        [SerializeField] private float arrowWidth  = 0.25f;
        [SerializeField] private float yOffset      = 0.12f;
        [SerializeField] private Color arrowColor   = new Color(0.3f, 1f, 0.4f, 0.55f);

        private EntityWorld _world;
        private EntityManager _em;
        private Material _arrowMat;

        // Pool of arrow GameObjects
        private readonly List<GameObject> _active = new();
        private readonly List<GameObject> _pool   = new();

        // Cached preview data per entity — computed once, displayed until destination changes
        private struct CachedSlot
        {
            public float3 WorldPos;
            public quaternion Facing;
        }

        private struct CachedPreview
        {
            public float3 Destination;          // the destination used to compute slots
            public List<CachedSlot> Slots;
        }

        private readonly Dictionary<Entity, CachedPreview> _cache = new();
        private readonly HashSet<Entity> _seenThisFrame = new();

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;

            _arrowMat = CreateMaterial();
        }

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }

            // Recycle all active arrows back to pool
            foreach (var go in _active)
            {
                if (go != null) { go.SetActive(false); _pool.Add(go); }
            }
            _active.Clear();
            _seenThisFrame.Clear();

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0)
            {
                _cache.Clear();
                return;
            }

            // Process each selected entity
            foreach (var entity in selection)
            {
                if (!_em.Exists(entity)) continue;

                // Skip buildings, battalion members
                if (_em.HasComponent<BuildingTag>(entity)) continue;
                if (_em.HasComponent<BattalionMemberData>(entity)) continue;

                // Must have an active destination
                if (!_em.HasComponent<DesiredDestination>(entity)) continue;
                var dd = _em.GetComponentData<DesiredDestination>(entity);
                if (dd.Has == 0) continue;

                float3 dest = dd.Position;
                _seenThisFrame.Add(entity);

                // Check cache: if we already computed slots for this exact destination, reuse them
                if (_cache.TryGetValue(entity, out var cached))
                {
                    if (math.lengthsq(cached.Destination - dest) < 0.01f)
                    {
                        // Same destination — display cached slots
                        foreach (var slot in cached.Slots)
                            PlaceArrow(slot.WorldPos, slot.Facing);
                        continue;
                    }
                }

                // New or changed destination — compute and cache
                var slots = new List<CachedSlot>();

                if (_em.HasComponent<BattalionLeader>(entity))
                {
                    // Battalion: use DestinationRot (set at command time, same facing applied on arrival)
                    var bl = _em.GetComponentData<BattalionLeader>(entity);
                    quaternion facing = bl.HasDestinationRot != 0
                        ? bl.DestinationRot
                        : bl.FormationRot;

                    // Handle uninitialized rotation
                    if (math.lengthsq(facing.value) < 0.001f)
                    {
                        float3 currentPos = _em.HasComponent<LocalTransform>(entity)
                            ? _em.GetComponentData<LocalTransform>(entity).Position
                            : dest;
                        float3 moveDir = dest - currentPos;
                        moveDir.y = 0;
                        if (math.lengthsq(moveDir) < 0.01f)
                            moveDir = new float3(0, 0, 1);
                        moveDir = math.normalize(moveDir);
                        facing = quaternion.LookRotationSafe(moveDir, new float3(0, 1, 0));
                    }

                    int cols = bl.Columns;
                    int rows = bl.Rows;
                    float spacing = bl.Spacing;

                    // Get living member count from buffer
                    int aliveCount = 0;
                    if (_em.HasBuffer<BattalionMember>(entity))
                    {
                        var buf = _em.GetBuffer<BattalionMember>(entity);
                        for (int i = 0; i < buf.Length; i++)
                        {
                            if (_em.Exists(buf[i].Value) && _em.HasComponent<Health>(buf[i].Value))
                            {
                                var hp = _em.GetComponentData<Health>(buf[i].Value);
                                if (hp.Value > 0) aliveCount++;
                            }
                        }
                    }

                    // Compute arrow slots at DESTINATION using formation rotation
                    int drawn = 0;
                    for (int row = 0; row < rows && drawn < aliveCount; row++)
                    {
                        for (int col = 0; col < cols && drawn < aliveCount; col++)
                        {
                            float3 localOffset = BattalionFormation.ComputeSlotOffset(col, row, cols, rows, spacing);
                            float3 slotWorld = dest + math.mul(facing, localOffset);
                            slots.Add(new CachedSlot { WorldPos = slotWorld, Facing = facing });
                            drawn++;
                        }
                    }
                }
                else
                {
                    // Single unit: compute facing from current pos toward destination
                    float3 currentPos = _em.HasComponent<LocalTransform>(entity)
                        ? _em.GetComponentData<LocalTransform>(entity).Position
                        : dest;
                    float3 moveDir = dest - currentPos;
                    moveDir.y = 0;
                    if (math.lengthsq(moveDir) < 0.01f)
                        moveDir = new float3(0, 0, 1);
                    moveDir = math.normalize(moveDir);
                    quaternion facing = quaternion.LookRotationSafe(moveDir, new float3(0, 1, 0));

                    slots.Add(new CachedSlot { WorldPos = dest, Facing = facing });
                }

                _cache[entity] = new CachedPreview { Destination = dest, Slots = slots };

                // Display the freshly computed slots
                foreach (var slot in slots)
                    PlaceArrow(slot.WorldPos, slot.Facing);
            }

            // Evict cache entries for entities no longer selected or no longer valid
            var toRemove = new List<Entity>();
            foreach (var kvp in _cache)
            {
                if (!_seenThisFrame.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var e in toRemove)
                _cache.Remove(e);
        }

        private void PlaceArrow(float3 worldPos, quaternion facing)
        {
            float terrainY = TerrainUtility.GetHeight(worldPos.x, worldPos.z);
            Vector3 pos = new Vector3(worldPos.x, terrainY + yOffset, worldPos.z);

            // Extract yaw from facing quaternion
            float3 fwd = math.mul(facing, new float3(0, 0, 1));
            float angle = math.degrees(math.atan2(fwd.x, fwd.z));

            var go = GetOrCreate();
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, angle, 0f);
            go.SetActive(true);
            _active.Add(go);
        }

        private GameObject GetOrCreate()
        {
            if (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                var go = _pool[last];
                _pool.RemoveAt(last);
                return go;
            }

            var arrow = CreateArrowObject();
            return arrow;
        }

        /// <summary>
        /// Creates a chevron / arrow-shaped mesh (▲) to clearly indicate direction.
        /// </summary>
        private GameObject CreateArrowObject()
        {
            var go = new GameObject("FormationArrow");
            go.transform.SetParent(transform);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateArrowMesh();

            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(_arrowMat);
            SetColor(mat, arrowColor);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }

        /// <summary>
        /// Builds a simple arrow/chevron mesh:
        ///     *        (tip, forward)
        ///    / \
        ///   /   \
        ///  *     *     (base corners)
        ///   \   /
        ///    \ /
        ///     *        (tail notch, gives it a chevron look)
        /// </summary>
        private Mesh CreateArrowMesh()
        {
            float hw = arrowWidth * 0.5f;
            float hl = arrowLength * 0.5f;
            float notch = arrowLength * 0.2f; // depth of tail notch

            var mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3( 0,     0,  hl),       // 0: tip (forward)
                new Vector3(-hw,    0, -hl),       // 1: base-left
                new Vector3( 0,     0, -hl + notch), // 2: tail notch (center)
                new Vector3( hw,    0, -hl),       // 3: base-right
            };
            mesh.triangles = new int[]
            {
                0, 3, 2,   // right half
                0, 2, 1,   // left half
            };
            mesh.normals = new Vector3[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0);
            mat.renderQueue = 3100;
            return mat;
        }

        private static void SetColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
        }

        void OnDestroy()
        {
            foreach (var go in _active) { if (go != null) Destroy(go); }
            foreach (var go in _pool)   { if (go != null) Destroy(go); }
            _active.Clear();
            _pool.Clear();
            _cache.Clear();
            if (_arrowMat != null) Destroy(_arrowMat);
        }
    }
}
