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
