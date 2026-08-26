// DebugLogOverlay.cs
// On-screen log console for BUILDS (2026-08-09).
//
// Why this exists: a player build that stalls during load gives you nothing —
// no console, no Editor window, and the loading screen just sits there. The
// skirmish "Building world..." hang was an ArgumentNullException thrown inside
// GameBootstrap.InitializeWorld; a throwing coroutine dies silently, so the
// screen froze with no visible cause and the only record was Player.log on
// disk. This puts that record on screen, in the build, as it happens.
//
// Deliberately IMGUI: OnGUI renders with no camera, no canvas and no UI
// prefab wired, which is exactly the situation during bootstrap and exactly
// when the uGUI HUD cannot help. GameBootstrap's BOOT PHASE readout uses the
// same trick.
//
// Behaviour:
//   * F1 toggles. Shift+F1 clears.
//   * Auto-opens the first time an Error/Exception/Assert arrives, so a hang
//     shows its cause without the player knowing any hotkey.
//   * Keeps the last MaxEntries messages in a ring; errors keep their stack.
//   * "Copy all" puts the buffer on the system clipboard for pasting into a
//     bug report.

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    public sealed class DebugLogOverlay : MonoBehaviour
    {
        private const KeyCode ToggleKey = KeyCode.F1;
        private const int MaxEntries = 400;
        private const int MaxStackLines = 12;

        private struct Entry
        {
            public string Stamp;
            public string Message;
            public string Stack;
            public LogType Type;
        }

        // Unity raises the threaded callback from job threads too, so the
        // buffer is written under a lock and only read on the main thread.
        private static readonly object Gate = new object();
        private static readonly Queue<Entry> Pending = new Queue<Entry>();

        private readonly List<Entry> _entries = new List<Entry>(MaxEntries);
        private Vector2 _scroll;
        private bool _visible;
        private bool _autoShown;
        private int _errorCount;
        private int _warningCount;
        private bool _followTail = true;

        private GUIStyle _row;
        private GUIStyle _panel;

        // ─── Mount ─────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Mount()
        {
            var go = new GameObject("[Debug Log Overlay]");
            go.AddComponent<DebugLogOverlay>();
            DontDestroyOnLoad(go);
        }

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }

        // Threaded: do the minimum here and hand off to Update.
        private static void OnLog(string message, string stack, LogType type)
        {
            var entry = new Entry
            {
                Stamp = System.DateTime.Now.ToString("HH:mm:ss.fff"),
                Message = message,
                Stack = IsProblem(type) ? Trim(stack) : null,
                Type = type,
            };
            lock (Gate)
            {
                Pending.Enqueue(entry);
                // Bound the hand-off queue too: a per-frame exception (the
                // cloud-projector one fired every Update) would otherwise grow
                // it without limit whenever the main thread is stalled.
                while (Pending.Count > MaxEntries) Pending.Dequeue();
            }
        }

        private static bool IsProblem(LogType type)
            => type == LogType.Error || type == LogType.Exception || type == LogType.Assert;

        private static string Trim(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return null;
            var lines = stack.Split('\n');
            if (lines.Length <= MaxStackLines) return stack.TrimEnd();
            return string.Join("\n", lines, 0, MaxStackLines).TrimEnd()
                   + "\n  ... (" + (lines.Length - MaxStackLines) + " more)";
        }

        private void Update()
        {
            lock (Gate)
            {
                while (Pending.Count > 0)
                {
                    var e = Pending.Dequeue();
                    _entries.Add(e);
                    if (e.Type == LogType.Warning) _warningCount++;
                    else if (IsProblem(e.Type)) _errorCount++;

                    if (IsProblem(e.Type) && !_autoShown)
                    {
                        _autoShown = true;
                        _visible = true;
                    }
                }
                while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }

            // Bare `Input` would bind to the TheWaningBorder.Input NAMESPACE.
            if (UnityEngine.Input.GetKeyDown(ToggleKey))
            {
                if (UnityEngine.Input.GetKey(KeyCode.LeftShift)
                    || UnityEngine.Input.GetKey(KeyCode.RightShift))
                {
                    _entries.Clear();
                    _errorCount = _warningCount = 0;
                }
                else _visible = !_visible;
            }
        }

        // ─── Draw ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();

            if (!_visible)
            {
                // Silent builds should still advertise the key, and a stalled
                // load should shout that something already went wrong.
                string hint = _errorCount > 0
                    ? "F1: log console (" + _errorCount + " error"
                      + (_errorCount == 1 ? "" : "s") + ")"
                    : "F1: log console";
                var prev = GUI.color;
                GUI.color = _errorCount > 0 ? new Color(1f, 0.5f, 0.4f) : new Color(1f, 1f, 1f, 0.5f);
                GUI.Label(new Rect(10f, Screen.height - 24f, 420f, 20f), hint);
                GUI.color = prev;
                return;
            }

            float w = Screen.width - 20f;
            float h = Mathf.Min(Screen.height * 0.6f, 620f);
            var area = new Rect(10f, 10f, w, h);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));

            GUILayout.BeginHorizontal();
            GUILayout.Label("LOG CONSOLE   errors " + _errorCount
                            + "   warnings " + _warningCount
                            + "   boot phase: " + TheWaningBorder.Core.MatchLifecycle.BootPhase,
                            GUILayout.ExpandWidth(true));
            _followTail = GUILayout.Toggle(_followTail, "Follow", GUILayout.Width(70f));
            if (GUILayout.Button("Copy all", GUILayout.Width(80f))) CopyAll();
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                _entries.Clear();
                _errorCount = _warningCount = 0;
            }
            if (GUILayout.Button("Close", GUILayout.Width(60f))) _visible = false;
            GUILayout.EndHorizontal();

            if (_followTail) _scroll.y = float.MaxValue;
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                _row.normal.textColor = ColorFor(e.Type);
                GUILayout.Label(e.Stamp + "  " + e.Message, _row);
                if (!string.IsNullOrEmpty(e.Stack))
                {
                    _row.normal.textColor = new Color(0.75f, 0.75f, 0.8f);
                    GUILayout.Label(e.Stack, _row);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Label("F1 close   Shift+F1 clear");
            GUILayout.EndArea();
        }

        private static Color ColorFor(LogType type) => type switch
        {
            LogType.Exception => new Color(1f, 0.42f, 0.38f),
            LogType.Error     => new Color(1f, 0.42f, 0.38f),
            LogType.Assert    => new Color(1f, 0.6f, 0.35f),
            LogType.Warning   => new Color(1f, 0.85f, 0.45f),
            _                 => new Color(0.86f, 0.88f, 0.92f),
        };

        private void EnsureStyles()
        {
            if (_row != null) return;
            _row = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = false,
                padding = new RectOffset(2, 2, 0, 0),
            };
            _panel = new GUIStyle(GUI.skin.box);
        }

        private void CopyAll()
        {
            var sb = new StringBuilder(4096);
            sb.Append("boot phase: ")
              .Append(TheWaningBorder.Core.MatchLifecycle.BootPhase)
              .Append('\n');
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                sb.Append(e.Stamp).Append("  [").Append(e.Type).Append("]  ")
                  .Append(e.Message).Append('\n');
                if (!string.IsNullOrEmpty(e.Stack)) sb.Append(e.Stack).Append('\n');
            }
            GUIUtility.systemCopyBuffer = sb.ToString();
        }
    }
}
