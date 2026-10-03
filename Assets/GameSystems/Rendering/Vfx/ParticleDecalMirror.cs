// ParticleDecalMirror.cs
// Draws a FLAT, ground-level particle subsystem (the rings, glows and shadows
// under the Lana Studio auras) as URP decal projections instead of quads.
// A quad lying at ground height z-fights the terrain and sinks into slopes; a
// decal is projected onto whatever ground is there.
//
// The particle system keeps simulating — emission, size, rotation and colour
// curves all still apply — with its renderer switched off; every frame each
// live particle drives one pooled DecalProjector (position, size, spin,
// colour as tint, alpha as fade). Added to the flat subsystems of the
// imported effect prefabs by PackVfxImport, which finds them by simulating
// each effect. If the active renderer has no decal feature, the quad is drawn
// as before, lifted clear of the ground. Presentation only.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class ParticleDecalMirror : MonoBehaviour
    {
        private static ParticleDecalMirrorConfig _cfg;
        private static ParticleDecalMirrorConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ParticleDecalMirrorConfig>());

        private static readonly int BaseMapId = Shader.PropertyToID("Base_Map");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");

        private ParticleSystem _ps;
        private ParticleSystem.Particle[] _buf;
        private readonly List<DecalProjector> _pool = new();
        private readonly List<Material> _mats = new();
        private Texture _texture;
        private bool _mirroring;

        void Start() => Begin();

        void LateUpdate() => Sync();

        /// <summary>Edit-mode preview (CastleArtPreview): set up and draw the
        /// current particles once, without entering Play mode.</summary>
        public void PreviewNow()
        {
            if (!_mirroring) Begin();
            Sync();
        }

        private void Begin()
        {
            _ps = GetComponent<ParticleSystem>();
            var cfg = Cfg;
            var psr = GetComponent<ParticleSystemRenderer>();
            if (cfg == null || psr == null) { enabled = false; return; }

            if (cfg.decalMaterial == null || !DecalsSupported())
            {
                // No decals here: keep the quad, lifted off the ground.
                transform.position += Vector3.up * cfg.fallbackLift;
                enabled = false;
                return;
            }

            _texture = TextureOf(_ps, psr);
            _buf = new ParticleSystem.Particle[Mathf.Max(1, Mathf.Min(cfg.maxParticles, _ps.main.maxParticles))];
            psr.enabled = false;
            _mirroring = true;
        }

        private void Sync()
        {
            if (!_mirroring) return;
            var cfg = Cfg;
            int n = _ps.GetParticles(_buf);
            bool world = _ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
            float scale = Mathf.Abs(transform.lossyScale.x);

            for (int i = 0; i < n; i++)
            {
                var p = _buf[i];
                var d = Projector(i, cfg);
                Vector3 pos = world ? p.position : transform.TransformPoint(p.position);
                // Hierarchy scaling: a particle draws at size × the transform's
                // scale in either simulation space.
                Vector3 size = p.GetCurrentSize3D(_ps) * scale;
                // Flat quads spin about their normal: rotation.z in local
                // alignment, and the projector's yaw on the ground.
                float spin = _ps.main.startRotation3D ? p.rotation3D.z : p.rotation;
                d.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, transform.eulerAngles.y - spin, 0f));
                d.size = new Vector3(size.x, size.y, cfg.depth);
                var c = p.GetCurrentColor(_ps);
                _mats[i].SetColor(TintId, new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f));
                d.fadeFactor = c.a / 255f;
                if (!d.enabled) d.enabled = true;
            }
            for (int i = n; i < _pool.Count; i++)
                if (_pool[i].enabled) _pool[i].enabled = false;
        }

        private DecalProjector Projector(int i, ParticleDecalMirrorConfig cfg)
        {
            while (_pool.Count <= i)
            {
                var go = new GameObject("Decal");
                go.transform.SetParent(transform, false);
                var d = go.AddComponent<DecalProjector>();
                var m = new Material(cfg.decalMaterial);
                if (_texture != null) m.SetTexture(BaseMapId, _texture);
                m.SetFloat(GlowId, cfg.glow);
                d.material = m;
                d.pivot = Vector3.zero;
                d.enabled = false;
                _pool.Add(d);
                _mats.Add(m);
            }
            return _pool[i];
        }

        /// <summary>The texture a particle draws: the texture-sheet sprite when
        /// the system uses one (Lana's do), else the material's base map.</summary>
        private static Texture TextureOf(ParticleSystem ps, ParticleSystemRenderer psr)
        {
            var tsa = ps.textureSheetAnimation;
            if (tsa.enabled && tsa.mode == ParticleSystemAnimationMode.Sprites && tsa.spriteCount > 0
                && tsa.GetSprite(0) != null)
                return tsa.GetSprite(0).texture;
            var m = psr.sharedMaterial;
            if (m == null) return null;
            if (m.HasProperty("_BaseMap")) return m.GetTexture("_BaseMap");
            return m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        }

        private static bool? _decalsSupported;
        /// <summary>True when the active URP renderer carries the Decal feature.</summary>
        private static bool DecalsSupported()
        {
            if (_decalsSupported.HasValue) return _decalsSupported.Value;
            bool ok = false;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var data = typeof(UniversalRenderPipelineAsset)
                    .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(urp) as ScriptableRendererData[];
                if (data != null)
                    foreach (var rd in data)
                        if (rd != null)
                            foreach (var f in rd.rendererFeatures)
                                if (f is DecalRendererFeature && f.isActive) ok = true;
            }
            _decalsSupported = ok;
            return ok;
        }

        void OnDestroy()
        {
            foreach (var m in _mats)
                if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
            _mats.Clear();
        }
    }
}
