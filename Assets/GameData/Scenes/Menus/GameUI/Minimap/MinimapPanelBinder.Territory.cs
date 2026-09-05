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
    }
}
