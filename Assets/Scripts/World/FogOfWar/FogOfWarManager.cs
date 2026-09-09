using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace TheWaningBorder.World.FogOfWar
{
    /// <summary>
    /// Core fog of war manager handling per-faction visibility grids.
    /// Maintains visible (current frame) and revealed (persistent) state for each cell.
    /// Updates a two-channel (visible, revealed) coverage texture for the
    /// human player's FoW overlay; the shader derives the alpha from both.
    /// </summary>
    public class FogOfWarManager : MonoBehaviour
    {
        public static FogOfWarManager Instance { get; private set; }

        public Vector2 WorldMin = new Vector2(-12.5f, -12.5f);
        public Vector2 WorldMax = new Vector2(12.5f, 12.5f);
        public float CellSize = 0.1f;

        /// <summary>
        /// Whose fog this is. RESOLVED IN Awake, NEVER IN A FIELD INITIALIZER
        /// (2026-09-08).
        ///
        /// This read `= GameSettings.LocalPlayerFaction` here, which is a
        /// MonoBehaviour INSTANCE field initializer — it runs inside the
        /// object's constructor, on Unity's deserialization thread, while the
        /// scene is loading. Touching GameSettings there ran that class's
        /// static constructor there too, and its own initializer called
        /// MapRegistry.Default -> SceneManager.sceneCountInBuildSettings,
        /// which Unity forbids from a constructor:
        ///
        ///   UnityException: GetNumScenesInBuildSettings is not allowed to be
        ///   called from a MonoBehaviour constructor ... Called from
        ///   MonoBehaviour 'FogOfWarManager' on game object 'FogOfWar'.
        ///   Rethrow as TypeInitializationException: GameSettings
        ///
        /// A TypeInitializationException is CACHED FOR THE LIFE OF THE DOMAIN.
        /// GameSettings was therefore dead for the whole session after this
        /// one throw, and every later reader of it threw too — the selection
        /// system and control groups went down with it. One field initializer
        /// took the entire game out.
        ///
        /// The value was never needed this early: the field is serialized (so
        /// a scene-authored manager carries its own), Ensure() assigns it, and
        /// the render path reads GameSettings.ViewFaction live.
        /// </summary>
        public Faction HumanFaction;
        public Material FogMaterial;
        public MeshRenderer FogRenderer;
        [Range(0, 1)] public float ExploredAlpha = 0.65f; // explored-but-not-currently-visible
        /// <summary>
        /// Never-seen ground is FULLY opaque. At the old 0.98 a two-percent
        /// window let the terrain, and in particular the lit edge of the map,
        /// show faintly through unexplored fog — so the shape and extent of the
        /// map were readable before anyone had scouted it. The shader's _Tint
        /// is already black, so 1.0 is pure black.
        /// </summary>
        [Range(0, 1)] public float HiddenAlpha = 1f;      // never seen
        public float TextureUpdateInterval = 0.1f;

        // Internal. NativeArrays, not managed arrays (2026-09-03): the
        // stamp/push work runs in Burst jobs — as managed loops the 4 Hz
        // fog pass cost ~29 ms per tick after the AA + crossfade upgrades
        // (304 FogStamp spikes in a 90 s match), a visible quarter-second
        // blip cadence. Indexer syntax is unchanged for the managed paths.
        float _nextTextureTime;
        int _w, _h;
        NativeArray<byte> _visible;   // [faction][cell] coverage, current frame
        NativeArray<byte> _revealed;  // [faction][cell] coverage, persistent
        Texture2D _tex;    // human overlay

        const int MaxFactions = 8;

        int Idx(int x, int y) => y * _w + x;

        // Map any enum to a safe slice [0..MaxFactions-1]
        int FOfs(Faction f)
        {
            int fi = (int)f;
            if (fi < 0) fi = -fi;
            fi %= MaxFactions;
            return fi * _w * _h;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Awake is the earliest point GameSettings may legally be touched
            // from a MonoBehaviour — see the HumanFaction docs above.
            HumanFaction = GameSettings.LocalPlayerFaction;

            _w = Mathf.CeilToInt((WorldMax.x - WorldMin.x) / CellSize);
            _h = Mathf.CeilToInt((WorldMax.y - WorldMin.y) / CellSize);
            _visible = new NativeArray<byte>(MaxFactions * _w * _h, Allocator.Persistent);
            _revealed = new NativeArray<byte>(MaxFactions * _w * _h, Allocator.Persistent);

            // FOUR channels (2026-09-03): RG = current (visible, revealed)
            // coverage, BA = the PREVIOUS push's coverage. Two channels per
            // state because a single alpha forced the shader to infer
            // "explored" from mid-ramp values, which synthesized an explored
            // SLIVER around every visible circle that borders unexplored
            // ground. Previous-state channels because the stamp pass runs at
            // 4 Hz (per-frame stamping measured 5-14 ms) and the overlay
            // stepped visibly as units moved — the shader now crossfades
            // previous -> current across the push interval, and since
            // coverage is a distance-like field the sharpened contour SLIDES
            // between positions instead of jumping. Data stays 4 Hz; the
            // motion reads continuous.
            _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false, true)
            {
                // SHARP explored/unexplored boundary (2026-08-31 directive).
                // This was TRILINEAR — maximum smear on a fog mask: the
                // frontier of knowledge blurred across metres of ground and
                // read as haze, which is exactly "no clear boundary between
                // explored and revealed". Point keeps every fog cell a crisp
                // step — the classic RTS look — and the map-scaled grid
                // keeps the steps small on any map size.
                filterMode = FilterMode.Bilinear, // smooth sub-texel frontier; the
                                                  // shader re-sharpens the bands
                wrapMode = TextureWrapMode.Clamp
            };

            EnsureMaterialBound();
            ClearAll();
            PushHumanTexture();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_visible.IsCreated) _visible.Dispose();
            if (_revealed.IsCreated) _revealed.Dispose();
            if (_teamVisible.IsCreated) _teamVisible.Dispose();
            if (_teamRevealed.IsCreated) _teamRevealed.Dispose();
            if (_pushedVis.IsCreated) _pushedVis.Dispose();
            if (_pushedRev.IsCreated) _pushedRev.Dispose();
            if (_prevVis.IsCreated) _prevVis.Dispose();
            if (_prevRev.IsCreated) _prevRev.Dispose();
        }

        /// <summary>Ensures FogMaterial, FogRenderer and shader params are bound to _tex.</summary>
        void EnsureMaterialBound()
        {
            if (FogMaterial == null && FogRenderer != null)
                FogMaterial = FogRenderer.sharedMaterial;

            if (FogMaterial == null) return;

            if (FogMaterial.mainTexture != _tex)
                FogMaterial.mainTexture = _tex;

            FogMaterial.SetVector("_WorldMin", new Vector4(WorldMin.x, 0, WorldMin.y, 0));
            FogMaterial.SetVector("_WorldMax", new Vector4(WorldMax.x, 0, WorldMax.y, 0));
            // The shader sharpens the bilinear gradient back into crisp
            // bands, so it needs to know where the plateaus sit.
            FogMaterial.SetFloat("_ExploredA", ExploredAlpha);
            FogMaterial.SetFloat("_HiddenA", HiddenAlpha);

            if (FogRenderer != null && FogRenderer.sharedMaterial != FogMaterial)
                FogRenderer.sharedMaterial = FogMaterial;
        }

        public void ClearAll()
        {
            ClearVisible();
            // NOTE: revealed persists across frames; do NOT clear here
        }

        /// <summary>Call once per frame before stamping to zero current visibility only.</summary>
        public void BeginFrame()
        {
            ClearVisible();
        }

        void ClearVisible()
        {
            if (!_visible.IsCreated) return;
            new ClearJob { Data = _visible }.Run();
        }

        [BurstCompile]
        struct ClearJob : IJob
        {
            public NativeArray<byte> Data;
            public void Execute()
            {
                for (int i = 0; i < Data.Length; i++) Data[i] = 0;
            }
        }

        /// <summary>
        /// Mark cells EXPLORED (not currently visible) for a faction, wherever
        /// <paramref name="accept"/> returns true for the cell centre in world XZ.
        ///
        /// Distinct from <see cref="Stamp"/>, which sets visible AND revealed:
        /// this is "you know this ground", not "you can see what is on it". A
        /// player who is granted their home region still needs units standing in
        /// it to watch it — terrain is remembered, activity is not.
        ///
        /// One-shot by design (match start); it walks the whole grid.
        /// </summary>
        public void RevealWhere(Faction f, Func<Vector2, bool> accept)
        {
            if (accept == null || !_revealed.IsCreated) return;
            int fx = FOfs(f);

            for (int y = 0; y < _h; y++)
            {
                float wz = WorldMin.y + (y + 0.5f) * CellSize;
                for (int x = 0; x < _w; x++)
                {
                    float wx = WorldMin.x + (x + 0.5f) * CellSize;
                    if (accept(new Vector2(wx, wz)))
                        _revealed[fx + Idx(x, y)] = 255;
                }
            }
        }

        /// <summary>
        /// Stamp a circular LoS for a faction — ANTI-ALIASED (2026-09-03).
        ///
        /// Cells used to be binary 0/1, and the bilinear + re-sharpen shader
        /// then traced the 50% contour of a BINARY grid: the staircase of the
        /// cells themselves, one jag per texel ("fog of war is still jagged").
        /// The rim cell now stores fractional coverage (distance-based, one
        /// cell of falloff), so the 50% contour of the filtered field IS the
        /// circle and straight frontiers between stamps are straight lines.
        ///
        /// Gameplay parity: a cell reads as visible at coverage >= 128, which
        /// is d &lt;= radius — the exact cell set the old binary test stamped.
        /// </summary>
        public void Stamp(Faction f, Vector3 worldPos, float radius)
        {
            int fx = FOfs(f);

            float gx = (worldPos.x - WorldMin.x) / CellSize;
            float gy = (worldPos.z - WorldMin.y) / CellSize;
            float r = Mathf.Max(0.01f, radius / CellSize);
            // 1.5 cells of falloff (0.5 -> 0.75 each way, 2026-09-03): at a
            // single cell the sharpened contour still scalloped between texel
            // centres. Half coverage stays exactly at d == r, so the gameplay
            // threshold is unmoved.
            float rOut = r + 0.75f;                   // coverage fades to 0 here
            float rIn = Mathf.Max(0f, r - 0.75f);     // fully covered inside
            int minx = Mathf.Clamp(Mathf.FloorToInt(gx - rOut), 0, _w - 1);
            int maxx = Mathf.Clamp(Mathf.CeilToInt(gx + rOut), 0, _w - 1);
            int miny = Mathf.Clamp(Mathf.FloorToInt(gy - rOut), 0, _h - 1);
            int maxy = Mathf.Clamp(Mathf.CeilToInt(gy + rOut), 0, _h - 1);
            float r2Out = rOut * rOut;
            float r2In = rIn * rIn;

            for (int y = miny; y <= maxy; y++)
            {
                for (int x = minx; x <= maxx; x++)
                {
                    float dx = (x + 0.5f) - gx;
                    float dy = (y + 0.5f) - gy;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > r2Out) continue;

                    byte cov = 255;
                    if (d2 > r2In)
                    {
                        // Rim ring only — the sqrt never runs on the interior.
                        cov = (byte)(Mathf.Clamp01((rOut - Mathf.Sqrt(d2)) / 1.5f) * 255f);
                        if (cov == 0) continue;
                    }

                    int i = fx + Idx(x, y);
                    if (cov > _visible[i]) _visible[i] = cov;
                    if (cov > _revealed[i]) _revealed[i] = cov;
                }
            }
        }

        /// <summary>One LoS circle for the Burst stamp batch.</summary>
        public struct StampCommand
        {
            public Vector3 Position;
            public float Radius;
            public Faction Faction;
        }

        /// <summary>
        /// Stamp every circle of a frame in ONE Burst job — the per-unit
        /// managed Stamp loop was the bulk of the 4 Hz fog pass cost at
        /// scale (hundreds of sighted entities x thousands of rim cells).
        /// Identical maths to <see cref="Stamp"/>.
        /// </summary>
        public void StampBatch(NativeArray<StampCommand> commands, int count)
        {
            if (!_visible.IsCreated || count <= 0) return;

            var jobCmds = new NativeArray<StampJob.Cmd>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
            {
                var c = commands[i];
                jobCmds[i] = new StampJob.Cmd
                {
                    GX = (c.Position.x - WorldMin.x) / CellSize,
                    GY = (c.Position.z - WorldMin.y) / CellSize,
                    R = Mathf.Max(0.01f, c.Radius / CellSize),
                    Ofs = FOfs(c.Faction),
                };
            }

            new StampJob
            {
                Cmds = jobCmds,
                Visible = _visible,
                Revealed = _revealed,
                W = _w,
                H = _h,
            }.Run();
            jobCmds.Dispose();
        }

        [BurstCompile]
        struct StampJob : IJob
        {
            public struct Cmd { public float GX, GY, R; public int Ofs; }

            [ReadOnly] public NativeArray<Cmd> Cmds;
            public NativeArray<byte> Visible;
            public NativeArray<byte> Revealed;
            public int W, H;

            public void Execute()
            {
                for (int ci = 0; ci < Cmds.Length; ci++)
                {
                    var c = Cmds[ci];
                    float rOut = c.R + 0.75f;
                    float rIn = rOut - 1.5f; if (rIn < 0f) rIn = 0f;
                    int minx = (int)(c.GX - rOut); if (minx < 0) minx = 0;
                    int maxx = (int)(c.GX + rOut) + 1; if (maxx > W - 1) maxx = W - 1;
                    int miny = (int)(c.GY - rOut); if (miny < 0) miny = 0;
                    int maxy = (int)(c.GY + rOut) + 1; if (maxy > H - 1) maxy = H - 1;
                    float r2Out = rOut * rOut;
                    float r2In = rIn * rIn;

                    for (int y = miny; y <= maxy; y++)
                    {
                        int row = c.Ofs + y * W;
                        for (int x = minx; x <= maxx; x++)
                        {
                            float dx = (x + 0.5f) - c.GX;
                            float dy = (y + 0.5f) - c.GY;
                            float d2 = dx * dx + dy * dy;
                            if (d2 > r2Out) continue;

                            byte cov = 255;
                            if (d2 > r2In)
                            {
                                float v = (rOut - Unity.Mathematics.math.sqrt(d2)) / 1.5f;
                                if (v <= 0f) continue;
                                if (v > 1f) v = 1f;
                                cov = (byte)(v * 255f);
                            }

                            int i = row + x;
                            if (cov > Visible[i]) Visible[i] = cov;
                            if (cov > Revealed[i]) Revealed[i] = cov;
                        }
                    }
                }
            }
        }

        /// <summary>Update the human overlay texture after stamping (throttled).</summary>
        public void EndFrameAndBuild()
        {
            // Shared line of sight. Merged BEFORE anything reads the grid, and
            // merged INTO each member's own slice, so every existing consumer
            // — IsVisible, IsRevealed, the overlay texture, the minimap, the
            // AI's intel scans — becomes team-aware without touching any of
            // them. docs/Design/Teams.md
            //
            // Deliberately NOT inside the texture throttle below: gameplay
            // queries run every frame and must see the merged result, while
            // the texture repaint is paced.
            MergeTeamVision();

            // Observer perspective: follow the viewed faction (the selected
            // asset's owner); no view faction = full reveal, overlay off.
            // Normal play resolves to LocalPlayerFaction and changes nothing.
            var view = GameSettings.ViewFaction;
            if (FogRenderer != null && FogRenderer.enabled != view.HasValue)
                FogRenderer.enabled = view.HasValue;
            if (view.HasValue && HumanFaction != view.Value)
            {
                HumanFaction = view.Value;
                _nextTextureTime = 0f; // repaint NOW — stale fog is the old player's vision
            }

            if (Time.unscaledTime < _nextTextureTime) return;
            _nextTextureTime = Time.unscaledTime + Mathf.Max(0f, TextureUpdateInterval);
            EnsureMaterialBound();
            PushHumanTexture();
        }

        // Scratch buffer for the team merge, kept alive between frames so the
        // merge does not allocate per frame.
        NativeArray<byte> _teamVisible;
        NativeArray<byte> _teamRevealed;

        /// <summary>
        /// OR every team member's visibility into a shared result and write it
        /// back to each member. Costs nothing in a free-for-all: if no team has
        /// two or more members the whole pass is skipped, which is the default
        /// lobby state.
        /// </summary>
        void MergeTeamVision()
        {
            if (!_visible.IsCreated || !_revealed.IsCreated) return;

            int cells = _w * _h;
            if (cells <= 0) return;

            for (byte team = 1; team <= Alliances.MaxTeams; team++)
            {
                // Collect this team's slice offsets.
                int memberCount = 0;
                int firstOfs = 0;
                Span<int> offsets = stackalloc int[MaxFactions];
                for (int f = 0; f < MaxFactions; f++)
                {
                    if (Alliances.TeamOf((Faction)f) != team) continue;
                    if (memberCount == 0) firstOfs = f * cells;
                    offsets[memberCount++] = f * cells;
                }
                if (memberCount < 2) continue;   // solo team == no sharing to do

                if (!_teamVisible.IsCreated || _teamVisible.Length < cells)
                {
                    if (_teamVisible.IsCreated) _teamVisible.Dispose();
                    if (_teamRevealed.IsCreated) _teamRevealed.Dispose();
                    _teamVisible = new NativeArray<byte>(cells, Allocator.Persistent);
                    _teamRevealed = new NativeArray<byte>(cells, Allocator.Persistent);
                }

                // Seed from the first member, then merge the rest in.
                NativeArray<byte>.Copy(_visible, firstOfs, _teamVisible, 0, cells);
                NativeArray<byte>.Copy(_revealed, firstOfs, _teamRevealed, 0, cells);

                for (int m = 1; m < memberCount; m++)
                {
                    int ofs = offsets[m];
                    for (int i = 0; i < cells; i++)
                    {
                        // Max, not OR: cells carry fractional AA coverage now.
                        byte v = _visible[ofs + i];
                        if (v > _teamVisible[i]) _teamVisible[i] = v;
                        byte rv = _revealed[ofs + i];
                        if (rv > _teamRevealed[i]) _teamRevealed[i] = rv;
                    }
                }

                // Write the union back to every member.
                for (int m = 0; m < memberCount; m++)
                {
                    NativeArray<byte>.Copy(_teamVisible, 0, _visible, offsets[m], cells);
                    NativeArray<byte>.Copy(_teamRevealed, 0, _revealed, offsets[m], cells);
                }
            }
        }

        void PushHumanTexture()
        {
            int ofs = FOfs(HumanFaction);

            if (_tex.width != _w || _tex.height != _h)
            {
                _tex.Reinitialize(_w, _h);
                _tex.filterMode = FilterMode.Bilinear;
                _tex.wrapMode = TextureWrapMode.Clamp;
                EnsureMaterialBound();
            }

            var data = _tex.GetRawTextureData<byte>();
            int cells = _w * _h;
            int required = cells * 4;   // RGBA32: four bytes per pixel
            if (data.Length != required)
            {
                _tex.Reinitialize(_w, _h);
                data = _tex.GetRawTextureData<byte>();
                EnsureMaterialBound();
            }

            // The crossfade SOURCE rides in BA. It is NOT simply the last
            // push: if a push lands while the previous fade is still
            // running, resetting the source to the last full field snaps
            // the displayed edge backwards for a frame — the "jitter while
            // a unit moves". The source written here is the field the
            // shader is DISPLAYING at this instant (prev blended toward
            // current by the in-flight blend factor), which makes every
            // swap seamless regardless of push-timing jitter.
            bool freshPrev = !_pushedVis.IsCreated || _pushedVis.Length != cells;
            if (freshPrev)
            {
                if (_pushedVis.IsCreated) _pushedVis.Dispose();
                if (_pushedRev.IsCreated) _pushedRev.Dispose();
                if (_prevVis.IsCreated) _prevVis.Dispose();
                if (_prevRev.IsCreated) _prevRev.Dispose();
                _pushedVis = new NativeArray<byte>(cells, Allocator.Persistent);
                _pushedRev = new NativeArray<byte>(cells, Allocator.Persistent);
                _prevVis = new NativeArray<byte>(cells, Allocator.Persistent);
                _prevRev = new NativeArray<byte>(cells, Allocator.Persistent);
            }

            // In-flight blend at the moment of this push, in 0..256 fixed point.
            int tq = 256;
            if (!freshPrev && _blendDuration > 0f)
                tq = Mathf.Clamp(Mathf.RoundToInt(
                    (Time.unscaledTime - _lastPushTime) / _blendDuration * 256f), 0, 256);

            new PushJob
            {
                Visible = _visible,
                Revealed = _revealed,
                Ofs = ofs,
                Cells = cells,
                PrevVis = _prevVis,
                PrevRev = _prevRev,
                PushedVis = _pushedVis,
                PushedRev = _pushedRev,
                Data = data,
                Tq = tq,
                Fresh = freshPrev ? (byte)1 : (byte)0,
            }.Run();

            _tex.Apply(false, false);

            // The crossfade spans the ACTUAL gap between pushes, so cadence
            // jitter never causes a fade to finish early and pop.
            float now = Time.unscaledTime;
            _blendDuration = Mathf.Clamp(now - _lastPushTime, 0.05f, 0.6f);
            _lastPushTime = now;

            // Reset the shader blend IN THE SAME FRAME as the texture swap.
            // Update() may already have run this frame with the OLD push
            // time, leaving _Blend near 1 — the swap frame then rendered the
            // new field fully and the next frame snapped BACK to the fade
            // start ("the scout's vision goes back for a frame").
            if (FogMaterial != null) FogMaterial.SetFloat("_Blend", 0f);
        }

        // Snapshot of the last pushed coverage (human faction) plus the
        // crossfade-source channels as last written — needed to compute the
        // DISPLAYED field when a push lands mid-fade.
        NativeArray<byte> _pushedVis;
        NativeArray<byte> _pushedRev;
        NativeArray<byte> _prevVis;
        NativeArray<byte> _prevRev;

        [BurstCompile]
        struct PushJob : IJob
        {
            [ReadOnly] public NativeArray<byte> Visible;
            [ReadOnly] public NativeArray<byte> Revealed;
            public int Ofs;
            public int Cells;
            public NativeArray<byte> PrevVis;
            public NativeArray<byte> PrevRev;
            public NativeArray<byte> PushedVis;
            public NativeArray<byte> PushedRev;
            public NativeArray<byte> Data;   // RGBA32 raw texture data
            public int Tq;
            public byte Fresh;

            public void Execute()
            {
                for (int i = 0; i < Cells; i++)
                {
                    byte vis = Visible[Ofs + i];
                    byte rev = Revealed[Ofs + i];

                    byte dispV, dispR;
                    if (Fresh != 0)
                    {
                        dispV = vis;
                        dispR = rev;
                    }
                    else
                    {
                        byte pv = PrevVis[i];
                        dispV = (byte)(pv + (((PushedVis[i] - pv) * Tq) >> 8));
                        byte pr = PrevRev[i];
                        dispR = (byte)(pr + (((PushedRev[i] - pr) * Tq) >> 8));
                    }

                    int d = i * 4;
                    Data[d] = vis;
                    Data[d + 1] = rev;
                    Data[d + 2] = dispV;
                    Data[d + 3] = dispR;

                    PrevVis[i] = dispV;
                    PrevRev[i] = dispR;
                    PushedVis[i] = vis;
                    PushedRev[i] = rev;
                }
            }
        }
        float _lastPushTime;
        float _blendDuration = 0.25f;

        void Update()
        {
            // One material float per frame drives the crossfade — the whole
            // per-frame cost of continuous fog motion.
            if (FogMaterial == null) return;
            float t = _blendDuration > 0f
                ? Mathf.Clamp01((Time.unscaledTime - _lastPushTime) / _blendDuration)
                : 1f;
            FogMaterial.SetFloat("_Blend", t);
        }

        public bool IsVisible(Faction f, Vector3 worldPos)
        {
            if (!WorldToCell(worldPos, out int x, out int y)) return false;
            // >= 128 = the cell centre is inside the stamped radius — the
            // same cell set the old binary stamp answered true for. The AA
            // rim below half coverage is visual only.
            return _visible[FOfs(f) + Idx(x, y)] >= 128;
        }

        public bool IsRevealed(Faction f, Vector3 worldPos)
        {
            if (!WorldToCell(worldPos, out int x, out int y)) return false;
            // Same half-coverage threshold as IsVisible.
            return _revealed[FOfs(f) + Idx(x, y)] >= 128;
        }

        bool WorldToCell(Vector3 pos, out int x, out int y)
        {
            x = Mathf.FloorToInt((pos.x - WorldMin.x) / CellSize);
            y = Mathf.FloorToInt((pos.z - WorldMin.y) / CellSize);
            return (x >= 0 && x < _w && y >= 0 && y < _h);
        }

        public void ForceRebuildGrid(bool clearRevealed = false)
        {
            int newW = Mathf.CeilToInt((WorldMax.x - WorldMin.x) / CellSize);
            int newH = Mathf.CeilToInt((WorldMax.y - WorldMin.y) / CellSize);

            if (newW <= 0 || newH <= 0)
            {
                return;
            }

            int oldW = _w;
            int oldH = _h;
            NativeArray<byte> oldRevealed = _revealed;

            _w = newW;
            _h = newH;

            int slice = _w * _h;
            if (_visible.IsCreated) _visible.Dispose();
            _visible = new NativeArray<byte>(MaxFactions * slice, Allocator.Persistent);

            if (clearRevealed || !oldRevealed.IsCreated || oldW <= 0 || oldH <= 0)
            {
                if (oldRevealed.IsCreated) oldRevealed.Dispose();
                _revealed = new NativeArray<byte>(MaxFactions * slice, Allocator.Persistent);
            }
            else
            {
                // Fix #242: previously both branches of the ternary allocated a
                // fresh zero array, so clearRevealed=false still wiped
                // exploration progress. Now we copy the old revealed data into
                // the new grid, clipping to the overlap rectangle when the
                // dimensions change.
                _revealed = new NativeArray<byte>(MaxFactions * slice, Allocator.Persistent);
                int copyW = Mathf.Min(oldW, _w);
                int copyH = Mathf.Min(oldH, _h);
                for (int f = 0; f < MaxFactions; f++)
                {
                    int oldBase = f * oldW * oldH;
                    int newBase = f * _w * _h;
                    for (int y = 0; y < copyH; y++)
                    {
                        NativeArray<byte>.Copy(
                            oldRevealed, oldBase + y * oldW,
                            _revealed,   newBase + y * _w,
                            copyW);
                    }
                }
                oldRevealed.Dispose();
            }

            if (_tex == null)
                _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false, true);
            else
                _tex.Reinitialize(_w, _h);

            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;   // smooth frontier (see ctor site)

            EnsureMaterialBound();
            PushHumanTexture();
        }

        public void ApplyBounds(Vector2 newMin, Vector2 newMax, float? newCellSize = null, bool clearRevealed = false, int surfaceGrid = 128)
        {
            WorldMin = newMin;
            WorldMax = newMax;
            if (newCellSize.HasValue) CellSize = Mathf.Max(0.05f, newCellSize.Value);

            EnsureMaterialBound();
            ForceRebuildGrid(clearRevealed);

            // Repaint pacing scales with the grid: the overlay rebuild walks
            // every cell, so a big map repaints a little less often instead of
            // costing proportionally more. 0.2 s on a million-cell grid is
            // invisible at the fog edge and halves the largest area-scaled
            // cost the fine (half-build-cell) fog grid carries.
            TextureUpdateInterval = (_w * _h) > 600_000 ? 0.2f : 0.1f;

            // Keep the enabled flag across surface rebuilds — observers run
            // with the overlay renderer hidden.
            bool rendererEnabled = FogRenderer == null || FogRenderer.enabled;
            if (FogRenderer != null)
            {
                var old = FogRenderer.gameObject;
                if (old != null) Destroy(old);
            }

            // Same stripped-shader trap as SetupFogOfWar: with no material and
            // no shader there is nothing to draw, so skip building the surface
            // rather than throwing on new Material(null). The fog GRID still
            // updates, so vision queries stay correct — only the overlay is gone.
            var mat = FogMaterial;
            if (mat == null)
            {
                var fogShader = ResolveFogShader();
                if (fogShader == null)
                {
                    // Nothing to draw. The fog GRID still updates, so vision
                    // queries stay correct — only the overlay is skipped.
                    FogRenderer = null;
                    PushHumanTexture();
                    return;
                }
                mat = new Material(fogShader);
            }

            GameObject surface = FogOfWarConformingMesh.Create(WorldMin, WorldMax, surfaceGrid, mat);
            surface.name = "FogSurface";
            surface.transform.SetParent(transform, false);
            FogRenderer = surface.GetComponent<MeshRenderer>();
            FogRenderer.enabled = rendererEnabled;

            EnsureMaterialBound();
            PushHumanTexture();
        }

        /// <summary>How far the fog grid and surface extend BEYOND the map
        /// rect, in metres. Trees and rocks planted at the very edge lean past
        /// the terrain bounds; without the skirt they rendered fully lit
        /// against the void next to unexplored black. Skirt cells are never
        /// stamped (nothing can stand there), so they stay hidden forever.</summary>
        public const float EdgePad = 15f;

        /// <summary>Shader file under a Resources/ folder, loaded by name.</summary>
        private const string FogShaderResource = "FogOfWarShader";

        /// <summary>
        /// Resolves the fog shader for BOTH the editor and a player build.
        ///
        /// Nothing in the project references Unlit/FogOfWar from a material or
        /// a scene, so it used to reach the player only if someone remembered
        /// to list it in Graphics > Always Included Shaders. Nobody did, so
        /// Shader.Find returned null in the build, `new Material(null)` threw
        /// out of GameBootstrap.InitializeWorld, the bootstrap coroutine died
        /// silently and the loading screen hung on "Building world..." forever.
        ///
        /// It now lives in a Resources/ folder — those are included in every
        /// build unconditionally — and is loaded explicitly rather than via
        /// Shader.Find, which is only reliable for shaders already loaded or
        /// pulled in by some other reference. Shader.Find stays as a fallback
        /// so a move or rename degrades instead of breaking.
        /// </summary>
        private static Shader ResolveFogShader()
        {
            var shader = Resources.Load<Shader>(FogShaderResource);
            if (shader != null) return shader;
            return Shader.Find("Unlit/FogOfWar");
        }

        /// <summary>
        /// Static helper to create and setup FogOfWar in a scene.
        /// </summary>
        public static void SetupFogOfWar()
        {
            if (FindFirstObjectByType<FogOfWarManager>() != null) return;

            var root = new GameObject("FogOfWar");
            var mgr = root.AddComponent<FogOfWarManager>();
            // Note: Awake() ran on AddComponent above and allocated the grid/texture
            // using the default WorldMin/Max (±12.5, CellSize 0.1). Setting the
            // fields directly afterwards used to leave _w/_h/_visible/_revealed
            // sized for that 25x25 default — every unit outside that box stamped
            // onto the clamped edge instead of revealing fog. ApplyBounds()
            // reallocates everything to the real map dimensions.
            mgr.HumanFaction = GameSettings.LocalPlayerFaction;

            var fogShader = ResolveFogShader();
            if (fogShader == null)
            {
                Debug.LogError(
                    "[FogOfWar] Shader \"Unlit/FogOfWar\" could not be resolved — "
                    + "continuing WITHOUT fog of war. Expected it at "
                    + "Assets/Scripts/World/FogOfWar/Resources/" + FogShaderResource + ".shader.");
            }
            else
            {
                var mat = new Material(fogShader);
                mat.renderQueue = 3000;
                mgr.FogMaterial = mat;
            }

            // Cover the ACTUAL playable rect. Baked terrains sit corner-at-
            // origin (0..size), so the old origin-centred ±MapHalfSize box
            // (with MapHalfSize snapped to the FARTHEST terrain coordinate)
            // built a surface and grid 2x the map per side — 4x the area,
            // burning 4x the per-frame fog work. Fall back to the centred
            // box only when no terrain exists yet (procedural maps are
            // origin-centred by construction).
            Vector2 min, max;
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                var tpos = terrain.transform.position;
                var tsize = terrain.terrainData.size;
                min = new Vector2(tpos.x, tpos.z);
                max = new Vector2(tpos.x + tsize.x, tpos.z + tsize.z);
            }
            else
            {
                int half = Mathf.Max(16, GameSettings.MapHalfSize);
                min = new Vector2(-half, -half);
                max = new Vector2(half, half);
            }
            min -= new Vector2(EdgePad, EdgePad);
            max += new Vector2(EdgePad, EdgePad);

            // FOG IS FINER THAN THE GAME GRID (2026-08-31 directive, rev.2).
            // Fog of war is a knowledge layer, not the sim grid — they are
            // different things, and the fog boundary must never be as chunky
            // as a build cell. So the cell is DERIVED from the build grid at
            // half its size (1 m today), on every map: the texel count grows
            // with map area, which is what keeps the explored/unexplored
            // boundary equally crisp on a big map instead of equally coarse.
            // (An earlier span/512 rule capped the texel count instead; on
            // Veilmarch that made fog cells EQUAL to the 2 m build grid.
            // Measured note: the 1 m grid never appeared in the Perf.log
            // spike ledger — the fog was not one of the lag offenders.)
            // The conforming mesh still densifies on big maps so the overlay
            // hugs the terrain instead of floating over slopes.
            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            float cell = BuildGrid.CellSize * 0.5f;
            int fogGrid = span > 700f ? 256 : 128;
            mgr.ApplyBounds(
                min,
                max,
                newCellSize: cell,
                clearRevealed: true,
                surfaceGrid: fogGrid);

            // ApplyBounds creates the fog surface itself; parent it under our root
            // so the hierarchy stays tidy.
            if (mgr.FogRenderer != null)
                mgr.FogRenderer.transform.SetParent(root.transform, true);

            var active = UnityEngine.Terrain.activeTerrain;
            if (active == null || active.terrainData == null)
            {
                root.AddComponent<OneShotFoWRebuilder>().Init(mgr, fogGrid);
            }
        }

        /// <summary>
        /// Helper component that rebuilds FoW mesh once terrain is available.
        /// </summary>
        private class OneShotFoWRebuilder : MonoBehaviour
        {
            FogOfWarManager _mgr;
            int _grid;

            public void Init(FogOfWarManager mgr, int grid) { _mgr = mgr; _grid = grid; }

            void LateUpdate()
            {
                var t = UnityEngine.Terrain.activeTerrain;
                if (t == null || t.terrainData == null) return;

                // Re-derive the bounds from the terrain that just appeared —
                // grid, surface and revealed data all resize to the actual
                // playable rect (ApplyBounds keeps exploration progress).
                var tpos = t.transform.position;
                var tsize = t.terrainData.size;
                // Same rule as the bootstrap: fog cell derives from the build
                // grid (half a build cell), never from the span — only the
                // conforming-mesh density scales with the map.
                float lateSpan = Mathf.Max(tsize.x, tsize.z);
                _mgr.ApplyBounds(
                    new Vector2(tpos.x - EdgePad, tpos.z - EdgePad),
                    new Vector2(tpos.x + tsize.x + EdgePad, tpos.z + tsize.z + EdgePad),
                    newCellSize: BuildGrid.CellSize * 0.5f,
                    clearRevealed: false,
                    surfaceGrid: lateSpan > 700f ? 256 : _grid);
                if (_mgr.FogRenderer != null)
                    _mgr.FogRenderer.transform.SetParent(transform, true);

                Destroy(this);
            }
        }
    }

    /// <summary>
    /// Creates a terrain-conforming mesh for the fog overlay to avoid z-fighting.
    /// </summary>
    public static class FogOfWarConformingMesh
    {
        public static GameObject Create(Vector2 worldMin, Vector2 worldMax, int grid = 128, Material mat = null)
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            // MapMagic tiles load with a Terrain whose TerrainData is null
            // until the graph regenerates — treat that as no terrain at all.
            if (terrain == null || terrain.terrainData == null)
                return CreateFlatQuad(worldMin, worldMax, mat);

            var td = terrain.terrainData;
            var tpos = terrain.transform.position;
            var tsize = td.size;

            int vertsX = Mathf.Max(2, grid + 1);
            int vertsZ = Mathf.Max(2, grid + 1);

            var verts = new Vector3[vertsX * vertsZ];
            var uvs = new Vector2[verts.Length];
            var tris = new int[(vertsX - 1) * (vertsZ - 1) * 6];

            for (int z = 0; z < vertsZ; z++)
            {
                float vz = Mathf.Lerp(worldMin.y, worldMax.y, z / (float)(vertsZ - 1));
                float vT = Mathf.InverseLerp(tpos.z, tpos.z + tsize.z, vz);
                for (int x = 0; x < vertsX; x++)
                {
                    float vx = Mathf.Lerp(worldMin.x, worldMax.x, x / (float)(vertsX - 1));
                    float uT = Mathf.InverseLerp(tpos.x, tpos.x + tsize.x, vx);

                    float y = td.GetInterpolatedHeight(uT, vT) + 0.03f;
                    int i = z * vertsX + x;
                    verts[i] = new Vector3(vx, y, vz);

                    float u = Mathf.InverseLerp(worldMin.x, worldMax.x, vx);
                    float v = Mathf.InverseLerp(worldMin.y, worldMax.y, vz);
                    uvs[i] = new Vector2(u, v);
                }
            }

            int ti = 0;
            for (int z = 0; z < vertsZ - 1; z++)
            {
                for (int x = 0; x < vertsX - 1; x++)
                {
                    int i0 = z * vertsX + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsX;
                    int i3 = i2 + 1;

                    tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                    tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
                }
            }

            var mesh = new Mesh { name = "FogConformMesh" };
            mesh.indexFormat = (verts.Length > 65000)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("FogConforming");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;

            if (mat == null) mat = new Material(Shader.Find("Unlit/FogOfWar"));
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
            return go;
        }

        static GameObject CreateFlatQuad(Vector2 min, Vector2 max, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FogOfWar";
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            go.transform.position = new Vector3(0, 0.2f, 0);
            go.transform.localScale = new Vector3(max.x - min.x, max.y - min.y, 1);
            var mr = go.GetComponent<MeshRenderer>();
            if (mat == null) mat = new Material(Shader.Find("Unlit/FogOfWar"));
            mr.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col) UnityEngine.Object.Destroy(col);
            return go;
        }
    }
}