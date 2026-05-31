/*******************************************************************************************
 * File: OreNode.cs
 * Author: NV3D
 * Description: Core logic for handling ore node interactions, animations, and drops.
 * Copyright © 2025 NV3D. All rights reserved.
 * This code is subject to the Unity Asset Store EULA and may not be redistributed or resold.
 *******************************************************************************************/

//This script manages behaviour and interactions with the Shatter Stone ore nodes. Requires MiningNodeAudio.cs

using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ShatterStone
{
    /// <summary>
    /// A cache for the visual node bounds
    /// </summary>
    public struct OreNodeBounds
    {
        public float minX, maxX, minZ, maxZ, centerY;

        public OreNodeBounds(float minX, float maxX, float minZ, float maxZ, float centerY)
        {
            this.minX = minX;
            this.maxX = maxX;
            this.minZ = minZ;
            this.maxZ = maxZ;
            this.centerY = centerY;
        }
    }

    /// <summary>
    /// Represents a generic ore node that can be gathered via interactions
    /// </summary>
    public class OreNode : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Drop Settings")]
        [SerializeField] protected GameObject pieces;
        [SerializeField] protected GameObject refinedPickup;
        [SerializeField, Min(0)] protected int dropOnHit;
        [SerializeField, Min(1)] protected int hitsToDestroy;
        [SerializeField, Min(0)] protected int dropOnDestroy;

        [Header("Knockback Settings")]
        [SerializeField] protected Vector3 knockAngle;
        [SerializeField] protected AnimationCurve knockCurve;
        [SerializeField] protected float knockDuration = 1f;

        [Header("Respawn Settings")]
        [SerializeField] protected bool enableRespawn = true;
        [SerializeField] protected float respawnDelay = 30f;

        [Header("Configuration")]
        [SerializeField] protected bool cacheVisualBoundaries = true;
        [SerializeField] protected MiningNodeAudio nodeAudio;
        [SerializeField] protected Collider nodeCollider;
        [SerializeField] protected Renderer[] childRenderers;

        #endregion

        #region Private Fields

        private OreNodeBounds nodeBounds;
        private int hitIndex;

        #endregion

        protected virtual void Start()
        {
            if (nodeAudio == null) nodeAudio = GetComponent<MiningNodeAudio>();
            if (nodeCollider == null) nodeCollider = GetComponent<Collider>();
            if (childRenderers == null || childRenderers.Length == 0)
                childRenderers = GetComponentsInChildren<Renderer>();
        }

        public virtual void Interact() => Interact(1);

        public virtual void Interact(int hits)
        {
            if (ShouldCalculateNodeBounds())
                nodeBounds = CalculateNodeBounds();

            InflictHit(GetDropCount(hits));

            if (hitIndex < hitsToDestroy)
            {
                StartCoroutine(Animate());
                nodeAudio?.PlayImpactSound();
            }
            else
            {
                ReplaceNodeVisualsWithBrokenOne();
            }
        }

        [Obsolete("Use Interact(hits) instead")]
        public void oreHit() => Interact(1);

        protected virtual int GetDropCount(int hits)
        {
            int total = dropOnHit * hits;
            if (hitIndex + hits >= hitsToDestroy)
                total += dropOnDestroy;
            return total;
        }

        protected virtual bool ShouldCalculateNodeBounds()
        {
            return !cacheVisualBoundaries || hitIndex == 0;
        }

        protected virtual OreNodeBounds CalculateNodeBounds()
        {
            Renderer renderer = TryGetComponent(out MeshRenderer meshRenderer) ? meshRenderer : GetComponentInChildren<Renderer>();
            if (renderer == null)
                return new OreNodeBounds();

            Bounds bounds = renderer.bounds;
            return new OreNodeBounds(bounds.min.x, bounds.max.x, bounds.min.z, bounds.max.z, bounds.center.y);
        }

        protected virtual void InflictHit(int dropCount)
        {
            hitIndex++;
            for (int i = 0; i < dropCount; i++)
            {
                Vector3 dropPos = CalculateRandomDropPosition(nodeBounds);
                Instantiate(refinedPickup, dropPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
            }
        }

        protected virtual Vector3 CalculateRandomDropPosition(OreNodeBounds bounds)
        {
            return new Vector3(
                Random.Range(bounds.minX, bounds.maxX),
                bounds.centerY,
                Random.Range(bounds.minZ, bounds.maxZ)
            );
        }

        protected virtual void ReplaceNodeVisualsWithBrokenOne()
        {
            pieces.transform.localScale = transform.localScale;
            Instantiate(pieces, transform.position, transform.rotation);
            if (nodeCollider) nodeCollider.enabled = false;
            foreach (var renderer in childRenderers) renderer.enabled = false;
            nodeAudio?.PlayShatterSound();

            if (enableRespawn)
                ResetNode(respawnDelay);
            else
                StartCoroutine(DelayDestroy());
        }

        protected virtual IEnumerator Animate()
        {
            if (nodeCollider) nodeCollider.enabled = false;
            Quaternion originalRotation = transform.localRotation;
            Quaternion knockRotation = Quaternion.Euler(knockAngle);

            float t = 0;
            while (t < knockDuration)
            {
                float v = knockCurve.Evaluate(t / knockDuration);
                transform.localRotation = originalRotation * Quaternion.Slerp(Quaternion.identity, knockRotation, v);
                t += Time.deltaTime;
                yield return null;
            }
            transform.localRotation = originalRotation;
            if (nodeCollider) nodeCollider.enabled = true;
        }


        public virtual void ResetNode(float respawnDelay) => StartCoroutine(ResetAsync(respawnDelay));

        public virtual IEnumerator ResetAsync(float respawnDelay)
        {
          yield return new WaitForSeconds(respawnDelay);
          RevertToInitialState();
        }

        protected virtual void RevertToInitialState()
        {
          hitIndex = 0;
          if (nodeCollider) nodeCollider.enabled = true;
          foreach (var rend in childRenderers) rend.enabled = true;
        }

        private const float DelayDestroySeconds = 5f;

        protected virtual IEnumerator DelayDestroy()
        {
            yield return new WaitForSeconds(DelayDestroySeconds);
            Destroy(gameObject);
        }
    }
}
