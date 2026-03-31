// PresentationSpawnSystem.cs
// Spawns and syncs visual GameObjects for ECS entities
// Location: Assets/Scripts/Presentation/PresentationSpawnSystem.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Presentation;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Bootstrap;

public class PresentationSpawnSystem : MonoBehaviour
{
    public static PresentationSpawnSystem Instance { get; private set; }

    // Prefab mapping: PresentationId -> Prefab path in Resources
    // IDs must match the PresentationID constants in each entity factory class
    private static readonly Dictionary<int, string> PrefabPaths = new()
    {
        // Buildings - Era 1 Core
        { 100, "Prefabs/Buildings/Hall" },               // Hall.PresentationID = 100
        { 101, "Prefabs/Buildings/GatherersHut" },       // GatherersHut.PresentationID = 101
        { 102, "Prefabs/Buildings/Hut" },                // Hut.PresentationID = 102
        { 510, "Prefabs/Buildings/Barracks" },           // Barracks.PresentationID = 510

        // Buildings - Era 1 Advanced
        { 520, "Prefabs/Buildings/TempleOfRidan" },      // TempleOfRidan.PresentationID = 520
        { 530, "Prefabs/Buildings/VaultOfAlmierra" },    // VaultOfAlmierra.PresentationID = 530
        { 540, "Prefabs/Buildings/FiendstoneKeep" },     // FiendstoneKeep.PresentationID = 540

        // Units
        { 200, "Prefabs/Units/Builder" },                // Builder.PresentationID = 200
        { 201, "Prefabs/Units/Swordsman" },              // Swordsman.PresentationID = 201
        { 202, "Prefabs/Units/Archer" },                 // Archer.PresentationID = 202
        { 203, "Prefabs/Units/Miner" },                  // Miner.PresentationID = 203
        { 206, "Prefabs/Units/Scout" },                  // Scout.PresentationID = 206
        { 207, "Prefabs/Units/Litharch" },               // Litharch.PresentationID = 207

        // Creatures & Resources
        { 300, "Prefabs/Creatures/Creature" },            // Creature.PresentationID = 300
        { 301, "Prefabs/Resources/CrystalNode" },         // Cadaver/CrystalNode.PresentationID = 301

        // Nature Obstacles (procedurally generated)
        { 400, "Procedural/Forest" },                      // ForestCluster (generated at runtime)
        { 401, "Procedural/Rock" },                        // RockFormation (generated at runtime)

        // Resources (procedurally generated)
        { 402, "Procedural/IronDeposit" },                 // IronDeposit (generated at runtime)

        // Alanthor Walls (procedurally generated)
        { 550, "Procedural/WallHub" },                      // Alanthor Wall Hub (generated at runtime)
        { 551, "Procedural/WallSegment" },                   // Alanthor Wall Segment (generated at runtime)

        // Alanthor Buildings (procedurally generated)
        { 560, "Procedural/Smelter" },                       // Alanthor Smelter/Forge (generated at runtime)

        // Crystal Curse (procedurally generated)
        { 311, "Procedural/CursedGround" },                  // Cursed Ground tile (generated at runtime)

        // Crystal Nodes (procedurally generated)
        { 310, "Procedural/CrystalMainNode" },                // CrystalMainNode (generated at runtime)
        { 312, "Procedural/CrystalResourceNode" },            // CrystalResourceNode (generated at runtime)
        { 313, "Procedural/CrystalEnforcementNode" },         // CrystalEnforcementNode (generated at runtime)
        { 314, "Procedural/CrystalSuppressionNode" },         // CrystalSuppressionNode (generated at runtime)
        { 315, "Procedural/CrystalRestorationNode" },         // CrystalRestorationNode (generated at runtime)
        { 316, "Procedural/CrystalTurretNode" },              // CrystalTurretNode (generated at runtime)

        // Crystal Units (prefabs in Resources/Prefabs/Curse/Units/)
        { 320, "Prefabs/Curse/Units/Crystallings" },            // Crystalling unit prefab
        { 321, "Prefabs/Curse/Units/Veilstingers" },            // Veilstinger unit prefab
        { 322, "Prefabs/Curse/Units/Godsplinters" },            // Godsplinter unit prefab
    };

    /// <summary>Presentation ID for cursed ground tiles.</summary>
    private const int CursedGroundPresentationId = 311;

    // Fallback prefabs if specific one not found
    private GameObject _fallbackUnitPrefab;
    private GameObject _fallbackBuildingPrefab;

    // Track which entities already have visuals
    private HashSet<Entity> _spawnedEntities = new();

    // Cache
    private Unity.Entities.World _world;
    private EntityManager _em;
    private EntityQuery _presentationQuery;

