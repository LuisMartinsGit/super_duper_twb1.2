
//This script handles the clean up and destroying of shattered pieces after an ore node has been mined.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ShatterStone
{
    public class ShatterCleanUp : MonoBehaviour
    {
        [SerializeField] private float delayBeforeShrink = 10f;
        [SerializeField] private float shrinkDuration = 2f;
        [SerializeField] private float randomStartOffset = 0.5f; // max +/- offset in seconds

        private struct PieceData
        {
            public Transform transform;
            public Vector3 originalScale;
            public float startDelay;
        }

        private List<PieceData> pieces = new();

        private void Start()
        {
            // Collect only child pieces, excluding the root
            foreach (Transform child in transform)
            {
                CollectPiecesRecursive(child);
            }

            StartCoroutine(HandleShrink());
        }

        private void CollectPiecesRecursive(Transform current)
        {
            // Skip particle system GameObjects
            if (current.GetComponent<ParticleSystem>() != null)
                return;

            float offset = Random.Range(-randomStartOffset, randomStartOffset);
            pieces.Add(new PieceData
            {
                transform = current,
                originalScale = current.localScale,
                startDelay = delayBeforeShrink + offset
            });

            foreach (Transform child in current)
            {
                CollectPiecesRecursive(child);
            }
        }

        private IEnumerator HandleShrink()
        {
            float elapsed = 0f;
            Dictionary<Transform, float> timers = new();

            foreach (var piece in pieces)
                timers[piece.transform] = 0f;

            float maxDuration = delayBeforeShrink + randomStartOffset + shrinkDuration;

            while (elapsed < maxDuration)
            {
                elapsed += Time.deltaTime;

                foreach (var piece in pieces)
                {
                    if (piece.transform == null) continue;

                    float pieceTime = elapsed - piece.startDelay;
                    if (pieceTime < 0f) continue;

                    float t = Mathf.Clamp01(pieceTime / shrinkDuration);
                    piece.transform.localScale = piece.originalScale * (1f - t);
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
