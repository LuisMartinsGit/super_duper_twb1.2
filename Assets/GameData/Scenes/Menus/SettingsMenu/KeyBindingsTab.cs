// KeyBindingsTab.cs
// Settings > Key Bindings. Code-built from the screen's own authored pieces, the way
// its HUD Scale row is, so it carries the authored look with no scene re-bake.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Input;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// The two tabs and the bindings page. Click a key, press the new one; Esc cancels.
    /// A change saves at once (KeyBindings), so Apply does not concern this page.
    /// </summary>
    public sealed class KeyBindingsTab : MonoBehaviour
    {
        const float TabHeight = 96f, TabWidth = 560f, TabGap = 28f, TabTop = 870f;
        const float PageHeight = 1576.8f, PageY = 10.8f;
        const float RowHeight = 116f, RowGapX = 60f, RowGapY = 10f, KeyWidth = 420f, Pad = 90f;

        static readonly Color TabOn = new Color(0.95f, 0.78f, 0.35f);
        static readonly Color TabOff = Color.white;

        GameObject _left, _right, _page;
        TMP_Text _status, _generalLabel, _keysLabel;
        readonly Dictionary<KeyAction, TMP_Text> _keyLabels = new Dictionary<KeyAction, TMP_Text>();
        KeyAction? _capturing;
        Array _allKeys;

        void Awake()
        {
            _left = Find("LeftColumn");
            _right = Find("RightColumn");
            var back = Find("BackButton");
            var labelSource = FindLabelSource();
            var status = Find("Status");
            _status = status != null ? status.GetComponent<TMP_Text>() : null;
            if (_left == null || _right == null || back == null || labelSource == null)
            {
                Debug.LogWarning("[Settings] the Key Bindings tab could not find the screen's pieces.");
                return;
            }

            var panel = (RectTransform)_left.transform.parent;
            _generalLabel = Tab(panel, back, "TabGeneral", Loc.T("GENERAL"), 1, () => Show(false));
            _keysLabel = Tab(panel, back, "TabKeys", Loc.T("KEY BINDINGS"), 0, () => Show(true));
            BuildPage(panel, back, labelSource);
            Show(false);
        }

        void OnDisable() => _capturing = null;

        GameObject Find(string node)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == node) return t.gameObject;
            return null;
        }

        /// <summary>The authored row label, whose font and size the page's rows take.</summary>
        TMP_Text FindLabelSource()
        {
            var row = Find("FullscreenRow");
            if (row == null) return null;
            foreach (var t in row.GetComponentsInChildren<TMP_Text>(true))
                if (t.gameObject.name == "Label") return t;
            return null;
        }

        /// <summary>A copy of the footer's Back button: its look, none of its wiring.</summary>
        static GameObject CloneButton(GameObject source, Transform parent, string name, Action onClick, out TMP_Text label)
        {
            var go = Instantiate(source, parent, false);
            go.name = name;
            var le = go.GetComponent<LayoutElement>();
            if (le != null) Destroy(le);
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            label = go.GetComponentInChildren<TMP_Text>(true);
            return go;
        }

        TMP_Text Tab(RectTransform panel, GameObject back, string name, string text, int fromRight, Action onClick)
        {
            var go = CloneButton(back, panel, name, onClick, out var label);
            if (label != null) label.text = text;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.965f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(TabWidth, TabHeight);
            rt.anchoredPosition = new Vector2(-fromRight * (TabWidth + TabGap), TabTop);
            return label;
        }

        void BuildPage(RectTransform panel, GameObject back, TMP_Text labelSource)
        {
            _page = new GameObject("KeyBindingsPage", typeof(RectTransform));
            var rt = (RectTransform)_page.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = new Vector2(0.035f, 0.5f);
            rt.anchorMax = new Vector2(0.965f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, PageHeight);
            rt.anchoredPosition = new Vector2(0f, PageY);

            // A copy of the columns' own plate, emptied, so the page sits on the same backing.
            var plate = Find("DisplayPlate");
            if (plate != null)
            {
                var bg = Instantiate(plate, rt, false);
                bg.name = "Plate";
                foreach (Transform child in bg.transform) child.gameObject.SetActive(false);
                foreach (var group in bg.GetComponents<LayoutGroup>()) group.enabled = false;
                foreach (var fitter in bg.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
                var br = (RectTransform)bg.transform;
                br.anchorMin = Vector2.zero;
                br.anchorMax = Vector2.one;
                br.offsetMin = br.offsetMax = Vector2.zero;
                br.SetAsFirstSibling();
            }

            int columns = 2, perColumn = (KeyBindings.All.Length + 1) / columns;
            for (int i = 0; i < KeyBindings.All.Length; i++)
            {
                int col = i / perColumn, row = i % perColumn;
                BuildRow(rt, back, labelSource, KeyBindings.All[i], col, row);
            }

            var reset = CloneButton(back, rt, "ResetKeys", ResetAll, out var resetLabel);
            if (resetLabel != null) resetLabel.text = Loc.T("RESET TO DEFAULTS");
            var rr = (RectTransform)reset.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(1f, 0f);
            rr.pivot = new Vector2(1f, 0f);
            rr.sizeDelta = new Vector2(TabWidth + 200f, TabHeight);
            rr.anchoredPosition = new Vector2(-Pad, Pad);

            var hint = Instantiate(labelSource, rt, false);
            hint.name = "KeysHint";
            hint.text = Loc.T("Click a key, then press the new one. Esc cancels. Changes save at once. " +
                              "Esc, 1-9, F1-F6, Shift, Ctrl and Alt are fixed.");
            hint.enableWordWrapping = true;
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            var hr = hint.rectTransform;
            hr.anchorMin = new Vector2(0f, 0f);
            hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0f, 0f);
            hr.offsetMin = new Vector2(Pad, Pad);
            hr.offsetMax = new Vector2(-(Pad * 2f + TabWidth + 200f), Pad + TabHeight * 1.6f);
        }

        void BuildRow(RectTransform page, GameObject back, TMP_Text labelSource, KeyBindings.Entry entry, int col, int row)
        {
            float colWidth = 0.5f;
            var go = new GameObject("Key_" + entry.Action, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(page, false);
            rt.anchorMin = new Vector2(col * colWidth, 1f);
            rt.anchorMax = new Vector2((col + 1) * colWidth, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(-(Pad + RowGapX * 0.5f), RowHeight);
            rt.anchoredPosition = new Vector2(Pad, -Pad - row * (RowHeight + RowGapY));

            var label = Instantiate(labelSource, rt, false);
            label.name = "Label";
            label.text = Loc.T(entry.Label);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = new Vector2(-(KeyWidth + RowGapX), 0f);

            KeyAction action = entry.Action;
            var key = CloneButton(back, rt, "KeyButton", () => BeginCapture(action), out var keyLabel);
            var kr = (RectTransform)key.transform;
            kr.anchorMin = kr.anchorMax = new Vector2(1f, 0.5f);
            kr.pivot = new Vector2(1f, 0.5f);
            kr.sizeDelta = new Vector2(KeyWidth, RowHeight - 16f);
            kr.anchoredPosition = Vector2.zero;
            if (keyLabel != null) _keyLabels[action] = keyLabel;
        }

        void Show(bool keys)
        {
            _capturing = null;
            ShowColumn(_left, !keys);
            ShowColumn(_right, !keys);
            if (_page != null) _page.SetActive(keys);
            if (_generalLabel != null) _generalLabel.color = keys ? TabOff : TabOn;
            if (_keysLabel != null) _keysLabel.color = keys ? TabOn : TabOff;
            if (keys) Refresh();
        }

        /// <summary>A column's contents, but never the screen title, which lives in one.</summary>
        void ShowColumn(GameObject column, bool show)
        {
            var title = Find("TitleLabel");
            foreach (Transform child in column.transform)
                if (title == null || !title.transform.IsChildOf(child))
                    child.gameObject.SetActive(show);
        }

        void Refresh()
        {
            foreach (var pair in _keyLabels)
                pair.Value.text = KeyBindings.NameOf(KeyBindings.Get(pair.Key));
        }

        void BeginCapture(KeyAction action)
        {
            Refresh();
            _capturing = action;
            if (_keyLabels.TryGetValue(action, out var label)) label.text = Loc.T("PRESS A KEY");
        }

        void ResetAll()
        {
            _capturing = null;
            KeyBindings.ResetAll();
            Refresh();
            Say(Loc.T("Keys reset to their defaults."));
        }

        void Say(string text) { if (_status != null) _status.text = text; }

        void Update()
        {
            if (_capturing == null || !UnityEngine.Input.anyKeyDown) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { _capturing = null; Refresh(); return; }

            _allKeys ??= Enum.GetValues(typeof(KeyCode));
            foreach (KeyCode key in _allKeys)
            {
                // The click that opened the capture is not an answer.
                if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) continue;
                if (!UnityEngine.Input.GetKeyDown(key)) continue;

                if (KeyBindings.IsReserved(key))
                {
                    Say(string.Format(Loc.T("{0} is fixed and cannot be bound."), KeyBindings.NameOf(key)));
                    return;
                }

                var swapped = KeyBindings.Set(_capturing.Value, key);
                _capturing = null;
                Refresh();
                Say(swapped.HasValue
                    ? string.Format(Loc.T("{0} was in use: {1} took the old key."),
                        KeyBindings.NameOf(key), Loc.T(LabelOf(swapped.Value)))
                    : "");
                return;
            }
        }

        static string LabelOf(KeyAction action)
        {
            foreach (var e in KeyBindings.All) if (e.Action == action) return e.Label;
            return action.ToString();
        }
    }
}
