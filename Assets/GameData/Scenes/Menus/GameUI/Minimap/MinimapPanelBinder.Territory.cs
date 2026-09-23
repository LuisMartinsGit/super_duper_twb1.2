// MinimapPanelBinder.Territory.cs
// The territory overlay: which region owns each overlay pixel, the
// region lattice, the ownership outline and the influence tint.
// The expensive half (region-per-pixel) is cached once.

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
    public sealed partial class MinimapPanelBinder
    {
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
                // Region per cell is resolved ONCE by the influence map and
                // read by index here. Resolving it live — 16k RegionAt calls
                // on every 0.1 s refresh — was a 64 ms stall ten times a
                // second on any map with authored outlines (2026-09-16).
                var regionOf = TheWaningBorder.Influence.PlayerInfluenceMap.RegionOfCell();
                for (int cy = 0; cy < res; cy++)
                {
                    int row = cy * res;
                    for (int cx = 0; cx < res; cx++)
                    {
                        int region = regionOf[row + cx];
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

        /// <summary>
        /// Everything the territory layer says about a pixel that does NOT
        /// change between refreshes (2026-09-16): which tint it takes and
        /// whether it sits on an ownership seam. Rebuilt only when an owner
        /// or a cursed flag changes, or the partition does — and composited
        /// onto the fog in ONE pass per refresh by CompositeTerritory. The
        /// three passes this replaces (tint, lattice, outline) each walked
        /// all 65k pixels ten times a second, two of them asking
        /// FogOfWarSystem.IsRevealedToFaction per pixel; that was most of
        /// the refresh with nothing on the map changing.
        /// </summary>
        private byte[] _tintIdx;            // 0 none, 1..8 owner+1, 9 curse
        private int[] _layerOwners;         // _ownerOfRegion the layer was built from
        private bool[] _layerCursed;        // _cursedRegion the layer was built from
        private int _layerPartition = -1;   // _regionEdgeSeeds the layer was built from
        private bool _layerAnyOwned;
        private readonly Color32[] _tintColor = new Color32[10];

        private const byte TintCurse = 9;
        private static readonly Color32 LatticeLine = new Color32(18, 18, 22, 255);

        /// <summary>
        /// The territory layer over the fog: tint, region lattice, ownership
        /// outline — the same pixels the three separate passes produced,
        /// from the cached layer. Falls back to the raw influence field on a
        /// map with no partition.
        /// </summary>
        private void DrawTerritory(Faction faction)
        {
            if (!_territoryStateReady)
            {
                DrawInfluenceTintFromField(faction);
                DrawRegionLatticeOnly();
                return;
            }
            RebuildTerritoryLayerIfNeeded();
            CompositeTerritory();
        }

        private void RebuildTerritoryLayerIfNeeded()
        {
            int n = _ovW * _ovH;
            int regions = _ownerOfRegion.Length;
            bool dirty = _tintIdx == null || _tintIdx.Length != n
                || _territoryEdge == null || _territoryEdge.Length != n
                || _layerPartition != _regionEdgeSeeds
                || _layerOwners == null || _layerOwners.Length != regions;
            if (!dirty)
            {
                for (int r = 0; r < regions; r++)
                {
                    if (_layerOwners[r] == _ownerOfRegion[r] && _layerCursed[r] == _cursedRegion[r]) continue;
                    dirty = true;
                    break;
                }
            }
            if (!dirty) return;

            if (_tintIdx == null || _tintIdx.Length != n) _tintIdx = new byte[n];
            if (_territoryEdge == null || _territoryEdge.Length != n) _territoryEdge = new byte[n];
            if (_layerOwners == null || _layerOwners.Length != regions)
            {
                _layerOwners = new int[regions];
                _layerCursed = new bool[regions];
            }
            System.Array.Copy(_ownerOfRegion, _layerOwners, regions);
            System.Array.Copy(_cursedRegion, _layerCursed, regions);
            _layerPartition = _regionEdgeSeeds;

            for (int f = 0; f < 8; f++) _tintColor[f + 1] = FactionColors.Get((Faction)f);
            _tintColor[TintCurse] = TheWaningBorder.Influence.PlayerInfluenceMap.ChannelColor(
                TheWaningBorder.Influence.PlayerInfluenceMap.CurseChannel);

            _layerAnyOwned = false;
            for (int r = 0; r < regions && !_layerAnyOwned; r++)
                _layerAnyOwned = _ownerOfRegion[r] >= 0;

            // Tint: WHOLE TERRITORIES (docs/Design/Regions.md §2) — the fill
            // stops exactly where the outline does. A cursed territory reads
            // as cursed whoever nominally holds it: the curse is what is
            // standing on the ground.
            for (int i = 0; i < n; i++)
            {
                int region = _regionAtPixel[i];
                if (region < 0 || region >= regions) { _tintIdx[i] = 0; continue; }
                int owner = _ownerOfRegion[region];
                if (_cursedRegion[region]) _tintIdx[i] = TintCurse;
                else if (owner >= 0 && owner <= 7) _tintIdx[i] = (byte)(owner + 1);
                else _tintIdx[i] = 0;
            }

            // Outline: mark the OWNED side of every ownership seam. Marking
            // the owner's own pixel rather than its neighbour's is what keeps
            // two adjacent players' borders as two lines in two colours
            // instead of one shared line whose colour depends on iteration
            // order. One pixel wide and it stays one pixel.
            System.Array.Clear(_territoryEdge, 0, n);
            if (!_layerAnyOwned) return;
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
        }

        /// <summary>
        /// One pass over the overlay, in the order the old passes ran: tint
        /// over the fog, then the region lattice, then the ownership line.
        /// Tint and line are gated on the fog's per-pixel "explored" answer —
        /// territory you have EXPLORED shows like a remembered building,
        /// territory you have never seen shows nothing, or the map would
        /// hand you every faction's holdings through unexplored black.
        /// The lattice is map structure, not intel, and is not gated.
        /// </summary>
        private void CompositeTerritory()
        {
            int n = _ovW * _ovH;
            bool haveLattice = _regionEdge != null && _regionEdge.Length == n;
            bool unfogged = _unfogged;
            int drawn = 0;

            for (int i = 0; i < n; i++)
            {
                bool revealed = unfogged || _pixelRevealed[i] != 0;
                var p = _overlayPixels[i];

                byte t = _tintIdx[i];
                if (t != 0 && revealed)
                {
                    Color32 tint = _tintColor[t];
                    float blend = t == TintCurse ? 0.6f : 0.55f;
                    p.r = (byte)(p.r + (tint.r - p.r) * blend);
                    p.g = (byte)(p.g + (tint.g - p.g) * blend);
                    p.b = (byte)(p.b + (tint.b - p.b) * blend);
                    if (p.a < 165) p.a = 165; // reads solidly over the terrain image
                }

                if (haveLattice)
                {
                    byte e = _regionEdge[i];
                    if (e != 0)
                    {
                        float a = e / 255f;
                        p = new Color32(
                            (byte)Mathf.Lerp(p.r, LatticeLine.r, a),
                            (byte)Mathf.Lerp(p.g, LatticeLine.g, a),
                            (byte)Mathf.Lerp(p.b, LatticeLine.b, a),
                            (byte)Mathf.Max(p.a, (byte)(a * 255f)));
                    }
                }

                byte tag = _territoryEdge[i];
                if (tag != 0 && revealed)
                {
                    Color32 line = _tintColor[tag];   // owner + 1: same table as the tint
                    p = new Color32(line.r, line.g, line.b, 255);
                    drawn++;
                }

                _overlayPixels[i] = p;
            }

            if (drawn > 0 && !_territoryLogged)
            {
                _territoryLogged = true;
                Debug.Log($"[Minimap] territory outlines online — {_ownerOfRegion.Length} region(s), " +
                          $"{drawn} outline pixel(s).");
            }
        }

        /// <summary>The region lattice alone, for the no-partition fallback
        /// (it is still baked when the map has seeds but no ownership).</summary>
        private void DrawRegionLatticeOnly()
        {
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return;
            BuildRegionEdgeCache();
            if (_regionEdge == null) return;
            for (int i = 0; i < _regionEdge.Length; i++)
            {
                byte e = _regionEdge[i];
                if (e == 0) continue;
                var c = _overlayPixels[i];
                float a = e / 255f;
                _overlayPixels[i] = new Color32(
                    (byte)Mathf.Lerp(c.r, LatticeLine.r, a),
                    (byte)Mathf.Lerp(c.g, LatticeLine.g, a),
                    (byte)Mathf.Lerp(c.b, LatticeLine.b, a),
                    (byte)Mathf.Max(c.a, (byte)(a * 255f)));
            }
        }

        private void BuildRegionEdgeCache()
        {
            // Keyed on the partition VERSION: a second map with the same
            // number of regions used to keep the first map's lattice.
            int seeds = TheWaningBorder.World.Regions.RegionMap.Version;
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

        /// <summary>Owner faction index at an overlay pixel, or -1 for Natural
        /// ground, the curse, and anything off the partition.</summary>
        private int OwnerAtPixel(int index)
        {
            int r = _regionAtPixel[index];
            if (r < 0 || r >= _ownerOfRegion.Length) return -1;
            int owner = _ownerOfRegion[r];
            return owner >= 0 && owner <= 7 ? owner : -1;
        }

        /// <summary>Influence-field tint for a map with NO partition — the only
        /// statement about ownership available on a map with no region seeds
        /// (the strongest channel at/over 0.5 owns the cell). Fog-gated like
        /// the territory tint.</summary>
        private void DrawInfluenceTintFromField(Faction faction)
        {
            bool unfogged = _unfogged;
            float bw = Mathf.Max(0.001f, _boundsMax.x - _boundsMin.x);
            float bh = Mathf.Max(0.001f, _boundsMax.y - _boundsMin.y);

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
    }
}
