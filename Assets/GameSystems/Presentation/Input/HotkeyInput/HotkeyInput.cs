// HotkeyInput.cs
// The keyboard: mode keys, stop/hold, idle-builder cycle, formation cycle,
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
        // Cached — CycleIdleBuilders created an undisposed query on every
        // press of the idle-builder key.
        private static readonly ComponentType[] IdleBuilderQueryTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _idleBuilderQuery;

        // Last-cycled builder, so subsequent presses advance through the list
        // instead of re-selecting the same unit.
        private int _builderCycleIndex = -1;

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

            // Hold position for all selected units
            if (UnityEngine.Input.GetKeyDown(cfg.holdPosition))
            {
                _modes.Disarm();
                _orders.IssueHoldPositionToSelection();
            }

            // Cycle through idle builders (workers with no BuildOrder/RepairOrder)
            if (UnityEngine.Input.GetKeyDown(cfg.cycleIdleBuilders))
                CycleIdleBuilders();

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
        /// Selects the next idle builder of the local player faction (and
        /// centers the camera on it). An "idle" builder is one with the
        /// CanBuild component and no active BuildOrder or RepairOrder.
        /// Press repeatedly to cycle. (Spec: 'b' cycles through idle builders.)
        /// </summary>
        private void CycleIdleBuilders()
        {
            var query = _idleBuilderQuery.Get(_em, IdleBuilderQueryTypes);
            using var entities = query.ToEntityArray(Allocator.Temp);

            var idle = new List<Entity>();
            foreach (var e in entities)
            {
                if (_em.GetComponentData<FactionTag>(e).Value != GameSettings.LocalPlayerFaction)
                    continue;
                if (_em.HasComponent<BuildOrder>(e)) continue;
                if (_em.HasComponent<RepairOrder>(e)) continue;
                idle.Add(e);
            }

            if (idle.Count == 0) return;

            _builderCycleIndex = (_builderCycleIndex + 1) % idle.Count;
            var pick = idle[_builderCycleIndex];

            SelectionSystem.ClearSelection();
            SelectionSystem.AddToSelection(pick);

            var pos = _em.GetComponentData<LocalTransform>(pick).Position;
            CameraController.FocusOn(new Vector3(pos.x, pos.y, pos.z));
        }
    }
}
