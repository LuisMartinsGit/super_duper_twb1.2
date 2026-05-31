// CadaverCrystalAnimator.cs
// Per-cadaver visual driver. Sits on the root of the instantiated
// P_Cadaver_Gem* GameObject. The cluster shows no animation during mining —
// it's "chipped away" implicitly as the patch loses nodes one by one. The
// only visual event is the full shatter when this node hits 0 crystal.
//
// Death-handoff:
//   PresentationSpawnSystem.CleanupDestroyedEntities() checks for this
//   component before destroying the view; if present, it calls
//   BeginDeath() and the animator owns final cleanup (the asset's debris
//   pieces are not parented to this GO, so they survive past Destroy()).

using System.Collections;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CadaverCrystalAnimator : MonoBehaviour
    {
        [SerializeField] private CadaverOreNode _oreNode;

        private bool _dying;

        private void Awake()
        {
            if (_oreNode == null) _oreNode = GetComponentInChildren<CadaverOreNode>();
        }

        private void Start()
        {
            // MiningNodeAudio.Awake forces spatialBlend=1 (3D) with a
            // logarithmic rolloff from MinDistance=1 — at the RTS camera's
            // typical 15–30 m distance the shatter clip drops to inaudible.
            // Pull it back to 2D so the depletion shatter always carries.
            // Start runs after every Awake, so this override survives.
            var src = GetComponent<AudioSource>();
            if (src != null)
            {
                src.spatialBlend = 0f;
                src.volume = 1f;
            }
        }

        /// <summary>
        /// Called by PresentationSpawnSystem on ECS entity destruction.
        /// Spawns the asset's debris pieces and schedules our own destroy.
        /// </summary>
        public void BeginDeath()
        {
            if (_dying) return;
            _dying = true;

            if (_oreNode != null) _oreNode.Shatter();

            StartCoroutine(DestroyAfterShatter());
        }

        private IEnumerator DestroyAfterShatter()
        {
            // Give the shatter SFX one frame to register; debris pieces
            // are spawned un-parented so they outlive this GameObject.
            yield return null;
            Destroy(gameObject);
        }
    }
}
