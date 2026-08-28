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

namespace TheWaningBorder.UI.GameUI
{
    public sealed class MinimapPanelBinder : MonoBehaviour
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
        private static readonly Color32 GlowBlip     = new Color32(255, 217, 77, 255);
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
        private static readonly ComponentType[] GlowQueryTypes =
        {
            ComponentType.ReadOnly<GlowPickupTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };

        private CachedEntityQuery _unitsQ, _buildingsQ, _obstaclesQ,
                                  _ironQ, _veilstoneQ, _veilsteelQ,
                                  _ritualsQ, _glowQ;

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

        private IEnumerator BuildElevationSprite(Vector2 worldMin, Vector2 worldMax)
        {
            _boundsMin = worldMin;
            _boundsMax = worldMax;
            float sizeX = worldMax.x - worldMin.x;
            float sizeZ = worldMax.y - worldMin.y;
            int texW, texH;
            if (sizeX >= sizeZ)
            {
                texW = MaxTextureSize;
                texH = Mathf.Max(8, Mathf.RoundToInt(MaxTextureSize * sizeZ / sizeX));
            }
            else
            {
                texH = MaxTextureSize;
                texW = Mathf.Max(8, Mathf.RoundToInt(MaxTextureSize * sizeX / sizeZ));
            }

            // Pass 1: world-space height per pixel (NaN = outside every
            // tile), one GetHeights grab per terrain tile.
            var heights = new float[texW * texH];
            for (int i = 0; i < heights.Length; i++) heights[i] = float.NaN;

            float minH = float.MaxValue, maxH = float.MinValue;
            var tiles = UnityEngine.Terrain.activeTerrains;
            foreach (var tile in tiles)
            {
                if (tile == null || tile.terrainData == null) continue;
                var data = tile.terrainData;
                Vector3 tPos = tile.transform.position;
                Vector3 tSize = data.size;
                int res = data.heightmapResolution;
                float[,] hm = data.GetHeights(0, 0, res, res); // [z, x], normalized 0..1

                int px0 = Mathf.Clamp(Mathf.CeilToInt((tPos.x - worldMin.x) / sizeX * texW - 0.5f), 0, texW - 1);
                int px1 = Mathf.Clamp(Mathf.FloorToInt((tPos.x + tSize.x - worldMin.x) / sizeX * texW - 0.5f), 0, texW - 1);
                int py0 = Mathf.Clamp(Mathf.CeilToInt((tPos.z - worldMin.y) / sizeZ * texH - 0.5f), 0, texH - 1);
                int py1 = Mathf.Clamp(Mathf.FloorToInt((tPos.z + tSize.z - worldMin.y) / sizeZ * texH - 0.5f), 0, texH - 1);

                for (int py = py0; py <= py1; py++)
                {
                    float wz = worldMin.y + (py + 0.5f) / texH * sizeZ;
                    float v = Mathf.Clamp01((wz - tPos.z) / tSize.z) * (res - 1);
                    int z0 = Mathf.Min((int)v, res - 2);
                    float fz = v - z0;

                    for (int px = px0; px <= px1; px++)
                    {
                        float wx = worldMin.x + (px + 0.5f) / texW * sizeX;
                        float u = Mathf.Clamp01((wx - tPos.x) / tSize.x) * (res - 1);
                        int x0 = Mathf.Min((int)u, res - 2);
                        float fx = u - x0;

                        float h = Mathf.Lerp(
                            Mathf.Lerp(hm[z0, x0], hm[z0, x0 + 1], fx),
                            Mathf.Lerp(hm[z0 + 1, x0], hm[z0 + 1, x0 + 1], fx),
                            fz) * tSize.y + tPos.y;

                        heights[py * texW + px] = h;
                        if (h < minH) minH = h;
                        if (h > maxH) maxH = h;
                    }

                    if ((py & (RowsPerFrame - 1)) == RowsPerFrame - 1) yield return null;
                }
            }

            if (minH >= maxH)
            {
                // Perfectly flat map — still show it, mid-ramp.
                minH = maxH - 1f;
            }

            // Pass 2: colorize. Height picks the ramp stop and the base
            // alpha; the finite-difference gradient drives both the NW
            // hillshade and the incline mark. The slope formula matches
            // PassabilityGrid's 4-point gradient, so ground drawn as cliff
            // is ground units cannot climb.
            float stepX = sizeX / texW;
            float stepZ = sizeZ / texH;
            var pixels = new Color32[texW * texH];
            var clear = new Color32(0, 0, 0, 0);

            for (int py = 0; py < texH; py++)
            {
                for (int px = 0; px < texW; px++)
                {
                    int i = py * texW + px;
                    float h = heights[i];
                    if (float.IsNaN(h)) { pixels[i] = clear; continue; }

                    float hl = SampleOr(heights, texW, texH, px - 1, py, h);
                    float hr = SampleOr(heights, texW, texH, px + 1, py, h);
                    float hd = SampleOr(heights, texW, texH, px, py - 1, h);
                    float hu = SampleOr(heights, texW, texH, px, py + 1, h);
                    float dx = (hr - hl) / (2f * stepX);
                    float dz = (hu - hd) / (2f * stepZ);
                    var normal = new Vector3(-dx, 1f, -dz).normalized;
                    float shade = Mathf.Lerp(0.6f, 1.05f,
                        Mathf.Clamp01(Vector3.Dot(normal, LightDir)));

                    float t = (h - minH) / (maxH - minH);
                    Color c = (t < 0.5f
                        ? Color.Lerp(LowColor, MidColor, t * 2f)
                        : Color.Lerp(MidColor, HighColor, (t - 0.5f) * 2f)) * shade;
                    float a = Mathf.Lerp(LowAlpha, HighAlpha, t);

                    // Steepness: darken toward rock as the incline grows,
                    // then blend hard to the cliff color across the last 15%
                    // below the walkability limit (soft edge, no aliasing).
                    float slope = Mathf.Sqrt(dx * dx + dz * dz);
                    float steep = Mathf.Clamp01(slope / PassabilityGrid.MaxWalkableSlope);
                    c = Color.Lerp(c, CliffColor, steep * 0.35f);
                    float cliff = Mathf.InverseLerp(
                        0.85f * PassabilityGrid.MaxWalkableSlope,
                        PassabilityGrid.MaxWalkableSlope, slope);
                    c = Color.Lerp(c, CliffColor, cliff);
                    a = Mathf.Lerp(a, CliffAlpha, cliff);

                    pixels[i] = new Color32(
                        (byte)(Mathf.Clamp01(c.r) * 255f),
                        (byte)(Mathf.Clamp01(c.g) * 255f),
                        (byte)(Mathf.Clamp01(c.b) * 255f),
                        (byte)(a * 255f));
                }
                if ((py & (RowsPerFrame - 1)) == RowsPerFrame - 1) yield return null;
            }

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                name = "MinimapElevation",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            _mapImage.sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                new Vector2(0.5f, 0.5f), 100f);
            _mapImage.color = Color.white;

