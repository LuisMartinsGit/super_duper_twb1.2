// PlayerInfluenceMap.cs
// CPU influence grid — design: docs/Design/Overview.md § The influence map.
//
// One channel per player (0..7, indexed by Faction) plus the curse (8).
// "Neutral" is not a channel: a cell whose strongest channel sits below
// NeutralThreshold simply reads as neutral (no colour). The whole map
// starts at 0.
//
// Sources deposit per simulation tick with linear falloff and everything
// decays slowly, so influence visibly *spreads* outward while a source
// lives (near cells cross the display threshold first, far cells later)
// and fades back to neutral when it dies.
//
// Written by InfluenceMapSystem; read by MinimapRenderer. Territory is
// public information by design — the overlay is deliberately NOT
// fog-of-war gated.
//
// (This replaces the older dormant GPU per-culture stack in this folder —
// InfluenceManager / AlanthorInfluence / RunaiiInfluence / FeraldisInfluence
// — which was never mounted in a scene and painted culture channels rather
// than the per-player + curse channels the design now calls for.)

using UnityEngine;

namespace TheWaningBorder.Influence
{
    public static class PlayerInfluenceMap
    {
        public const int PlayerChannels = 8;
        public const int CurseChannel = 8;
        public const int ChannelCount = 9;
        public const int Resolution = 128;
        public const float MaxValue = 100f;

        /// <summary>Below this normalized strength a cell reads as neutral.</summary>
        public const float NeutralThreshold = 0.10f;

        /// <summary>Curse influence colour (design: purple).</summary>
        public static readonly Color CurseColor = new Color(0.58f, 0.27f, 0.82f);

        private static float[] _values; // [ (y * Resolution + x) * ChannelCount + channel ]
        private static Vector2 _worldMin;
        private static Vector2 _worldSize;

        public static bool Ready { get; private set; }

        /// <summary>World-space XZ of the grid's minimum corner.</summary>
        public static Vector2 WorldMin => _worldMin;

        /// <summary>World-space XZ extent covered by the grid.</summary>
        public static Vector2 WorldSize => _worldSize;

        public static void Configure(Vector2 worldMin, Vector2 worldSize)
        {
            _worldMin = worldMin;
            _worldSize = new Vector2(Mathf.Max(1f, worldSize.x), Mathf.Max(1f, worldSize.y));
            _values = new float[Resolution * Resolution * ChannelCount];
            Ready = true;
        }

        /// <summary>Drop all data; the next Configure starts from all-neutral.
        /// Called when a new match world spins up.</summary>
        public static void Reset()
        {
            Ready = false;
            _values = null;
        }

        /// <summary>Uniform decay toward neutral. Proportional
        /// (<paramref name="fraction"/> of the current value per call) so
        /// territory collapses back to neutral within tens of seconds of
        /// losing its source, plus a small <paramref name="linear"/> term so
        /// cells actually reach 0 instead of asymptoting forever.
        /// <para><paramref name="exemptChannelMask"/> (bit i = channel i)
        /// names channels that do NOT decay at all — Feraldis territory is
        /// permanent and recedes only where something takes it, which is
        /// <see cref="DecayOutranked"/>'s job (design:
        /// docs/Design/Age_1_Feraldis.md § Feraldis influence never
        /// decays).</para></summary>
        public static void Decay(float fraction, float linear, int exemptChannelMask = 0)
        {
            if (!Ready) return;
            float keep = 1f - Mathf.Clamp01(fraction);
            var v = _values;

            if (exemptChannelMask == 0)
            {
                for (int i = 0; i < v.Length; i++)
                {
                    float x = v[i] * keep - linear;
                    v[i] = x > 0f ? x : 0f;
                }
                return;
            }

            for (int i = 0; i < v.Length; i += ChannelCount)
            {
                for (int c = 0; c < ChannelCount; c++)
                {
                    if ((exemptChannelMask & (1 << c)) != 0) continue;
                    float x = v[i + c] * keep - linear;
                    v[i + c] = x > 0f ? x : 0f;
                }
            }
        }

