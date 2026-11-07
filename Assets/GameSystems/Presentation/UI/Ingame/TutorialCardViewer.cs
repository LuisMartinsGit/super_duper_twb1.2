// TutorialCardViewer.cs
// Shows the infographic cards (docs/Design/Tutorial_Cards_And_Story.md): by itself when
// a tutorial step becomes the suggestion, and on request as the Field Guide in the pause
// menu. The cards are images built by tools/tutorial_cards/gen_cards.py, loaded on demand
// from Resources/UI/TutorialCards/<language>/, because a catalog reference would hold every
// card in memory for the whole match.
//
// It never gates the player: one click anywhere outside its buttons moves on, and the last
// card closes it. Nothing here touches the simulation.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.Ingame
{
    public sealed class TutorialCardViewer : MonoBehaviour
    {
        /// <summary>Every card the Field Guide offers, in reading order.</summary>
        private static readonly string[] FieldGuide =
        {
            "01", "02", "03", "04", "05", "06", "07", "08", "09",
            "14", "15", "11", "12", "13", "10", "16", "17",
            "18", "19", "20", "21", "22", "23", "24", "25", "26",
            "T1", "T2", "T3", "T4", "T5",
        };

        private const int SortingOrder = 300;
        private const float ButtonWidth = 240f;
        private const float ButtonHeight = 62f;

        private static TutorialCardViewer _instance;

        /// <summary>The player asked the tutorial to stop showing cards.</summary>
        public static bool AutoOff { get; set; }

        private readonly List<string> _cards = new List<string>();
        private int _at;
        private bool _auto;
        private Image _image;
        private TMP_Text _counter;
        private GameObject _previous, _stop;
        private TMP_Text _nextLabel;

        /// <summary>Show these cards now, or after the ones already waiting.</summary>
        public static void ShowAuto(Transform parent, IList<string> ids)
        {
            if (AutoOff || ids == null || ids.Count == 0) return;
            var v = Ensure(parent);
            bool wasOpen = v.gameObject.activeSelf && v._auto;
            if (!wasOpen) { v._cards.Clear(); v._at = 0; v._auto = true; }
            for (int i = 0; i < ids.Count; i++) if (!v._cards.Contains(ids[i])) v._cards.Add(ids[i]);
            v.Refresh();
        }

        /// <summary>Open the whole Field Guide from the first card.</summary>
        public static void OpenFieldGuide(Transform parent)
        {
            var v = Ensure(parent);
            v._cards.Clear();
            v._cards.AddRange(FieldGuide);
            v._at = 0;
            v._auto = false;
            v.Refresh();
        }

        private static TutorialCardViewer Ensure(Transform parent)
        {
            if (_instance != null) return _instance;
            var rt = GameUIKit.Rect(parent, "TutorialCardViewer");
            GameUIKit.Stretch(rt);
            // Its own canvas, so it sits above the pause menu; a nested canvas
            // takes no clicks without its own raycaster.
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            rt.gameObject.AddComponent<GraphicRaycaster>();
            _instance = rt.gameObject.AddComponent<TutorialCardViewer>();
            _instance.Build(rt);
            return _instance;
        }

        private void Build(RectTransform rt)
        {
            var scrim = GameUIKit.Image(rt, "scrim", new Color(0f, 0f, 0f, 0.78f), raycast: true);
            GameUIKit.Stretch(scrim.rectTransform);
            UITooltip.Relay(scrim.gameObject).OnLeftClick = Next;

            _image = GameUIKit.Image(rt, "card", Color.white);
            _image.preserveAspect = true;
            _image.raycastTarget = false;
            var cr = _image.rectTransform;
            cr.anchorMin = new Vector2(0.08f, 0.13f);
            cr.anchorMax = new Vector2(0.92f, 0.97f);
            cr.offsetMin = cr.offsetMax = Vector2.zero;

            var row = GameUIKit.Rect(rt, "buttons");
            row.anchorMin = new Vector2(0.5f, 0f);
            row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, 24f);
            row.sizeDelta = new Vector2(ButtonWidth * 4f + 180f + 60f, ButtonHeight);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            _counter = GameUIKit.Text(row, "counter", "", 26f, GameUIKit.TextDim, TextAlignmentOptions.Center, wrap: false);
            _counter.rectTransform.sizeDelta = new Vector2(150f, ButtonHeight);
            _previous = MakeButton(row, "previous", Loc.T("Previous"), Previous).gameObject;
            _nextLabel = MakeButton(row, "next", Loc.T("Next"), Next);
            MakeButton(row, "close", Loc.T("Close"), Close);
            _stop = MakeButton(row, "stop", Loc.T("Stop showing cards"), () => { AutoOff = true; Close(); }).gameObject;
        }

        private static TMP_Text MakeButton(Transform parent, string name, string label, System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            var text = GameUIKit.Text(rt, "label", label, 24f, GameUIKit.TextMain, TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);
            UITooltip.Relay(bg.gameObject).OnLeftClick = click;
            return text;
        }

        private void Next()
        {
            if (_at + 1 >= _cards.Count) { Close(); return; }
            _at++;
            Refresh();
        }

        private void Previous()
        {
            if (_at <= 0) return;
            _at--;
            Refresh();
        }

        private void Close() => gameObject.SetActive(false);

        private void Refresh()
        {
            gameObject.SetActive(true);
            string lang = Loc.IsPortuguese ? "pt" : "en";
            string id = _cards[_at];
            var sprite = Resources.Load<Sprite>($"UI/TutorialCards/{lang}/card_{id}");
            if (sprite == null) Debug.LogError($"[GameUI] Tutorial card {lang}/{id} is missing.");
            _image.sprite = sprite;
            _image.enabled = sprite != null;
            _counter.text = _cards.Count > 1 ? $"{_at + 1} / {_cards.Count}" : "";
            _previous.SetActive(_at > 0);
            _nextLabel.transform.parent.gameObject.SetActive(_at + 1 < _cards.Count);
            _stop.SetActive(_auto);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
