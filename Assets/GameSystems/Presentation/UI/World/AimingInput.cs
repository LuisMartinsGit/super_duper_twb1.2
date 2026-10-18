// AimingInput.cs
// "Does a spell's aim own the mouse right now?" — the one answer the input
// layer and the selection ask, for both aiming tools (the ground ring,
// GroundTargeting, and the entity pick, EntityTargeting).
//
// The tools confirm (and cancel) on the mouse PRESS and switch off at once,
// but the selection completes a click on the RELEASE and the order input runs
// in the same frame as the tool in an undefined order — so "is a tool active"
// alone let the confirming click also select what was clicked, and the
// cancelling right-click also issue a move order. The press that ended an aim
// is therefore swallowed until its button is released.
// docs/Design/Spells.md §6.

using UnityEngine;

namespace TheWaningBorder.UI.World
{
    public static class AimingInput
    {
        private static bool _swallowing;

        /// <summary>True while a spell is being aimed, and for the rest of the
        /// press (until release) that confirmed or cancelled one.</summary>
        public static bool HoldsPointer
        {
            get
            {
                if (GroundTargeting.IsActive || EntityTargeting.IsActive) return true;
                if (!_swallowing) return false;
                if (UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButtonUp(0)
                    || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButtonUp(1))
                    return true;
                _swallowing = false;
                return false;
            }
        }

        /// <summary>Called by a tool on the press that confirms or cancels it.</summary>
        public static void SwallowCurrentPress() => _swallowing = true;
    }
}
