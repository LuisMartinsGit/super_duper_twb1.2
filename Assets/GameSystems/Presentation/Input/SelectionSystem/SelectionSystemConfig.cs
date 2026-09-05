using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Tunables for selection. The asset is SelectionSystem.asset, beside
    /// SelectionSystem.cs.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Selection System",
                     fileName = "SelectionSystem")]
    public sealed class SelectionSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>
        /// Seconds within which a second click counts as a double click, which
        /// selects every unit of that class on screen.
        /// </summary>
        public float doubleClickThreshold;
    }
}
