using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.UI.Ingame
{
    /// <summary>
    /// Tunables of <see cref="ResourceGainPopups"/>, the floating "+N [icon]"
    /// shown where a faction gains resources. The asset is
    /// ResourceGainPopups.asset beside the class. No field initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/UI/ResourceGainPopups",
                     fileName = "ResourceGainPopups")]
    public sealed class ResourceGainPopupsConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Seconds a popup stays (fading over its last 40 %).</summary>
        public float lifetimeSeconds;
        /// <summary>How far it rises over its life, world units.</summary>
        public float riseWorldUnits;
        /// <summary>Where it starts above the spot, world units.</summary>
        public float yOffset;
        public float fontSize;
        public Color color;
        /// <summary>Oldest popups are dropped past this many.</summary>
        public int maxPopups;
        /// <summary>Overlay canvas order: above the damage numbers (60), under the HUD (100).</summary>
        public int canvasSortingOrder;
    }
}
