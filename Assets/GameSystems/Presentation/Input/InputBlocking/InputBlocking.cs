// InputBlocking.cs
// "Is the player aiming at the world, or at the interface?"
// Part of: Input/ — split out of RTSInputManager.

using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// The one predicate that decides whether a world click / hotkey is the
    /// player's, or belongs to something already holding the mouse.
    ///
    /// Static and stateless on purpose: it reads published facts
    /// (<see cref="TheWaningBorder.Core.PresentationState"/>, the EventSystem)
    /// and owns none of them, so anything that needs the same answer can ask
    /// without going through the input manager.
    /// </summary>
    public static class InputBlocking
    {
        /// <summary>True when this frame's pointer input is not the world's.</summary>
        public static bool ShouldBlock()
        {
            // Pointer over a uGUI element (the minimap RawImage is the main
            // one in this stack): the element handles its own clicks — a
            // right-click on the minimap must issue the MINIMAP move order,
            // not ALSO raycast the world behind the HUD and issue a second
            // conflicting command.
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return true;

            // One-frame suppression (after GUI button clicks)
            if (BuilderCommandPanel.SuppressClicksThisFrame)
            {
                BuilderCommandPanel.SuppressClicksThisFrame = false;
                return true;
            }

            // Old IMGUI panel guards (EntityActionPanel / EntityInfoPanel /
            // SpellPanel / CultureChoicePopup) removed with the old UI
            // (2026-07-17); the EventSystem check above covers the final uGUI.

            // Block while aiming an ability with the ground-target ring
            // (sect powers / Reliquary abilities) — GroundTargeting owns the
            // mouse until cast or cancel.
            if (GroundTargeting.IsActive)
                return true;

            // Block during building placement
            if (TheWaningBorder.Core.PresentationState.PlacingBuilding)
                return true;

            // Block while the unit sandbox has a unit armed — the click that
            // drops the unit must not ALSO order the current selection to walk
            // there. Same contract as PlacingBuilding above; the property is
            // false in every mode but ScenarioType.Sandbox, where the panel is
            // the only thing that mounts it.
            if (SandboxPanel.IsPlacing)
                return true;

            return false;
        }
    }
}
