// FarmCropField.Fences.cs
// The field's boundary by Farm level (docs/Design/Age_1_Alanthor.md § The
// Farm, "The field's look"): none at level 1, a rough wooden fence at level 2 —
// Synty pieces (FarmCropField.asset lists them), drawn instanced, one piece per
// non-opening segment. Level 3's low stone wall is NOT drawn here: it is a
// simulation object (FarmStoneWall in the Farm's folder) whose pieces have
// views of their own (§ The level-3 wall).
//
// WHERE the segments go is NOT decided here: FarmBoundaryLayout.Compute (the
// Farm's folder) is the pure layout — rim, neighbour rims, road edges, with
// openings where roads cross and one facing the Farm's door. This file only
// feeds it the world (the yield's blocked test, FarmRing.Field, and the road
// MASK texture read on the CPU) and draws what it returns. Rebuilt with the
// crop's cadence and change check, plus the Farm's level and the road mask
// around the ring.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    public partial class FarmCropField
    {
        /// <summary>One Synty piece, ready to instance: its mesh, an
        /// instancing copy of each material, and how to fit it to a span.</summary>
        private sealed class Piece
        {
            public Mesh Mesh;
            public Material[] Materials;
            public bool LengthAlongX;     // the mesh's long horizontal axis
            public Bounds MeshBounds;
        }

        private Piece[] _fencePieces;
        private readonly List<FarmBoundaryLayout.Neighbour> _otherFarms = new List<FarmBoundaryLayout.Neighbour>();
        private readonly List<FarmBoundaryLayout.Segment> _segments = new List<FarmBoundaryLayout.Segment>();
        private LayoutWorld _layoutWorld;

        /// <summary>The layout's view of the world: the yield's blocked test and
        /// the road mask this presentation samples.</summary>
        private sealed class LayoutWorld : FarmBoundaryLayout.IWorld
        {
            public FarmCropField Owner;
            public bool IsBlocked(float2 p) => Owner._ring.IsBlocked(p);
            public float RoadCoverage(float2 p) => Owner.Road(p);
        }

        // ── The road mask on the CPU ───────────────────────────────────────
        // RoadNetwork keeps its mask readable (Apply(false, false)); the fence
        // reads the same texture the crop shader samples, never a copy of the rules.

        private Texture2D _roadTex;
        private NativeArray<Color32> _roadPx;
        private Vector4 _maskST;
        private int _roadW, _roadH;
        private bool _roadOk;

        private void BindRoadMask()
        {
            _roadOk = false;
            if (Shader.GetGlobalFloat("_TWB_RoadEnabled") < 0.5f) return;
            _roadTex = Shader.GetGlobalTexture("_TWB_RoadMask") as Texture2D;
            if (_roadTex == null || !_roadTex.isReadable) return;
            _roadPx = _roadTex.GetPixelData<Color32>(0);
            _roadW = _roadTex.width; _roadH = _roadTex.height;
            _maskST = Shader.GetGlobalVector("_TWB_MaskST");
            _roadOk = _roadPx.IsCreated && _roadPx.Length >= _roadW * _roadH;
        }

        /// <summary>Road coverage 0..1 at world XZ (bilinear, as the shader).</summary>
        private float Road(float2 p)
        {
            if (!_roadOk) return 0f;
            float u = p.x * _maskST.x + _maskST.z, v = p.y * _maskST.y + _maskST.w;
            float x = u * _roadW - 0.5f, y = v * _roadH - 0.5f;
            int x0 = (int)math.floor(x), y0 = (int)math.floor(y);
            float fx = x - x0, fy = y - y0;
            float a = Px(x0, y0), b = Px(x0 + 1, y0), c = Px(x0, y0 + 1), d = Px(x0 + 1, y0 + 1);
            return math.lerp(math.lerp(a, b, fx), math.lerp(c, d, fx), fy);
        }

        private float Px(int x, int y)
        {
            x = math.clamp(x, 0, _roadW - 1); y = math.clamp(y, 0, _roadH - 1);
            return _roadPx[y * _roadW + x].r / 255f;
        }

        /// <summary>A coarse hash of the road mask over a ring, so a new road
        /// (or one reclaimed by the grass) re-lays the fence.</summary>
        private uint RoadHash(float2 c)
        {
            BindRoadMask();
            if (!_roadOk) return 0u;
            float r = FarmRing.OuterRadius;
            uint h = 2166136261u;
            for (float z = -r; z <= r; z += 2f)
            for (float x = -r; x <= r; x += 2f)
            {
                int q = (int)(Road(c + new float2(x, z)) * 4f);   // quartiles: wear-in steps don't churn it
                h = (h ^ (uint)q) * 16777619u;
            }
            return h;
        }

        // ── Building the boundary ──────────────────────────────────────────

        private void BuildFences(Entity farm, Field f, FarmCropFieldConfig cfg, int kind)
        {
            f.FenceBuilt = true;
            f.FenceKind = kind;
            var pieces = kind == 2 ? Pieces(ref _fencePieces, cfg.fencePieces) : null;
            if (f.FenceMatrices != null) foreach (var l in f.FenceMatrices) l.Clear();
            if (pieces == null || pieces.Length == 0) return;
            if (f.FenceMatrices == null || f.FenceMatrices.Length != pieces.Length)
            {
                f.FenceMatrices = new List<Matrix4x4>[pieces.Length];
                for (int i = 0; i < pieces.Length; i++) f.FenceMatrices[i] = new List<Matrix4x4>();
            }

            BindRoadMask();
            float2 c = f.Position.xz;
            float R = FarmRing.OuterRadius;
            float len = cfg.fencePieceLength;
            float height = cfg.fenceHeight;
            var rng = new Unity.Mathematics.Random(math.max(1u, math.hash(math.round(c * 8f)) ^ 0x9E3779B9u));

            // The fields of the same owner or an ally merge; a hostile Farm's is
            // fenced off along the split (FarmBoundaryLayout, NEIGHBOUR LINE).
            var mine = EntityManager.HasComponent<FactionTag>(farm)
                ? EntityManager.GetComponentData<FactionTag>(farm).Value : default;
            _otherFarms.Clear();
            for (int i = 0; i < _obstacles.Count; i++)
            {
                var o = _obstacles[i];
                if (!o.IsFarm || o.Entity == farm) continue;
                if (math.distancesq(o.Centre, c) < 4f * R * R)
                    _otherFarms.Add(new FarmBoundaryLayout.Neighbour
                    {
                        Centre = o.Centre,
                        Fence = Alliances.AreHostile(mine, o.Owner),
                    });
            }

            _layoutWorld ??= new LayoutWorld { Owner = this };
            var settings = new FarmBoundaryLayout.Settings
            {
                PieceLength = len,
                Inset = cfg.fenceInset,
                CropProbe = cfg.fenceCropProbe,
                RoadEdgeCoverage = _roadOk ? cfg.roadFenceCoverage : 2f,   // no mask: no road edges
                RoadBlockCoverage = _roadOk ? cfg.roadBlockCoverage : 2f,
                GridMeters = cfg.fenceGridMeters,
                DoorOpeningMeters = cfg.doorOpeningMeters,
            };
            _segments.Clear();
            FarmBoundaryLayout.Compute(c, R, FarmRing.FootprintHalfMeters, f.DoorDirection, _otherFarms,
                _layoutWorld, settings, _segments);
            for (int i = 0; i < _segments.Count; i++)
            {
                var sg = _segments[i];
                if (sg.Opening) continue;
                int v = rng.NextInt(pieces.Length);
                f.FenceMatrices[v].Add(PieceMatrix(pieces[v], sg.A, sg.B, height, ref rng));
            }
        }

        /// <summary>One piece fitted to the span A-B on the terrain: its long axis
        /// along the span (pitched with the slope), scaled to the span's length
        /// and the configured height, its base a few centimetres into the ground.</summary>
        private Matrix4x4 PieceMatrix(Piece p, float2 a, float2 b, float height, ref Unity.Mathematics.Random rng)
        {
            var a3 = new Vector3(a.x, TerrainUtility.GetHeight(a.x, a.y), a.y);
            var b3 = new Vector3(b.x, TerrainUtility.GetHeight(b.x, b.y), b.y);
            var dir = b3 - a3;
            float span = dir.magnitude;
            var rot = Quaternion.LookRotation(dir / math.max(span, 1e-4f), Vector3.up);
            if (p.LengthAlongX) rot *= Quaternion.Euler(0f, -90f, 0f);   // local X -> forward
            var bs = p.MeshBounds;
            float meshLen = p.LengthAlongX ? bs.size.x : bs.size.z;
            float sh = height / math.max(bs.size.y, 1e-3f);
            float sl = span / math.max(meshLen, 1e-3f) * rng.NextFloat(1.0f, 1.06f);   // a touch of overlap
            var scale = p.LengthAlongX ? new Vector3(sl, sh, sh) : new Vector3(sh, sh, sl);
            var pivot = new Vector3(bs.center.x, bs.min.y + 0.04f / math.max(sh, 1e-3f), bs.center.z);
            return Matrix4x4.TRS((a3 + b3) * 0.5f, rot, Vector3.one)
                 * Matrix4x4.Scale(scale)
                 * Matrix4x4.Translate(-pivot);
        }

        // ── Pieces and drawing ─────────────────────────────────────────────

        private Piece[] Pieces(ref Piece[] cache, GameObject[] prefabs)
        {
            if (cache != null) return cache;
            var list = new List<Piece>();
            if (prefabs != null)
                foreach (var go in prefabs)
                {
                    if (go == null) continue;
                    var mf = go.GetComponentInChildren<MeshFilter>();
                    var mr = go.GetComponentInChildren<MeshRenderer>();
                    if (mf == null || mf.sharedMesh == null || mr == null) continue;
                    var src = mr.sharedMaterials;
                    var mats = new Material[src.Length];
                    for (int i = 0; i < src.Length; i++)
                        mats[i] = src[i] == null ? null : new Material(src[i]) { name = src[i].name + " (farm fence)", enableInstancing = true };
                    var b = mf.sharedMesh.bounds;
                    list.Add(new Piece { Mesh = mf.sharedMesh, Materials = mats, MeshBounds = b, LengthAlongX = b.size.x >= b.size.z });
                }
            if (list.Count == 0) Debug.LogError("[FarmCropField] no usable fence pieces on FarmCropField.asset");
            cache = list.ToArray();
            return cache;
        }

        private void DrawFences(Field f, FarmCropFieldConfig cfg)
        {
            if (f.FenceKind == 0 || f.FenceMatrices == null) return;
            var pieces = _fencePieces;
            if (pieces == null) return;
            for (int v = 0; v < pieces.Length && v < f.FenceMatrices.Length; v++)
            {
                var mats = f.FenceMatrices[v];
                if (mats.Count == 0) continue;
                var p = pieces[v];
                for (int s = 0; s < p.Mesh.subMeshCount && s < p.Materials.Length; s++)
                {
                    if (p.Materials[s] == null) continue;
                    var rp = new RenderParams(p.Materials[s])
                    {
                        worldBounds = f.Bounds,
                        shadowCastingMode = ShadowCastingMode.On,
                        receiveShadows = true,
                    };
                    Graphics.RenderMeshInstanced(rp, p.Mesh, s, mats);
                }
            }
        }

        private void ReleaseFencePieces()
        {
            if (_fencePieces != null)
                foreach (var p in _fencePieces)
                    foreach (var m in p.Materials) if (m != null) Object.Destroy(m);
            _fencePieces = null;
        }
    }
}
