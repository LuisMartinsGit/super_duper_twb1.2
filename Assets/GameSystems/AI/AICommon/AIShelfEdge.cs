// AIShelfEdge.cs
// "Is this spot on a cliff shelf's edge?" -- the shared test the tower placers
// use to stand towers where they shoot DOWN onto ramps and approaches
// (2026-10-17, docs/Design/Game_AI.md § Walls).
//
// A shelf edge: terrain-blocked ground (the cliff face) 4-16 m away in some
// direction, with walkable ground at least 3 m lower beyond it (18-26 m out).
// Not closer: on a partitioned map the cliff IS the territory border, and a tower
// keeps clear of the steep lip, so the legal spots stand a few metres back;
// a tower's 24 m reach still covers the ramp below. Terrain only
// (the PassabilityGrid's terrain mask and the heightmap), so every peer agrees;
// cached per 2 m cell, cleared when the region map changes (a new map).

using System.Collections.Generic;
using Unity.Mathematics;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public static class AIShelfEdge
    {
        static readonly Dictionary<long, bool> _cache = new Dictionary<long, bool>();
        static int _version = int.MinValue;

        public static bool Is(float2 p)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null || !grid.IsMaskReady) return false;
            if (_version != RegionMap.Version) { _cache.Clear(); _version = RegionMap.Version; }
            long key = ((long)(int)math.floor(p.x * 0.5f) << 32) ^ (uint)(int)math.floor(p.y * 0.5f);
            if (_cache.TryGetValue(key, out bool known)) return known;

            float h0 = TerrainUtility.GetHeight(p.x, p.y);
            bool edge = false;
            for (int d = 0; d < 8 && !edge; d++)
            {
                float ang = d * (math.PI / 4f);
                float2 dir = new float2(math.cos(ang), math.sin(ang));
                bool blocked = false;
                for (float s = 4f; s <= 16f; s += 1f)
                {
                    float2 q = p + dir * s;
                    if (grid.GetCell(grid.WorldToCell(new float3(q.x, 0f, q.y))) == PassabilityGrid.TerrainBlocked)
                    { blocked = true; break; }
                }
                if (!blocked) continue;
                for (float s = 18f; s <= 26f; s += 2f)
                {
                    float2 q = p + dir * s;
                    if (grid.GetCell(grid.WorldToCell(new float3(q.x, 0f, q.y))) == PassabilityGrid.TerrainBlocked) continue;
                    if (TerrainUtility.GetHeight(q.x, q.y) <= h0 - 3f) { edge = true; break; }
                }
            }
            _cache[key] = edge;
            return edge;
        }
    }
}
