// ProceduralBorderParticleGenerator.cs
// Builds a per-node ambient ParticleSystem for the Veilstone-faction "border
// ground" — slow purple motes drifting from the node area. One particle
// system per veilstone node (NOT per tile).
//
// Iteration 2 (2026-05-21, task-border-ground-luminous-veilstone-111 item 6):
//   The velocity field is now driven by the node's spread progression:
//     - WHILE EXPANDING (CurrentRingRadius < SpreadRadius × 0.95): motes
//       flow RADIALLY OUTWARD from the node centre, evoking "tendrils
//       reaching outward to claim new ground."
//     - WHEN FULLY SPREAD: falls back to gentle upward drift like
//       Iteration 1. The emission disc shrinks to the current ring
//       radius so particles spawn at the spread frontier rather than
//       in already-stable territory.
//
//   The polling is done by an attached BorderNodeAmbientDirector
//   MonoBehaviour that reads BorderSpreadState via the EntityManager
//   every ~0.25 s and updates the particle system's modules. Cheap and
//   self-cleaning — dies with the node entity.
//
// Determinism / multiplayer:
//   Particle RNG is non-deterministic across peers. Intentional — particles
//   are purely cosmetic (no gameplay, no commands, no entity allocations).
//
// Perf:
//   ~4 emissions/sec × 4 s lifetime ≈ 16 alive particles per node.
//
// Location: Assets/Scripts/Presentation/ProceduralBorderParticleGenerator.cs

