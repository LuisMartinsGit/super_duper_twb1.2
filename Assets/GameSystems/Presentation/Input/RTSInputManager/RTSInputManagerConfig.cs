using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Tunables for the input pump. The asset is RTSInputManager.asset,
    /// beside RTSInputManager.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    ///
    /// Only what the PUMP itself needs. Formation spacing moved to
    /// FormationInputConfig and the pick distances to ScreenPickConfig /
    /// WorldClickInputConfig when those responsibilities were split out —
    /// a config follows its owner.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/RTS Input Manager",
                     fileName = "RTSInputManager")]
    public sealed class RTSInputManagerConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>
        /// Layers a world click may hit. Owned here because the pump picks
        /// with it for hover AND hands it to WorldClickInput for targeting —
        /// the two must never disagree.
        /// </summary>
        public LayerMask clickMask;
    }
}