        /// <summary>The "can only be REPLACED" half of a non-decaying
        /// channel: the channels in <paramref name="channelMask"/> lose
        /// strength on a cell only while some OTHER channel (any other
        /// player, or the curse) sits at or above them there. Everywhere
        /// else they hold their value forever.
        /// <para>The comparison is deliberately "at or above", not "above":
        /// values saturate at <see cref="MaxValue"/>, so under a strict
        /// greater-than a saturated cell could never be contested at all and
        /// a maxed Feraldis claim would be permanently unconquerable.</para>
        /// </summary>
        public static void DecayOutranked(int channelMask, float fraction, float linear)
        {
            if (!Ready || channelMask == 0) return;
            float keep = 1f - Mathf.Clamp01(fraction);
            var v = _values;

            for (int i = 0; i < v.Length; i += ChannelCount)
            {
                // Top two values on this cell: for the strongest channel the
                // best rival is the runner-up, for every other channel it is
                // the leader. One pass, no per-channel rescan.
                float best = 0f, second = 0f;
                int bestChannel = -1;
                for (int c = 0; c < ChannelCount; c++)
                {
                    float x = v[i + c];
                    if (x > best) { second = best; best = x; bestChannel = c; }
                    else if (x > second) second = x;
                }
                if (best <= 0f) continue;

                for (int c = 0; c < ChannelCount; c++)
                {
                    if ((channelMask & (1 << c)) == 0) continue;
                    float mine = v[i + c];
                    if (mine <= 0f) continue;
                    float rival = c == bestChannel ? second : best;
                    if (rival <= 0f || rival < mine) continue; // unchallenged — holds
                    float x = mine * keep - linear;
                    v[i + c] = x > 0f ? x : 0f;
                }
            }
        }

        /// <summary>Deposit influence around a world position with linear
        /// falloff — full amount at the centre, zero at the radius.</summary>
        public static void Deposit(float worldX, float worldZ, float radius, int channel, float amount)
        {
            if (!Ready || channel < 0 || channel >= ChannelCount || radius <= 0f) return;

            float cellW = _worldSize.x / Resolution;
            float cellH = _worldSize.y / Resolution;
            float u = (worldX - _worldMin.x) / _worldSize.x * Resolution;
            float v = (worldZ - _worldMin.y) / _worldSize.y * Resolution;
            int rx = Mathf.CeilToInt(radius / cellW);
            int ry = Mathf.CeilToInt(radius / cellH);
            int cx = Mathf.FloorToInt(u);
            int cy = Mathf.FloorToInt(v);

            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (y < 0 || y >= Resolution) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= Resolution) continue;
                    float dx = (x + 0.5f - u) * cellW;
                    float dz = (y + 0.5f - v) * cellH;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist >= radius) continue;

