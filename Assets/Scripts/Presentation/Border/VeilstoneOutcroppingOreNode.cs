// VeilstoneOutcroppingOreNode.cs
// Thin subclass of NV3D's OreNode used as the runtime script on our
// P_VeilstoneOutcropping_Gem* prefabs. Keeps the asset's serialized field bindings
// (pieces / nodeAudio / childRenderers) but bypasses its hit counter, drop
// spawning, and respawn/destroy timers — the ECS layer owns lifetime, and
// the only animation we use is the full shatter on node depletion.

using System.Collections;
using UnityEngine;
using ShatterStone;

namespace TheWaningBorder.Presentation
{
    public sealed class VeilstoneOutcroppingOreNode : OreNode
    {
        public void Shatter()
        {
            if (pieces != null)
            {
                // Spawn debris at unit scale. The vendor's Pieces prefab is
                // authored at scale 1 with convex MeshColliders that just
                // barely interpenetrate at rest; spawning at our visual
                // scale (3× for iron, 6× for veilstone) bloats every collider
                // proportionally and physics resolves the overlap as a
                // violent explosion instead of a crumble. Slightly-smaller-
                // than-the-rock debris is the price for the right read.
                pieces.transform.localScale = Vector3.one;
                Instantiate(pieces, transform.position, transform.rotation);
            }
            if (nodeCollider) nodeCollider.enabled = false;
            if (childRenderers != null)
            {
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    if (childRenderers[i] != null) childRenderers[i].enabled = false;
                }
            }
            nodeAudio?.PlayShatterSound();
        }

        protected override IEnumerator DelayDestroy()
        {
            yield break;
        }
    }
}
