// InfluenceTerrainPainter.cs
// Paints the terrain itself from the influence + blood maps. The designer
// adds terrain layers by NAME and the painter drives their splat weights:
//
//   "AlanthorInfluence"  ← strongest Alanthor-culture player channel
//   "RunaiiInfluence"    ← strongest Runai-culture player channel
//   "FeraldisInfluence"  ← strongest Feraldis-culture player channel
//   "CurseInfluence"     ← the curse channel
//   "Blood"              ← the blood map (unit deaths)
//
// Influence layers ramp in from the 0.5 border to full at 0.8; blood ramps
// 0.15 → 0.7. Missing layers are simply skipped — add whichever textures
// you want visualized. Fog of war applies: unexplored ground keeps its
// authored splat (no intel leak).
//
// Painting is FRONT-TRACKED: rows whose blend weights are in motion (the
// active front) are repainted EVERY frame, advancing by SmoothPerSecond ×
// frame-dt — a continuous glide with no sweep cadence to read as ticks,
// bands, or stripes. Activity propagates to neighbouring rows as a front
// moves, and a few scrambled discovery rows per frame catch fronts that
// appear away from any active row (new sources, blood, fog reveals). Rows
// at rest cost nothing — no weight math, no upload. The authored alphamaps
// are cached on init and restored on destroy — TerrainData is an ASSET, so
// play-mode edits would otherwise persist in the editor.

