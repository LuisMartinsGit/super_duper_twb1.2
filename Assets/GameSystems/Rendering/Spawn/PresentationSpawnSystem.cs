// PresentationSpawnSystem.cs
// Spawns and syncs visual GameObjects for ECS entities
// Per-entity builders live as partials in their entity folders (the
// Vault of Almierra, Border LargeNode, Alanthor Wall set, and the three
// Resources nodes); dead procedural Hall/Hut/Barracks builders deleted 2026-08-12.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Rendering;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.Entities;

using TheWaningBorder.Core;
// Fix #204: marked partial. The obstacle and wall procedural generators
// have been extracted to PresentationSpawnSystem.Obstacles.cs and
// PresentationSpawnSystem.Walls.cs to reduce this file's line count.
public partial class PresentationSpawnSystem : MonoBehaviour
{
    public static PresentationSpawnSystem Instance { get; private set; }

    // Prefab mapping: PresentationId -> Prefab path in Resources
    // IDs must match the PresentationID constants in each entity factory class
    private static readonly Dictionary<int, string> PrefabPaths = new()
    {
        // NO BUILDING ROWS (2026-10-20, Art_Direction.md § 13): every player
        // building's model is on its SO (SpawnBuildingFromSO), chapels' on
        // ChapelArt.asset. This table is units, creatures and the map only.

        // Units
        { 200, "Prefabs/Units/SK_Character_Human_Peasant" }, // Worker (PresentationID 200) — single-character Humanoid peasant (auto-avatar, no duplicate bones)
        { 201, "Prefabs/Units/Swordsman" },              // Swordsman.PresentationID = 201
        { 202, "Prefabs/Units/Archer" },                 // Archer.PresentationID = 202
        { 203, "Prefabs/Units/Worker" },                  // legacy worker PID 203 (no prefab; the Worker is PID 200)
        { 206, "Prefabs/Units/Scout" },                  // Scout.PresentationID = 206
        { 207, "Prefabs/Units/Litharch" },               // Litharch.PresentationID = 207
        { 250, "Prefabs/Units/Ledger" },                 // Ledger.PresentationID = 250 — floating automaton (built by WireLedgerVisuals)
        // Feraldis fire-and-blood roster: every unit now carries its own prefab
        // on its SO (2026-10-12); only the Eagle (no SO) is still listed here.
        { 345, "Prefabs/Units/Scout" },                  // Eagle.PresentationID = 345 (placeholder art)
        // Sect units carry their prefabs on their SOs (2026-10-12). 388-403
        // are all taken (chapels 390-399 plus the 400-403 Forest/Rock/deposit
        // block; chapel overflow sits at 412/413); new unit pids start at 420. A
        // duplicate key throws in the static initializer and kills ALL
        // presentation (invisible units). If you add IDs here, run a
        // duplicate-key scan over this file first.

        // Creatures & Resources
        { 300, "Prefabs/Creatures/Creature" },            // Creature.PresentationID = 300
        { 301, "Prefabs/Resources/BorderNode" },         // VeilstoneOutcropping/BorderNode.PresentationID = 301

        // Nature Obstacles (procedurally generated)
        { 400, "Procedural/Forest" },                      // ForestCluster (generated at runtime)
        { 401, "Procedural/Rock" },                        // RockFormation (generated at runtime)

        // Resources (procedurally generated)
        { 402, "Procedural/IronDeposit" },                 // IronDeposit (generated at runtime)
        { 403, "Procedural/VeilsteelDeposit" },            // Veilsteel "Sharp Crystals" node (generated at runtime)

        // Alanthor Walls (procedurally generated)
        { 550, "Procedural/WallHub" },                      // Alanthor Wall Hub (generated at runtime)
        { 551, "Procedural/WallSegment" },                   // Alanthor Wall Segment (legacy, no longer spawned)
        { 552, "Procedural/WallInstance" },                  // Alanthor Wall Instance (small wall piece)
        { 553, "Procedural/WallTower" },                     // Alanthor Wall Tower (upgraded instance)
        { 554, "Procedural/WallGate" },                      // Alanthor Wall Gate (upgraded instance)
        { 555, "Procedural/WallCurve" },                     // Alanthor curved segment: one swept mesh (drawn walls)
        { 556, "Procedural/WallCurveCell" },                 // Alanthor curved segment cell: pick collider only

        // The Border (procedurally generated)
        { 311, "Procedural/BorderGround" },                  // Border Ground tile (generated at runtime)

        // Veilstone Nodes (procedurally generated)
        { 310, "Procedural/BorderMainNode" },                // BorderMainNode (generated at runtime)

        // Veilstone Units (prefabs in Resources/Prefabs/Border/Units/)
        { 320, "Prefabs/Border/Units/Crystallings" },            // Crystalling unit prefab
        { 321, "Prefabs/Border/Units/Veilstingers" },            // Veilstinger unit prefab
        { 322, "Prefabs/Border/Units/Godsplinters" },            // Godsplinter unit prefab

    };


    /// <summary>Presentation ID for border ground tiles.</summary>
    private const int BorderGroundPresentationId = 311;

    // Longbowman (see Entities/Units/Longbowman.cs). Its corpses linger and
    // dissolve via CorpseDissolver instead of vanishing on death.
    private const int LongbowmanPresentationId = 205;

    /// <summary>Ledger (pentapod automaton). Its death is a DISASSEMBLY rather
    /// than a death clip — see LedgerDisassembly.</summary>
    private const int LedgerPresentationId = 250;

    // Phase-lock "draw lead": seconds before the reload completes that the
    // draw→shoot is cued, so the bow's release lands on the arrow spawn. ≈ draw
    // (61f) + the recoil up to the loose (~most of 41f) @ 60 fps. Tunable — see
    // UnitAnimationSync.AttackCycleSeconds.
    private const float LongbowmanAttackCycleSeconds = 1.45f;

    // Building model orientation offset. Authoring convention: building
    // prefabs + procedural meshes face -Z, but the default RTS camera
    // looks at +Z, so visuals need a 180° Y-rotation to face the camera.
    // ECS LocalTransform.Rotation stays clean (mouse-wheel yaw); the
    // offset is applied only when writing the GameObject rotation.
    private static readonly Quaternion BuildingVisualOffsetY180 = Quaternion.Euler(0f, 180f, 0f);

    /// <summary>
    /// <summary>
    /// Asset-pack character prefabs — and our unit prefab VARIANTS of them
    /// (Litharch/Scholar/Sentinel/Berserker/... are variants of ExplosiveLLC
    /// Warrior characters) — inherit the pack's demo player-controller stack:
    /// WarriorAnimsFREE input/movement scripts that poll unbound input axes
    /// ("Input Button Attack is not setup" spam) and SuperCharacterController
    /// physics that writes invalid layers and fights the ECS movement sync.
    /// Strip every such behaviour from instantiated visuals; renderers,
    /// colliders (needed for selection), and the Animator survive.
    ///
    /// Dependents are destroyed before their RequireComponent dependencies:
    /// pass 1 removes the pack's namespaced scripts, pass 2 the controller
    /// core they depend on.
    /// </summary>
    private static void StripThirdPartyControllers(GameObject go)
    {
        var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);

