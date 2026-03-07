// FlowFieldManager.cs
// Managed MonoBehaviour singleton that owns the flow field cache, throttles
// per-frame generation, and provides the public request API for flow fields.
// Location: Assets/Scripts/Systems/Movement/FlowFieldManager.cs

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Singleton MonoBehaviour that manages flow field generation and caching.
    /// - LRU cache of up to 8 flow fields keyed by destination cell index.
    /// - Throttles generation to max 2 fields per frame to avoid spikes.
    /// - Provides RequestFlowField() for systems to obtain direction data.
    ///
    /// Execution order: -40 (after PassabilityGrid at -50, before ECS systems).
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class FlowFieldManager : MonoBehaviour
    {
        // =====================================================================
        // SINGLETON
        // =====================================================================

        public static FlowFieldManager Instance { get; private set; }

        // =====================================================================
        // CONFIGURATION
        // =====================================================================

        /// <summary>Maximum number of cached flow fields before LRU eviction.</summary>
        private const int MaxCacheSize = 8;

        /// <summary>Maximum flow fields generated per frame to avoid frame spikes.</summary>
        private const int MaxFieldsPerFrame = 2;

        /// <summary>Maximum cells to check in spiral search when snapping to passable cell.</summary>
        private const int MaxSnapSearchCells = 25;

        // =====================================================================
        // CACHE
        // =====================================================================

        /// <summary>Maps destination cell index to its computed FlowField.</summary>
        private Dictionary<int, FlowField> _cache;

        /// <summary>
        /// LRU tracking: front of list = most recently used.
        /// Each node value is a destination cell index matching a _cache key.
        /// </summary>
        private LinkedList<int> _lruOrder;

        /// <summary>
        /// Quick lookup from destination cell index to its LinkedListNode
        /// for O(1) MoveToFront operations.
        /// </summary>
        private Dictionary<int, LinkedListNode<int>> _lruNodeMap;

        // =====================================================================
        // THROTTLE & REQUEST QUEUE
        // =====================================================================

        private int _fieldsGeneratedThisFrame;
        private Queue<FlowFieldRequest> _pendingRequests;

        // =====================================================================
        // LIFECYCLE
        // =====================================================================

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[FlowFieldManager] Duplicate instance destroyed.");
                Destroy(this);
                return;
            }

            Instance = this;

            _cache = new Dictionary<int, FlowField>(MaxCacheSize);
            _lruOrder = new LinkedList<int>();
            _lruNodeMap = new Dictionary<int, LinkedListNode<int>>(MaxCacheSize);
            _pendingRequests = new Queue<FlowFieldRequest>();
        }

        void OnDestroy()
        {
            // Dispose all cached flow fields to free NativeArrays
            if (_cache != null)
            {
                foreach (var kvp in _cache)
                {
                    var field = kvp.Value;
                    field.Dispose();
                }
                _cache.Clear();
            }

            _lruOrder?.Clear();
            _lruNodeMap?.Clear();
            _pendingRequests?.Clear();

            if (Instance == this)
                Instance = null;
        }

        // =====================================================================
        // UPDATE — Process pending requests with throttle
        // =====================================================================

        void Update()
        {
            _fieldsGeneratedThisFrame = 0;

            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            while (_pendingRequests.Count > 0 && _fieldsGeneratedThisFrame < MaxFieldsPerFrame)
            {
                var request = _pendingRequests.Dequeue();

                // Skip if already cached (another request may have triggered generation)
                if (_cache.ContainsKey(request.DestinationCellIndex))
                    continue;

                // Generate the flow field
                #if UNITY_EDITOR
                var sw = System.Diagnostics.Stopwatch.StartNew();
                #endif

                var flowField = FlowFieldGenerator.Generate(
                    grid.Cells,
                    grid.Width,
                    grid.Height,
                    request.DestinationCellIndex
                );

                #if UNITY_EDITOR
                sw.Stop();
                Debug.Log($"[FlowFieldManager] Generated flow field for cell {request.DestinationCellIndex} " +
                          $"in {sw.Elapsed.TotalMilliseconds:F2}ms ({grid.Width}x{grid.Height} grid)");
                #endif

                AddToCache(request.DestinationCellIndex, flowField);
                _fieldsGeneratedThisFrame++;
            }
        }

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        /// <summary>
        /// Request a flow field for the given world destination.
        /// Returns the field immediately if cached; otherwise queues for generation
        /// and returns null (caller should retry next frame).
        /// </summary>
        /// <param name="worldDestination">World position of the desired destination.</param>
        /// <returns>The cached FlowField, or null if not yet available.</returns>
        public FlowField? RequestFlowField(float3 worldDestination)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return null;

            int cellIndex = WorldToCellIndex(grid, worldDestination);
            if (cellIndex < 0) return null;

            // If destination cell is impassable, snap to nearest passable cell
            if (grid.Cells[cellIndex] != PassabilityGrid.Passable)
            {
                cellIndex = SnapToPassable(grid, cellIndex);
                if (cellIndex < 0) return null;
            }

            // Check cache
            if (_cache.TryGetValue(cellIndex, out var cached))
            {
                PromoteLRU(cellIndex);
                return cached;
            }

            // Queue for generation (avoid duplicates)
            bool alreadyQueued = false;
            foreach (var req in _pendingRequests)
            {
                if (req.DestinationCellIndex == cellIndex)
                {
                    alreadyQueued = true;
                    break;
                }
            }

            if (!alreadyQueued)
            {
                _pendingRequests.Enqueue(new FlowFieldRequest
                {
                    DestinationCellIndex = cellIndex,
                    WorldDestination = worldDestination,
                });
            }

            return null;
        }

        /// <summary>
        /// Check if a flow field is cached for the given world destination.
        /// </summary>
        public bool HasCachedField(float3 worldDestination)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return false;

            int cellIndex = WorldToCellIndex(grid, worldDestination);
            if (cellIndex < 0) return false;

            if (grid.Cells[cellIndex] != PassabilityGrid.Passable)
            {
                cellIndex = SnapToPassable(grid, cellIndex);
                if (cellIndex < 0) return false;
            }

            return _cache.ContainsKey(cellIndex);
        }

        /// <summary>
        /// Invalidate all cached flow fields (e.g., when passability grid changes
        /// due to building placement/destruction).
        /// </summary>
        public void InvalidateAll()
        {
            if (_cache == null) return;

            foreach (var kvp in _cache)
            {
                var field = kvp.Value;
                field.Dispose();
            }

            _cache.Clear();
            _lruOrder.Clear();
            _lruNodeMap.Clear();
        }

        /// <summary>
        /// Invalidate a specific cached flow field by destination cell index.
        /// </summary>
        public void Invalidate(int destinationCellIndex)
        {
            if (_cache == null) return;

            if (_cache.TryGetValue(destinationCellIndex, out var field))
            {
                field.Dispose();
                _cache.Remove(destinationCellIndex);

                if (_lruNodeMap.TryGetValue(destinationCellIndex, out var node))
                {
                    _lruOrder.Remove(node);
                    _lruNodeMap.Remove(destinationCellIndex);
                }
            }
        }

        // =====================================================================
        // CACHE MANAGEMENT (LRU)
        // =====================================================================

        /// <summary>
        /// Add a flow field to the cache. If cache is full, evict the least recently used entry.
        /// </summary>
        private void AddToCache(int cellIndex, FlowField field)
        {
            // Evict LRU if at capacity
            while (_cache.Count >= MaxCacheSize && _lruOrder.Count > 0)
            {
                int evictKey = _lruOrder.Last.Value;
                _lruOrder.RemoveLast();
                _lruNodeMap.Remove(evictKey);

                if (_cache.TryGetValue(evictKey, out var evicted))
                {
                    evicted.Dispose();
                    _cache.Remove(evictKey);
                }
            }

            _cache[cellIndex] = field;
            var newNode = _lruOrder.AddFirst(cellIndex);
            _lruNodeMap[cellIndex] = newNode;
        }

        /// <summary>
        /// Move an existing cache entry to the front of the LRU list (most recently used).
        /// </summary>
        private void PromoteLRU(int cellIndex)
        {
            if (_lruNodeMap.TryGetValue(cellIndex, out var node))
            {
                _lruOrder.Remove(node);
                var newNode = _lruOrder.AddFirst(cellIndex);
                _lruNodeMap[cellIndex] = newNode;
            }
        }

        // =====================================================================
        // COORDINATE HELPERS
        // =====================================================================

        /// <summary>
        /// Convert a world position to a flat cell index via PassabilityGrid.
        /// Returns -1 if out of bounds.
        /// </summary>
        private static int WorldToCellIndex(PassabilityGrid grid, float3 worldPos)
        {
            int2 cell = grid.WorldToCell(worldPos);

            if (cell.x < 0 || cell.x >= grid.Width || cell.y < 0 || cell.y >= grid.Height)
                return -1;

            return cell.y * grid.Width + cell.x;
        }

        /// <summary>
        /// Spiral search outward from an impassable cell to find the nearest passable cell.
        /// Checks up to MaxSnapSearchCells cells. Returns -1 if none found.
        /// </summary>
        private static int SnapToPassable(PassabilityGrid grid, int cellIndex)
        {
            int cx = cellIndex % grid.Width;
            int cy = cellIndex / grid.Width;
            int cellsChecked = 0;

            // Spiral outward in expanding rings
            for (int ring = 1; cellsChecked < MaxSnapSearchCells; ring++)
            {
                for (int dx = -ring; dx <= ring && cellsChecked < MaxSnapSearchCells; dx++)
                {
                    for (int dy = -ring; dy <= ring && cellsChecked < MaxSnapSearchCells; dy++)
                    {
                        // Only check cells on the perimeter of the current ring
                        if (math.abs(dx) != ring && math.abs(dy) != ring)
                            continue;

                        int nx = cx + dx;
                        int ny = cy + dy;

                        if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height)
                        {
                            cellsChecked++;
                            continue;
                        }

                        int neighborIndex = ny * grid.Width + nx;
                        if (grid.Cells[neighborIndex] == PassabilityGrid.Passable)
                            return neighborIndex;

                        cellsChecked++;
                    }
                }
            }

            return -1;
        }
    }
}
