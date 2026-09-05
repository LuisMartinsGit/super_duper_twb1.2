using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// The key bindings. The asset is HotkeyInput.asset, beside HotkeyInput.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    ///
    /// The MODIFIERS (Shift to queue / add, Ctrl to assign) are deliberately
    /// not here. They are engine-side RTS convention rather than something a
    /// designer tunes, and they are read in three places — the queue-freeze in
    /// RTSInputManager, the waypoint path in WorldClickInput, and the control
    /// groups below — so a single owner for them would be a fourth thing to
    /// thread through everything for no gain.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Hotkey Input",
                     fileName = "HotkeyInput")]
    public sealed class HotkeyInputConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Arms attack-move: the next right-click moves and engages.</summary>
        public KeyCode attackMove;

        /// <summary>Arms patrol: the next right-click sets the patrol leg.</summary>
        public KeyCode patrol;

        /// <summary>Cycles the formation shape and re-slots the selection.</summary>
        public KeyCode cycleFormation;

        /// <summary>Stops the selection where it stands.</summary>
        public KeyCode stop;

        /// <summary>Holds position — stay put, but still fight what comes.</summary>
        public KeyCode holdPosition;

        /// <summary>Selects and centres on the next idle builder.</summary>
        public KeyCode cycleIdleBuilders;

        /// <summary>Toggles planning mode; pressed again it executes the plan.</summary>
        public KeyCode planningMode;

        /// <summary>Executes a planned batch without leaving planning mode.</summary>
        public KeyCode planningExecute;

        /// <summary>First control-group key; the rest follow it in KeyCode order.</summary>
        public KeyCode controlGroupFirst;

        /// <summary>How many control groups those keys cover.</summary>
        public int controlGroupCount;
    }
}
