// RTSInputManager.cs
// The input pump: owns the four input responsibilities and runs them in order.
// Part of: Input/

using UnityEngine;
using Unity.Entities;
using EntityWorld = Unity.Entities.World;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Core.Commands.Issuing;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// The MonoBehaviour that holds the player's input frame together. It does
    /// no input work itself — it owns one object per responsibility and calls
    /// them in the order the frame requires:
    ///
    ///   <see cref="InputBlocking"/>   is this frame's pointer ours at all?
    ///   <see cref="HotkeyInput"/>     the keyboard
    ///   <see cref="WorldClickInput"/> the right mouse button
    ///   <see cref="FormationInput"/>  the formation shape those two share
    ///   <see cref="InputModes"/>      the armed attack-move / patrol state
    ///
    /// What a command MEANS for the selection is not here and never was after
    /// 2026-09-01: that is <see cref="SelectionOrders"/>, in Runtime. This
    /// class knows about screens and keys; it knows no gameplay rules.
    /// </summary>
    public class RTSInputManager : MonoBehaviour
    {
        #region Configuration

        private RTSInputManagerConfig _cfg;
        private RTSInputManagerConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RTSInputManagerConfig>());

        #endregion

        #region State

        private EntityWorld _world;
        private EntityManager _em;

        // The order layer lives in Runtime; it is handed the world and a way
        // to read the selection, and knows nothing else about the UI.
        private SelectionOrders _orders;

        private InputModes _modes;
        private FormationInput _formation;
        private HotkeyInput _hotkeys;
        private WorldClickInput _clicks;

        // Tracks Shift state across frames so we can detect the down→up
        // transition and unfreeze accumulated queues at that moment.
        private bool _shiftWasHeld;

        /// <summary>Entity under the cursor, or Entity.Null.</summary>
        public static Entity HoveredEntity { get; private set; }

        #endregion

        #region Lifecycle

        /// <summary>Set in Awake so the pause menu can drive the Esc cascade
        /// (see <see cref="CancelModesOrSelection"/>).</summary>
        private static RTSInputManager _instance;

        void Awake()
        {
            _instance = this;
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            _em = _world.EntityManager;

            gameObject.AddComponent<ControlGroupSystem>();

            _orders = new SelectionOrders(_em, () => SelectionSystem.CurrentSelection);
            _modes = new InputModes();
            _formation = new FormationInput(_orders);
            _hotkeys = new HotkeyInput(_em, _orders, _modes, _formation);
            _clicks = new WorldClickInput(_em, _orders, _modes, Cfg.clickMask);
        }

        void OnDestroy()
        {
            _instance = null;
        }

        /// <summary>
        /// One step of the Esc cascade: cancel attack-move / patrol aiming,
        /// else drop the selection. Returns true when something was cancelled.
        ///
        /// Esc used to be handled here directly, but this manager stops
        /// processing hotkeys the moment the pointer is over any uGUI element
        /// (<see cref="InputBlocking"/>) — which is most of the screen once the
        /// HUD is up, and ALL of it once the pause menu is open. PauseMenuPanel
        /// owns the key now and calls down into this; there is exactly one Esc
        /// cascade and it works wherever the cursor happens to be.
        /// </summary>
        public static bool CancelModesOrSelection()
        {
            var self = _instance;
            if (self != null && self._modes.Disarm())
                return true;

            if (SelectionSystem.CurrentSelection != null
                && SelectionSystem.CurrentSelection.Count > 0)
            {
                SelectionSystem.ClearSelection();
                return true;
            }
            return false;
        }

        void Update()
        {
            // Detect Shift release independent of UI/blocking guards: if the
            // user lets go of Shift while their cursor happens to be over a
            // panel, queues must still resume — otherwise the frozen tag
            // would persist forever and the units would never move.
            bool shiftHeldNow = UnityEngine.Input.GetKey(KeyCode.LeftShift)
                              || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (_shiftWasHeld && !shiftHeldNow)
                _orders.UnfreezeAllQueues();
            _shiftWasHeld = shiftHeldNow;

            // Same reason: a formation shape requested by the actions panel
            // arrives WITH the cursor sitting on the button that sent it, so
            // draining it after the block guard means it only lands once the
            // player moves off the panel.
            _formation.ApplyPendingRequest();

            // Block input during UI interactions or building placement
            if (InputBlocking.ShouldBlock())
                return;

            // Update hover state (always allowed, even for observers)
            UpdateHover();

            // Observer mode: block all commands but allow hover/selection
            if (GameSettings.IsObserver)
                return;

            _hotkeys.Tick();
            _clicks.Tick();
        }

        #endregion

        #region Hover Detection

        private void UpdateHover()
        {
            var hovered = ScreenPick.EntityUnderMouse(Cfg.clickMask, _em);
            HoveredEntity = _em.Exists(hovered) ? hovered : Entity.Null;
        }

        #endregion

        #region Debug Gui

        void OnGUI()
        {
            // The banner belongs to the modes it reports; the pump only owns
            // the OnGUI call, because a plain class cannot have one.
            _modes?.DrawBanner();
        }

        #endregion
    }
}
