using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        /// <summary>Seconds between catch-up uploads of the explored map once
        /// the live layer carries the moving sight (see EndFrameAndBuild).</summary>
        public float RevealedPushInterval = 2f;
        int _staticVersion, _pushedStaticVersion = -1;

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

        /// <summary>The explored-ground map, for a saved game
        /// (docs/Design/Replays_And_Saves.md). Null before Awake.</summary>
        public byte[] ExportRevealed() => _revealed.IsCreated ? _revealed.ToArray() : null;

        /// <summary>Restore what <see cref="ExportRevealed"/> wrote. Ignored if the
        /// map's fog grid has a different size.</summary>
        public void ImportRevealed(byte[] data)
        {
            if (data == null || !_revealed.IsCreated || data.Length != _revealed.Length) return;
            _revealed.CopyFrom(data);
        }

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
            if (_static.IsCreated) _static.Dispose();
            if (_pushedVis.IsCreated) _pushedVis.Dispose();
            if (_pushedRev.IsCreated) _pushedRev.Dispose();
            if (_prevVis.IsCreated) _prevVis.Dispose();
            if (_prevRev.IsCreated) _prevRev.Dispose();
            if (_dispStatic.IsCreated) _dispStatic.Dispose();
            ReleaseLive();
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

        // ---- Late-game pass bookkeeping (2026-09-25 perf pass) ----------
        //
        // _sliceDirty: a faction slice of _visible MAY hold non-zero cells.
        // Clearing used to zero all eight slices every pass (8 MB on a
        // million-cell map) although a 2-player match only ever writes two.
        //
        // _static: a per-faction layer holding the stamps of everything that
        // does NOT move (buildings, walls, towers). Late game those are most
        // of the sighted entities, and every one was re-stamped each pass.
        // The layer is rebuilt only when the static set's content hash
        // changes; each pass then copies it and stamps only the units. Max
        // is commutative, so the result is byte-identical to stamping all.
        readonly bool[] _sliceDirty = new bool[MaxFactions];

        /// <summary>Bumped whenever any grid may have changed (a stamp pass,
        /// a reveal, a team merge, a rebuild). Readers that sample the grids
        /// on their own cadence — the minimap — skip the sampling when it has
        /// not moved. Presentation only; never read by the simulation.</summary>
        public int DataVersion { get; private set; }
        readonly bool[] _staticHas = new bool[MaxFactions];
        NativeArray<byte> _static;
        ulong _staticHash;
        int _staticCount = -1;
        bool _staticValid;

        static int FIdx(Faction f)
        {
            int fi = (int)f;
            if (fi < 0) fi = -fi;
            return fi % MaxFactions;
        }

        unsafe void ClearVisible()
        {
            if (!_visible.IsCreated) return;
            int cells = _w * _h;
            byte* p = (byte*)_visible.GetUnsafePtr();
            for (int f = 0; f < MaxFactions; f++)
            {
                if (!_sliceDirty[f]) continue;
                UnsafeUtility.MemClear(p + (long)f * cells, cells);
                _sliceDirty[f] = false;
            }
        }

        /// <summary>Drops the cached static layer (grid resized / revealed
        /// wiped) so the next pass re-stamps it into both grids.</summary>
        void InvalidateStatic()
        {
            _staticValid = false;
            for (int f = 0; f < MaxFactions; f++) { _staticHas[f] = false; _sliceDirty[f] = false; }
            if (_static.IsCreated) _static.Dispose();
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
            DataVersion++;
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
            _sliceDirty[FIdx(f)] = true;
            DataVersion++;
        }

        /// <summary>One LoS circle for the Burst stamp batch.</summary>
        public struct StampCommand
        {
            public Vector3 Position;
            public float Radius;
            public Faction Faction;
            /// <summary>The sighted entity (Entity.Null for the Shardroot's
            /// reveal): the live layer interpolates each source's circle
            /// between ticks, as the renderer does its model.</summary>
            public Unity.Entities.Entity Source;
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
            RunFactionPass(commands, count, clearFirst: false);
            DataVersion++;
        }

        /// <summary>
        /// One full reveal pass: clear, stamp, and (when the static set has
        /// changed) rebuild the static layer. Replaces BeginFrame +
        /// StampBatch for the per-tick caller.
        ///
        /// <paramref name="statics"/> are the sighted entities that do not
        /// move; <paramref name="staticHash"/> is an ORDER-INDEPENDENT hash of
        /// their (faction, position, radius), so a chunk reorder never forces
        /// a rebuild but any real change (placed, destroyed, upgraded LoS,
        /// moved) does. Correctness never depends on the static/mobile split:
        /// a mis-classified mover simply changes the hash and rebuilds.
        /// </summary>
        public void StampPass(NativeArray<StampCommand> statics, int staticCount, ulong staticHash,
                              NativeArray<StampCommand> mobiles, int mobileCount)
        {
            if (!_visible.IsCreated) return;

            if (!_staticValid || staticHash != _staticHash || staticCount != _staticCount)
                RebuildStatic(statics, staticCount, staticHash);

            RunFactionPass(mobiles, mobileCount, clearFirst: true);
            DataVersion++;
        }

        /// <summary>True when the cached static layer was built from exactly
        /// this (hash, count) and is still valid — a caller that knows its
        /// static set has not changed may then pass no commands at all
        /// (StampPass will not rebuild). 2026-10-16 perf.</summary>
        public bool StaticLayerMatches(ulong hash, int count)
            => _staticValid && _static.IsCreated && hash == _staticHash && count == _staticCount;

        unsafe void RebuildStatic(NativeArray<StampCommand> statics, int count, ulong hash)
        {
            int cells = _w * _h;
            if (!_static.IsCreated || _static.Length != MaxFactions * cells)
            {
                if (_static.IsCreated) _static.Dispose();
                _static = new NativeArray<byte>(MaxFactions * cells, Allocator.Persistent);
                for (int f = 0; f < MaxFactions; f++) _staticHas[f] = false;
            }

            var cmds = SortByFaction(statics, count, out var start, out var cnt);
            var flags = new NativeArray<byte>(MaxFactions, Allocator.TempJob);
            for (int f = 0; f < MaxFactions; f++)
            {
                // Clear a slice that held stamps before or is about to.
                bool has = cnt[f] > 0;
                flags[f] = (byte)((_staticHas[f] || has) ? FlagClear : 0);
                _staticHas[f] = has;
            }

            // Static stamps go into the LAYER and into Revealed. Revealed
            // never shrinks (only a grid rebuild wipes it, which invalidates
            // this cache), so after this one write it already contains every
            // static stamp and the per-pass copy need only touch Visible.
            new FactionPassJob
            {
                Vis = (byte*)_static.GetUnsafePtr(),
                Rev = (byte*)_revealed.GetUnsafePtr(),
                Static = null,
                Cmds = cmds,
                Start = start,
                Count = cnt,
                Flags = flags,
                Cells = cells,
                W = _w,
                H = _h,
            }.Schedule(MaxFactions, 1).Complete();

            cmds.Dispose(); start.Dispose(); cnt.Dispose(); flags.Dispose();
            _staticHash = hash;
            _staticCount = count;
            _staticValid = true;
            _staticVersion++;   // the overlay re-uploads the building sight now
        }

        unsafe void RunFactionPass(NativeArray<StampCommand> mobiles, int count, bool clearFirst)
        {
            int cells = _w * _h;
            var cmds = SortByFaction(mobiles, count, out var start, out var cnt);
            var flags = new NativeArray<byte>(MaxFactions, Allocator.TempJob);
            bool useStatic = clearFirst && _staticValid && _static.IsCreated;
            for (int f = 0; f < MaxFactions; f++)
            {
                byte fl = 0;
                if (clearFirst)
                {
                    if (useStatic && _staticHas[f]) fl = FlagCopyStatic;
                    else if (_sliceDirty[f]) fl = FlagClear;
                    _sliceDirty[f] = (useStatic && _staticHas[f]) || cnt[f] > 0;
                }
                else if (cnt[f] > 0) _sliceDirty[f] = true;
                flags[f] = fl;
            }

            new FactionPassJob
            {
                Vis = (byte*)_visible.GetUnsafePtr(),
                Rev = (byte*)_revealed.GetUnsafePtr(),
                Static = useStatic ? (byte*)_static.GetUnsafeReadOnlyPtr() : null,
                Cmds = cmds,
                Start = start,
                Count = cnt,
                Flags = flags,
                Cells = cells,
                W = _w,
                H = _h,
            }.Schedule(MaxFactions, 1).Complete();

            cmds.Dispose(); start.Dispose(); cnt.Dispose(); flags.Dispose();
        }

        /// <summary>Counting sort of the commands into per-faction runs so
        /// each faction's slice is stamped by exactly one worker.</summary>
        NativeArray<StampJobCmd> SortByFaction(NativeArray<StampCommand> src, int count,
            out NativeArray<int> start, out NativeArray<int> cnt)
        {
            if (count > src.Length) count = src.Length;
            if (count < 0) count = 0;
            start = new NativeArray<int>(MaxFactions, Allocator.TempJob);
            cnt = new NativeArray<int>(MaxFactions, Allocator.TempJob);
            var cmds = new NativeArray<StampJobCmd>(count, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            if (count == 0) return cmds;

            // Burst (2026-10-16): the same counting sort, compiled — it was
            // two managed passes over every sighted unit per fog pass. Same
            // float expressions in the same order (IEEE-exact either way).
            new SortByFactionJob
            {
                Src = src, Count = count, Start = start, Cnt = cnt, Cmds = cmds,
                WorldMinX = WorldMin.x, WorldMinY = WorldMin.y, CellSize = CellSize,
                Cells = _w * _h, MaxF = MaxFactions,
            }.Run();
            return cmds;
        }

        [BurstCompile]
        struct SortByFactionJob : IJob
        {
            [ReadOnly] public NativeArray<StampCommand> Src;
            public int Count;
            public NativeArray<int> Start;
            public NativeArray<int> Cnt;
            public NativeArray<StampJobCmd> Cmds;
            public float WorldMinX, WorldMinY, CellSize;
            public int Cells, MaxF;

            static int Fi(Faction f, int maxF)
            {
                int fi = (int)f;
                if (fi < 0) fi = -fi;
                return fi % maxF;
            }

            public void Execute()
            {
                for (int i = 0; i < Count; i++) Cnt[Fi(Src[i].Faction, MaxF)]++;
                int run = 0;
                for (int f = 0; f < MaxF; f++) { Start[f] = run; run += Cnt[f]; }
                var fill = new NativeArray<int>(MaxF, Allocator.Temp);
                for (int i = 0; i < Count; i++)
                {
                    var c = Src[i];
                    int f = Fi(c.Faction, MaxF);
                    float r = c.Radius / CellSize;
                    Cmds[Start[f] + fill[f]++] = new StampJobCmd
                    {
                        GX = (c.Position.x - WorldMinX) / CellSize,
                        GY = (c.Position.z - WorldMinY) / CellSize,
                        R = 0.01f > r ? 0.01f : r,   // Mathf.Max(0.01f, r), exactly
                        Ofs = f * Cells,
                    };
                }
                fill.Dispose();
            }
        }

        const byte FlagClear = 1;
        const byte FlagCopyStatic = 2;

        public struct StampJobCmd { public float GX, GY, R; public int Ofs; }

        /// <summary>
        /// One worker per faction slice: seed the slice (copy the static
        /// layer, or clear it), then stamp that faction's circles. Slices
        /// are disjoint, so the workers never touch the same byte.
        /// </summary>
        [BurstCompile]
        unsafe struct FactionPassJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public byte* Vis;
            [NativeDisableUnsafePtrRestriction] public byte* Rev;
            [NativeDisableUnsafePtrRestriction] public byte* Static;
            [ReadOnly] public NativeArray<StampJobCmd> Cmds;
            [ReadOnly] public NativeArray<int> Start;
            [ReadOnly] public NativeArray<int> Count;
            [ReadOnly] public NativeArray<byte> Flags;
            public int Cells, W, H;

            public void Execute(int f)
            {
                long ofs = (long)f * Cells;
                byte fl = Flags[f];
                if ((fl & FlagCopyStatic) != 0 && Static != null)
                    UnsafeUtility.MemCpy(Vis + ofs, Static + ofs, Cells);
                else if ((fl & FlagClear) != 0)
                    UnsafeUtility.MemClear(Vis + ofs, Cells);

                int s = Start[f], e = s + Count[f];
                for (int k = s; k < e; k++)
                    StampOne(Cmds[k], Vis, Rev, W, H);
            }
        }

        /// <summary>
        /// Row-span stamp. Byte-identical to the old per-cell loop: the
        /// sqrt per row only bounds which cells are tested — cells outside
        /// the widened outer span are provably beyond rOut, cells inside the
        /// shrunken inner span provably inside rIn (both with a full cell of
        /// margin), and every rim cell still runs the exact original test.
        /// </summary>
        static unsafe void StampOne(in StampJobCmd c, byte* vis, byte* rev, int W, int H)
        {
            float rOut = c.R + 0.75f;
            float rIn = rOut - 1.5f; if (rIn < 0f) rIn = 0f;
            int minx = (int)(c.GX - rOut); if (minx < 0) minx = 0;
            int maxx = (int)(c.GX + rOut) + 1; if (maxx > W - 1) maxx = W - 1;
            int miny = (int)(c.GY - rOut); if (miny < 0) miny = 0;
            int maxy = (int)(c.GY + rOut) + 1; if (maxy > H - 1) maxy = H - 1;
            float r2Out = rOut * rOut;
            float r2In = rIn * rIn;
            float cx = c.GX - 0.5f;

            for (int y = miny; y <= maxy; y++)
            {
                float dy = (y + 0.5f) - c.GY;
                float dy2 = dy * dy;
                if (dy2 > r2Out) continue;     // d2 >= dy2 for every cell of the row

                float hOut = Unity.Mathematics.math.sqrt(r2Out - dy2);
                int x0 = (int)Unity.Mathematics.math.floor(cx - hOut) - 1; if (x0 < minx) x0 = minx;
                int x1 = (int)Unity.Mathematics.math.ceil(cx + hOut) + 1; if (x1 > maxx) x1 = maxx;
                if (x0 > x1) continue;

                int i0 = x1 + 1, i1 = x1;      // empty interior by default
                if (dy2 < r2In)
                {
                    float hIn = Unity.Mathematics.math.sqrt(r2In - dy2);
                    int a = (int)Unity.Mathematics.math.ceil(cx - hIn) + 1;
                    int b = (int)Unity.Mathematics.math.floor(cx + hIn) - 1;
                    if (a < x0) a = x0;
                    if (b > x1) b = x1;
                    if (a <= b) { i0 = a; i1 = b; }
                }

                long row = c.Ofs + (long)y * W;
                RimCells(c, vis, rev, row, y, x0, i0 - 1, r2Out, r2In, rOut);
                if (i0 <= i1)
                {
                    UnsafeUtility.MemSet(vis + row + i0, 255, i1 - i0 + 1);
                    UnsafeUtility.MemSet(rev + row + i0, 255, i1 - i0 + 1);
                }
                RimCells(c, vis, rev, row, y, i1 + 1, x1, r2Out, r2In, rOut);
            }
        }

        static unsafe void RimCells(in StampJobCmd c, byte* vis, byte* rev, long row, int y,
            int xa, int xb, float r2Out, float r2In, float rOut)
        {
            for (int x = xa; x <= xb; x++)
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

                long i = row + x;
                if (cov > vis[i]) vis[i] = cov;
                if (cov > rev[i]) rev[i] = cov;
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

            // UPLOAD CADENCE (2026-10-16, measured on Veilmarch): the texture
            // push — a 4 MB upload on a big map — was ~2.2 ms of every 4 Hz
            // fog pass, and it existed to keep the explored channel up with
            // moving units. The live layer now draws units' sight AND keeps
            // its own explored copy every tick, so the texture only carries
            // building sight (re-pushed the moment that layer changes) and
            // the explored map, which is caught up every
            // RevealedPushInterval. Until a static layer exists the texture
            // still carries everything, at the old cadence.
            float now = Time.unscaledTime;
            bool staticChanged = _staticValid && _staticVersion != _pushedStaticVersion;
            if (!staticChanged && now < _nextTextureTime) return;
            _nextTextureTime = now + Mathf.Max(0f, _staticValid ? RevealedPushInterval : TextureUpdateInterval);
            _pushedStaticVersion = _staticVersion;
            EnsureMaterialBound();
            PushHumanTexture();
        }

        /// <summary>
        /// Max every team member's visibility into a shared result and write
        /// it back to each member. Costs nothing in a free-for-all: if no team
        /// has two or more members the whole pass is skipped, which is the
        /// default lobby state.
        ///
        /// Burst, parallel over cells, in place (2026-09-25): the managed
        /// per-cell loop was a million-iteration C# loop per team per pass.
        /// Each cell reads every member and writes the max back to every
        /// member, so no scratch buffer is needed and the result is identical.
        /// </summary>
        unsafe void MergeTeamVision()
        {
            if (!_visible.IsCreated || !_revealed.IsCreated) return;

            int cells = _w * _h;
            if (cells <= 0) return;

            for (byte team = 1; team <= Alliances.MaxTeams; team++)
            {
                int memberCount = 0;
                Span<int> members = stackalloc int[MaxFactions];
                for (int f = 0; f < MaxFactions; f++)
                {
                    if (Alliances.TeamOf((Faction)f) != team) continue;
                    members[memberCount++] = f;
                }
                if (memberCount < 2) continue;   // solo team == no sharing to do

                var offsets = new NativeArray<int>(memberCount, Allocator.TempJob);
                bool anyDirty = false;
                for (int m = 0; m < memberCount; m++)
                {
                    offsets[m] = members[m] * cells;
                    anyDirty |= _sliceDirty[members[m]];
                }

                new TeamMergeJob
                {
                    Vis = (byte*)_visible.GetUnsafePtr(),
                    Rev = (byte*)_revealed.GetUnsafePtr(),
                    Offsets = offsets,
                }.Schedule(cells, 16384).Complete();
                offsets.Dispose();

                if (anyDirty)
                    for (int m = 0; m < memberCount; m++) _sliceDirty[members[m]] = true;
            }
        }

        [BurstCompile]
        unsafe struct TeamMergeJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public byte* Vis;
            [NativeDisableUnsafePtrRestriction] public byte* Rev;
            [ReadOnly] public NativeArray<int> Offsets;

            public void Execute(int i)
            {
                byte v = 0, r = 0;
                for (int m = 0; m < Offsets.Length; m++)
                {
                    long o = Offsets[m] + (long)i;
                    if (Vis[o] > v) v = Vis[o];
                    if (Rev[o] > r) r = Rev[o];
                }
                for (int m = 0; m < Offsets.Length; m++)
                {
                    long o = Offsets[m] + (long)i;
                    Vis[o] = v;
                    Rev[o] = r;
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
            bool reinit = false;
            if (data.Length != required)
            {
                _tex.Reinitialize(_w, _h);
                data = _tex.GetRawTextureData<byte>();
                EnsureMaterialBound();
                reinit = true;
            }

            // Nothing to upload when the human slice is byte-identical to the
            // last push (2026-09-25): an idle base, a paused match, a stable
            // late-game front. The 4 MB Apply is the expensive half of a push;
            // a 2 MB memcmp is not. Skipping also lets an in-flight crossfade
            // finish on its own clock, which is what a same-data push would
            // have converged to anyway.
            // The VISIBLE channel is the static (building) sight only — units
            // are the live GPU layer's (see SetLiveSight). Falls back to the
            // whole grid until the first stamp pass has built a static layer.
            var visSrc = DisplayVisible(cells, ofs, out int visOfs);

            if (!reinit && _pushedVis.IsCreated && _pushedVis.Length == cells
                && SliceEquals(visSrc, visOfs, _pushedVis, cells)
                && SliceEquals(_revealed, ofs, _pushedRev, cells))
                return;

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
                Visible = visSrc,
                VisOfs = visOfs,
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
            }.Schedule(cells, 16384).Complete();

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

        /// <summary>
        /// What the overlay shows as VISIBLE from the CPU grid: the static
        /// (building) sight of the viewer and their team-mates, max-merged —
        /// the moving sight is drawn live on the GPU. Before the first stamp
        /// pass has built a static layer it is the whole visible grid, which
        /// is what the overlay showed before the live layer existed.
        /// </summary>
        NativeArray<byte> DisplayVisible(int cells, int humanOfs, out int ofs)
        {
            if (!_staticValid || !_static.IsCreated || _static.Length != MaxFactions * cells)
            {
                ofs = humanOfs;
                return _visible;
            }
            if (!_dispStatic.IsCreated || _dispStatic.Length != cells)
            {
                if (_dispStatic.IsCreated) _dispStatic.Dispose();
                _dispStatic = new NativeArray<byte>(cells, Allocator.Persistent);
            }

            var offsets = new NativeArray<int>(MaxFactions, Allocator.TempJob);
            int n = 0;
            for (int f = 0; f < MaxFactions; f++)
                if (SharesSight((Faction)f, HumanFaction)) offsets[n++] = f * cells;

            new StaticMaxJob
            {
                Static = _static,
                Offsets = offsets,
                Count = n,
                Out = _dispStatic,
            }.Schedule(cells, 16384).Complete();
            offsets.Dispose();

            ofs = 0;
            return _dispStatic;
        }

        NativeArray<byte> _dispStatic;

        [BurstCompile]
        struct StaticMaxJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> Static;
            [ReadOnly] public NativeArray<int> Offsets;
            public int Count;
            public NativeArray<byte> Out;

            public void Execute(int i)
            {
                byte v = 0;
                for (int m = 0; m < Count; m++)
                {
                    byte s = Static[Offsets[m] + i];
                    if (s > v) v = s;
                }
                Out[i] = v;
            }
        }

        // Snapshot of the last pushed coverage (human faction) plus the
        // crossfade-source channels as last written — needed to compute the
        // DISPLAYED field when a push lands mid-fade.
        NativeArray<byte> _pushedVis;
        NativeArray<byte> _pushedRev;
        NativeArray<byte> _prevVis;
        NativeArray<byte> _prevRev;

        static unsafe bool SliceEquals(NativeArray<byte> a, int aOfs, NativeArray<byte> b, int count)
        {
            byte* pa = (byte*)a.GetUnsafeReadOnlyPtr() + aOfs;
            byte* pb = (byte*)b.GetUnsafeReadOnlyPtr();
            return UnsafeUtility.MemCmp(pa, pb, count) == 0;
        }

        // Parallel over cells (2026-09-25): each index touches only its own
        // cell in every array and its own four texel bytes.
        [BurstCompile]
        struct PushJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> Visible;
            [ReadOnly] public NativeArray<byte> Revealed;
            public int VisOfs;   // into Visible (the display source)
            public int Ofs;      // into Revealed (the human faction's slice)
            public int Cells;
            public NativeArray<byte> PrevVis;
            public NativeArray<byte> PrevRev;
            public NativeArray<byte> PushedVis;
            public NativeArray<byte> PushedRev;
            [NativeDisableParallelForRestriction]
            public NativeArray<byte> Data;   // RGBA32 raw texture data
            public int Tq;
            public byte Fresh;

            public void Execute(int i)
            {
                {
                    byte vis = Visible[VisOfs + i];
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

        // ── Live sight layer (2026-10-16) ──────────────────────────────────
        // The CPU grid is stamped at 4 Hz and the overlay crossfades between
        // pushes, so the DISPLAYED fog edge trailed a moving unit by 0.25 to
        // 0.5 s. Moving sight sources (units, eagles, the Shardroot) are now
        // ALSO drawn on the GPU every sim tick, from FogOfWarSystem's
        // per-tick list, into _liveVis; the fog shader shows ground as seen
        // where either layer does. The CPU texture's visible channel carries
        // only the STATIC (building) sight, so the units' lagging CPU stamps
        // cannot leave a trail. Gameplay is untouched: IsVisible, the AI and
        // lockstep still read the CPU grid.
        const int LiveBatch = 1023;   // DrawMeshInstanced's limit per call
        NativeList<StampCommand> _live;
        RenderTexture _liveVis, _liveRev;
        Material _liveMat;
        Mesh _liveQuad;
        UnityEngine.Rendering.CommandBuffer _liveCb;
        MaterialPropertyBlock _liveProps;
        Matrix4x4[] _liveMats = new Matrix4x4[LiveBatch];
        float[] _liveRadii = new float[LiveBatch];
        readonly System.Collections.Generic.List<Matrix4x4> _liveAllMats = new System.Collections.Generic.List<Matrix4x4>(256);
        readonly System.Collections.Generic.List<float> _liveAllRadii = new System.Collections.Generic.List<float>(256);
        int _liveRevFaction = -1;
        Camera _depthCamera;
        const string LiveShaderResource = "FogLiveSight";

        /// <summary>
        /// The moving sight sources of every faction, as of this sim tick.
        /// Called by FogOfWarSystem every tick (the CPU stamp stays 4 Hz).
        /// </summary>
        public void SetLiveSight(NativeArray<StampCommand> commands, int count)
        {
            if (!_live.IsCreated) _live = new NativeList<StampCommand>(256, Allocator.Persistent);
            _live.Clear();
            count = Mathf.Min(count, commands.Length);
            if (count > 0) _live.AddRange(commands.GetSubArray(0, count));
        }

        void LateUpdate()
        {
            EnsureCameraDepth();
            RenderLiveSight();
        }

        /// <summary>
        /// The fog reads the camera DEPTH texture to find each pixel's world
        /// position. The PC pipeline asset has it on; the Mobile one (quality
        /// level 0) does not — so the game camera requests it itself.
        /// </summary>
        void EnsureCameraDepth()
        {
            var cam = Camera.main;
            if (cam == null || cam == _depthCamera) return;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            if (data != null)
                data.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On;
            _depthCamera = cam;
        }

        /// <summary>Do <paramref name="source"/>'s units give sight to
        /// <paramref name="viewer"/>? Itself, or a team-mate — the same rule
        /// MergeTeamVision applies to the CPU grid. docs/Design/Teams.md</summary>
        static bool SharesSight(Faction source, Faction viewer)
        {
            if (source == viewer) return true;
            byte team = Alliances.TeamOf(viewer);
            return team != 0 && Alliances.TeamOf(source) == team;
        }

        void RenderLiveSight()
        {
            if (FogMaterial == null || _w <= 0 || _h <= 0) return;
            if (!GameSettings.ViewFaction.HasValue) return;   // overlay off (full reveal)
            if (!EnsureLiveResources()) return;

            var viewer = HumanFaction;
            float cell = CellSize;
            float pad = 0.75f * cell + cell;   // the ramp's outer edge, plus a texel

            _liveAllMats.Clear();
            _liveAllRadii.Clear();
            _liveFrame++;
            float step = Mathf.Max(0.0001f, TheWaningBorder.Core.Multiplayer.LockstepTiming.TickDuration);
            float dt = Time.deltaTime / step;
            if (_live.IsCreated)
            {
                for (int i = 0; i < _live.Length; i++)
                {
                    var c = _live[i];
                    if (!SharesSight(c.Faction, viewer)) continue;
                    float r = Mathf.Max(0.01f, c.Radius);
                    float size = 2f * (r + pad);
                    Vector2 at = SmoothedSightPos(c, dt);
                    _liveAllMats.Add(Matrix4x4.TRS(new Vector3(at.x, at.y, 0f),
                        Quaternion.identity, new Vector3(size, size, 1f)));
                    _liveAllRadii.Add(r);
                }
            }
            // Forget sources that are gone (dead, out of the shared set) —
            // swept every so often rather than every frame.
            if ((_liveFrame & 63) == 0) SweepLiveViews();

            _liveMat.SetFloat("_Cell", cell);
            var cb = _liveCb;
            cb.Clear();
            cb.SetViewProjectionMatrices(Matrix4x4.identity,
                Matrix4x4.Ortho(WorldMin.x, WorldMax.x, WorldMin.y, WorldMax.y, -1f, 1f));

            // The live explored layer belongs to ONE viewer: a change of
            // perspective (observer following another player) starts it over.
            if (_liveRevFaction != (int)viewer)
            {
                cb.SetRenderTarget(_liveRev);
                cb.ClearRenderTarget(false, true, Color.clear);
                _liveRevFaction = (int)viewer;
            }

            cb.SetRenderTarget(_liveVis);
            cb.ClearRenderTarget(false, true, Color.clear);
            DrawLiveCircles(cb);
            cb.SetRenderTarget(_liveRev);
            DrawLiveCircles(cb);

            Graphics.ExecuteCommandBuffer(cb);
        }

        // Per-source smoothing, the same scheme PresentationSpawnSystem uses
        // for the unit MODELS: a new simulated position opens a segment from
        // where the circle is DRAWN now, played out over one tick of real
        // time. Without it the circles stepped at the 30 Hz tick while the
        // models glided, and the fog edge jittered against its own unit.
        struct LiveView { public Vector2 From, To, Render; public float T; public int Seen; }
        readonly System.Collections.Generic.Dictionary<Unity.Entities.Entity, LiveView> _liveViews =
            new System.Collections.Generic.Dictionary<Unity.Entities.Entity, LiveView>(256);
        readonly System.Collections.Generic.List<Unity.Entities.Entity> _liveStale =
            new System.Collections.Generic.List<Unity.Entities.Entity>(32);
        int _liveFrame;
        const float LiveTeleportSq = 8f * 8f;   // = the renderer's teleport threshold

        Vector2 SmoothedSightPos(in StampCommand c, float dt)
        {
            var sim = new Vector2(c.Position.x, c.Position.z);
            if (c.Source == Unity.Entities.Entity.Null) return sim;

            if (!_liveViews.TryGetValue(c.Source, out var v))
                v = new LiveView { From = sim, To = sim, Render = sim, T = 1f };
            else if (v.To != sim)
            {
                bool teleport = (v.Render - sim).sqrMagnitude > LiveTeleportSq;
                v.From = teleport ? sim : v.Render;
                v.To = sim;
                v.T = teleport ? 1f : 0f;
            }
            v.T = Mathf.Min(1f, v.T + dt);
            v.Render = Vector2.Lerp(v.From, v.To, v.T);
            v.Seen = _liveFrame;
            _liveViews[c.Source] = v;
            return v.Render;
        }

        void SweepLiveViews()
        {
            _liveStale.Clear();
            foreach (var kv in _liveViews)
                if (_liveFrame - kv.Value.Seen > 8) _liveStale.Add(kv.Key);
            foreach (var e in _liveStale) _liveViews.Remove(e);
        }

        void DrawLiveCircles(UnityEngine.Rendering.CommandBuffer cb)
        {
            // A -nographics process (headless batches, lockstep peers) has no
            // instancing: the call throws every frame. Nothing is seen there.
            if (!SystemInfo.supportsInstancing) return;
            int total = _liveAllMats.Count;
            for (int start = 0; start < total; start += LiveBatch)
            {
                int n = Mathf.Min(LiveBatch, total - start);
                _liveAllMats.CopyTo(start, _liveMats, 0, n);
                _liveAllRadii.CopyTo(start, _liveRadii, 0, n);
                _liveProps.Clear();
                _liveProps.SetFloatArray("_Radius", _liveRadii);
                cb.DrawMeshInstanced(_liveQuad, 0, _liveMat, 0, _liveMats, n, _liveProps);
            }
        }

        bool EnsureLiveResources()
        {
            if (_liveMat == null)
            {
                var shader = Resources.Load<Shader>(LiveShaderResource) ?? Shader.Find("Hidden/TWB/FogLiveSight");
                if (shader == null) return false;
                _liveMat = new Material(shader) { enableInstancing = true, hideFlags = HideFlags.DontSave };
            }
            if (_liveQuad == null)
            {
                _liveQuad = new Mesh { name = "FogLiveQuad", hideFlags = HideFlags.DontSave };
                _liveQuad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                };
                _liveQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            }
            if (_liveCb == null) _liveCb = new UnityEngine.Rendering.CommandBuffer { name = "FogLiveSight" };
            if (_liveProps == null) _liveProps = new MaterialPropertyBlock();

            if (_liveVis == null || _liveVis.width != _w || _liveVis.height != _h)
            {
                ReleaseLiveTargets();
                _liveVis = MakeLiveTarget("FogLiveVis");
                _liveRev = MakeLiveTarget("FogLiveRev");
                _liveRevFaction = -1;   // a fresh target holds garbage: clear it
            }
            FogMaterial.SetTexture("_LiveVis", _liveVis);
            FogMaterial.SetTexture("_LiveRev", _liveRev);
            return true;
        }

        RenderTexture MakeLiveTarget(string name)
        {
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            var rt = new RenderTexture(_w, _h, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = FilterMode.Bilinear,   // the same smooth frontier as the CPU texture
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.DontSave,
            };
            rt.Create();
            return rt;
        }

        void ReleaseLiveTargets()
        {
            if (_liveVis != null) { _liveVis.Release(); Destroy(_liveVis); _liveVis = null; }
            if (_liveRev != null) { _liveRev.Release(); Destroy(_liveRev); _liveRev = null; }
        }

        void ReleaseLive()
        {
            ReleaseLiveTargets();
            if (_live.IsCreated) _live.Dispose();
            if (_liveMat != null) Destroy(_liveMat);
            if (_liveQuad != null) Destroy(_liveQuad);
            _liveCb?.Release();
            _liveCb = null;
        }

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
            // Fresh zeroed grid: nothing is dirty, and the static layer is the
            // wrong size and (if Revealed is wiped below) no longer folded in.
            InvalidateStatic();
            DataVersion++;

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

            GameObject surface = FogFullscreenSurface.Create(mat);
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
    /// The fog's draw surface (2026-10-16): one quad already in CLIP space,
    /// covering the whole screen for every camera. The fog shader rebuilds
    /// each pixel's world position from the depth texture, so nothing about
    /// the surface depends on the terrain. It replaced a terrain-conforming
    /// sheet, which fogged everything by the ground BEHIND it — tree tops
    /// slid in and out of the fog as the camera moved.
    /// </summary>
    public static class FogFullscreenSurface
    {
        public static GameObject Create(Material mat)
        {
            var mesh = new Mesh { name = "FogFullscreen" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // The vertices are clip-space, not world: bounds that can never
            // be frustum-culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

            var go = new GameObject("FogFullscreen");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;

            if (mat == null) mat = new Material(Shader.Find("Unlit/FogOfWar"));
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
            return go;
        }
    }
}
