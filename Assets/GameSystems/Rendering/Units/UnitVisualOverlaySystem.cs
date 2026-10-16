// UnitVisualOverlaySystem.cs
// Procedural overlays for unit feedback (spec refinement #7):
//   - Rank pips above the unit (one per UnitRank.Value, capped at 5).
//   - Shield bar under the pips while an equipment shield holds points.
//
// 2026-10-15 (3,000-unit pass): drawn with GPU INSTANCING, not GameObjects.
// Every unit used to own a wrapper GameObject plus a sphere per rank pip
// (minimum one) and a two-cube shield bar — at 3,000 units that was 3,000+
// MeshRenderers, each casting a shadow into both cascades, each moved by a
// Transform write whenever its unit walked. The same meshes, materials and
// transforms are now submitted as instanced draws each frame, culled on the
// CPU against the gameplay camera, so the cost is a matrix per VISIBLE pip
// and a handful of draw calls. Differences from the GameObject version:
//   - pips and bars no longer cast shadows (a 14 cm marker's shadow);
//   - they follow the unit view's fog visibility: an enemy hidden by fog of
//     war no longer shows its pips floating in the dark (same fix
//     UnitIndicatorSystem got for its discs).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheWaningBorder.Rendering
{
    public class UnitVisualOverlaySystem : MonoBehaviour
    {
        private const float PipBaseHeight = 2.4f;
        private const float PipSpacing = 0.18f;
        private const float PipSize = 0.14f;
        private const float ShieldBarHeight = PipBaseHeight - 0.25f;

        /// <summary>Frustum pad (m) around a unit's overlay anchor: the widest
        /// overlay is the 1 m shield bar.</summary>
        private const float CullPad = 1.5f;

        /// <summary>Instances per draw call — the classic constant-buffer
        /// ceiling for instanced matrices.</summary>
        private const int MaxInstancesPerDraw = 1023;

        private Unity.Entities.World _world;
        private EntityManager _em;
        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] UnitQueryTypes = {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>() };
        private TheWaningBorder.Core.CachedEntityQuery _unitQuery;

        private Material _pipMat;
        private Material _shieldBgMat;
        private Material _shieldFillMat;
        private Mesh _sphereMesh;
        private Mesh _cubeMesh;

        private Matrix4x4[] _pips = new Matrix4x4[1024];
        private Matrix4x4[] _bgs = new Matrix4x4[64];
        private Matrix4x4[] _fills = new Matrix4x4[64];
        private int _pipCount, _bgCount, _fillCount;

        void Awake()
        {
            _pipMat = BuildMat(new Color(1.00f, 0.85f, 0.30f, 1f), emissive: true);
            _shieldBgMat = BuildMat(new Color(0.10f, 0.20f, 0.30f, 0.85f), emissive: false);
            _shieldFillMat = BuildMat(new Color(0.45f, 0.80f, 1.00f, 0.95f), emissive: true);
            _sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);
            _cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }
            if (_pipMat == null || _sphereMesh == null || _cubeMesh == null) return;

            _pipCount = _bgCount = _fillCount = 0;
            var evm = EntityViewManager.Instance;

            var unitQuery = _unitQuery.Get(_em, UnitQueryTypes);
            unitQuery.CompleteDependency();
            var entityHandle = _em.GetEntityTypeHandle();
            var ltHandle = _em.GetComponentTypeHandle<LocalTransform>(true);
            var rankHandle = _em.GetComponentTypeHandle<UnitRank>(true);
            var shieldHandle = _em.GetComponentTypeHandle<ShieldBar>(true);

            using var chunks = unitQuery.ToArchetypeChunkArray(Allocator.Temp);
            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var entities = chunk.GetNativeArray(entityHandle);
                var transforms = chunk.GetNativeArray(ref ltHandle);
                bool hasRank = chunk.Has(ref rankHandle);
                bool hasShield = chunk.Has(ref shieldHandle);
                var ranks = hasRank ? chunk.GetNativeArray(ref rankHandle) : default;
                var shields = hasShield ? chunk.GetNativeArray(ref shieldHandle) : default;

                for (int i = 0; i < entities.Length; i++)
                {
                    float3 p = transforms[i].Position;
                    var anchor = new Vector3(p.x, p.y + PipBaseHeight, p.z);

                    // Cheapest test first: pure math on the frame's planes.
                    if (!ProceduralRigCulling.Visible(anchor, CullPad)) continue;

                    // FOG GATE: follow the unit view's own visibility (the
                    // fog system deactivates hidden views). No view yet = not
                    // drawn, as the unit itself is not drawn either.
                    if (evm != null)
                    {
                        if (!evm.TryGetView(entities[i], out var view) || view == null
                            || !view.activeInHierarchy)
                            continue;
                    }

                    // ── Rank pips ──
                    int rank = hasRank ? math.clamp(ranks[i].Value, 1, 5) : 1;
                    float row = -(rank - 1) * PipSpacing * 0.5f;
                    for (int k = 0; k < rank; k++)
                        Push(ref _pips, ref _pipCount,
                            new Vector3(p.x + row + k * PipSpacing, anchor.y, p.z),
                            new Vector3(PipSize, PipSize, PipSize));

                    // ── Shield bar ── (visible only while Current > 0)
                    if (hasShield)
                    {
                        var sb = shields[i];
                        if (sb.Current > 0 && sb.Max > 0)
                        {
                            float ratio = math.clamp((float)sb.Current / sb.Max, 0f, 1f);
                            float y = p.y + ShieldBarHeight;
                            Push(ref _bgs, ref _bgCount,
                                new Vector3(p.x, y, p.z), new Vector3(1f, 0.08f, 0.1f));
                            // Pivoted to the left edge so it shrinks rightward.
                            Push(ref _fills, ref _fillCount,
                                new Vector3(p.x - 0.5f * (1f - ratio), y, p.z),
                                new Vector3(ratio, 0.088f, 0.12f));
                        }
                    }
                }
            }

            Draw(_pipMat, _sphereMesh, _pips, _pipCount);
            Draw(_shieldBgMat, _cubeMesh, _bgs, _bgCount);
            Draw(_shieldFillMat, _cubeMesh, _fills, _fillCount);
        }

        void OnDestroy()
        {
            if (_pipMat != null) Destroy(_pipMat);
            if (_shieldBgMat != null) Destroy(_shieldBgMat);
            if (_shieldFillMat != null) Destroy(_shieldFillMat);
        }

        private static void Push(ref Matrix4x4[] buf, ref int count, Vector3 pos, Vector3 scale)
        {
            if (count == buf.Length) System.Array.Resize(ref buf, buf.Length * 2);
            // Axis-aligned TRS without the quaternion maths.
            var m = new Matrix4x4();
            m.m00 = scale.x; m.m11 = scale.y; m.m22 = scale.z; m.m33 = 1f;
            m.m03 = pos.x; m.m13 = pos.y; m.m23 = pos.z;
            buf[count++] = m;
        }

        private static readonly Bounds HugeBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

        private static void Draw(Material mat, Mesh mesh, Matrix4x4[] buf, int count)
        {
            if (count == 0 || mat == null) return;
            // A headless player (no graphics device) has no instancing and
            // throws on every call; there is nothing to draw there anyway.
            if (!SystemInfo.supportsInstancing) return;
            var rp = new RenderParams(mat)
            {
                // Culled per instance above; the batch bounds only need to
                // keep Unity from culling the whole batch.
                worldBounds = HugeBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                layer = 0,
            };
            for (int start = 0; start < count; start += MaxInstancesPerDraw)
                Graphics.RenderMeshInstanced(rp, mesh, 0, buf,
                    Mathf.Min(MaxInstancesPerDraw, count - start), start);
        }

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return mesh;
        }

        private static UnitVisualOverlaySystemConfig _cfg;
        private static UnitVisualOverlaySystemConfig Cfg
            => _cfg != null ? _cfg
             : (_cfg = TheWaningBorder.Core.Settings.ComponentConfig.Require<UnitVisualOverlaySystemConfig>());

        private static Material BuildMat(Color color, bool emissive)
        {
            // Copy of the shipped instanced base (see the config for why it
            // must be an asset). A missing config is logged by Require; the
            // Shader.Find path then still works in the Editor.
            var baseMat = Cfg != null ? Cfg.instancedLitBase : null;
            Material mat;
            if (baseMat != null)
                mat = new Material(baseMat);
            else
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                   ?? Shader.Find("Standard")
                                   ?? Shader.Find("Sprites/Default"));
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (emissive && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2.5f);
            // Required by Graphics.RenderMeshInstanced.
            mat.enableInstancing = true;
            return mat;
        }
    }
}
