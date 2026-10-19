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

            // Same reason again: a right-drag released over a panel never
            // reaches WorldClickInput.Tick, so its ghost must be cleared here.
            _clicks.CancelDragIfReleased();

            // Same reason: a formation shape requested by the actions panel
            // arrives WITH the cursor sitting on the button that sent it, so
            // draining it after the block guard means it only lands once the
            // player moves off the panel.
            _formation.ApplyPendingRequest();

            // Block input during UI interactions or building placement
            if (InputBlocking.ShouldBlock())
            {
                _hoverHint = null;   // the cursor is on the HUD: no world hint
                return;
            }

            // Update hover state (always allowed, even for observers)
            UpdateHover();

            // Observer mode: block all commands but allow hover/selection
            if (GameSettings.IsSpectating)
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

            // The right-click hint: with the Shardroot's bearer selected, a
            // Temple says what a right-click there will do (§ 3.1d). The text
            // and the rule are the order layer's; this only draws it.
            _hoverHint = HoveredEntity != Entity.Null && !GameSettings.IsSpectating
                ? _orders.EnshrineHoverHint(HoveredEntity, out _hoverHintOk)
                : null;
        }

        private string _hoverHint;
        private bool _hoverHintOk;
        private GUIStyle _hintStyle;

        private void DrawHoverHint()
        {
            if (string.IsNullOrEmpty(_hoverHint)) return;
            if (_hintStyle == null)
                _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            Vector2 m = UnityEngine.Input.mousePosition;
            var r = new Rect(m.x + 18f, Screen.height - m.y + 6f, 420f, 24f);
            _hintStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), _hoverHint, _hintStyle);
            _hintStyle.normal.textColor = _hoverHintOk
                ? TheWaningBorder.UI.World.EntityTargeting.ValidTint
                : TheWaningBorder.UI.World.EntityTargeting.InvalidTint;
            GUI.Label(r, _hoverHint, _hintStyle);
        }

        #endregion

        #region Debug Gui

        void OnGUI()
        {
            // The banner belongs to the modes it reports; the pump only owns
            // the OnGUI call, because a plain class cannot have one.
            _modes?.DrawBanner();
            DrawHoverHint();
        }

        #endregion
    }
}