        // Pass 1: namespaced pack scripts (WarriorInputController,
        // WarriorMovementController, WarriorController, IKHands, ...).
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue; // missing-script slot
            string ns = b.GetType().Namespace ?? string.Empty;
            if (ns.StartsWith("WarriorAnimsFREE") || ns.StartsWith("RPGCharacterAnims"))
            {
                DestroyImmediate(b);
                behaviours[i] = null;
            }
        }

        // Pass 2: the controller core (global namespace) the pack scripts
        // RequireComponent — safe to remove now that dependents are gone.
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;
            string typeName = b.GetType().Name;
            if (typeName == "SuperCharacterController" || typeName == "SuperStateMachine")
            {
                DestroyImmediate(b);
            }
        }

        // Asset-pack clips carry baked AnimationEvents (FootL/FootR footsteps,
        // Hit, ...) whose receiver scripts are exactly what we stripped above —
        // without a receiver every loop logs "AnimationEvent ... has no
        // receiver". Game logic is ECS-driven, so presentation animators don't
        // consume events at all: stop firing them.
        var animators = go.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].fireEvents = false;
    }

    /// <summary>
    /// Apply the +180° Y visual offset for any entity with BuildingTag.
    /// Units pass through unchanged.
    /// </summary>
    /// <summary>A building, or a PLAN of one (docs/Design/Planned_Buildings.md):
    /// a plan has no BuildingTag on purpose, but it must pick, fit and face its
    /// visual exactly as the building it will become.</summary>
    private bool IsBuildingLike(Entity entity)
        => _em.HasComponent<BuildingTag>(entity) || _em.HasComponent<PlannedBuilding>(entity);

    private Quaternion VisualRotation(Entity entity, Quaternion entityRot)
    {
        if (IsBuildingLike(entity))
            return entityRot * BuildingVisualOffsetY180;
        return entityRot;
    }

    // Fallback prefabs if specific one not found
    private GameObject _fallbackUnitPrefab;
    private GameObject _fallbackBuildingPrefab;

    // Track which entities already have visuals
    private HashSet<Entity> _spawnedEntities = new();

    // Entities that gained a view this frame, tagged in one batch at the end.
    private readonly List<Entity> _tagScratch = new();
    /// <summary>Viewed entities still waiting for the sim to seed their
    /// PresentationViewSpawned component; see FlushViewTags.</summary>
    private readonly List<Entity> _tagPending = new();

    /// <summary>
    /// Per-view sync state. SyncTransforms runs over EVERY presentation entity
    /// every frame, and most of them are buildings, wall modules, obstacles and
    /// resource nodes that never move once placed. For each of those it was
    /// still paying, every frame: a Terrain.SampleHeight, two GetComponent
    /// walks, and three transform writes. A finished AI perimeter alone is
    /// ~165 wall modules per faction, so this scaled straight into the frame
    /// time as the map filled up. Caching the resolved components and the
    /// last-written transform lets a static entity cost one comparison.
    /// </summary>
    private sealed class ViewSync
    {
        /// <summary>The GameObject the cached components were read off. A
        /// building that swaps prefab (level-up, culture variant) gets a NEW
        /// view for the same entity, so compare before trusting the cache.
        /// </summary>
        public GameObject View;
        public ProceduralScaleTag ScaleTag;
        public BuildingRiseData Rise;
        /// <summary>An emplaced engine stands on a wall deck: its simulated
        /// Y IS its height, and no ground/bridge sample may replace it.</summary>
        public bool KeepSimY;
        /// <summary>Building or plan (gets the +180° visual offset) —
        /// resolved with the view instead of two HasComponent calls per
        /// moving unit per frame (2026-10-15).</summary>
        public bool BuildingLike;
        /// <summary>Resolved with the view — was a GetComponent per frame
        /// for every building under construction.</summary>
        public BuildingVisualSinkDepth Sink;
        /// <summary>Frame this view was last synced (see SyncView).</summary>
        public int LastFrame = -1;

        public bool Primed;
        public Unity.Mathematics.float3 LastPos;
        public Unity.Mathematics.quaternion LastRot;
        public float LastScale;
        public bool LastUnderConstruction;
        /// <summary>TerrainUtility.TerrainVersion at the last write — a
        /// changed value means the ground moved under this visual (terrain
        /// finished generating, or the scene swapped) and the cached height
        /// must be re-sampled.</summary>
        public int LastTerrainVersion;
        /// <summary>What was last WRITTEN to the view's transform (only
        /// valid while Primed - a new view clears Primed). A frame whose
        /// computed pose matches exactly skips the write: a Transform write
        /// is never free (hierarchy-change dispatch), even with an equal value.</summary>
        public Vector3 WrittenPos;
        public Quaternion WrittenRot;
        public float WrittenScale = float.NaN;

        // ── Fixed-step interpolation ────────────────────────────────────
        //
        // Under lockstep the simulation publishes transforms in discrete jumps
        // (once per tick, not once per frame), and a view snapped straight to
        // them steps visibly between ticks — the whole game reads as though it
        // were running at the tick rate. These carry the segment being played
        // out between the last two simulated transforms.
        //
        // Unused when the simulation free-runs per frame: there is nothing to
        // interpolate then, and doing it anyway would add a frame of lag.

        /// <summary>Where the view was when the current segment started.</summary>
        public Unity.Mathematics.float3 FromPos;
        public Quaternion FromRot;
        /// <summary>The simulated transform the segment is heading for.</summary>
        public Unity.Mathematics.float3 ToPos;
        public Quaternion ToRot;
        /// <summary>0..1 progress through the current segment.</summary>
        public float SegmentT;
        /// <summary>Last transform written to the GameObject.</summary>
        public Unity.Mathematics.float3 RenderPos;
        public Quaternion RenderRot;
    }

    /// <summary>
    /// A simulated jump longer than this is a teleport (spawn, snap-to-passable
    /// escape, rally warp), not movement — interpolating across it would drag
    /// the visual through everything in between.
    /// </summary>
    private const float TeleportSnapDistanceSq = 8f * 8f;

    private readonly Dictionary<Entity, ViewSync> _viewSync = new();

    /// <summary>Reused scratch for the per-frame destroyed-entity sweep.</summary>
    private readonly List<Entity> _staleScratch = new();

    // Cache
    private Unity.Entities.World _world;
    private EntityManager _em;
    private EntityQuery _presentationQuery;

    /// <summary>
    /// The same set MINUS anything that already has a view. This is what makes
    /// spawning reactive: in the steady state it matches nothing, so the spawn
    /// pass ends at an IsEmpty check instead of sweeping every entity in the
    /// world to conclude there is nothing to do.
    /// </summary>
    private EntityQuery _missingViewQuery;


    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Create fallback primitives
        _fallbackUnitPrefab = CreateFallbackPrefab("FallbackUnit", PrimitiveType.Capsule, 0.5f);
        _fallbackBuildingPrefab = CreateFallbackPrefab("FallbackBuilding", PrimitiveType.Cube, 2f);
    }

    void Start()
    {
        _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        if (_world != null && _world.IsCreated)
        {
            _em = _world.EntityManager;
            _presentationQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            _missingViewQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<PresentationViewSpawned>()
            );
        }
    }

    void Update()
    {
        if (_world == null || !_world.IsCreated) return;

        // Instrumented (2026-08-16 perf sweep): three unthrottled full sweeps
        // per frame — the prime suspect for the unattributed FRAME spikes.
        double t0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
        CleanupDestroyedEntities();
        DrainFactionRefreshes();
        SpawnMissingVisuals();
        PumpPendingReclads();
        SyncTransforms();
        TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("PresentSync",
            (UnityEngine.Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
    }

    /// <summary>
    /// Detects ECS entities that have been destroyed (e.g., by DeathSystem)
    /// and cleans up their corresponding GameObjects.
    /// </summary>
    private void CleanupDestroyedEntities()
    {
        if (EntityViewManager.Instance == null) return;

        // Collect stale entities (can't modify HashSet during iteration).
        // Reused across frames — this ran every frame and allocated a fresh
        // List even on the overwhelmingly common frame where nothing died.
        _staleScratch.Clear();

        foreach (var entity in _spawnedEntities)
        {
            if (!_em.Exists(entity))
            {
                _staleScratch.Add(entity);
            }
        }

        foreach (var entity in _staleScratch)
        {
            _spawnedEntities.Remove(entity);
            _viewSync.Remove(entity);

            if (EntityViewManager.Instance.TryGetView(entity, out var go))
            {
                EntityViewManager.Instance.UnregisterView(entity);
                if (go != null)
                {
                    var outcroppingAnim = go.GetComponent<TheWaningBorder.Rendering.VeilstoneOutcroppingCrystalAnimator>();
                    var disassembly = go.GetComponent<TheWaningBorder.Rendering.LedgerDisassembly>();
                    var corpse = go.GetComponent<CorpseDissolver>();
                    if (disassembly != null)
                    {
                        // Ledger: scatter the parts, let them fall, and only
                        // then hand the wreckage to CorpseDissolver. Checked
                        // before the plain corpse branch because the Ledger
                        // ends up carrying both.
                        disassembly.BeginDeath();
                    }
                    else if (outcroppingAnim != null)
                    {
                        // Hand off to the Shatter Stone animator: it spawns
                        // the asset's debris pieces (which outlive this GO,
                        // they aren't parented) and owns the final Destroy.
                        outcroppingAnim.BeginDeath();
                    }
                    else if (corpse != null)
                    {
                        // Longbowman corpse: let it finish the death animation,
                        // linger in the final pose, then dissolve. It owns the
                        // final Destroy of this GameObject.
                        corpse.BeginDeath();
                    }
                    else
                    {
                        Destroy(go);
                    }
                }
            }
        }
    }

    /// <summary>In-match cap on view creations per frame. A training wave
    /// completing across four factions used to Instantiate a dozen-plus
    /// animated rigs in ONE frame — prefab instantiate + Animator init all
    /// landing together, the periodic Animation-category frame spike of the
    /// 2026-08-31 profile. Spread over frames the same work is invisible; a
    /// unit appearing two frames late does not exist at RTS zoom. The budget
    /// is lifted while the loading overlay is up so the initial map
    /// population (hundreds of nodes/forests/buildings) still lands before
    /// the player sees the world.</summary>
    private const int MaxViewSpawnsPerFrame = 8;

    // Reused across frames — burst frames are the norm for this list's
    // lifetime, and per-frame allocation was the GC drip this system already
    // fixed once in CleanupDestroyedEntities.
    private readonly System.Collections.Generic.List<int> _missingScratch = new();

    private void SpawnMissingVisuals()
    {
        if (_presentationQuery == null) return;

        // THE WHOLE POINT: nothing new exists, so there is nothing to do and
        // we never touch the world. This is true for the overwhelming majority
        // of frames in a match — the pass used to copy three arrays covering
        // every entity in the world to discover exactly that.
        // IsEmpty, NOT IsEmptyIgnoreFilter (2026-09-25). PresentationViewSpawned
        // is ENABLEABLE and pre-added (disabled) to every presented entity, so
        // every archetype CARRIES it and the Exclude is answered per entity by
        // the enabled bit, not by the archetype match. IsEmptyIgnoreFilter
        // ignores enabled bits and was therefore never true: the early-out
        // never fired and the three-array copy below ran every frame. Same
        // trap PresentationViewTagSeedSystem documents for None<T>.
        if (_missingViewQuery.IsEmpty) return;

        var entities = _missingViewQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var presentations = _missingViewQuery.ToComponentDataArray<PresentationId>(Unity.Collections.Allocator.Temp);
        var transforms = _missingViewQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

        // Pass 1: what is missing. Cheap set lookups only.
        _missingScratch.Clear();
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (_spawnedEntities.Contains(entity)) continue;
            if (EntityViewManager.Instance != null &&
                EntityViewManager.Instance.TryGetView(entity, out _))
            {
                _spawnedEntities.Add(entity);
                _tagScratch.Add(entity);
                continue;
            }
            _missingScratch.Add(i);
        }

        // Pass 2: spawn under the budget, nearest to the camera first, so a
        // burst that must be split always materialises the views the player
        // is actually looking at.
        int budget = TheWaningBorder.Core.PresentationState.LoadingOverlayVisible
            ? int.MaxValue : MaxViewSpawnsPerFrame;

        if (_missingScratch.Count > budget)
        {
            var cam = TheWaningBorder.Core.PresentationState.MainCamera;
            if (cam != null)
            {
                Vector3 cp = cam.transform.position;
                _missingScratch.Sort((a, b) =>
                {
                    Vector3 pa = transforms[a].Position, pb = transforms[b].Position;
                    float da = (pa.x - cp.x) * (pa.x - cp.x) + (pa.z - cp.z) * (pa.z - cp.z);
                    float db = (pb.x - cp.x) * (pb.x - cp.x) + (pb.z - cp.z) * (pb.z - cp.z);
                    return da.CompareTo(db);
                });
            }
        }

        int spawned = 0;
        for (int m = 0; m < _missingScratch.Count && spawned < budget; m++)
        {
            int i = _missingScratch[m];
            var entity = entities[i];

            var go = SpawnVisual(entity, presentations[i].Id, transforms[i]);
            if (go != null)
            {
                if (EntityViewManager.Instance != null)
                    EntityViewManager.Instance.RegisterView(entity, go);
                _spawnedEntities.Add(entity);
                _tagScratch.Add(entity);
                spawned++;
            }
            // A null view retries next frame, exactly as before the budget —
            // it costs no budget, so it cannot starve the queue behind it. It
            // is deliberately NOT tagged, so it stays in _missingViewQuery.
        }

        entities.Dispose();
        presentations.Dispose();
        transforms.Dispose();

        FlushViewTags();
    }

    /// <summary>
    /// Flip PresentationViewSpawned ON for everything that got a view this
    /// frame. Never a structural change (2026-09-13): this MonoBehaviour runs
    /// on FRAME time under a per-frame spawn budget, so an AddComponent here
    /// landed on a different SIM TICK on every lockstep peer -- the Veilmarch
    /// 15-31-23 fork (a Hall foundation placed on tick 48565 got its view tag
    /// on 48566 on two peers and on 48569/48571 on the other two; the chunk
    /// reorder then forked Pos/Rot/Nav/rng 64 ticks later). Units have carried
    /// the tag pre-added DISABLED since 2026-09-03 (TransientState); every
    /// other PresentationId entity gets it from PresentationViewTagSeedSystem,
    /// which runs inside the sim tick and is therefore the same on all peers.
    /// An entity whose seed has not landed yet waits in _tagPending -- it is
    /// already in _spawnedEntities, so it is never spawned twice.
    /// </summary>
    private void FlushViewTags()
    {
        // Retry the ones that were waiting for the seed.
        if (_tagPending.Count > 0)
        {
            for (int i = _tagPending.Count - 1; i >= 0; i--)
            {
                var e = _tagPending[i];
                if (!_em.Exists(e)) { _tagPending.RemoveAt(i); continue; }
                if (!_em.HasComponent<PresentationViewSpawned>(e)) continue;
                _em.SetComponentEnabled<PresentationViewSpawned>(e, true);
                _tagPending.RemoveAt(i);
            }
        }

        if (_tagScratch.Count == 0) return;
        for (int i = 0; i < _tagScratch.Count; i++)
        {
            var e = _tagScratch[i];
            if (_em.HasComponent<PresentationViewSpawned>(e))
                _em.SetComponentEnabled<PresentationViewSpawned>(e, true);
            else
                _tagPending.Add(e);
        }
        _tagScratch.Clear();
    }

    private GameObject SpawnVisual(Entity entity, int presentationId, LocalTransform transform)
    {
        // Get position and adjust Y to terrain height using shared utility
        Vector3 pos = transform.Position;
        // An emplaced engine stands on a wall deck, not on the ground under
        // it: its simulated Y is where it is drawn.
        if (!_em.HasComponent<EmplacedEngineTag>(entity))
            pos.y = TerrainUtility.GetHeight(pos.x, pos.z);

        // === PROCEDURAL OBSTACLES: generate compound GameObjects instead of loading prefabs ===
        if (presentationId == ObstacleBootstrap.ForestPresentationId)
        {
            float radius = _em.HasComponent<Radius>(entity) ? _em.GetComponentData<Radius>(entity).Value : 5f;
            var go = CreateProceduralForest(pos, radius, entity);
            return go;
        }
        if (presentationId == ObstacleBootstrap.RockPresentationId)
        {
            float radius = _em.HasComponent<Radius>(entity) ? _em.GetComponentData<Radius>(entity).Value : 3f;
            var go = CreateProceduralRockFormation(pos, radius, entity);
            return go;
        }
        if (presentationId == IronDepositBootstrap.IronDepositPresentationId)
        {
            var go = CreateProceduralIronDeposit(pos, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.SupplyNode.PresentationID)
        {
            var go = CreateProceduralSupplyNode(pos, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Bootstrap.VeilsteelDepositBootstrap.VeilsteelDepositPresentationId)
        {
            var go = CreateProceduralVeilsteelDeposit(pos, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.SanctumCrystal.PresentationID)
            return CreateSanctumCrystal(pos, entity);

        // === ALANTHOR WALLS: procedural hub, instances, towers, gates ===
        // Wall pieces carry the owner's colour on their ownership parts, and
        // they do NOT go through ApplyFactionColor: that path uses
        // `renderer.materials`, which clones a material per renderer, and a
        // finished perimeter is hundreds of pieces. ApplyWallOwnerColor does
        // the same job through a property block.
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.HubPresentationID)
        {
            var go = CreateProceduralWallHub(pos, entity);
            ApplyWallOwnerColor(go, entity);
            return go;
        }
        if (presentationId == 551) // Legacy segment — kept for backward compat
        {
            var go = CreateProceduralWallSegment(pos, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.InstancePresentationID)
        {
            var go = CreateProceduralWallInstance(pos, entity);
            AttachConstructionAnimation(go);
            ApplyWallOwnerColor(go, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.CurvedSegmentPresentationID)
            return CreateProceduralCurvedWall(pos, entity);   // tints its own sub-meshes
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.CurveCellPresentationID)
            return CreateCurveCellPick(pos, entity);
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.BallistaEmplacementPresentationID
            || presentationId == TheWaningBorder.Entities.AlanthorWall.TrebuchetEmplacementPresentationID)
        {
            var go = CreateProceduralWallEmplacement(pos, entity,
                presentationId == TheWaningBorder.Entities.AlanthorWall.TrebuchetEmplacementPresentationID);
            AttachConstructionAnimation(go);
            ApplyWallOwnerColor(go, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.TowerPresentationID)
        {
            var go = CreateProceduralWallTower(pos, entity);
            AttachConstructionAnimation(go);
            ApplyWallOwnerColor(go, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.GatePresentationID)
        {
            var go = CreateProceduralWallGate(pos, entity);
            AttachConstructionAnimation(go);
            ApplyWallOwnerColor(go, entity);
            return go;
        }

        // === PLAYER BUILDINGS: ONLY THROUGH THE SO (2026-10-20, Art_Direction.md § 13) ===
        // Base SO at level 0 / under construction / a plan; the culture's
        // level SO at level N; ChapelArt for a chapel. Nothing else.
        if (TryResolveBuildingVisual(_em, entity, out var soModel))
            return SpawnBuildingFromSO(entity, soModel, transform, pos);

        // === Unit authored visuals (tech-tree implementation) ===
        // High-detail procedural builders co-located with their entities. Units
        // get the hand-rolled tail (collider + EntityReference; their animator
        // MonoBehaviours self-tint the faction accents); buildings route through
        // FinishProceduralBuilding for rotation/collider/rise/damage plus the
        // faction marker for their Stripe_* accent parts. The seed keeps the
        // deterministic-randomness convention (entity.Index + salt).
        {
            GameObject authored = null;
            bool authoredIsBuilding = false;
            bool spawnsFinished = false;   // Field Hospital raises pre-built

            // Real authored art SUPERSEDES the procedural placeholder. These
            // builders were written as stand-ins until a building had a model;
            // because this switch runs before the prefab resolution below, a
            // freshly authored prefab was silently ignored and the placeholder
            // kept rendering (the Royal Stable kept its procedural look after
            // being given real art). Checked generically off the SO prefab's
            // layout, so every future building supersedes its own placeholder
            // the moment its art lands — no per-id edit here.
            switch (presentationId)
            {
                // 368 Spearman: no procedural case — its SO prefab (the MPFB
                // human in its unit folder) is the visual now.
                // 201: the Swordsman prefab is a Synty variant whose skinned-mesh
                // guid resolves to nothing on disk (invisible). Authored rig wins.
                case 201: authored = TheWaningBorder.Rendering.SwordsmanVisual.Build(entity.Index + 201); break;
                case 202: authored = TheWaningBorder.Rendering.ArcherVisual.Build(entity.Index + 202); break;
                case 349: authored = TheWaningBorder.Rendering.OutriderVisual.Build(entity.Index + 349); break;
                case 336: authored = TheWaningBorder.Rendering.CataphractVisual.Build(entity.Index + 336); break;
                case 251: authored = TheWaningBorder.Rendering.KingLexorVisual.Build(entity.Index + 251); break;
                case 346: authored = TheWaningBorder.Rendering.NoblemanVisual.Build(entity.Index + 346); break;
                case 347: authored = TheWaningBorder.Rendering.BatteringRamVisual.Build(entity.Index + 347); break;
                case 348: authored = TheWaningBorder.Rendering.TrebuchetVisual.Build(entity.Index + 348); break;
                // The Ballista — mobile and wall-mounted — is procedural
                // (2026-09-29); it used to render as the Hunter's human model.
                case TheWaningBorder.Entities.Ballista.PresentationID:
                    authored = TheWaningBorder.Rendering.BallistaVisual.Build(entity.Index + 385); break;
                // The emplacement PLATFORMS. Their engines are separate
                // entities and reuse the mobile Ballista / Trebuchet art
                // (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet
                // emplacements).
                // Raise Anew (Renewal) — the three conjured fortifications, one
                // per power level. Permanent, and raised pre-built like the tent.
                // Age 0 choice buildings (culture-neutral):
                // 530: the Vault generator predates this switch and already fits
                //      its own collider + EntityReference at its tail; both are
                //      idempotent under FinishProceduralBuilding (the fitter
                //      reuses the root BoxCollider, the reference is get-or-add),
                //      which adds the rotation / sink-rise construction animation /
                //      faction marker the generator lacks. No double-finishing.
                // Sect buildings (561-564) — one per sect, capped at 5 per
                // faction. Alanthor cluster; the other eight land with their
                // pass. docs/Design/Sects.md section 1.
                // Sect units. These pids already had PrefabPaths rows pointing
                // at borrowed Synty/Litharch stand-ins; this switch runs first,
                // so the authored rigs win without touching that dictionary.
                case 370: authored = TheWaningBorder.Rendering.ScarGuardVisual.Build(entity.Index + 370); break;
                case 371: authored = TheWaningBorder.Rendering.CollectorVisual.Build(entity.Index + 371); break;
                case 372: authored = TheWaningBorder.Rendering.SallyguardVisual.Build(entity.Index + 372); break;
                case 387: authored = TheWaningBorder.Rendering.ArtificerVisual.Build(entity.Index + 387); break;

            }

            if (authored != null)
            {
                authored.transform.position = pos;

                if (authoredIsBuilding)
                {
                    var finished = FinishProceduralBuilding(authored, entity, transform);
                    ApplyFactionColor(finished, entity);   // tints the Stripe_* accents
                    if (spawnsFinished)
                    {
                        // No construction phase: neutralize the sink/rise state the
                        // builder tail added so the tent stands complete at once.
                        var rise = finished.GetComponent<TheWaningBorder.Rendering.BuildingRiseData>();
                        if (rise != null) rise.ApplyRise(1f, 0f);
                    }
                    return finished;
                }

                // Unit tail: rotation/scale ride SyncTransforms; collider + link here.
                var scaleTag = authored.GetComponent<ProceduralScaleTag>();
                float baseScale = scaleTag != null && scaleTag.BaseScale > 0.001f ? scaleTag.BaseScale : 1f;
                authored.transform.localScale = Vector3.one * transform.Scale * baseScale;
                FitSelectionCollider(authored, entity, _em);
                var er = authored.GetComponent<EntityReference>();
                if (er == null) er = authored.AddComponent<EntityReference>();
                er.Entity = entity;
                return authored;
            }
        }

        // Units & standard buildings (Hall/Hut/Barracks/etc.) now get their visual
        // from the prefab on their SO (resolved by PresentationId via TechCatalog), with a
        // cube/capsule fallback — see the prefab resolution near the end of this method.
        // The walls / obstacles / veilstone-node / border-tile branches above keep their own
        // dedicated visuals.

        // === THE BORDER ground tiles: voxel-block cluster visual RETIRED
        // (2026-07-24). It was the OLD Border design's ground look (task-111);
        // under the Curse & Shardroot canon the cursed ground is painted
        // per-pixel by the TWB/Terrain/Lit curse overlay (InfluenceMaskTexture
        // masks), and the legacy blocks rendered as untextured checker slabs
        // on top of it. The sim entity keeps an invisible anchor GameObject
        // so cleanup/handoff paths stay untouched. ===
        if (presentationId == BorderGroundPresentationId)
        {
            var go = new GameObject("BorderGroundTile (visual retired)");
            go.transform.position = pos;
            return go;
        }

        // === CRYSTAL LOOT PILE (outcropping): procedural veilstone cluster on the ground ===
        if (presentationId == 301)
        {
            var go = CreateProceduralVeilstoneOutcroppingLoot(pos, entity);
            return go;
        }

        // === THE SHARDROOT (ground pickup, or embedded in a Maw) ===
        if (presentationId == TheWaningBorder.Core.Config.BorderConstants.ShardrootPresentationID)
            return CreateShardrootVisual(pos, entity);

        // === CRYSTAL NODES (buildings): procedural veilstone-themed visuals ===
        // Veilstone UNITS (320-322) use actual prefabs, so they fall through to prefab loading below
        if (presentationId == 310)
        {
            var go = CreateProceduralCrystalEntity(pos, presentationId, entity);
            AttachConstructionAnimation(go);
            return go;
        }

        // Procedural unit/building generation has been removed — visuals now come from the
        // prefab referenced on the entity's SO (resolved by PresentationId via TechCatalog),
        // falling back to a primitive cube/capsule when no prefab is assigned.

        GameObject prefab = null;

        // Authoritative visual: the prefab referenced on this entity's SO (under GameData).
        // Null prefab (or no SO for this PresentationId) falls through to the ID-range
        // cube/capsule fallback below.
        if (TechCatalog.TryGetPrefab(presentationId, out var soPrefab))
            prefab = soPrefab;

        // Default prefab for this presentation id
        if (prefab == null && PrefabPaths.TryGetValue(presentationId, out string path))
        {
            prefab = Resources.Load<GameObject>(path);
        }

        // Guaranteed primitive fallback (2026-07-13): CUBE for buildings,
        // CAPSULE for units — decided by the entity's actual components,
        // not the old ID-range guess (new PresentationIds outside the
        // 200-499 unit band were getting building cubes). ID range stays
        // as the tiebreak for entities carrying neither tag.
        if (prefab == null)
        {
            if (_em.HasComponent<UnitTag>(entity))
                prefab = _fallbackUnitPrefab;
            else if (IsBuildingLike(entity))
                prefab = _fallbackBuildingPrefab;
            else if (presentationId >= 200 && presentationId < 500)
                prefab = _fallbackUnitPrefab;
            else
                prefab = _fallbackBuildingPrefab;
        }

        if (prefab == null) return null;

        var goInst = Instantiate(prefab);
        goInst.SetActive(true); // Ensure active (fallback prefabs are stored inactive)
        StripThirdPartyControllers(goInst);
        goInst.name = $"Entity_{entity.Index}_{presentationId}";
        goInst.transform.position = pos;
        goInst.transform.rotation = VisualRotation(entity, transform.Rotation);
        // Preserve the prefab's authored root scale (FBX-variant prefabs often
        // carry a non-1 root scale). The placement preview instantiates the
        // same prefab as-is, so stomping the root with the bare ECS scale made
        // the built visual differ in size from its own preview. The
        // ProceduralScaleTag keeps SyncTransforms applying the same factor
        // every frame.
        {
            var authored = prefab.transform.localScale;
            float authoredUniform = (authored.x + authored.y + authored.z) / 3f;
            if (authoredUniform < 0.001f) authoredUniform = 1f;
            // Snug-fit sized buildings to their grid footprint. Returns 1 for
            // units, nodes and unsized buildings, so this is a no-op for them.
            // docs/Design/Build_Grid.md
            float authoredPreFit = authoredUniform;
            authoredUniform *= ComputeFootprintFit(goInst, entity, out Vector3 variantOffset);
            if (!Mathf.Approximately(authoredUniform, 1f) || variantOffset != Vector3.zero)
            {
                var scaleTag = goInst.GetComponent<ProceduralScaleTag>()
                            ?? goInst.AddComponent<ProceduralScaleTag>();
                scaleTag.BaseScale = authoredUniform;
                scaleTag.AuthoredScale = authoredPreFit;
                scaleTag.BaseOffset = variantOffset;
            }
            goInst.transform.localScale = Vector3.one * transform.Scale * authoredUniform;
        }

        // Selection collider — always refit to the visual's renderer bounds
        // so the click target matches the mesh. Prefab-authored colliders are
        // routinely mismatched (too big → block clicks on things behind; too
        // small / pivot-centered → half the box underground, hard to click).
        // Floored by the entity's sim footprint so it can never come out
        // smaller than the space the building actually occupies.
        FitSelectionCollider(goInst, entity, _em);

        // Add EntityReference for raycasting/selection
        var entityRef = goInst.GetComponent<EntityReference>();
        if (entityRef == null)
            entityRef = goInst.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Attach VeilstingerGunTracker for Veilstinger units (PresentationId 321)
        // This rotates leftgun/rightgun children to LookAt their respective targets
        if (presentationId == 321)
        {
            var gunTracker = goInst.AddComponent<VeilstingerGunTracker>();
            gunTracker.Entity = entity;
        }

        // Attach UnitAnimationSync for units that have an Animator
        // (units have MoveSpeed, buildings don't)
        if (_em.HasComponent<MoveSpeed>(entity))
        {
            // Ledger: a clockwork automaton comes APART when it dies. Attached
            // outside the Animator branch below on purpose — LedgerWalker
            // disables the Animator (its gait is procedural), so anything gated
            // on one would never be added to a Ledger at all.
            if (presentationId == LedgerPresentationId)
            {
                goInst.AddComponent<TheWaningBorder.Rendering.LedgerDisassembly>();
            }

            var animator = goInst.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // OFF-SCREEN RIGS COST NOTHING (2026-08-31 profile): the
                // prefabs ship with the Unity default (AlwaysAnimate), so
                // every off-screen army was evaluating its full skeleton
                // every frame. CullUpdateTransforms keeps the state machine
                // advancing (no pose pop or timing desync when a unit
                // scrolls into view) but skips the bone writes while no
                // renderer is visible — which is most of the map, most of
                // the time.
                // 2026-10-15 (3,000-unit pass): the culling mode, keep-state-
                // on-disable and the skinned-mesh flags now come from
                // UnitAnimationSync.asset — CullCompletely by default, which
                // also stops the state machine and clip sampling off-screen.
                UnitAnimationSync.ConfigureView(goInst, animator);

                var animSync = goInst.AddComponent<UnitAnimationSync>();
                animSync.LinkedEntity = entity;

                // If the prefab's Animator has no controller of its own (e.g. a
                // prefab that is a variant of a character FBX — its inherited
                // Animator ships without one), assign the controller authored on
                // the unit's SO. Set BEFORE UnitAnimationSync.Start() runs, so its
                // parameter probe sees the controller's parameters. Units whose
                // prefab already bakes in a controller (e.g. Longbowman) keep it.
                if (animator.runtimeAnimatorController == null &&
                    TechCatalog.TryGetController(presentationId, out var soController))
                {
                    animator.runtimeAnimatorController = soController;
                }

                if (presentationId == LongbowmanPresentationId)
                {
                    // Sync the draw→shoot cycle to the unit's fire rate so
                    // exactly one cycle plays per arrow (not ~1.5).
                    animSync.AttackCycleSeconds = LongbowmanAttackCycleSeconds;
                }

                // Longbowman corpses linger in their final death pose then
                // dissolve out, rather than popping out of existence. The
                // component stays dormant until CleanupDestroyedEntities hands it
                // the GameObject on entity destruction (BeginDeath).
                if (presentationId == LongbowmanPresentationId)
                {
                    goInst.AddComponent<CorpseDissolver>();
                }
            }
        }

        // Apply faction coloring
        ApplyFactionColor(goInst, entity);

        // Progressive battle-damage shader for buildings that spawn via the
        // generic prefab path (those that don't go through FinishProceduralBuilding).
        if (_em.HasComponent<BuildingTag>(entity))
        {
            var dmgVis = goInst.GetComponent<TheWaningBorder.Rendering.BuildingDamageVisual>()
                      ?? goInst.AddComponent<TheWaningBorder.Rendering.BuildingDamageVisual>();
            dmgVis.Entity = entity;

            // Team-color the atlas: blue-painted regions -> faction color.
            // FinishProceduralBuilding does this for its path; the generic
            // SO-prefab path never did, so prefab buildings kept their
            // authored blue roofs/trim regardless of owner.
            if (_em.HasComponent<FactionTag>(entity))
            {
                var fac = _em.GetComponentData<FactionTag>(entity).Value;
                BuildingFactionColorMarker.Apply(goInst, FactionColors.Get(fac));
            }

            // Construction rise: sink depth from ACTUAL renderer bounds + the
            // per-piece rise snapshot. This generic prefab path never attached
            // these, so SyncTransforms fell back to a Radius*2 sink — any
            // building mesh taller than that poked its roof above the ground
            // at construction progress 0. Must run after position/scale are
            // final (bounds are world-space).
            AttachConstructionAnimation(goInst);

            // Warm lantern pool for large buildings (Art_Direction.md §4.4).
            TheWaningBorder.Rendering.BuildingLanternLight.Attach(goInst, entity, _em);
        }

        return goInst;
    }

    private void ApplyFactionColor(GameObject go, Entity entity)
    {
        if (!_em.HasComponent<FactionTag>(entity)) return;

        var faction = _em.GetComponentData<FactionTag>(entity).Value;
        var color = FactionColors.Get(faction); // Returns culture color if faction has aged up

        bool isBuilding = _em.HasComponent<BuildingTag>(entity);

        if (isBuilding)
        {
            // Single source of truth for the building faction-color rule
            // (roof / stripe / atlas swap). This used to be a second, divergent
            // copy of the roof+stripe rule that lived only here — so any visual
            // produced outside PresentationSpawnSystem (notably the upgrade
            // prefab swap) never got it and kept an authored blue roof.
            BuildingFactionColorMarker.Apply(go, color);
        }
        // Units keep their authored prefab materials untinted. Team ownership
        // reads through selection rings, health bars, and the minimap instead.
        // (The old whole-body wash / Synty atlas team-region recolor was removed
        // here — it flattened the real character models wired from the KI and
        // ExplosiveLLC packs into a single flat colour.)
    }

    /// <summary>
    /// Finish setting up a procedurally generated building: collider, EntityReference,
    /// transform sync, and faction coloring. Mirrors the setup done for prefab-based buildings.
    /// </summary>
    private GameObject FinishProceduralBuilding(GameObject go, Entity entity, LocalTransform transform)
    {
        go.name = $"Entity_{entity.Index}_{(_em.HasComponent<PresentationId>(entity) ? _em.GetComponentData<PresentationId>(entity).Id : 0)}";
        go.transform.rotation = VisualRotation(entity, transform.Rotation);
        // Keep the authored root scale that the caller already applied
        // (ProceduralScaleTag.BaseScale for FBX-variant prefabs). This used to
        // assign `Vector3.one * transform.Scale`, dropping BaseScale — so the
        // collider below was fitted at a scale the visual does not run at.
        {
            var scaleTag = go.GetComponent<ProceduralScaleTag>();
            float baseScale = scaleTag != null && scaleTag.BaseScale > 0.001f ? scaleTag.BaseScale : 1f;
            go.transform.localScale = Vector3.one * transform.Scale * baseScale;
        }

        // Aggregate selection collider fitted to the actual visual bounds
        // (shared fitter — also clears the same-frame-queryable colliders the
        // Prim helpers destroyed), floored by the entity's sim footprint.
        FitSelectionCollider(go, entity, _em);

        // Cache the visual extent so SyncTransforms can sink the building
        // fully underground during construction. Pivot may sit above the
        // ground (authored prefabs often anchor at base centre, but some
        // sit higher), so include any positive max-y on top of size.y.
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int r = 1; r < renderers.Length; r++)
                    bounds.Encapsulate(renderers[r].bounds);

                float visualHeight = bounds.size.y +
                    Mathf.Max(0f, bounds.max.y - go.transform.position.y);
                if (visualHeight > 0f)
                {
                    var sink = go.GetComponent<TheWaningBorder.Rendering.BuildingVisualSinkDepth>()
                            ?? go.AddComponent<TheWaningBorder.Rendering.BuildingVisualSinkDepth>();
                    sink.Value = visualHeight + 0.5f; // small pad keeps the tip below
                }
            }
        }

        // EntityReference for raycasting/selection
        var entityRef = go.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = go.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Warm lantern pool for large buildings (Art_Direction.md §4.4).
        TheWaningBorder.Rendering.BuildingLanternLight.Attach(go, entity, _em);

        // Authored prefabs paint team-color regions with a flat marker hue
        // (default pure blue). Replace with the faction color at runtime so
        // each player's buildings carry their lobby color. Procedural
        // buildings have no marker materials so this is a no-op for them.
        if (_em.HasComponent<FactionTag>(entity))
        {
            var fac = _em.GetComponentData<FactionTag>(entity).Value;
            BuildingFactionColorMarker.Apply(go, FactionColors.Get(fac));
        }

        // Multi-variant authored prefabs (Lv0 + per-culture Lv1-Lv3 inside
        // one prefab): hide the culture branches so construction always
        // shows the numbered Lv0 model. Must run BEFORE the rise snapshot.
        var variant = TheWaningBorder.Rendering.BuildingVariantVisual.TrySetup(go);
        // The spawn's footprint fit was measured with EVERY branch active (the
        // prefab authors them all on), so a construction site drew Lv0 at the
        // union's fit — the Fortress's Shelter at 0.43 instead of 0.95 — and
        // jumped to full size the moment it finished and refit. Measure the
        // branch that is actually shown.
        if (variant != null)
        {
            RefitVariantView(go, entity, _em);
            var refit = go.GetComponent<ProceduralScaleTag>();
            if (refit != null && refit.BaseScale > 0.001f)
                go.transform.localScale = Vector3.one * transform.Scale * refit.BaseScale;
            FitSelectionCollider(go, entity, _em);   // it was fitted to every branch
        }

        // Snapshot the visual hierarchy for the staggered construction rise.
        // Done last so any runtime-attached helpers are included in the pieces.
        var rise = go.GetComponent<TheWaningBorder.Rendering.BuildingRiseData>()
                ?? go.AddComponent<TheWaningBorder.Rendering.BuildingRiseData>();
        rise.Init();

        // Progressive battle-damage shader, driven from the entity's Health. It
        // stays dormant (original materials untouched) until the building first
        // takes damage. Guarded to true buildings so resource/veilstone nodes that
        // also route through here keep their own shaders.
        if (_em.HasComponent<BuildingTag>(entity))
        {
            var dmgVis = go.GetComponent<TheWaningBorder.Rendering.BuildingDamageVisual>()
                      ?? go.AddComponent<TheWaningBorder.Rendering.BuildingDamageVisual>();
            dmgVis.Entity = entity;
        }

        return go;
    }

    /// <summary>
    /// Attach the sink-depth tag (sized to actual renderer bounds) and the
    /// rise-animation snapshot to a procedural visual that doesn't pass
    /// through FinishProceduralBuilding — walls, towers, gates,
    /// border nodes. Idempotent: safe to call repeatedly.
    /// </summary>
    private static void AttachConstructionAnimation(GameObject go)
    {
        if (go == null) return;

        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);
            float visualHeight = bounds.size.y +
                Mathf.Max(0f, bounds.max.y - go.transform.position.y);
            if (visualHeight > 0f)
            {
                var sink = go.GetComponent<TheWaningBorder.Rendering.BuildingVisualSinkDepth>()
                        ?? go.AddComponent<TheWaningBorder.Rendering.BuildingVisualSinkDepth>();
                sink.Value = visualHeight + 0.5f;
            }
        }

        var rise = go.GetComponent<TheWaningBorder.Rendering.BuildingRiseData>()
                ?? go.AddComponent<TheWaningBorder.Rendering.BuildingRiseData>();
        rise.Init();
    }

    // EnsureBuildingUpgradeStateAtLevel1 removed (2026-08-16): stamping
    // BuildingUpgradeState + ApplyLevel from presentation code was a
    // determinism bug — see BuildingLevelOneSeedSystem for the sim-side
    // replacement and the full rationale.

    /// <summary>
    /// Finish setting up a procedurally generated unit: collider, EntityReference,
    /// transform sync, and faction coloring.
    /// </summary>
    private GameObject FinishProceduralUnit(GameObject go, Entity entity, int presentationId, LocalTransform transform)
    {
        go.name = $"Entity_{entity.Index}_{presentationId}";
        go.transform.rotation = transform.Rotation;

        // Apply ECS scale multiplied by procedural base scale
        var scaleTag = go.GetComponent<ProceduralScaleTag>();
        float baseScale = (scaleTag != null) ? scaleTag.BaseScale : 1f;
        go.transform.localScale = Vector3.one * transform.Scale * baseScale;

        // Selection collider fitted to the unit's visual bounds, floored by the
        // unit's own Radius so slim models stay comfortably clickable.
        FitSelectionCollider(go, entity, _em);

        // EntityReference for raycasting/selection
        var entityRef = go.GetComponent<EntityReference>();
        if (entityRef == null) entityRef = go.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Attach VeilstingerGunTracker for Veilstinger units
        if (presentationId == 321)
        {
            var gunTracker = go.AddComponent<VeilstingerGunTracker>();
            gunTracker.Entity = entity;
        }

        // Apply faction coloring (overwrites white/neutral parts with faction color)
        ApplyFactionColor(go, entity);

        return go;
    }

    // Faction banner pole/flag REMOVED (2026-07-15, user request): ownership
    // reads through team-color regions, selection rings, and the minimap.

    /// <summary>
    /// Reapply faction colors to all visuals belonging to a faction.
    /// Called after age-up to refresh culture colors on existing entities.
    /// </summary>
    public void RefreshFactionColors(Faction faction)
    {
        if (EntityViewManager.Instance == null) return;

        var entities = _presentationQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (!_em.HasComponent<FactionTag>(entity)) continue;
            if (_em.GetComponentData<FactionTag>(entity).Value != faction) continue;

            if (EntityViewManager.Instance.TryGetView(entity, out var go) && go != null)
            {
                ApplyFactionColor(go, entity);
            }
        }

        entities.Dispose();
    }

    // ── Deferred faction refresh (2026-10-10) ─────────────────────────
    //
    // AgeUpSystem runs inside the simulation tick; a visual rebuild there
    // stalls every lockstep peer for as long as it takes, whoever aged up.
    // It queues the faction instead and the rebuild runs here, one faction
    // per frame, on the presentation side.
    private static readonly List<Faction> _pendingRefreshes = new();

    /// <summary>
    /// Ask for <see cref="RefreshFactionVisuals"/> on the next presentation
    /// frame. Safe to call from the simulation: it touches no GameObject.
    /// </summary>
    public static void RequestFactionRefresh(Faction faction)
    {
        if (!_pendingRefreshes.Contains(faction)) _pendingRefreshes.Add(faction);
    }

    private void DrainFactionRefreshes()
    {
        if (_pendingRefreshes.Count == 0) return;
        var faction = _pendingRefreshes[0];
        _pendingRefreshes.RemoveAt(0);
        double t0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
        RefreshFactionVisuals(faction);
        TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("RefreshFactionVisuals",
            (UnityEngine.Time.realtimeSinceStartupAsDouble - t0) * 1000.0, faction.ToString());
    }

    /// <summary>
    /// After an age-up: refresh the faction's building colours and have the
    /// level swap re-resolve every building now. A culture's look is its SO
    /// model (Art_Direction.md § 13) — the level SO at level 1, a ladderless
    /// building's culturePrefabs entry, the Trading Outpost a converted
    /// Veilstone Mine became — and BuildingPrefabSwapSystem replaces any view
    /// whose SO answer changed, with the dissolve. Nothing is rebuilt here.
    /// From the simulation, use <see cref="RequestFactionRefresh"/>.
    /// </summary>
    public void RefreshFactionVisuals(Faction faction)
    {
        if (EntityViewManager.Instance == null) return;
        var entities = _presentationQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (!_em.HasComponent<FactionTag>(entity)) continue;
            if (_em.GetComponentData<FactionTag>(entity).Value != faction) continue;
            if (!_em.HasComponent<BuildingTag>(entity)) continue;
            if (EntityViewManager.Instance.TryGetView(entity, out var go) && go != null)
                ApplyFactionColor(go, entity);
        }
        entities.Dispose();
        TheWaningBorder.Rendering.BuildingPrefabSwapSystem.Instance?.RescanNow();
    }

    // ── SyncTransforms chunk skip (2026-09-25) ─────────────────────────
    //
    // The per-view fast path below already made a static view cheap, but
    // "cheap" was still a dictionary lookup, a ViewSync lookup, a
    // HasComponent and three compares for EVERY tree, rock, node and wall
    // module on the map, every frame. Now whole chunks whose LocalTransform
    // did not change since the last pass are skipped without touching a
    // single entity. Three things can still need a write on an unchanged
    // chunk, and each is covered:
    //   * a view (re)registered on an unchanged entity -> the registration
    //     journal (EntityViewManager.DrainRegistered) puts it in _activeViews;
    //   * an interpolation segment still playing out, or a building that
    //     just left construction -> kept in _activeViews until it settles;
    //   * the ground moving under everything (TerrainVersion), a journal
    //     overflow, or a lockstep/free-run switch -> one full sweep.
    // Under-construction chunks are always walked (their rise animates).
    private uint _lastSyncVersion;
    private int _lastSyncTerrainVersion = int.MinValue;
    private bool _lastSyncInterpolate;
    private bool _syncEverRan;
    private readonly HashSet<Entity> _activeViews = new();
    private readonly List<Entity> _activeScratch = new();

    private void SyncTransforms()
    {
        var evm = EntityViewManager.Instance;
        if (evm == null) return;

        // Under lockstep the simulation advances in fixed steps, so transforms
        // arrive in discrete jumps and views have to be interpolated between
        // them or the whole game reads as though it ran at the tick rate.
        bool interpolate = TheWaningBorder.Core.Multiplayer.LockstepTiming.FixedStepActive;
        float stepDuration = Mathf.Max(
            0.0001f, TheWaningBorder.Core.Multiplayer.LockstepTiming.TickDuration);

        _activeScratch.Clear();
        evm.DrainRegistered(_activeScratch, out bool overflow);
        for (int k = 0; k < _activeScratch.Count; k++) _activeViews.Add(_activeScratch[k]);

        bool full = !_syncEverRan || overflow
            || interpolate != _lastSyncInterpolate
            || _lastSyncTerrainVersion != TerrainUtility.TerrainVersion;
        uint lastVersion = _lastSyncVersion;
        _lastSyncVersion = _em.GlobalSystemVersion;
        _syncEverRan = true;
        _lastSyncInterpolate = interpolate;
        _lastSyncTerrainVersion = TerrainUtility.TerrainVersion;
        int frame = Time.frameCount;

        _presentationQuery.CompleteDependency();
        var entityHandle = _em.GetEntityTypeHandle();
        var ltHandle = _em.GetComponentTypeHandle<LocalTransform>(true);
        var ucHandle = _em.GetComponentTypeHandle<UnderConstruction>(true);

        var chunks = _presentationQuery.ToArchetypeChunkArray(Unity.Collections.Allocator.Temp);
        for (int c = 0; c < chunks.Length; c++)
        {
            var chunk = chunks[c];
            bool underConstruction = chunk.Has(ref ucHandle);
            if (!full && !underConstruction && !chunk.DidChange(ref ltHandle, lastVersion))
                continue;

            var entities = chunk.GetNativeArray(entityHandle);
            var transforms = chunk.GetNativeArray(ref ltHandle);
            for (int i = 0; i < entities.Length; i++)
                SyncView(evm, entities[i], transforms[i], underConstruction,
                    interpolate, stepDuration, frame);
        }
        chunks.Dispose();

        // Views whose chunk was skipped but which still owe a write.
        if (_activeViews.Count > 0)
        {
            _activeScratch.Clear();
            _activeScratch.AddRange(_activeViews);
            for (int k = 0; k < _activeScratch.Count; k++)
            {
                var e = _activeScratch[k];
                if (!_em.Exists(e) || !_em.HasComponent<PresentationId>(e)
                    || !_em.HasComponent<LocalTransform>(e))
                {
                    _activeViews.Remove(e);
                    continue;
                }
                SyncView(evm, e, _em.GetComponentData<LocalTransform>(e),
                    _em.HasComponent<UnderConstruction>(e), interpolate, stepDuration, frame);
            }
        }
    }

    /// <summary>Metres above the walking surface a simulated Y must be
    /// before the view asks whether the unit is on a wall-walk. Engine
    /// tolerance, not tuning: well over the millimetre gap between the sim's
    /// and the renderer's height samples, well under the wall's deck height.</summary>
    private const float WallWalkLiftThreshold = 0.75f;

    /// <summary>On the rampart nav layer, or mid-climb to / from it.</summary>
    private bool IsOnWallLayer(Entity entity)
    {
        if (_em.HasComponent<NavLayerIndex>(entity)
            && _em.GetComponentData<NavLayerIndex>(entity).Layer == NavLayerIndex.LayerRampart)
            return true;
        return _em.HasComponent<LayeredMoveOrder>(entity) || _em.HasComponent<LayerTraversalState>(entity);
    }

    private void SyncView(EntityViewManager evm, Entity entity, LocalTransform lt,
        bool underConstruction, bool interpolate, float stepDuration, int frame)
    {
        if (!evm.TryGetView(entity, out var go) || go == null)
        {
            _activeViews.Remove(entity);
            return;
        }

        // ── Per-view cache (see ViewSync) ────────────────────────
        if (!_viewSync.TryGetValue(entity, out var sync))
        {
            sync = new ViewSync();
            _viewSync[entity] = sync;
        }
        // Once per frame: an active view can be reached by both the chunk
        // walk and the active pass, and a second visit would advance its
        // interpolation twice.
        if (sync.LastFrame == frame) return;
        sync.LastFrame = frame;

        if (sync.View != go)
        {
            sync.View = go;
            sync.ScaleTag = go.GetComponent<ProceduralScaleTag>();
            sync.Rise = go.GetComponent<TheWaningBorder.Rendering.BuildingRiseData>();
            sync.Sink = go.GetComponent<TheWaningBorder.Rendering.BuildingVisualSinkDepth>();
            sync.KeepSimY = _em.HasComponent<EmplacedEngineTag>(entity);
            sync.BuildingLike = IsBuildingLike(entity);
            sync.Primed = false;   // new view: force a full write
        }

        // Static-entity fast path: nothing about this visual changed
        // since the last sync, so skip the terrain sample and the
        // three transform writes entirely. Under-construction visuals
        // never take it — their rise animation advances every frame.
        // Nor does a view still playing out an interpolation segment.
        if (sync.Primed && !underConstruction && !sync.LastUnderConstruction
            && sync.LastTerrainVersion == TerrainUtility.TerrainVersion
            && sync.LastPos.Equals(lt.Position)
            && sync.LastRot.value.Equals(lt.Rotation.value)
            && sync.LastScale == lt.Scale
            && (!interpolate || sync.SegmentT >= 1f))
        {
            _activeViews.Remove(entity);
            return;
        }

        var simPos = lt.Position;
        var simRot = sync.BuildingLike
            ? (Quaternion)lt.Rotation * BuildingVisualOffsetY180
            : (Quaternion)lt.Rotation;

        if (interpolate)
        {
            // A changed simulated transform opens a new segment, played
            // out over one tick's worth of real time. Starting it from
            // where the view actually IS (not from the previous
            // simulated value) keeps the motion continuous even when a
            // frame is long enough to span two ticks.
            if (!sync.Primed
                || !sync.LastPos.Equals(simPos)
                || !sync.LastRot.value.Equals(lt.Rotation.value))
            {
                bool teleport = !sync.Primed
                    || Unity.Mathematics.math.distancesq(sync.RenderPos, simPos) > TeleportSnapDistanceSq;

                sync.FromPos = teleport ? simPos : sync.RenderPos;
                sync.FromRot = teleport ? simRot : sync.RenderRot;
                sync.ToPos = simPos;
                sync.ToRot = simRot;
                sync.SegmentT = teleport ? 1f : 0f;
            }

            sync.SegmentT = Mathf.Min(1f, sync.SegmentT + Time.deltaTime / stepDuration);
            sync.RenderPos = Unity.Mathematics.math.lerp(sync.FromPos, sync.ToPos, sync.SegmentT);
            sync.RenderRot = Quaternion.Slerp(sync.FromRot, sync.ToRot, sync.SegmentT);
        }
        else
        {
            sync.RenderPos = simPos;
            sync.RenderRot = simRot;
            sync.SegmentT = 1f;
        }

        var pos = (Vector3)sync.RenderPos;
        // Nearest walking surface to the SIMULATED Y — keeps visuals
        // on the bridge deck when the entity is on a bridge, and on
        // the ground when it walks under one. Sampled at the RENDERED
        // x/z so a unit crossing a slope rides it smoothly rather than
        // in tick-sized steps.
        if (!sync.KeepSimY)
        {
            float surfaceY = TerrainUtility.GetSurfaceHeight(pos.x, pos.z, sync.RenderPos.y);
            // A unit on a stone WALL-WALK (or climbing to it) is simulated at
            // the deck, metres above the ground under it. Snapping it to the
            // ground drew it INSIDE the masonry — invisible and unclickable
            // (2026-10-20). Only a unit simulated clearly above the surface
            // pays for the layer lookup.
            if (sync.RenderPos.y - surfaceY > WallWalkLiftThreshold && IsOnWallLayer(entity))
                surfaceY = sync.RenderPos.y;
            pos.y = surfaceY;
        }

        // Construction rising animation: pieces start below ground and
        // rise into place bottom-to-top via BuildingRiseData. Falls back
        // to a rigid root sink for visuals that don't have rise data
        // attached (e.g. mid-construction prefab swap before re-init).
        if (underConstruction)
        {
            var uc = _em.GetComponentData<UnderConstruction>(entity);
            float ratio = uc.Total > 0 ? Mathf.Clamp01(uc.Progress / uc.Total) : 1f;
            // Cached on the view (was a GetComponent per frame per site).
            // A miss re-resolves: the tag can be added after registration.
            var sinkTag = sync.Sink != null ? sync.Sink
                : (sync.Sink = go.GetComponent<TheWaningBorder.Rendering.BuildingVisualSinkDepth>());
            float sinkDepth = (sinkTag != null && sinkTag.Value > 0f)
                ? sinkTag.Value
                : (_em.HasComponent<Radius>(entity)
                    ? _em.GetComponentData<Radius>(entity).Value * 2f
                    : 3f);

            var rise = sync.Rise;
            if (rise != null && rise.HasPieces)
                rise.ApplyRise(ratio, sinkDepth);
            else
                // Rigid root sink — also the path for single-mesh
                // prefabs whose only renderer sits on the root (no
                // child pieces to stagger).
                pos.y -= sinkDepth * (1f - ratio);
        }
        else
        {
            // Just exited UnderConstruction: snap pieces back to rest
            // once and fire the same flourish used for level-up swaps
            // so the transition reads clearly to the player.
            var rise = sync.Rise;
            if (rise != null && rise.NotifyConstructionComplete())
            {
                Color accent = new Color(1f, 0.85f, 0.45f);
                if (_em.HasComponent<FactionTag>(entity))
                {
                    var cFac = _em.GetComponentData<FactionTag>(entity).Value;
                    accent = FactionColors.Get(cFac);

                    // The finished building takes its level's SO model now
                    // (Art_Direction.md § 13): the swap re-resolves it at
                    // once, with the dissolve; this flourish is the cue.
                    TheWaningBorder.Rendering.BuildingPrefabSwapSystem.Instance?.RescanNow(entity, flourish: false);
                }
                TheWaningBorder.Rendering.BuildingLevelUpEffect.Spawn(go, accent);
            }
        }

        // Respect procedural unit base scale (ProceduralScaleTag)
        float baseScale = (sync.ScaleTag != null) ? sync.ScaleTag.BaseScale : 1f;
        float finalScale = lt.Scale * baseScale;

        // Pivot-offset compensation: shift the root so the SCALED
        // bounds centre — not the prefab pivot — sits on the entity
        // position. Rotates with the view. Zero for units and
        // procedural builders (see ProceduralScaleTag.BaseOffset).
        if (sync.ScaleTag != null && sync.ScaleTag.BaseOffset != Vector3.zero)
            pos -= (sync.RenderRot * sync.ScaleTag.BaseOffset) * finalScale;

        // Write only what changed (exact compares). A unit standing still
        // under lockstep, or a building mid-rise, computes the same root
        // pose frame after frame.
        if (!sync.Primed || !pos.Equals(sync.WrittenPos) || !sync.RenderRot.Equals(sync.WrittenRot))
        {
            go.transform.SetPositionAndRotation(pos, sync.RenderRot);
            sync.WrittenPos = pos;
            sync.WrittenRot = sync.RenderRot;
        }
        if (!sync.Primed || !finalScale.Equals(sync.WrittenScale))
        {
            go.transform.localScale = Vector3.one * finalScale;
            sync.WrittenScale = finalScale;
        }

        sync.LastPos = lt.Position;
        sync.LastRot = lt.Rotation;
        sync.LastScale = lt.Scale;
        sync.LastUnderConstruction = underConstruction;
        sync.LastTerrainVersion = TerrainUtility.TerrainVersion;
        sync.Primed = true;

        // Still owes a write next frame even if its chunk goes quiet.
        if ((interpolate && sync.SegmentT < 1f) || underConstruction)
            _activeViews.Add(entity);
        else
            _activeViews.Remove(entity);
    }

    private GameObject CreateFallbackPrefab(string name, PrimitiveType type, float scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(false); // Hide the template
        DontDestroyOnLoad(go);
        return go;
    }

    private Color GetFactionColor(Faction faction)
    {
        return faction switch
        {
            Faction.Blue => new Color(0.3f, 0.5f, 1f),
            Faction.Red => new Color(1f, 0.3f, 0.3f),
            Faction.Green => new Color(0.3f, 1f, 0.3f),
            Faction.Yellow => new Color(1f, 1f, 0.3f),
            Faction.Purple => new Color(0.8f, 0.3f, 1f),
            Faction.Orange => new Color(1f, 0.6f, 0.2f),
            Faction.Teal => new Color(0.2f, 0.8f, 0.8f),
            Faction.White => new Color(0.9f, 0.9f, 0.9f),
            Faction.Border => new Color(0.6f, 0.85f, 0.95f), // icy cyan — veilstone aesthetic
            _ => Color.gray
        };
    }


    /// <summary>
    /// Fit ONE root BoxCollider to the combined renderer bounds of the visual
    /// so the click target matches what the player actually sees. Every other
    /// collider under the root is removed — presentation colliders exist ONLY
    /// for selection raycasts, and prefab-authored ones are routinely wrong
    /// (oversized loot boxes block clicks; undersized unit boxes make units
    /// like the Veilstinger nearly unclickable). Bounds are stored in
    /// root-LOCAL space so later uniform scaling (ECS scale x
    /// ProceduralScaleTag) keeps collider and mesh matched. The min* floors
    /// (world units at fit time) keep slim visuals comfortably clickable.
    /// </summary>
    /// <summary>
    /// Entity-aware wrapper: fits to the renderer bounds as usual, but never
    /// smaller than the entity's SIMULATION footprint.
    ///
    /// Renderer bounds alone are not a trustworthy click target. They are
    /// measured at whatever scale the visual happens to hold at fit time, they
    /// exclude anything inactive, and for FBX-variant prefabs they can be a
    /// fraction of the space the building actually occupies — the Hall (a 4x4
    /// footprint in the sim) ended up with a box barely a metre across and was
    /// nearly unclickable. The sim already knows how big each entity is, via
    /// BuildingSize or Radius; that is the authority for how big its click
    /// target must be, so use it as a floor.
    /// </summary>
    internal static BoxCollider FitSelectionCollider(GameObject root, Entity entity, EntityManager em,
        float minWorldXZ = 0.9f, float minWorldY = 1.0f)
    {
        if (em.Exists(entity))
        {
            if (em.HasComponent<BuildingSize>(entity))
            {
                // Grid cells are 1 m. Floor both axes by the SHORT side so the
                // box stays correct whichever way the building is rotated.
                var bs = em.GetComponentData<BuildingSize>(entity);
                minWorldXZ = Mathf.Max(minWorldXZ, Mathf.Min(bs.Width, bs.Height));
                minWorldY = Mathf.Max(minWorldY, 2.0f); // buildings are never ankle-high
            }
            else if (em.HasComponent<Radius>(entity))
            {
                minWorldXZ = Mathf.Max(minWorldXZ, em.GetComponentData<Radius>(entity).Value * 2f);
            }
        }

        return FitSelectionCollider(root, minWorldXZ, minWorldY);
    }

    /// <summary>
    /// Replace every collider on a visual with ONE axis-aligned box that fills
    /// exactly the build cell the entity occupies — a 2 m cube standing on the
    /// ground, not a box shrink-wrapped to the art.
    ///
    /// Mesh-fitted boxes (<see cref="FitSelectionCollider"/>) are wrong for
    /// one-cell resource nodes: the gem-cluster art is a ragged clump that
    /// under-fills its cell in some directions and pokes past it in others, so
    /// the click target and the ground the node actually blocks disagree. A
    /// cell-sized cube makes selection, the impassable cell, and the sim
    /// <see cref="Radius"/> all describe the same square.
    ///
    /// <paramref name="worldScaleFactor"/> is the visual's eventual UNIFORM
    /// world scale (ECS <c>LocalTransform.Scale</c> × <see cref="ProceduralScaleTag.BaseScale"/>
    /// — see SyncTransforms). BoxCollider.size is local, so the world size is
    /// divided back out here; reading transform.lossyScale instead would be
    /// wrong, because SyncTransforms has not applied the scale yet at spawn.
    /// docs/Design/Build_Grid.md
    /// </summary>
    /// <summary>
    /// The uniform world scale a visual will end up at once SyncTransforms runs
    /// (entity <c>LocalTransform.Scale</c> × the prefab's
    /// <see cref="ProceduralScaleTag.BaseScale"/>). Feed this to
    /// <see cref="FitCellBoxCollider"/>: at spawn the transform has not been
    /// scaled yet, so reading lossyScale there gives the wrong answer.
    /// </summary>
    private float CellColliderScaleFor(Entity entity, float baseScale)
    {
        float ecsScale = 1f;
        if (_em.Exists(entity) && _em.HasComponent<Unity.Transforms.LocalTransform>(entity))
        {
            float s = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Scale;
            if (s > 1e-4f) ecsScale = s;
        }
        return ecsScale * baseScale;
    }

    internal static BoxCollider FitCellBoxCollider(GameObject root,
        float worldScaleFactor, float worldHeight = 2f, float cellsAcross = 1f)
    {
        BoxCollider col = null;
        var existing = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            var c = existing[i];
            if (col == null && c.gameObject == root && c is BoxCollider bc) { col = bc; continue; }
            Object.Destroy(c);
        }
        if (col == null) col = root.AddComponent<BoxCollider>();

        float s = Mathf.Abs(worldScaleFactor) > 1e-4f ? Mathf.Abs(worldScaleFactor) : 1f;
        float across = BuildGrid.CellSize * cellsAcross;
        col.size = new Vector3(across, worldHeight, across) / s;
        col.center = new Vector3(0f, worldHeight * 0.5f, 0f) / s;
        return col;
    }

    internal static BoxCollider FitSelectionCollider(GameObject root,
        float minWorldXZ = 0.9f, float minWorldY = 1.0f)
    {
        // Drop every existing collider; keep (reuse) a root BoxCollider if
        // one is already there. Destroyed colliders linger queryable until
        // end of frame — harmless, the fitted box takes over afterwards.
        BoxCollider col = null;
        var existing = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            var c = existing[i];
            if (col == null && c.gameObject == root && c is BoxCollider bc) { col = bc; continue; }
            Object.Destroy(c);
        }
        if (col == null) col = root.AddComponent<BoxCollider>();

        // Combined renderer bounds in root-local space (world AABB corners
        // inverse-transformed, so child rotations are handled). Particle /
        // trail / line renderers are skipped — their bounds balloon.
        //
        // ACTIVE renderers only: Renderer.bounds on a disabled object is not
        // maintained, and multi-variant building prefabs ship with every
        // culture/level branch deactivated. Folding their stale (often
        // origin-centred) bounds in either ballooned the box across the map or
        // dragged its centre off the model. Fall back to including inactive ones
        // only when nothing is active, which is better than no bounds at all.
        bool has = false;
        Vector3 min = Vector3.zero, max = Vector3.zero;
        var renderers = root.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            var b = r.bounds;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3(
                    (k & 1) == 0 ? b.min.x : b.max.x,
                    (k & 2) == 0 ? b.min.y : b.max.y,
                    (k & 4) == 0 ? b.min.z : b.max.z);
                var lp = root.transform.InverseTransformPoint(corner);
                if (!has) { min = lp; max = lp; has = true; }
                else { min = Vector3.Min(min, lp); max = Vector3.Max(max, lp); }
            }
        }

        Vector3 center, size;
        if (has)
        {
            center = (min + max) * 0.5f;
            size = max - min;
        }
        else
        {
            // No renderers (yet) — 1m box above the pivot as a stopgap.
            center = Vector3.up * 0.5f;
            size = Vector3.one;
        }

        // Minimum click size, converted from world to local units.
        var ls = root.transform.lossyScale;
        float sx = Mathf.Abs(ls.x) > 1e-4f ? Mathf.Abs(ls.x) : 1f;
        float sy = Mathf.Abs(ls.y) > 1e-4f ? Mathf.Abs(ls.y) : 1f;
        float sz = Mathf.Abs(ls.z) > 1e-4f ? Mathf.Abs(ls.z) : 1f;
        size.x = Mathf.Max(size.x, minWorldXZ / sx);
        size.y = Mathf.Max(size.y, minWorldY / sy);
        size.z = Mathf.Max(size.z, minWorldXZ / sz);

        col.center = center;
        col.size = size;
        return col;
    }




    void OnDestroy()
    {
        // Clean up fallback prefabs
        if (_fallbackUnitPrefab != null) Destroy(_fallbackUnitPrefab);
        if (_fallbackBuildingPrefab != null) Destroy(_fallbackBuildingPrefab);

        if (Instance == this) { Instance = null; _pendingRefreshes.Clear(); }
    }
}