using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Static factory for the per-veilstone-node ambient particle drift.
    /// Returns a tracked GameObject the caller parents to the node's root.
    /// </summary>
    public static class ProceduralBorderParticleGenerator
    {
        // ---- Tunables ----

        private const float FallbackSpreadRadius = 15f;
        private const float EmissionRate = 4f;
        private const float ParticleLifetime = 4f;
        private const float UpwardDriftSpeed = 0.4f;

        /// <summary>Radial outward speed when the node is still expanding.</summary>
        private const float OutwardFlowSpeed = 1.6f;

        private const float StartSpeed = 0.3f;
        private const float ParticleSizeMin = 0.05f;
        private const float ParticleSizeMax = 0.15f;
        private const float EmissionRadiusRatio = 0.9f;
        private const int MaxParticles = 64;

        // Palette — matches shard gradient endpoints.
        private static readonly Color MotePurple = new Color(0.608f, 0.357f, 0.878f, 1f);
        private static readonly Color MoteGreen  = new Color(0.478f, 0.784f, 0.227f, 1f);

        // ---- Shared material + texture ----

        private static Material _moteMaterial;
        private static Texture2D _moteTexture;

        // ====================================================================
        //  PUBLIC API
        // ====================================================================

        public static GameObject Create(Vector3 nodePos, Entity nodeEntity, EntityManager em)
        {
            float spreadRadius = FallbackSpreadRadius;
            if (em != default && em.Exists(nodeEntity) && em.HasComponent<BorderNode>(nodeEntity))
            {
                var cn = em.GetComponentData<BorderNode>(nodeEntity);
                if (cn.SpreadRadius > 0.01f) spreadRadius = cn.SpreadRadius;
            }
            float emissionRadius = spreadRadius * EmissionRadiusRatio;

            var go = new GameObject($"BorderParticles_{nodeEntity.Index}");
            go.transform.position = nodePos;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(ParticleLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(StartSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(ParticleSizeMin, ParticleSizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(MotePurple);
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = EmissionRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = emissionRadius;
            shape.radiusThickness = 1f;
            shape.position = new Vector3(0f, 0.1f, 0f);
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // Initial velocity field — gentle upward drift (the "idle" mode).
            // The director will swap this to outward flow while expanding.
            var velOverLife = ps.velocityOverLifetime;
            velOverLife.enabled = true;
            velOverLife.space = ParticleSystemSimulationSpace.World;
            velOverLife.x = new ParticleSystem.MinMaxCurve(0f);
            velOverLife.y = new ParticleSystem.MinMaxCurve(UpwardDriftSpeed);
            velOverLife.z = new ParticleSystem.MinMaxCurve(0f);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(MotePurple, 0f),
                    new GradientColorKey(MoteGreen,  1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f,    0f),
                    new GradientAlphaKey(0.85f, 0.25f),
                    new GradientAlphaKey(0.85f, 0.7f),
                    new GradientAlphaKey(0f,    1f),
                });
            colorOverLife.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,   0.6f),
                new Keyframe(0.4f, 1.0f),
                new Keyframe(1f,   0.7f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetMoteMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Attach the director so the velocity field tracks spread state.
            var director = go.AddComponent<BorderNodeAmbientDirector>();
            director.Init(nodeEntity, spreadRadius);

            ps.Play();
            return go;
        }

        // ====================================================================
        //  SHARED MATERIAL + TEXTURE
        // ====================================================================

        private static Material GetMoteMaterial()
        {
            if (_moteMaterial != null) return _moteMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "BorderMoteMat" };
            mat.mainTexture = GetMoteTexture();
            mat.color = MotePurple;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", MotePurple);

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3100;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 2);
            mat.EnableKeyword("_ALPHABLEND_ON");

            _moteMaterial = mat;
            return _moteMaterial;
        }

        private static Texture2D GetMoteTexture()
        {
            if (_moteTexture != null) return _moteTexture;

            const int res = 32;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                name = "BorderMoteDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float c = (res - 1) * 0.5f;
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;
                    px[y * res + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            _moteTexture = tex;
            return _moteTexture;
        }

        // ====================================================================
        //  AMBIENT DIRECTOR — polls spread state, retargets velocity field
        // ====================================================================

        /// <summary>
        /// Reads the node's <see cref="BorderSpreadState.CurrentRingRadius"/>
        /// every ~0.25 s. While the spread is still expanding, switches the
        /// owning ParticleSystem's velocityOverLifetime to RADIAL OUTWARD
        /// flow + sizes the emission disc to the current ring radius.
        /// Once spread is complete, restores gentle upward drift over the
        /// full spread radius.
        /// </summary>
        public sealed class BorderNodeAmbientDirector : MonoBehaviour
        {
            private const float PollIntervalSeconds = 0.25f;
            // While expanding, switch to outward flow when the ring is less
            // than 95% of the target SpreadRadius. Stops flickering at the
            // very end of expansion.
            private const float ExpandingThreshold = 0.95f;

            private Entity _node;
            private float _spreadRadius;
            private ParticleSystem _ps;
            private float _timer;
            private bool _lastExpanding = true; // start in "expanding" mode

            public void Init(Entity nodeEntity, float spreadRadius)
            {
                _node = nodeEntity;
                _spreadRadius = Mathf.Max(0.01f, spreadRadius);
                _ps = GetComponent<ParticleSystem>();

                ApplyExpandingField(_spreadRadius);
            }

            private void Update()
            {
                if (_ps == null) return;
                _timer -= Time.deltaTime;
                if (_timer > 0f) return;
                _timer = PollIntervalSeconds;

                // Re-resolve the world EVERY poll instead of caching an
                // EntityManager at Init: the ECS world is disposed before
                // these presentation GameObjects on match teardown / scene
                // reload, and a cached manager then throws NRE deep inside
                // Exists() (GetCheckedEntityDataAccess on a dead world).
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                var em = world.EntityManager;

                if (!em.Exists(_node)) return;
                if (!em.HasComponent<BorderSpreadState>(_node)) return;
                var st = em.GetComponentData<BorderSpreadState>(_node);

                float ring = Mathf.Max(0.5f, st.CurrentRingRadius);
                bool expanding = ring < _spreadRadius * ExpandingThreshold;

                // Always retarget while expanding — the ring radius grows
                // continuously, so the emission disc + flow direction should
                // track it. Once idle, only retarget on the transition.
                if (expanding)
                {
                    ApplyExpandingField(ring);
                }
                else if (_lastExpanding)
                {
                    ApplyIdleField(_spreadRadius);
                }
                _lastExpanding = expanding;
            }

            private void ApplyExpandingField(float frontierRadius)
            {
                // Emission disc tracks the frontier so motes spawn near the
                // newest border ground.
                var shape = _ps.shape;
                shape.radius = Mathf.Max(0.5f, frontierRadius * EmissionRadiusRatio);

                // Outward velocity in world XZ. Built-in
                // velocityOverLifetime can't take a runtime-computed radial
                // direction per particle, so we use the orbitalOffset trick:
                // set radial offset rate via velocityOverLifetime.radial (which
                // IS supported and is the per-particle outward speed). Falls
                // back to scaling startSpeed if radial isn't available.
                var velOverLife = _ps.velocityOverLifetime;
                velOverLife.enabled = true;
                velOverLife.space = ParticleSystemSimulationSpace.Local;
                velOverLife.x = new ParticleSystem.MinMaxCurve(0f);
                velOverLife.y = new ParticleSystem.MinMaxCurve(0.15f); // slight rise
                velOverLife.z = new ParticleSystem.MinMaxCurve(0f);
                velOverLife.radial = new ParticleSystem.MinMaxCurve(OutwardFlowSpeed);
            }

            private void ApplyIdleField(float fullRadius)
            {
                var shape = _ps.shape;
                shape.radius = Mathf.Max(0.5f, fullRadius * EmissionRadiusRatio);

                var velOverLife = _ps.velocityOverLifetime;
                velOverLife.enabled = true;
                velOverLife.space = ParticleSystemSimulationSpace.World;
                velOverLife.x = new ParticleSystem.MinMaxCurve(0f);
                velOverLife.y = new ParticleSystem.MinMaxCurve(UpwardDriftSpeed);
                velOverLife.z = new ParticleSystem.MinMaxCurve(0f);
                velOverLife.radial = new ParticleSystem.MinMaxCurve(0f);
            }
        }
    }
}
