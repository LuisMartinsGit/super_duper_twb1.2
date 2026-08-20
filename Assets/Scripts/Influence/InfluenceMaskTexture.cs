// InfluenceMaskTexture.cs
// Builds the small world-space coverage masks the TWB terrain shader
// (TWB/Terrain/Lit + TWBTerrainOverlays.hlsl) samples per-pixel:
//
//   _TWB_CultureMask (128² RGBA32)  R = Alanthor, G = Feraldis, B = Runai,
//                                   A = Curse — from PlayerInfluenceMap
//   _TWB_BloodMask   (128² RGBA32)  R = Blood — from BloodMap
//
// Coverage targets use the same ramps the old terrain painter used
// (influence 0.5→0.8, blood 0.15→0.7) and are EASED on the CPU at
// EasePerSecond, so the shader's fronts creep continuously. This is the
// entire per-frame cost of dynamic ground: ~16k texels of float math and
// two 64 KB texture uploads (skipped entirely when nothing is changing).
// It replaces ALL runtime SetAlphamaps painting — the terrain's splat data
// is never touched at runtime again.
//
// Fog of war: masks are not fog-gated. Unexplored ground is hidden by the
// fog overlay itself, and territory is public information by design (same
// rule as the minimap overlay).

using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Maps;

namespace TheWaningBorder.Influence
{
    [DefaultExecutionOrder(2000)]
    public sealed class InfluenceMaskTexture : MonoBehaviour
    {
        private const int Res = PlayerInfluenceMap.Resolution; // BloodMap matches
        private const float InfluenceStart = 0.5f; // border threshold
        private const float InfluenceFull = 0.8f;
        // BloodStart lowered 0.15 -> 0.10 (2026-08-04) so a single death's
        // tight puddle (centre ~0.3) renders clearly on its own.
        private const float BloodStart = 0.10f;
        private const float BloodFull = 0.7f;

        /// <summary>Max coverage change per second — the front's glide rate.</summary>
        private const float EasePerSecond = 0.2f;

        // Curse ground is driven ONLY by the curse influence channel (the
        // crust deposits into it every pulse, so the footprint tracks the
        // crystals). A max-dilate pass then stretches the coverage this many
        // mask cells outward so the corruption visibly reaches beyond the
        // crystal border. 1 mask cell ≈ mapSize/128 (4 m on a 512 m map).
        private const int CurseSpreadCells = 3;

        // ─── Auto-mount on gameplay scenes ────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!MapRegistry.IsGameplayScene(scene.name)) return;
            if (Object.FindFirstObjectByType<InfluenceMaskTexture>() != null) return;
            new GameObject("[Influence Mask Texture]")
                .AddComponent<InfluenceMaskTexture>();
        }

        // ─── State ────────────────────────────────────────────────────────
        private Texture2D _cultureTex;
        private Texture2D _bloodTex;
        private Color32[] _culturePixels;
        private Color32[] _bloodPixels;
        private float[] _eased;    // 5 channels per cell: A, F, R, curse, blood
        // ── Player frontier ────────────────────────────────────────────
        // The culture channels below collapse every player of a culture into
        // ONE colour (MaxChannel), so two Alanthor players with touching
        // territory painted as a single indistinguishable blob and neither
        // could see the frontier. These track WHICH player owns each cell so
        // a seam can be carved where two owners meet. -1 = unowned.
        private sbyte[] _owner;
        private bool[] _frontier;
        /// <summary>How much of the culture fill survives on a frontier cell.
        /// Low enough to read as a dark line through the territory colour.</summary>
        private const float FrontierFill = 0.30f;

        private float[] _curseRaw; // pre-dilation curse coverage
        private float[] _curseTmp; // separable dilation scratch
        private bool _configured;
        private bool _snapFirstFrame; // seed state appears instantly, no fade-in
        private static InfluenceMaskTexture _instance;

