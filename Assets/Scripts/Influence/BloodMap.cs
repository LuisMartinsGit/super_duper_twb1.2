// BloodMap.cs
// Independent single-channel grid tracking blood spilled on the ground.
// Every non-Border unit death deposits blood at its position (DeathSystem
// hook), scaled by the unit's max HP. Blood fades very slowly, so old
// battlefields stay stained for minutes.
//
// Configured/decayed alongside PlayerInfluenceMap by InfluenceMapSystem;
// visualized by InfluenceTerrainPainter (terrain layer named "Blood") —
// and available for future Feraldis mechanics (blood-fueled claims).

using UnityEngine;

namespace TheWaningBorder.Influence
{
    public static class BloodMap
    {
        public const int Resolution = 128;
        public const float MaxValue = 100f;

        /// <summary>World-space splat radius of one death. Tightened 5 -> 2.5
        /// (2026-08-04): a single death now reads as a small LOCAL puddle,
        /// and clustered deaths no longer blob into one oversized pool.
        /// <para>Public so callers that need to COVER an area can tile splats
        /// at the right spacing instead of copying the number — AddBlood's
        /// radius is fixed, unlike Drain's.</para></summary>
        public const float SplatRadius = 2.5f;

        /// <summary>Blood value a full splat (amount = 1) deposits at its
        /// centre. Raised with the radius cut (70 -> 120, 2026-08-04) so ONE
        /// footsoldier death (~0.25 amount -> 30/100 centre) crosses the
        /// display threshold on its own instead of needing 2-3 deaths.</summary>
        private const float SplatStrength = 120f;

        private static float[] _values;
        private static Vector2 _worldMin;
        private static Vector2 _worldSize;

        public static bool Ready { get; private set; }

        public static void Configure(Vector2 worldMin, Vector2 worldSize)
        {
            _worldMin = worldMin;
            _worldSize = new Vector2(Mathf.Max(1f, worldSize.x), Mathf.Max(1f, worldSize.y));
            _values = new float[Resolution * Resolution];
            Ready = true;
        }

        public static void Reset()
        {
            Ready = false;
            _values = null;
        }

        /// <summary>Deposit a blood splat. <paramref name="amount"/> in 0..1
        /// (DeathSystem passes maxHP/200 clamped — a footsoldier ~0.25, a
        /// hero 1.0). Linear falloff to the splat radius.</summary>
        public static void AddBlood(Vector3 worldPos, float amount)
        {
            if (!Ready || amount <= 0f) return;

            float cellW = _worldSize.x / Resolution;
            float cellH = _worldSize.y / Resolution;
            float u = (worldPos.x - _worldMin.x) / _worldSize.x * Resolution;
            float v = (worldPos.z - _worldMin.y) / _worldSize.y * Resolution;
            int rx = Mathf.CeilToInt(SplatRadius / cellW);
            int ry = Mathf.CeilToInt(SplatRadius / cellH);
            int cx = Mathf.FloorToInt(u);
            int cy = Mathf.FloorToInt(v);
            float deposit = SplatStrength * Mathf.Clamp01(amount);

            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (y < 0 || y >= Resolution) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= Resolution) continue;
                    float dx = (x + 0.5f - u) * cellW;
                    float dz = (y + 0.5f - v) * cellH;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist >= SplatRadius) continue;

