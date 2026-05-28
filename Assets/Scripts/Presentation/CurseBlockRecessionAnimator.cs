// CurseBlockRecessionAnimator.cs
// Per-cluster animator that, on death-handoff from PresentationSpawnSystem,
// scales every child block's Y back to 0 over ~0.7 s and emits a small
// inward-flowing particle stream toward the owning node centre. Destroys
// the cluster GameObject when the animation completes.
//
// Iteration 2 of task-cursed-ground-luminous-crystals-111: implements
// item 7 ("Recession animation — 'reverse tendrils'") plus open
// question 4 ("Recession cleanup ordering").
//
// Death-handoff (per spec, simpler approach):
//   The component lives on the cluster root from the moment the cluster
//   spawns, in STANDBY mode (Update returns immediately while
//   _dying == false). PresentationSpawnSystem.CleanupDestroyedEntities
//   checks for this component before calling Destroy(go); if present, it
//   calls BeginDeath(entity) — passing the entity for EntityViewManager
//   detachment — and skips the immediate Destroy. The animator then
//   owns the GO's final cleanup.
//
// Cell-claim release:
//   The cluster passed its list of claimed grid cells at construction.
//   On final destroy the animator releases them back to the static
//   HashSet so newly-spawned cursed-ground tiles can re-claim them
//   (e.g. if the curse re-spreads to the same area later).
//
// Location: Assets/Scripts/Presentation/CurseBlockRecessionAnimator.cs

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Per-cluster animator. Sits dormant on the cluster root until PSS
    /// triggers death; then animates Y scale → 0, emits inward particles,
    /// and destroys the cluster.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CurseBlockRecessionAnimator : MonoBehaviour
    {
        // ---- Configuration (set by Init) ----
        private List<MeshRenderer> _blocks;
        private float _duration;
        private Vector3 _nodeCenter;
        private List<int2> _claimedCells;

        // ---- Runtime state ----
        private bool _dying;
        private float _elapsed;
        private float[] _startHeights;
        private ParticleSystem _inwardPs;

        // Shared material for the inward stream — cached.
        private static Material _streamMaterial;

        /// <summary>
        /// Called by <see cref="ProceduralCurseShardGenerator.Create"/> at
        /// spawn time. The animator stays dormant until BeginDeath() flips it.
        /// </summary>
        public void Init(
            List<MeshRenderer> blocks,
            float duration,
            Vector3 nodeCenter,
            List<int2> claimedCells)
        {
            _blocks = blocks;
            _duration = math.max(0.01f, duration);
            _nodeCenter = nodeCenter;
            _claimedCells = claimedCells;
        }

        /// <summary>
        /// Death-handoff entry point. PSS calls this when an entity's
        /// presentation should be destroyed; the animator takes ownership and
        /// PSS skips its Destroy(go) call. PSS also unregisters the view from
        /// EntityViewManager before calling this so no later sync touches the
        /// dying GO.
        /// </summary>
        public void BeginDeath()
        {
            if (_dying) return;
            _dying = true;
            _elapsed = 0f;

            if (_blocks == null) { Destroy(gameObject); return; }

            // If the growth animator is still alive (curse receded mid-growth)
            // disable it so we don't fight over transform.localScale.y.
            var growth = GetComponent<CurseBlockGrowthAnimator>();
            if (growth != null) Destroy(growth);

            // Capture the current Y scale per block as the starting point —
            // some blocks may still be mid-growth when death hits, so we
            // animate from wherever they currently are, not from the target.
            _startHeights = new float[_blocks.Count];
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i] == null) { _startHeights[i] = 0f; continue; }
                _startHeights[i] = _blocks[i].transform.localScale.y;
            }

            _inwardPs = BuildInwardParticleSystem(transform, _nodeCenter);
        }

        private void Update()
        {
            if (!_dying) return;

            _elapsed += Time.deltaTime;
            float progress = math.saturate(_elapsed / _duration);
            // Ease-in for "collapse" feel — fast at start, slow at end.
            float eased = progress * progress;

            for (int i = 0; i < _blocks.Count; i++)
            {
                var r = _blocks[i];
                if (r == null) continue;
                var t = r.transform;
                float h = math.max(0.0001f, _startHeights[i] * (1f - eased));
                var s = t.localScale;
                t.localScale = new Vector3(s.x, h, s.z);
                // (Hex prism is base-anchored — no Y re-anchor needed.)

                // Emit 2-3 inward particles from each block top, once at
                // ~10% progress (the "puff up before sinking" frame).
                if (progress > 0.05f && progress < 0.10f && _inwardPs != null && (i % 2) == 0)
                {
                    Vector3 blockTop = t.position + new Vector3(0f, h + 0.05f, 0f);
                    Vector3 toward = (_nodeCenter - blockTop);
                    toward.y = 0.1f; // slight upward arc
                    if (toward.sqrMagnitude > 0.01f) toward = toward.normalized * 1.5f;
                    EmitInwardAt(blockTop, toward);
                }
            }

            if (progress >= 1f)
            {
                ProceduralCurseShardGenerator.ReleaseClaimedCells(_claimedCells);

                if (_inwardPs != null)
                {
                    var mainSelf = _inwardPs.main;
                    var psGo = _inwardPs.gameObject;
                    psGo.transform.SetParent(null, worldPositionStays: true);
                    Destroy(psGo, mainSelf.startLifetime.constantMax + 0.1f);
                    _inwardPs = null;
                }

                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // Defensive — if the GO is destroyed by external code (e.g. a
            // scene reload) without the animation completing, still release
            // the cells.
            if (_claimedCells != null && _claimedCells.Count > 0)
            {
                ProceduralCurseShardGenerator.ReleaseClaimedCells(_claimedCells);
                _claimedCells.Clear();
            }
        }

        // ====================================================================
        //  INWARD STREAM PARTICLE SYSTEM
        // ====================================================================

        private void EmitInwardAt(Vector3 worldPos, Vector3 velocity)
        {
            if (_inwardPs == null) return;
            var emitParams = new ParticleSystem.EmitParams
            {
                position = worldPos,
                velocity = velocity,
                applyShapeToPosition = false,
            };
            _inwardPs.Emit(emitParams, UnityEngine.Random.Range(2, 4));
        }

        private static ParticleSystem BuildInwardParticleSystem(Transform parent, Vector3 nodeCenter)
        {
            var go = new GameObject("RecessionStream");
            // Parent to the cluster root — same scope as the cluster itself
            // for tidy hierarchy; we'll detach on cleanup so the trailing
            // particles can finish playing in world space.
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f); // use velocity from EmitParams
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.10f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.55f, 1f, 1f));
            main.maxParticles = 512;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            // No additional velocity-over-lifetime — we let EmitParams.velocity
            // carry the toward-node-centre direction. Gravity = 0 + drag 0 so
            // the particle keeps drifting on its initial momentum.

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.85f, 0.55f, 1f), 0f),
                    new GradientColorKey(new Color(0.5f,  0.85f, 0.4f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.0f, 0f),
                    new GradientAlphaKey(0.85f, 0.2f),
                    new GradientAlphaKey(0.0f, 1f),
                });
            colorOverLife.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetStreamMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        private static Material GetStreamMaterial()
        {
            if (_streamMaterial != null) return _streamMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "CurseRecessionStreamMat" };
            mat.color = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3100;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 2);
            mat.EnableKeyword("_ALPHABLEND_ON");
            _streamMaterial = mat;
            return _streamMaterial;
        }
    }
}
