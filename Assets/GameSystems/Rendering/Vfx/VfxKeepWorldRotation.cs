// VfxKeepWorldRotation.cs
// Keeps an effect that FOLLOWS a unit (a buff loop, an ability's lasting
// effect, the Shardroot bearer's flicker…) at the world rotation it spawned
// with, so the unit turning does not spin it. It stays parented — position
// follows, fog of war hides it with the unit, it dies with the view — only
// the rotation is pinned. Added by VfxSpawn to every parented effect.
// Presentation only; no tunables, so no config asset.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class VfxKeepWorldRotation : MonoBehaviour
    {
        private Quaternion _world;

        void Awake() => _world = transform.rotation;

        void LateUpdate() => transform.rotation = _world;
    }
}
