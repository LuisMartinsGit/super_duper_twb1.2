// FlowFieldManager.cs
// Managed MonoBehaviour singleton that owns the flow field cache, throttles
// per-frame generation, and provides the public request API for flow fields.
// Maintains a FlowFieldLookup struct for Burst-compatible direction lookup.
// Location: Assets/Scripts/Systems/Movement/FlowFieldManager.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Singleton MonoBehaviour that manages flow field generation and caching.
    /// - LRU cache of up to 8 flow fields keyed by snapped destination cell index.
    /// - Destination snapping to 4-cell coarser grid for cache deduplication.
    /// - Async multi-frame generation: BFS scheduled on worker thread, completed next frame.
    /// - Grid version tracking for staleness detection on passability changes.
    /// - NativeArray pooling via FlowFieldArrayPool for zero-allocation steady state.
    /// - FlowFieldLookup struct for Burst-compatible direction lookup by MovementSystem.
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

        /// <summary>Maximum flow fields scheduled per frame to avoid frame spikes.</summary>
        private const int MaxFieldsPerFrame = 2;

        /// <summary>Maximum cells to check in spiral search when snapping to passable cell.</summary>
        private const int MaxSnapSearchCells = 25;

        /// <summary>Snap destination cells to a coarser grid for cache deduplication (~8 world units).</summary>
        private int _snapCells = 2;

        // =====================================================================
        // CACHE
        // =====================================================================

        /// <summary>Maps snapped destination cell index to its computed FlowField.</summary>
        private Dictionary<int, FlowField> _cache;

        /// <summary>
        /// LRU tracking: front of list = most recently used.
        /// Each node value is a snapped destination cell index matching a _cache key.
        /// </summary>
        private LinkedList<int> _lruOrder;

        /// <summary>
        /// Quick lookup from snapped destination cell index to its LinkedListNode
        /// for O(1) MoveToFront operations.
        /// </summary>
        private Dictionary<int, LinkedListNode<int>> _lruNodeMap;

        // =====================================================================
        // GRID VERSION
        // =====================================================================

        /// <summary>
        /// Incremented each time passability grid changes (building placed/destroyed).
        /// Cached fields with GridVersion less than this are treated as stale.
        /// </summary>
        private int _gridVersion;

        // =====================================================================
        // THROTTLE & REQUEST QUEUE
        // =====================================================================

        private int _fieldsScheduledThisFrame;
        private Queue<FlowFieldRequest> _pendingRequests;

        // =====================================================================
        // ASYNC IN-FLIGHT JOBS
        // =====================================================================

        /// <summary>In-flight async flow field generation handles.</summary>
        private List<FlowFieldGenerator.AsyncFlowFieldHandle> _inFlightJobs;

        /// <summary>
        /// Track which snapped destination indices have in-flight jobs to avoid duplicates.
        /// </summary>
        private HashSet<int> _inFlightDestinations;

        // =====================================================================
        // FLOW FIELD LOOKUP (Burst-compatible shared data)
        // =====================================================================

        /// <summary>
        /// Flat NativeArray holding direction data for all cache slots concatenated.
        /// Slot i occupies [i * _totalCells .. (i+1) * _totalCells - 1].
        /// Allocated once (MaxCacheSize * totalCells) and persists for the session.
        /// </summary>
        private NativeArray<float2> _lookupDirectionData;

        /// <summary>Maps snapped destination cell index to slot index in _lookupDirectionData.</summary>
        private NativeHashMap<int, int> _lookupDestToSlot;

        /// <summary>
        /// The current FlowFieldLookup struct, rebuilt each frame.
        /// MovementSystem reads this at the start of OnUpdate.
        /// </summary>
        public FlowFieldLookup CurrentLookup { get; private set; }

        /// <summary>
        /// Track which slot indices are free for reuse when cache entries are evicted.
        /// </summary>
        private Stack<int> _freeSlots;

        /// <summary>
        /// Maps snapped destination cell index to its slot index in the direction data array.
        /// Managed-side mirror of _lookupDestToSlot for easy slot management.
        /// </summary>
        private Dictionary<int, int> _destToSlotManaged;

        // =====================================================================
        // POOL INITIALIZATION
        // =====================================================================

        private bool _poolInitialized;
        private int _totalCells;

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
            _inFlightJobs = new List<FlowFieldGenerator.AsyncFlowFieldHandle>(MaxFieldsPerFrame);
            _inFlightDestinations = new HashSet<int>();
            _freeSlots = new Stack<int>();
            _destToSlotManaged = new Dictionary<int, int>(MaxCacheSize);
            _gridVersion = 0;
        }

        void OnDestroy()
        {
            // Complete and cancel any in-flight jobs
            if (_inFlightJobs != null)
            {
                for (int i = 0; i < _inFlightJobs.Count; i++)
                    FlowFieldGenerator.CancelAsync(_inFlightJobs[i]);
                _inFlightJobs.Clear();
            }
            _inFlightDestinations?.Clear();

            // Dispose all cached flow fields (returns arrays to pool)
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
            _destToSlotManaged?.Clear();

            // Dispose lookup NativeArrays
            if (_lookupDirectionData.IsCreated)
                _lookupDirectionData.Dispose();
            if (_lookupDestToSlot.IsCreated)
                _lookupDestToSlot.Dispose();

            // Dispose all pooled arrays
            FlowFieldArrayPool.DisposeAll();

            if (Instance == this)
                Instance = null;
        }

        // =====================================================================
        // UPDATE — Complete async jobs, process pending requests, rebuild lookup
        // =====================================================================

        void Update()
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            // Lazy initialization of pool and lookup arrays
            if (!_poolInitialized)
            {
                InitializePool(grid);
            }

            _fieldsScheduledThisFrame = 0;
            bool lookupDirty = false;

            // -----------------------------------------------------------------
            // PHASE 1: Complete in-flight async jobs
            // -----------------------------------------------------------------
            for (int i = _inFlightJobs.Count - 1; i >= 0; i--)
            {
                var handle = _inFlightJobs[i];

                // Check if stale (grid version changed since scheduling)
                if (handle.GridVersion < _gridVersion)
                {
                    FlowFieldGenerator.CancelAsync(handle);
                    _inFlightDestinations.Remove(handle.DestinationIndex);
                    _inFlightJobs.RemoveAt(i);
                    continue;
                }

                if (!handle.IntegrationHandle.IsCompleted)
                    continue;

                #if UNITY_EDITOR
                var sw = System.Diagnostics.Stopwatch.StartNew();
                #endif

                var flowField = FlowFieldGenerator.CompleteAsync(handle);

                #if UNITY_EDITOR
                sw.Stop();
                Debug.Log($"[FlowFieldManager] Completed async flow field for cell {handle.DestinationIndex} " +
                          $"(direction pass: {sw.Elapsed.TotalMilliseconds:F2}ms, {handle.Width}x{handle.Height} grid)");
                #endif

                int snappedDest = handle.DestinationIndex;
                _inFlightDestinations.Remove(snappedDest);
                _inFlightJobs.RemoveAt(i);

                // Skip if cache already has a valid entry for this destination
                if (_cache.TryGetValue(snappedDest, out var existing) && existing.GridVersion >= _gridVersion)
                {
                    flowField.Dispose();
                    continue;
                }

                AddToCache(snappedDest, flowField);
                lookupDirty = true;
            }

            // -----------------------------------------------------------------
            // PHASE 2: Schedule new async jobs from pending requests
            // -----------------------------------------------------------------
            while (_pendingRequests.Count > 0 && _fieldsScheduledThisFrame < MaxFieldsPerFrame)
            {
                var request = _pendingRequests.Dequeue();
                int snappedDest = request.DestinationCellIndex;

                // Skip if already cached and not stale
                if (_cache.TryGetValue(snappedDest, out var cached) && cached.GridVersion >= _gridVersion)
                    continue;

                // Skip if already in-flight
                if (_inFlightDestinations.Contains(snappedDest))
                    continue;

                #if UNITY_EDITOR
                Debug.Log($"[FlowFieldManager] Scheduling async flow field for cell {snappedDest} " +
                          $"(gridVersion={_gridVersion})");
                #endif

                var handle = FlowFieldGenerator.ScheduleAsync(
                    grid.Cells,
                    grid.Width,
                    grid.Height,
                    snappedDest,
                    _gridVersion
                );

                _inFlightJobs.Add(handle);
                _inFlightDestinations.Add(snappedDest);
                _fieldsScheduledThisFrame++;
            }

            // -----------------------------------------------------------------
            // PHASE 3: Rebuild FlowFieldLookup if cache changed
            // -----------------------------------------------------------------
            if (lookupDirty)
            {
                RebuildLookup(grid);
            }
        }

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        /// <summary>
        /// Request a flow field for the given world destination.
        /// Returns the field immediately if cached and not stale; otherwise queues
        /// for async generation and returns null (caller should retry next frame).
        /// Destinations are snapped to a 4-cell coarser grid for cache deduplication.
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

            // Snap to coarser grid for cache deduplication
            int snappedIndex = SnapCellIndex(cellIndex, grid.Width, grid.Height, _snapCells);

            // Check cache (must also be non-stale)
            if (_cache.TryGetValue(snappedIndex, out var cached) && cached.GridVersion >= _gridVersion)
            {
                PromoteLRU(snappedIndex);
                return cached;
            }

            // Queue for async generation (avoid duplicates)
            if (!_inFlightDestinations.Contains(snappedIndex))
            {
                bool alreadyQueued = false;
                foreach (var req in _pendingRequests)
                {
                    if (req.DestinationCellIndex == snappedIndex)
                    {
                        alreadyQueued = true;
                        break;
                    }
                }

                if (!alreadyQueued)
                {
                    _pendingRequests.Enqueue(new FlowFieldRequest
                    {
                        DestinationCellIndex = snappedIndex,
                        WorldDestination = worldDestination,
                    });
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a flow field is cached (and non-stale) for the given world destination.
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

            int snappedIndex = SnapCellIndex(cellIndex, grid.Width, grid.Height, _snapCells);
            return _cache.TryGetValue(snappedIndex, out var cached) && cached.GridVersion >= _gridVersion;
        }

        /// <summary>
        /// Invalidate all cached flow fields by incrementing the grid version.
        /// Stale entries will be treated as cache misses and regenerated on demand.
        /// In-flight async jobs with older versions will be cancelled on completion.
        /// Called when the passability grid changes (building placement/destruction).
        /// </summary>
        public void InvalidateAll()
        {
            _gridVersion++;

            #if UNITY_EDITOR
            Debug.Log($"[FlowFieldManager] InvalidateAll — grid version now {_gridVersion}");
            #endif
        }

        /// <summary>
        /// Invalidate a specific cached flow field by destination cell index.
        /// </summary>
        public void Invalidate(int destinationCellIndex)
        {
            if (_cache == null) return;

            if (_cache.TryGetValue(destinationCellIndex, out var field))
            {
                // Free the slot in the lookup
                if (_destToSlotManaged.TryGetValue(destinationCellIndex, out int slot))
                {
                    _freeSlots.Push(slot);
                    _destToSlotManaged.Remove(destinationCellIndex);
                }

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
        // POOL & LOOKUP INITIALIZATION
        // =====================================================================

        /// <summary>
        /// Initialize the NativeArray pool and shared lookup arrays.
        /// Deferred to first Update() when PassabilityGrid is available.
        /// </summary>
        private void InitializePool(PassabilityGrid grid)
        {
            _totalCells = grid.Width * grid.Height;
            _snapCells = Mathf.Max(1, Mathf.RoundToInt(8f / grid.CellSize));
            FlowFieldArrayPool.Init(_totalCells);

            // Allocate the flat direction data array for all cache slots
            _lookupDirectionData = new NativeArray<float2>(
                MaxCacheSize * _totalCells, Allocator.Persistent);

            _lookupDestToSlot = new NativeHashMap<int, int>(MaxCacheSize, Allocator.Persistent);

            // Initialize all slots as free
            for (int i = MaxCacheSize - 1; i >= 0; i--)
                _freeSlots.Push(i);

            // Build initial (empty) lookup
            CurrentLookup = new FlowFieldLookup
            {
                DirectionData = _lookupDirectionData,
                DestToSlot = _lookupDestToSlot,
                CellsPerField = _totalCells,
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                CellSize = grid.CellSize,
                Origin = grid.Origin,
                IsValid = true,
            };

            _poolInitialized = true;

            Debug.Log($"[FlowFieldManager] Pool initialized: {_totalCells} cells/field, " +
                      $"{MaxCacheSize} cache slots, " +
                      $"{MaxCacheSize * _totalCells * 8 / 1024}KB lookup buffer");
        }

        // =====================================================================
        // CACHE MANAGEMENT (LRU) WITH LOOKUP SLOT TRACKING
        // =====================================================================

        /// <summary>
        /// Add a flow field to the cache. If cache is full, evict the least recently used entry.
        /// Assigns a lookup slot and copies direction data into the shared NativeArray.
        /// </summary>
        private void AddToCache(int cellIndex, FlowField field)
        {
            // If this destination already has a stale entry, evict it first
            if (_cache.ContainsKey(cellIndex))
            {
                EvictEntry(cellIndex);
            }

            // Evict LRU if at capacity
            while (_cache.Count >= MaxCacheSize && _lruOrder.Count > 0)
            {
                int evictKey = _lruOrder.Last.Value;
                EvictEntry(evictKey);
            }

            // Assign a lookup slot
            int slot = _freeSlots.Pop();
            _destToSlotManaged[cellIndex] = slot;

            // Copy direction data into the shared NativeArray at the slot offset
            int offset = slot * _totalCells;
            NativeArray<float2>.Copy(field.DirectionField, 0, _lookupDirectionData, offset, _totalCells);

            _cache[cellIndex] = field;
            var newNode = _lruOrder.AddFirst(cellIndex);
            _lruNodeMap[cellIndex] = newNode;
        }

        /// <summary>
        /// Evict a single cache entry, returning its slot and disposing the flow field.
        /// </summary>
        private void EvictEntry(int cellIndex)
        {
            if (!_cache.TryGetValue(cellIndex, out var evicted))
                return;

            // Return the lookup slot
            if (_destToSlotManaged.TryGetValue(cellIndex, out int slot))
            {
                _freeSlots.Push(slot);
                _destToSlotManaged.Remove(cellIndex);
            }

            evicted.Dispose();
            _cache.Remove(cellIndex);

            if (_lruNodeMap.TryGetValue(cellIndex, out var node))
            {
                _lruOrder.Remove(node);
                _lruNodeMap.Remove(cellIndex);
            }
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
        // LOOKUP REBUILD
        // =====================================================================

        /// <summary>
        /// Rebuild the NativeHashMap portion of the FlowFieldLookup to reflect
        /// current cache contents. Direction data is already up-to-date in the
        /// flat NativeArray (written on AddToCache).
        /// </summary>
        private void RebuildLookup(PassabilityGrid grid)
        {
            _lookupDestToSlot.Clear();

            foreach (var kvp in _destToSlotManaged)
            {
                _lookupDestToSlot.Add(kvp.Key, kvp.Value);
            }

            CurrentLookup = new FlowFieldLookup
            {
                DirectionData = _lookupDirectionData,
                DestToSlot = _lookupDestToSlot,
                CellsPerField = _totalCells,
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                CellSize = grid.CellSize,
                Origin = grid.Origin,
                IsValid = true,
            };

            // After rebuilding with all cache slots filled at least once, mark pool as warmed up
            if (_cache.Count >= MaxCacheSize)
            {
                FlowFieldArrayPool.MarkWarmedUp();
            }
        }

        // =====================================================================
        // DESTINATION SNAPPING
        // =====================================================================

        /// <summary>
        /// Snap a cell index to a coarser grid (every SnapCells cells) for cache deduplication.
        /// Units moving to slightly different positions in the same area share one flow field.
        /// Snaps to the center of the coarser cell to maintain path quality.
        /// </summary>
        private static int SnapCellIndex(int cellIndex, int width, int height, int snapCells)
        {
            int cx = cellIndex % width;
            int cy = cellIndex / width;

            // Snap to center of coarse cell
            cx = (cx / snapCells) * snapCells + snapCells / 2;
            cy = (cy / snapCells) * snapCells + snapCells / 2;

            // Clamp to grid bounds
            cx = math.min(cx, width - 1);
            cy = math.min(cy, height - 1);

            return cy * width + cx;
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
