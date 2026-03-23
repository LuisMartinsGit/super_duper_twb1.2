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
            Hut, GatherersHut, Barracks, Shrine, Vault, Keep, Wall, Smelter,
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

        // Wall hub snapping
        private const float WallHubSnapDistance = 2.0f;
        private Entity _snappedHub;  // Hub we're snapping to (Entity.Null if not snapping)

        // Prefab previews
        private GameObject _prefabGatherersHut;
        private GameObject _prefabHut;
        private GameObject _prefabBarracks;
        private GameObject _prefabShrine;
        private GameObject _prefabVault;
        private GameObject _prefabKeep;

        // Panel sizing
        public const float PanelWidth = 300f;
        public const float PanelHeight = 170f;
        private RectOffset _padding;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            _padding = new RectOffset(10, 10, 10, 10);

            // Load preview prefabs
            _prefabGatherersHut = Resources.Load<GameObject>("Prefabs/Buildings/GatherersHut");
            _prefabHut = Resources.Load<GameObject>("Prefabs/Buildings/Hut");
            _prefabBarracks = Resources.Load<GameObject>("Prefabs/Buildings/Barracks");
            _prefabShrine = Resources.Load<GameObject>("Prefabs/Buildings/TempleOfRidan");
            _prefabVault = Resources.Load<GameObject>("Prefabs/Runai/Buildings/VaultOfAlmierra");
            _prefabKeep = Resources.Load<GameObject>("Prefabs/Feraldis/Buildings/FiendstoneKeep");
        }

        void Update()
        {
            PanelRectScreenBL = new Rect(10f, 10f, PanelWidth, PanelHeight);

            if (IsPlacingBuilding)
            {
                if (_placingInstance == null) { CancelPlacement(); return; }

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

                    // Check placement validity for non-wall buildings
                    if (_currentBuild != BuildType.Wall)
                    {
                        _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
                        float radius = BuildCommandHelper.GetBuildingRadius(BuildId(_currentBuild));
                        _placementValid = BuildCommandHelper.IsValidBuildPosition(
                            _em, (float3)_placingInstance.transform.position, radius);
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
                        SpawnSelectedBuilding((float3)pos);

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
            var instance = FindObjectOfType<BuilderCommandPanel>();
            if (instance == null) return;

            instance._currentBuild = buildingId switch
            {
                "Hut" => BuildType.Hut,
                "GatherersHut" => BuildType.GatherersHut,
                "Barracks" => BuildType.Barracks,
                "TempleOfRidan" => BuildType.Shrine,
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

            var prefab = _currentBuild switch
            {
                BuildType.GatherersHut => _prefabGatherersHut,
                BuildType.Hut => _prefabHut,
                BuildType.Barracks => _prefabBarracks,
                BuildType.Shrine => _prefabShrine,
                BuildType.Vault => _prefabVault,
                BuildType.Keep => _prefabKeep,
                BuildType.Wall => null, // Procedural — uses placeholder cube
                BuildType.Smelter => null, // Procedural — uses placeholder cube
                _ => _prefabHut
            };

            if (prefab != null)
            {
                _placingInstance = Instantiate(prefab);
            }
            else
            {
                // Placeholder cube for buildings without a prefab
                _placingInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _placingInstance.transform.localScale = Vector3.one * 2f;
                var r = _placingInstance.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.5f, 0.4f, 0.2f, 0.5f);
            }

            _placingInstance.name = "PlacementPreview";

            // Disable colliders on preview
            foreach (var col in _placingInstance.GetComponentsInChildren<Collider>())
                col.enabled = false;

            // Make semi-transparent
            foreach (var renderer in _placingInstance.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        var c = mat.color;
                        c.a = 0.5f;
                        mat.color = c;
                    }
                }
            }

            IsPlacingBuilding = true;
            GathererHutAreaDisplay.IsPlacingGathererHutType = (_currentBuild == BuildType.GatherersHut);
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
                    if (mat.HasProperty("_Color"))
                        mat.color = tint;
                }
            }
        }

        private void SpawnSelectedBuilding(float3 pos)
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
            BuildType.Shrine => "TempleOfRidan",
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

                // Mark segment as under construction too
                if (!_em.HasComponent<UnderConstruction>(segment))
                    _em.AddComponentData(segment, new UnderConstruction { Progress = 0f, Total = 5f });
                if (_em.HasComponent<Health>(segment))
                {
                    var hp = _em.GetComponentData<Health>(segment);
                    _em.SetComponentData(segment, new Health { Value = 1, Max = hp.Max });
                }

                AssignBuildersToConstruction(segment, "Alanthor_Wall", pos);
            }

            // Current hub becomes the first hub for the next chain link
            _wallFirstHub = hub;
            _wallFirstHubPos = pos;
            _wallPlacingSecondHub = true;
            _snappedHub = Entity.Null;
        }

        private bool TryGetMouseWorld(out Vector3 world)
        {
            world = default;
            var cam = Camera.main;
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