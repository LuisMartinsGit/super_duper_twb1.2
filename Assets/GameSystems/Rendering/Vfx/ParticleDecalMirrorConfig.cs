using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="ParticleDecalMirror"/>. The asset is
    /// ParticleDecalMirror.asset, beside ParticleDecalMirror.cs. No field
    /// initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Particle Decal Mirror",
                     fileName = "ParticleDecalMirror")]
    public sealed class ParticleDecalMirrorConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Material on the TWB/VfxDecal shader graph (base map ×
        /// tint, emissive) every mirrored particle is drawn with — instanced
        /// per projector so each carries its own texture and colour.</summary>
        public Material decalMaterial;
        /// <summary>Emission strength on top of the particle colour (the
        /// particles were additive; a decal glows through emission instead).</summary>
        public float glow;
        /// <summary>Projection depth, metres — how far above and below the
        /// particle the decal reaches onto the ground (and slopes).</summary>
        public float depth;
        /// <summary>Most decals one subsystem may drive at once.</summary>
        public int maxParticles;
        /// <summary>Lift, metres, applied instead when the renderer has no
        /// decal support (the Mobile renderer): the flat quad is drawn as a
        /// particle again, just clear of the ground.</summary>
        public float fallbackLift;
    }
}
