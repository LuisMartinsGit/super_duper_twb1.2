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

        // ── Right-drag formation facing (2026-10-15) ──

        /// <summary>
        /// Screen pixels the cursor must travel with the right button held
        /// before a ground press becomes a FACING DRAG. Below it the release is
        /// a plain right-click and routes exactly as it always did.
        /// </summary>
        public float dragThresholdPixels;

        /// <summary>Degrees the drag direction must turn before the ghost
        /// layout is rebuilt (BuildPlan allocates; this keeps it off the
        /// per-frame path while the cursor jitters).</summary>
        public float previewRefreshDegrees;

        /// <summary>Colour of one ghost slot marker.</summary>
        public Color previewSlotColor;

        /// <summary>Diameter of a ghost slot marker, in metres.</summary>
        public float previewSlotSize;

        /// <summary>Colour of the facing arrow drawn from the press point
        /// along the drag.</summary>
        public Color previewArrowColor;

        /// <summary>Width of the facing arrow line, in metres.</summary>
        public float previewArrowWidth;

        /// <summary>Height above the terrain the ghost markers and arrow
        /// float at, in metres.</summary>
        public float previewYOffset;
    }
}
