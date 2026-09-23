// MinimapPanelBinder.cs
// Fills the authored Minimap panel's "Map" Image with a translucent
// elevation image generated at runtime from the loaded map's baked Unity
// Terrain (maps are hand-authored — there is no stored minimap art, so the
// image is rebuilt per map from whatever terrain the scene ships).
//
// The image is a hillshaded hypsometric ramp (mossy lowland to pale
// summit, lit from the north-west) whose alpha scales with height but
// never reaches zero — low ground stays readable against the frame's
// backdrop. Inclines at or past the gameplay walkability limit
// (PassabilityGrid.MaxWalkableSlope) render as near-opaque dark cliff
// rock, so impassable terrain reads at a glance. Pixels outside the
// terrain tiles (non-rectangular multi-tile unions) stay fully
// transparent.
//
// Heights are pulled per tile with TerrainData.GetHeights (one native call
// per tile, not per pixel) and the colorize pass is timesliced across
// frames so map load never hitches.
//
// On top of the terrain image sit the live layers, ported from the retired
// MinimapRenderer (Assets/Scripts/World/Minimap, deleted with the old UI
// stacks):
// - FoW dimming + entity blips, drawn into one RawImage overlay at 10 Hz:
//   faction-colored units (enemies only while visible), buildings (visible
//   solid / revealed ghost), rocks, iron deposits, veilstone outcroppings,
//   the veilsteel node, ritual markers and glow pickups (both fog-ignorant
//   by spec).
// - The main camera's ground footprint as a white 4-line rectangle,
//   updated every frame.
// - Clicks on the map: left snaps the camera, right issues move orders to
//   the selected own units. The overlay is the raycast target, so hovering
//   the minimap also reads as pointer-over-UI for the world-input guards.

