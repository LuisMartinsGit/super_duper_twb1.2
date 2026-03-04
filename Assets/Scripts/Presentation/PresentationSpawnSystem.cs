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
    };

    // Fallback prefabs if specific one not found
    private GameObject _fallbackUnitPrefab;
    private GameObject _fallbackBuildingPrefab;

    // Track which entities already have visuals
    private HashSet<Entity> _spawnedEntities = new();

    // Cache
    private Unity.Entities.World _world;
    private EntityManager _em;
    private EntityQuery _presentationQuery;

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
        int treeCount = rng.Next(15, 21); // Dense forest: 15-20 trees

        // Colors
        var trunkBrown = new Color(0.35f, 0.22f, 0.10f);
        var canopyDarkGreen = new Color(0.15f, 0.35f, 0.10f);
        var canopyLightGreen = new Color(0.25f, 0.50f, 0.15f);

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
                trunkRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                trunkRenderer.material.color = trunkBrown;
            }
            // Remove trunk collider (root will have one big collider)
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
                canopyRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                float greenVariation = (float)rng.NextDouble();
                canopyRenderer.material.color = Color.Lerp(canopyDarkGreen, canopyLightGreen, greenVariation);
            }
            // Remove canopy collider
            var canopyCol = canopy.GetComponent<Collider>();
            if (canopyCol != null) Destroy(canopyCol);
        }

        // Add a single large collider for the whole forest
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

    void OnDestroy()
    {
        // Clean up fallback prefabs
        if (_fallbackUnitPrefab != null) Destroy(_fallbackUnitPrefab);
        if (_fallbackBuildingPrefab != null) Destroy(_fallbackBuildingPrefab);

        if (Instance == this) Instance = null;
    }
}