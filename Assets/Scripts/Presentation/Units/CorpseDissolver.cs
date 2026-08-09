// CorpseDissolver.cs
// Owns a dead unit's GameObject after its ECS entity is destroyed.
// Location: Assets/Scripts/Presentation/Units/CorpseDissolver.cs

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Lets a dead unit's corpse linger and then dissolve away.
    ///
    /// Attached at spawn (dormant) by PresentationSpawnSystem. When the ECS
    /// entity is destroyed, PresentationSpawnSystem.CleanupDestroyedEntities
    /// hands the GameObject off via <see cref="BeginDeath"/> instead of
    /// destroying it (the same pattern VeilstoneOutcroppingCrystalAnimator
    /// uses). From there this component:
    ///   1. lets the (non-looping, terminal) death animation finish and freezes
    ///      on its final pose,
    ///   2. lingers <see cref="LingerSeconds"/> seconds in that pose,
    ///   3. swaps every renderer to a URP dissolve-shader instance (keeping the
    ///      original faction-recoloured albedo) and ramps _Dissolve 0→1 over
    ///      <see cref="DissolveSeconds"/>, then destroys the GameObject.
    ///
    /// Once handed off the corpse is fully decoupled from ECS — its entity is
    /// gone, so PresentationSpawnSystem.SyncTransforms no longer touches it and
    /// it stays put where it died.
    /// </summary>
    public sealed class CorpseDissolver : MonoBehaviour
    {
        [Tooltip("Seconds the corpse holds its final death pose before dissolving.")]
        public float LingerSeconds = 15f;

        [Tooltip("Seconds the dissolve fade takes.")]
        public float DissolveSeconds = 1.5f;

        /// <summary>Resources path of the dissolve material (Assets/Resources/CorpseDissolve.mat).</summary>
        private const string DissolveMaterialResource = "CorpseDissolve";

        private static readonly int DissolveID  = Shader.PropertyToID("_Dissolve");
        private static readonly int BaseMapID   = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorID     = Shader.PropertyToID("_Color");

        private bool _started;
        private readonly List<Material> _ownedMaterials = new();

        /// <summary>Begin the corpse lifecycle. Idempotent.</summary>
        public void BeginDeath()
        {
            if (_started) return;
            _started = true;

            // A corpse shouldn't be selectable or block raycasts.
            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // Keep animating off-screen so the fall always completes.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // 1) Wait for the death state to reach its last frame. The entity
                //    is gone, so nothing else drives this Animator; the death
                //    clip is non-looping and terminal, so it settles and holds.
                float safety = 5f;
                while (safety > 0f)
                {
                    var st = animator.GetCurrentAnimatorStateInfo(0);
                    if (!animator.IsInTransition(0) && st.normalizedTime >= 1f) break;
                    safety -= Time.deltaTime;
                    yield return null;
                }

                animator.speed = 0f; // lock the final pose
            }

            // 2) Linger in the final death pose.
            if (LingerSeconds > 0f)
                yield return new WaitForSeconds(LingerSeconds);

            // 3) Dissolve out.
            SwapToDissolveMaterials();
            if (_ownedMaterials.Count == 0)
            {
                Destroy(gameObject);
                yield break;
            }

            float dur = Mathf.Max(0.01f, DissolveSeconds);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float v = Mathf.Clamp01(t / dur);
                for (int i = 0; i < _ownedMaterials.Count; i++)
                    _ownedMaterials[i].SetFloat(DissolveID, v);
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Replace every renderer's materials with dissolve-shader instances,
        /// carrying over each slot's albedo + tint so the corpse still reads as
        /// itself while it fades. The albedo already contains the faction colour
        /// (SyntyTeamColorRecolor bakes it into the atlas), so copying the base
        /// map is enough.
        /// </summary>
        private void SwapToDissolveMaterials()
        {
            var master = Resources.Load<Material>(DissolveMaterialResource);
            if (master == null)
            {
                Debug.LogWarning(
                    "[CorpseDissolver] Missing Resources/CorpseDissolve material — " +
                    "skipping dissolve, destroying corpse.");
                return;
            }

            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                var src = renderer.materials; // instances — carry the recoloured atlas
                var dst = new Material[src.Length];

                for (int s = 0; s < src.Length; s++)
                {
                    var srcMat = src[s];
                    var dm = new Material(master);

                    Texture tex = srcMat.HasProperty(BaseMapID)
                        ? srcMat.GetTexture(BaseMapID)
                        : srcMat.mainTexture;
                    if (tex != null)
                    {
                        dm.SetTexture(BaseMapID, tex);
                        dm.mainTexture = tex;
                    }

                    Color col =
                        srcMat.HasProperty(BaseColorID) ? srcMat.GetColor(BaseColorID) :
                        srcMat.HasProperty(ColorID)     ? srcMat.GetColor(ColorID)     :
                        Color.white;
                    dm.SetColor(BaseColorID, col);
                    dm.SetFloat(DissolveID, 0f);

                    dst[s] = dm;
                    _ownedMaterials.Add(dm);
                }

                renderer.materials = dst;
            }
        }

        private void OnDestroy()
        {
            // Destroy the material instances we created so they don't leak.
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] != null)
                    Destroy(_ownedMaterials[i]);
            }
            _ownedMaterials.Clear();
        }
    }
}
