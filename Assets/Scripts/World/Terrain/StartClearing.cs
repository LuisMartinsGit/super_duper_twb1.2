// StartClearing.cs
// The clear ground around every player start (docs/Design/Territory_Claims.md
// §11, 2026-10-06).
//
// Within StartClearing.asset → radiusCells build cells of each player's start
// position (the Fortress spot PlayerSpawnSystem resolved):
//
//   * NO RESOURCE NODE stands — supply, iron, veilstone, veilsteel, and so no
//     curse node either (the curse rises only on resource nodes). Enforced in
//     ResourceNodeSite.IsLegal, the one legality test every node factory
//     resolves through, so the territory generator, the marker bootstraps,
//     the mirrored-map copies and the curse's later precipitation all obey it.
//     TerritoryResources also skips the clearing up front.
//   * NO IMPASSABLE PAINT. Every terrain layer named "NoWalk" is removed from
//     the alphamap inside the circle, its weight handed to the dominant
//     walkable layer around the start, so the ground LOOKS walkable as well as
//     being walkable; then PassabilityGrid re-tests the terrain-blocked cells
//     in the circle (ReleaseTerrainInCircles), which bumps its MaskGeneration
//     and makes TerrainCostBakeSystem re-bake the nav cost field.
//   * NO TREES (terrain tree instances), and no forest stand blocks a cell
//     (NatureRegionBootstrap releases the stands' discs inside the circle).
//
// What the clearing does NOT change: the heightmap. Ground the incline budget
// or the water line blocks (a cliff, a lake) stays blocked — it is geometry,
// not paint, and flattening it would move every height sample in the match.
//
// Deterministic: a pure function of the map's terrain data and the sorted
// start positions, run once at match load on every lockstep peer before any
// node spawns and before reachability is computed.
//
// The terrain edit is made on a per-match CLONE of the TerrainData: the maps
// ship their TerrainData as assets, and an edit in a Play-mode session would
// otherwise be written into the asset for good.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World.Terrain
{
    public static class StartClearing
    {
        private static StartClearingConfig _cfg;
        private static StartClearingConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<StartClearingConfig>());

        /// <summary>Clearing radius in metres (StartClearing.asset → radiusCells
        /// × BuildGrid.CellSize). 0 when the config is missing (logged loudly
        /// by ComponentConfig.Require).</summary>
        public static float Radius
        {
            get
            {
                var cfg = Cfg;
                return cfg != null ? math.max(0, cfg.radiusCells) * BuildGrid.CellSize : 0f;
            }
        }

        private static readonly List<float3> _starts = new();
        private static readonly float3[] NoStarts = new float3[0];
        private static int _epoch = int.MinValue;

        /// <summary>The start positions this match's clearing was applied
        /// around — empty until <see cref="Apply"/> runs in THIS match (static
        /// state survives into the next match in the same process, so it is
        /// keyed to MatchLifecycle.MatchEpoch).</summary>
        public static IReadOnlyList<float3> Starts =>
            _epoch == MatchLifecycle.MatchEpoch ? (IReadOnlyList<float3>)_starts : NoStarts;

        /// <summary>
        /// True when a square of half-size <paramref name="half"/> centred on
        /// <paramref name="centre"/> (XZ) reaches into any start's clearing.
        /// </summary>
        public static bool Covers(float3 centre, float half)
        {
            var starts = Starts;
            if (starts.Count == 0) return false;
            float r = Radius;
            if (r <= 0f) return false;
            float r2 = r * r;
            for (int i = 0; i < starts.Count; i++)
            {
                var s = starts[i];
                float dx = math.max(0f, math.abs(s.x - centre.x) - half);
                float dz = math.max(0f, math.abs(s.z - centre.z) - half);
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        // ── Applying it ────────────────────────────────────────────────────

        /// <summary>The per-match TerrainData clones made by this class.</summary>
        private static readonly List<TerrainData> _clones = new();

        /// <summary>
        /// Record this match's starts and clear the ground around them:
        /// strip NoWalk paint and trees from the terrain, then reopen the
        /// passability cells the paint was blocking. Call once, on every
        /// peer, after the Fortresses are placed and BEFORE nature regions,
        /// reachability and resource nodes. <paramref name="starts"/> must be
        /// in the same order on every peer (sorted by faction).
        /// </summary>
        public static void Apply(IReadOnlyList<float3> starts)
        {
            _starts.Clear();
            if (starts != null)
                for (int i = 0; i < starts.Count; i++) _starts.Add(starts[i]);
            _epoch = MatchLifecycle.MatchEpoch;

            DestroyStaleClones();

            float r = Radius;
            if (r <= 0f || _starts.Count == 0) return;

            int texels = 0, trees = 0;
            var terrains = UnityEngine.Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null || t.terrainData == null) continue;
                texels += ClearPaint(t, r);
                trees += ClearTrees(t, r);
            }

            int cells = PassabilityGrid.Instance != null
                ? PassabilityGrid.Instance.ReleaseTerrainInCircles(_starts, r)
                : 0;

            TWBLog.Log($"[StartClearing] {r:0.#} m around {_starts.Count} start(s): " +
                       $"{texels} NoWalk texel(s) repainted, {trees} tree(s) removed, " +
                       $"{cells} passability cell(s) reopened.");
        }

        /// <summary>
        /// Strip every NoWalk layer from the alphamap texels the clearing
        /// touches, handing the weight to the dominant walkable layer within
        /// twice the radius (ties to the lower layer index). Returns how many
        /// texels changed.
        /// </summary>
        private static int ClearPaint(UnityEngine.Terrain t, float r)
        {
            var layers = t.terrainData.terrainLayers;
            int layerCount = t.terrainData.alphamapLayers;
            if (layers == null || layerCount <= 1) return 0;

            var isNoWalk = new bool[layerCount];
            bool anyNoWalk = false, anyWalkable = false;
            for (int l = 0; l < layerCount; l++)
            {
                var layer = l < layers.Length ? layers[l] : null;
                isNoWalk[l] = layer != null
                              && layer.name.IndexOf("nowalk", System.StringComparison.OrdinalIgnoreCase) >= 0;
                anyNoWalk |= isNoWalk[l];
                anyWalkable |= !isNoWalk[l];
            }
            if (!anyNoWalk || !anyWalkable) return 0;

            int changed = 0;
            Vector3 origin = t.transform.position;
            for (int s = 0; s < _starts.Count; s++)
            {
                var td = t.terrainData;   // the clone once the first start has edited it
                Vector3 size = td.size;
                int w = td.alphamapWidth, h = td.alphamapHeight;
                if (w <= 0 || h <= 0 || size.x <= 0f || size.z <= 0f) return changed;
                float sx = size.x / w, sz = size.z / h;
                float cx = _starts[s].x, cz = _starts[s].z;

                // The block read covers 2r: the outer ring only votes for the
                // fill layer; texels are edited inside r alone.
                float reach = r * 2f;
                int x0 = math.max(0, (int)math.floor((cx - reach - origin.x) / sx));
                int x1 = math.min(w - 1, (int)math.floor((cx + reach - origin.x) / sx));
                int y0 = math.max(0, (int)math.floor((cz - reach - origin.z) / sz));
                int y1 = math.min(h - 1, (int)math.floor((cz + reach - origin.z) / sz));
                if (x0 > x1 || y0 > y1) continue;   // this start is off this terrain

                int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                float[,,] block = td.GetAlphamaps(x0, y0, bw, bh);

                // Vote: the walkable layer carrying the most weight around the start.
                var votes = new float[layerCount];
                bool anyPaint = false;
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                    {
                        float d2 = TexelDistSq(x0 + x, y0 + y, sx, sz, origin, cx, cz);
                        if (d2 >= reach * reach) continue;
                        for (int l = 0; l < layerCount; l++)
                        {
                            if (isNoWalk[l]) { if (d2 < r * r && block[y, x, l] > 0f) anyPaint = true; }
                            else votes[l] += block[y, x, l];
                        }
                    }
                if (!anyPaint) continue;

                int fill = -1;
                for (int l = 0; l < layerCount; l++)
                {
                    if (isNoWalk[l]) continue;
                    if (fill < 0 || votes[l] > votes[fill]) fill = l;
                }
                if (fill < 0) continue;

                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                    {
                        if (TexelDistSq(x0 + x, y0 + y, sx, sz, origin, cx, cz) >= r * r) continue;
                        float moved = 0f;
                        for (int l = 0; l < layerCount; l++)
                        {
                            if (!isNoWalk[l] || block[y, x, l] <= 0f) continue;
                            moved += block[y, x, l];
                            block[y, x, l] = 0f;
                        }
                        if (moved <= 0f) continue;
                        block[y, x, fill] = math.min(1f, block[y, x, fill] + moved);
                        changed++;
                    }

                EnsureClone(t).SetAlphamaps(x0, y0, block);
            }
            return changed;
        }

        /// <summary>Squared XZ distance from (cx, cz) to the nearest point of
        /// alphamap texel (ax, ay). A texel is cleared when ANY of it is inside
        /// the circle, so every passability cell whose centre lies in the
        /// circle samples a cleared texel (PassabilityGrid floors u × width).</summary>
        private static float TexelDistSq(int ax, int ay, float sx, float sz, Vector3 origin,
            float cx, float cz)
        {
            float minX = origin.x + ax * sx, minZ = origin.z + ay * sz;
            float dx = math.max(0f, math.max(minX - cx, cx - (minX + sx)));
            float dz = math.max(0f, math.max(minZ - cz, cz - (minZ + sz)));
            return dx * dx + dz * dz;
        }

        /// <summary>Remove every terrain tree instance standing inside a
        /// clearing. Returns how many went.</summary>
        private static int ClearTrees(UnityEngine.Terrain t, float r)
        {
            var td = t.terrainData;
            var trees = td.treeInstances;
            if (trees == null || trees.Length == 0) return 0;

            Vector3 origin = t.transform.position;
            Vector3 size = td.size;
            float r2 = r * r;
            var kept = new List<TreeInstance>(trees.Length);
            for (int i = 0; i < trees.Length; i++)
            {
                float wx = origin.x + trees[i].position.x * size.x;
                float wz = origin.z + trees[i].position.z * size.z;
                bool inside = false;
                for (int s = 0; s < _starts.Count && !inside; s++)
                {
                    float dx = wx - _starts[s].x, dz = wz - _starts[s].z;
                    inside = dx * dx + dz * dz < r2;
                }
                if (!inside) kept.Add(trees[i]);
            }

            int removed = trees.Length - kept.Count;
            if (removed > 0)
                EnsureClone(t).treeInstances = kept.ToArray();
            return removed;
        }

        /// <summary>The terrain's data, swapped for a per-match clone the
        /// first time this match edits it (terrain and collider both).</summary>
        private static TerrainData EnsureClone(UnityEngine.Terrain t)
        {
            var td = t.terrainData;
            if (_clones.Contains(td)) return td;

            var clone = UnityEngine.Object.Instantiate(td);
            clone.name = td.name + " (start clearing)";
            t.terrainData = clone;
            var col = t.GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = clone;
            _clones.Add(clone);
            return clone;
        }

        /// <summary>Destroy clones left by an earlier match whose terrain is
        /// gone (the scene was unloaded) — they would otherwise leak a whole
        /// TerrainData per match played in the process.</summary>
        private static void DestroyStaleClones()
        {
            if (_clones.Count == 0) return;
            var live = UnityEngine.Terrain.activeTerrains;
            for (int i = _clones.Count - 1; i >= 0; i--)
            {
                var c = _clones[i];
                bool inUse = false;
                for (int k = 0; k < live.Length && !inUse; k++)
                    inUse = live[k] != null && live[k].terrainData == c;
                if (c == null || !inUse)
                {
                    if (c != null) UnityEngine.Object.Destroy(c);
                    _clones.RemoveAt(i);
                }
            }
        }
    }
}
