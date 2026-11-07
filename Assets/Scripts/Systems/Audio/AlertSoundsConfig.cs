using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Audio
{
    /// <summary>
    /// Tunables for <see cref="AlertSounds"/>. The asset is AlertSounds.asset, beside
    /// AlertSounds.cs. No field initialisers: the values live in the asset and nowhere
    /// else. The clips are in GameData/Audio/Alerts/, from tools/audio/gen_alert_sounds.py.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Alert Sounds",
                     fileName = "AlertSounds")]
    public sealed class AlertSoundsConfig : ScriptableObject, IComponentConfig
    {
        public AudioClip underAttackClip;
        public AudioClip shardrootClip;
        public AudioClip monumentClip;
        public AudioClip countdownClip;
        public AudioClip victoryClip;
        public AudioClip defeatClip;

        /// <summary>Volumes, 0-1 (the master volume applies on top).</summary>
        public float underAttackVolume;
        public float shardrootVolume;
        public float monumentVolume;
        public float countdownVolume;
        public float victoryVolume;
        public float defeatVolume;

        /// <summary>Seconds before the horn repeats for a fight in the same area.</summary>
        public float attackRepeatSeconds;
        /// <summary>The shortest gap between two horns, even for a new area, seconds.</summary>
        public float attackMinGapSeconds;
        /// <summary>A hit further than this from the last horn is a new area, metres.</summary>
        public float attackAreaRadius;
        /// <summary>The shortest gap between two map-event sounds, seconds.</summary>
        public float eventMinGapSeconds;
    }
}
