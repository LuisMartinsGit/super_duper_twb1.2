using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Tunables for the right-click layer. The asset is WorldClickInput.asset,
    /// beside WorldClickInput.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    ///
    /// The click LAYER MASK is not here — it belongs to RTSInputManager, which
    /// picks with it for hover as well and hands it down. Two copies of a mask
    /// that must agree is exactly the drift this rule exists to stop.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/World Click Input",
                     fileName = "WorldClickInput")]
    public sealed class WorldClickInputConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>How far a click ray reaches into the world, in metres.</summary>
        public float clickRayLength;

        /// <summary>
        /// Nav layer the wall deck lives on. A cursor ray is projected onto the
        /// deck plane and the hit cell tested for passability THERE, which is
        /// what makes a rampart clickable without a top collider.
        /// </summary>
        public byte wallDeckLayer;

        /// <summary>
        /// Below this much vertical ray direction the deck-plane projection is
        /// degenerate (the camera is looking along the plane) and the rampart
        /// test is skipped.
        /// </summary>
        public float rampartRayEpsilon;
    }
}
