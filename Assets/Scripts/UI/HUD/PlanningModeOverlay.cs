// File: Assets/Scripts/UI/HUD/PlanningModeOverlay.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;

/// <summary>
/// Planning mode overlay (Z key). Commands are queued visually and executed
/// on second Z press or Enter. ESC cancels. BFME2-style planning mode.
/// </summary>
[DefaultExecutionOrder(950)]
public class PlanningModeOverlay : MonoBehaviour
{
    public struct PlanEntry
    {
        public Entity Unit;
        public QueuedCommandType Type;
        public float3 Position;
    }

    private static PlanningModeOverlay _instance;
    private static bool _isActive;
    private static List<PlanEntry> _plans = new List<PlanEntry>();

    public static bool IsActive => _isActive;

    private void Awake()
    {
        _instance = this;
        _isActive = false;
        _plans.Clear();
    }

    public static void Toggle()
    {
        _isActive = !_isActive;
        if (!_isActive)
            _plans.Clear();
    }

    public static void Cancel()
    {
        _isActive = false;
        _plans.Clear();
    }

    public static void AddPlan(Entity unit, QueuedCommandType type, float3 position)
    {
        _plans.Add(new PlanEntry { Unit = unit, Type = type, Position = position });
    }

    /// <summary>
    /// Execute all planned commands. First command per unit is issued directly,
    /// subsequent commands are queued via the command queue buffer.
    /// </summary>
    public static void ExecuteAll(EntityManager em)
    {
        // Group plans by unit
        var perUnit = new Dictionary<Entity, List<PlanEntry>>();
        foreach (var plan in _plans)
        {
            if (!em.Exists(plan.Unit)) continue;
            if (!perUnit.ContainsKey(plan.Unit))
                perUnit[plan.Unit] = new List<PlanEntry>();
            perUnit[plan.Unit].Add(plan);
        }

        foreach (var kvp in perUnit)
        {
            var unit = kvp.Key;
            var cmds = kvp.Value;
            if (cmds.Count == 0) continue;

            // Issue first command directly
            IssueCommand(em, unit, cmds[0]);

            // Queue the rest
            if (cmds.Count > 1)
            {
                if (!em.HasBuffer<QueuedCommand>(unit))
                    em.AddBuffer<QueuedCommand>(unit);
                var buffer = em.GetBuffer<QueuedCommand>(unit);
                for (int i = 1; i < cmds.Count; i++)
                {
                    buffer.Add(new QueuedCommand
                    {
                        Type = cmds[i].Type,
                        TargetPosition = cmds[i].Position,
                        TargetEntity = Entity.Null
                    });
                }
                if (!em.HasComponent<CommandQueueActive>(unit))
                    em.AddComponent<CommandQueueActive>(unit);
            }
        }

        _isActive = false;
        _plans.Clear();
    }

    private static void IssueCommand(EntityManager em, Entity unit, PlanEntry plan)
    {
        switch (plan.Type)
        {
            case QueuedCommandType.Move:
                CommandRouter.IssueMove(em, unit, plan.Position, CommandSource.LocalPlayer);
                break;
            case QueuedCommandType.AttackMove:
                CommandRouter.IssueAttackMove(em, unit, plan.Position, CommandSource.LocalPlayer);
                break;
            case QueuedCommandType.Patrol:
                CommandRouter.IssuePatrol(em, unit, plan.Position, CommandSource.LocalPlayer);
                break;
        }
    }

    private void OnGUI()
    {
        if (!_isActive) return;

        // Draw "PLANNING MODE" label
        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter
        };
        labelStyle.normal.textColor = new Color(1f, 0.8f, 0f, 1f);

        GUI.Label(new Rect(0, 40, Screen.width, 40), "PLANNING MODE (Z to execute, ESC to cancel)", labelStyle);

        // Draw planned waypoint count
        var countStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.UpperCenter
        };
        countStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(0, 70, Screen.width, 30), $"{_plans.Count} command(s) queued", countStyle);

        // Draw waypoint markers in world space
        var cam = Camera.main;
        if (cam == null) return;

        foreach (var plan in _plans)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(new Vector3(plan.Position.x, plan.Position.y + 0.5f, plan.Position.z));
            if (screenPos.z <= 0) continue;

            float guiY = Screen.height - screenPos.y;

            // Color by command type
            Color markerColor;
            string label;
            switch (plan.Type)
            {
                case QueuedCommandType.AttackMove:
                    markerColor = Color.red;
                    label = "A";
                    break;
                case QueuedCommandType.Patrol:
                    markerColor = Color.cyan;
                    label = "P";
                    break;
                default:
                    markerColor = Color.green;
                    label = "M";
                    break;
            }

            // Draw marker circle
            GUI.color = markerColor;
            var markerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            markerStyle.normal.textColor = markerColor;
            GUI.Label(new Rect(screenPos.x - 12, guiY - 12, 24, 24), "◉", markerStyle);

            // Draw type label
            var typeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            typeStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(screenPos.x - 8, guiY + 10, 16, 16), label, typeStyle);

            GUI.color = Color.white;
        }
    }
}
