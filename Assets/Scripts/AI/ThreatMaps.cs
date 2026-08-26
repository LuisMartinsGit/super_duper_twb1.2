// ThreatMaps.cs
// Per-faction tactical threat grids (AI plan M1). Coarse 16 u cells holding an
// integer "danger to this faction" value: stamped from known enemy military
// presence (IntelSystem) and from own units taking damage, decayed every intel
// tick. Pure integer math so accumulation is deterministic under lockstep
// (the AI is host-only in multiplayer, but we keep the discipline anyway).
//
// This is TACTICAL data — unrelated to the GPU Influence terrain overlay.

using Unity.Mathematics;

namespace TheWaningBorder.AI
{
    public static class ThreatMaps
    {
        public const float CellSize = 16f;
        private const int MaxFactions = 8;

        // Decay multiplier per intel tick (1 s): v = v * 7 / 8 — half-life ~5 s.
        private const int DecayNum = 7;
        private const int DecayDen = 8;

        private static int _half = -1;
        private static int _w;
        private static int[][] _grids;

        /// <summary>Drop all grids (called when a new world boots).</summary>
        public static void ResetAll()
        {
            _grids = null;
            _half = -1;
        }

        private static void Ensure()
        {
            if (_grids != null && _half == GameSettings.MapHalfSize) return;
            _half = GameSettings.MapHalfSize;
            _w = math.max(2, (int)math.ceil((2f * _half) / CellSize));
            _grids = new int[MaxFactions][];
            for (int i = 0; i < MaxFactions; i++)
                _grids[i] = new int[_w * _w];
        }

        private static int FIdx(Faction f)
        {
            int fi = (int)f;
            if (fi < 0) fi = -fi;
            return fi % MaxFactions;
        }

        private static bool TryCell(float3 pos, out int idx)
        {
            int cx = (int)math.floor((pos.x + _half) / CellSize);
            int cz = (int)math.floor((pos.z + _half) / CellSize);
            if (cx < 0 || cz < 0 || cx >= _w || cz >= _w) { idx = 0; return false; }
            idx = cz * _w + cx;
            return true;
        }

        /// <summary>Add threat at a world position on <paramref name="victim"/>'s
        /// grid, with a half-strength splash into the 4-neighborhood.</summary>
        public static void Stamp(Faction victim, float3 pos, int amount)
        {
            if (amount <= 0) return;
            Ensure();
            if (!TryCell(pos, out int idx)) return;
            var g = _grids[FIdx(victim)];
            g[idx] += amount;
            int half = amount / 2;
            if (half > 0)
            {
                int cx = idx % _w, cz = idx / _w;
                if (cx > 0) g[idx - 1] += half;
                if (cx < _w - 1) g[idx + 1] += half;
                if (cz > 0) g[idx - _w] += half;
                if (cz < _w - 1) g[idx + _w] += half;
            }
        }

        /// <summary>Apply one decay step to every faction grid.</summary>
        public static void DecayAll()
        {
            if (_grids == null) return;
            for (int f = 0; f < MaxFactions; f++)
            {
                var g = _grids[f];
                for (int i = 0; i < g.Length; i++)
                {
                    int v = g[i];
                    if (v == 0) continue;
                    g[i] = v * DecayNum / DecayDen;
                }
            }
        }

        /// <summary>Threat value at a world position (0 when off-map / unstamped).</summary>
        public static int Sample(Faction f, float3 pos)
        {
            if (_grids == null) return 0;
            Ensure();
            return TryCell(pos, out int idx) ? _grids[FIdx(f)][idx] : 0;
        }

        /// <summary>Highest cell value within <paramref name="radius"/> of a position.</summary>
        public static int MaxInRadius(Faction f, float3 pos, float radius)
        {
            if (_grids == null) return 0;
            Ensure();
            var g = _grids[FIdx(f)];
            int r = math.max(0, (int)math.ceil(radius / CellSize));
            int cx = (int)math.floor((pos.x + _half) / CellSize);
            int cz = (int)math.floor((pos.z + _half) / CellSize);
            int best = 0;
            for (int dz = -r; dz <= r; dz++)
            {
                int z = cz + dz;
                if (z < 0 || z >= _w) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= _w) continue;
                    int v = g[z * _w + x];
                    if (v > best) best = v;
                }
            }
            return best;
        }
    }
}
