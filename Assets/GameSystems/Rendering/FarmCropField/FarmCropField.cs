// FarmCropField.cs
// The cereal growing on every Alanthor Farm's crop ring, and the tilled soil
// under it (docs/Design/Age_1_Alanthor.md § The Farm, "The field's look").
//
//   WHERE  FarmRing is the one definition of the ring. A tuft stands only on
//          FREE ring ground — not under a building, not on impassable ground,
//          not on the half of an overlap that is the nearer Farm's (two
//          rings are split straight down the middle, so the crop runs right
//          up to the bisector from both sides — no bare lens). Roads
//          are not obstacles for the yield, but the crop shader cuts every
//          stalk standing on a road (the road mask), so roads stay visible.
//   WHEN   A Farm's field starts as bare tilled soil the moment it is
//          finished and grows to ripe over growSeconds, faster with Workers
//          inside. A Farm first seen already finished (a loaded save, a
//          hostile Farm met later) shows ripe. A hostile Farm out of vision
//          keeps the field it had when last seen (Last_Seen_Buildings.md).
//   WHO    Only Farms whose view is showing for the local player.
//
// Presentation only: reads ECS state, never writes it. The per-Farm instance
// buffer and soil mesh are rebuilt only when the Farm appears or the things
// blocking its ring change (checked every rebuildInterval), never per frame;
// the per-frame work is one RenderMeshPrimitives (+ one soil RenderMesh) per
// visible Farm, culled by its bounds and thinned with camera distance.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class FarmCropField : SystemBase
    {
        private FarmCropFieldConfig _cfg;
        private FarmCropFieldConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<FarmCropFieldConfig>());

        /// <summary>Editor probes: when set (0..1), every field shows this growth.</summary>
        public static float? DebugGrowthOverride;
        /// <summary>Editor probes: when set, every field shows this Farm level's boundary.</summary>
        public static byte? DebugLevelOverride;

        private sealed class Field
        {
            public float3 Position;
            public bool Finished;
            public float Growth;          // the field's own growth, 0..1
            public float ShownGrowth;     // what the player sees (frozen out of vision)
            public uint Signature;
            public bool Built;
            public GraphicsBuffer Instances;
            public int Count;
            public Mesh Soil;
            public MaterialPropertyBlock CropProps;
            public MaterialPropertyBlock SoilProps;
            public Bounds Bounds;
            public bool Alive;
            // The boundary (FarmCropField.Fences.cs): the fence at L2 (L3's wall is a sim object).
            public byte Level;            // the owner's Farm level (FactionBuildingLevels)
            public byte ShownLevel;       // what the player sees (frozen out of vision)
            public uint FenceSignature;
            public bool FenceBuilt;
            public int FenceKind;         // 0 none, 2 fence
            public List<Matrix4x4>[] FenceMatrices;
            public float2 DoorDirection;  // world XZ the Farm's door faces
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CropInstance
        {
            public float4 PosHeight;   // root xyz, stalk height (m)
            public float4 Extra;       // yaw, seed, edge thinning, unused
        }

        /// <summary>A building that may reach some ring: what the change check hashes.</summary>
        private struct Obstacle
        {
            public Entity Entity;
            public float2 Centre;
            public float Reach;        // how far from its centre it can block
            public bool IsFarm;
            public Faction Owner;         // a Farm's owner: the fence runs only against a hostile one
        }

        private readonly Dictionary<Entity, Field> _fields = new Dictionary<Entity, Field>();
        private readonly List<Entity> _drop = new List<Entity>();
        private readonly List<Obstacle> _obstacles = new List<Obstacle>();
        // The yield's own blocked test (FarmRing.Field), so crop and pay never disagree.
        private readonly FarmRing.Field _ring = new FarmRing.Field();
        private readonly List<CropInstance> _scratch = new List<CropInstance>();
        private Mesh _tuft;
        private Material _cropMat, _soilMat;
        private float _sinceCheck;
        private bool _disabled;

        private static readonly int ID_Instances = Shader.PropertyToID("_CropInstances");
        private static readonly int ID_Growth = Shader.PropertyToID("_Growth");
        private static readonly int ID_FarmCentre = Shader.PropertyToID("_FarmCentre");
        private static readonly int ID_Tilled = Shader.PropertyToID("_Tilled");

        protected override void OnCreate()
        {
            RequireForUpdate<FarmTag>();
            // A headless run (harness, dedicated replay check) draws nothing.
            _disabled = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        }

        protected override void OnDestroy()
        {
            foreach (var f in _fields.Values) Release(f);
            _fields.Clear();
            if (_tuft != null) Object.Destroy(_tuft);
            if (_cropMat != null) Object.Destroy(_cropMat);
            if (_soilMat != null) Object.Destroy(_soilMat);
            ReleaseFencePieces();
        }

        protected override void OnUpdate()
        {
            if (_disabled) return;
            var cfg = Cfg;
            if (cfg == null || cfg.cropShader == null) return;
            if (!EnsureResources(cfg)) return;
            PushMaterialSettings(cfg);

            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            var views = EntityViewManager.Instance;

            // 1. Track every Farm: construction state and growth.
            foreach (var f in _fields.Values) f.Alive = false;
            foreach (var (xf, fac, e) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<FarmTag>().WithNone<PlannedBuilding>().WithEntityAccess())
            {
                bool finished = !em.HasComponent<UnderConstruction>(e);
                if (!_fields.TryGetValue(e, out var f))
                {
                    f = new Field
                    {
                        Position = xf.ValueRO.Position,
                        Finished = finished,
                        // Met already finished: it has been growing out of our sight.
                        Growth = finished ? 1f : 0f,
                        CropProps = new MaterialPropertyBlock(),
                        SoilProps = new MaterialPropertyBlock(),
                    };
                    f.ShownGrowth = f.Growth;
                    _fields[e] = f;
                }
                f.Alive = true;
                f.Position = xf.ValueRO.Position;
                f.DoorDirection = FarmStoneWall.DoorDirection(xf.ValueRO.Rotation);
                if (finished && !f.Finished) { f.Finished = true; f.Growth = 0f; }
                if (!f.Finished) continue;

                if (f.Growth < 1f && dt > 0f)
                {
                    int slots = em.HasBuffer<WallGarrisonSlot>(e) ? em.GetBuffer<WallGarrisonSlot>(e).Length : 0;
                    float share = slots > 0 ? WallGarrison.OccupantCount(em, e) / (float)slots : 0f;
                    float rate = (1f + cfg.workerGrowthBonus * share) / math.max(1f, cfg.growSeconds);
                    f.Growth = math.min(1f, f.Growth + rate * dt);
                }
                f.Level = TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, fac.ValueRO.Value, Farm.BuildingId);
                if (!LastSeenBuildings.IsFrozen(e)) { f.ShownGrowth = f.Growth; f.ShownLevel = f.Level; }
            }

            _drop.Clear();
            foreach (var kv in _fields) if (!kv.Value.Alive) _drop.Add(kv.Key);
            for (int i = 0; i < _drop.Count; i++) { Release(_fields[_drop[i]]); _fields.Remove(_drop[i]); }
            if (_fields.Count == 0) return;

            // 2. On a cadence, rebuild any field whose blocked set changed.
            _sinceCheck += UnityEngine.Time.unscaledDeltaTime;
            bool anyUnbuilt = false;
            foreach (var f in _fields.Values) if (f.Finished && !f.Built) { anyUnbuilt = true; break; }
            if (anyUnbuilt || _sinceCheck >= cfg.rebuildInterval)
            {
                _sinceCheck = 0f;
                RefreshFields(em, cfg);
            }

            // 3. Draw the fields whose Farm the player can see.
            var cam = UnityEngine.Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var kv in _fields)
            {
                var f = kv.Value;
                if (!f.Finished || !f.Built || f.Count == 0) continue;
                if (views == null || !views.TryGetView(kv.Key, out var view) || view == null
                    || !view.activeInHierarchy) continue;

                float growth = DebugGrowthOverride ?? f.ShownGrowth;
                float dist = cam != null ? Vector3.Distance(camPos, f.Bounds.center) : 0f;
                float lod = math.lerp(1f, cfg.lodFarFraction,
                    math.saturate((dist - cfg.lodNearDistance) / math.max(1f, cfg.lodFarDistance - cfg.lodNearDistance)));
                int count = math.clamp((int)(f.Count * lod), 1, f.Count);

                f.CropProps.SetBuffer(ID_Instances, f.Instances);
                f.CropProps.SetFloat(ID_Growth, growth);
                var rp = new RenderParams(_cropMat)
                {
                    worldBounds = f.Bounds,
                    matProps = f.CropProps,
                    shadowCastingMode = cfg.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                Graphics.RenderMeshPrimitives(rp, _tuft, 0, count);

                if (cfg.soilEnabled && f.Soil != null && _soilMat != null)
                {
                    f.SoilProps.SetVector(ID_FarmCentre, new Vector4(f.Position.x, f.Position.y, f.Position.z, 0f));
                    f.SoilProps.SetFloat(ID_Tilled, 1f - 0.6f * math.saturate(growth * 1.5f));
                    var sp = new RenderParams(_soilMat)
                    {
                        worldBounds = f.Bounds,
                        matProps = f.SoilProps,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                    };
                    Graphics.RenderMesh(sp, f.Soil, 0, Matrix4x4.identity);
                }

                DrawFences(f, cfg);
            }
        }

        // ── Building the fields ────────────────────────────────────────────

        private static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<BuildingSize>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery QC_Buildings;

        private void RefreshFields(EntityManager em, FarmCropFieldConfig cfg)
        {
            // Every footprint once, then per Farm only the ones that reach its ring.
            _obstacles.Clear();
            var q = QC_Buildings.Get(em, QT_Buildings);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var sizes = q.ToComponentDataArray<BuildingSize>(Allocator.Temp))
            {
                float outerR = FarmRing.OuterRadius;
                for (int i = 0; i < ents.Length; i++)
                {
                    float half = math.length(new float2(sizes[i].Width, sizes[i].Height)) * 0.5f;
                    _obstacles.Add(new Obstacle
                    {
                        Entity = ents[i],
                        Centre = xfs[i].Position.xz,
                        // A Farm blocks with its whole ring; walls are long, be generous.
                        Reach = math.max(em.HasComponent<FarmTag>(ents[i]) ? outerR : 0f, half) + 2f,
                        IsFarm = em.HasComponent<FarmTag>(ents[i]),
                        Owner = em.HasComponent<FactionTag>(ents[i]) ? em.GetComponentData<FactionTag>(ents[i]).Value : default,
                    });
                }
            }

            var grid = PassabilityGrid.Instance;
            int gridGen = grid != null ? grid.MaskGeneration : 0;
            float outer = FarmRing.OuterRadius;

            foreach (var kv in _fields)
            {
                var f = kv.Value;
                if (!f.Finished) continue;
                float2 c = f.Position.xz;

                // Signature of everything that can reach this ring: a building
                // placed, finished into the ring or destroyed changes it.
                uint sig = 2166136261u ^ (uint)gridGen;
                for (int i = 0; i < _obstacles.Count; i++)
                {
                    var o = _obstacles[i];
                    if (o.Entity == kv.Key) continue;
                    float reach = outer + o.Reach;
                    if (math.distancesq(o.Centre, c) > reach * reach) continue;
                    sig = (sig ^ (uint)o.Entity.Index) * 16777619u;
                    sig = (sig ^ (uint)o.Entity.Version) * 16777619u;
                    sig = (sig ^ math.hash(math.round(o.Centre * 4f))) * 16777619u;
                }
                sig = (sig ^ math.hash(math.round(c * 4f))) * 16777619u;

                bool cropStale = !f.Built || sig != f.Signature;
                byte level = DebugLevelOverride ?? f.ShownLevel;
                // Level 3's low stone wall is a simulation object with views of
                // its own (FarmStoneWall, PresentationSpawnSystem.FarmStoneWall.cs):
                // only the level-2 fence is drawn here.
                int kind = level == 2 ? 2 : 0;
                uint fenceSig = kind == 0 ? 0u : (sig ^ (uint)kind * 2654435761u) * 16777619u ^ RoadHash(c);
                bool fenceStale = !f.FenceBuilt || fenceSig != f.FenceSignature;
                if (!cropStale && !fenceStale) continue;

                _ring.Gather(em, c, kv.Key, FarmRing.Ghost.None);
                if (cropStale)
                {
                    f.Signature = sig;
                    Build(f, cfg);
                }
                if (fenceStale)
                {
                    f.FenceSignature = fenceSig;
                    BuildFences(kv.Key, f, cfg, kind);
                }
            }
        }

        private void Build(Field f, FarmCropFieldConfig cfg)
        {
            float2 c = f.Position.xz;
            float outer = FarmRing.OuterRadius;
            float s = math.max(0.15f, cfg.tuftSpacing);
            int n = (int)math.ceil(outer / s);
            // Seeded by where the Farm stands, so a rebuild never reshuffles the field.
            var rng = new Unity.Mathematics.Random(math.max(1u, math.hash(math.round(c * 8f))));

            _scratch.Clear();
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int gz = -n; gz <= n; gz++)
            for (int gx = -n; gx <= n; gx++)
            {
                float2 jitter = (rng.NextFloat2() - 0.5f) * 2f * cfg.tuftJitter * s;
                float2 p = c + new float2(gx, gz) * s + jitter;
                float r1 = rng.NextFloat(), r2 = rng.NextFloat(), r3 = rng.NextFloat();
                if (!FarmRing.InRing(c, p)) continue;
                if (_ring.IsBlocked(p)) continue;

                float edge = math.saturate((math.length(p - c) - (outer - cfg.edgeFadeMeters)) / math.max(0.01f, cfg.edgeFadeMeters));
                if (r3 < edge * 0.6f) continue;   // the rim thins out
                float y = TerrainUtility.GetHeight(p.x, p.y);
                minY = math.min(minY, y); maxY = math.max(maxY, y);
                _scratch.Add(new CropInstance
                {
                    PosHeight = new float4(p.x, y, p.y, math.lerp(cfg.stalkHeightMin, cfg.stalkHeightMax, r1)),
                    Extra = new float4(r2 * math.PI * 2f, rng.NextFloat(), edge * 0.35f, 0f),
                });
            }

            // Shuffle, so drawing the first N thins the field evenly (distance LOD).
            for (int i = _scratch.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (_scratch[i], _scratch[j]) = (_scratch[j], _scratch[i]);
            }

            if (f.Instances != null && f.Instances.count < math.max(1, _scratch.Count))
            {
                f.Instances.Release();
                f.Instances = null;
            }
            if (f.Instances == null)
                f.Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.max(1, _scratch.Count),
                    System.Runtime.InteropServices.Marshal.SizeOf<CropInstance>());
            if (_scratch.Count > 0) f.Instances.SetData(_scratch);
            f.Count = _scratch.Count;

            if (minY > maxY) { minY = f.Position.y; maxY = f.Position.y; }
            float top = cfg.stalkHeightMax + 0.5f;
            f.Bounds = new Bounds(new Vector3(c.x, (minY + maxY + top) * 0.5f, c.y),
                new Vector3(outer * 2f + 1f, (maxY - minY) + top + 1f, outer * 2f + 1f));

            if (cfg.soilEnabled) BuildSoil(f, cfg);
            f.Built = true;
        }

        /// <summary>A terrain-following grid over the ring; vertex alpha = free ring ground.</summary>
        private void BuildSoil(Field f, FarmCropFieldConfig cfg)
        {
            float2 c = f.Position.xz;
            float outer = FarmRing.OuterRadius + 0.5f;
            float step = math.max(0.5f, cfg.soilGridMeters);
            int n = (int)math.ceil(outer / step);
            int side = n * 2 + 1;
            var verts = new Vector3[side * side];
            var cols = new Color32[side * side];
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                float2 p = c + new float2(x - n, z - n) * step;
                float y = TerrainUtility.GetHeight(p.x, p.y) + cfg.soilLift;
                int i = z * side + x;
                verts[i] = new Vector3(p.x, y, p.y);
                // Soft rim: full inside the ring, fading over the last metre.
                float rr = math.length(p - c);
                float a = 1f - math.saturate((rr - (FarmRing.OuterRadius - 1f)) / 1.2f);
                if (!FarmRing.InRing(c, p) && rr < FarmRing.OuterRadius) a = 0f;   // the footprint
                if (a > 0f && _ring.IsBlocked(p)) a = 0f;
                cols[i] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            var tris = new List<int>((side - 1) * (side - 1) * 6);
            for (int z = 0; z < side - 1; z++)
            for (int x = 0; x < side - 1; x++)
            {
                int i0 = z * side + x, i1 = i0 + 1, i2 = i0 + side, i3 = i2 + 1;
                if (cols[i0].a == 0 && cols[i1].a == 0 && cols[i2].a == 0 && cols[i3].a == 0) continue;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
            if (f.Soil == null) f.Soil = new Mesh { name = "FarmSoil" };
            f.Soil.Clear();
            f.Soil.indexFormat = IndexFormat.UInt32;
            f.Soil.vertices = verts;
            f.Soil.colors32 = cols;
            f.Soil.SetTriangles(tris, 0);
            f.Soil.RecalculateNormals();
            f.Soil.RecalculateBounds();
        }

        private static void Release(Field f)
        {
            f.Instances?.Release();
            f.Instances = null;
            if (f.Soil != null) Object.Destroy(f.Soil);
            f.Soil = null;
        }

        // ── Materials and the tuft mesh ────────────────────────────────────

        private bool EnsureResources(FarmCropFieldConfig cfg)
        {
            if (_cropMat == null) _cropMat = new Material(cfg.cropShader) { name = "FarmCrop (runtime)", enableInstancing = false };
            if (_soilMat == null && cfg.soilShader != null) _soilMat = new Material(cfg.soilShader) { name = "FarmSoil (runtime)" };
            if (_tuft == null) _tuft = BuildTuftMesh(cfg);
            return _cropMat != null && _tuft != null;
        }

        /// <summary>The config is read every frame so it can be tuned live in Play mode.</summary>
        private void PushMaterialSettings(FarmCropFieldConfig cfg)
        {
            var m = _cropMat;
            m.SetColor("_ColorBase", cfg.colorBase);
            m.SetColor("_ColorYoung", cfg.colorYoung);
            m.SetColor("_ColorStalkRipe", cfg.colorStalkRipe);
            m.SetColor("_ColorEarYoung", cfg.colorEarYoung);
            m.SetColor("_ColorEarRipe", cfg.colorEarRipe);
            m.SetFloat("_ColorVariation", cfg.colorVariation);
            m.SetFloat("_SproutHeight", cfg.sproutHeight);
            m.SetFloat("_EarAppear", cfg.earAppearAt);
            m.SetFloat("_RipenStart", cfg.ripenStart);
            m.SetFloat("_Translucency", cfg.translucency);
            m.SetFloat("_Smoothness", cfg.smoothness);
            m.SetFloat("_FootOcclusion", cfg.footOcclusion);
            m.SetFloat("_NormalUp", cfg.normalUpBias);
            m.SetFloat("_WindStrength", cfg.windStrength);
            m.SetFloat("_WindSpeed", cfg.windSpeed);
            m.SetFloat("_WindScale", cfg.windScale);
            m.SetFloat("_GustStrength", cfg.gustStrength);
            m.SetVector("_WindDir", cfg.windDirection);
            m.SetFloat("_RoadCutLow", cfg.roadCutLow);
            m.SetFloat("_RoadCutHigh", cfg.roadCutHigh);
            if (_soilMat != null)
            {
                _soilMat.SetColor("_SoilColor", cfg.soilColor);
                _soilMat.SetColor("_FurrowColor", cfg.soilFurrowColor);
                _soilMat.SetFloat("_FurrowSpacing", cfg.soilFurrowSpacing);
                _soilMat.SetFloat("_Opacity", cfg.soilOpacity);
                _soilMat.SetFloat("_RoadCutLow", cfg.roadCutLow);
                _soilMat.SetFloat("_RoadCutHigh", cfg.roadCutHigh);
            }
        }

        /// <summary>
        /// One tuft at height 1: stalks as tapering ribbons with a spindle ear
        /// on top, and leaf blades arching out at the foot. uv0.x is the height
        /// along the tuft (what the wind bends and the colour ramps with), uv0.y
        /// the part (0 stalk, 1 ear, 2 leaf). Ear vertices all sit at the ear's
        /// base and carry their offset in uv1, so the shader can grow the ear
        /// in absolute metres while the stalk scales with the crop's height.
        /// </summary>
        private static Mesh BuildTuftMesh(FarmCropFieldConfig cfg)
        {
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv0 = new List<Vector4>();
            var uv1 = new List<Vector3>();
            var tri = new List<int>();
            var rng = new Unity.Mathematics.Random(0x5EED1234u);
            const int Segs = 3;

            for (int sIdx = 0; sIdx < cfg.stalksPerTuft; sIdx++)
            {
                float2 o = RandomInDisc(ref rng) * cfg.tuftRadius;
                float hs = rng.NextFloat(0.82f, 1f);
                float2 lean = RandomInDisc(ref rng) * 0.1f;
                float a = rng.NextFloat(0f, math.PI);
                var side = new Vector3(math.cos(a), 0f, math.sin(a));
                var face = new Vector3(-side.z, 0f, side.x);

                int b = pos.Count;
                for (int k = 0; k <= Segs; k++)
                {
                    float tk = k / (float)Segs;
                    var centre = new Vector3(o.x + lean.x * tk * tk, hs * tk, o.y + lean.y * tk * tk);
                    float w = math.lerp(cfg.stalkWidthBase, cfg.stalkWidthTop, tk) * 0.5f;
                    pos.Add(centre - side * w); pos.Add(centre + side * w);
                    nrm.Add(face); nrm.Add(face);
                    uv0.Add(new Vector4(hs * tk, 0f, 0f, 0f)); uv0.Add(new Vector4(hs * tk, 0f, 0f, 0f));
                    uv1.Add(Vector3.zero); uv1.Add(Vector3.zero);
                }
                for (int k = 0; k < Segs; k++)
                {
                    int i0 = b + k * 2;
                    tri.Add(i0); tri.Add(i0 + 2); tri.Add(i0 + 1);
                    tri.Add(i0 + 1); tri.Add(i0 + 2); tri.Add(i0 + 3);
                }

                // The ear: a four-sided spindle along the stalk's lean, drooping a little.
                var earBase = new Vector3(o.x + lean.x, hs, o.y + lean.y);
                var axis = new Vector3(lean.x * 2.5f, 1f, lean.y * 2.5f).normalized;
                var perpA = Vector3.Cross(axis, Vector3.forward).normalized;
                if (perpA.sqrMagnitude < 0.01f) perpA = Vector3.right;
                var perpB = Vector3.Cross(axis, perpA).normalized;
                int e = pos.Count;
                AddEar(pos, nrm, uv0, uv1, earBase, Vector3.zero, -axis, hs);
                for (int k = 0; k < 4; k++)
                {
                    float ang = k * math.PI * 0.5f + 0.4f;
                    var radial = perpA * math.cos(ang) + perpB * math.sin(ang);
                    AddEar(pos, nrm, uv0, uv1, earBase,
                        axis * (cfg.earLength * 0.45f) + radial * cfg.earRadius, radial, hs);
                }
                AddEar(pos, nrm, uv0, uv1, earBase, axis * cfg.earLength, axis, hs);
                for (int k = 0; k < 4; k++)
                {
                    int m0 = e + 1 + k, m1 = e + 1 + (k + 1) % 4;
                    tri.Add(e); tri.Add(m1); tri.Add(m0);
                    tri.Add(e + 5); tri.Add(m0); tri.Add(m1);
                }
            }

            for (int l = 0; l < cfg.leavesPerTuft; l++)
            {
                float a = rng.NextFloat(0f, math.PI * 2f);
                var dir = new Vector3(math.cos(a), 0f, math.sin(a));
                var side = new Vector3(-dir.z, 0f, dir.x);
                float len = cfg.leafLength * rng.NextFloat(0.75f, 1.1f);
                int b = pos.Count;
                for (int k = 0; k <= Segs; k++)
                {
                    float tk = k / (float)Segs;
                    // Rises, then arches outward and over.
                    var centre = dir * (len * 0.55f * tk) + Vector3.up * (len * 0.75f * math.sin(tk * 1.9f));
                    float w = cfg.leafWidth * 0.5f * (1f - tk * 0.85f);
                    pos.Add(centre - side * w); pos.Add(centre + side * w);
                    var n = Vector3.Cross(side, (dir * 0.55f + Vector3.up * 0.75f * math.cos(tk * 1.9f) * 1.9f).normalized).normalized;
                    nrm.Add(n); nrm.Add(n);
                    float t = math.saturate(centre.y);
                    uv0.Add(new Vector4(t, 2f, 0f, 0f)); uv0.Add(new Vector4(t, 2f, 0f, 0f));
                    uv1.Add(Vector3.zero); uv1.Add(Vector3.zero);
                }
                for (int k = 0; k < Segs; k++)
                {
                    int i0 = b + k * 2;
                    tri.Add(i0); tri.Add(i0 + 2); tri.Add(i0 + 1);
                    tri.Add(i0 + 1); tri.Add(i0 + 2); tri.Add(i0 + 3);
                }
            }

            var mesh = new Mesh { name = "FarmCropTuft" };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tri, 0);
            // Instances are placed by the shader; the draw call's worldBounds culls.
            mesh.bounds = new Bounds(Vector3.up * 0.6f, new Vector3(2f, 2f, 2f));
            return mesh;
        }

        private static void AddEar(List<Vector3> pos, List<Vector3> nrm, List<Vector4> uv0, List<Vector3> uv1,
            Vector3 basePos, Vector3 offset, Vector3 normal, float hs)
        {
            pos.Add(basePos);
            nrm.Add(normal.normalized);
            uv0.Add(new Vector4(hs, 1f, 0f, 0f));
            uv1.Add(offset);
        }

        private static float2 RandomInDisc(ref Unity.Mathematics.Random rng)
        {
            float a = rng.NextFloat(0f, math.PI * 2f);
            return new float2(math.cos(a), math.sin(a)) * math.sqrt(rng.NextFloat());
        }
    }
}
