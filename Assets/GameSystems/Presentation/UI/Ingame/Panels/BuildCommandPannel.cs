// Building placement UI with preview and cost checking

using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.Input;
using TheWaningBorder.Data;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;
using TheWaningBorder.Rendering;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Handles building placement preview and spawning.
    /// Works with EntityActionPanel for UI integration.
    /// </summary>
    public class WorkerCommandPanel : MonoBehaviour
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

        private static WorkerCommandPanel _activeInstance;
        private string _currentBuildId;

        private EntityWorld _world;
        private EntityManager _em;

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] HubSnapQueryTypes =
        {
            ComponentType.ReadOnly<WallHubTag>(),
            ComponentType.ReadOnly<Unity.Transforms.LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _hubSnapQuery;

        [SerializeField] private LayerMask placementMask = ~0;
        [SerializeField] private float yOffset = 0f;

        // Current placement preview
        private GameObject _placingInstance;
        private bool _placementIsPlaceholderCube;

        /// <summary>The ONE Mine button is active: every frame the node under
        /// the cursor picks the concrete extractor (_currentBuildId), and the
        /// ghost follows it (TerritoryOwnership.ResolveExtractorAt).</summary>
        private bool _genericMine;

        /// <summary>The ghost is a procedural visual built at its final size —
        /// not footprint-fitted.</summary>
        private bool _placementIsExactProcedural;

        // Build type
        public enum BuildType
        {
            Hut, GatherersHut, Barracks, ArcheryRange, Vault, Keep, Wall, Temple,
            // Runai culture buildings
            RunaiOutpost, RunaiTradeHub, RunaiBazaar, RunaiSiegeWorkshop,
            // Alanthor culture buildings (PracticeRange retired — it is the
            // leveled Archery Range)
            AlanthorWatchTower, AlanthorSiegeYard, AlanthorRoyalStable,
            // The emplacement pair (docs/Design/Age_1_Alanthor.md).
            AlanthorBallistaEmplacement, AlanthorTrebuchetEmplacement,
            // Feraldis culture buildings
            FeraldisHuntingLodge, FeraldisLoggingStation, FeraldisLonghouse, FeraldisTotemTower, FeraldisSiegeYard,
            FeraldisWarTotem, FeraldisPasture, Mine, VeilstoneMine, AlanthorSawyer,
            // Per-hub "Build Wall" action: anchors a new hub + connecting
            // segment onto an existing wall hub. Placed without a worker;
            // auto-builds in 30 s. Entered via
            // WorkerCommandPanel.TriggerHubBuildWall(sourceHub).
            WallExtend,
            // Any other catalog building. NOT a fallback to some default
            // building — the placed id is _currentBuildId, always.
            Other
        }
        private BuildType _currentBuild = BuildType.Other;

        /// <summary>Why the current ghost is red (None while it is white).</summary>
        private PlacementRefusal _placementRefusal;

        /// <summary>Why the current placement ghost is refused, for any UI
        /// that wants to show it; None when valid or not placing.</summary>
        public static PlacementRefusal CurrentPlacementRefusal =>
            _activeInstance != null && IsPlacingBuilding ? _activeInstance._placementRefusal
                                                         : PlacementRefusal.None;

        // Hub-anchored "Build Wall" placement: the source hub the player
        // selected when invoking the action. The next LMB click drops a new
        // hub at the cursor and a segment connecting it to this anchor.
        // Set by TriggerHubBuildWall, cleared on placement / cancel.
        private Entity _wallExtendSourceHub;

        // Walls are DRAWN (docs/Design/Age_1_Alanthor.md § Drawing walls):
        // press starts a path, drag extends it under a curvature limit,
        // retracing erases, release places every hub as one order. The tool
        // owns the path and its preview; this panel owns the mouse and the
        // commit. _drawStartHub is the friendly hub the path began on (the
        // per-hub Build Wall source, or one under the press), or Null.
        private WallDrawTool _wallDraw;
        /// <summary>The friendly wall cell the current stroke began on (it
        /// becomes a hub when the order lands), or Null.</summary>
        private Entity _drawStartCell;
        /// <summary>World XZ the current stroke began at, after snapping.</summary>
        private Vector2 _drawStartPos;
        private Entity _drawStartHub;

        /// <summary>Self-build timer (seconds) for hubs + instances placed via
        /// the per-hub "Build Wall" action. No worker is dispatched; the
        /// AutoConstructionSystem ticks Progress at 1.0/s.</summary>
        private const float WallExtendBuildSeconds = 30f;

        /// <summary>Build-Wall click within this distance of an existing friendly hub
        /// snaps onto it: reuse the hub and build only the connecting segment (no new
        /// hub, no hub cost). Twice the hub's radius, so a press anywhere on or just
        /// beside the tower snaps to it — DERIVED from the hub, so it shrank with it
        /// on 2026-09-21 instead of staying a stale 6 m that swallowed clicks a whole
        /// tower away. Must stay equal to CommandRouter's WallPathHubSnap, which is
        /// what the executor re-resolves the snap against.</summary>
        private static float HubSnapRadius
            => TheWaningBorder.Entities.AlanthorWall.HubRadius * 2f;

        /// <summary>True while the wall being placed is a Palisade rather than
        /// the Alanthor Stone Wall — two buildings on one tool
        /// (docs/Design/Age_0.md § Palisade). Every snap, cost and order
        /// below asks it, so a palisade never joins stone.</summary>
        private bool PlacingPalisade => _currentBuildId == AlanthorWall.PalisadeHubId;

        /// <summary>The hub id of the wall being placed.</summary>
        private string WallHubId => AlanthorWall.HubIdFor(PlacingPalisade);

        // Placement validity
        private bool _placementValid = true;
        // Which state the preview materials were last configured for; null =
        // not yet (a fresh ghost). Lets UpdatePreviewColor run every frame
        // without re-flipping surface modes and keywords every frame.
        private bool? _previewModeValid;

        // Placement yaw in degrees (mouse-wheel rotation during placement)
        private float _placementYaw;
        private const float YawStepDegrees = 15f;

        // Prefab previews
        private GameObject _prefabGatherersHut;
        private GameObject _prefabHut;
        private GameObject _prefabBarracks;
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
            _prefabTemple = Resources.Load<GameObject>("Prefabs/Buildings/TempleOfRidan");
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
                    // ONE MINE BUTTON: the node under the cursor decides which
                    // extractor this is. A change rebuilds the ghost as that
                    // pithead; with no node in reach it stays "Mine" and the
                    // ghost reads red with the node rule.
                    if (_genericMine)
                    {
                        var mineEm = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
                        string resolved = TheWaningBorder.World.Regions.TerritoryOwnership.ResolveExtractorAt(
                            mineEm, GameSettings.LocalPlayerFaction, (float3)p, out var mineId, out _)
                            ? mineId : "Mine";
                        if (resolved != _currentBuildId)
                        {
                            _currentBuildId = resolved;
                            _currentBuild = BuildTypeFor(resolved);
                            StartPlacement();
                            if (_placingInstance == null) return;
                        }
                    }

                    // Snap the ghost to the 2 m build grid so the player sees
                    // the exact cells the building will take, not a free-float
                    // position that jumps when BuildingFactory snaps it later.
                    // The wall HUB is the exception: it is grid-exempt
                    // (docs/Design/Build_Grid.md § 5), so its ghost follows the
                    // cursor exactly, as the placed hub will.
                    // docs/Design/Build_Grid.md
                    string snapId = _currentBuildId;
                    if (!string.IsNullOrEmpty(snapId))
                    {
                        // An EXTRACTOR snaps to its node first, and to the bare
                        // grid only when no node is in reach — the ghost has to
                        // show the cells the building will actually take, and
                        // CommandRouter applies the identical snap on commit.
                        var snapEm = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld)
                            .EntityManager;
                        if (!TheWaningBorder.World.Regions.TerritoryOwnership.TrySnapToNode(
                                snapEm, snapId, (float3)p, out float3 snapped))
                            snapped = BuildGrid.Snap((float3)p, snapId);

                        // Re-sample terrain height AT the snapped column — the
                        // raycast height belongs to the unsnapped point and can
                        // be metres off on a slope.
                        p = new Vector3(snapped.x,
                                        TerrainUtility.GetHeight(snapped.x, snapped.z),
                                        snapped.z);
                    }

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
                        var buildSize = BuildCommandHelper.GetBuildingSize(_currentBuildId);
                        // ONE evaluation, with its REASON, shared with the
                        // click guard in SpawnSelectedBuilding — the router's
                        // own gates (caps, territory + Hall adjacency, node,
                        // one Hall per territory, worker on site), then the
                        // geometry, then the blood / forest rules.
                        _placementRefusal = EvaluatePlacement(
                            (float3)_placingInstance.transform.position, _currentBuildId,
                            PlacementFaction(_currentBuildId), out _);
                        _placementValid = _placementRefusal == PlacementRefusal.None;
                        UpdatePreviewColor(_placementValid);

                        // Grid marks under the cursor, then the footprint
                        // outline on top. docs/Design/Build_Grid.md
                        BuildGridOverlay.Show((float3)_placingInstance.transform.position);
                        BuildFootprintOutline.Show(
                            (float3)_placingInstance.transform.position,
                            buildSize, _placementValid);
                    }
                    else
                    {
                        BuildGridOverlay.Show((float3)_placingInstance.transform.position);
                        // Wall hubs snap and are footprint-shaped too; they
                        // just skip the AABB validity gate (hub-anchor
                        // proximity gates them instead), so draw as valid.
                        BuildFootprintOutline.Show(
                            (float3)_placingInstance.transform.position,
                            BuildCommandHelper.GetBuildingSize(_currentBuildId), true);
                    }
                }

                // Confirm placement
                bool isWallBuild = _currentBuild == BuildType.Wall || _currentBuild == BuildType.WallExtend;
                if (isWallBuild)
                {
                    UpdateWallDrawing();
                }
                else if (UnityEngine.Input.GetMouseButtonDown(0) && !_placementValid)
                {
                    // Name the rule the red ghost broke, not "invalid placement".
                    PlayerNotificationSystem.Notify(
                        PlacementRefusalText.Of(_placementRefusal, _currentBuildId));
                }
                if (UnityEngine.Input.GetMouseButtonDown(0) && !isWallBuild && _placementValid)
                {
                    var pos = _placingInstance.transform.position;

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
            if (GameSettings.IsSpectating) return;

            var instance = FindFirstObjectByType<WorkerCommandPanel>();
            if (instance == null) return;

            // THE DATA-DRIVEN ID IS WHAT GETS PLACED (2026-09-26). This used to
            // round-trip the id through the BuildType enum and back, and both
            // switches ended in `_ => Hut` — so every id the enum did not name
            // (all five sect buildings: Mending Hall, Muster Yard, Stonehold,
            // Veilworks, Reliquary) placed and PAID FOR a real Hut, skipping
            // the sect cap and the Veilworks crust exception on the way. The
            // enum now only flags the few ids with special handling (walls,
            // preview prefabs); the id itself is carried straight through.
            string id = buildingId;
            if (!IsPlaceableId(id))
            {
                // Refuse loudly. An unknown id is a data bug — never a Hut.
                Debug.LogError($"[WorkerCommandPanel] Refused placement of unknown building id " +
                               $"'{buildingId}' — it is not in the TechCatalog.");
                PlayerNotificationSystem.NotifyError(
                    PlacementRefusalText.Of(PlacementRefusal.UnknownBuilding));
                return;
            }

            instance._genericMine = id == "Mine";
            instance._currentBuildId = id;
            instance._currentBuild = BuildTypeFor(id);

            instance.StartPlacement();
            SuppressClicksThisFrame = true;
        }

        /// <summary>True when the id names a building the catalog knows.</summary>
        private static bool IsPlaceableId(string id)
            => !string.IsNullOrEmpty(id)
               && (TechCatalog.TryGetBuilding(id, out var def) && def != null);

        /// <summary>
        /// The special-handling flag for an id — walls draw, a few buildings
        /// have hand-picked preview prefabs. <see cref="BuildType.Other"/> for
        /// everything else, which is NOT a fallback building: the id is.
        /// </summary>
        private static BuildType BuildTypeFor(string buildingId)
            => buildingId switch
            {
                "Hut" => BuildType.Hut,
                "GatherersHut" => BuildType.GatherersHut,
                "Barracks" => BuildType.Barracks,
                "ArcheryRange" => BuildType.ArcheryRange,
                "TempleOfRidan" => BuildType.Temple,
                "VaultOfAlmierra" => BuildType.Vault,
                "FiendstoneKeep" => BuildType.Keep,
                "Alanthor_Wall" or "Palisade" => BuildType.Wall,
                "VeilstoneMine" => BuildType.VeilstoneMine,
                // Runai culture buildings
                "Runai_Outpost" => BuildType.RunaiOutpost,
                "Runai_TradeHub" => BuildType.RunaiTradeHub,
                "ThessarasBazaar" => BuildType.RunaiBazaar,
                "Runai_SiegeWorkshop" => BuildType.RunaiSiegeWorkshop,
                // Alanthor culture buildings
                "Alanthor_Tower" => BuildType.AlanthorWatchTower,
                "Alanthor_SiegeYard" => BuildType.AlanthorSiegeYard,
                "Alanthor_RoyalStable" => BuildType.AlanthorRoyalStable,
                "Alanthor_BallistaEmplacement" => BuildType.AlanthorBallistaEmplacement,
                "Alanthor_TrebuchetEmplacement" => BuildType.AlanthorTrebuchetEmplacement,
                // Feraldis culture buildings
                "Feraldis_HuntingLodge" => BuildType.FeraldisHuntingLodge,
                "Feraldis_LoggingStation" => BuildType.FeraldisLoggingStation,
                "Feraldis_Longhouse" => BuildType.FeraldisLonghouse,
                "Feraldis_Tower" => BuildType.FeraldisTotemTower,
                "Feraldis_SiegeYard" => BuildType.FeraldisSiegeYard,
                "Feraldis_WarTotem" => BuildType.FeraldisWarTotem,
                "Feraldis_Pasture" => BuildType.FeraldisPasture,
                "Mine" => BuildType.Mine,
                _ => BuildType.Other
            };

        public void StartPlacement()
        {
            CancelPlacement();
            // _currentBuildId was set by the trigger (TriggerBuildingPlacement /
            // TriggerHubBuildWall) and is kept for a shift-click re-entry.
            if (string.IsNullOrEmpty(_currentBuildId)) return;
            _placementIsPlaceholderCube = false;
            _placementIsExactProcedural = false;
            _placementRefusal = PlacementRefusal.None;

            // Culture for the preview: the COMPLETED culture only.
            // FactionColors flips at click time (unit-tint preview), but
            // buildings must not change until the age-up research finishes
            // (AgeUpSystem writes FactionProgress.Culture on completion).
            byte playerCulture = Cultures.None;
            var cultureWorld = _world ?? EntityWorld.DefaultGameObjectInjectionWorld;
            if (cultureWorld != null && cultureWorld.IsCreated)
                playerCulture = CultureConfig.GetCompletedCulture(
                    cultureWorld.EntityManager, GameSettings.LocalPlayerFaction);

            // Get presentation ID for the current build type
            int previewPid = GetPreviewPresentationId(_currentBuild, _currentBuildId);

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
            // The ore extractors preview as the pithead they will be (MineVisual).
            var mineKind = TheWaningBorder.Rendering.MineVisual.KindFor(_currentBuildId);
            if (mineKind != null)
            {
                procPreview = TheWaningBorder.Rendering.MineVisual.Build(0, mineKind.Value);
                _placementIsExactProcedural = true;
            }
            else if (previewPid > 0 && TechCatalog.TryGetPrefab(previewPid, out var soPrev) && soPrev != null)
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
                    // Final fallback: placeholder cube, sized to the actual
                    // footprint rather than a fixed 2 m block so the ghost
                    // reads as the ground the building will take.
                    var fbSize = BuildCommandHelper.GetBuildingSize(_currentBuildId);
                    _placementIsPlaceholderCube = true;
                    _placingInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _placingInstance.transform.localScale =
                        new Vector3(fbSize.x, math.max(fbSize.x, fbSize.y) * 0.5f, fbSize.y);
                    var r = _placingInstance.GetComponent<Renderer>();
                    if (r != null) r.material.color = new Color(0.5f, 0.4f, 0.2f, 0.5f);
                }
            }

            } // end of else (no upgrade-aware prefab found)

            _placingInstance.name = "PlacementPreview";

            if (!_placementIsPlaceholderCube && !_placementIsExactProcedural)
            {
                // Scale the ghost exactly like the real spawn: the runtime
                // multiplies every prefab visual by ComputeFootprintFit, so a
                // preview without it renders up to 4x the finished building.
                // Measured BEFORE the variant setup below so the ghost and the
                // spawn see the same set of active renderers.
                var fitSize = BuildCommandHelper.GetBuildingSize(_currentBuildId);
                float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(
                    _placingInstance, fitSize.x, fitSize.y, out Vector3 fitOffset);
                _placingInstance.transform.localScale *= fit;

                // Stand the ghost on the ground like the real spawn does
                // (ProceduralScaleTag.BaseOffset.y). Applied to the CHILDREN,
                // in root-local units: the root's own position is the
                // placement point that SpawnSelectedBuilding commits.
                if (Mathf.Abs(fitOffset.y) > 0.001f)
                    for (int c = 0; c < _placingInstance.transform.childCount; c++)
                        _placingInstance.transform.GetChild(c).localPosition -= new Vector3(0f, fitOffset.y, 0f);

                // Multi-variant prefabs author every culture branch active; the
                // real spawn hides them via BuildingVariantVisual. Without the
                // same setup the ghost shows Lv0 AND every level stacked.
                var variant = TheWaningBorder.Rendering.BuildingVariantVisual
                    .TrySetup(_placingInstance);
                if (variant != null && playerCulture != Cultures.None)
                    variant.ShowVariant(playerCulture, 1);
            }

            // Disable colliders on preview
            foreach (var col in _placingInstance.GetComponentsInChildren<Collider>())
                col.enabled = false;

            // Blank every preview albedo so the ghost reads as a plain maquette:
            // solid white while placeable, translucent red while not. The surface
            // mode (opaque / transparent) is set by UpdatePreviewColor on the
            // first frame and whenever validity flips.
            foreach (var renderer in _placingInstance.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
                }
            }
            _previewModeValid = null;

            // Reset rotation for fresh placement (mouse wheel adjusts during Update).
            _placementYaw = 0f;

            IsPlacingBuilding = true;
            TheWaningBorder.Core.PresentationState.PlacingBuilding = true;
        }

        /// <summary>
        /// Reconfigure a URP Lit/Unlit material clone to render in Transparent
        /// surface mode so per-frame `_BaseColor` alpha values actually blend.
        /// Safe no-op for non-URP shaders that don't have these properties.
        /// </summary>
        /// <summary>Undo <see cref="MakeMaterialTransparent"/>: URP Opaque surface, depth write on.</summary>
        private static void MakeMaterialOpaque(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f); // 0 = Opaque
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

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
            TheWaningBorder.Core.PresentationState.PlacingBuilding = false;
            BuildFootprintOutline.Hide();
            BuildGridOverlay.Hide();
            if (_wallDraw != null) _wallDraw.Clear();
            _drawStartHub = Entity.Null;
            _drawStartCell = Entity.Null;

            // Reset hub-anchored placement state (per-hub Build Wall action).
            _wallExtendSourceHub = Entity.Null;
        }

        private void CancelPlacementPreviewOnly()
        {
            if (_placingInstance != null) Destroy(_placingInstance);
            _placingInstance = null;
            IsPlacingBuilding = false;
            TheWaningBorder.Core.PresentationState.PlacingBuilding = false;
            BuildFootprintOutline.Hide();
            BuildGridOverlay.Hide();
            if (_wallDraw != null) _wallDraw.Clear();
            _drawStartHub = Entity.Null;
        }

        // ── Drawing a wall ────────────────────────────────────────────────

        private WallDrawTool WallDraw
        {
            get
            {
                if (_wallDraw == null)
                    _wallDraw = new GameObject("WallDrawTool").AddComponent<WallDrawTool>();
                return _wallDraw;
            }
        }

        /// <summary>
        /// One frame of the wall interaction. Press: start the path at the
        /// cursor — or at a friendly hub under it (and the per-hub Build Wall
        /// source hub always). Hold: extend, recompute hub ghosts, preview.
        /// Release: commit the whole path as one PlaceWallPath order; a press
        /// with no drag is the old single hub.
        /// </summary>
        private void UpdateWallDrawing()
        {
            var tool = WallDraw;
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
            var fac = GetSelectedFactionOrDefault();

            if (UnityEngine.Input.GetMouseButtonDown(0) && TryGetMouseWorld(out Vector3 down))
            {
                // The path starts on a friendly hub under the cursor (or the
                // per-hub Build Wall source hub), else on a friendly wall CELL
                // under it — which becomes a hub when the order lands, so a
                // wall can branch off a standing wall (T / X) — else on the
                // ground.
                _drawStartHub = _currentBuild == BuildType.WallExtend && _wallExtendSourceHub != Entity.Null
                    && _em.Exists(_wallExtendSourceHub)
                    ? _wallExtendSourceHub
                    : FindNearestHubForSnap((float3)down, fac, Entity.Null);
                _drawStartCell = _drawStartHub == Entity.Null
                    ? FindNearestCellForSnap((float3)down, fac)
                    : Entity.Null;
                Entity anchor = _drawStartHub != Entity.Null ? _drawStartHub : _drawStartCell;
                Vector2 start = anchor != Entity.Null
                    ? new Vector2(_em.GetComponentData<Unity.Transforms.LocalTransform>(anchor).Position.x,
                                  _em.GetComponentData<Unity.Transforms.LocalTransform>(anchor).Position.z)
                    : new Vector2(down.x, down.z);
                _drawStartPos = start;
                tool.Begin(start);
                if (_placingInstance != null) _placingInstance.SetActive(false);
                SuppressClicksThisFrame = true;
            }

            if (!tool.Drawing) return;

            if (UnityEngine.Input.GetMouseButton(0) && TryGetMouseWorld(out Vector3 cur))
                tool.Extend(new Vector2(cur.x, cur.z));

            bool startsOnHub = _drawStartHub != Entity.Null;
            var startKind = startsOnHub ? CommandRouter.WallPathKind.ExistingHub
                          : _drawStartCell != Entity.Null ? CommandRouter.WallPathKind.CellHub
                          : CommandRouter.WallPathKind.NewHub;
            // The stroke's end snaps onto a friendly hub (not the one it started
            // from unless the stroke is long enough to be a loop) — that is how
            // a wall closes or joins an older one — or onto a friendly wall
            // cell, which becomes a hub: a T-junction into a standing wall.
            // Tested at the PATH's end, not the cursor's: the snap belongs to
            // what was drawn.
            Entity endHub = Entity.Null, endCell = Entity.Null;
            float3? endSnap = null;
            bool closesOnOwnStart = false;
            // Closing into a loop is only ever the deliberate case: the end is
            // brought back within snap reach of the start AND the stroke is
            // long enough to be a legal loop at all (a circle of the minimum
            // bend radius). A shorter U or arc ending near its start is an
            // OPEN end -- previewed as such, and refused on release with a
            // reason if its end hub would sit on the start -- never a ring.
            // The start then shows a distinct close ring while a release
            // would close (WallDrawTool.ClosesLoop).
            float loopMin = Mathf.Max(3f * HubSnapRadius, tool.MinLoopLength);
            bool endNearStart = false;
            if (tool.PointCount >= 2)
            {
                Vector2 endXZ = tool.EndXZ;
                var endWorld = new float3(endXZ.x, 0f, endXZ.y);
                float ex = endXZ.x - _drawStartPos.x, ez = endXZ.y - _drawStartPos.y;
                endNearStart = ex * ex + ez * ez < HubSnapRadius * HubSnapRadius;
                endHub = FindNearestHubForSnap(endWorld, fac, Entity.Null);
                if (endHub == _drawStartHub && tool.Length < loopMin) endHub = Entity.Null;
                if (endHub == Entity.Null)
                {
                    endCell = FindNearestCellForSnap(endWorld, fac);
                    // Not the cell it started on, and not so close to the
                    // start that two hubs would overlap.
                    if (endCell == _drawStartCell) endCell = Entity.Null;
                    if (endCell != Entity.Null)
                    {
                        var cp = _em.GetComponentData<Unity.Transforms.LocalTransform>(endCell).Position;
                        float dx = cp.x - _drawStartPos.x, dz = cp.z - _drawStartPos.y;
                        if (dx * dx + dz * dz < AlanthorWall.HubWidth * AlanthorWall.HubWidth) endCell = Entity.Null;
                    }
                }
                Entity endAnchor = endHub != Entity.Null ? endHub : endCell;
                if (endAnchor != Entity.Null)
                    endSnap = _em.GetComponentData<Unity.Transforms.LocalTransform>(endAnchor).Position;
                else if (!startsOnHub && tool.Length >= loopMin)
                {
                    // A loop back onto a start that is not a hub YET (a new
                    // hub, or a cell this order converts): the end is that
                    // start. The executor raises the start hub first, so by
                    // the time it reaches the end, FindWallHubNear finds it.
                    if (endNearStart)
                    {
                        closesOnOwnStart = true;
                        endSnap = new float3(_drawStartPos.x,
                            TerrainUtility.GetHeight(_drawStartPos.x, _drawStartPos.y),
                            _drawStartPos.y);
                    }
                }
            }
            var endKind = endHub != Entity.Null || closesOnOwnStart ? CommandRouter.WallPathKind.ExistingHub
                        : endCell != Entity.Null ? CommandRouter.WallPathKind.CellHub
                        : CommandRouter.WallPathKind.NewHub;
            // An open end left on top of the start (too short to close) would
            // raise two overlapping hubs: that end hub is not legal. The
            // start itself (distance 0) is exempt.
            bool openEndOnStart = endNearStart && endSnap == null && tool.Length >= 3f * HubSnapRadius;
            Vector2 drawStart = _drawStartPos;
            tool.ComputeLayout(startKind, endKind, endSnap, p =>
            {
                if (openEndOnStart)
                {
                    float dx = p.x - drawStart.x, dz = p.z - drawStart.y;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > 0.01f && d2 < HubSnapRadius * HubSnapRadius) return false;
                }
                return BuildCommandHelper.IsValidBuildPosition(_em, p,
                        BuildCommandHelper.GetBuildingSize(WallHubId), WallHubId)
                    && MeetsTerritoryRequirement(fac, p, WallHubId);
            });
            // The whole length must be clear — buildings, obstacles, other
            // walls, impassable ground — not just the hub spots. Where the
            // stroke joins a standing hub or cell it may touch that wall.
            // Same cross-section test the executor applies
            // (CommandRouter.WallLineClear); docs/Design/Build_Grid.md.
            {
                var joints = new System.Collections.Generic.List<float3>(2);
                Entity startAnchor = _drawStartHub != Entity.Null ? _drawStartHub : _drawStartCell;
                if (startAnchor != Entity.Null && _em.Exists(startAnchor))
                    joints.Add(_em.GetComponentData<Unity.Transforms.LocalTransform>(startAnchor).Position);
                if (endSnap.HasValue) joints.Add(endSnap.Value);
                float jr = CommandRouter.WallJunctionClearance;
                bool pal = PlacingPalisade;
                tool.MarkBlocked((p, tan) =>
                {
                    for (int j = 0; j < joints.Count; j++)
                        if (math.distancesq(p.xz, joints[j].xz) < jr * jr) return false;
                    return !CommandRouter.WallCrossSectionClear(p, tan, pal);
                });
            }
            tool.ShowPreview();

            if (!UnityEngine.Input.GetMouseButtonUp(0)) return;

            tool.End();
            int newHubs = tool.NewHubCount;
            if (tool.Points.Count == 0 || (newHubs == 0 && tool.PointCount < 2))
            {
                // Pressed on a hub and released without drawing: nothing to place.
                CancelPlacementPreviewOnly();
                return;
            }
            if (openEndOnStart)
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Wall is too short to close into a loop"));
                CancelPlacementPreviewOnly();
                return;
            }
            if (tool.Problem != WallDrawTool.PathProblem.None)
            {
                PlayerNotificationSystem.NotifyError(Loc.T(
                    tool.Problem == WallDrawTool.PathProblem.TooTight ? "Wall bends too sharply"
                    : tool.Problem == WallDrawTool.PathProblem.Blocked ? "Wall runs into something in its way"
                    : "Wall runs back over itself"));
                CancelPlacementPreviewOnly();
                return;
            }
            if (!tool.AllHubsValid)
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Wall crosses ground you cannot build on"));
                CancelPlacementPreviewOnly();
                return;
            }
            // The executor refuses a wall that attaches to a standing hub or
            // cell while a wall level researches; say so instead of letting
            // the order vanish.
            bool touchesStanding = _drawStartHub != Entity.Null || _drawStartCell != Entity.Null
                || endHub != Entity.Null || endCell != Entity.Null;
            if (touchesStanding && CommandRouter.WallsLockedForUpgrade(_em, fac))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Walls are being upgraded"));
                CancelPlacementPreviewOnly();
                return;
            }
            // The executor refuses a wall that leaves its owner's ground at any
            // point (CommandRouter.WallLineOnOwnGround) — name the rule here.
            if (!CommandRouter.WallLineOnOwnGround(_em, fac, tool.Points))
            {
                PlayerNotificationSystem.NotifyError(TerritoryRefusal(WallHubId));
                CancelPlacementPreviewOnly();
                return;
            }
            // Hubs AND every curtain module — the executor's own price
            // (CommandRouter.WallPathCost; walls are paid per module).
            var total = CommandRouter.WallPathCost(tool.Points, tool.Kinds, PlacingPalisade);
            if (!FactionEconomy.CanAfford(_em, fac, total))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                CancelPlacementPreviewOnly();
                return;
            }

            var pts = new System.Collections.Generic.List<float3>(tool.Points);
            var kinds = new System.Collections.Generic.List<CommandRouter.WallPathKind>(tool.Kinds);
            CommandRouter.IssuePlaceWallPath(_em, pts, kinds, fac, palisade: PlacingPalisade);

            // A lone first hub is worker-built (the executor keeps that
            // behaviour); send the selected workers to it as before.
            if (!startsOnHub && _drawStartCell == Entity.Null && pts.Count == 1)
            {
                var sel = SelectionSystem.CurrentSelection;
                if (sel != null)
                    foreach (var b in sel)
                        if (_em.Exists(b) && _em.HasComponent<CanBuild>(b))
                            CommandRouter.IssueBuild(_em, b, Entity.Null, WallHubId, pts[0]);
            }
            SuppressClicksThisFrame = true;
            CancelPlacementPreviewOnly();
        }

        private void UpdatePreviewColor(bool valid)
        {
            if (_placingInstance == null) return;
            if (_previewModeValid == valid) return; // materials already hold this state
            _previewModeValid = valid;

            // Placeable: solid opaque white. Blocked: translucent red.
            Color tint = valid
                ? Color.white
                : new Color(1f, 0.3f, 0.3f, 0.5f);
            foreach (var renderer in _placingInstance.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                {
                    if (valid) MakeMaterialOpaque(mat);
                    else       MakeMaterialTransparent(mat);

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

            var id = _currentBuildId;
            if (!IsPlaceableId(id))
            {
                Debug.LogError($"[WorkerCommandPanel] Refused placement of unknown building id '{id}'.");
                PlayerNotificationSystem.NotifyError(
                    PlacementRefusalText.Of(PlacementRefusal.UnknownBuilding));
                return;
            }

            var fac = PlacementFaction(id);

            // Block trading post if faction already has 10
            if (id == "Runai_TradingPost")
            {
                int tpCount = BuildingFactory.GetFactionBuildingCount<TradingPostTag>(_em, fac);
                if (tpCount >= 10)
                {
                    PlayerNotificationSystem.Notify(Loc.T("Maximum 10 Trading Posts"));
                    return;
                }
            }

            // Per-faction caps from the SO (Houses: 20).
            if (BuildingFactory.AtFactionCap(_em, fac, id))
            {
                PlayerNotificationSystem.Notify(string.Format(Loc.T("Limit reached: {0} per faction"),
                    TechCatalog.Building(id).maxPerFaction));
                return;
            }

            // Block additional Temples of Ridan — only one per faction.
            // Counts both completed and under-construction Temples so a
            // double-click during a 50 s build can't sneak a second order in.
            if (id == "TempleOfRidan")
            {
                int templeCount = BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(_em, fac);
                if (templeCount >= 1)
                {
                    PlayerNotificationSystem.Notify(Loc.T("Only one Temple of Ridan per faction"));
                    return;
                }
            }

            // Block choice building if faction already has one
            if (BuildingFactory.IsChoiceBuilding(id))
            {
                var existing = BuildingFactory.GetFactionChoiceBuilding(_em, fac);
                if (existing != null)
                {
                    PlayerNotificationSystem.Notify(Loc.T("Already have a choice building"));
                    return;
                }
            }

            // THE SAME EVALUATION THE GHOST RAN, re-asked at the click so a
            // stale frame cannot slip one through: the router's gates (caps,
            // territory + the Hall's adjacency rule, the extractor node, one
            // Hall per territory, a worker on site for a Hall), the geometry,
            // and the blood / forest rules — each refusal named. Extractors
            // come back snapped onto their node, the position the router
            // queues.
            var refusal = EvaluatePlacement(pos, id, fac, out Entity hallWorker, out pos);
            if (refusal != PlacementRefusal.None)
            {
                PlayerNotificationSystem.NotifyError(PlacementRefusalText.Of(refusal, id));
                return;
            }

            // The price the executor will charge THIS faction — the Hall's
            // escalation (Regions.md §2), Deep Foundations and, for a Trading
            // Outpost, its outcrop's cost ramp (priced at the snapped site).
            var cost = BuildCosts.For(_em, fac, id, pos);

            // Affordability CHECK only — the SPEND lives in
            // CommandRouter.PlaceBuildingDirect, the executor both the
            // single-player branch below and every lockstep peer run, so all
            // banks (and the desync checksum built from them) stay aligned
            // (docs/Multiplayer_LAN_Readiness.md).
            if (!FactionEconomy.CanAfford(_em, fac, cost))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                return;
            }

            if (GameSettings.UsesLockstep)
            {
                // Lockstep (multiplayer AND single-player): queue via lockstep — building created on all
                // clients at same tick. A Hall carries the worker standing at
                // its site; the executor re-checks that worker at that tick.
                CommandRouter.IssuePlaceBuilding(_em, id, pos, fac, hallWorker, out _,
                    CommandSource.LocalPlayer, yawDegrees);

                // Send selected workers to the build position — the building entity doesn't
                // exist yet (created 2 ticks later), so we issue Build with Entity.Null target.
                // BuildCommandHelper handles null target by moving to position and auto-finding
                // the nearest UnderConstruction building when the worker arrives.
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

            // Single player: create building directly and assign workers.
            // PlaceBuildingDirect re-checks a claim and spends; Entity.Null
            // means a claim rule refused it (LastPlacementRefusal says which)
            // or the bank came up short between the CanAfford check and now.
            Entity building = CommandRouter.PlaceBuildingDirect(_em, id, pos, fac, hallWorker,
                CommandRouter.YawFromWire(CommandRouter.YawToWire(yawDegrees)));
            if (building == Entity.Null)
            {
                var why = CommandRouter.LastPlacementRefusal;
                PlayerNotificationSystem.NotifyError(why != PlacementRefusal.None
                    ? PlacementRefusalText.Of(why, id)
                    : Loc.T("Not enough resources"));
                return;
            }

            // The mouse-wheel rotation is applied by PlaceBuildingDirect itself
            // (the executor), so single player and lockstep agree.

            // Flatten the terrain under the building footprint so the model
            // sits on level ground regardless of the (≤15°) underlying slope.
            var sizeForFlatten = BuildCommandHelper.GetBuildingSize(id);
            float halfExtent = math.max(sizeForFlatten.x, sizeForFlatten.y) * 0.5f;
            var pt = TheWaningBorder.World.Terrain.ProceduralTerrain.Instance;
            if (pt != null) pt.FlattenAt(new Vector3(pos.x, 0f, pos.z), halfExtent);

            AssignWorkersToConstruction(building, id, pos);
        }

        /// <summary>
        /// Assigns selected worker units to construct the given building.
        /// </summary>
        private void AssignWorkersToConstruction(Entity building, string buildingId, float3 pos)
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

        /// <summary>
        /// The faction a placement of <paramref name="buildingId"/> belongs
        /// to. Choice buildings are placed from the top-bar buttons, which can
        /// be clicked with anything (or nothing) selected — the selection-
        /// derived faction is not trustworthy for them. They always belong to
        /// the local player.
        /// </summary>
        private Faction PlacementFaction(string buildingId)
            => BuildingFactory.IsChoiceBuilding(buildingId)
                ? GameSettings.LocalPlayerFaction
                : GetSelectedFactionOrDefault();

        private PlacementRefusal EvaluatePlacement(float3 pos, string buildingId, Faction fac,
            out Entity hallWorker)
            => EvaluatePlacement(pos, buildingId, fac, out hallWorker, out _);

        /// <summary>
        /// Every rule a placement answers to, in one place, with the REASON
        /// the first failing one refused — the ghost's colour, the click's
        /// refusal notice and the command all come from this.
        ///
        /// 1. The router's own gates (<see cref="CommandRouter.CheckPlaceBuilding"/>):
        ///    per-faction caps, the territory gate with the Hall's ADJACENCY
        ///    rule, the extractor node, one Hall per territory, and — for a
        ///    Hall — one of the selected workers within
        ///    <see cref="TerritoryOwnership.HallWorkerRange"/> of the site.
        ///    Asked, not copied, so the ghost cannot go white on a click the
        ///    router refuses.
        /// 2. The geometry (crust, terrain, overlap).
        /// 3. The ghost-only terrain rules: blood for a War Totem, a forest
        ///    for a Sawyer.
        ///
        /// <paramref name="hallWorker"/> is the worker the Hall command
        /// will carry (Entity.Null for anything else); <paramref name="placedPos"/>
        /// is the position the router will queue (an extractor snapped onto
        /// its node).
        /// </summary>
        private PlacementRefusal EvaluatePlacement(float3 pos, string buildingId, Faction fac,
            out Entity hallWorker, out float3 placedPos)
        {
            hallWorker = Entity.Null;
            placedPos = pos;
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return PlacementRefusal.None;
            var em = w.EntityManager;
            if (!IsPlaceableId(buildingId)) return PlacementRefusal.UnknownBuilding;

            if (TerritoryOwnership.NeedsWorkerNearby(buildingId))
                hallWorker = TerritoryOwnership.NearestWorker(
                    em, fac, SelectionSystem.CurrentSelection, pos.x, pos.z);

            var r = CommandRouter.CheckPlaceBuilding(em, buildingId, ref placedPos, fac, hallWorker);
            if (r != PlacementRefusal.None) return r;

            // The id goes in so the crust rule can make its one exception:
            // Veilworks (Reclamation) is the only building that may be raised
            // on cursed ground.
            r = BuildCommandHelper.CheckBuildPosition(em, placedPos,
                BuildCommandHelper.GetBuildingSize(buildingId), buildingId);
            if (r != PlacementRefusal.None) return r;

            if (!MeetsBloodRequirement(placedPos, buildingId)) return PlacementRefusal.NotOnBlood;
            if (!MeetsPatchRequirement(em, placedPos, buildingId)) return PlacementRefusal.NotByForest;
            return PlacementRefusal.None;
        }

        /// <summary>
        /// Alanthor factions may only place buildings inside their own
        /// influence border — own channel ≥ 0.5 on the influence map
        /// (docs/Design/Overview.md § The influence map). Every other
        /// culture (and pre-culture Age 0) is unrestricted.
        /// Exempt: Gatherer's Huts (grant no influence, harvest anywhere)
        /// and Watch Towers (forward claims — they PROJECT influence into
        /// new ground).
        /// </summary>
        private static bool MeetsTerritoryRequirement(Faction fac, float3 pos, string buildingId)
        {
            var w = EntityWorld.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return true;
            return TheWaningBorder.World.Regions.TerritoryOwnership.CanBuildAt(
                w.EntityManager, fac, buildingId, pos.x, pos.z);
        }

        /// <summary>The refusal to show for a blocked placement — a claim
        /// structure fails for a different reason than an ordinary building
        /// (someone else holds this ground, rather than you do not).</summary>
        private static string TerritoryRefusal(string buildingId) =>
            TheWaningBorder.World.Regions.TerritoryOwnership.IsClaimStructure(buildingId)
                ? Loc.T("Cannot claim ground another player holds")
                : Loc.T("You can only build in your own territory");

        /// <summary>
        /// Feraldis War Totems may only be planted ON BLOOD — the culture
        /// claims ground it has bled on (docs/Design/Age_1_Feraldis.md).
        /// Every other building, and every other culture, is unaffected.
        /// </summary>
        private static bool MeetsBloodRequirement(float3 pos, string buildingId)
        {
            if (buildingId != "Feraldis_WarTotem") return true;
            return TheWaningBorder.Influence.BloodMap.SampleWorld(pos.x, pos.z)
                >= TheWaningBorder.Core.Config.FeraldisConstants.TotemPlacementBloodThreshold;
        }

        /// <summary>How far from a forest's EDGE a Sawyer may stand.</summary>
        private const float SawyerForestReach = 14f;

        /// <summary>
        /// Placement rules that are about NEARBY TERRAIN rather than about a
        /// node — today just the Sawyer, which has to stand against a forest.
        /// The extractors left here when their rule became "stand on a free
        /// node of your own kind", which TerritoryOwnership owns.
        /// </summary>
        private static bool MeetsPatchRequirement(EntityManager em, float3 pos, string buildingId)
        {
            // (The Sawyer and its forest rule are gone with the forest
            // resource, 2026-10-01.)

            // The Mine's own patch check USED to live here, over an 18 m
            // radius that accepted an iron node OR a veilstone one. Both halves
            // were wrong: the iron Mine and the Veilstone Mine are separate
            // buildings wanting separate nodes, and the router only ever
            // accepted a node within SupplyNodeSnapRange. Extractors are gated
            // by TerritoryOwnership.OnFreeNodeFor at the call site now, which
            // is the same rule the router applies.
            return true;
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
            // Barracks / Hut participate in the upgrade system. GatherersHut
            // uses a single prefab regardless of culture (no _al_1, no _ru_1 etc.) —
            // we route it through here too so the placement preview matches the
            // real spawn instead of falling back to the procedural model.
            string baseName = bt switch
            {
                BuildType.Hut          => "Hut",
                BuildType.Barracks     => "Barracks",
                BuildType.GatherersHut => "GatherersHut",
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
        /// The PresentationId to preview a building with. Read from the
        /// building's own recipe (BuildingFactory.GetPresentationId), the same
        /// number the real spawn uses — the old per-BuildType table ended in
        /// `_ => 102`, so every building it did not list (sect buildings, the
        /// Temple) previewed as a Hut. Walls are procedural and answer 0.
        /// </summary>
        private static int GetPreviewPresentationId(BuildType t, string buildingId) => t switch
        {
            BuildType.Wall => 0,       // Procedural wall handled separately
            BuildType.WallExtend => 0, // Same procedural hub mesh as Wall
            _ => BuildingFactory.GetPresentationId(buildingId),
        };

        // task-109: Alanthor wall primitives — only Alanthor_Wall (hub) and Alanthor_Tower
        //           (standalone watch tower) are placeable. Alanthor_WallTower and
        //           Alanthor_WallGate are conversion-only (segment selection → Convert
        //           to Tower / Convert to Gate). See docs/Design/Age_1_Alanthor.md
        //           § Wall System (BFME2 hub-and-segment) and the static-ctor
        //           Debug.Assert guard in EntityExtractors.cs / EntityActionExtractor.
        /// <summary>
        /// Place the FIRST wall hub. Standard worker-driven construction (5s).
        /// No chaining — subsequent hubs use the per-hub Build Wall action
        /// (TriggerHubBuildWall / SpawnExtendedWallHub) which auto-connects
        /// with a segment and self-builds in 30s without a worker.
        /// </summary>
        private void SpawnFirstWallHub(float3 pos)
        {
            _em = (_world ?? EntityWorld.DefaultGameObjectInjectionWorld).EntityManager;
            var fac = GetSelectedFactionOrDefault();

            // The territorial rule applies to wall hubs too — a wall fortifies
            // ground you hold, it does not take new ground.
            if (!MeetsTerritoryRequirement(fac, pos, WallHubId))
            {
                PlayerNotificationSystem.NotifyError(TerritoryRefusal(WallHubId));
                return;
            }

            // Affordability CHECK only — the SPEND and the hub creation live
            // in CommandRouter.PlaceWallHubDirect, which every peer executes.
            // The old body created the hub straight from this click handler,
            // so in MP the wall existed on this machine alone AND its off-tick
            // NetworkId consumption shifted every later id assigned that tick.
            // docs/Multiplayer_Desync_Sweep_2026-08-16.md
            if (!BuildCosts.TryGet(WallHubId, out var cost)) cost = default;
            if (!FactionEconomy.CanAfford(_em, fac, cost))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                return;
            }

            if (GameSettings.UsesLockstep)
            {
                CommandRouter.IssuePlaceWallHub(_em, pos, fac, palisade: PlacingPalisade);

                // Workers head for the position now; the hub entity is
                // created two ticks later, so Build rides Entity.Null and
                // auto-finds the foundation on arrival — same pattern as
                // ordinary MP placement above.
                var sel = SelectionSystem.CurrentSelection;
                if (sel != null)
                {
                    foreach (var b in sel)
                    {
                        if (!_em.Exists(b) || !_em.HasComponent<CanBuild>(b)) continue;
                        CommandRouter.IssueBuild(_em, b, Entity.Null, WallHubId, pos);
                    }
                }
                return;
            }

            Entity hub = CommandRouter.PlaceWallHubDirect(_em, pos, fac, palisade: PlacingPalisade);
            if (hub == Entity.Null)
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                return;
            }

            AssignWorkersToConstruction(hub, WallHubId, pos);
        }

        /// <summary>
        /// Per-hub "Build Wall" placement: drop a new wall hub at <paramref name="pos"/>
        /// AND a connecting segment back to <see cref="_wallExtendSourceHub"/>.
        /// The new hub + every wall instance along the segment are tagged
        /// <see cref="AutoConstructTag"/> and self-build in 30s — no worker
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
                PlayerNotificationSystem.NotifyError(Loc.T("Source hub no longer exists"));
                _wallExtendSourceHub = Entity.Null;
                return;
            }

            // Snap: a click near an existing friendly hub (other than the source)
            // reuses that hub and builds ONLY the connecting segment — no new hub,
            // no hub cost. Otherwise place + pay for a new self-building hub.
            // The checks below are issue-side UX; the SPEND and every entity
            // creation live in CommandRouter.WallExtendDirect, which every
            // peer executes (docs/Multiplayer_Desync_Sweep_2026-08-16.md).
            Entity hub = FindNearestHubForSnap(pos, fac, _wallExtendSourceHub);
            if (hub != Entity.Null)
            {
                if (AlanthorWall.AreHubsConnected(_em, _wallExtendSourceHub, hub))
                {
                    PlayerNotificationSystem.NotifyError(Loc.T("Those hubs are already connected"));
                    _wallExtendSourceHub = Entity.Null;
                    return;
                }
            }
            else
            {
                // Territorial rule — the NEW hub must sit in territory you
                // hold, same as the first one.
                if (!MeetsTerritoryRequirement(fac, pos, WallHubId))
                {
                    PlayerNotificationSystem.NotifyError(TerritoryRefusal(WallHubId));
                    _wallExtendSourceHub = Entity.Null;
                    return;
                }

                if (!BuildCosts.TryGet(WallHubId, out var cost)) cost = default;
                var srcPos = _em.GetComponentData<Unity.Transforms.LocalTransform>(_wallExtendSourceHub).Position;
                cost = cost + CommandRouter.WallRunCost(PlacingPalisade, math.distance(srcPos.xz, pos.xz));
                if (!FactionEconomy.CanAfford(_em, fac, cost))
                {
                    PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                    return;
                }
            }

            CommandRouter.IssueWallExtend(_em, _wallExtendSourceHub, hub, pos, fac);

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
            var q = _hubSnapQuery.Get(_em, HubSnapQueryTypes);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            float bestSq = HubSnapRadius * HubSnapRadius;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (e == exclude) continue;
                if (_em.GetComponentData<FactionTag>(e).Value != fac) continue;
                // A palisade never snaps to a stone hub, nor the reverse.
                if (_em.HasComponent<PalisadeTag>(e) != PlacingPalisade) continue;
                var hpos = _em.GetComponentData<Unity.Transforms.LocalTransform>(e).Position;
                float dx = pos.x - hpos.x, dz = pos.z - hpos.z;
                float d = dx * dx + dz * dz;
                if (d < bestSq) { bestSq = d; best = e; }
            }
            return best;
        }

        /// <summary>A press within this distance of a friendly wall cell
        /// starts (or ends) the stroke ON that cell, which becomes a hub.
        /// One module: the cell's own reach.</summary>
        private const float CellSnapRadius = AlanthorWall.InstanceSpacing;

        /// <summary>
        /// Nearest friendly wall cell to <paramref name="pos"/> within
        /// <see cref="CellSnapRadius"/> that can become a hub (plain,
        /// finished, not a gate or tower). Null when none.
        /// </summary>
        private Entity FindNearestCellForSnap(float3 pos, Faction fac)
            => CommandRouter.FindWallCellNear(_em, pos, fac, CellSnapRadius, PlacingPalisade);

        /// <summary>
        /// Enter hub-anchored placement mode for the per-hub "Build Wall"
        /// action. The next LMB click drops a new hub at the cursor plus a
        /// segment connecting back to <paramref name="sourceHub"/>; both
        /// self-build in 30s. Called by HUD action buttons when the button
        /// fires on a selected wall hub.
        /// </summary>
        public static void TriggerHubBuildWall(Entity sourceHub)
        {
            if (GameSettings.IsSpectating) return;
            var instance = FindFirstObjectByType<WorkerCommandPanel>();
            if (instance == null) return;

            // Order matters: StartPlacement() calls CancelPlacement(), which resets
            // _wallExtendSourceHub to Entity.Null. Set the anchor AFTER so it
            // survives — otherwise the click commit sees a null source and bails
            // ("Source hub no longer exists"), clearing the preview without building.
            instance._currentBuild = BuildType.WallExtend;
            // Per-hub Build Wall extends the hub's OWN kind of wall.
            var hubEm = EntityWorld.DefaultGameObjectInjectionWorld.EntityManager;
            instance._currentBuildId = AlanthorWall.HubIdFor(AlanthorWall.IsPalisade(hubEm, sourceHub));
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