    // Throttle SyncTransforms to ~15fps to reduce per-frame cost
    private float _syncTimer;

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
        }
    }

    void Update()
    {
        if (_world == null || !_world.IsCreated) return;

        CleanupDestroyedEntities();
        SpawnMissingVisuals();
        SyncTransforms();
    }

    /// <summary>
    /// Detects ECS entities that have been destroyed (e.g., by DeathSystem)
    /// and cleans up their corresponding GameObjects.
    /// </summary>
    private void CleanupDestroyedEntities()
    {
        if (EntityViewManager.Instance == null) return;

        // Collect stale entities (can't modify HashSet during iteration)
        var toRemove = new System.Collections.Generic.List<Entity>();

        foreach (var entity in _spawnedEntities)
        {
            if (!_em.Exists(entity))
            {
                toRemove.Add(entity);
            }
        }

        foreach (var entity in toRemove)
        {
            _spawnedEntities.Remove(entity);

            if (EntityViewManager.Instance.TryGetView(entity, out var go))
            {
                EntityViewManager.Instance.UnregisterView(entity);
                if (go != null) Destroy(go);
            }
        }
    }

    private void SpawnMissingVisuals()
    {
        if (_presentationQuery == null) return;

        var entities = _presentationQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var presentations = _presentationQuery.ToComponentDataArray<PresentationId>(Unity.Collections.Allocator.Temp);
        var transforms = _presentationQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];

            // Skip if already spawned
            if (_spawnedEntities.Contains(entity)) continue;

            // Skip if EntityViewManager already has it
            if (EntityViewManager.Instance != null &&
                EntityViewManager.Instance.TryGetView(entity, out _))
            {
                _spawnedEntities.Add(entity);
                continue;
            }

            var presentationId = presentations[i].Id;
            var transform = transforms[i];

            // Spawn the visual
            var go = SpawnVisual(entity, presentationId, transform);
            if (go != null)
            {
                // Register with EntityViewManager
                if (EntityViewManager.Instance != null)
                    EntityViewManager.Instance.RegisterView(entity, go);

                _spawnedEntities.Add(entity);

                Debug.Log($"[PresentationSpawnSystem] Spawned visual for entity {entity.Index} (PresentationId: {presentationId})");
            }
        }

        entities.Dispose();
        presentations.Dispose();
        transforms.Dispose();
    }

    private GameObject SpawnVisual(Entity entity, int presentationId, LocalTransform transform)
    {
        // Get position and adjust Y to terrain height using shared utility
        Vector3 pos = transform.Position;
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

        // === ALANTHOR WALLS: procedural hub (cylinder) and segment (elongated cube) ===
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.HubPresentationID)
        {
            var go = CreateProceduralWallHub(pos, entity);
            return go;
        }
        if (presentationId == TheWaningBorder.Entities.AlanthorWall.SegmentPresentationID)
        {
            var go = CreateProceduralWallSegment(pos, entity);
            return go;
        }

        // === ALANTHOR SMELTER: procedural forge building ===
        if (presentationId == TheWaningBorder.Entities.Smelter.PresentationID)
        {
            var go = CreateProceduralSmelter(pos, entity);
            return go;
        }

        // === CRYSTAL CURSE: paint terrain splatmap instead of spawning visible plane ===
        if (presentationId == CursedGroundPresentationId)
        {
            float radius = _em.HasComponent<Radius>(entity) ? _em.GetComponentData<Radius>(entity).Value : 2f;
            if (ProceduralTerrain.Instance != null)
            {
                ProceduralTerrain.Instance.PaintCursedGround(pos.x, pos.z, radius);
            }
            // Return a minimal hidden root so PresentationSpawnSystem tracks this entity
            // (needed for cleanup when entity is destroyed, e.g. crystal node killed)
            var go = new GameObject($"CursedGround_{entity.Index}");
            go.transform.position = pos;
            go.SetActive(false); // Invisible — terrain painting is the visual
            return go;
        }

        // === CRYSTAL LOOT PILE (cadaver): procedural crystal cluster on the ground ===
        if (presentationId == 301)
        {
            var go = CreateProceduralCadaverLoot(pos, entity);
            return go;
        }

        // === CRYSTAL NODES (buildings): procedural crystal-themed visuals ===
        // Crystal UNITS (320-322) use actual prefabs, so they fall through to prefab loading below
        if (presentationId >= 310 && presentationId <= 316 && presentationId != 311)
        {
            var go = CreateProceduralCrystalEntity(pos, presentationId, entity);
            return go;
        }

        GameObject prefab = null;

        // Try to load specific prefab
        if (PrefabPaths.TryGetValue(presentationId, out string path))
        {
            prefab = Resources.Load<GameObject>(path);
        }

        // Fallback based on ID range
        // Units: 200-299, Buildings: 100-199 and 500+
        if (prefab == null)
        {
            if (presentationId >= 200 && presentationId < 500)
                prefab = _fallbackUnitPrefab;
            else
                prefab = _fallbackBuildingPrefab;

            Debug.LogWarning($"[PresentationSpawnSystem] No prefab for PresentationId {presentationId}, using fallback");
        }

        if (prefab == null) return null;

        var goInst = Instantiate(prefab);
        goInst.SetActive(true); // Ensure active (fallback prefabs are stored inactive)
        goInst.name = $"Entity_{entity.Index}_{presentationId}";
        goInst.transform.position = pos;
        goInst.transform.rotation = transform.Rotation;
        goInst.transform.localScale = Vector3.one * transform.Scale;

        // Ensure a collider exists for raycasting/selection
        if (goInst.GetComponentInChildren<Collider>() == null)
        {
            var col = goInst.AddComponent<BoxCollider>();
            // Size collider based on entity radius
            float radius = 0.5f;
            if (_em.HasComponent<Radius>(entity))
                radius = _em.GetComponentData<Radius>(entity).Value;
            col.size = Vector3.one * radius * 2f;
        }

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

        // Apply faction coloring
        ApplyFactionColor(goInst, entity);

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
            // Buildings: color roof parts and stripe materials for faction ownership
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                // Tint roof objects with faction color (preserves texture detail)
                bool isRoof = renderer.gameObject.name.IndexOf("roof",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (var mat in renderer.materials)
                {
                    if (isRoof)
                    {
                        // Replace roof texture with white so faction color is solid
                        if (mat.HasProperty("_BaseMap"))
                            mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                        if (mat.HasProperty("_MainTex"))
                            mat.SetTexture("_MainTex", Texture2D.whiteTexture);

                        if (mat.HasProperty("_BaseColor"))
                            mat.SetColor("_BaseColor", color);
                        else if (mat.HasProperty("_Color"))
                            mat.color = color;
                    }

                    if (mat.HasProperty("_StripeColor"))
                        mat.SetColor("_StripeColor", color);
                }
            }
            AddFactionBanner(go, color);
        }
        else
        {
            // Units: apply faction color to all materials
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat.HasProperty("_Color"))
                        mat.color = color;
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", color);
                    if (mat.HasProperty("_StripeColor"))
                        mat.SetColor("_StripeColor", color);
                }
            }
        }
    }

    /// <summary>
    /// Adds a small colored banner on top of a building for faction ownership visibility.
    /// </summary>
    private void AddFactionBanner(GameObject building, Color color)
    {
        // Skip if banner already exists
        if (building.transform.Find("FactionBanner") != null) return;

        // Find the highest point of the building for banner placement
        float maxY = 0f;
        foreach (var renderer in building.GetComponentsInChildren<Renderer>())
        {
            float top = renderer.bounds.max.y - building.transform.position.y;
            if (top > maxY) maxY = top;
        }

        // Create banner pole + flag
        var banner = new GameObject("FactionBanner");
        banner.transform.SetParent(building.transform, false);
        banner.transform.localPosition = new Vector3(0f, maxY + 0.1f, 0f);

        // Pole
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(banner.transform, false);
        pole.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        pole.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
        Object.Destroy(pole.GetComponent<Collider>());
        pole.GetComponent<Renderer>().material.color = new Color(0.3f, 0.25f, 0.2f); // Dark wood

        // Flag
        var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flag.name = "Flag";
        flag.transform.SetParent(banner.transform, false);
        flag.transform.localPosition = new Vector3(0.2f, 0.85f, 0f);
        flag.transform.localScale = new Vector3(0.35f, 0.2f, 0.04f);
        Object.Destroy(flag.GetComponent<Collider>());
        var flagMat = flag.GetComponent<Renderer>().material;
        flagMat.color = color;
        if (flagMat.HasProperty("_BaseColor"))
            flagMat.SetColor("_BaseColor", color);
    }

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

    private void SyncTransforms()
    {
        if (EntityViewManager.Instance == null) return;

        // Throttle to ~15fps — visual position updates at this rate are smooth enough
        _syncTimer += Time.deltaTime;
        if (_syncTimer < 0.066f) return;
        _syncTimer = 0f;

        var entities = _presentationQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var transforms = _presentationQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            if (EntityViewManager.Instance.TryGetView(entities[i], out var go) && go != null)
            {
                var pos = (Vector3)transforms[i].Position;
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                go.transform.position = pos;
                go.transform.rotation = transforms[i].Rotation;
                go.transform.localScale = Vector3.one * transforms[i].Scale;
            }
        }

        entities.Dispose();
        transforms.Dispose();
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
            _ => Color.gray
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PROCEDURAL OBSTACLE GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a forest cluster: several procedural trees (trunk + canopy) scattered within radius.
    /// </summary>
    private GameObject CreateProceduralForest(Vector3 center, float radius, Entity entity)
    {
        var root = new GameObject($"Forest_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 12345);
        int treeCount = rng.Next(20, 31); // Dense forest: 20-30 trees

        // Colors
        var trunkBrown = new Color(0.35f, 0.22f, 0.10f);
        var canopyDarkGreen = new Color(0.15f, 0.35f, 0.10f);
        var canopyLightGreen = new Color(0.25f, 0.50f, 0.15f);

        // Ground foliage colors (fallen leaves / forest floor)
        var foliageDark = new Color(0.20f, 0.28f, 0.08f);
        var foliageLight = new Color(0.30f, 0.22f, 0.10f);

        // Shared materials
        var litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Forest floor: scattered ground cover patches (uses separate RNG to not desync tree positions)
        var groundRng = new System.Random(entity.Index + 99999);
        int patchCount = groundRng.Next(8, 15);
        for (int p = 0; p < patchCount; p++)
        {
            float pAngle = (float)(groundRng.NextDouble() * Mathf.PI * 2f);
            float pDist = (float)(groundRng.NextDouble() * radius * 0.75f);
            float px = Mathf.Cos(pAngle) * pDist;
            float pz = Mathf.Sin(pAngle) * pDist;
            float py = TerrainUtility.GetHeight(center.x + px, center.z + pz) - center.y;

            float patchSize = 1.5f + (float)groundRng.NextDouble() * 2.5f;
            float patchRot = (float)groundRng.NextDouble() * 360f;
            float colorT = (float)groundRng.NextDouble();

            var patch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            patch.name = $"GroundPatch_{p}";
            patch.transform.SetParent(root.transform, false);
            patch.transform.localPosition = new Vector3(px, py + 0.05f, pz);
            patch.transform.localRotation = Quaternion.Euler(90f, patchRot, 0f);
            patch.transform.localScale = new Vector3(patchSize, patchSize, 1f);

            var patchRenderer = patch.GetComponent<Renderer>();
            if (patchRenderer != null)
            {
                patchRenderer.material = new Material(litShader);
                patchRenderer.material.color = Color.Lerp(foliageDark, foliageLight, colorT);
                patchRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var patchCol = patch.GetComponent<Collider>();
            if (patchCol != null) Destroy(patchCol);
        }

        for (int i = 0; i < treeCount; i++)
        {
            // Random position tightly packed within radius
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(rng.NextDouble() * radius * 0.65f); // Tighter packing
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            float treeHeight = 2.5f + (float)rng.NextDouble() * 3f;
            float trunkRadius = 0.12f + (float)rng.NextDouble() * 0.08f;
            float canopyRadius = 0.7f + (float)rng.NextDouble() * 0.5f;

            // Get terrain height at tree position
            float treeY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            Vector3 treeBase = new Vector3(offsetX, treeY - center.y, offsetZ);

            // Trunk (cylinder)
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = $"Trunk_{i}";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = treeBase + Vector3.up * (treeHeight * 0.35f);
            trunk.transform.localScale = new Vector3(trunkRadius * 2f, treeHeight * 0.4f, trunkRadius * 2f);
            var trunkRenderer = trunk.GetComponent<Renderer>();
            if (trunkRenderer != null)
            {
                trunkRenderer.material = new Material(litShader);
                trunkRenderer.material.color = trunkBrown;
            }
            // Remove trunk collider (individual tree ECS entities handle collision)
            var trunkCol = trunk.GetComponent<Collider>();
            if (trunkCol != null) Destroy(trunkCol);

            // Canopy (sphere)
            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = $"Canopy_{i}";
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localPosition = treeBase + Vector3.up * (treeHeight * 0.65f);
            canopy.transform.localScale = Vector3.one * canopyRadius * 2f;
            var canopyRenderer = canopy.GetComponent<Renderer>();
            if (canopyRenderer != null)
            {
                canopyRenderer.material = new Material(litShader);
                float greenVariation = (float)rng.NextDouble();
                canopyRenderer.material.color = Color.Lerp(canopyDarkGreen, canopyLightGreen, greenVariation);
            }
            // Remove canopy collider
            var canopyCol = canopy.GetComponent<Collider>();
            if (canopyCol != null) Destroy(canopyCol);
        }

        // Add a single large collider for the whole forest (selection/raycasting)
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(radius * 2f, 6f, radius * 2f);
        boxCol.center = Vector3.up * 3f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Create a rock formation: several randomly rotated boulders scattered within radius.
    /// </summary>
    private GameObject CreateProceduralRockFormation(Vector3 center, float radius, Entity entity)
    {
        var root = new GameObject($"Rocks_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 67890);
        int rockCount = rng.Next(3, 6);

        // Colors
        var darkGrey = new Color(0.30f, 0.28f, 0.26f);
        var lightGrey = new Color(0.50f, 0.48f, 0.44f);
        var warmGrey = new Color(0.42f, 0.38f, 0.34f);

        for (int i = 0; i < rockCount; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(rng.NextDouble() * radius * 0.7f);
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            float rockSize = 1f + (float)rng.NextDouble() * 1.5f;

            // Get terrain height at rock position
            float rockY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            Vector3 rockBase = new Vector3(offsetX, rockY - center.y, offsetZ);

            // Boulder (stretched cube for angular look)
            var boulder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boulder.name = $"Boulder_{i}";
            boulder.transform.SetParent(root.transform, false);
            boulder.transform.localPosition = rockBase + Vector3.up * (rockSize * 0.3f);

            // Random squash/stretch for natural boulder shapes
            float sx = rockSize * (0.6f + (float)rng.NextDouble() * 0.8f);
            float sy = rockSize * (0.4f + (float)rng.NextDouble() * 0.6f);
            float sz = rockSize * (0.6f + (float)rng.NextDouble() * 0.8f);
            boulder.transform.localScale = new Vector3(sx, sy, sz);

            // Random rotation
            boulder.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 20f - 10f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 15f - 7.5f
            );

            var boulderRenderer = boulder.GetComponent<Renderer>();
            if (boulderRenderer != null)
            {
                boulderRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                float greyVariation = (float)rng.NextDouble();
                Color baseColor = Color.Lerp(darkGrey, lightGrey, greyVariation);
                baseColor = Color.Lerp(baseColor, warmGrey, (float)rng.NextDouble() * 0.3f);
                boulderRenderer.material.color = baseColor;
            }

            // Remove individual boulder colliders
            var boulderCol = boulder.GetComponent<Collider>();
            if (boulderCol != null) Destroy(boulderCol);
        }

        // Add a single collider for the whole formation
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(radius * 2f, 4f, radius * 2f);
        boxCol.center = Vector3.up * 2f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Create an iron deposit: a cluster of dark metallic rocks with reddish-brown iron ore veins.
    /// </summary>
    private GameObject CreateProceduralIronDeposit(Vector3 center, Entity entity)
    {
        var root = new GameObject($"IronDeposit_{entity.Index}");
        root.transform.position = center;

        var rng = new System.Random(entity.Index + 54321);
        int rockCount = rng.Next(3, 6);

        // Iron ore colors — dark grey with rusty orange veins
        var ironDark = new Color(0.25f, 0.22f, 0.20f);
        var ironRusty = new Color(0.55f, 0.30f, 0.15f);
        var ironLight = new Color(0.40f, 0.35f, 0.30f);

        for (int i = 0; i < rockCount; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float dist = (float)(rng.NextDouble() * 1.2f);
            float offsetX = Mathf.Cos(angle) * dist;
            float offsetZ = Mathf.Sin(angle) * dist;

            float rockSize = 0.6f + (float)rng.NextDouble() * 1.0f;

            float rockY = TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ);
            Vector3 rockBase = new Vector3(offsetX, rockY - center.y, offsetZ);

            // Iron rock (sphere for smoother ore look)
            var ore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ore.name = $"IronOre_{i}";
            ore.transform.SetParent(root.transform, false);
            ore.transform.localPosition = rockBase + Vector3.up * (rockSize * 0.25f);

            // Squash slightly for natural boulder shape
            float sx = rockSize * (0.7f + (float)rng.NextDouble() * 0.6f);
            float sy = rockSize * (0.5f + (float)rng.NextDouble() * 0.4f);
            float sz = rockSize * (0.7f + (float)rng.NextDouble() * 0.6f);
            ore.transform.localScale = new Vector3(sx, sy, sz);

            ore.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 15f - 7.5f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 10f - 5f
            );

            var oreRenderer = ore.GetComponent<Renderer>();
            if (oreRenderer != null)
            {
                oreRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                float variation = (float)rng.NextDouble();
                Color baseColor = Color.Lerp(ironDark, ironLight, variation * 0.5f);
                // Mix in rusty tint for ore vein appearance
                baseColor = Color.Lerp(baseColor, ironRusty, (float)rng.NextDouble() * 0.45f);
                oreRenderer.material.color = baseColor;
                // Slight metallic sheen
                if (oreRenderer.material.HasProperty("_Metallic"))
                    oreRenderer.material.SetFloat("_Metallic", 0.4f);
                if (oreRenderer.material.HasProperty("_Smoothness"))
                    oreRenderer.material.SetFloat("_Smoothness", 0.3f);
            }

            // Remove individual colliders
            var oreCol = ore.GetComponent<Collider>();
            if (oreCol != null) Destroy(oreCol);
        }

        // Add a single collider for the deposit
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(3f, 2f, 3f);
        boxCol.center = Vector3.up * 1f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ALANTHOR WALL PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a wall hub: a cylinder tower representing a wall connection point.
    /// </summary>
    private GameObject CreateProceduralWallHub(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallHub_{entity.Index}");
        root.transform.position = center;

        // Main cylinder tower
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "HubCylinder";
        cylinder.transform.SetParent(root.transform, false);
        cylinder.transform.localPosition = Vector3.up * 1.5f;
        cylinder.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f); // Diameter 1.2, height 3 (cylinder is 2 tall * 1.5 scale)

        var renderer = cylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            renderer.material.color = new Color(0.55f, 0.50f, 0.42f); // Stone grey-brown
        }

        // Remove individual collider
        var col = cylinder.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Single collider on root
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.5f, 3f, 1.5f);
        boxCol.center = Vector3.up * 1.5f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Create a wall segment: an elongated tall cube connecting two hubs.
    /// Reads WallConnection to determine length and orientation.
    /// </summary>
    private GameObject CreateProceduralWallSegment(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallSegment_{entity.Index}");
        root.transform.position = center;

        // Calculate segment length from WallConnection
        float length = 5f; // default fallback
        if (_em.HasComponent<WallConnection>(entity))
        {
            var conn = _em.GetComponentData<WallConnection>(entity);
            if (_em.Exists(conn.HubA) && _em.Exists(conn.HubB) &&
                _em.HasComponent<Unity.Transforms.LocalTransform>(conn.HubA) &&
                _em.HasComponent<Unity.Transforms.LocalTransform>(conn.HubB))
            {
                var posA = _em.GetComponentData<Unity.Transforms.LocalTransform>(conn.HubA).Position;
                var posB = _em.GetComponentData<Unity.Transforms.LocalTransform>(conn.HubB).Position;
                length = Unity.Mathematics.math.distance(
                    new Unity.Mathematics.float2(posA.x, posA.z),
                    new Unity.Mathematics.float2(posB.x, posB.z));
            }
        }

        // Wall cube: thin, tall, stretched along local Z
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallCube";
        wall.transform.SetParent(root.transform, false);
        wall.transform.localPosition = Vector3.up * 1.5f;
        wall.transform.localScale = new Vector3(0.6f, 3f, length); // Thin, 3 units tall, spans the distance

        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            renderer.material.color = new Color(0.50f, 0.45f, 0.38f); // Slightly darker stone
        }

        // Remove individual collider
        var col = wall.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Single collider on root
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(0.8f, 3f, length);
        boxCol.center = Vector3.up * 1.5f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Apply rotation from ECS entity (segment is rotated to face hub A → hub B)
        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
        {
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        }

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ALANTHOR SMELTER (FORGE) PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a smelter/forge: a dark metallic cube base with a chimney (cylinder) on top.
    /// Uses warm orange-red tones to suggest heat/metalworking.
    /// </summary>
    private GameObject CreateProceduralSmelter(Vector3 center, Entity entity)
    {
        var root = new GameObject($"Smelter_{entity.Index}");
        root.transform.position = center;

        // Colors
        var darkMetal = new Color(0.22f, 0.20f, 0.18f);
        var warmStone = new Color(0.40f, 0.30f, 0.22f);
        var chimneyGrey = new Color(0.30f, 0.28f, 0.26f);
        var embers = new Color(0.8f, 0.3f, 0.1f);

        // Main building body (cube)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "SmelterBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up * 1.25f;
        body.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);

        var bodyRenderer = body.GetComponent<Renderer>();
        if (bodyRenderer != null)
        {
            bodyRenderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            bodyRenderer.material.color = warmStone;
            if (bodyRenderer.material.HasProperty("_Metallic"))
                bodyRenderer.material.SetFloat("_Metallic", 0.3f);
        }
        var bodyCol = body.GetComponent<Collider>();
        if (bodyCol != null) Destroy(bodyCol);

        // Chimney (tall cylinder)
        var chimney = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        chimney.name = "Chimney";
        chimney.transform.SetParent(root.transform, false);
        chimney.transform.localPosition = new Vector3(0.6f, 3.5f, 0.6f);
        chimney.transform.localScale = new Vector3(0.6f, 1.5f, 0.6f);

        var chimneyRenderer = chimney.GetComponent<Renderer>();
        if (chimneyRenderer != null)
        {
            chimneyRenderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            chimneyRenderer.material.color = chimneyGrey;
            if (chimneyRenderer.material.HasProperty("_Metallic"))
                chimneyRenderer.material.SetFloat("_Metallic", 0.5f);
        }
        var chimneyCol = chimney.GetComponent<Collider>();
        if (chimneyCol != null) Destroy(chimneyCol);

        // Forge opening (small glowing cube at front)
        var forgeOpening = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forgeOpening.name = "ForgeOpening";
        forgeOpening.transform.SetParent(root.transform, false);
        forgeOpening.transform.localPosition = new Vector3(0f, 0.5f, 1.3f);
        forgeOpening.transform.localScale = new Vector3(0.8f, 0.7f, 0.2f);

        var openingRenderer = forgeOpening.GetComponent<Renderer>();
        if (openingRenderer != null)
        {
            openingRenderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            openingRenderer.material.color = embers;
            if (openingRenderer.material.HasProperty("_EmissionColor"))
            {
                openingRenderer.material.EnableKeyword("_EMISSION");
                openingRenderer.material.SetColor("_EmissionColor", embers * 0.5f);
            }
        }
        var openingCol = forgeOpening.GetComponent<Collider>();
        if (openingCol != null) Destroy(openingCol);

        // Anvil (small dark cube in front)
        var anvil = GameObject.CreatePrimitive(PrimitiveType.Cube);
        anvil.name = "Anvil";
        anvil.transform.SetParent(root.transform, false);
        anvil.transform.localPosition = new Vector3(0f, 0.3f, 2.0f);
        anvil.transform.localScale = new Vector3(0.5f, 0.6f, 0.4f);

        var anvilRenderer = anvil.GetComponent<Renderer>();
        if (anvilRenderer != null)
        {
            anvilRenderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            anvilRenderer.material.color = darkMetal;
            if (anvilRenderer.material.HasProperty("_Metallic"))
                anvilRenderer.material.SetFloat("_Metallic", 0.7f);
            if (anvilRenderer.material.HasProperty("_Smoothness"))
                anvilRenderer.material.SetFloat("_Smoothness", 0.4f);
        }
        var anvilCol = anvil.GetComponent<Collider>();
        if (anvilCol != null) Destroy(anvilCol);

        // Single collider for entire building
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(3f, 5f, 3f);
        boxCol.center = Vector3.up * 2f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRYSTAL CURSE PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a cursed ground tile: a flat dark purple disc on the terrain
    /// with a subtle purple emission glow to mark crystal corruption.
    /// </summary>
    private GameObject CreateProceduralCursedGround(Vector3 center, float radius, Entity entity)
    {
        var root = new GameObject($"CursedGround_{entity.Index}");
        root.transform.position = center;

        // Dark purple corruption colors
        var corruptionDark = new Color(0.15f, 0.05f, 0.20f, 0.85f);
        var corruptionGlow = new Color(0.40f, 0.10f, 0.55f);

        // Main disc (flattened cylinder for a ground patch look)
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "CurseDisc";
        disc.transform.SetParent(root.transform, false);
        disc.transform.localPosition = Vector3.up * 0.02f; // Barely above ground
        disc.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);

        var discRenderer = disc.GetComponent<Renderer>();
        if (discRenderer != null)
        {
            var mat = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = corruptionDark;

            // Enable transparency for semi-transparent ground overlay
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // 1 = Transparent in URP
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
            }

            // Purple emission glow
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", corruptionGlow * 0.3f);
            }

            discRenderer.material = mat;
        }

        // Remove collider -- cursed ground should not block movement or selection
        var discCol = disc.GetComponent<Collider>();
        if (discCol != null) Destroy(discCol);

        // No EntityReference or collider needed -- cursed ground is non-selectable
        // The ECS entity handles all gameplay logic (DPS, ownership, etc.)

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CRYSTAL ENTITY PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a procedural crystal loot pile visual for cadaver/death-drop entities.
    /// Small cluster of glowing purple crystal shards on the ground, mineable by workers.
    /// </summary>
    private GameObject CreateProceduralCadaverLoot(Vector3 center, Entity entity)
    {
        var root = new GameObject($"CrystalLoot_{entity.Index}");
        root.transform.position = center;

        var purpleBase = new Color(0.40f, 0.08f, 0.55f, 0.70f);
        var greenTip   = new Color(0.06f, 0.28f, 0.10f, 0.55f);
        var emPurple   = new Color(0.35f, 0.05f, 0.50f);

        // Small cluster of 3-5 bulbous crystal nubs (spheres)
        int shardCount = Random.Range(3, 6);
        for (int i = 0; i < shardCount; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shard.name = $"Shard_{i}";
            shard.transform.SetParent(root.transform);

            float angle = (i / (float)shardCount) * 360f + Random.Range(-20f, 20f);
            float dist = Random.Range(0.05f, 0.3f);
            float x = Mathf.Cos(angle * Mathf.Deg2Rad) * dist;
            float z = Mathf.Sin(angle * Mathf.Deg2Rad) * dist;

            // Bulbous eroded shape — short and wide spheres
            float height = Random.Range(0.25f, 0.55f);
            float width = Random.Range(0.12f, 0.22f);
            shard.transform.localPosition = new Vector3(x, height * 0.4f, z);
            shard.transform.localScale = new Vector3(width, height, width);

            float tiltAngle = Random.Range(5f, 25f);
            shard.transform.localRotation = Quaternion.Euler(
                Random.Range(-tiltAngle, tiltAngle),
                angle + Random.Range(-30f, 30f),
                Random.Range(-tiltAngle, tiltAngle));

            DestroyCollider(shard);

            float tipT = Random.Range(0.1f, 0.5f);
            ApplyCrystalMaterial(shard, purpleBase, greenTip, emPurple, tipBlend: tipT);
        }

        // Tiny point light for the loot pile
        var lightObj = new GameObject("LootGlow");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = Vector3.up * 0.25f;
        var pl = lightObj.AddComponent<Light>();
        pl.type = LightType.Point;
        pl.color = Color.white;
        pl.intensity = 0.6f;
        pl.range = 1.2f;
        pl.shadows = LightShadows.None;

        // Add a box collider to the root for selection/raycasting
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(0.8f, 0.6f, 0.8f);
        boxCol.center = Vector3.up * 0.3f;

        // Add EntityReference for raycasting/selection
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Create a procedural visual for crystal nodes (buildings) and crystal units.
    /// Uses crystalline shapes with purple/violet tones and emission glow.
    /// </summary>
    private GameObject CreateProceduralCrystalEntity(Vector3 center, int presentationId, Entity entity)
    {
        bool isUnit = presentationId >= 320; // 320+ are units, 310-316 are nodes/buildings
        string label = presentationId switch
        {
            310 => "CrystalMainNode",
            312 => "ResourceNode",
            313 => "EnforcementNode",
            314 => "SuppressionNode",
            315 => "RestorationNode",
            316 => "TurretNode",
            320 => "Crystalling",
            321 => "Veilstinger",
            322 => "Godsplinter",
            _ => "CrystalEntity"
        };

        var root = new GameObject($"{label}_{entity.Index}");
        root.transform.position = center;

        // Crystal color palette
        var crystalCore = GetCrystalColor(presentationId);
        var crystalGlow = crystalCore * 1.4f;
        crystalGlow.a = 1f;

        if (isUnit)
        {
            CreateCrystalUnitVisual(root, presentationId, crystalCore, crystalGlow);
        }
        else
        {
            CreateCrystalNodeVisual(root, presentationId, crystalCore, crystalGlow, entity);
        }

        // Add collider
        var boxCol = root.AddComponent<BoxCollider>();
        if (isUnit)
        {
            float unitHeight = presentationId == 322 ? 2.5f : 1.5f; // Godsplinter is taller
            boxCol.size = new Vector3(1f, unitHeight, 1f);
            boxCol.center = Vector3.up * (unitHeight * 0.5f);
        }
        else
        {
            float nodeSize = presentationId == 310 ? 3f : 2f; // Main node is larger
            boxCol.size = new Vector3(nodeSize, nodeSize * 1.5f, nodeSize);
            boxCol.center = Vector3.up * (nodeSize * 0.75f);
        }

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Get the base crystal color for a given presentation ID.
    /// Different sub-node types use different color accents.
    /// </summary>
    private static Color GetCrystalColor(int presentationId)
    {
        return presentationId switch
        {
            310 => new Color(0.55f, 0.15f, 0.70f),  // Main node: deep purple
            312 => new Color(0.30f, 0.60f, 0.70f),  // Resource: teal-crystal
            313 => new Color(0.70f, 0.20f, 0.20f),  // Enforcement: crimson
            314 => new Color(0.50f, 0.10f, 0.55f),  // Suppression: dark violet
            315 => new Color(0.20f, 0.70f, 0.40f),  // Restoration: emerald
            316 => new Color(0.70f, 0.50f, 0.15f),  // Turret: amber
            320 => new Color(0.45f, 0.20f, 0.60f),  // Crystalling: purple
            321 => new Color(0.35f, 0.15f, 0.55f),  // Veilstinger: dark purple
            322 => new Color(0.60f, 0.25f, 0.65f),  // Godsplinter: bright violet
            _ => new Color(0.50f, 0.15f, 0.60f)     // Default crystal purple
        };
    }

    /// <summary>
    /// Create a crystal node/building visual: large bulbous, eroded crystals with purple base
    /// fading to dark green tips.  Translucent, reflective material with dim purple emission
    /// and a white point light at the core.
    /// </summary>
    private void CreateCrystalNodeVisual(GameObject root, int presentationId, Color coreColor, Color glowColor, Entity entity)
    {
        float scale = presentationId == 310 ? 1.5f : 1f; // Main node is 50% larger

        // --- Colour palette ---
        var purpleBase  = new Color(0.40f, 0.08f, 0.55f, 0.70f); // translucent deep purple
        var greenTip    = new Color(0.06f, 0.28f, 0.10f, 0.55f); // translucent dark green
        var emissionPurple = new Color(0.35f, 0.05f, 0.50f);     // dim purple glow

        var rng = new System.Random(entity.Index + presentationId);

        // --- Central dominant crystal (tall bulbous sphere, stretched vertically) ---
        var mainCrystal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mainCrystal.name = "MainCrystal";
        mainCrystal.transform.SetParent(root.transform, false);
        float mainHeight = 2.8f * scale;
        mainCrystal.transform.localPosition = Vector3.up * (mainHeight * 0.45f);
        mainCrystal.transform.localScale = new Vector3(1.1f * scale, mainHeight, 0.95f * scale);
        mainCrystal.transform.localRotation = Quaternion.Euler(
            (float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 360f,
            (float)rng.NextDouble() * 6f - 3f);
        ApplyCrystalMaterial(mainCrystal, purpleBase, greenTip, emissionPurple, tipBlend: 0.65f);
        DestroyCollider(mainCrystal);

        // --- Secondary bulbous crystals (leaning outward) ---
        int secondaryCount = presentationId == 310 ? 4 : 2;
        for (int i = 0; i < secondaryCount; i++)
        {
            float angle = (i / (float)secondaryCount) * 360f + (float)rng.NextDouble() * 40f;
            float dist  = (0.55f + (float)rng.NextDouble() * 0.35f) * scale;
            float h     = 1.4f + (float)rng.NextDouble() * 1.0f;

            var crystal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crystal.name = $"Crystal_{i}";
            crystal.transform.SetParent(root.transform, false);
            float px = Mathf.Cos(angle * Mathf.Deg2Rad) * dist;
            float pz = Mathf.Sin(angle * Mathf.Deg2Rad) * dist;
            crystal.transform.localPosition = new Vector3(px, h * 0.4f * scale, pz);
            crystal.transform.localScale = new Vector3(
                (0.55f + (float)rng.NextDouble() * 0.25f) * scale,
                h * scale,
                (0.50f + (float)rng.NextDouble() * 0.20f) * scale);
            // Lean outward from center for organic feel
            float lean = 10f + (float)rng.NextDouble() * 20f;
            crystal.transform.localRotation = Quaternion.Euler(
                Mathf.Cos(angle * Mathf.Deg2Rad) * lean,
                angle + (float)rng.NextDouble() * 30f,
                Mathf.Sin(angle * Mathf.Deg2Rad) * lean);

            float tipT = 0.4f + (float)rng.NextDouble() * 0.4f;
            ApplyCrystalMaterial(crystal, purpleBase, greenTip, emissionPurple, tipBlend: tipT);
            DestroyCollider(crystal);
        }

        // --- Small eroded nub clusters at the base ---
        int nubCount = presentationId == 310 ? 6 : 3;
        for (int i = 0; i < nubCount; i++)
        {
            float angle = (float)rng.NextDouble() * 360f;
            float dist  = (0.3f + (float)rng.NextDouble() * 0.8f) * scale;
            float nubH  = 0.25f + (float)rng.NextDouble() * 0.45f;

            var nub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nub.name = $"Nub_{i}";
            nub.transform.SetParent(root.transform, false);
            float px = Mathf.Cos(angle * Mathf.Deg2Rad) * dist;
            float pz = Mathf.Sin(angle * Mathf.Deg2Rad) * dist;
            nub.transform.localPosition = new Vector3(px, nubH * 0.35f * scale, pz);
            nub.transform.localScale = new Vector3(
                (0.25f + (float)rng.NextDouble() * 0.20f) * scale,
                nubH * scale,
                (0.25f + (float)rng.NextDouble() * 0.20f) * scale);
            nub.transform.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 30f - 15f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 30f - 15f);

            // Nubs are more purple (base region), less green blend
            ApplyCrystalMaterial(nub, purpleBase, greenTip, emissionPurple, tipBlend: 0.15f);
            DestroyCollider(nub);
        }

        // --- Ground stain / base disc ---
        var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        basePlate.name = "BasePlate";
        basePlate.transform.SetParent(root.transform, false);
        basePlate.transform.localPosition = Vector3.up * 0.04f;
        basePlate.transform.localScale = new Vector3(1.8f * scale, 0.04f, 1.8f * scale);
        var darkBase = purpleBase * 0.3f;
        darkBase.a = 0.85f;
        ApplyCrystalMaterial(basePlate, darkBase, darkBase, emissionPurple * 0.15f, tipBlend: 0f);
        DestroyCollider(basePlate);

        // --- White point light inside the crystal cluster ---
        var lightObj = new GameObject("CrystalCoreLight");
        lightObj.transform.SetParent(root.transform, false);
        lightObj.transform.localPosition = Vector3.up * (1.0f * scale);
        var pointLight = lightObj.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.color = Color.white;
        pointLight.intensity = presentationId == 310 ? 1.8f : 1.2f;
        pointLight.range = 3.5f * scale;
        pointLight.shadows = LightShadows.None;
    }

    /// <summary>Helper to strip colliders off procedural primitives.</summary>
    private void DestroyCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    /// <summary>
    /// Create a crystal unit visual: a crystalline humanoid form using primitives.
    /// </summary>
    private void CreateCrystalUnitVisual(GameObject root, int presentationId, Color coreColor, Color glowColor)
    {
        float scale = presentationId switch
        {
            320 => 0.6f,  // Crystalling: small
            321 => 0.8f,  // Veilstinger: medium
            322 => 1.2f,  // Godsplinter: large siege
            _ => 0.8f
        };

        // Body (elongated sphere)
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up * (0.8f * scale);
        body.transform.localScale = new Vector3(0.5f * scale, 0.8f * scale, 0.4f * scale);
        var emPurple = new Color(0.35f, 0.05f, 0.50f);
        var greenTip = new Color(0.06f, 0.28f, 0.10f, 0.55f);
        // Units use a slightly more opaque base
        Color unitBase = coreColor;
        unitBase.a = 0.75f;
        ApplyCrystalMaterial(body, unitBase, greenTip, emPurple, tipBlend: 0.3f);
        DestroyCollider(body);

        // Head crystal (small cube tilted)
        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = Vector3.up * (1.4f * scale);
        head.transform.localScale = Vector3.one * (0.25f * scale);
        head.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        Color brightCore = coreColor * 1.3f;
        brightCore.a = 0.80f;
        ApplyCrystalMaterial(head, brightCore, greenTip, emPurple, tipBlend: 0.55f);
        DestroyCollider(head);

        // Godsplinter gets extra siege appendages
        if (presentationId == 322)
        {
            var cannon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cannon.name = "SiegeCannon";
            cannon.transform.SetParent(root.transform, false);
            cannon.transform.localPosition = new Vector3(0f, 1.2f * scale, 0.5f * scale);
            cannon.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cannon.transform.localScale = new Vector3(0.15f * scale, 0.6f * scale, 0.15f * scale);
            Color cannonBase = coreColor * 0.8f;
            cannonBase.a = 0.70f;
            ApplyCrystalMaterial(cannon, cannonBase, greenTip, emPurple, tipBlend: 0.4f);
            DestroyCollider(cannon);
        }

        // Veilstinger gets wing-like crystal shards
        if (presentationId == 321)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wing.name = side < 0 ? "LeftWing" : "RightWing";
                wing.transform.SetParent(root.transform, false);
                wing.transform.localPosition = new Vector3(0.4f * scale * side, 1f * scale, -0.1f * scale);
                wing.transform.localScale = new Vector3(0.4f * scale, 0.15f * scale, 0.3f * scale);
                wing.transform.localRotation = Quaternion.Euler(0f, 0f, -20f * side);
                Color wingBase = coreColor * 0.9f;
                wingBase.a = 0.65f;
                ApplyCrystalMaterial(wing, wingBase, greenTip, emPurple, tipBlend: 0.5f);
                DestroyCollider(wing);
            }
        }
    }

    /// <summary>
    /// Apply a translucent, reflective crystalline material with purple-to-green gradient
    /// and dim purple emission.
    /// <paramref name="tipBlend"/> controls how much green-tip color is mixed in (0 = pure base, 1 = pure tip).
    /// </summary>
    private static void ApplyCrystalMaterial(GameObject go, Color baseColor, Color tipColor,
        Color emissionColor, float tipBlend)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);

        // Blend base purple → dark green tip
        Color blended = Color.Lerp(baseColor, tipColor, tipBlend);

        // --- URP Transparent surface type ---
        // _Surface: 0 = Opaque, 1 = Transparent
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        // _Blend: 0 = Alpha
        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 0f);
        // Render queue for transparent
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");

        // Source / destination blend for alpha transparency
        if (mat.HasProperty("_SrcBlend"))
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlend"))
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);

        // Base color with alpha for translucency
        mat.color = blended;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", blended);

        // Highly reflective and refractant surface
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0.85f);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.95f);

        // IOR-like refraction boost (URP Lit doesn't have true IOR, but high smoothness +
        // specular highlights on transparent surface gives a convincing glass/crystal look)
        if (mat.HasProperty("_SpecularHighlights"))
            mat.SetFloat("_SpecularHighlights", 1f);
        if (mat.HasProperty("_EnvironmentReflections"))
            mat.SetFloat("_EnvironmentReflections", 1f);

        // Dim purple emission
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emissionColor * 0.35f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        renderer.material = mat;
    }

    void OnDestroy()
    {
        // Clean up fallback prefabs
        if (_fallbackUnitPrefab != null) Destroy(_fallbackUnitPrefab);
        if (_fallbackBuildingPrefab != null) Destroy(_fallbackBuildingPrefab);

        if (Instance == this) Instance = null;
    }
}