using System.Collections.Generic;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.FogOfWar;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Influence
{
    [DefaultExecutionOrder(2000)]
    public sealed class InfluenceTerrainPainter : MonoBehaviour
    {
        private const float InfluenceWeightStart = 0.5f;  // border threshold
        private const float InfluenceWeightFull = 0.8f;
        private const float BloodWeightStart = 0.15f;
        private const float BloodWeightFull = 0.7f;
        private const float LayerRetryInterval = 5f;

        // Temporal smoothing: max painted-weight change per texel per SECOND.
        // Active rows repaint every frame, so a fading texel moves by
        // SmoothPerSecond × dt each frame — a full rock→marble fade takes
        // ~7 s of per-frame glide with no step large enough to see.
        private const float SmoothPerSecond = 0.15f;

        /// <summary>Active (in-motion) rows painted per frame. Fronts taller
        /// than this round-robin, halving the glide rate rather than
        /// stepping.</summary>
        private const int ActiveRowsBudget = 64;

        /// <summary>Background discovery rows per frame (scrambled order) —
        /// finds fronts that appear away from any active row.</summary>
        private const int ScanRowsPerFrame = 8;

        /// <summary>Clamp on a row's catch-up step after going unvisited —
        /// keeps a freshly discovered front easing in instead of popping.</summary>
        private const float MaxCatchUpSeconds = 0.5f;

        private enum SourceKind : byte { AlanthorCulture, RunaiCulture, FeraldisCulture, Curse, Blood, VeilstonePatch }

        private struct PaintLayer
        {
            public int Index;        // terrain layer index
            public SourceKind Kind;
        }

        // ─── Auto-mount on gameplay scenes ────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        // RETIRED AGAIN (2026-08-03, same day): the splat-painting fallback
        // flickered (row-amortized SetAlphamaps fighting the MapMagic
        // terrain). The ORIGINAL per-pixel path is restored instead —
        // TerrainOverlayMaterialBinder now assigns the TWBTerrain material
        // (TWB/Terrain/Lit + InfluenceMaskTexture masks) to the runtime
        // MapMagic terrain, which is what this painter was standing in for.
        // Class kept for reference; flip PainterEnabled only if a map ever
        // ships whose terrain cannot take the TWB material.
        private const bool PainterEnabled = false;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Non-constant-first ordering keeps the const gate free of
            // unreachable-code noise.
            if (!MapRegistry.IsGameplayScene(scene.name) || !PainterEnabled) return;
            if (Object.FindFirstObjectByType<InfluenceTerrainPainter>() != null) return;
            new GameObject("[Influence Terrain Painter]")
                .AddComponent<InfluenceTerrainPainter>();
        }

        // Layer assets appended to any terrain that lacks them (the MapMagic
        // terrain's layers come from its graph and never include ours).
        // Copied into Resources by GameDataMaintenanceTool.WireInfluenceLayers.
        private static readonly string[] RequiredLayerResources =
        {
            "TerrainLayers/AlanthorInfluence",
            "TerrainLayers/Blood",
            "TerrainLayers/CurseInfluence",
            "TerrainLayers/VeilstonePatch",
        };

        private static void EnsureRequiredLayers(TerrainData data)
        {
            var existing = data.terrainLayers;
            var list = new List<TerrainLayer>(existing);
            bool changed = false;
            foreach (var res in RequiredLayerResources)
            {
                var layer = Resources.Load<TerrainLayer>(res);
                if (layer == null) continue;
                bool present = false;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].name == layer.name) { present = true; break; }
                if (!present) { list.Add(layer); changed = true; }
            }
            if (changed) data.terrainLayers = list.ToArray();
        }

        // ─── State ────────────────────────────────────────────────────────
        private Terrain _terrain;
        private TerrainData _data;
        private int _alphaRes;
        private int _layerCount;
        private float[,,] _baseWeights;
        private float[,,] _slice;      // single reused row buffer
        private float[,,] _applied;    // last written paint weight per texel per paint layer
        private bool[] _rowActive;     // row has weights in motion → repaint every frame
        private float[] _rowLastPaint; // Time.time of the row's last repaint
        private int[] _scanOrder;      // scrambled row order for discovery
        private int _scanCursor;
        private int _activeCursor;     // round-robin resume point over active rows
        private float _retryTimer;
        private bool _initialized;

        private readonly List<PaintLayer> _paintLayers = new();
        // Layers that must never be painted over (impassable ground) —
        // paint weight scales by (1 − authored NoWalk weight) per texel.
        private readonly List<int> _maskLayers = new();
        private readonly List<int> _alanthorChannels = new();
        private readonly List<int> _runaiChannels = new();
        private readonly List<int> _feraldisChannels = new();

        private void Update()
        {
            if (!_initialized)
            {
                _retryTimer -= Time.deltaTime;
                if (_retryTimer > 0f) return;
                _retryTimer = LayerRetryInterval;
                TryInit();
                return;
            }

            // Another system can add a terrain layer at runtime AFTER we cached
            // the count (the Veil adds a "VeilCrust" splat layer once its field
            // initialises). Our slice would then be the wrong depth and
            // SetAlphamaps throws "layers should be N". Detect the change and
            // re-initialise against the new layer set on the next frame.
            if (_data == null || _data.alphamapLayers != _layerCount)
            {
                _initialized = false;
                _retryTimer = 0f; // re-init immediately, no 5 s retry wait
                return;
            }

            if (!PlayerInfluenceMap.Ready) return;
            PaintActiveRows();
        }

        private void OnDestroy()
        {
            // Only restore if the layer layout still matches what we captured.
            // If another painter (e.g. the Veil) changed the terrain's layer
            // count since, it owns the authoritative restore — a mismatched
            // SetAlphamaps would throw the same "wrong layer count" exception.
            if (_initialized && _data != null && _baseWeights != null
                && _data.alphamapLayers == _baseWeights.GetLength(2))
                _data.SetAlphamaps(0, 0, _baseWeights);
        }

        // ─── Init ─────────────────────────────────────────────────────────

        private void TryInit()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return;

            var data = terrain.terrainData;
            EnsureRequiredLayers(data);
            var layers = data.terrainLayers;
            _paintLayers.Clear();
            _maskLayers.Clear();

            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (l == null) continue;
                string n = l.name;
                string tex = l.diffuseTexture != null ? l.diffuseTexture.name : null;

                // Case-insensitive: designers name layers "blood",
                // "Blood", "BLOOD" — all should register.
                static bool Is(string a, string b, string target) =>
                    string.Equals(a, target, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b, target, System.StringComparison.OrdinalIgnoreCase);

                SourceKind? kind = null;
                if (Is(n, tex, "AlanthorInfluence")) kind = SourceKind.AlanthorCulture;
                else if (Is(n, tex, "RunaiiInfluence")) kind = SourceKind.RunaiCulture;
                else if (Is(n, tex, "FeraldisInfluence")) kind = SourceKind.FeraldisCulture;
                else if (Is(n, tex, "CurseInfluence")) kind = SourceKind.Curse;
                else if (Is(n, tex, "Blood")) kind = SourceKind.Blood;
                else if (Is(n, tex, "VeilstonePatch")) kind = SourceKind.VeilstonePatch;

                if (kind.HasValue)
                {
                    _paintLayers.Add(new PaintLayer { Index = i, Kind = kind.Value });
                }
                else if ((n != null && n.IndexOf("nowalk", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                         (tex != null && tex.IndexOf("nowalk", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    // Impassable-ground texture — influence/blood never
                    // replace it.
                    _maskLayers.Add(i);
                }
            }

            if (_paintLayers.Count == 0) return; // nothing to drive (yet) — retry later

            _terrain = terrain;
            _data = data;
            _alphaRes = data.alphamapResolution;
            _layerCount = data.alphamapLayers;
            _baseWeights = data.GetAlphamaps(0, 0, _alphaRes, _alphaRes);
            _slice = new float[1, _alphaRes, _layerCount];
            _applied = new float[_alphaRes, _alphaRes, _paintLayers.Count];
            _rowActive = new bool[_alphaRes];
            _rowLastPaint = new float[_alphaRes];
            for (int i = 0; i < _alphaRes; i++) _rowLastPaint[i] = Time.time;

            // Deterministic shuffle (xorshift Fisher-Yates) — discovery rows
            // land all over the map instead of top-to-bottom.
            _scanOrder = new int[_alphaRes];
            for (int i = 0; i < _alphaRes; i++) _scanOrder[i] = i;
            uint rng = 0x9E3779B9u;
            for (int i = _alphaRes - 1; i > 0; i--)
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                int j = (int)(rng % (uint)(i + 1));
                (_scanOrder[i], _scanOrder[j]) = (_scanOrder[j], _scanOrder[i]);
            }
            _scanCursor = 0;
            _activeCursor = 0;
            _initialized = true;
        }

        // ─── Painting ─────────────────────────────────────────────────────

        /// <summary>Repaint every active (in-motion) row this frame, plus a
        /// few scrambled discovery rows to notice fronts starting elsewhere.
        /// No sweep cadence exists — nothing to read as ticks or stripes.</summary>
        private void PaintActiveRows()
        {
            RefreshCultureChannels();

            Vector3 tPos = _terrain.GetPosition();
            Vector3 tSize = _data.size;
            int paintCount = _paintLayers.Count;

            // Per-layer scratch for the current texel (reused).
            var targets = new float[paintCount];
            var weights = new float[paintCount];

            // Active rows first — repainted EVERY frame so the front glides.
            // Round-robin from _activeCursor so fronts taller than the budget
            // still share it fairly.
            int painted = 0;
            int start = _activeCursor;
            for (int i = 0; i < _alphaRes && painted < ActiveRowsBudget; i++)
            {
                int row = (start + i) % _alphaRes;
                if (!_rowActive[row]) continue;
                PaintRow(row, tPos, tSize, paintCount, targets, weights);
                painted++;
                _activeCursor = (row + 1) % _alphaRes;
            }

            // Discovery: a few scrambled rows per frame catch fronts that
            // appear away from any active row (new sources, blood splats,
            // fog reveals). Motion found here self-propagates via the
            // neighbour-marking in PaintRow.
            for (int s = 0; s < ScanRowsPerFrame; s++)
            {
                int row = _scanOrder[_scanCursor];
                _scanCursor = (_scanCursor + 1) % _alphaRes;
                if (_rowActive[row]) continue; // already painted above
                PaintRow(row, tPos, tSize, paintCount, targets, weights);
            }
        }

        /// <summary>Recompute one alphamap row: rate-limit each texel's
        /// written weight toward its target, upload if anything moved, and
        /// keep the row (plus its neighbours, so a moving front propagates)
        /// in the active set until every texel has converged.</summary>
        private void PaintRow(int row, Vector3 tPos, Vector3 tSize,
            int paintCount, float[] targets, float[] weights)
        {
            float now = Time.time;
            float maxStep = SmoothPerSecond
                * Mathf.Clamp(now - _rowLastPaint[row], 0f, MaxCatchUpSeconds);
            _rowLastPaint[row] = now;

            float wz = tPos.z + (row + 0.5f) / _alphaRes * tSize.z;
            bool rowChanged = false;
            bool converged = true;

            for (int c = 0; c < _alphaRes; c++)
            {
                float wx = tPos.x + (c + 0.5f) / _alphaRes * tSize.x;

                // NOT fog-gated (2026-08-03): territory, curse and blood
                // ground are public information — painted globally, matching
                // the shader-mask path and the minimap rule. (Fog still hides
                // the ground itself via the fog overlay.)

                // NoWalk mask: impassable ground keeps its authored
                // texture at FULL weight — paint only competes for the
                // walkable remainder (1 − NoWalk), so influence/blood
                // blend around impassable ground without ever thinning
                // its splat.
                float noWalk = 0f;
                for (int m = 0; m < _maskLayers.Count; m++)
                    noWalk += _baseWeights[row, c, _maskLayers[m]];
                if (noWalk > 1f) noWalk = 1f;
                float available = 1f - noWalk;

                float total = 0f;
                for (int p = 0; p < paintCount; p++)
                {
                    float t = available > 0f
                        ? LayerWeight(_paintLayers[p].Kind, wx, wz) * available
                        : 0f;
                    targets[p] = t;
                    total += t;
                }

                // Normalize if the drivers overlap past the walkable
                // remainder.
                if (total > available)
                {
                    float inv = available / total;
                    for (int p = 0; p < paintCount; p++) targets[p] *= inv;
                }

                // Rate-limit the written weight toward the target.
                total = 0f;
                for (int p = 0; p < paintCount; p++)
                {
                    float cur = _applied[row, c, p];
                    float w = Mathf.Clamp(targets[p], cur - maxStep, cur + maxStep);
                    if (w != cur) rowChanged = true;
                    if (Mathf.Abs(w - targets[p]) > 0.001f) converged = false;
                    weights[p] = w;
                    total += w;
                }
                if (total > available && total > 0f)
                {
                    float inv = available / total;
                    for (int p = 0; p < paintCount; p++) weights[p] *= inv;
                    total = available;
                }
                for (int p = 0; p < paintCount; p++)
                    _applied[row, c, p] = weights[p];

                // Non-mask authored layers shrink to make room for the
                // paint; mask layers pass through untouched.
                float keep = available > 0f ? (available - total) / available : 0f;
                for (int l = 0; l < _layerCount; l++)
                    _slice[0, c, l] = _baseWeights[row, c, l] * keep;
                for (int m = 0; m < _maskLayers.Count; m++)
                {
                    int ml = _maskLayers[m];
                    _slice[0, c, ml] = _baseWeights[row, c, ml];
                }
                for (int p = 0; p < paintCount; p++)
                    _slice[0, c, _paintLayers[p].Index] += weights[p];
            }

            _rowActive[row] = !converged;
            if (!converged)
            {
                // The front likely spans into the neighbouring rows — pull
                // them into the active set so motion propagates row-to-row
                // without waiting for the discovery scan.
                if (row > 0) _rowActive[row - 1] = true;
                if (row < _alphaRes - 1) _rowActive[row + 1] = true;
            }

            // Rows at rest upload nothing.
            if (rowChanged)
                _data.SetAlphamaps(0, row, _slice);
        }

        private float LayerWeight(SourceKind kind, float wx, float wz)
        {
            switch (kind)
            {
                case SourceKind.Blood:
                    return Ramp(BloodMap.SampleWorld(wx, wz), BloodWeightStart, BloodWeightFull);

                case SourceKind.Curse:
                    return Ramp(PlayerInfluenceMap.ChannelStrengthWorld(
                        PlayerInfluenceMap.CurseChannel, wx, wz),
                        InfluenceWeightStart, InfluenceWeightFull);

                case SourceKind.AlanthorCulture:
                    return Ramp(MaxChannelStrength(_alanthorChannels, wx, wz),
                        InfluenceWeightStart, InfluenceWeightFull);

                case SourceKind.RunaiCulture:
                    return Ramp(MaxChannelStrength(_runaiChannels, wx, wz),
                        InfluenceWeightStart, InfluenceWeightFull);

                case SourceKind.FeraldisCulture:
                    return Ramp(MaxChannelStrength(_feraldisChannels, wx, wz),
                        InfluenceWeightStart, InfluenceWeightFull);

                // Ore-bearing ground under a veilstone patch. Already a 0..1
                // coverage with its own soft edge, so no influence ramp — and
                // its own layer, never the curse's: a resource patch is not
                // cursed ground.
                case SourceKind.VeilstonePatch:
                    return TheWaningBorder.Entities.VeilstonePatchGround.Any
                        ? TheWaningBorder.Entities.VeilstonePatchGround.CoverageAt(wx, wz)
                        : 0f;

                default:
                    return 0f;
            }
        }

        private static float Ramp(float s, float start, float full)
            => Mathf.Clamp01((s - start) / (full - start));

        private static float MaxChannelStrength(List<int> channels, float wx, float wz)
        {
            float best = 0f;
            for (int i = 0; i < channels.Count; i++)
            {
                float v = PlayerInfluenceMap.ChannelStrengthWorld(channels[i], wx, wz);
                if (v > best) best = v;
            }
            return best;
        }

        private void RefreshCultureChannels()
        {
            _alanthorChannels.Clear();
            _runaiChannels.Clear();
            _feraldisChannels.Clear();
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
