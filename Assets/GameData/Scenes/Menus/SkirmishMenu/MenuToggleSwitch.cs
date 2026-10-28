// MenuToggleSwitch.cs
// A boolean menu option drawn with the Synty "Interface Fantasy Menus"
// TOGGLE prefab (Button_FantasyMenus_Toggle_01: dark plate, blue fill that
// fades in when on, gold frame, hover animation), wrapped so the lobby
// screens keep owning the state.
//
// The authored option cells (SkirmishMenu.unity, MultiplayerMenu.unity) still
// carry a plain Button "Track" plus an ON/OFF "State" text, built before the
// kit had a toggle. Replace() swaps that pair for one Synty toggle at runtime
// - the scene references stay valid, nothing is rebound - and the ON/OFF word
// moves onto the toggle's own label. When a screen has no toggle prefab wired,
// the pair stays and this component drives it instead (colour swap), so an
// undressed screen still reads and still clicks.
//
// Lives with SkirmishMenu.unity, its first consumer; the multiplayer lobby,
// StartCultureOption and the code-built OBSERVER option share it.

using System;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    [DisallowMultipleComponent]
    public sealed class MenuToggleSwitch : MonoBehaviour
    {
        /// <summary>The Synty uGUI toggle, when the screen was dressed.</summary>
        public Toggle Toggle;
        /// <summary>The authored Button track, when it was not.</summary>
        public Button Button;
        /// <summary>The ON / OFF word: the Synty label, or the authored State text.</summary>
        public TMP_Text Label;
        /// <summary>The authored track image, painted on / off when undressed.</summary>
        public Image Plain;

        // Undressed palette: the menu's gold when on, the plate's dark when off.
        public static readonly Color PlainOn  = new Color(0.690f, 0.525f, 0.173f);
        public static readonly Color PlainOff = new Color(0.086f, 0.118f, 0.141f);

        private bool _on;

        /// <summary>Raised after a CLICK flips the value; never by <see cref="SetOn"/>.</summary>
        public event Action<bool> Changed;

        public bool IsOn => _on;

        public bool Interactable
        {
            get => Toggle != null ? Toggle.interactable : Button == null || Button.interactable;
            set
            {
                if (Toggle != null) Toggle.interactable = value;
                if (Button != null) Button.interactable = value;
            }
        }

        /// <summary>Show the value without raising <see cref="Changed"/>.</summary>
        public void SetOn(bool on)
        {
            _on = on;
            if (Label != null) Label.text = Loc.T(on ? "ON" : "OFF");
            if (Toggle != null) Toggle.SetIsOnWithoutNotify(on);
            if (Plain != null) Plain.color = on ? PlainOn : PlainOff;
        }

        private void OnToggled(bool on)
        {
            SetOn(on);
            Changed?.Invoke(on);
        }

        private void OnClicked() => OnToggled(!_on);

        /// <summary>
        /// Dress an authored option: put one Synty toggle where the
        /// <paramref name="track"/> Button sits (same parent, same place in the
        /// row), sized to fill the row, and hide the track and its
        /// <paramref name="state"/> text. With no <paramref name="prefab"/> the
        /// authored pair is kept and driven as is. Idempotent per track.
        /// </summary>
        public static MenuToggleSwitch Replace(Button track, TMP_Text state, GameObject prefab)
        {
            if (track == null) return null;

            var existing = track.GetComponent<MenuToggleSwitch>();
            if (existing != null) return existing;
            var row = track.transform.parent;
            if (row != null)
            {
                var dressed = row.GetComponentInChildren<MenuToggleSwitch>(true);
                if (dressed != null && dressed.Toggle != null) return dressed;
            }

            if (prefab == null || row == null)
                return DriveAuthored(track, state);

            var go = UnityEngine.Object.Instantiate(prefab, row, false);
            go.name = "Toggle";
            go.transform.SetSiblingIndex(track.transform.GetSiblingIndex());
            var toggle = go.GetComponent<Toggle>();
            if (toggle == null)
            {
                Debug.LogError($"[MenuToggleSwitch] \"{prefab.name}\" has no Toggle on its root - " +
                               "not a Synty toggle prefab. Keeping the authored track.", track);
                UnityEngine.Object.Destroy(go);
                return DriveAuthored(track, state);
            }

            // The Synty prefab groups its toggle with allowSwitchOff = false,
            // which would make the option impossible to turn OFF by clicking.
            toggle.group = null;
            foreach (var group in go.GetComponentsInChildren<ToggleGroup>(true))
                UnityEngine.Object.Destroy(group);

            // Fill the row the track used to share with the State text; the
            // height is the track's own, so the row keeps its authored size.
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            var trackLe = track.GetComponent<LayoutElement>();
            float h = trackLe != null && trackLe.minHeight > 0f ? trackLe.minHeight : 52f;
            le.minHeight = le.preferredHeight = h;
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;

            // The ON / OFF word rides on the toggle's label, in the lobby's
            // own face, size and colour (the Synty label is 46pt for a 320 px
            // plate).
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.raycastTarget = false;
                if (state != null)
                {
                    label.font = state.font;
                    label.fontSize = state.fontSize;
                    label.fontStyle = state.fontStyle;
                    label.color = state.color;
                }
                var lrt = label.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(12f, 0f);
                lrt.offsetMax = new Vector2(-12f, 0f);
            }

            track.gameObject.SetActive(false);
            if (state != null) state.gameObject.SetActive(false);

            var sw = go.AddComponent<MenuToggleSwitch>();
            sw.Toggle = toggle;
            sw.Label = label;
            toggle.onValueChanged.AddListener(sw.OnToggled);
            sw.SetOn(false);
            return sw;
        }

        /// <summary>Drive the authored pair as it is (no prefab wired).</summary>
        private static MenuToggleSwitch DriveAuthored(Button track, TMP_Text state)
        {
            var sw = track.gameObject.AddComponent<MenuToggleSwitch>();
            sw.Button = track;
            sw.Label = state;
            sw.Plain = track.targetGraphic as Image;
            // The authored tracks shipped with raycastTarget off, which is
            // why the fog and curse options never answered a click.
            if (sw.Plain != null) sw.Plain.raycastTarget = true;
            track.onClick.AddListener(sw.OnClicked);
            sw.SetOn(false);
            return sw;
        }

        /// <summary>
        /// A second option built to match <paramref name="styleRow"/>: the
        /// whole row (State text + Track button, or the dressed toggle) is
        /// cloned under <paramref name="parent"/>, so a code-built option
        /// has exactly the authored one's size, spacing and look.
        /// </summary>
        public static MenuToggleSwitch CloneRow(Transform styleRow, Transform parent, GameObject prefab, string name)
        {
            if (styleRow == null || parent == null) return null;
            var row = UnityEngine.Object.Instantiate(styleRow.gameObject, parent, false);
            row.name = name;

            // A clone of an already-dressed row carries the dressed toggle
            // (with a dead copy of its wrapper) and the hidden pair; strip
            // the copy and dress the pair afresh so the listeners are ours.
            // Immediate, or Replace() below would still find the copy.
            foreach (var stale in row.GetComponentsInChildren<MenuToggleSwitch>(true))
            {
                if (stale.Toggle != null) UnityEngine.Object.DestroyImmediate(stale.Toggle.gameObject);
                else UnityEngine.Object.DestroyImmediate(stale);
            }

            Button track = null;
            TMP_Text state = null;
            foreach (var b in row.GetComponentsInChildren<Button>(true))
                if (b.GetComponent<Toggle>() == null) { track = b; break; }
            foreach (var t in row.GetComponentsInChildren<TMP_Text>(true))
                if (t.name == "State") { state = t; break; }
            if (track == null)
            {
                Debug.LogError($"[MenuToggleSwitch] option row \"{styleRow.name}\" has no Track " +
                               "Button to clone - the option cell's structure drifted.", styleRow);
                UnityEngine.Object.Destroy(row);
                return null;
            }
            track.gameObject.SetActive(true);
            if (state != null) state.gameObject.SetActive(true);
            track.onClick.RemoveAllListeners();
            return Replace(track, state, prefab);
        }
    }
}