                    int idx = y * Resolution + x;
                    float nv = _values[idx] + deposit * (1f - dist / SplatRadius);
                    _values[idx] = nv > MaxValue ? MaxValue : nv;
                }
            }
        }

        /// <summary>Slow uniform fade (legacy — see
        /// <see cref="DecayInsideInfluence"/>, the §2.5b rev.3 path).</summary>
        public static void Decay(float fraction, float linear)
        {
            if (!Ready) return;
            float keep = 1f - Mathf.Clamp01(fraction);
            var v = _values;
            for (int i = 0; i < v.Length; i++)
            {
                float x = v[i] * keep - linear;
                v[i] = x > 0f ? x : 0f;
            }
        }

        /// <summary>§2.5b rev.3 fade: blood inside ANY player influence
        /// (channel strength >= threshold01) fades as before — tended ground
        /// is cleaned. Blood OUTSIDE influence is ETERNAL: the stain stays
        /// until something uses it (blood-curse spawns drain it). Both grids
        /// are 128² over the same terrain bounds, so cells correspond 1:1.</summary>
        public static void DecayInsideInfluence(float fraction, float linear, float threshold01)
        {
            if (!Ready) return;
            float keep = 1f - Mathf.Clamp01(fraction);
            var v = _values;
            for (int y = 0; y < Resolution; y++)
            {
                int row = y * Resolution;
                for (int x = 0; x < Resolution; x++)
                {
                    int idx = row + x;
                    if (v[idx] <= 0f) continue;

                    bool covered = false;
                    for (int c = 0; c < PlayerInfluenceMap.PlayerChannels && !covered; c++)
                        covered = PlayerInfluenceMap.CellValue(x, y, c) >= threshold01;
                    if (!covered) continue;

                    float nv = v[idx] * keep - linear;
                    v[idx] = nv > 0f ? nv : 0f;
                }
            }
        }

        /// <summary>Zero all blood within a world radius — a blood-curse
        /// spawn consumes the pool that birthed it.</summary>
        public static void Drain(float worldX, float worldZ, float radius)
        {
            if (!Ready || radius <= 0f) return;

            float cellW = _worldSize.x / Resolution;
            float cellH = _worldSize.y / Resolution;
            float u = (worldX - _worldMin.x) / _worldSize.x * Resolution;
            float v = (worldZ - _worldMin.y) / _worldSize.y * Resolution;
            int rx = Mathf.CeilToInt(radius / cellW);
            int ry = Mathf.CeilToInt(radius / cellH);
            int cx = Mathf.FloorToInt(u);
            int cy = Mathf.FloorToInt(v);
            float r2 = radius * radius;

            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (y < 0 || y >= Resolution) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= Resolution) continue;
                    float dx = (x + 0.5f - u) * cellW;
                    float dz = (y + 0.5f - v) * cellH;
                    if (dx * dx + dz * dz > r2) continue;
                    _values[y * Resolution + x] = 0f;
                }
            }
        }

        /// <summary>Scale down all blood within a world radius by
        /// <paramref name="fraction"/> (0..1) and return the MEAN normalized
        /// blood (0..1) that was there before the cut.
        ///
        /// Unlike <see cref="Drain"/> (which zeroes a disc outright), this is
        /// a partial take — a Feraldis War Totem drinking a slice of its pool
        /// each pulse, so a big pool feeds a totem for a long time. The mean
        /// (not the sum) is returned deliberately: cell size scales with the
        /// map, so a summed pool would make totems stronger on small maps.
        /// </summary>
        public static float Consume(float worldX, float worldZ, float radius, float fraction)
        {
            if (!Ready || radius <= 0f) return 0f;
            fraction = Mathf.Clamp01(fraction);

            float cellW = _worldSize.x / Resolution;
            float cellH = _worldSize.y / Resolution;
            float u = (worldX - _worldMin.x) / _worldSize.x * Resolution;
            float v = (worldZ - _worldMin.y) / _worldSize.y * Resolution;
            int rx = Mathf.CeilToInt(radius / cellW);
            int ry = Mathf.CeilToInt(radius / cellH);
            int cx = Mathf.FloorToInt(u);
            int cy = Mathf.FloorToInt(v);
            float r2 = radius * radius;

            float total = 0f;
            int count = 0;
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (y < 0 || y >= Resolution) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= Resolution) continue;
                    float dx = (x + 0.5f - u) * cellW;
                    float dz = (y + 0.5f - v) * cellH;
                    if (dx * dx + dz * dz > r2) continue;

                    int idx = y * Resolution + x;
                    total += _values[idx];
                    count++;
                    _values[idx] *= (1f - fraction);
                }
            }
            return count > 0 ? total / (count * MaxValue) : 0f;
        }

        /// <summary>Raw normalized value (0..1) of one grid cell — used by
        /// the border-contour tracer.</summary>
        public static float CellValue(int x, int y)
        {
            if (!Ready || x < 0 || x >= Resolution || y < 0 || y >= Resolution) return 0f;
            return _values[y * Resolution + x] / MaxValue;
        }

        /// <summary>True when any cell is at or above the given normalized
        /// strength — cheap presence check so display passes can skip an
        /// empty map.</summary>
        public static bool HasPresence(float threshold01)
        {
            if (!Ready) return false;
            float t = threshold01 * MaxValue;
            for (int i = 0; i < _values.Length; i++)
                if (_values[i] >= t) return true;
            return false;
        }

        /// <summary>Bilinear blood strength (0..1) at a world position.</summary>
        public static float SampleWorld(float worldX, float worldZ)
        {
            if (!Ready) return 0f;

            float u = (worldX - _worldMin.x) / _worldSize.x;
            float v = (worldZ - _worldMin.y) / _worldSize.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;

            float fx = Mathf.Clamp(u * Resolution - 0.5f, 0f, Resolution - 1f);
            float fy = Mathf.Clamp(v * Resolution - 0.5f, 0f, Resolution - 1f);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Mathf.Min(x0 + 1, Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, Resolution - 1);
            float tx = fx - x0, ty = fy - y0;

            float v00 = _values[y0 * Resolution + x0];
            float v10 = _values[y0 * Resolution + x1];
            float v01 = _values[y1 * Resolution + x0];
            float v11 = _values[y1 * Resolution + x1];

            float top = v00 + (v10 - v00) * tx;
            float bot = v01 + (v11 - v01) * tx;
            return (top + (bot - top) * ty) / MaxValue;
        }
    }
}
