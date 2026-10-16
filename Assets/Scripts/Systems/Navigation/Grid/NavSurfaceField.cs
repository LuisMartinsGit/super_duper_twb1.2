// NavSurfaceField.cs
// Burst-readable mirrors of the three MANAGED walking-surface authorities the
// movement integrator consults every step (perf pass 2026-10-15):
//
//   * the terrain height  — TerrainUtility.GetHeight (Terrain.SampleHeight)
//   * the bridge decks    — BridgeSurface.TryGetDeckHeight (managed pieces)
//   * the terrain mask    — PassabilityGrid.GetCell / IsBridgeDeckOnly
//
// None of those can be called from a Burst job, and they were what kept
// UnitIntegratorSystem a main-thread foreach (35 ms of a 49 ms late-game
// tick). This file snapshots each into NativeArrays on the main thread, and
// the job reads them through the blittable views NavSurface / NavTerrainMask.
//
// HEIGHTS ARE A BAKED GRID, NOT SampleHeight. The terrain heightmap is copied
// once per terrain (TerrainData.GetHeights — the stored 16-bit samples, so
// every peer bakes identical floats) and sampled with BILINEAR interpolation
// in Burst (FloatMode.Deterministic). That differs from Unity's SampleHeight
// by a few millimetres between samples, and from the managed GetHeight that
// presentation and the other sim systems still call — harmless (units are
// re-snapped every step), and IDENTICAL on every peer, which is what
// lockstep needs. The heightmap never changes at runtime (no SetHeights
// outside generation), so the bake re-runs only when the terrain object or
// its data changes (TerrainUtility.TerrainVersion / reference / size).
//
// Bridges are static scene furniture (BridgeSurface header) — re-copied only
// when BridgeSurface.Version moves. The deck-only mask is a managed bool[] in
// PassabilityGrid, re-copied when its MaskGeneration moves; the cell mask
// itself is PassabilityGrid.Cells, already a NativeArray, read in place (the
// integrator completes its jobs inside its own OnUpdate, so no managed write
// to it can overlap a read).
//
// Main thread only: Refresh() / views are called from system OnUpdate.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>One bridge deck piece: rows 0-2 of its world-to-local matrix
    /// plus its mesh-local bounds. See BridgeSurface.SampleDeck.</summary>
    public struct NavBridgePiece
    {
        public float4 R0, R1, R2;
        public float3 Center;
        public float3 Extents;
    }

    /// <summary>
    /// Job-side walking-surface view: baked terrain heights + bridge decks.
    /// Mirrors TerrainUtility.GetHeight / GetSurfaceHeight,
    /// BridgeSurface.TryGetDeckHeight and LayeredMoveSystem.RampartSurfaceY.
    /// </summary>
    public struct NavSurface
    {
        [ReadOnly] public NativeArray<float> Heights;   // ResZ * ResX, world Y minus BaseY
        public int ResX, ResZ;
        public float2 TerrainOrigin;                     // terrain transform XZ
        public float2 TerrainSize;                       // terrainData.size XZ
        public float BaseY;                              // terrain transform Y
        public byte HasTerrain;

        [ReadOnly] public NativeArray<NavBridgePiece> Pieces;
        [ReadOnly] public NativeArray<float4> BridgeAabb;   // minX, minZ, maxX, maxZ
        [ReadOnly] public NativeArray<int2> BridgeRange;    // first piece, piece count
        public int BridgeCount;

        /// <summary>Ground height at (x, z). Bilinear over the baked heightmap,
        /// clamped to its edges. <paramref name="fallback"/> when no terrain is
        /// baked (terrainless scenes) — the caller passes "stay where you are".</summary>
        public float GroundHeight(float x, float z, float fallback)
        {
            if (HasTerrain == 0) return fallback;
            float u = (x - TerrainOrigin.x) / TerrainSize.x * (ResX - 1);
            float v = (z - TerrainOrigin.y) / TerrainSize.y * (ResZ - 1);
            // NaN guard: never index with a garbage cell (no bounds checks in
            // a player build). Same spirit as GetHeight's non-finite guard.
            if (math.isnan(u) || math.isnan(v)) return fallback;
            u = math.clamp(u, 0f, ResX - 1);
            v = math.clamp(v, 0f, ResZ - 1);
            int x0 = (int)math.floor(u);
            int z0 = (int)math.floor(v);
            int x1 = math.min(x0 + 1, ResX - 1);
            int z1 = math.min(z0 + 1, ResZ - 1);
            float fx = u - x0;
            float fz = v - z0;
            float h00 = Heights[z0 * ResX + x0];
            float h10 = Heights[z0 * ResX + x1];
            float h01 = Heights[z1 * ResX + x0];
            float h11 = Heights[z1 * ResX + x1];
            float h = math.lerp(math.lerp(h00, h10, fx), math.lerp(h01, h11, fx), fz);
            return h + BaseY;
        }

        /// <summary>BridgeSurface.TryGetDeckHeight: highest deck top face
        /// under the vertical line at (x, z).</summary>
        public bool TryGetDeckHeight(float x, float z, out float y)
        {
            y = float.MinValue;
            bool hit = false;
            for (int b = 0; b < BridgeCount; b++)
            {
                float4 bb = BridgeAabb[b];
                if (x < bb.x || x > bb.z || z < bb.y || z > bb.w) continue;
                int2 r = BridgeRange[b];
                for (int i = r.x; i < r.x + r.y; i++)
                {
                    var p = Pieces[i];
                    // basePt = WorldToLocal * (x, 0, z, 1); yCol = column 1.
                    float bx = p.R0.x * x + p.R0.z * z + p.R0.w;
                    float by = p.R1.x * x + p.R1.z * z + p.R1.w;
                    float bz = p.R2.x * x + p.R2.z * z + p.R2.w;
                    float cx = p.R0.y, cy = p.R1.y, cz = p.R2.y;
                    if (math.abs(cy) < 1e-6f) continue;   // degenerate (vertical deck)

                    float topLocalY = p.Center.y + p.Extents.y;
                    float t = (topLocalY - by) / cy;      // world Y of the top face
                    float lx = bx + t * cx;
                    float lz = bz + t * cz;
                    if (math.abs(lx - p.Center.x) > p.Extents.x + BridgeSurface.DeckEdgeEpsilon) continue;
                    if (math.abs(lz - p.Center.z) > p.Extents.z + BridgeSurface.DeckEdgeEpsilon) continue;
                    if (t > y) { y = t; hit = true; }
                }
            }
            return hit;
        }

        /// <summary>TerrainUtility.GetSurfaceHeight: the deck wins when it is
        /// above the ground and within step-up reach of <paramref name="referenceY"/>.</summary>
        public float SurfaceHeight(float x, float z, float referenceY)
        {
            float ground = GroundHeight(x, z, referenceY);
            if (BridgeCount > 0
                && TryGetDeckHeight(x, z, out float deckY)
                && deckY > ground
                && deckY - referenceY <= BridgeSurface.MountStepLimit)
                return deckY;
            return ground;
        }

        /// <summary>LayeredMoveSystem.RampartSurfaceY: a bridge deck where one
        /// covers the point, else the stone wall-walk over the ground
        /// (LayerTransitionSystem.DeckYAt).</summary>
        public float RampartSurfaceY(float x, float z, float referenceY)
        {
            if (BridgeCount > 0 && TryGetDeckHeight(x, z, out float deckY))
                return deckY;
            const float deck = TheWaningBorder.Entities.AlanthorWall.DeckHeight;
            return GroundHeight(x, z, referenceY - deck) + deck;
        }
    }

    /// <summary>Job-side view of PassabilityGrid's terrain mask.</summary>
    public struct NavTerrainMask
    {
        [ReadOnly] public NativeArray<byte> Cells;
        [ReadOnly] public NativeArray<byte> DeckOnly;
        public int Width, Height;
        public float CellSize;
        public float2 Origin;
        /// <summary>PassabilityGrid.Instance != null &amp;&amp; IsMaskReady.</summary>
        public byte Ready;

        public int2 WorldToCell(float3 p)
            => new int2((int)math.floor((p.x - Origin.x) / CellSize),
                        (int)math.floor((p.z - Origin.y) / CellSize));

        /// <summary>PassabilityGrid.GetCell — TerrainBlocked off the grid.</summary>
        public byte GetCell(int2 c)
        {
            if (c.x < 0 || c.x >= Width || c.y < 0 || c.y >= Height)
                return PassabilityGrid.TerrainBlocked;
            return Cells[c.y * Width + c.x];
        }

        /// <summary>PassabilityGrid.IsBridgeDeckOnly.</summary>
        public bool IsBridgeDeckOnly(int2 c)
        {
            if (c.x < 0 || c.x >= Width || c.y < 0 || c.y >= Height) return false;
            return DeckOnly[c.y * Width + c.x] != 0;
        }
    }

    /// <summary>Owner of the native snapshots. Main thread only.</summary>
    public static class NavSurfaceField
    {
        // Terrain bake.
        private static NativeArray<float> s_heights;
        private static int s_resX, s_resZ;
        private static float2 s_terrainOrigin, s_terrainSize;
        private static float s_baseY;
        private static bool s_hasTerrain;
        private static TerrainData s_bakedData;
        private static int s_bakedVersion = int.MinValue;
        private static Vector3 s_bakedPos, s_bakedSize;

        // Bridges.
        private static NativeArray<NavBridgePiece> s_pieces;
        private static NativeArray<float4> s_aabb;
        private static NativeArray<int2> s_range;
        private static int s_bridgeCount;
        private static int s_bridgeVersion = int.MinValue;

        // Deck-only mask mirror.
        private static NativeArray<byte> s_deckOnly;
        private static PassabilityGrid s_maskGrid;
        private static int s_maskGeneration = int.MinValue;

        // Placeholder for a mask that is not built yet: a job field must
        // always hold an allocated container.
        private static NativeArray<byte> s_emptyBytes;

        private static bool s_quitHooked;

        private static void EnsureAllocated()
        {
            if (!s_quitHooked)
            {
                s_quitHooked = true;
                Application.quitting += Dispose;
            }
            if (!s_heights.IsCreated) s_heights = new NativeArray<float>(1, Allocator.Persistent);
            if (!s_pieces.IsCreated) s_pieces = new NativeArray<NavBridgePiece>(1, Allocator.Persistent);
            if (!s_aabb.IsCreated) s_aabb = new NativeArray<float4>(1, Allocator.Persistent);
            if (!s_range.IsCreated) s_range = new NativeArray<int2>(1, Allocator.Persistent);
            if (!s_deckOnly.IsCreated) s_deckOnly = new NativeArray<byte>(1, Allocator.Persistent);
            if (!s_emptyBytes.IsCreated) s_emptyBytes = new NativeArray<byte>(1, Allocator.Persistent);
        }

        /// <summary>Free every snapshot. Idempotent; the next Get re-bakes.</summary>
        public static void Dispose()
        {
            if (s_heights.IsCreated) s_heights.Dispose();
            if (s_pieces.IsCreated) s_pieces.Dispose();
            if (s_aabb.IsCreated) s_aabb.Dispose();
            if (s_range.IsCreated) s_range.Dispose();
            if (s_deckOnly.IsCreated) s_deckOnly.Dispose();
            if (s_emptyBytes.IsCreated) s_emptyBytes.Dispose();
            s_hasTerrain = false;
            s_bakedData = null;
            s_bakedVersion = int.MinValue;
            s_bridgeCount = 0;
            s_bridgeVersion = int.MinValue;
            s_maskGrid = null;
            s_maskGeneration = int.MinValue;
        }

        /// <summary>The walking-surface view, re-baking whatever changed.</summary>
        public static NavSurface GetSurface()
        {
            EnsureAllocated();
            RefreshTerrain();
            RefreshBridges();
            return new NavSurface
            {
                Heights = s_heights,
                ResX = s_resX,
                ResZ = s_resZ,
                TerrainOrigin = s_terrainOrigin,
                TerrainSize = s_terrainSize,
                BaseY = s_baseY,
                HasTerrain = (byte)(s_hasTerrain ? 1 : 0),
                Pieces = s_pieces,
                BridgeAabb = s_aabb,
                BridgeRange = s_range,
                BridgeCount = s_bridgeCount,
            };
        }

        /// <summary>The terrain-mask view (Ready = 0 until PassabilityGrid's
        /// mask is built, exactly the integrator's old IsMaskReady gate).</summary>
        public static NavTerrainMask GetMask()
        {
            EnsureAllocated();
            var pg = PassabilityGrid.Instance;
            if (pg == null || !pg.IsMaskReady || !pg.Cells.IsCreated
                || pg.Cells.Length < pg.Width * pg.Height)
            {
                return new NavTerrainMask
                {
                    Cells = s_emptyBytes, DeckOnly = s_emptyBytes,
                    Width = 0, Height = 0, CellSize = 1f, Origin = float2.zero, Ready = 0,
                };
            }

            if (!ReferenceEquals(pg, s_maskGrid) || pg.MaskGeneration != s_maskGeneration
                || s_deckOnly.Length != pg.Width * pg.Height)
            {
                int n = pg.Width * pg.Height;
                if (s_deckOnly.Length != n)
                {
                    s_deckOnly.Dispose();
                    s_deckOnly = new NativeArray<byte>(math.max(1, n), Allocator.Persistent);
                }
                for (int z = 0; z < pg.Height; z++)
                    for (int x = 0; x < pg.Width; x++)
                        s_deckOnly[z * pg.Width + x] = (byte)(pg.IsBridgeDeckOnly(new int2(x, z)) ? 1 : 0);
                s_maskGrid = pg;
                s_maskGeneration = pg.MaskGeneration;
            }

            return new NavTerrainMask
            {
                Cells = pg.Cells,
                DeckOnly = s_deckOnly,
                Width = pg.Width,
                Height = pg.Height,
                CellSize = pg.CellSize,
                Origin = new float2(pg.Origin.x, pg.Origin.z),
                Ready = 1,
            };
        }

        private static void RefreshTerrain()
        {
            // Same gate GameBootstrap / PassabilityGrid wait on: the heightmap
            // is not final until generation completes. The terrain itself is
            // resolved exactly as TerrainUtility.GetHeight resolves it.
            if (!ProceduralTerrain.IsGenerationComplete)
            {
                s_hasTerrain = false;
                return;
            }
            var terrain = TerrainUtility.GetActiveTerrain();
            if (terrain == null || terrain.terrainData == null) { s_hasTerrain = false; return; }

            var data = terrain.terrainData;
            var pos = terrain.transform.position;
            var size = data.size;
            if (s_hasTerrain && ReferenceEquals(data, s_bakedData)
                && s_bakedVersion == TerrainUtility.TerrainVersion
                && pos == s_bakedPos && size == s_bakedSize)
                return;

            int res = data.heightmapResolution;
            if (res < 2 || size.x <= 0f || size.z <= 0f) { s_hasTerrain = false; return; }

            // GetHeights returns [z, x], normalised 0..1 from the stored
            // 16-bit samples — the same bits on every peer.
            float[,] h = data.GetHeights(0, 0, res, res);
            if (s_heights.Length != res * res)
            {
                s_heights.Dispose();
                s_heights = new NativeArray<float>(res * res, Allocator.Persistent);
            }
            float sy = size.y;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    s_heights[z * res + x] = h[z, x] * sy;

            s_resX = res;
            s_resZ = res;
            s_terrainOrigin = new float2(pos.x, pos.z);
            s_terrainSize = new float2(size.x, size.z);
            s_baseY = pos.y;
            s_hasTerrain = true;
            s_bakedData = data;
            s_bakedVersion = TerrainUtility.TerrainVersion;
            s_bakedPos = pos;
            s_bakedSize = size;
        }

        private static readonly List<BridgeSurface.PieceSnapshot> s_pieceScratch = new List<BridgeSurface.PieceSnapshot>();
        private static readonly List<Vector4> s_aabbScratch = new List<Vector4>();
        private static readonly List<Vector2Int> s_rangeScratch = new List<Vector2Int>();

        private static void RefreshBridges()
        {
            if (BridgeSurface.Version == s_bridgeVersion) return;
            s_bridgeVersion = BridgeSurface.Version;

            BridgeSurface.Snapshot(s_pieceScratch, s_aabbScratch, s_rangeScratch);
            int np = s_pieceScratch.Count, nb = s_aabbScratch.Count;

            if (s_pieces.Length != math.max(1, np))
            {
                s_pieces.Dispose();
                s_pieces = new NativeArray<NavBridgePiece>(math.max(1, np), Allocator.Persistent);
            }
            if (s_aabb.Length != math.max(1, nb))
            {
                s_aabb.Dispose();
                s_aabb = new NativeArray<float4>(math.max(1, nb), Allocator.Persistent);
                s_range.Dispose();
                s_range = new NativeArray<int2>(math.max(1, nb), Allocator.Persistent);
            }

            for (int i = 0; i < np; i++)
            {
                var p = s_pieceScratch[i];
                var m = p.WorldToLocal;
                s_pieces[i] = new NavBridgePiece
                {
                    R0 = new float4(m.m00, m.m01, m.m02, m.m03),
                    R1 = new float4(m.m10, m.m11, m.m12, m.m13),
                    R2 = new float4(m.m20, m.m21, m.m22, m.m23),
                    Center = p.BoundsCenter,
                    Extents = p.BoundsExtents,
                };
            }
            for (int b = 0; b < nb; b++)
            {
                var a = s_aabbScratch[b];
                s_aabb[b] = new float4(a.x, a.y, a.z, a.w);
                var r = s_rangeScratch[b];
                s_range[b] = new int2(r.x, r.y);
            }
            s_bridgeCount = nb;
        }
    }
}