            // Layout must have settled before the overlay can be aspect-fit
            // to the Map rect (it has, after the terrain wait, but guard).
            while (_mapImage.rectTransform.rect.width < 1f)
                yield return null;
            CreateOverlayLayers(texW, texH);
        }

        private static float SampleOr(float[] heights, int w, int h, int x, int y, float fallback)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return fallback;
            float v = heights[y * w + x];
            return float.IsNaN(v) ? fallback : v;
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

        private void Update()
        {
            if (!_layersReady) return;
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
            DrawInfluenceTint(faction);
            DrawRegionBoundaries(faction);
            // Over the neutral lattice: a region division is map structure, an
            // ownership border is a claim, and the claim has to win where they
            // run along the same line.
            DrawTerritoryOutlines(faction);
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

        /// <summary>
        /// Resolve who holds each territory, and which ones read as cursed,
        /// once per overlay refresh. Shared by the TINT and the OUTLINE — the
        /// two have to agree about where a territory ends, and computing it
        /// twice is how they would stop agreeing.
        ///
        /// False when there is no partition (a scenario fixture, or a map with
        /// no seeds), which puts the tint back on the influence field.
        /// </summary>
        private bool ResolveTerritoryState()
        {
            _territoryStateReady = false;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return false;

            BuildRegionEdgeCache();
            if (_regionAtPixel == null) return false;

            // TerritoryIncomeSystem derives ownership on a 5 s tick whose first
            // run is a full interval into the match. Deriving it here too keeps
            // the opening from showing nothing at all.
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return false;
            if (!TheWaningBorder.World.Regions.TerritoryOwnership.Ready)
            {
                TheWaningBorder.World.Regions.TerritoryOwnership.Recompute(w.EntityManager);
                if (!TheWaningBorder.World.Regions.TerritoryOwnership.Ready) return false;
            }

            int regions = TheWaningBorder.World.Regions.RegionMap.Count;
            if (regions <= 0) return false;
            if (_ownerOfRegion == null || _ownerOfRegion.Length != regions)
            {
                _ownerOfRegion = new int[regions];
                _cursedRegion = new bool[regions];
            }

            for (int r = 0; r < regions; r++)
                _ownerOfRegion[r] = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(r);

            // Curse coverage per territory, on the influence grid. The curse
            // owns no territories (Regions.md §3 is unimplemented), so what
            // makes it territory-granular is thresholding its field a whole
            // territory at a time — the same rule and the same share the ground
            // overlay uses, read from there so the minimap and the world cannot
            // disagree about which territory has fallen.
            int res = TheWaningBorder.Influence.PlayerInfluenceMap.Resolution;
            if (TheWaningBorder.Influence.PlayerInfluenceMap.Ready)
            {
                var cursed = new int[regions];
                var total = new int[regions];
                Vector2 wMin = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
                Vector2 wSize = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
                for (int cy = 0; cy < res; cy++)
                {
                    float wz = wMin.y + (cy + 0.5f) / res * wSize.y;
                    for (int cx = 0; cx < res; cx++)
                    {
                        float wx = wMin.x + (cx + 0.5f) / res * wSize.x;
                        int region = TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);
                        if (region < 0 || region >= regions) continue;
                        total[region]++;
                        if (TheWaningBorder.Influence.PlayerInfluenceMap.CellValue(
                                cx, cy, TheWaningBorder.Influence.PlayerInfluenceMap.CurseChannel)
                            >= CurseInfluenceThreshold)
                            cursed[region]++;
                    }
                }
                for (int r = 0; r < regions; r++)
                    _cursedRegion[r] = total[r] > 0
                        && cursed[r] / (float)total[r]
                           >= TheWaningBorder.Influence.InfluenceMaskTexture.CursedTerritoryShare;
            }
            else
            {
                for (int r = 0; r < regions; r++) _cursedRegion[r] = false;
            }

            _territoryStateReady = true;
            return true;
        }

        /// <summary>Curse influence at which a cell counts as cursed — the same
        /// 0.5 the ground overlay and the world border contour use.</summary>
        private const float CurseInfluenceThreshold = 0.5f;

        /// <summary>
        /// Region boundaries (docs/Design/Regions.md), drawn OVER the territory
        /// tint and UNDER the blips.
        ///
        /// The minimap is where the region map is meant to be READ, so unlike
        /// the terrain (a quiet darkening) these are a legible lattice you can
        /// plan against.
        ///
        /// Rasterised ONCE and cached. The partition never moves during a
        /// match, and this runs on every overlay refresh over every overlay
        /// pixel -- with up to 40 seeds and a linear nearest-seed scan, redoing
        /// it live would be millions of distance tests per second for an image
        /// that is identical every time.
        ///
        /// Fog: region SHAPE is map structure, not intel -- the same partition
        /// the lobby preview shows before the match starts -- so it is not
        /// fog-gated. Who OWNS a region still follows the influence map's rules.
        /// </summary>
        private void DrawRegionBoundaries(Faction faction)
        {
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return;

            BuildRegionEdgeCache();
            if (_regionEdge == null) return;

            var line = new Color32(18, 18, 22, 255);
            for (int i = 0; i < _regionEdge.Length; i++)
            {
                byte e = _regionEdge[i];
                if (e == 0) continue;

                var c = _overlayPixels[i];
                float a = e / 255f;
                _overlayPixels[i] = new Color32(
                    (byte)Mathf.Lerp(c.r, line.r, a),
                    (byte)Mathf.Lerp(c.g, line.g, a),
                    (byte)Mathf.Lerp(c.b, line.b, a),
                    (byte)Mathf.Max(c.a, (byte)(a * 255f)));
            }
        }

        private void BuildRegionEdgeCache()
        {
            int seeds = TheWaningBorder.World.Regions.RegionMap.Count;
            if (_regionEdge != null && _regionEdge.Length == _ovW * _ovH && _regionEdgeSeeds == seeds)
                return;

            _regionEdge = new byte[_ovW * _ovH];
            _regionAtPixel = new short[_ovW * _ovH];
            _regionEdgeSeeds = seeds;

            float bw = Mathf.Max(0.001f, _boundsMax.x - _boundsMin.x);
            float bh = Mathf.Max(0.001f, _boundsMax.y - _boundsMin.y);

            // Half-width in METRES sized from the minimap's pixel pitch, so the
            // lattice stays ~1.5 px wide on any map instead of thinning to
            // nothing on a large one.
            float width = Mathf.Max(1f, bw / Mathf.Max(1, _ovW) * 1.5f);

            for (int py = 0; py < _ovH; py++)
            {
                float wz = _boundsMin.y + (py + 0.5f) / _ovH * bh;
                int row = py * _ovW;
                for (int px = 0; px < _ovW; px++)
                {
                    float wx = _boundsMin.x + (px + 0.5f) / _ovW * bw;
                    // RegionAt, so unclaimable ground (mountain, cliff, water,
                    // the rim) belongs to NOBODY — Regions.md §1. A massif in
                    // the middle of a holding becomes a hole in its owner's
                    // outline and the border runs around the foot of it.
                    int region = TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);
                    _regionAtPixel[row + px] = (short)region;

                    // EdgeStrengthAt returns 0 on exactly the ground RegionAt
                    // calls None, and both answer that by sampling the terrain.
                    // Asking once and short-circuiting halves the bake's
                    // terrain reads over the whole overlay.
                    float e = region == TheWaningBorder.World.Regions.RegionMap.None
                        ? 0f
                        : TheWaningBorder.World.Regions.RegionMap.EdgeStrengthAt(wx, wz, width);
                    _regionEdge[row + px] = e <= 0.15f ? (byte)0 : (byte)(Mathf.Clamp01(e) * 0.85f * 255f);
                }
            }
        }

        /// <summary>
        /// TERRITORY OUTLINES — the same statement the in-world ribbon makes
        /// (InfluenceOverlayRenderer), in the same colours: a line around the
        /// ground each faction actually HOLDS, in that faction's banner colour.
        ///
        /// Ownership, not influence. The tint above is driven by the influence
        /// map, which InfluenceMapSystem leaves flat zero for the whole of
        /// Age 0 — so on the minimap too there was nothing to read until a
        /// culture was adopted. docs/Design/Regions.md §2 says you hold your
        /// start region from tick 0, and this is what shows it.
        ///
        /// The boundary is found by comparing each pixel's owner against its
        /// four neighbours: one pass over the overlay and no distance tests,
        /// because the expensive half — which region each pixel is in — is
        /// baked once by BuildRegionEdgeCache.
        ///
        /// Fog follows the tint's rule exactly: ground you have EXPLORED keeps
        /// showing its owner like a remembered building, ground you have never
        /// seen shows nothing. Without that the outline would draw straight
        /// through unexplored black and hand you every faction's holdings.
        /// </summary>
        private void DrawTerritoryOutlines(Faction faction)
        {
            // Ownership was resolved once for this refresh, before the tint —
            // the fill and the line have to stop in the same place.
            if (!_territoryStateReady) return;

            int regions = _ownerOfRegion.Length;
            bool anyOwned = false;
            for (int r = 0; r < regions && !anyOwned; r++)
                anyOwned = _ownerOfRegion[r] >= 0;
            if (!anyOwned) return;

            int n = _ovW * _ovH;
            if (_territoryEdge == null || _territoryEdge.Length != n)
                _territoryEdge = new byte[n];
            System.Array.Clear(_territoryEdge, 0, n);

            // Pass 1 — mark the OWNED side of every ownership seam. Marking the
            // owner's own pixel rather than its neighbour's is what keeps two
            // adjacent players' borders as two lines in two colours instead of
            // one shared line whose colour depends on iteration order.
            for (int py = 0; py < _ovH; py++)
            {
                int row = py * _ovW;
                for (int px = 0; px < _ovW; px++)
                {
                    int i = row + px;
                    int owner = OwnerAtPixel(i);
                    if (owner < 0) continue;

                    bool edge =
                        px == 0 || px == _ovW - 1 || py == 0 || py == _ovH - 1
                        || OwnerAtPixel(i - 1) != owner
                        || OwnerAtPixel(i + 1) != owner
                        || OwnerAtPixel(i - _ovW) != owner
                        || OwnerAtPixel(i + _ovW) != owner;

                    if (edge) _territoryEdge[i] = (byte)(owner + 1);   // 0 = not an edge
                }
            }

            // No thickening pass: the seam is one pixel and stays one pixel.
            // It was grown inward by one to make it easier to see, which put a
            // two-pixel band on a small overlay — far too heavy for a line
            // whose job is to mark where ground changes hands.

            bool unfogged = !GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null;
            float bw = Mathf.Max(0.001f, _boundsMax.x - _boundsMin.x);
            float bh = Mathf.Max(0.001f, _boundsMax.y - _boundsMin.y);
            int drawn = 0;

            for (int py = 0; py < _ovH; py++)
            {
                int row = py * _ovW;
                for (int px = 0; px < _ovW; px++)
                {
                    int i = row + px;
                    byte tag = _territoryEdge[i];
                    if (tag == 0) continue;
                    int owner = tag - 1;
                    if (owner < 0 || owner > 7) continue;

                    if (!unfogged)
                    {
                        var wp = new float3(
                            _boundsMin.x + (px + 0.5f) / _ovW * bw, 0f,
                            _boundsMin.y + (py + 0.5f) / _ovH * bh);
                        if (!FogOfWarSystem.IsRevealedToFaction(faction, wp)) continue;
                    }

                    Color32 line = FactionColors.Get((Faction)owner);
                    _overlayPixels[i] = new Color32(line.r, line.g, line.b, 255);
                    drawn++;
                }
            }

            if (drawn > 0 && !_territoryLogged)
            {
                _territoryLogged = true;
                Debug.Log($"[Minimap] territory outlines online — {regions} region(s), " +
                          $"{drawn} outline pixel(s).");
            }
        }

        /// <summary>Owner faction index at an overlay pixel, or -1 for Natural
        /// ground, the curse, and anything off the partition.</summary>
        private int OwnerAtPixel(int index)
        {
            int r = _regionAtPixel[index];
            if (r < 0 || r >= _ownerOfRegion.Length) return -1;
            int owner = _ownerOfRegion[r];
            return owner >= 0 && owner <= 7 ? owner : -1;
        }

        /// <summary>Flashing event diamonds (MinimapPings) on top of every
        /// other layer — damage red, curse purple, power gold.</summary>
        private void DrawPings()
        {
            var pings = MinimapPings.Live();
            if (pings.Count == 0) return;
            bool flashOn = (Time.time * 4f) % 2f < 1.2f; // fast blink, longer on-phase
            if (!flashOn) return;

            float invBw = 1f / Mathf.Max(0.001f, _boundsMax.x - _boundsMin.x);
            float invBh = 1f / Mathf.Max(0.001f, _boundsMax.y - _boundsMin.y);
            for (int i = 0; i < pings.Count; i++)
            {
                var p = pings[i];
                int cx = Mathf.RoundToInt((p.Pos.x - _boundsMin.x) * invBw * _ovW);
                int cy = Mathf.RoundToInt((p.Pos.z - _boundsMin.y) * invBh * _ovH);
                int r = p.Big ? 7 : 4;
                for (int dy = -r; dy <= r; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= _ovH) continue;
                    int half = r - Mathf.Abs(dy); // diamond
                    for (int dx = -half; dx <= half; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= _ovW) continue;
                        _overlayPixels[py * _ovW + px] = p.Color;
                    }
                }
            }
        }

        /// <summary>Territory tint (2026-08-04): every influence channel's
        /// OWNED ground — ownership-clipped exactly like the world border
        /// overlay (the strongest channel at/over 0.5 owns the cell) —
        /// blended between the fog pass and the blips: players in their
        /// banner colour, the curse in purple. Influence-grid resolution
        /// (128²), so the pass is 16k samples at 10 Hz, not per-pixel.</summary>
        private void DrawInfluenceTint(Faction faction)
        {
            // Influence is painted OVER the fog layer (RefreshFog runs first),
            // so without a visibility test it drew straight through unexplored
            // black — handing the player the shape of every faction's territory
            // and the curse's spread across ground nobody had scouted. Territory
            // you have EXPLORED still shows once revealed, like a remembered
            // building; territory you have never seen shows nothing.
            bool unfogged = !GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null;
            float bw = Mathf.Max(0.001f, _boundsMax.x - _boundsMin.x);
            float bh = Mathf.Max(0.001f, _boundsMax.y - _boundsMin.y);

            if (_territoryStateReady)
            {
                // WHOLE TERRITORIES (docs/Design/Regions.md §2). Ground is held
                // a territory at a time, so the fill has to stop exactly where
                // the outline does. Painted straight off the influence field it
                // did not: it was a soft bubble around whatever was depositing,
                // spilling past the border and fading out over ground nobody
                // held — so the minimap disagreed with its own border lines and
                // with the terrain, and none of the three was the rule.
                for (int py = 0; py < _ovH; py++)
                {
                    int row = py * _ovW;
                    for (int px = 0; px < _ovW; px++)
                    {
                        int i = row + px;
                        int region = _regionAtPixel[i];
                        if (region < 0 || region >= _ownerOfRegion.Length) continue;

                        int owner = _ownerOfRegion[region];
                        bool cursed = _cursedRegion[region];
                        // A cursed territory reads as cursed whoever nominally
                        // holds it: the curse is what is standing on the ground.
                        if (owner < 0 && !cursed) continue;

                        Color32 tint = cursed
                            ? TheWaningBorder.Influence.PlayerInfluenceMap.ChannelColor(
                                  TheWaningBorder.Influence.PlayerInfluenceMap.CurseChannel)
                            : FactionColors.Get((Faction)owner);
                        float blend = cursed ? 0.6f : 0.55f;

                        if (!unfogged)
                        {
                            var wp = new float3(
                                _boundsMin.x + (px + 0.5f) / _ovW * bw, 0f,
                                _boundsMin.y + (py + 0.5f) / _ovH * bh);
                            if (!FogOfWarSystem.IsRevealedToFaction(faction, wp)) continue;
                        }

                        var p = _overlayPixels[i];
                        p.r = (byte)(p.r + (tint.r - p.r) * blend);
                        p.g = (byte)(p.g + (tint.g - p.g) * blend);
                        p.b = (byte)(p.b + (tint.b - p.b) * blend);
                        if (p.a < 165) p.a = 165; // reads solidly over the terrain image
                        _overlayPixels[i] = p;
                    }
                }
                return;
            }

            // No partition to fill by: fall back to the influence field, which
            // is the only statement about ownership available on a map with no
            // region seeds.
            if (!TheWaningBorder.Influence.PlayerInfluenceMap.Ready) return;
            const float threshold = 0.5f;
            int res = TheWaningBorder.Influence.PlayerInfluenceMap.Resolution;
            int channels = TheWaningBorder.Influence.PlayerInfluenceMap.ChannelCount;

            Vector2 wMin = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
            Vector2 wSize = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
            float invBw = 1f / bw;
            float invBh = 1f / bh;

            for (int cy = 0; cy < res; cy++)
            {
                for (int cx = 0; cx < res; cx++)
                {
                    int owner = -1;
                    float bestV = threshold;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float v = TheWaningBorder.Influence.PlayerInfluenceMap.CellValue(cx, cy, ch);
                        if (v >= bestV) { bestV = v; owner = ch; }
                    }
                    if (owner < 0) continue;

                    bool isCurse = owner == TheWaningBorder.Influence.PlayerInfluenceMap.CurseChannel;
                    Color32 tint = TheWaningBorder.Influence.PlayerInfluenceMap.ChannelColor(owner);
                    float blend = isCurse ? 0.6f : 0.55f;

                    float wx0 = wMin.x + cx / (float)res * wSize.x;
                    float wz0 = wMin.y + cy / (float)res * wSize.y;
                    float wx1 = wx0 + wSize.x / res;
                    float wz1 = wz0 + wSize.y / res;

                    if (!unfogged)
                    {
                        var cellMid = new float3((wx0 + wx1) * 0.5f, 0f, (wz0 + wz1) * 0.5f);
                        if (!FogOfWarSystem.IsRevealedToFaction(faction, cellMid)) continue;
                    }
                    int px0 = Mathf.Clamp(Mathf.FloorToInt((wx0 - _boundsMin.x) * invBw * _ovW), 0, _ovW - 1);
                    int px1 = Mathf.Clamp(Mathf.CeilToInt((wx1 - _boundsMin.x) * invBw * _ovW), 0, _ovW);
                    int py0 = Mathf.Clamp(Mathf.FloorToInt((wz0 - _boundsMin.y) * invBh * _ovH), 0, _ovH - 1);
                    int py1 = Mathf.Clamp(Mathf.CeilToInt((wz1 - _boundsMin.y) * invBh * _ovH), 0, _ovH);

                    for (int py = py0; py < py1; py++)
                    {
                        int row = py * _ovW;
                        for (int px = px0; px < px1; px++)
                        {
                            var p = _overlayPixels[row + px];
                            p.r = (byte)(p.r + (tint.r - p.r) * blend);
                            p.g = (byte)(p.g + (tint.g - p.g) * blend);
                            p.b = (byte)(p.b + (tint.b - p.b) * blend);
                            if (p.a < 165) p.a = 165;
                            _overlayPixels[row + px] = p;
                        }
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (!_layersReady) return;
            UpdateCameraViewRect();
        }

        /// <summary>
        /// Fog pass over the whole overlay: hidden ground solid black,
        /// revealed ground half-dimmed, visible ground clear. The 3-state
        /// fog is sampled on a coarse grid (one query per FogSampleStride
        /// pixels — fewer queries than the old per-2x2-block version), the
        /// grid is 3x3 box-blurred to round the FoW grid's staircase
        /// corners, and the result is bilinearly upsampled to the overlay —
        /// state borders render as smooth ramps instead of square blocks.
        /// </summary>
        private void RefreshFog(Faction faction)
        {
            // No view faction = unfogged (mirrors FogVisibilitySyncSystem).
            if (!GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null)
            {
                for (int i = 0; i < _overlayPixels.Length; i++)
                    _overlayPixels[i] = ClearPixel;
                return;
            }

            // Sample points sit every FogSampleStride pixels, plus one
            // column/row past the far edge so upsampling never extrapolates.
            int gw = _ovW / FogSampleStride + 2;
            int gh = _ovH / FogSampleStride + 2;
            if (_fogGrid == null || _fogGrid.Length != gw * gh)
            {
                _fogGrid = new float[gw * gh];
                _fogGridSmooth = new float[gw * gh];
            }

            for (int gy = 0; gy < gh; gy++)
            {
                float wz = Mathf.Lerp(_boundsMin.y, _boundsMax.y,
                    gy * FogSampleStride / (float)_ovH);
                for (int gx = 0; gx < gw; gx++)
                {
                    float wx = Mathf.Lerp(_boundsMin.x, _boundsMax.x,
                        gx * FogSampleStride / (float)_ovW);
                    var pos = new float3(wx, 0f, wz);

                    float a;
                    if (FogOfWarSystem.IsVisibleToFaction(faction, pos)) a = 0f;
                    else if (FogOfWarSystem.IsRevealedToFaction(faction, pos)) a = RevealedFogAlpha;
                    else a = HiddenFogAlpha;
                    _fogGrid[gy * gw + gx] = a;
                }
            }

            // 3x3 box blur, edge-clamped.
            for (int gy = 0; gy < gh; gy++)
            {
                for (int gx = 0; gx < gw; gx++)
                {
                    float sum = 0f;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int sy = Mathf.Clamp(gy + oy, 0, gh - 1);
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int sx = Mathf.Clamp(gx + ox, 0, gw - 1);
                            sum += _fogGrid[sy * gw + sx];
                        }
                    }
                    _fogGridSmooth[gy * gw + gx] = sum / 9f;
                }
            }

            float inv = 1f / FogSampleStride;
            for (int y = 0; y < _ovH; y++)
            {
                float gyF = y * inv;
                int gy0 = (int)gyF;
                float fy = gyF - gy0;
                int rowA = gy0 * gw;
                int rowB = Mathf.Min(gy0 + 1, gh - 1) * gw;
                for (int x = 0; x < _ovW; x++)
                {
                    float gxF = x * inv;
                    int gx0 = (int)gxF;
                    float fx = gxF - gx0;
                    int gx1 = Mathf.Min(gx0 + 1, gw - 1);

                    float a = Mathf.Lerp(
                        Mathf.Lerp(_fogGridSmooth[rowA + gx0], _fogGridSmooth[rowA + gx1], fx),
                        Mathf.Lerp(_fogGridSmooth[rowB + gx0], _fogGridSmooth[rowB + gx1], fx),
                        fy);
                    _overlayPixels[y * _ovW + x] = a > 0.004f
                        ? new Color32(0, 0, 0, (byte)(a * 255f))
                        : ClearPixel;
                }
            }
        }

        private void DrawBlips(EntityManager em, Faction faction)
        {
            // No view faction (and FoW-off matches) = every blip drawn.
            bool unfogged = !GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null;

            // Units: own always, others only while FoW-visible.
            var unitsQ = _unitsQ.Get(em, UnitQueryTypes);
            using (var facs = unitsQ.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = unitsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < facs.Length; i++)
                {
                    var pos = xfs[i].Position;
                    bool mine = facs[i].Value == faction;
                    if (!mine && !unfogged
                        && !FogOfWarSystem.IsVisibleToFaction(faction, pos)) continue;
                    DrawDisc(WorldToOverlayPixel(pos), UnitRadiusPx, FactionColors.Get(facs[i].Value));
                }
            }

            // Buildings: own always, others visible = solid, revealed = ghost
            // (pre-darkened — the overlay replaces pixels, it doesn't blend).
            var buildingsQ = _buildingsQ.Get(em, BuildingQueryTypes);
            using (var facs = buildingsQ.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = buildingsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < facs.Length; i++)
                {
                    var pos = xfs[i].Position;
                    bool mine = facs[i].Value == faction;
                    bool vis = mine || unfogged || FogOfWarSystem.IsVisibleToFaction(faction, pos);
                    if (!vis && !FogOfWarSystem.IsRevealedToFaction(faction, pos)) continue;

                    Color baseCol = FactionColors.Get(facs[i].Value);
                    Color32 c = vis ? (Color32)baseCol
                                    : (Color32)Color.Lerp(Color.black, baseCol, 0.55f);
                    DrawDisc(WorldToOverlayPixel(pos), BuildingRadiusPx, c);
                }
            }

            // Rocks and resource nodes — static landmarks, but only once the
            // ground is EXPLORED (unexplored minimap is solid black; drawing
            // them fog-ignorant would leak the map layout). Resource nodes
            // also carry ObstacleTag + PresentationId (navmesh carving) and
            // are skipped in the rocks pass so the dedicated passes below
            // keep their colors.
            var obstaclesQ = _obstaclesQ.Get(em, ObstacleQueryTypes);
            using (var ents = obstaclesQ.ToEntityArray(Allocator.Temp))
            using (var xfs = obstaclesQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.HasComponent<IronMineTag>(ents[i])
                        || em.HasComponent<VeilstoneOutcroppingTag>(ents[i])
                        || em.HasComponent<VeilsteelDepositTag>(ents[i])) continue;
                    if (!unfogged
                        && !FogOfWarSystem.IsRevealedToFaction(faction, xfs[i].Position)) continue;
                    DrawDisc(WorldToOverlayPixel(xfs[i].Position), 2, RockBlip);
                }
            }

            Faction? gate = unfogged ? (Faction?)null : faction;
            DrawSimpleBlips(_ironQ.Get(em, IronQueryTypes), 2, IronBlip, gate);
            DrawSimpleBlips(_veilstoneQ.Get(em, VeilstoneQueryTypes), 2, VeilstoneBlip, gate);
            DrawSimpleBlips(_veilsteelQ.Get(em, VeilsteelQueryTypes), 2, VeilsteelBlip, gate);

            // Ritual markers — visible to all players regardless of fog (the
            // spec is explicit that rituals are universally locatable); the
            // colors match RitualBeamSystem's beam tints.
            var ritualsQ = _ritualsQ.Get(em, RitualQueryTypes);
            using (var actives = ritualsQ.ToComponentDataArray<ActiveRitualOnNode>(Allocator.Temp))
            using (var xfs = ritualsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < actives.Length; i++)
                {
                    Color32 c = actives[i].Kind switch
                    {
                        RitualKind.Conversion        => RitualConversionBlip,
                        RitualKind.ViolentExtraction => RitualExtractionBlip,
                        _                            => RitualDefaultBlip,
                    };
                    DrawDisc(WorldToOverlayPixel(xfs[i].Position), 4, c);
                }
            }

            // Glow pickups — gold, also fog-ignorant by spec.
            DrawSimpleBlips(_glowQ.Get(em, GlowQueryTypes), 3, GlowBlip);
        }

        /// <summary>Draw a disc per query entity. Pass <paramref name="revealGate"/>
        /// to draw only on ground that faction has explored; null = fog-ignorant
        /// (rituals, Glow — universally locatable by spec).</summary>
        private void DrawSimpleBlips(EntityQuery query, int radius, Color32 color,
            Faction? revealGate = null)
        {
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                if (revealGate.HasValue
                    && !FogOfWarSystem.IsRevealedToFaction(revealGate.Value, xfs[i].Position))
                    continue;
                DrawDisc(WorldToOverlayPixel(xfs[i].Position), radius, color);
            }
        }

        private int2 WorldToOverlayPixel(float3 pos)
        {
            float u = Mathf.InverseLerp(_boundsMin.x, _boundsMax.x, pos.x);
            float v = Mathf.InverseLerp(_boundsMin.y, _boundsMax.y, pos.z);
            return new int2(
                Mathf.Clamp(Mathf.FloorToInt(u * _ovW), 0, _ovW - 1),
                Mathf.Clamp(Mathf.FloorToInt(v * _ovH), 0, _ovH - 1));
        }

        private void DrawDisc(int2 center, int r, Color32 col)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int yy = center.y + dy;
                if (yy < 0 || yy >= _ovH) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = center.x + dx;
                    if (xx < 0 || xx >= _ovW) continue;
                    if (dx * dx + dy * dy <= r2)
                        _overlayPixels[yy * _ovW + xx] = col;
                }
            }
        }

        // ── Camera view rectangle ──────────────────────────────────────────

        private void UpdateCameraViewRect()
        {
            var main = Camera.main;
            if (main == null || _viewLines == null) return;

            Vector3 p00 = RayToGround(main, new Vector2(0f, 0f));
            Vector3 p10 = RayToGround(main, new Vector2(1f, 0f));
            Vector3 p11 = RayToGround(main, new Vector2(1f, 1f));
            Vector3 p01 = RayToGround(main, new Vector2(0f, 1f));

            Vector2 px00 = WorldToOverlayLocal(p00);
            Vector2 px10 = WorldToOverlayLocal(p10);
            Vector2 px11 = WorldToOverlayLocal(p11);
            Vector2 px01 = WorldToOverlayLocal(p01);

            DrawViewLine(0, px00, px10);
            DrawViewLine(1, px10, px11);
            DrawViewLine(2, px11, px01);
            DrawViewLine(3, px01, px00);
        }

        /// <summary>World position to the view-lines' center-anchored local
        /// space. InverseLerp clamps, so a horizon-tilted camera pins its
        /// rectangle to the map edges instead of escaping the panel.</summary>
        private Vector2 WorldToOverlayLocal(Vector3 world)
        {
            float u = Mathf.InverseLerp(_boundsMin.x, _boundsMax.x, world.x);
            float v = Mathf.InverseLerp(_boundsMin.y, _boundsMax.y, world.z);
            Rect r = _overlayRect.rect;
            return new Vector2((u - 0.5f) * r.width, (v - 0.5f) * r.height);
        }

        private void DrawViewLine(int index, Vector2 start, Vector2 end)
        {
            Vector2 diff = end - start;
            var lineRect = _viewLines[index].rectTransform;
            lineRect.anchoredPosition = start;
            lineRect.sizeDelta = new Vector2(diff.magnitude, ViewLineThickness);
            lineRect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);
        }

        private static Vector3 RayToGround(Camera cam, Vector2 viewport01)
        {
            var ground = new Plane(Vector3.up, Vector3.zero);
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0f));
            if (ground.Raycast(ray, out float t)) return ray.GetPoint(t);
            Vector3 p = ray.origin + ray.direction * 1000f;
            return new Vector3(p.x, 0f, p.z);
        }

        // ── Clicks ─────────────────────────────────────────────────────────

        internal void OnMinimapClick(PointerEventData eventData)
        {
            if (!TryGetWorldPosition(eventData, out float wx, out float wz)) return;

            if (eventData.button == PointerEventData.InputButton.Right)
                IssueMoveOrders(wx, wz);
            else
                TheWaningBorder.Input.GameCamera.FocusOn(new Vector3(wx, 0f, wz), instant: true);
        }

        private bool TryGetWorldPosition(PointerEventData eventData, out float wx, out float wz)
        {
            wx = 0f;
            wz = 0f;
            if (_overlayRect == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _overlayRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return false;

            // Local space depends on the pivot: local ∈ [-pivot*size,
            // (1-pivot)*size] on each axis. Normalize to [0..1].
            Rect r = _overlayRect.rect;
            Vector2 pivot = _overlayRect.pivot;
            float u = Mathf.Clamp01(local.x / r.width + pivot.x);
            float v = Mathf.Clamp01(local.y / r.height + pivot.y);
            wx = Mathf.Lerp(_boundsMin.x, _boundsMax.x, u);
            wz = Mathf.Lerp(_boundsMin.y, _boundsMax.y, v);
            return true;
        }

        private void IssueMoveOrders(float wx, float wz)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            var faction = GameSettings.LocalPlayerFaction;
            var destination = new float3(wx, TerrainUtility.GetHeight(wx, wz), wz);

            foreach (var entity in selection)
            {
                if (!em.Exists(entity)) continue;
                if (!em.HasComponent<UnitTag>(entity)) continue;
                if (!em.HasComponent<FactionTag>(entity)) continue;
                if (em.GetComponentData<FactionTag>(entity).Value != faction) continue;
                CommandRouter.IssueMove(em, entity, destination);
            }
        }
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
