// KeyBindings.cs
// The keys a player can rebind (Settings > Key Bindings). The defaults are
// HotkeyInput.asset; a player's choices are PlayerProfile (settings.json).

using System;
using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>A rebindable command. The order is the settings tab's order.</summary>
    public enum KeyAction
    {
        AttackMove, Patrol, Stop, HoldPosition, AggressiveStance, DefensiveStance,
        CycleFormation, CycleIdleWorkers, PlanningMode, PlanningExecute,
        ReligionMenu, ReligionTree, PanUp, PanDown, PanLeft, PanRight,
    }

    /// <summary>
    /// The one place a rebindable key is read. Fixed on purpose, and refused as a
    /// binding: Esc (the menu cascade), the control-group digits, F1-F6 (sect powers,
    /// with Shift and Ctrl for the other slots) and the modifiers, which all of those
    /// and the mouse commands use.
    /// </summary>
    public static class KeyBindings
    {
        public struct Entry
        {
            public KeyAction Action;
            public string Label;
            public Func<HotkeyInputConfig, KeyCode> Default;
        }

        public static readonly Entry[] All =
        {
            new Entry { Action = KeyAction.AttackMove, Label = "Attack-move", Default = c => c.attackMove },
            new Entry { Action = KeyAction.Patrol, Label = "Patrol", Default = c => c.patrol },
            new Entry { Action = KeyAction.Stop, Label = "Stop", Default = c => c.stop },
            new Entry { Action = KeyAction.HoldPosition, Label = "Hold position", Default = c => c.holdPosition },
            new Entry { Action = KeyAction.AggressiveStance, Label = "Aggressive stance", Default = c => c.aggressiveStance },
            new Entry { Action = KeyAction.DefensiveStance, Label = "Defensive stance", Default = c => c.defensiveStance },
            new Entry { Action = KeyAction.CycleFormation, Label = "Cycle formation", Default = c => c.cycleFormation },
            new Entry { Action = KeyAction.CycleIdleWorkers, Label = "Next idle worker", Default = c => c.cycleIdleWorkers },
            new Entry { Action = KeyAction.PlanningMode, Label = "Planning mode", Default = c => c.planningMode },
            new Entry { Action = KeyAction.PlanningExecute, Label = "Execute plan", Default = c => c.planningExecute },
            new Entry { Action = KeyAction.ReligionMenu, Label = "Religion menu", Default = c => c.religionMenu },
            new Entry { Action = KeyAction.ReligionTree, Label = "Religion tree", Default = c => c.religionTree },
            new Entry { Action = KeyAction.PanUp, Label = "Pan camera up", Default = c => c.panUp },
            new Entry { Action = KeyAction.PanDown, Label = "Pan camera down", Default = c => c.panDown },
            new Entry { Action = KeyAction.PanLeft, Label = "Pan camera left", Default = c => c.panLeft },
            new Entry { Action = KeyAction.PanRight, Label = "Pan camera right", Default = c => c.panRight },
        };

        private static KeyCode[] _keys;

        private static HotkeyInputConfig Cfg => ComponentConfig.Require<HotkeyInputConfig>();

        /// <summary>The key bound to this command now.</summary>
        public static KeyCode Get(KeyAction action)
        {
            if (_keys == null) Rebuild();
            return _keys[(int)action];
        }

        public static bool Down(KeyAction action) => UnityEngine.Input.GetKeyDown(Get(action));
        public static bool Held(KeyAction action) => UnityEngine.Input.GetKey(Get(action));

        private static void Rebuild()
        {
            var cfg = Cfg;
            _keys = new KeyCode[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                KeyCode key = cfg != null ? All[i].Default(cfg) : KeyCode.None;
                if (PlayerProfile.TryGetKey(All[i].Action.ToString(), out int saved)
                    && Enum.IsDefined(typeof(KeyCode), saved) && !IsReserved((KeyCode)saved))
                    key = (KeyCode)saved;
                _keys[(int)All[i].Action] = key;
            }
        }

        /// <summary>Keys that mean something fixed, so they cannot be bound.</summary>
        public static bool IsReserved(KeyCode key)
        {
            if (key == KeyCode.None || key == KeyCode.Escape) return true;
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) return true;
            if (key >= KeyCode.F1 && key <= KeyCode.F6) return true;
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift
                || key == KeyCode.LeftControl || key == KeyCode.RightControl
                || key == KeyCode.LeftAlt || key == KeyCode.RightAlt) return true;
            var cfg = Cfg;
            return cfg != null && key >= cfg.controlGroupFirst
                && key < cfg.controlGroupFirst + cfg.controlGroupCount;
        }

        /// <summary>
        /// Binds a key, saved at once. A key already in use swaps: the other command
        /// takes this one's old key, so no command is ever left with none. Returns the
        /// command that swapped, or null.
        /// </summary>
        public static KeyAction? Set(KeyAction action, KeyCode key)
        {
            if (_keys == null) Rebuild();
            KeyAction? swapped = null;
            KeyCode old = _keys[(int)action];
            foreach (var e in All)
            {
                if (e.Action == action || _keys[(int)e.Action] != key) continue;
                _keys[(int)e.Action] = old;
                PlayerProfile.SetKey(e.Action.ToString(), (int)old);
                swapped = e.Action;
                break;
            }
            _keys[(int)action] = key;
            PlayerProfile.SetKey(action.ToString(), (int)key);
            return swapped;
        }

        public static void ResetAll()
        {
            PlayerProfile.ClearKeys();
            _keys = null;
        }

        /// <summary>A key's name for a label.</summary>
        public static string NameOf(KeyCode key) => key switch
        {
            KeyCode.Return => "Enter",
            KeyCode.UpArrow => "Up",
            KeyCode.DownArrow => "Down",
            KeyCode.LeftArrow => "Left",
            KeyCode.RightArrow => "Right",
            KeyCode.Comma => ",",
            KeyCode.Period => ".",
            KeyCode.Semicolon => ";",
            KeyCode.Quote => "'",
            KeyCode.Slash => "/",
            KeyCode.Backslash => "\\",
            KeyCode.LeftBracket => "[",
            KeyCode.RightBracket => "]",
            KeyCode.Minus => "-",
            KeyCode.Equals => "=",
            KeyCode.BackQuote => "`",
            _ => key.ToString(),
        };
    }
}
