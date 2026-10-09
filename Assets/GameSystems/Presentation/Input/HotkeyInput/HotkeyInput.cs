// HotkeyInput.cs
// The keyboard: mode keys, stop, stances, idle-worker cycle, formation cycle,
// planning mode, control groups. Every binding comes from HotkeyInput.asset.
// Part of: Input/ — split out of RTSInputManager.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core.Commands.Issuing;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.UI.Ingame;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Reads the keys and turns each into one call on something else — the
    /// aiming modes, the order layer, the formation state, the control
    /// groups. It decides WHICH key was pressed and nothing about what a
    /// command means; that lives in
    /// <see cref="TheWaningBorder.Core.Commands.Issuing.SelectionOrders"/>.
    ///
    /// ESC is deliberately absent: PauseMenuPanel owns the whole cascade from
    /// a component that is never gated by pointer-over-UI, and calls back into
    /// <see cref="RTSInputManager.CancelModesOrSelection"/>.
    /// </summary>
    public sealed class HotkeyInput
    {
        private readonly EntityManager _em;
        private readonly SelectionOrders _orders;
        private readonly InputModes _modes;
        private readonly FormationInput _formation;

        private HotkeyInputConfig _cfg;
        private HotkeyInputConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<HotkeyInputConfig>());

        public HotkeyInput(EntityManager em, SelectionOrders orders,
                           InputModes modes, FormationInput formation)
        {
            _em = em;
            _orders = orders;
            _modes = modes;
            _formation = formation;
        }

        public void Tick()
        {
            var cfg = Cfg;
            if (cfg == null) return;   // the error is already logged by Require

            // Arms attack-move
            if (UnityEngine.Input.GetKeyDown(cfg.attackMove))
                _modes.ArmAttackMove();

            // Arms patrol
            if (UnityEngine.Input.GetKeyDown(cfg.patrol))
                _modes.ArmPatrol();

            // Cycle formation shape (Box -> Line -> Wedge -> Staggered)
            if (UnityEngine.Input.GetKeyDown(cfg.cycleFormation))
                _formation.Cycle();

            // Stop all selected units
            if (UnityEngine.Input.GetKeyDown(cfg.stop))
            {
                _modes.Disarm();
                _orders.IssueStopToSelection();
            }

            // Stances (docs/Design/Stances.md §6). Hold also stops the unit,
            // so it disarms any aiming mode like Stop does; the other two are
            // modes and leave the current order alone.
            if (UnityEngine.Input.GetKeyDown(cfg.holdPosition))
            {
                _modes.Disarm();
                _orders.IssueHoldPositionToSelection();
            }
            if (UnityEngine.Input.GetKeyDown(cfg.aggressiveStance))
                _orders.IssueStanceToSelection(UnitStanceMode.Aggressive);
            if (UnityEngine.Input.GetKeyDown(cfg.defensiveStance))
                _orders.IssueStanceToSelection(UnitStanceMode.Defensive);

            // Cycle through idle workers (workers with no BuildOrder/RepairOrder)
            if (UnityEngine.Input.GetKeyDown(cfg.cycleIdleWorkers))
                CycleIdleWorkers();

            // Toggle planning mode (BFME2); the execute key also fires it
            if (UnityEngine.Input.GetKeyDown(cfg.planningMode))
            {
                if (PlanningModeOverlay.IsActive)
                    PlanningModeOverlay.ExecuteAll(_em);
                else
                    PlanningModeOverlay.Toggle();
            }
            if (PlanningModeOverlay.IsActive && UnityEngine.Input.GetKeyDown(cfg.planningExecute))
                PlanningModeOverlay.ExecuteAll(_em);

            HandleControlGroups(cfg);
        }

        // Control groups: one key per group, running up from controlGroupFirst.
        // The Ctrl / Shift modifiers stay in code — see the config's remarks.
        private void HandleControlGroups(HotkeyInputConfig cfg)
        {
            for (int i = 0; i < cfg.controlGroupCount; i++)
            {
                if (!UnityEngine.Input.GetKeyDown(cfg.controlGroupFirst + i)) continue;

                bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl)
                         || UnityEngine.Input.GetKey(KeyCode.RightControl);
                bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift)
                          || UnityEngine.Input.GetKey(KeyCode.RightShift);

                if (ctrl)
                    ControlGroupSystem.AssignGroup(i);
                else if (shift)
                    ControlGroupSystem.AddToGroup(i);
                else
                    ControlGroupSystem.HandleRecallOrCenter(i);

                break;
            }
        }

        /// <summary>
        /// Selects the next idle worker of the local player faction and
        /// centres the camera on it — IdleWorkerCycler, shared with the HUD's
        /// idle-builder button. Press repeatedly to cycle.
        /// </summary>
        private void CycleIdleWorkers() => IdleWorkerCycler.SelectNext(_em);
    }
}