                    float falloff = 1f - dist / radius;
                    int idx = (y * Resolution + x) * ChannelCount + channel;
                    float nv = _values[idx] + amount * falloff;
                    _values[idx] = nv > MaxValue ? MaxValue : nv;
                }
            }
        }

        /// <summary>
        /// Zero a disc of influence, the counterpart to <see cref="Deposit"/> —
        /// the same relationship <see cref="BloodMap.Drain"/> has with
        /// <see cref="BloodMap.AddBlood"/>.
        ///
        /// This exists because <see cref="Deposit"/> clamps only the UPPER
        /// bound, so a negative amount would drive cells below zero and leave
        /// territory that needs an equal positive deposit before it reads as
        /// neutral again. Erasing is a distinct operation, not negative
        /// depositing.
        /// </summary>
        /// <param name="channel">Channel to clear, or -1 for every channel.</param>
        public static void Erase(float worldX, float worldZ, float radius, int channel = -1)
        {
            if (!Ready || radius <= 0f) return;
            if (channel >= ChannelCount) return;

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

                    int cell = (y * Resolution + x) * ChannelCount;
                    if (channel < 0)
                        for (int c = 0; c < ChannelCount; c++) _values[cell + c] = 0f;
                    else
                        _values[cell + channel] = 0f;
                }
            }
        }

        /// <summary>Dominant channel at a world position. Returns false when
        /// the cell is neutral (no channel above the display threshold) or
        /// the position is off-map.</summary>
        public static bool Sample(float worldX, float worldZ, out int channel, out float strength01)
        {
            channel = -1;
            strength01 = 0f;
            if (!Ready) return false;

            int x = Mathf.FloorToInt((worldX - _worldMin.x) / _worldSize.x * Resolution);
            int y = Mathf.FloorToInt((worldZ - _worldMin.y) / _worldSize.y * Resolution);
            if (x < 0 || x >= Resolution || y < 0 || y >= Resolution) return false;

            int baseIdx = (y * Resolution + x) * ChannelCount;
            float best = 0f;
            for (int c = 0; c < ChannelCount; c++)
            {
                float v = _values[baseIdx + c];
                if (v > best) { best = v; channel = c; }
            }

            strength01 = best / MaxValue;
            if (strength01 < NeutralThreshold)
            {
                channel = -1;
                strength01 = 0f;
                return false;
            }
            return true;
        }

        /// <summary>Raw normalized value (0..1) of one channel at one grid
        /// cell. Used by the border-contour tracer (marching squares).</summary>
        public static float CellValue(int x, int y, int channel)
        {
            if (!Ready || x < 0 || x >= Resolution || y < 0 || y >= Resolution
                || channel < 0 || channel >= ChannelCount) return 0f;
            return _values[(y * Resolution + x) * ChannelCount + channel] / MaxValue;
        }

        /// <summary>True when the channel has any cell at or above the given
        /// normalized strength — cheap presence scan so per-channel passes
        /// can skip empty channels.</summary>
        public static bool ChannelHasPresence(int channel, float threshold01)
        {
            if (!Ready || channel < 0 || channel >= ChannelCount) return false;
            float t = threshold01 * MaxValue;
            for (int i = channel; i < _values.Length; i += ChannelCount)
                if (_values[i] >= t) return true;
            return false;
        }

        /// <summary>Dominant channel of a grid cell by cell coordinates —
        /// same semantics as <see cref="Sample"/> but skips the world→cell
        /// math. Used by the overlay texture builder.</summary>
        public static bool SampleCell(int x, int y, out int channel, out float strength01)
        {
            channel = -1;
            strength01 = 0f;
            if (!Ready || x < 0 || x >= Resolution || y < 0 || y >= Resolution) return false;

            int baseIdx = (y * Resolution + x) * ChannelCount;
            float best = 0f;
            for (int c = 0; c < ChannelCount; c++)
            {
                float v = _values[baseIdx + c];
                if (v > best) { best = v; channel = c; }
            }

            strength01 = best / MaxValue;
            if (strength01 < NeutralThreshold)
            {
                channel = -1;
                strength01 = 0f;
                return false;
            }
            return true;
        }

        /// <summary>Bilinearly interpolated dominant channel at normalized
        /// grid coordinates (u, v in 0..1). Interpolates every channel across
        /// the four surrounding cells before picking the strongest, so
        /// territory boundaries read as continuous shapes instead of cell
        /// blobs. Returns false when the point is neutral. Unlike the
        /// cell-based samplers, <paramref name="strength01"/> is reported
        /// even below the neutral threshold so callers can find the 0.5
        /// border contour reliably.</summary>
        public static bool SampleSmooth(float u, float v, out int channel, out float strength01)
        {
            channel = -1;
            strength01 = 0f;
            if (!Ready) return false;

            // Cell centres sit at integer coordinates in this space.
            float fx = Mathf.Clamp(u * Resolution - 0.5f, 0f, Resolution - 1f);
            float fy = Mathf.Clamp(v * Resolution - 0.5f, 0f, Resolution - 1f);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Mathf.Min(x0 + 1, Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, Resolution - 1);
            float tx = fx - x0, ty = fy - y0;

            int i00 = (y0 * Resolution + x0) * ChannelCount;
            int i10 = (y0 * Resolution + x1) * ChannelCount;
            int i01 = (y1 * Resolution + x0) * ChannelCount;
            int i11 = (y1 * Resolution + x1) * ChannelCount;

            float best = 0f;
            for (int c = 0; c < ChannelCount; c++)
            {
                float top = _values[i00 + c] + (_values[i10 + c] - _values[i00 + c]) * tx;
                float bot = _values[i01 + c] + (_values[i11 + c] - _values[i01 + c]) * tx;
                float val = top + (bot - top) * ty;
                if (val > best) { best = val; channel = c; }
            }

            strength01 = best / MaxValue;
            if (strength01 < NeutralThreshold)
            {
                channel = -1;
                return false;
            }
            return true;
        }

        /// <summary>Bilinearly interpolated strength of ONE specific channel
        /// at a world position (0..1), regardless of dominance. Gameplay
        /// checks use this: Alanthor build gating and the economy doubling
        /// both ask "is MY influence ≥ 0.5 here?".</summary>
        public static float ChannelStrengthWorld(int channel, float worldX, float worldZ)
        {
            if (!Ready || channel < 0 || channel >= ChannelCount) return 0f;

            float u = (worldX - _worldMin.x) / _worldSize.x;
            float v = (worldZ - _worldMin.y) / _worldSize.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;

            float fx = Mathf.Clamp(u * Resolution - 0.5f, 0f, Resolution - 1f);
            float fy = Mathf.Clamp(v * Resolution - 0.5f, 0f, Resolution - 1f);
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Mathf.Min(x0 + 1, Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, Resolution - 1);
            float tx = fx - x0, ty = fy - y0;

            float v00 = _values[(y0 * Resolution + x0) * ChannelCount + channel];
            float v10 = _values[(y0 * Resolution + x1) * ChannelCount + channel];
            float v01 = _values[(y1 * Resolution + x0) * ChannelCount + channel];
            float v11 = _values[(y1 * Resolution + x1) * ChannelCount + channel];

            float top = v00 + (v10 - v00) * tx;
            float bot = v01 + (v11 - v01) * tx;
            return (top + (bot - top) * ty) / MaxValue;
        }

        /// <summary>World-space variant of <see cref="SampleSmooth"/>.</summary>
        public static bool SampleSmoothWorld(float worldX, float worldZ, out int channel, out float strength01)
        {
            channel = -1;
            strength01 = 0f;
            if (!Ready) return false;
            float u = (worldX - _worldMin.x) / _worldSize.x;
            float v = (worldZ - _worldMin.y) / _worldSize.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            return SampleSmooth(u, v, out channel, out strength01);
        }

        /// <summary>Fill <paramref name="dst"/> (<see cref="Resolution"/>²
        /// entries, row-major) with the index of the strongest channel on
        /// each cell, or -1 where the cell is empty. The border tracer needs
        /// dominance for every cell of every channel; deriving it once here
        /// is one pass over the grid instead of nine.</summary>
        public static void FillDominantChannels(sbyte[] dst)
        {
            if (!Ready || dst == null || dst.Length < Resolution * Resolution) return;
            var v = _values;
            for (int cell = 0, i = 0; cell < Resolution * Resolution; cell++, i += ChannelCount)
            {
                float best = 0f;
                int bestChannel = -1;
                for (int c = 0; c < ChannelCount; c++)
                {
                    float x = v[i + c];
                    if (x > best) { best = x; bestChannel = c; }
                }
                dst[cell] = (sbyte)bestChannel;
            }
        }

        /// <summary>Fill <paramref name="dst"/> with one channel's
        /// normalized (0..1) value per cell, row-major. Strided single pass —
        /// far cheaper than <see cref="CellValue"/> per cell.</summary>
        public static void FillChannel(int channel, float[] dst)
        {
            if (!Ready || dst == null || channel < 0 || channel >= ChannelCount
                || dst.Length < Resolution * Resolution) return;
            var v = _values;
            for (int cell = 0, i = channel; cell < Resolution * Resolution; cell++, i += ChannelCount)
                dst[cell] = v[i] / MaxValue;
        }

        /// <summary>Display colour: players use their banner colour, the
        /// curse is purple. Neutral cells never reach this call (Sample
        /// returns false for them).</summary>
        public static Color ChannelColor(int channel)
        {
            if (channel == CurseChannel) return CurseColor;
            if (channel >= 0 && channel < PlayerChannels) return FactionColors.Get((Faction)channel);
            return Color.clear;
        }
    }
}
