using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Tunables for control groups. The asset is ControlGroupSystem.asset,
    /// beside ControlGroupSystem.cs.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Control Group System",
                     fileName = "ControlGroupSystem")]
    public sealed class ControlGroupSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>
        /// Seconds within which pressing the same group number twice centres
        /// the camera on it instead of just recalling it.
        /// </summary>
        public float doubleTapThreshold;
    }
}
