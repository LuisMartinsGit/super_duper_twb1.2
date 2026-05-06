// File: Assets/Scripts/UI/Panels/BuilderCommandPanel.cs
// Building placement UI with preview and cost checking

using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.Input;
using TheWaningBorder.Data;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Presentation;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// Handles building placement preview and spawning.
    /// Works with EntityActionPanel for UI integration.
    /// </summary>
    public class BuilderCommandPanel : MonoBehaviour
    {
        // Shared state for RTSInput and other systems
        public static bool PanelVisible;
        public static Rect PanelRectScreenBL;
        public static bool IsPlacingBuilding;
        public static bool SuppressClicksThisFrame;

        /// <summary>Current building ID being placed, or null if not placing.</summary>
        public static string CurrentBuildId => _activeInstance != null ? _activeInstance._currentBuildId : null;

        /// <summary>Whether the current placement position is valid.</summary>
        public static bool PlacementIsValid => _activeInstance != null ? _activeInstance._placementValid : true;

        private static BuilderCommandPanel _activeInstance;
        private string _currentBuildId;

        private EntityWorld _world;
        private EntityManager _em;

        [Header("Placement")]
        [SerializeField] private LayerMask placementMask = ~0;
        [SerializeField] private float yOffset = 0f;

        // Current placement preview
        private GameObject _placingInstance;

        // Build type
        public enum BuildType
        {
            Hut, GatherersHut, Barracks, Shrine, Vault, Keep, Wall, Smelter, Temple,
            // Runai culture buildings
            RunaiOutpost, RunaiTradeHub, RunaiBazaar, RunaiSiegeWorkshop,
            // Alanthor culture buildings
            AlanthorWatchTower, AlanthorGarrison, AlanthorRoyalStable, AlanthorSiegeYard,
            // Feraldis culture buildings
            FeraldisHuntingLodge, FeraldisLoggingStation, FeraldisLonghouse, FeraldisTotemTower, FeraldisSiegeYard
        }
        private BuildType _currentBuild = BuildType.Hut;

        // Wall chain-placement state
        private bool _wallPlacingSecondHub;
        private Entity _wallFirstHub;
        private float3 _wallFirstHubPos;

        // Placement validity
        private bool _placementValid = true;

        // Placement yaw in degrees (mouse-wheel rotation during placement)
        private float _placementYaw;
        private const float YawStepDegrees = 15f;

        // Wall hub snapping
        private const float WallHubSnapDistance = 2.0f;
        private Entity _snappedHub;  // Hub we're snapping to (Entity.Null if not snapping)

        // Prefab previews
        private GameObject _prefabGatherersHut;
        private GameObject _prefabHut;
        private GameObject _prefabBarracks;
        private GameObject _prefabShrine;
        private GameObject _prefabTemple;
        private GameObject _prefabVault;
        private GameObject _prefabKeep;

        // Panel sizing
        public const float PanelWidth = 300f;
        public const float PanelHeight = 170f;
        private RectOffset _padding;

        void Awake()
        {
            _activeInstance = this;
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            _padding = new RectOffset(10, 10, 10, 10);

            // Load preview prefabs
            _prefabGatherersHut = Resources.Load<GameObject>("Prefabs/Buildings/GatherersHut");
            _prefabHut = Resources.Load<GameObject>("Prefabs/Buildings/Hut");
            _prefabBarracks = Resources.Load<GameObject>("Prefabs/Buildings/Barracks");
            _prefabShrine = Resources.Load<GameObject>("Prefabs/Buildings/TempleOfRidan");
            _prefabTemple = Resources.Load<GameObject>("Prefabs/Buildings/TempleOfRidan"); // Reuses same prefab for now
            _prefabVault = Resources.Load<GameObject>("Prefabs/Runai/Buildings/VaultOfAlmierra");
            _prefabKeep = Resources.Load<GameObject>("Prefabs/Feraldis/Buildings/FiendstoneKeep");
        }

        void Update()
        {
            PanelRectScreenBL = new Rect(10f, 10f, PanelWidth, PanelHeight);

            if (IsPlacingBuilding)
            {
                if (_placingInstance == null) { CancelPlacement(); return; }

                // Mouse-wheel rotation (non-wall buildings only). Walls follow hub snap.
                if (_currentBuild != BuildType.Wall)
                {
                    float wheel = UnityEngine.Input.mouseScrollDelta.y;
                    if (math.abs(wheel) > 0.01f)
                    {
                        _placementYaw += math.sign(wheel) * YawStepDegrees;
                    }
                }

                if (TryGetMouseWorld(out Vector3 p))
                {
                    // Wall hub snapping: snap preview to nearby existing hubs
                    if (_currentBuild == BuildType.Wall)
                    {
                        _snappedHub = FindNearestWallHub((float3)p, WallHubSnapDistance);
                        if (_snappedHub != Entity.Null)
                        {
                            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
                            var hubPos = _em.GetComponentData<LocalTransform>(_snappedHub).Position;
                            p = new Vector3(hubPos.x, hubPos.y, hubPos.z);
                        }
                    }

                    _placingInstance.transform.position = p + Vector3.up * yOffset;
                    if (_currentBuild != BuildType.Wall)
                        // +180° visual offset so building previews face the
                        // default camera. Matches the runtime visual rotation
                        // applied in PresentationSpawnSystem.VisualRotation —
                        // ECS LocalTransform stays clean (yaw only), the
                        // visual is offset.
                        _placingInstance.transform.rotation =
                            Quaternion.Euler(0f, _placementYaw + 180f, 0f);

                    // Check placement validity for non-wall buildings (AABB collision)
                    if (_currentBuild != BuildType.Wall)
                    {
                        _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
                        var buildSize = BuildCommandHelper.GetBuildingSize(BuildId(_currentBuild));
                        _placementValid = BuildCommandHelper.IsValidBuildPosition(
                            _em, (float3)_placingInstance.transform.position, buildSize);
                        UpdatePreviewColor(_placementValid);
                    }
                }

                // Confirm placement
                if (UnityEngine.Input.GetMouseButtonDown(0) && _currentBuild != BuildType.Wall && !_placementValid)
                {
                    PlayerNotificationSystem.Notify("Invalid placement");
                }
                if (UnityEngine.Input.GetMouseButtonDown(0) && (_currentBuild == BuildType.Wall || _placementValid))
                {
                    var pos = _placingInstance.transform.position;

                    if (_currentBuild == BuildType.Wall)
                    {
                        // Wall chain mode: place hub, stay in placement mode for next hub
                        SpawnWallHub((float3)pos);
                    }
                    else
                    {
                        SpawnSelectedBuilding((float3)pos, _placementYaw);

                        // Shift held → stay in placement mode for another building
                        if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
                        {
                            // Destroy old preview and create a fresh one
                            if (_placingInstance != null) Destroy(_placingInstance);
                            _placingInstance = null;
                            StartPlacement(); // re-enters placement with same _currentBuild
                        }
                        else
                        {
                            CancelPlacementPreviewOnly();
                        }
                    }
                    SuppressClicksThisFrame = true;
                }

                // Cancel
                if (UnityEngine.Input.GetMouseButtonDown(1) || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelPlacement();
                }
            }
        }

        /// <summary>
        /// Start building placement mode for a specific building ID.
        /// Called from EntityActionPanel.
        /// </summary>
        public static void TriggerBuildingPlacement(string buildingId)
        {
            if (GameSettings.IsObserver) return;

            var instance = FindFirstObjectByType<BuilderCommandPanel>();
            if (instance == null) return;

            instance._currentBuild = buildingId switch
            {
                "Hut" => BuildType.Hut,
                "GatherersHut" => BuildType.GatherersHut,
                "Barracks" => BuildType.Barracks,
                "ShrineOfAhridan" => BuildType.Shrine,
                "TempleOfRidan" => BuildType.Temple,
                "VaultOfAlmierra" => BuildType.Vault,
                "FiendstoneKeep" => BuildType.Keep,
                "Alanthor_Wall" => BuildType.Wall,
                "Alanthor_Smelter" => BuildType.Smelter,
                // Runai culture buildings
                "Runai_Outpost" => BuildType.RunaiOutpost,
                "Runai_TradeHub" => BuildType.RunaiTradeHub,
                "ThessarasBazaar" => BuildType.RunaiBazaar,
                "Runai_SiegeWorkshop" => BuildType.RunaiSiegeWorkshop,
                // Alanthor culture buildings
                "Alanthor_Tower" => BuildType.AlanthorWatchTower,
                "Alanthor_Garrison" => BuildType.AlanthorGarrison,
                "Alanthor_Stable" => BuildType.AlanthorRoyalStable,
                "Alanthor_SiegeYard" => BuildType.AlanthorSiegeYard,
                // Feraldis culture buildings
                "Feraldis_HuntingLodge" => BuildType.FeraldisHuntingLodge,
                "Feraldis_LoggingStation" => BuildType.FeraldisLoggingStation,
                "Feraldis_Longhouse" => BuildType.FeraldisLonghouse,
                "Feraldis_Tower" => BuildType.FeraldisTotemTower,
                "Feraldis_SiegeYard" => BuildType.FeraldisSiegeYard,
                _ => BuildType.Hut
            };

            instance.StartPlacement();
            SuppressClicksThisFrame = true;
        }

        public void StartPlacement()
        {
            CancelPlacement();
            _currentBuildId = BuildId(_currentBuild);

            // Determine the player's current culture for preview tone
            byte playerCulture = Cultures.None;
            playerCulture = FactionColors.GetFactionCulture(GameSettings.LocalPlayerFaction);

            // Get presentation ID for the current build type
            int previewPid = GetPreviewPresentationId(_currentBuild);

            // ── Upgrade-aware prefab-first preview ─────────────────────
            // Mirror the actual spawn path: pre-age-up shows the L0 base
            // prefab (Hall.prefab / Barracks.prefab / Hut.prefab); after
            // culture is picked, show the L1 prefab (e.g. Hall_al_1) so
            // the player previews exactly what they'll see once
            // BuildingCultureAutoLevelSystem auto-bumps the new building.
            GameObject upgradePreview = TryLoadUpgradePreviewPrefab(_currentBuild, playerCulture);
            if (upgradePreview != null)
            {
                _placingInstance = Instantiate(upgradePreview);
                _placingInstance.SetActive(true);
            }
            else
            {

            // Try procedural generation first
            GameObject procPreview = null;
            if (previewPid > 0)
            {
                // Handle shared PresentationId 355 by checking BuildType directly
                if (previewPid == 355)
                {
                    bool isAlanthor = (_currentBuild == BuildType.AlanthorGarrison);
                    procPreview = ProceduralBuildingGenerator.Create355(
                        Vector3.zero, Entity.Null, isAlanthor);
                }
                else
                {
                    procPreview = ProceduralBuildingGenerator.TryCreate(
                        previewPid, Vector3.zero, Entity.Null, playerCulture);
                }
            }

            if (procPreview != null)
            {
                _placingInstance = procPreview;
            }
            else
            {
                // Try loading prefab
                var prefab = _currentBuild switch
                {
                    BuildType.GatherersHut => _prefabGatherersHut,
                    BuildType.Hut => _prefabHut,
                    BuildType.Barracks => _prefabBarracks,
                    BuildType.Shrine => _prefabShrine,
                    BuildType.Vault => _prefabVault,
                    BuildType.Keep => _prefabKeep,
                    _ => null
                };

                if (prefab != null)
                {
                    _placingInstance = Instantiate(prefab);
                }
                else
                {
                    // Final fallback: placeholder cube
                    _placingInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _placingInstance.transform.localScale = Vector3.one * 2f;
                    var r = _placingInstance.GetComponent<Renderer>();
                    if (r != null) r.material.color = new Color(0.5f, 0.4f, 0.2f, 0.5f);
                }
            }

            } // end of else (no upgrade-aware prefab found)

            _placingInstance.name = "PlacementPreview";

            // Disable colliders on preview
            foreach (var col in _placingInstance.GetComponentsInChildren<Collider>())
                col.enabled = false;

            // Switch each preview material to URP Transparent surface so the
            // green/red tint (RGBA, alpha < 1) renders translucent. Without this,
            // URP Lit materials default to Opaque and ignore the alpha channel.
            foreach (var renderer in _placingInstance.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                    MakeMaterialTransparent(mat);
            }

            // Reset rotation for fresh placement (mouse wheel adjusts during Update).
            _placementYaw = 0f;

            IsPlacingBuilding = true;
            GathererHutAreaDisplay.IsPlacingGathererHutType = (_currentBuild == BuildType.GatherersHut);
        }

        /// <summary>
        /// Reconfigure a URP Lit/Unlit material clone to render in Transparent
        /// surface mode so per-frame `_BaseColor` alpha values actually blend.
        /// Safe no-op for non-URP shaders that don't have these properties.
        /// </summary>
        private static void MakeMaterialTransparent(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);   // 0 = Alpha
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        public void CancelPlacement()
        {
            if (_placingInstance != null) Destroy(_placingInstance);
            _placingInstance = null;
            IsPlacingBuilding = false;
            GathererHutAreaDisplay.IsPlacingGathererHutType = false;

            // Reset wall chain state
            _wallPlacingSecondHub = false;
            _wallFirstHub = Entity.Null;
            _snappedHub = Entity.Null;
        }

        private void CancelPlacementPreviewOnly()
        {
            if (_placingInstance != null) Destroy(_placingInstance);
            _placingInstance = null;
            IsPlacingBuilding = false;
            GathererHutAreaDisplay.IsPlacingGathererHutType = false;
        }

        private void UpdatePreviewColor(bool valid)
        {
            if (_placingInstance == null) return;
            Color tint = valid
                ? new Color(0.5f, 1f, 0.5f, 0.5f)
                : new Color(1f, 0.3f, 0.3f, 0.5f);
            foreach (var renderer in _placingInstance.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                {
                    // URP Lit/Unlit use _BaseColor; legacy shaders use _Color.
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", tint);
                    if (mat.HasProperty("_Color"))
                        mat.color = tint;
                }
            }
        }

        private void SpawnSelectedBuilding(float3 pos, float yawDegrees)
        {
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;

            var fac = GetSelectedFactionOrDefault();

            var id = BuildId(_currentBuild);

            // Block trading post if faction already has 10
            if (id == "Runai_TradingPost")
            {
                int tpCount = BuildingFactory.GetFactionBuildingCount<TradingPostTag>(_em, fac);
                if (tpCount >= 10)
                {
                    PlayerNotificationSystem.Notify("Maximum 10 Trading Posts");
                    return;
                }
            }

            // Block choice building if faction already has one
            if (BuildingFactory.IsChoiceBuilding(id))
            {
                var existing = BuildingFactory.GetFactionChoiceBuilding(_em, fac);
                if (existing != null)
                {
                    PlayerNotificationSystem.Notify("Already have a choice building");
                    return;
                }
            }

            if (!BuildCosts.TryGet(id, out var cost)) cost = default;

            if (!FactionEconomy.Spend(_em, fac, cost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources");
                return;
            }

            if (GameSettings.IsMultiplayer)
            {
                // Multiplayer: queue via lockstep — building created on all clients at same tick
                CommandRouter.IssuePlaceBuilding(_em, id, pos, fac);

                // Send selected builders to the build position — the building entity doesn't
                // exist yet (created 2 ticks later), so we issue Build with Entity.Null target.
                // BuildCommandHelper handles null target by moving to position and auto-finding
                // the nearest UnderConstruction building when the builder arrives.
                var sel = SelectionSystem.CurrentSelection;
                if (sel != null)
                {
                    foreach (var entity in sel)
                    {
                        if (!_em.Exists(entity)) continue;
                        if (!_em.HasComponent<CanBuild>(entity)) continue;
                        CommandRouter.IssueBuild(_em, entity, Entity.Null, id, pos);
                    }
                }
                return;
            }

            // Single player: create building directly and assign builders
            Entity building = CommandRouter.PlaceBuildingDirect(_em, id, pos, fac);

            // Apply mouse-wheel rotation to the new building's transform.
            if (_em.HasComponent<LocalTransform>(building))
            {
                var lt = _em.GetComponentData<LocalTransform>(building);
                lt.Rotation = quaternion.RotateY(math.radians(yawDegrees));
                _em.SetComponentData(building, lt);
            }

            // Flatten the terrain under the building footprint so the model
            // sits on level ground regardless of the (≤15°) underlying slope.
            var sizeForFlatten = BuildCommandHelper.GetBuildingSize(id);
            float halfExtent = math.max(sizeForFlatten.x, sizeForFlatten.y) * 0.5f;
            var pt = TheWaningBorder.World.Terrain.ProceduralTerrain.Instance;
            if (pt != null) pt.FlattenAt(new Vector3(pos.x, 0f, pos.z), halfExtent);

            AssignBuildersToConstruction(building, id, pos);
        }

        /// <summary>
        /// Assigns selected builder units to construct the given building.
        /// </summary>
        private void AssignBuildersToConstruction(Entity building, string buildingId, float3 pos)
        {
            var sel = SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count == 0) return;

            foreach (var entity in sel)
            {
                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<CanBuild>(entity)) continue;

                CommandRouter.IssueBuild(_em, entity, building, buildingId, pos);
            }
        }

        private Faction GetSelectedFactionOrDefault()
        {
            var sel = SelectionSystem.CurrentSelection;
            if (sel != null && sel.Count > 0)
            {
                var e = sel[0];
                if (_em.Exists(e) && _em.HasComponent<FactionTag>(e))
                    return _em.GetComponentData<FactionTag>(e).Value;
            }
            return GameSettings.LocalPlayerFaction;
        }

        private static string BuildId(BuildType t) => t switch
        {
            BuildType.Hut => "Hut",
            BuildType.GatherersHut => "GatherersHut",
            BuildType.Barracks => "Barracks",
            BuildType.Shrine => "ShrineOfAhridan",
            BuildType.Temple => "TempleOfRidan",
            BuildType.Vault => "VaultOfAlmierra",
            BuildType.Keep => "FiendstoneKeep",
            BuildType.Wall => "Alanthor_Wall",
            BuildType.Smelter => "Alanthor_Smelter",
            // Runai culture buildings
            BuildType.RunaiOutpost => "Runai_Outpost",
            BuildType.RunaiTradeHub => "Runai_TradeHub",
            BuildType.RunaiBazaar => "ThessarasBazaar",
            BuildType.RunaiSiegeWorkshop => "Runai_SiegeWorkshop",
            // Alanthor culture buildings
            BuildType.AlanthorWatchTower => "Alanthor_Tower",
            BuildType.AlanthorGarrison => "Alanthor_Garrison",
            BuildType.AlanthorRoyalStable => "Alanthor_Stable",
            BuildType.AlanthorSiegeYard => "Alanthor_SiegeYard",
            // Feraldis culture buildings
            BuildType.FeraldisHuntingLodge => "Feraldis_HuntingLodge",
            BuildType.FeraldisLoggingStation => "Feraldis_LoggingStation",
            BuildType.FeraldisLonghouse => "Feraldis_Longhouse",
            BuildType.FeraldisTotemTower => "Feraldis_Tower",
            BuildType.FeraldisSiegeYard => "Feraldis_SiegeYard",
            _ => "Hut"
        };

        // Cached preview-prefab lookups so Resources.Load runs at most once
        // per (BuildType, culture) pair. null = "no upgrade-aware prefab present"
        // (the existing procedural / explicit-prefab fallback applies).
        private readonly System.Collections.Generic.Dictionary<(BuildType, byte), GameObject>
            _previewPrefabCache = new();
        private readonly System.Collections.Generic.HashSet<(BuildType, byte)>
            _previewPrefabNegativeCache = new();

        /// <summary>
        /// Resolve the upgrade-aware preview prefab for the current build:
        /// L0 base prefab when the player hasn't picked a culture yet, L1
        /// prefab when they have (so the placement ghost matches the visual
        /// the new building will assume the moment construction finishes
        /// and BuildingCultureAutoLevelSystem auto-bumps it). Returns null
        /// for build types not in the upgrade ladder OR when no matching
        /// prefab is present in Resources — caller falls through to the
        /// existing procedural / prefab-by-id path.
        /// </summary>
        private GameObject TryLoadUpgradePreviewPrefab(BuildType bt, byte culture)
        {
            // Only Hall / Barracks / Hut participate in the upgrade system
            // (matches BuildingUpgradeable on the entity factories).
            string baseName = bt switch
            {
                BuildType.Hut      => "Hut",
                BuildType.Barracks => "Barracks",
                _                  => null,
            };
            if (baseName == null) return null;

            var key = (bt, culture);
            if (_previewPrefabCache.TryGetValue(key, out var cached)) return cached;
            if (_previewPrefabNegativeCache.Contains(key)) return null;

            string code = TheWaningBorder.Core.Settings.BuildingUpgradeConfig.CultureCode(culture);
            string path = string.IsNullOrEmpty(code)
                ? $"Prefabs/Buildings/{baseName}"          // L0 — pre age-up
                : $"Prefabs/Buildings/{baseName}_{code}_1"; // L1 — post age-up
            var loaded = Resources.Load<GameObject>(path);
            if (loaded == null)
            {
                _previewPrefabNegativeCache.Add(key);
                return null;
            }
            _previewPrefabCache[key] = loaded;
            return loaded;
        }

        /// <summary>
        /// Get the PresentationId for preview rendering of a BuildType.
        /// </summary>
        private static int GetPreviewPresentationId(BuildType t) => t switch
        {
            BuildType.Hut => 102,
            BuildType.GatherersHut => 101,
            BuildType.Barracks => 510,
            BuildType.Shrine => 520,
            BuildType.Vault => 530,
            BuildType.Keep => 540,
            BuildType.Wall => 0,     // Procedural wall handled separately
            BuildType.Smelter => 0,  // Procedural smelter handled separately
            BuildType.RunaiOutpost => 350,
            BuildType.RunaiTradeHub => 351,
            BuildType.RunaiBazaar => 352,
            BuildType.RunaiSiegeWorkshop => 353,
            BuildType.AlanthorWatchTower => 354,
            BuildType.AlanthorGarrison => 355,
            BuildType.AlanthorRoyalStable => 356,
            BuildType.AlanthorSiegeYard => 357,
            BuildType.FeraldisHuntingLodge => 358,
            BuildType.FeraldisLoggingStation => 359,
            BuildType.FeraldisLonghouse => 360,
            BuildType.FeraldisTotemTower => 361,
            BuildType.FeraldisSiegeYard => 362,
            _ => 102
        };

        /// <summary>
        /// Wall chain-placement: place a hub, and if chaining, auto-connect to the previous hub.
        /// </summary>
        private void SpawnWallHub(float3 pos)
        {
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
            var fac = GetSelectedFactionOrDefault();

            Entity hub;

            // If snapped to an existing hub, reuse it instead of creating a new one
            if (_snappedHub != Entity.Null && _em.Exists(_snappedHub))
            {
                hub = _snappedHub;
                pos = _em.GetComponentData<LocalTransform>(hub).Position;
                // No cost — we're connecting to an existing hub, not building a new one
            }
            else
            {
                if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;

                if (!FactionEconomy.Spend(_em, fac, cost))
                {
                    PlayerNotificationSystem.NotifyError("Not enough resources");
                    return;
                }

                // Create the hub entity
                hub = AlanthorWall.CreateHub(_em, pos, fac);

                // Mark as under construction
                if (!_em.HasComponent<UnderConstruction>(hub))
                    _em.AddComponentData(hub, new UnderConstruction { Progress = 0f, Total = 5f });
                if (_em.HasComponent<Health>(hub))
                {
                    var hp = _em.GetComponentData<Health>(hub);
                    _em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
                }

                // Assign builders
                AssignBuildersToConstruction(hub, "Alanthor_Wall", pos);
            }

            // If chaining from a previous hub, spawn a wall segment between them
            if (_wallPlacingSecondHub && _em.Exists(_wallFirstHub) && hub != _wallFirstHub)
            {
                Entity segment = AlanthorWall.CreateSegment(_em, _wallFirstHub, hub, fac);

                // Mark all wall instances in this segment as under construction.
                // Snapshot entities BEFORE iterating: AddComponentData below is a
                // structural change that invalidates the WallInstanceRef buffer
                // handle, and AssignBuildersToConstruction → IssueBuild also
                // mutates archetypes — touching the live buffer in the next loop
                // iteration throws ObjectDisposedException.
                if (_em.HasBuffer<WallInstanceRef>(segment))
                {
                    var instances = _em.GetBuffer<WallInstanceRef>(segment);
                    int count = instances.Length;
                    var snapshot = new Unity.Collections.NativeArray<Entity>(
                        count, Unity.Collections.Allocator.Temp);
                    for (int i = 0; i < count; i++)
                        snapshot[i] = instances[i].Instance;

                    for (int i = 0; i < count; i++)
                    {
                        var inst = snapshot[i];
                        if (!_em.Exists(inst)) continue;
                        if (!_em.HasComponent<UnderConstruction>(inst))
                            _em.AddComponentData(inst, new UnderConstruction { Progress = 0f, Total = 5f });
                        if (_em.HasComponent<Health>(inst))
                        {
                            var hp = _em.GetComponentData<Health>(inst);
                            _em.SetComponentData(inst, new Health { Value = 1, Max = hp.Max });
                        }
                        if (_em.HasComponent<LocalTransform>(inst))
                            AssignBuildersToConstruction(inst, "Alanthor_Wall",
                                _em.GetComponentData<LocalTransform>(inst).Position);
                    }
                    snapshot.Dispose();
                }
            }

            // Current hub becomes the first hub for the next chain link
            _wallFirstHub = hub;
            _wallFirstHubPos = pos;
            _wallPlacingSecondHub = true;
            _snappedHub = Entity.Null;
        }

        // Fix #222: cached Camera.main reference
        private Camera _cachedCamera;

        private bool TryGetMouseWorld(out Vector3 world)
        {
            world = default;
            var cam = _cachedCamera != null ? _cachedCamera : (_cachedCamera = Camera.main);
            if (!cam) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            // Primary: raycast against placement mask
            if (Physics.Raycast(ray, out var hit, 10000f, placementMask, QueryTriggerInteraction.Ignore))
            {
                world = hit.point;
                return true;
            }

            // Fallback: use terrain utility with plane intersection for ray
            if (TerrainUtility.IsReady(out UnityEngine.Terrain terrain))
            {
                Plane tp = new Plane(Vector3.up, new Vector3(0, terrain.transform.position.y, 0));
                if (tp.Raycast(ray, out float t))
                {
                    var p = ray.GetPoint(t);
                    world = new Vector3(p.x, TerrainUtility.GetHeight(p.x, p.z), p.z);
                    return true;
                }
            }

            // Last resort: ground plane at y=0
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float d2))
            {
                var p = ray.GetPoint(d2);
                world = new Vector3(p.x, 0f, p.z);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Find the nearest existing wall hub within snap distance of the given position.
        /// Returns Entity.Null if none found.
        /// </summary>
        private Entity FindNearestWallHub(float3 pos, float maxDist)
        {
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;

            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<WallHubTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var hubs = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var fac = GetSelectedFactionOrDefault();
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < hubs.Length; i++)
            {
                // Only snap to own faction's hubs
                if (factions[i].Value != fac) continue;

                float dist = math.distance(
                    new float2(pos.x, pos.z),
                    new float2(transforms[i].Position.x, transforms[i].Position.z));

                if (dist < nearestDist && dist <= maxDist)
                {
                    nearest = hubs[i];
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        public static bool IsPointerOverPanel()
        {
            if (!PanelVisible) return false;
            return PanelRectScreenBL.Contains(UnityEngine.Input.mousePosition);
        }
    }
}