using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Planning mode (Z key). Commands are queued visually and executed on a second
/// Z press or Enter; ESC cancels.
///
/// Was IMGUI: OnGUI built a GUIStyle per label per frame and drew markers with a
/// Unicode glyph. Now bound to an AUTHORED prefab (PlanningModeHUD.prefab) whose
/// markers are uGUI objects cloned from a template. The static API is unchanged;
/// RTSInputManager and PauseMenuPanel call it.
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
        BindNodes();
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

            // Queue the rest THROUGH THE ROUTER: a raw buffer append exists
            // only on this machine, so a committed plan's orders 2..N never
            // reached the other peer — its copy of the unit stopped after the
            // first order (2026-08-16 sweep, B7). IssueQueuedWaypoint
            // replicates each entry and activates the queue on every peer.
            for (int i = 1; i < cmds.Count; i++)
            {
                TheWaningBorder.Core.Commands.CommandRouter.IssueQueuedWaypoint(
                    em, unit, cmds[i].Type, cmds[i].Position, Entity.Null,
                    TheWaningBorder.Core.Commands.CommandSource.LocalPlayer);
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

    // ── Prefab binding ────────────────────────────────────────────────

    private RectTransform _root;
    private TMP_Text _title, _count;
    private GameObject _markerTemplate;
    private readonly List<GameObject> _markers = new List<GameObject>();
    private readonly Stack<GameObject> _markerPool = new Stack<GameObject>();

    private void BindNodes()
    {
        _root = GetComponent<RectTransform>();

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Title") _title = t.GetComponent<TMP_Text>();
            else if (t.name == "Count") _count = t.GetComponent<TMP_Text>();
            else if (t.name == "MarkerTemplate") _markerTemplate = t.gameObject;
        }

        if (_markerTemplate == null)
            Debug.LogError("[Planning] prefab has no 'MarkerTemplate' — no markers will show.");
        else
            _markerTemplate.SetActive(false);

        if (_title != null)
            _title.text = Loc.T("PLANNING MODE (Z to execute, ESC to cancel)");
    }

    private void LateUpdate()
    {
        // The CHILDREN are toggled, not this object: the static API has to keep
        // working while planning mode is idle, and a disabled component would
        // stop ticking.
        bool show = _isActive;
        if (_title != null) _title.gameObject.SetActive(show);
        if (_count != null) _count.gameObject.SetActive(show);

        if (!show) { ClearMarkers(); return; }

        if (_count != null)
            _count.text = string.Format(Loc.T("{0} command(s) queued"), _plans.Count);

        var cam = Camera.main;
        if (cam == null || _markerTemplate == null) { ClearMarkers(); return; }

        while (_markers.Count > _plans.Count) Release(_markers.Count - 1);

        for (int i = 0; i < _plans.Count; i++)
        {
            var plan = _plans[i];
            Vector3 screen = cam.WorldToScreenPoint(
                new Vector3(plan.Position.x, plan.Position.y + 0.5f, plan.Position.z));

            // Behind the camera reports a mirrored point; skip rather than draw
            // a marker where the order is not.
            if (screen.z <= 0f)
            {
                if (i < _markers.Count) _markers[i].SetActive(false);
                continue;
            }

            var marker = i < _markers.Count ? _markers[i] : Rent();
            marker.SetActive(true);
            marker.transform.position = new Vector3(screen.x, screen.y, 0f);

            var label = marker.GetComponentInChildren<TMP_Text>(true);
            if (label == null) continue;

            switch (plan.Type)
            {
                case QueuedCommandType.AttackMove: label.text = "A"; label.color = Color.red;   break;
                case QueuedCommandType.Patrol:     label.text = "P"; label.color = Color.cyan;  break;
                default:                           label.text = "M"; label.color = Color.green; break;
            }
        }
    }

    private GameObject Rent()
    {
        var go = _markerPool.Count > 0 ? _markerPool.Pop() : Instantiate(_markerTemplate, _root);
        go.name = "Marker";
        _markers.Add(go);
        return go;
    }

    private void Release(int index)
    {
        var go = _markers[index];
        go.SetActive(false);
        _markerPool.Push(go);
        _markers.RemoveAt(index);
    }

    private void ClearMarkers()
    {
        while (_markers.Count > 0) Release(_markers.Count - 1);
    }
}
