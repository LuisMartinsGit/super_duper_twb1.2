// InfluenceBridge.cs
// Connects the ECS game world to the GPU terrain influence painting system.
//
// DESIGN APPROACH — Why a polling bridge?
//   The terrain influence components (AlanthorInfluence, RunaiiInfluence, FeraldisInfluence)
//   are MonoBehaviours that own GPU resources (RenderTextures, shaders). The ECS systems
//   (TradingPostSystem, WallEnclosureIncomeSystem, etc.) should not know about GPU rendering.
//
//   Instead of sprinkling calls to InfluenceManager throughout a dozen ECS systems, this
//   bridge MonoBehaviour polls the ECS EntityManager on a 2-second interval, reads the
//   current game state, and pushes it to the appropriate influence component.
//
//   The one exception is Feraldis blood: unit deaths must be captured per-event (you can't
//   retroactively know which units died since the last poll). DeathSystem calls the static
//   method OnUnitDied() for that — it's the only cross-system touch.
//
// SETUP:
//   Add this component to the same GameObject as InfluenceManager.
//   The SyncTotem default claim radius is set in the inspector.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class InfluenceBridge : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Polling")]
    [Tooltip("How often (seconds) to re-read ECS state and push to influence components.")]
    [SerializeField] private float pollInterval = 2f;

    [Header("Feraldis Totem")]
    [Tooltip("World-unit claim radius applied when a Totem Tower completes. " +
             "Blood within this radius gets converted to Feraldis territory.")]
    [SerializeField] private float defaultTotemClaimRadius = 20f;

    [Header("Alanthor Tower")]
    [Tooltip("Influence circle radius around each Watch Tower (world units).")]
    [SerializeField] private float watchTowerInfluenceRadius = 15f;

    [Header("Runaii Lane")]
    [Tooltip("Half-width of each trade-lane capsule (world units).")]
    [SerializeField] private float tradeLaneHalfWidth = 4f;

    // -------------------------------------------------------------------------
    // Singleton — accessed by DeathSystem for blood events
    // -------------------------------------------------------------------------
    public static InfluenceBridge Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Cached sub-components
    // -------------------------------------------------------------------------
    private AlanthorInfluence  _alanthor;
    private RunaiiInfluence    _runaii;
    private FeraldisInfluence  _feraldis;

    // -------------------------------------------------------------------------
    // Totem tracking
    // We need to know when a totem is new (AddTotem) vs already registered
    // (do nothing) vs gone (RemoveTotem). We use entity.Index as the stable key.
    // -------------------------------------------------------------------------
    // Maps ECS entity index → FeraldisInfluence totem slot index
    private readonly Dictionary<int, int> _totemEntityToSlot = new();
    // Tracks which entity indices were seen last poll — used to detect removals
    private readonly HashSet<int> _lastSeenTotemEntities = new();

    // -------------------------------------------------------------------------
    // Poll timer
    // -------------------------------------------------------------------------
    private float _timer;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
        _alanthor = GetComponentInChildren<AlanthorInfluence>();
        _runaii   = GetComponentInChildren<RunaiiInfluence>();
        _feraldis = GetComponentInChildren<FeraldisInfluence>();

        // Run first sync immediately so influence is correct from frame 1
        _timer = pollInterval;
    }

    private void Update()
    {
        // Tick the poll timer; sync when it expires
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = pollInterval;

        // Guard: ECS world must be running
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var em = world.EntityManager;

        // Each sync method is independent; all run every pollInterval seconds
        SyncAlanthorWalls(em);
        SyncAlanthorTowers(em);
        SyncRunaiiTradeLanes(em);
        SyncFeraldisTotemTowers(em);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // =========================================================================
    // PUBLIC — called by DeathSystem (blood must be per-event, not polled)
    // =========================================================================

    /// <summary>
    /// Called by DeathSystem when a non-crystal unit reaches 0 HP.
    /// Immediately blits a blood splat into BloodMap.
    ///
    /// <paramref name="amount"/> should be in [0,1]:
    ///   Mathf.Clamp01(unit.MaxHP / 200f) works well —
    ///   a weak footsoldier (50 HP) → 0.25, a hero (200+ HP) → 1.0.
    /// </summary>
    public static void OnUnitDied(Vector3 worldPos, float amount)
    {
        // Null-safe: if there's no Feraldis system in this scene, just ignore
        if (Instance == null || Instance._feraldis == null) return;
        Instance._feraldis.SpillBlood(worldPos, amount);
    }

    // =========================================================================
    // ALANTHOR — wall polygons
    // =========================================================================

    /// <summary>
    /// Reads all WallEnclosureIncomeTag entities (created by WallEnclosureIncomeSystem)
    /// which store their polygon vertices in WallEnclosureVertex buffers.
    /// Converts those to List<List<Vector2>> and pushes to AlanthorInfluence.
    ///
    /// WallEnclosureIncomeSystem already does the hard work of finding closed chains
    /// every 5 seconds. We just piggy-back on its output here.
    /// </summary>
    private void SyncAlanthorWalls(EntityManager em)
    {
        if (_alanthor == null) return;

        // Build query: enclosure income entities that carry vertex buffers
        var query = em.CreateEntityQuery(
            ComponentType.ReadOnly<WallEnclosureIncomeTag>(),
            ComponentType.ReadOnly<FactionTag>());

        using var entities = query.ToEntityArray(Allocator.Temp);

        var polygons = new List<List<Vector2>>(entities.Length);

        for (int i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            if (!em.HasBuffer<WallEnclosureVertex>(e)) continue;

            var buf = em.GetBuffer<WallEnclosureVertex>(e, isReadOnly: true);
            if (buf.Length < 3) continue;

            var poly = new List<Vector2>(buf.Length);
            for (int v = 0; v < buf.Length; v++)
                poly.Add(buf[v].Position);

            polygons.Add(poly);
        }

        // SetWalls internally calls RequestRebakeAlanthor, so only push if
        // the polygon set has changed to avoid unnecessary GPU work.
        // (Simple: push every poll; AlanthorInfluence will re-bake once per LateUpdate anyway.)
        _alanthor.SetWalls(polygons);
    }

    // =========================================================================
    // ALANTHOR — tower circles
    // =========================================================================

    /// <summary>
    /// Queries all completed WatchTower entities and pushes their positions to
    /// AlanthorInfluence, which paints influence circles around each one.
    /// </summary>
    private void SyncAlanthorTowers(EntityManager em)
    {
        if (_alanthor == null) return;

        // Query: WatchTowerTag + LocalTransform, exclude under-construction
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All  = new[] { ComponentType.ReadOnly<WatchTowerTag>(),
                           ComponentType.ReadOnly<LocalTransform>() },
            None = new[] { ComponentType.ReadOnly<UnderConstruction>() }
        });

        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        var towers = new List<(Vector3 position, float radius)>(transforms.Length);
        for (int i = 0; i < transforms.Length; i++)
        {
            var p = transforms[i].Position;
            towers.Add((new Vector3(p.x, p.y, p.z), watchTowerInfluenceRadius));
        }

        _alanthor.SetTowers(towers);
    }

    // =========================================================================
    // RUNAII — trade lanes
    // =========================================================================

    /// <summary>
    /// Reads all valid TradeLane components (one per TradingPost entity that has a
    /// next post in sequence). Converts from-post → to-post world positions into
    /// a lane list and sends it to RunaiiInfluence.
    ///
    /// TradingPostSystem already maintains LaneValid; we trust that flag here.
    /// </summary>
    private void SyncRunaiiTradeLanes(EntityManager em)
    {
        if (_runaii == null) return;

        // Query: TradingPostTag + TradeLane + LocalTransform, exclude under-construction
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All  = new[] { ComponentType.ReadOnly<TradingPostTag>(),
                           ComponentType.ReadOnly<TradeLane>(),
                           ComponentType.ReadOnly<LocalTransform>() },
            None = new[] { ComponentType.ReadOnly<UnderConstruction>() }
        });

        using var entities   = query.ToEntityArray(Allocator.Temp);
        using var lanes      = query.ToComponentDataArray<TradeLane>(Allocator.Temp);
        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        var activeLanes = new List<(Vector3 from, Vector3 to, float width)>();

        for (int i = 0; i < entities.Length; i++)
        {
            // Skip invalid lanes (end-of-chain posts have LaneValid = 0)
            if (lanes[i].LaneValid == 0) continue;

            Entity nextPost = lanes[i].NextPost;
            if (nextPost == Entity.Null || !em.Exists(nextPost)) continue;
            if (!em.HasComponent<LocalTransform>(nextPost)) continue;

            var fromP = transforms[i].Position;
            var toP   = em.GetComponentData<LocalTransform>(nextPost).Position;

            activeLanes.Add((
                new Vector3(fromP.x, fromP.y, fromP.z),
                new Vector3(toP.x,   toP.y,   toP.z),
                tradeLaneHalfWidth
            ));
        }

        _runaii.SetLanes(activeLanes);
    }

    // =========================================================================
    // FERALDIS — totem towers
    // =========================================================================

    /// <summary>
    /// Tracks which TotemTower entities exist in the ECS world.
    ///
    /// When a new totem is found:  calls FeraldisInfluence.AddTotem()
    /// When a totem disappears:    calls FeraldisInfluence.RemoveTotem()
    /// Existing totems:            no-op (avoid re-triggering a full rebake every poll)
    ///
    /// This way we detect construction completion naturally: a TotemTower is only
    /// visible to this query once UnderConstruction is removed, which happens in
    /// CompleteConstruction().
    /// </summary>
    private void SyncFeraldisTotemTowers(EntityManager em)
    {
        if (_feraldis == null) return;

        // Query: TotemTowerTag + LocalTransform, exclude under-construction
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All  = new[] { ComponentType.ReadOnly<TotemTowerTag>(),
                           ComponentType.ReadOnly<LocalTransform>() },
            None = new[] { ComponentType.ReadOnly<UnderConstruction>() }
        });

        using var entities   = query.ToEntityArray(Allocator.Temp);
        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        // Collect the set of entity indices currently alive
        var currentEntities = new HashSet<int>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
            currentEntities.Add(entities[i].Index);

        // --- Detect removed totems (were tracked last poll, now gone) ---
        var removed = new List<int>(); // entity indices to remove from map
        foreach (var kvp in _totemEntityToSlot)
        {
            if (!currentEntities.Contains(kvp.Key))
                removed.Add(kvp.Key);
        }
        foreach (int entityIdx in removed)
        {
            _feraldis.RemoveTotem(_totemEntityToSlot[entityIdx]);
            _totemEntityToSlot.Remove(entityIdx);
            // Note: territory within that totem's radius will be cleared on the next
            // FeraldisInfluence.Rebake(), which RequestRebakeFeraldis() inside
            // RemoveTotem() will schedule.
        }

        // --- Detect new totems (present now, not yet in the map) ---
        for (int i = 0; i < entities.Length; i++)
        {
            int idx = entities[i].Index;
            if (_totemEntityToSlot.ContainsKey(idx)) continue; // already registered

            var p = transforms[i].Position;
            int slotIndex = _feraldis.AddTotem(
                new Vector3(p.x, p.y, p.z),
                defaultTotemClaimRadius);

            _totemEntityToSlot[idx] = slotIndex;
        }
    }
}
