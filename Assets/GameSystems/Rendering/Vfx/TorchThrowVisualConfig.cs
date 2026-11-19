using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="TorchThrowVisual"/>, the torch a melee unit
    /// throws at a building (decision 82). The asset is TorchThrowVisual.asset,
    /// beside TorchThrowVisual.cs. No field initialisers. Presentation only —
    /// the hit itself is the simulation's (MeleeCombatSystem).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Torch Throw Visual",
                     fileName = "TorchThrowVisual")]
    public sealed class TorchThrowVisualConfig : ScriptableObject, IComponentConfig
    {
        static TorchThrowVisualConfig _i;
        public static TorchThrowVisualConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<TorchThrowVisualConfig>());

        /// <summary>Seconds from the throw to the landing.</summary>
        public float flightSeconds;
        /// <summary>Height of the arc's top above the straight line, metres.</summary>
        public float arcHeight;
        /// <summary>Release height above the thrower's feet, metres.</summary>
        public float releaseHeight;
        /// <summary>Landing height above the building's ground, metres.</summary>
        public float impactHeight;
        /// <summary>Turns a second the torch tumbles end over end.</summary>
        public float spinTurnsPerSecond;

        /// <summary>The burning head: HDR colour (blooms) and world size.</summary>
        [ColorUsage(true, true)] public Color headColor;
        public float headSize;
        /// <summary>The wooden haft's colour and length.</summary>
        public Color haftColor;
        public float haftLength;

        /// <summary>The streak: HDR colour at the torch and at the tail end
        /// (alpha fades it), its seconds and width at the torch.</summary>
        [ColorUsage(true, true)] public Color streakColor;
        [ColorUsage(true, true)] public Color streakTailColor;
        public float streakSeconds;
        public float streakWidth;

        /// <summary>The point light the torch carries in flight.</summary>
        public Color lightColor;
        public float lightIntensity;
        public float lightRange;

        /// <summary>The flash where it lands: a glowing burst and a light,
        /// both fading over flashSeconds.</summary>
        [ColorUsage(true, true)] public Color flashColor;
        public float flashSize;
        public float flashLightIntensity;
        public float flashLightRange;
        public float flashSeconds;

        /// <summary>Most torches and flashes with a light at once — every one
        /// past it still flies and flashes, unlit (lights cost per pixel).</summary>
        public int maxLights;
    }
}