        private readonly System.Collections.Generic.List<int> _alanthorChannels = new();
        private readonly System.Collections.Generic.List<int> _feraldisChannels = new();
        private readonly System.Collections.Generic.List<int> _runaiChannels = new();

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Shader.SetGlobalFloat("_TWB_OverlaysEnabled", 0f);
            if (_cultureTex != null) Destroy(_cultureTex);
            if (_bloodTex != null) Destroy(_bloodTex);
        }

        /// <summary>Eased Alanthor ground coverage (0..1) at a world position
        /// — the same value the terrain shader blends slate/terraces with, so
        /// presentation systems (rampart props) can grow in lockstep with the
        /// painted front. Bilinear; 0 when no mask is live.</summary>
        public static float AlanthorCoverage(float worldX, float worldZ)
        {
            var inst = _instance;
            if (inst == null || !inst._configured) return 0f;

            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;
            float u = (worldX - min.x) / size.x * Res - 0.5f;
            float v = (worldZ - min.y) / size.y * Res - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, Res - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, Res - 1);
            int x1 = Mathf.Min(x0 + 1, Res - 1);
            int y1 = Mathf.Min(y0 + 1, Res - 1);
            float tx = Mathf.Clamp01(u - x0);
            float ty = Mathf.Clamp01(v - y0);

            float e00 = inst._eased[(y0 * Res + x0) * 5];
            float e10 = inst._eased[(y0 * Res + x1) * 5];
            float e01 = inst._eased[(y1 * Res + x0) * 5];
            float e11 = inst._eased[(y1 * Res + x1) * 5];
            float top = e00 + (e10 - e00) * tx;
            float bot = e01 + (e11 - e01) * tx;
            return top + (bot - top) * ty;
        }

        private void Update()
        {
            if (!PlayerInfluenceMap.Ready) return;
            if (!_configured) Configure();

            // Instrumented (2026-08-16 perf sweep): two 128x128 passes plus a
            // texture upload on most frames (the ease keeps values creeping).
            double perfT0 = Time.realtimeSinceStartupAsDouble;

            RefreshCultureChannels();

            // First frame snaps to targets so established territory shows
            // instantly instead of fading in from a clean map.
            float step = _snapFirstFrame ? float.MaxValue : EasePerSecond * Time.deltaTime;
            bool cultureDirty = _snapFirstFrame, bloodDirty = _snapFirstFrame;

            for (int y = 0; y < Res; y++)
            {
                int row = y * Res;
                for (int x = 0; x < Res; x++)
                {
                    int i = row + x;
                    int e = i * 5;

                    // Owner = the single strongest PLAYER channel on this cell,
                    // across every culture. Needed for the frontier pass below;
                    // the culture fills themselves stay culture-collapsed.
                    float aRaw = MaxChannelOwner(_alanthorChannels, x, y, out int aOwner);
                    float fRaw = MaxChannelOwner(_feraldisChannels, x, y, out int fOwner);
                    float rRaw = MaxChannelOwner(_runaiChannels,   x, y, out int rOwner);

                    int owner = -1;
                    float ownerVal = InfluenceStart;
                    if (aRaw > ownerVal) { ownerVal = aRaw; owner = aOwner; }
                    if (fRaw > ownerVal) { ownerVal = fRaw; owner = fOwner; }
                    if (rRaw > ownerVal) { ownerVal = rRaw; owner = rOwner; }
                    _owner[i] = (sbyte)owner;

                    float a = Ramp(aRaw, InfluenceStart, InfluenceFull);
                    float f = Ramp(fRaw, InfluenceStart, InfluenceFull);
                    float r = Ramp(rRaw, InfluenceStart, InfluenceFull);
                    float b = Ramp(BloodMap.CellValue(x, y), BloodStart, BloodFull);
                    _curseRaw[i] = Ramp(
                        PlayerInfluenceMap.CellValue(x, y, PlayerInfluenceMap.CurseChannel),
                        InfluenceStart, InfluenceFull);

                    cultureDirty |= Ease(ref _eased[e + 0], a, step);
                    cultureDirty |= Ease(ref _eased[e + 1], f, step);
                    cultureDirty |= Ease(ref _eased[e + 2], r, step);
                    bloodDirty   |= Ease(ref _eased[e + 4], b, step);

                    _culturePixels[i].r = (byte)(_eased[e + 0] * 255f);
                    _culturePixels[i].g = (byte)(_eased[e + 1] * 255f);
                    _culturePixels[i].b = (byte)(_eased[e + 2] * 255f);
                    _bloodPixels[i].r = (byte)(_eased[e + 4] * 255f);
                }
            }

            // ── Player frontier seam ─────────────────────────────────────
            // Where two DIFFERENT players own adjoining cells, carve the fill
            // back so a dark line reads through the territory colour. This is
            // what makes an Alanthor-vs-Alanthor border visible at all: both
            // players share one culture channel, so without this their fills
            // are literally the same colour and the frontier is invisible.
            //
            // Done as a data pass on the mask rather than a new channel or a
            // shader edit: RGB are the three cultures and A is the curse, so
            // there is no spare channel to put a border in.
            for (int y = 0; y < Res; y++)
            {
                int row = y * Res;
                for (int x = 0; x < Res; x++)
                {
                    int i = row + x;
                    sbyte mine = _owner[i];
                    bool edge = false;
                    if (mine >= 0)
                    {
                        // 4-neighbourhood is enough for a 1-cell seam and keeps
                        // the pass cheap; diagonals fill in visually.
                        if (x > 0        && _owner[i - 1]   >= 0 && _owner[i - 1]   != mine) edge = true;
                        else if (x < Res - 1 && _owner[i + 1]   >= 0 && _owner[i + 1]   != mine) edge = true;
                        else if (y > 0        && _owner[i - Res] >= 0 && _owner[i - Res] != mine) edge = true;
                        else if (y < Res - 1 && _owner[i + Res] >= 0 && _owner[i + Res] != mine) edge = true;
                    }
                    _frontier[i] = edge;
                }
            }

            for (int i = 0; i < _frontier.Length; i++)
            {
                if (!_frontier[i]) continue;
                _culturePixels[i].r = (byte)(_culturePixels[i].r * FrontierFill);
                _culturePixels[i].g = (byte)(_culturePixels[i].g * FrontierFill);
                _culturePixels[i].b = (byte)(_culturePixels[i].b * FrontierFill);
                cultureDirty = true;
            }

            // ── Curse halo: separable max-dilate with linear falloff, so the
            // cursed ground stretches CurseSpreadCells beyond the crust (and
            // therefore beyond the crystal border) and fades outward.
            for (int y = 0; y < Res; y++)
            {
                int row = y * Res;
                for (int x = 0; x < Res; x++)
                {
                    float best = 0f;
                    for (int dx = -CurseSpreadCells; dx <= CurseSpreadCells; dx++)
                    {
                        int sx = x + dx;
                        if (sx < 0 || sx >= Res) continue;
                        float w = 1f - Mathf.Abs(dx) / (float)(CurseSpreadCells + 1);
                        float v = _curseRaw[row + sx] * w;
                        if (v > best) best = v;
                    }
                    _curseTmp[row + x] = best;
                }
            }
            for (int y = 0; y < Res; y++)
            {
                int row = y * Res;
                for (int x = 0; x < Res; x++)
                {
                    float best = 0f;
                    for (int dz = -CurseSpreadCells; dz <= CurseSpreadCells; dz++)
                    {
                        int sy = y + dz;
                        if (sy < 0 || sy >= Res) continue;
                        float w = 1f - Mathf.Abs(dz) / (float)(CurseSpreadCells + 1);
                        float v = _curseTmp[sy * Res + x] * w;
                        if (v > best) best = v;
                    }
                    int i = row + x;
                    cultureDirty |= Ease(ref _eased[i * 5 + 3], best, step);
                    _culturePixels[i].a = (byte)(_eased[i * 5 + 3] * 255f);
                }
            }

            // Idle maps upload nothing.
            if (cultureDirty)
            {
                _cultureTex.SetPixels32(_culturePixels);
                _cultureTex.Apply(false, false);
            }
            if (bloodDirty)
            {
                _bloodTex.SetPixels32(_bloodPixels);
                _bloodTex.Apply(false, false);
            }
            _snapFirstFrame = false;

            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("InfluenceMask",
                (Time.realtimeSinceStartupAsDouble - perfT0) * 1000.0);
        }

        private void Configure()
        {
            _cultureTex = MakeMask("TWB_CultureMask");
            _bloodTex = MakeMask("TWB_BloodMask");
            _culturePixels = new Color32[Res * Res];
            _bloodPixels = new Color32[Res * Res];
            _eased = new float[Res * Res * 5];
            _owner = new sbyte[Res * Res];
            _frontier = new bool[Res * Res];
            _curseRaw = new float[Res * Res];
            _curseTmp = new float[Res * Res];
            _snapFirstFrame = true;

            // A fresh Texture2D's contents are UNDEFINED until the first
            // upload, and the dirty-flag optimisation below never uploads a
            // map that starts (and stays) empty — so push the cleared state
            // once or the GPU reads garbage as full-map coverage.
            _cultureTex.SetPixels32(_culturePixels);
            _cultureTex.Apply(false, false);
            _bloodTex.SetPixels32(_bloodPixels);
            _bloodTex.Apply(false, false);

            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;
            Shader.SetGlobalTexture("_TWB_CultureMask", _cultureTex);
            Shader.SetGlobalTexture("_TWB_BloodMask", _bloodTex);
            Shader.SetGlobalVector("_TWB_MaskST", new Vector4(
                1f / size.x, 1f / size.y, -min.x / size.x, -min.y / size.y));
            Shader.SetGlobalFloat("_TWB_OverlaysEnabled", 1f);
            _instance = this;
            _configured = true;
            TWBLog.Log($"[InfluenceMask] ground overlay masks online ({Res}x{Res})");
        }

        private static Texture2D MakeMask(string name) => new(Res, Res, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        /// <summary>Move cur toward target capped at step; true if it moved
        /// enough to need a re-upload (quantized to the byte the texture
        /// actually stores).</summary>
        private static bool Ease(ref float cur, float target, float step)
        {
            if (cur == target) return false;
            float d = target - cur;
            float next = Mathf.Abs(d) <= step ? target : cur + Mathf.Sign(d) * step;
            bool moved = (byte)(next * 255f) != (byte)(cur * 255f);
            cur = next;
            return moved;
        }

        private static float Ramp(float s, float start, float full)
            => Mathf.Clamp01((s - start) / (full - start));

        /// <summary>
        /// Strongest value across <paramref name="channels"/> on this cell, plus
        /// WHICH channel produced it. MaxChannel throws the owner away, which is
        /// exactly why same-culture frontiers were invisible.
        /// </summary>
        private static float MaxChannelOwner(System.Collections.Generic.List<int> channels,
            int x, int y, out int owner)
        {
            float best = 0f;
            owner = -1;
            for (int i = 0; i < channels.Count; i++)
            {
                float v = PlayerInfluenceMap.CellValue(x, y, channels[i]);
                if (v > best) { best = v; owner = channels[i]; }
            }
            return best;
        }

        private static float MaxChannel(System.Collections.Generic.List<int> channels, int x, int y)
        {
            float best = 0f;
            for (int i = 0; i < channels.Count; i++)
            {
                float v = PlayerInfluenceMap.CellValue(x, y, channels[i]);
                if (v > best) best = v;
            }
            return best;
        }

        private void RefreshCultureChannels()
        {
            _alanthorChannels.Clear();
            _feraldisChannels.Clear();
            _runaiChannels.Clear();
            for (int f = 0; f < PlayerInfluenceMap.PlayerChannels; f++)
            {
                byte culture = FactionColors.GetFactionCulture((Faction)f);
                if (culture == Cultures.Alanthor) _alanthorChannels.Add(f);
                else if (culture == Cultures.Runai) _runaiChannels.Add(f);
                else if (culture == Cultures.Feraldis) _feraldisChannels.Add(f);
            }
        }
    }
}
