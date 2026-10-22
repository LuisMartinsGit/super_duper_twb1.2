using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="CleanseSfx"/>. The asset is CleanseSfx.asset,
    /// beside CleanseSfx.cs. No field initialisers: the values live in the
    /// asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Cleanse Sfx",
                     fileName = "CleanseSfx")]
    public sealed class CleanseSfxConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Looped at the Litharch for as long as it channels Cleanse
        /// (Cleanse_Loop.wav, a copy of Magic Arsenal's lightloop).</summary>
        public AudioClip loopClip;
        /// <summary>Played once at the aim point when the cast lands and the
        /// starting circle is cleared (Cleanse_Burst.wav, a copy of Magic
        /// Arsenal's magic_cast_light).</summary>
        public AudioClip burstClip;
        /// <summary>Volume of the loop, 0-1 (the master volume applies on top).</summary>
        public float loopVolume;
        /// <summary>Volume of the burst, 0-1.</summary>
        public float burstVolume;
        /// <summary>Volume of the burst played on every pulse after the landing one.</summary>
        public float pulseVolume;
        /// <summary>Seconds the loop takes to fade in when a channel starts
        /// (or comes into view) and out when it ends.</summary>
        public float fadeSeconds;
        /// <summary>3D: full volume within this distance of the listener
        /// (the camera), metres.</summary>
        public float minDistance;
        /// <summary>3D: silent beyond this distance of the listener, metres
        /// (linear roll-off between the two).</summary>
        public float maxDistance;
        /// <summary>How positional the sound is, 0 (flat) to 1 (fully 3D).</summary>
        public float spatialBlend;
        /// <summary>How often channels are sampled, seconds.</summary>
        public float pollSeconds;
    }
}
