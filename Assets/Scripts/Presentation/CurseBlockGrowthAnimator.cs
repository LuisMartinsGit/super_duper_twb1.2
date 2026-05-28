// CurseBlockGrowthAnimator.cs
// Per-cluster animator that ramps each child block's Y scale from 0 → its
// target height over ~1.0 s with an ease-out-back overshoot, then
// self-destructs to keep steady-state Update cost zero.
//
// Iteration 2 of task-cursed-ground-luminous-crystals-111: implements
// item 5 ("Block growth animation — 'crystal bloom'").
//
// One animator per cluster ROOT (not per block) — open-question 2 from
// the spec. Iterating the cluster's block list once per frame is far
// cheaper than 25 MonoBehaviour Update callbacks per tile.
//
// Particle burst:
//   When each block reaches ~90% of its growth progress, a single-burst
//   ParticleSystem fires at the block's top emitting 5-10 motes coloured
//   to match the block's bucket emission. The burst PS is shared across
//   the cluster — one ParticleSystem on the root, emitted via Emit() at
//   each block-top world position.
//
// Cleanup:
//   When growth completes, the animator detaches the burst PS so it can
//   finish playing its remaining particles before destroying itself, and
//   calls Destroy(this) — but NOT Destroy(gameObject), since the cluster
//   GameObject must persist for the recession animator + pulse driver.
//
// Location: Assets/Scripts/Presentation/CurseBlockGrowthAnimator.cs

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Animates the Y scale of every child block in a cluster from 0 to its
    /// per-cell target height. Lives only during the growth window
    /// (~1 s) then destroys itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CurseBlockGrowthAnimator : MonoBehaviour
    {
        private List<MeshRenderer> _blocks;
        private List<float> _targetHeights;
        private float _duration;
        private float _elapsed;
        private Color _emissionColor;
        private bool[] _burstFired;

        // Single shared burst ParticleSystem for the cluster.
        private ParticleSystem _burstPs;

        // Shared material for the burst PS — cached statically so all clusters
        // batch into the same draw call.
        private static Material _burstMaterial;

        /// <summary>
        /// Configure the animator. Caller passes the cluster's block list and
        /// matching target-height list (parallel arrays).
        /// </summary>
        public void Init(
            List<MeshRenderer> blocks,
            List<float> targetHeights,
            float duration,
            Color emissionColor)
        {
            _blocks = blocks;
            _targetHeights = targetHeights;
            _duration = math.max(0.01f, duration);
            _emissionColor = emissionColor;
            _burstFired = new bool[blocks.Count];

            // Snap every block to scale 0 immediately — caller is supposed to
            // have done this but be defensive.
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i] == null) continue;
                var t = _blocks[i].transform;
                var s = t.localScale;
                t.localScale = new Vector3(s.x, 0f, s.z);
            }

            _burstPs = BuildBurstParticleSystem(transform, emissionColor);
        }

        private void Update()
        {
            if (_blocks == null) { Destroy(this); return; }

            _elapsed += Time.deltaTime;
            float progress = math.saturate(_elapsed / _duration);

            // Ease-out-back overshoot for the "pop" feel — small overshoot at
            // ~0.85 progress, settles at 1.0.
            float eased = EaseOutBack(progress);

            for (int i = 0; i < _blocks.Count; i++)
            {
                var r = _blocks[i];
                if (r == null) continue;
                var t = r.transform;
                float target = _targetHeights[i];
                float h = target * eased;
                var s = t.localScale;
                t.localScale = new Vector3(s.x, math.max(0.0001f, h), s.z);

                // (No Y re-anchor needed — the hex-prism mesh is anchored
                // at its base, y=0..1 in local space. localPosition.y stays
                // at 0; scaling Y grows the block upward from the ground.)

                // Fire the per-block particle burst at ~90% growth, at the
                // top of the block (its current world Y = ground + h since
                // the mesh's top is at local +1 × scale.y).
                if (!_burstFired[i] && progress >= 0.9f && _burstPs != null)
                {
                    _burstFired[i] = true;
                    Vector3 topWorld = t.position + new Vector3(0f, h + 0.05f, 0f);
                    EmitBurstAt(topWorld);
                }
            }

            if (progress >= 1f)
            {
                // Detach the burst PS so it can finish playing its trail then
                // destroy itself. Parented to the cluster root so it dies
                // alongside the cluster if the cluster is destroyed first.
                if (_burstPs != null)
                {
                    var psGo = _burstPs.gameObject;
                    var mainSelf = _burstPs.main;
                    Destroy(psGo, mainSelf.startLifetime.constantMax + 0.1f);
                    _burstPs = null;
                }
                // Release per-frame work — recession animator (separate
                // component) stays around in standby mode.
                Destroy(this);
            }
        }

        // ====================================================================
        //  EASING
        // ====================================================================

        /// <summary>
        /// Classic "ease out back" — overshoots ~10% past 1 and settles.
        /// </summary>
        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        // ====================================================================
        //  BURST PARTICLE SYSTEM
        // ====================================================================

        private void EmitBurstAt(Vector3 worldPos)
        {
            if (_burstPs == null) return;
            var emitParams = new ParticleSystem.EmitParams
            {
                position = worldPos,
                applyShapeToPosition = false,
            };
            _burstPs.Emit(emitParams, UnityEngine.Random.Range(5, 11));
        }

        private static ParticleSystem BuildBurstParticleSystem(Transform parent, Color color)
        {
            var go = new GameObject("GrowthBurst");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
            // Saturate the emission color so it reads well in the bloom pass.
            Color tint = color;
            float maxC = Mathf.Max(tint.r, tint.g, tint.b, 0.01f);
            tint = new Color(tint.r / maxC, tint.g / maxC, tint.b / maxC, 1f);
            main.startColor = new ParticleSystem.MinMaxGradient(tint);
            main.maxParticles = 256;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.3f; // gentle upward bias

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // burst-only via Emit()

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var velOverLife = ps.velocityOverLifetime;
            velOverLife.enabled = true;
            velOverLife.space = ParticleSystemSimulationSpace.Local;
            velOverLife.y = new ParticleSystem.MinMaxCurve(0.5f);

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(tint, 0f),
                    new GradientColorKey(tint, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.0f, 1f),
                });
            colorOverLife.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetBurstMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        private static Material GetBurstMaterial()
        {
            if (_burstMaterial != null) return _burstMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "CurseBurstMat" };
            mat.color = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3100;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 2);
            mat.EnableKeyword("_ALPHABLEND_ON");
            _burstMaterial = mat;
            return _burstMaterial;
        }
    }
}
