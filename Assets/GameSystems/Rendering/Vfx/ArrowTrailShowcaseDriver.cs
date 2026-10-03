// ArrowTrailShowcaseDriver.cs
// The label layer for the Arrow Trails scenario: one caption floating over
// each firing lane, so a streak on screen can be matched to the tech that
// produced it without counting lanes from the left.
//
// It draws and nothing else. The shooting is ordinary combat between ordinary
// units — that is the point of the scenario, and a driver that faked the
// arrows would be testing itself rather than the game.
//
// Same OnGUI world-to-screen approach SpellShowcaseDriver uses for its grid
// captions, including the one-pixel shadow: white text over a bright sky is
// unreadable without it.
//
// It also carries the arrow-effect TUNING PANEL (top right): pick a tier
// (Stone / Iron / Veilstone / Veilsteel). Stone and Iron: trail width. The two
// projectile tiers: whole-tip and hit size, plus one button per part of the
// tip prefab — it selects that Particle System on the prefab so the Inspector
// edits it with its own controls; the next arrow is rebuilt from the edited
// prefab. Sliders write ArrowTrailTiers.asset (re-read every spawn / hit), so
// every change shows on that tier's next shot and is kept after Play mode.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class ArrowTrailShowcaseDriver : MonoBehaviour
    {
        public struct Lane
        {
            public Vector3 At;
            public string Title;
            public string Detail;
            public Color Tint;
        }

        private readonly List<Lane> _lanes = new();
        private GUIStyle _title;
        private GUIStyle _detail;
        private Rect _panel = new Rect(0f, 0f, 300f, 0f);
        private bool _panelPlaced;
        private bool _panelOpen = true;

        public void AddLane(Vector3 at, string title, string detail, Color tint)
            => _lanes.Add(new Lane { At = at, Title = title, Detail = detail, Tint = tint });

        /// <summary>Shift every caption when the scenario is re-centered onto
        /// the player-1 start — the labels are authored around the same local
        /// origin the entities are, and RecenterScenario only moves ECS data.</summary>
        public void Recenter(float ox, float oz)
        {
            for (int i = 0; i < _lanes.Count; i++)
            {
                var l = _lanes[i];
                l.At = new Vector3(l.At.x + ox, l.At.y, l.At.z + oz);
                _lanes[i] = l;
            }
        }

        private void OnGUI()
        {
            DrawTuningPanel();
            if (_lanes.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 15,
                    fontStyle = FontStyle.Bold,
                };
                _detail = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                };
            }

            for (int i = 0; i < _lanes.Count; i++)
            {
                var lane = _lanes[i];
                var sp = cam.WorldToScreenPoint(lane.At);
                if (sp.z <= 0f) continue;   // behind the camera

                float x = sp.x - 110f;
                float y = Screen.height - sp.y;
                Draw(new Rect(x, y, 220f, 20f), lane.Title, _title, lane.Tint);
                Draw(new Rect(x, y + 17f, 220f, 18f), lane.Detail, _detail,
                     new Color(0.85f, 0.85f, 0.85f));
            }
        }

        // ── tuning panel ────────────────────────────────────────────────

        private void DrawTuningPanel()
        {
            if (!_panelPlaced) { _panel.x = Screen.width - _panel.width - 12f; _panel.y = 60f; _panelPlaced = true; }
            _panel = GUILayout.Window(0x7A11, _panel, TuningWindow, "Arrow effects (live)", GUILayout.Width(_panel.width));
        }

        private static readonly string[] TierNames = { "Stone", "Iron", "Veilstone", "Veilsteel" };
        private int _tier = 2;

        private void TuningWindow(int id)
        {
            var arrows = TheWaningBorder.Core.Settings.ComponentConfig.Require<ArrowTrailTiersConfig>();
            _panelOpen = GUILayout.Toggle(_panelOpen, _panelOpen ? " hide" : " show");
            if (_panelOpen && arrows != null)
            {
                // Which arrow the panel is about.
                _tier = GUILayout.Toolbar(_tier, TierNames);
                bool changed = false;
                switch (_tier)
                {
                    case 0: changed |= Slider("Trail width", ref arrows.stoneTrailWidth, 0.02f, 2f); break;
                    case 1: changed |= Slider("Trail width", ref arrows.ironTrailWidth, 0.02f, 2f); break;
                    default:
                        var look = _tier == 2 ? arrows.veilstone : arrows.veilsteel;
                        if (look == null) { GUILayout.Label("No settings for this tier."); break; }
                        changed |= Slider("Tip (whole)", ref look.tipScale, 0.02f, 1f);
                        changed |= Slider("Hit", ref look.hitScale, 0.02f, 1f);
                        PartButtons(look.tipPrefab);
                        break;
                }
                if (changed) MarkDirty(arrows);
                GUILayout.Label("Applies to the next arrow / hit of that tier.");
            }
            GUI.DragWindow();
        }

        /// <summary>
        /// One button per particle system in the tier's tip prefab. Clicking it
        /// selects that part ON THE PREFAB, so the Inspector shows its own
        /// Particle System controls — every module, every curve. Edits are
        /// made to the prefab itself, so they persist, and the next arrow of
        /// the tier is rebuilt from it (ArrowTrailTiers watches for edits).
        /// </summary>
        private static void PartButtons(GameObject tipPrefab)
        {
#if UNITY_EDITOR
            if (tipPrefab == null) return;
            GUILayout.Label("Edit a part in the Inspector:");
            // Particle systems AND trail renderers — the trail and its shadow
            // are TrailRenderers, which a particle-only list misses.
            foreach (var t in tipPrefab.GetComponentsInChildren<Transform>(true))
            {
                bool particles = t.TryGetComponent<ParticleSystem>(out _);
                bool trail = t.TryGetComponent<TrailRenderer>(out _);
                if (!particles && !trail) continue;
                string label = t.name + (t == tipPrefab.transform ? " (root)" : "") + (trail ? "  [trail]" : "");
                if (GUILayout.Button(label))
                {
                    UnityEditor.Selection.activeObject = t.gameObject;
                    UnityEditor.EditorGUIUtility.PingObject(tipPrefab);
                }
            }
#endif
        }

        private static bool Slider(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(90f));
            GUILayout.Label(v.ToString("0.00"), GUILayout.Width(36f));
            GUILayout.EndHorizontal();
            if (Mathf.Approximately(v, value)) return false;
            value = v;
            return true;
        }

        private static void MarkDirty(Object asset)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(asset);   // keep the tuned values after Play mode
#endif
        }

        private void Draw(Rect r, string text, GUIStyle style, Color color)
        {
            var prev = style.normal.textColor;

            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);

            style.normal.textColor = color;
            GUI.Label(r, text, style);

            style.normal.textColor = prev;
        }
    }
}