using System.Collections;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class MinimapPanelBinder : MonoBehaviour
    {
        // Longest texture edge; the short edge follows the terrain's world
        // aspect so the Image's preserveAspect letterboxes instead of
        // stretching non-square maps.
        private const int MaxTextureSize = 512;

        private const int RowsPerFrame = 64;

        // Alpha ramps with height but never hits zero, so lowlands still
        // read against the frame's dark backdrop; cliffs are near-opaque.
        private const float LowAlpha   = 0.62f;
        private const float HighAlpha  = 0.88f;
        private const float CliffAlpha = 0.96f;

        // Hypsometric ramp: mossy lowland -> dry tan -> pale summit.
        // CliffColor is the dark rock that steep inclines blend toward.
        private static readonly Color LowColor   = new Color(0.24f, 0.38f, 0.27f);
        private static readonly Color MidColor   = new Color(0.60f, 0.52f, 0.36f);
        private static readonly Color HighColor  = new Color(0.88f, 0.84f, 0.71f);
        private static readonly Color CliffColor = new Color(0.21f, 0.13f, 0.10f);

        // North-west sun for the hillshade.
        private static readonly Vector3 LightDir =
            new Vector3(-0.55f, 0.7f, 0.55f).normalized;

        // ── Live layers (ported from the retired MinimapRenderer) ──────────

        private const float OverlayRefreshInterval = 0.1f;
        private const int UnitRadiusPx = 2;
        private const int BuildingRadiusPx = 3;
        private const float ViewLineThickness = 3f;

        // Fog is sampled once per FogSampleStride overlay pixels, blurred,
        // and bilinearly upsampled — see RefreshFog.
        private const int FogSampleStride = 4;
        private const float RevealedFogAlpha = 0.5f;
        private const float HiddenFogAlpha = 1f;   // unexplored = solid black

        private static readonly Color32 ClearPixel   = new Color32(0, 0, 0, 0);
        private static readonly Color32 RockBlip     = new Color32(97, 92, 84, 255);
        private static readonly Color32 IronBlip     = new Color32(140, 82, 38, 255);
        private static readonly Color32 VeilstoneBlip = new Color32(140, 64, 217, 255);
        private static readonly Color32 VeilsteelBlip = new Color32(199, 184, 235, 255);
        private static readonly Color32 ShardrootBlip     = new Color32(255, 217, 77, 255);
        private static readonly Color32 RitualConversionBlip = new Color32(115, 255, 140, 255);
        private static readonly Color32 RitualExtractionBlip = new Color32(255, 115, 51, 255);
        private static readonly Color32 RitualDefaultBlip    = new Color32(166, 242, 255, 255);

        private static readonly ComponentType[] UnitQueryTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] BuildingQueryTypes =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        // Requiring PresentationId keeps individual trees out (forests have
        // no per-tree presentation); resource nodes also carry ObstacleTag
        // (navmesh carving) and are skipped per entity in the draw loop so
        // they keep their own colors.
        private static readonly ComponentType[] ObstacleQueryTypes =
        {
            ComponentType.ReadOnly<ObstacleTag>(),
            ComponentType.ReadOnly<PresentationId>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] IronQueryTypes =
        {
            ComponentType.ReadOnly<IronMineTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] VeilstoneQueryTypes =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] VeilsteelQueryTypes =
        {
            ComponentType.ReadOnly<VeilsteelDepositTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] RitualQueryTypes =
        {
            ComponentType.ReadOnly<ActiveRitualOnNode>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] ShardrootQueryTypes =
        {
            ComponentType.ReadOnly<ShardrootPickupTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };

        private CachedEntityQuery _unitsQ, _buildingsQ, _obstaclesQ,
                                  _ironQ, _veilstoneQ, _veilsteelQ,
                                  _ritualsQ, _shardrootQ;

        private Image _mapImage;

        private Vector2 _boundsMin, _boundsMax;
        private RectTransform _overlayRect;
        private Texture2D _overlayTex;
        private Color32[] _overlayPixels;
        private float[] _fogGrid, _fogGridSmooth;
        private int _ovW, _ovH;
        private Image[] _viewLines;
        private bool _layersReady;
        private float _overlayTimer;

        private void Awake()
        {
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (string.Equals(img.name, "Map", System.StringComparison.OrdinalIgnoreCase))
                {
                    _mapImage = img;
                    break;
                }
            }
            if (_mapImage == null)
            {
                TWBLog.Log("[GameUI] Minimap: no \"Map\" Image child found — node renamed?");
                return;
            }

            // Invisible until the generated sprite lands (the prefab keeps
            // no placeholder art).
            _mapImage.sprite = null;
            _mapImage.color = new Color(1f, 1f, 1f, 0f);
            _mapImage.preserveAspect = true;
        }

        private IEnumerator Start()
        {
            if (_mapImage == null) yield break;

            while (!TerrainUtility.IsReady())
                yield return null;

            if (!TerrainUtility.TryGetWorldBounds(out var min, out var max)
                || max.x - min.x < 1f || max.y - min.y < 1f)
            {
                TWBLog.Log("[GameUI] Minimap: terrain ready but no world bounds — no map image.");
                yield break;
            }

            yield return BuildElevationSprite(min, max);
        }

        // ── Live layers ────────────────────────────────────────────────────

        /// <summary>
        /// Build the dynamic layers once the terrain image exists: the
        /// fog+blip RawImage overlay, the 4 camera-view lines, and the click
        /// relay. The overlay's anchors reproduce the Map Image's
        /// preserveAspect letterboxing so overlay pixels line up with the
        /// terrain image on non-square maps.
        /// </summary>
        private void CreateOverlayLayers(int texW, int texH)
        {
            var mapRect = _mapImage.rectTransform;
            Rect r = mapRect.rect;
            float rectAspect = r.width / r.height;
            float texAspect = (float)texW / texH;
            Vector2 aMin, aMax;
            if (texAspect > rectAspect)
            {
                float hFrac = rectAspect / texAspect;
                aMin = new Vector2(0f, 0.5f - hFrac * 0.5f);
                aMax = new Vector2(1f, 0.5f + hFrac * 0.5f);
            }
            else
            {
                float wFrac = texAspect / rectAspect;
                aMin = new Vector2(0.5f - wFrac * 0.5f, 0f);
                aMax = new Vector2(0.5f + wFrac * 0.5f, 1f);
            }

            var go = new GameObject("MapOverlay", typeof(RectTransform), typeof(RawImage));
            _overlayRect = (RectTransform)go.transform;
            _overlayRect.SetParent(mapRect, false);
            _overlayRect.anchorMin = aMin;
            _overlayRect.anchorMax = aMax;
            _overlayRect.offsetMin = Vector2.zero;
            _overlayRect.offsetMax = Vector2.zero;

            _ovW = Mathf.Max(64, texW / 2);
            _ovH = Mathf.Max(64, texH / 2);
            _overlayTex = new Texture2D(_ovW, _ovH, TextureFormat.RGBA32, false)
            {
                name = "MinimapOverlay",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _overlayPixels = new Color32[_ovW * _ovH];
            _overlayTex.SetPixels32(_overlayPixels);
            _overlayTex.Apply(false, false);

            var raw = go.GetComponent<RawImage>();
            raw.texture = _overlayTex;
            raw.raycastTarget = true;
            go.AddComponent<MinimapClickRelay>().Init(this);

            _viewLines = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var lineGo = new GameObject("ViewLine" + i, typeof(RectTransform), typeof(Image));
                var lineRect = (RectTransform)lineGo.transform;
                lineRect.SetParent(_overlayRect, false);
                lineRect.anchorMin = new Vector2(0.5f, 0.5f);
                lineRect.anchorMax = new Vector2(0.5f, 0.5f);
                lineRect.pivot = new Vector2(0f, 0.5f);
                var img = lineGo.GetComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
                _viewLines[i] = img;
            }

            _layersReady = true;
        }

        private void OnDestroy()
        {
            if (_overlayTex != null) Destroy(_overlayTex);
            if (_mapImage != null && _mapImage.sprite != null && _mapImage.sprite.texture != null)
                Destroy(_mapImage.sprite.texture);
        }

        /// <summary>Last yaw the map content was rotated to.</summary>
        private float _mapYaw = float.NaN;

        /// <summary>
        /// Spin the map so "up" is always where the camera is looking.
        ///
        /// Rotating the MAP IMAGE rotates everything at once — the overlay, the
        /// blips and the view-cone lines are all its children, so they stay
        /// registered with the terrain for free. Clicks keep working without
        /// any change: TryGetWorldPosition goes through
        /// RectTransformUtility.ScreenPointToLocalPointInRectangle, which
        /// already accounts for the rect's rotation.
        ///
        /// Runs every frame, NOT on the throttled overlay tick below — the map
        /// must track the camera smoothly while it is turning, and a 1/4 second
        /// cadence would make it visibly step.
        /// </summary>
        private void UpdateMapRotation()
        {
            if (_mapImage == null) return;
            float yaw = TheWaningBorder.CameraRig.CameraController.Yaw;
            if (!float.IsNaN(_mapYaw) && Mathf.Abs(Mathf.DeltaAngle(_mapYaw, yaw)) < 0.05f) return;
            _mapYaw = yaw;
            // +yaw, not -yaw: the map content turns the same way the camera
            // does, which brings whatever the camera faces round to the top.
            _mapImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, yaw);
        }

        private void Update()
        {
            if (!_layersReady) return;

            UpdateMapRotation();

            _overlayTimer += Time.unscaledDeltaTime;
            if (_overlayTimer < OverlayRefreshInterval) return;
            _overlayTimer = 0f;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            // Observer perspective: render the viewed player's minimap
            // (fog, influence tint, blips); LocalPlayerFaction otherwise.
            var faction = GameSettings.ViewFactionOrLocal;
            RefreshFog(faction);
            ResolveTerritoryState();
            // Tint, then the neutral lattice, then the ownership outline over
            // both: a region division is map structure, an ownership border
            // is a claim, and the claim has to win where they run along the
            // same line. One composite pass from cached layers (2026-09-16).
            DrawTerritory(faction);
            DrawBlips(world.EntityManager, faction);
            DrawPings();
            _overlayTex.SetPixels32(_overlayPixels);
            _overlayTex.Apply(false, false);
        }

        /// <summary>
        /// Cached boundary coverage per overlay pixel, 0..255. Built once.
        /// </summary>
        private byte[] _regionEdge;
        private int _regionEdgeSeeds = -1;
        /// <summary>Region id per overlay pixel, built with the edge cache and
        /// for the same reason — the partition never moves during a match, only
        /// who owns it does.</summary>
        private short[] _regionAtPixel;
        /// <summary>Owner per region, refreshed each pass. Sized to the region
        /// count, so the per-pixel loop is an array index rather than a call.</summary>
        private int[] _ownerOfRegion;
        private byte[] _territoryEdge;
        private bool _territoryLogged;
        /// <summary>Whether each territory reads as cursed ground this pass.</summary>
        private bool[] _cursedRegion;
        /// <summary>Set once per overlay refresh by ResolveTerritoryState.</summary>
        private bool _territoryStateReady;

        /// <summary>Curse influence at which a cell counts as cursed — the same
        /// 0.5 the ground overlay and the world border contour use.</summary>
        private const float CurseInfluenceThreshold = 0.5f;

        private void LateUpdate()
        {
            if (!_layersReady) return;
            UpdateCameraViewRect();
        }

        // ── Camera view rectangle ──────────────────────────────────────────

        // ── Clicks ─────────────────────────────────────────────────────────

    }

    /// <summary>
    /// Forwards pointer clicks on the minimap overlay to the binder. Left
    /// snaps the camera, right issues move orders — explicit if/else, never
    /// fall-through (the old renderer once ran the camera snap on every
    /// right-click move order through exactly that bug).
    /// </summary>
    public sealed class MinimapClickRelay : MonoBehaviour, IPointerClickHandler
    {
        private MinimapPanelBinder _binder;

        public void Init(MinimapPanelBinder binder) => _binder = binder;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_binder != null) _binder.OnMinimapClick(eventData);
        }
    }
}
