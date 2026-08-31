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
        /// <summary>The one-shot region-boundary bake has landed. False until
        /// RegionMap actually has a partition — see the retry in Update.</summary>
        private bool _regionEdgesBaked;

        // ── Territory-granular ground (2026-08-28) ───────────────────────
        //
        // The culture fills and the curse used to be drawn straight off the
        // influence FIELD, so both read as soft point-value blobs: a bubble
        // around whatever was depositing, with a feathered rim that belonged to
        // nobody. Ground is owned a TERRITORY at a time (docs/Design/Regions.md
        // §2) and it has to look that way — a player should be able to tell
        // what they hold by looking at the floor, and "mostly ours, fading out"
        // is not a thing you can hold.
        //
        // So the verdict is per territory and binary; the easing below still
        // glides it in, which is what keeps a flip from snapping.
        /// <summary>Region id per mask cell, baked once with the edges.</summary>
        private short[] _regionCellIndex;
        /// <summary>Culture holding each territory this pass, Cultures.None
        /// for unowned. Sized to the region count.</summary>
        private byte[] _territoryCulture;
        /// <summary>Owning faction per territory, -1 for none — the frontier
        /// seam needs to know WHICH player, not just which culture.</summary>
        private sbyte[] _territoryOwner;
        /// <summary>Whether each territory reads as cursed ground.</summary>
        private bool[] _territoryCursed;

        /// <summary>
        /// Share of a territory's cells that must be over the curse threshold
        /// before the whole territory reads as cursed. Deliberately well under
        /// half: the curse arriving should flip the ground while it is still
        /// arriving, not after it has already won.
        /// </summary>
        public const float CursedTerritoryShare = 0.35f;
        private bool _snapFirstFrame; // seed state appears instantly, no fade-in
        private static InfluenceMaskTexture _instance;

        // ── Event gating (Regions.md §3b, 2026-08-31) ─────────────────────
        // Ownership and blood are the mask's only inputs, and both carry a
        // DataVersion now. The 128² passes run only while an input moved or
        // an ease glide is still in flight; a quiet map costs two compares.
        private int _lastInfVersion = int.MinValue;
        private int _lastBloodVersion = int.MinValue;
        private bool _settled;

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

            // Region lines are baked once, but NOT necessarily at Configure:
            // we come alive the moment a terrain exists (that is all
            // PlayerInfluenceMap.Ready needs), and the partition is built later
            // in the loading coroutine, after the marker registry refresh. So
            // Configure's bake usually ran against an EMPTY RegionMap, returned
            // without writing a single texel, and — being a one-shot — never
            // ran again: the G channel stayed 0 for the whole match and the
            // terrain had no region boundaries at all. Retry until it lands.
            bool regionEdgesJustBaked = false;
            if (!_regionEdgesBaked && TheWaningBorder.World.Regions.RegionMap.Ready)
            {
                BakeRegionEdges();
                _regionEdgesBaked = true;
                regionEdgesJustBaked = true;
            }

            // EVENT-DRIVEN (Regions.md §3b): run the passes only when an
            // input version moved, an ease is still gliding, or the region
            // bake just landed. The 2026-08-16 instrumentation measured these
            // passes + a texture upload on MOST frames; now a static map
            // costs two integer compares.
            bool inputsMoved = _lastInfVersion != PlayerInfluenceMap.DataVersion
                            || _lastBloodVersion != BloodMap.DataVersion
                            || regionEdgesJustBaked || _snapFirstFrame;
            if (!inputsMoved && _settled) return;
            _lastInfVersion = PlayerInfluenceMap.DataVersion;
            _lastBloodVersion = BloodMap.DataVersion;

            double perfT0 = Time.realtimeSinceStartupAsDouble;

            RefreshCultureChannels();

            // First frame snaps to targets so established territory shows
            // instantly instead of fading in from a clean map.
            float step = _snapFirstFrame ? float.MaxValue : EasePerSecond * Time.deltaTime;
            bool cultureDirty = _snapFirstFrame;
            // A fresh region bake writes .g on every texel, so the blood
            // texture must be re-uploaded even when no blood moved.
            bool bloodDirty = _snapFirstFrame || regionEdgesJustBaked;

            bool byTerritory = ResolveTerritories();

            for (int y = 0; y < Res; y++)
            {
                int row = y * Res;
                for (int x = 0; x < Res; x++)
                {
                    int i = row + x;
                    int e = i * 5;

                    float a, f, r;
                    int owner;

                    if (byTerritory)
                    {
                        // WHOLE TERRITORIES. The cell's culture is its
                        // territory's culture, at full strength or not at all —
                        // no ramp, because there is no partial ownership to
                        // ramp through. Unclaimable ground (RegionAt == None:
                        // mountain, cliff, water) belongs to nobody and stays
                        // bare, which is what keeps a culture's ground stopping
                        // at the foot of a crag instead of climbing it.
                        int region = _regionCellIndex[i];
                        byte culture = region >= 0 && region < _territoryCulture.Length
                            ? _territoryCulture[region] : Cultures.None;
                        owner = region >= 0 && region < _territoryOwner.Length
                            ? _territoryOwner[region] : -1;

                        a = culture == Cultures.Alanthor ? 1f : 0f;
                        f = culture == Cultures.Feraldis ? 1f : 0f;
                        r = culture == Cultures.Runai    ? 1f : 0f;

                        _curseRaw[i] = region >= 0 && region < _territoryCursed.Length
                                       && _territoryCursed[region] ? 1f : 0f;
                    }
                    else
                    {
                        // No partition (a scenario fixture, or a map with no
                        // seeds): fall back to the influence field, which is
                        // the only statement about ownership available there.
                        float aRaw = MaxChannelOwner(_alanthorChannels, x, y, out int aOwner);
                        float fRaw = MaxChannelOwner(_feraldisChannels, x, y, out int fOwner);
                        float rRaw = MaxChannelOwner(_runaiChannels,   x, y, out int rOwner);

                        owner = -1;
                        float ownerVal = InfluenceStart;
                        if (aRaw > ownerVal) { ownerVal = aRaw; owner = aOwner; }
                        if (fRaw > ownerVal) { ownerVal = fRaw; owner = fOwner; }
                        if (rRaw > ownerVal) { ownerVal = rRaw; owner = rOwner; }

                        a = Ramp(aRaw, InfluenceStart, InfluenceFull);
                        f = Ramp(fRaw, InfluenceStart, InfluenceFull);
                        r = Ramp(rRaw, InfluenceStart, InfluenceFull);
                        _curseRaw[i] = Ramp(
                            PlayerInfluenceMap.CellValue(x, y, PlayerInfluenceMap.CurseChannel),
                            InfluenceStart, InfluenceFull);
                    }

                    _owner[i] = (sbyte)owner;
                    float b = Ramp(BloodMap.CellValue(x, y), BloodStart, BloodFull);

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
                // Deliberately NOT setting cultureDirty: the seam is a pure
                // function of the eased data already uploaded, so a pass in
                // which nothing eased writes byte-identical pixels — marking
                // dirty here forced an upload on every pass with any live
                // frontier, and kept the version-gated mask from settling.
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

            // Nothing eased this pass — the mask has caught up with its
            // inputs and can sleep until a version moves again.
            _settled = !cultureDirty && !bloodDirty;

            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("InfluenceMask",
                (Time.realtimeSinceStartupAsDouble - perfT0) * 1000.0);
        }

        /// <summary>
        /// Work out, once per pass, what each TERRITORY looks like: whose
        /// culture paints it and whether it reads as cursed. False when there is
        /// no partition to work from, which puts the cell loop back on the
        /// (now static) ownership grid.
        ///
        /// The curse verdict comes straight from TerritoryOwnership now:
        /// Regions.md §3 is IMPLEMENTED (2026-08-31, CurseTerritorySystem) —
        /// the curse holds whole territories exactly like a player, so the
        /// ground reads cursed precisely where the ownership says Curse.
        /// </summary>
        private bool ResolveTerritories()
        {
            if (!_regionEdgesBaked || _regionCellIndex == null) return false;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return false;

            int regions = TheWaningBorder.World.Regions.RegionMap.Count;
            if (regions <= 0) return false;

            if (_territoryCulture == null || _territoryCulture.Length != regions)
            {
                _territoryCulture = new byte[regions];
                _territoryOwner = new sbyte[regions];
                _territoryCursed = new bool[regions];
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            var em = world.EntityManager;

            if (!TheWaningBorder.World.Regions.TerritoryOwnership.Ready)
                TheWaningBorder.World.Regions.TerritoryOwnership.Recompute(em);

            for (int t = 0; t < regions; t++)
            {
                int owner = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t);
                _territoryOwner[t] = (sbyte)(owner >= 0 && owner <= 7 ? owner : -1);
                // COMPLETED culture only: the ground must not change the instant
                // the age-up research is queued.
                _territoryCulture[t] = _territoryOwner[t] >= 0
                    ? CultureConfig.GetCompletedCulture(
                          em, (Faction)_territoryOwner[t])
                    : Cultures.None;
                // Ownership IS the curse verdict now — no coverage counting.
                _territoryCursed[t] =
                    owner == TheWaningBorder.World.Regions.TerritoryOwnership.Curse;
            }

            return true;
        }

        /// <summary>
        /// Region boundaries, baked ONCE into the blood mask's spare G channel.
        ///
        /// Two reasons this rides along instead of getting its own texture: the
        /// partition is static for the whole match (seeds are authored and never
        /// move), and _TWB_BloodMask only ever uses R — G/B/A were free. So the
        /// terrain gets region lines for zero extra samplers, zero extra uploads
        /// and zero per-frame cost. The per-frame loop writes only .r, so this
        /// survives every subsequent upload.
        ///
        /// The culture mask had no spare channel, which is why the player
        /// frontier seam had to be carved out of the fill instead.
        /// </summary>
        private void BakeRegionEdges()
        {
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return;

            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;

            // ~2 mask texels wide, expressed in metres so the line stays the
            // same THICKNESS ON THE GROUND on any map size rather than getting
            // fatter as the map grows. A boundary thinner than the mask's own
            // texel spacing can only ever be sampled by whichever texel centre
            // happens to land near it, so it beads instead of reading as a line.
            float width = Mathf.Max(2f, size.x / Res * 2f);

            if (_regionCellIndex == null || _regionCellIndex.Length != Res * Res)
                _regionCellIndex = new short[Res * Res];

            int lit = 0;
            for (int y = 0; y < Res; y++)
            {
                float wz = min.y + (y + 0.5f) / Res * size.y;
                int row = y * Res;
                for (int x = 0; x < Res; x++)
                {
                    float wx = min.x + (x + 0.5f) / Res * size.x;

                    // Which territory this cell belongs to, banked in the same
                    // pass — the partition never moves, so asking once here
                    // spares the per-frame culture fill a nearest-seed search.
                    if (_regionCellIndex != null)
                        _regionCellIndex[row + x] =
                            (short)TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);

                    float e = Mathf.Clamp01(
                        TheWaningBorder.World.Regions.RegionMap.EdgeStrengthAt(wx, wz, width));

                    // Shape HERE, in linear maths, and store the finished
                    // coverage — the shader then uses it as-is.
                    //
                    // The squaring (keeps the core dark and the falloff thin)
                    // used to live in the shader, which crushed the line twice
                    // over: the mask is an sRGB Texture2D and the project
                    // renders in LINEAR colour space, so a stored 0.8 arrives
                    // at the shader as 0.60 and squares to 0.36 — an 8 %
                    // darkening where a 45 % one was intended. Only a texel
                    // centre landing exactly on the boundary survived that, so
                    // the "quiet darkening" of Regions.md §7 was invisible on
                    // the ground even once the bake ran.
                    //
                    // LinearToGammaSpace pre-applies the inverse of the
                    // sampler's sRGB decode, so what the shader reads is the
                    // number written here. Only .g is touched: .r (blood) keeps
                    // its own long-standing tuning, crush and all.
                    float shaped = e * e;
                    _bloodPixels[row + x].g =
                        (byte)(Mathf.Clamp01(Mathf.LinearToGammaSpace(shaped)) * 255f + 0.5f);
                    if (shaped > 0.05f) lit++;
                }
            }

            // Loud on purpose, once per match: "the terrain has no region
            // lines" has now had two separate causes (an empty partition at
            // bake time, then a gamma double-crush), and neither left a trace
            // anyone could read back afterwards. TWBLog compiles out.
            Debug.Log($"[InfluenceMask] region boundaries baked — " +
                      $"{TheWaningBorder.World.Regions.RegionMap.Count} region(s), " +
                      $"{lit}/{Res * Res} mask texels on a boundary, " +
                      $"line half-width {width:0.0} m.");
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
            BakeRegionEdges();
            _regionEdgesBaked = TheWaningBorder.World.Regions.RegionMap.Ready;

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
