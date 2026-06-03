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
            Hut, GatherersHut, Barracks, ArcheryRange, Shrine, Vault, Keep, Wall, Smelter, Temple, Hall,
            // Runai culture buildings
            RunaiOutpost, RunaiTradeHub, RunaiBazaar, RunaiSiegeWorkshop,
            // Alanthor culture buildings
            AlanthorWatchTower, AlanthorPracticeRange, AlanthorSiegeYard, AlanthorRoyalStable,
            // Feraldis culture buildings
            FeraldisHuntingLodge, FeraldisLoggingStation, FeraldisLonghouse, FeraldisTotemTower, FeraldisSiegeYard,
            // Per-hub "Build Wall" action: anchors a new hub + connecting
            // segment onto an existing wall hub. Placed without a builder;
            // auto-builds in 30 s. Entered via
            // BuilderCommandPanel.TriggerHubBuildWall(sourceHub).
            WallExtend
        }
        private BuildType _currentBuild = BuildType.Hut;

        // Hub-anchored "Build Wall" placement: the source hub the player
        // selected when invoking the action. The next LMB click drops a new
        // hub at the cursor and a segment connecting it to this anchor.
        // Set by TriggerHubBuildWall, cleared on placement / cancel.
        private Entity _wallExtendSourceHub;

        /// <summary>Self-build timer (seconds) for hubs + instances placed via
        /// the per-hub "Build Wall" action. No builder is dispatched; the
        /// AutoConstructionSystem ticks Progress at 1.0/s.</summary>
        private const float WallExtendBuildSeconds = 30f;

        /// <summary>Build-Wall click within this distance of an existing friendly hub
        /// snaps onto it: reuse the hub and build only the connecting segment (no new
        /// hub, no hub cost). ~ the hub's own half-width so clicking on a hub snaps.</summary>
        private const float HubSnapRadius = 6f;

        // Placement validity
        private bool _placementValid = true;

        // Placement yaw in degrees (mouse-wheel rotation during placement)
        private float _placementYaw;
        private const float YawStepDegrees = 15f;

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
                    _placingInstance.transform.position = p + Vector3.up * yOffset;
                    if (_currentBuild != BuildType.Wall && _currentBuild != BuildType.WallExtend)
                        // +180° visual offset so building previews face the
                        // default camera. Matches the runtime visual rotation
                        // applied in PresentationSpawnSystem.VisualRotation —
                        // ECS LocalTransform stays clean (yaw only), the
                        // visual is offset.
                        _placingInstance.transform.rotation =
                            Quaternion.Euler(0f, _placementYaw + 180f, 0f);

                    // Check placement validity for non-wall buildings (AABB collision).
                    // Wall builds are deliberately exempt — they're already gated by
                    // hub-anchor proximity (WallExtend) or are point-placements (Wall).
                    if (_currentBuild != BuildType.Wall && _currentBuild != BuildType.WallExtend)
                    {
                        _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
                        var buildSize = BuildCommandHelper.GetBuildingSize(BuildId(_currentBuild));
                        _placementValid = BuildCommandHelper.IsValidBuildPosition(
                            _em, (float3)_placingInstance.transform.position, buildSize);
                        UpdatePreviewColor(_placementValid);
                    }
                }

                // Confirm placement
                bool isWallBuild = _currentBuild == BuildType.Wall || _currentBuild == BuildType.WallExtend;
                if (UnityEngine.Input.GetMouseButtonDown(0) && !isWallBuild && !_placementValid)
                {
                    PlayerNotificationSystem.Notify("Invalid placement");
                }
                if (UnityEngine.Input.GetMouseButtonDown(0) && (isWallBuild || _placementValid))
                {
                    var pos = _placingInstance.transform.position;

                    if (_currentBuild == BuildType.Wall)
                    {
                        // First hub only — placed by a builder using the normal
                        // construction path. No chain mode any more: subsequent
                        // hubs use the per-hub Build Wall action (WallExtend)
                        // which auto-connects + auto-builds in 30s.
                        SpawnFirstWallHub((float3)pos);
                        CancelPlacementPreviewOnly();
                    }
                    else if (_currentBuild == BuildType.WallExtend)
                    {
                        SpawnExtendedWallHub((float3)pos);
                        CancelPlacementPreviewOnly();
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
                "Hall" => BuildType.Hall,
                "Hut" => BuildType.Hut,
                "GatherersHut" => BuildType.GatherersHut,
                "Barracks" => BuildType.Barracks,
                "ArcheryRange" => BuildType.ArcheryRange,
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
                "Alanthor_PracticeRange" => BuildType.AlanthorPracticeRange,
                "Alanthor_SiegeYard" => BuildType.AlanthorSiegeYard,
                "Alanthor_RoyalStable" => BuildType.AlanthorRoyalStable,
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

            // Preview uses the building's SO prefab (resolved by PresentationId). Null falls
            // through to the prefab switch / placeholder cube below.
            GameObject procPreview = null;
            if (previewPid > 0 && TechCatalog.TryGetPrefab(previewPid, out var soPrev) && soPrev != null)
            {
                procPreview = Instantiate(soPrev);
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

            // Reset hub-anchored placement state (per-hub Build Wall action).
            _wallExtendSourceHub = Entity.Null;
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

            // Block additional Halls past the 6-per-faction cap (counts both
            // the starting Hall and any in-progress builds). Builders can
            // only place Halls post-age-up — the GetBuildingActions catalog
            // gate enforces the culture requirement at button-render time;
            // this is the runtime fallback so a stale UI click can't bypass
            // it.
            if (id == "Hall")
            {
                int hallCount = BuildingFactory.GetFactionBuildingCount<HallTag>(_em, fac);
                if (hallCount >= 6)
                {
                    PlayerNotificationSystem.Notify("Maximum 6 Halls per faction");
                    return;
                }
            }

            // Block additional Temples of Ridan — only one per faction.
            // Counts both completed and under-construction Temples so a
            // double-click during a 50 s build can't sneak a second order in.
            if (id == "TempleOfRidan")
            {
                int templeCount = BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(_em, fac);
                if (templeCount >= 1)
                {
                    PlayerNotificationSystem.Notify("Only one Temple of Ridan per faction");
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
            BuildType.ArcheryRange => "ArcheryRange",
            BuildType.Shrine => "ShrineOfAhridan",
            BuildType.Temple => "TempleOfRidan",
            BuildType.Vault => "VaultOfAlmierra",
            BuildType.Keep => "FiendstoneKeep",
            BuildType.Wall => "Alanthor_Wall",
            BuildType.WallExtend => "Alanthor_Wall", // per-hub Build Wall — same preview as a base hub
            BuildType.Hall => "Hall",
            BuildType.Smelter => "Alanthor_Smelter",
            // Runai culture buildings
            BuildType.RunaiOutpost => "Runai_Outpost",
            BuildType.RunaiTradeHub => "Runai_TradeHub",
            BuildType.RunaiBazaar => "ThessarasBazaar",
            BuildType.RunaiSiegeWorkshop => "Runai_SiegeWorkshop",
            // Alanthor culture buildings
            BuildType.AlanthorWatchTower => "Alanthor_Tower",
            BuildType.AlanthorPracticeRange => "Alanthor_PracticeRange",
            BuildType.AlanthorSiegeYard => "Alanthor_SiegeYard",
            BuildType.AlanthorRoyalStable => "Alanthor_RoyalStable",
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
            // Hall / Barracks / Hut participate in the upgrade system. GatherersHut
            // uses a single prefab regardless of culture (no _al_1, no _ru_1 etc.) —
            // we route it through here too so the placement preview matches the
            // real spawn instead of falling back to the procedural model.
            string baseName = bt switch
            {
                BuildType.Hut          => "Hut",
                BuildType.Barracks     => "Barracks",
                BuildType.GatherersHut => "GatherersHut",
                BuildType.Hall         => "Hall",
                _                      => null,
            };
            if (baseName == null) return null;

            var key = (bt, culture);
            if (_previewPrefabCache.TryGetValue(key, out var cached)) return cached;
            if (_previewPrefabNegativeCache.Contains(key)) return null;

            // GatherersHut never evolves — always use the L0 prefab regardless of culture.
            bool useL0Only = bt == BuildType.GatherersHut;
            string code = useL0Only ? "" : TheWaningBorder.Core.Settings.BuildingUpgradeConfig.CultureCode(culture);
            string path = string.IsNullOrEmpty(code)
                ? $"Prefabs/Buildings/{baseName}"          // L0 — pre age-up (or culture-agnostic)
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
            BuildType.ArcheryRange => 511,
            BuildType.Shrine => 520,
            BuildType.Vault => 530,
            BuildType.Keep => 540,
            BuildType.Wall => 0,     // Procedural wall handled separately
            BuildType.WallExtend => 0, // Same procedural hub mesh as Wall
            BuildType.Hall => 100,   // Hall.PresentationID — uses the standard Hall prefab
            BuildType.Smelter => 0,  // Procedural smelter handled separately
            BuildType.RunaiOutpost => 350,
            BuildType.RunaiTradeHub => 351,
            BuildType.RunaiBazaar => 352,
            BuildType.RunaiSiegeWorkshop => 353,
            BuildType.AlanthorWatchTower => 354,
            BuildType.AlanthorPracticeRange => 355,
            BuildType.AlanthorSiegeYard => 357,
            BuildType.AlanthorRoyalStable => 356,
            BuildType.FeraldisHuntingLodge => 358,
            BuildType.FeraldisLoggingStation => 359,
            BuildType.FeraldisLonghouse => 360,
            BuildType.FeraldisTotemTower => 361,
            BuildType.FeraldisSiegeYard => 362,
            _ => 102
        };

        // task-109: Alanthor wall primitives — only Alanthor_Wall (hub) and Alanthor_Tower
        //           (standalone watch tower) are placeable. Alanthor_WallTower and
        //           Alanthor_WallGate are conversion-only (segment selection → Convert
        //           to Tower / Convert to Gate). See docs/Design/Age_1_Alanthor.md
        //           § Wall System (BFME2 hub-and-segment) and the static-ctor
        //           Debug.Assert guard in EntityExtractors.cs / EntityActionExtractor.
        /// <summary>
        /// Place the FIRST wall hub. Standard builder-driven construction (5s).
        /// No chaining — subsequent hubs use the per-hub Build Wall action
        /// (TriggerHubBuildWall / SpawnExtendedWallHub) which auto-connects
        /// with a segment and self-builds in 30s without a builder.
        /// </summary>
        private void SpawnFirstWallHub(float3 pos)
        {
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
            var fac = GetSelectedFactionOrDefault();

            if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
            if (!FactionEconomy.Spend(_em, fac, cost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources");
                return;
            }

            Entity hub = AlanthorWall.CreateHub(_em, pos, fac);

            if (!_em.HasComponent<UnderConstruction>(hub))
                _em.AddComponentData(hub, new UnderConstruction { Progress = 0f, Total = 5f });
            if (_em.HasComponent<Health>(hub))
            {
                var hp = _em.GetComponentData<Health>(hub);
                _em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
            }

            AssignBuildersToConstruction(hub, "Alanthor_Wall", pos);
        }

        /// <summary>
        /// Per-hub "Build Wall" placement: drop a new wall hub at <paramref name="pos"/>
        /// AND a connecting segment back to <see cref="_wallExtendSourceHub"/>.
        /// The new hub + every wall instance along the segment are tagged
        /// <see cref="AutoConstructTag"/> and self-build in 30s — no builder
        /// is dispatched. Pays the standard Alanthor_Wall cost once for the
        /// new hub; the segment + instances ride for free (matches the
        /// previous chain-mode behaviour where the segment was bundled with
        /// the hub purchase).
        /// </summary>
        private void SpawnExtendedWallHub(float3 pos)
        {
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
            var fac = GetSelectedFactionOrDefault();

            if (_wallExtendSourceHub == Entity.Null || !_em.Exists(_wallExtendSourceHub))
            {
                PlayerNotificationSystem.NotifyError("Source hub no longer exists");
                _wallExtendSourceHub = Entity.Null;
                return;
            }

            // Snap: a click near an existing friendly hub (other than the source)
            // reuses that hub and builds ONLY the connecting segment — no new hub,
            // no hub cost. Otherwise place + pay for a new self-building hub.
            Entity hub = FindNearestHubForSnap(pos, fac, _wallExtendSourceHub);
            if (hub != Entity.Null)
            {
                if (AlanthorWall.AreHubsConnected(_em, _wallExtendSourceHub, hub))
                {
                    PlayerNotificationSystem.NotifyError("Those hubs are already connected");
                    _wallExtendSourceHub = Entity.Null;
                    return;
                }
            }
            else
            {
                if (!BuildCosts.TryGet("Alanthor_Wall", out var cost)) cost = default;
                if (!FactionEconomy.Spend(_em, fac, cost))
                {
                    PlayerNotificationSystem.NotifyError("Not enough resources");
                    return;
                }

                // New hub — auto-construct, no builder, 30s.
                hub = AlanthorWall.CreateHub(_em, pos, fac);
                _em.AddComponentData(hub,
                    new UnderConstruction { Progress = 0f, Total = WallExtendBuildSeconds });
                _em.AddComponent<AutoConstructTag>(hub);
                if (_em.HasComponent<Health>(hub))
                {
                    var hp = _em.GetComponentData<Health>(hub);
                    _em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
                }
            }

            // Segment — CreateSegment also spawns the wall instances along the
            // line and wires both hubs' WallHubLink buffers. The source hub may
            // still be under construction; that's fine — the segment graph
            // doesn't gate on hub completion.
            Entity segment = AlanthorWall.CreateSegment(_em, _wallExtendSourceHub, hub, fac);

            // Tag every spawned wall instance for auto-construction too.
            // CreateSegment's structural changes have all settled by the time
            // it returns, but we snapshot the buffer anyway because the
            // AddComponentData calls below ARE structural — iterating the
            // live buffer while archetypes change would crash. (Same pattern
            // the old chain-placement code used.)
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
                        _em.AddComponentData(inst,
                            new UnderConstruction { Progress = 0f, Total = WallExtendBuildSeconds });
                    if (!_em.HasComponent<AutoConstructTag>(inst))
                        _em.AddComponent<AutoConstructTag>(inst);
                    if (_em.HasComponent<Health>(inst))
                    {
                        var hp = _em.GetComponentData<Health>(inst);
                        _em.SetComponentData(inst, new Health { Value = 1, Max = hp.Max });
                    }
                }
                snapshot.Dispose();
            }

            // Single-shot action — clear the anchor so the next Build Wall
            // click on a hub starts fresh.
            _wallExtendSourceHub = Entity.Null;
        }

        /// <summary>
        /// Nearest friendly Wall Hub to <paramref name="pos"/> within
        /// <see cref="HubSnapRadius"/>, excluding <paramref name="exclude"/>.
        /// Returns Entity.Null when none is in range (caller places a fresh hub).
        /// </summary>
        private Entity FindNearestHubForSnap(float3 pos, Faction fac, Entity exclude)
        {
            var q = _em.CreateEntityQuery(
                ComponentType.ReadOnly<WallHubTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            float bestSq = HubSnapRadius * HubSnapRadius;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (e == exclude) continue;
                if (_em.GetComponentData<FactionTag>(e).Value != fac) continue;
                var hpos = _em.GetComponentData<Unity.Transforms.LocalTransform>(e).Position;
                float dx = pos.x - hpos.x, dz = pos.z - hpos.z;
                float d = dx * dx + dz * dz;
                if (d < bestSq) { bestSq = d; best = e; }
            }
            return best;
        }

        /// <summary>
        /// Enter hub-anchored placement mode for the per-hub "Build Wall"
        /// action. The next LMB click drops a new hub at the cursor plus a
        /// segment connecting back to <paramref name="sourceHub"/>; both
        /// self-build in 30s. Called from HudBridge when the action button
        /// fires on a selected wall hub.
        /// </summary>
        public static void TriggerHubBuildWall(Entity sourceHub)
        {
            if (GameSettings.IsObserver) return;
            var instance = FindFirstObjectByType<BuilderCommandPanel>();
            if (instance == null) return;

            // Order matters: StartPlacement() calls CancelPlacement(), which resets
            // _wallExtendSourceHub to Entity.Null. Set the anchor AFTER so it
            // survives — otherwise the click commit sees a null source and bails
            // ("Source hub no longer exists"), clearing the preview without building.
            instance._currentBuild = BuildType.WallExtend;
            instance.StartPlacement();
            instance._wallExtendSourceHub = sourceHub;
            SuppressClicksThisFrame = true;
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

        public static bool IsPointerOverPanel()
        {
            if (!PanelVisible) return false;
            return PanelRectScreenBL.Contains(UnityEngine.Input.mousePosition);
        }
    }
}