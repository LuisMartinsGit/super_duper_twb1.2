using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Tunables for the screen-to-entity pick. The asset is ScreenPick.asset,
    /// beside ScreenPick.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Screen Pick",
                     fileName = "ScreenPick")]
    public sealed class ScreenPickConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>How far the pick ray reaches into the world, in metres.</summary>
        public float rayLength;
    }
}
