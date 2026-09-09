// LedgerAbilityShowcase.cs
// Test-scene driver: walks the shared ability list on a timer and casts each
// entry from the live unit, with a readout of what is firing.
//
// The LIST and the CASTING both live in AbilityShowcase, which the sandbox's
// Abilities tab uses too — this file is only the clock and the label. It used
// to build its own copy of the list; two places deriving the same 120 entries
// from the same tables is one place too many.
//
// Lives on the test director rather than on the unit, so a run continues
// across deaths and respawns instead of restarting from the top each round.

using UnityEngine;
using TheWaningBorder.Abilities.Vfx;

namespace TheWaningBorder.Rendering
{
    public class LedgerAbilityShowcase : MonoBehaviour
    {
        [Header("Pacing")]
        [Tooltip("Seconds between casts.")]
        public float interval = 1.6f;

        [Tooltip("Start casting this long after the scene begins, so the idle " +
                 "and the first steps are visible first.")]
        public float startDelay = 3f;

        [Header("Target")]
        [Tooltip("Cast from here. The director points this at the live unit and " +
                 "re-points it after every respawn.")]
        public Transform caster;

        [Header("Readout")]
        public bool showLabel = true;

        private int _index = -1;
        private float _next;
        private GUIStyle _style;
        private string _current = "";

        private void Start() => _next = Time.time + startDelay;

        private void Update()
        {
            var entries = AbilityShowcase.Entries;
            if (entries.Count == 0 || Time.time < _next) return;
            _next = Time.time + Mathf.Max(0.2f, interval);

            _index = (_index + 1) % entries.Count;
            var e = entries[_index];

            AbilityShowcase.Cast(e, caster != null ? caster.position : transform.position);
            _current = $"{_index + 1}/{entries.Count}   {e.Label}\n{e.Detail}";
        }

        private void OnGUI()
        {
            if (!showLabel || string.IsNullOrEmpty(_current)) return;
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                normal = { textColor = Color.white },
            };
            var r = new Rect(24f, 24f, Screen.width - 48f, 90f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 14f, r.y + 8f, r.width - 28f, r.height - 16f), _current, _style);
        }
    }
}
