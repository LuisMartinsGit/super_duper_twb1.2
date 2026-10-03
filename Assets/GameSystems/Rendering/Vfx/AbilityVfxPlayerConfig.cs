using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="AbilityVfxPlayer"/>. The asset is
    /// AbilityVfxPlayer.asset, beside AbilityVfxPlayer.cs. No field
    /// initialisers: the values live in the asset and nowhere else. The effect
    /// prefabs themselves are NOT here — each ability's lives in its own folder
    /// as AbilityDefSO.vfxPrefab.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Ability Vfx Player",
                     fileName = "AbilityVfxPlayer")]
    public sealed class AbilityVfxPlayerConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Effect scale per metre of the affected UNIT's height (the
        /// Lana effects are authored around a ~2 m character).</summary>
        public float unitScalePerMetre;
        /// <summary>Effect scale per metre of the affected BUILDING's
        /// half-width.</summary>
        public float buildingScalePerMetre;
        /// <summary>Radius, metres, an area effect is authored at: an Area
        /// ability of radius R plays at R / this.</summary>
        public float areaAuthoredRadius;
        /// <summary>Clamp on the area scale, so a 25 m power does not fill the screen.</summary>
        public float areaMinScale;
        public float areaMaxScale;
        /// <summary>Seconds a timed effect keeps fading after the ability ends.</summary>
        public float fadeSeconds;
        /// <summary>How often units are scanned for passive-aura effects.</summary>
        public float passivePollSeconds;
    }
}
