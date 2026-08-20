// PlayerNamePrompt.cs
// One-time "what should we call you?" card, shown on the very first run.
// Location: Assets/Scripts/UI/Menus/PlayerNamePrompt.cs

using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Asks for the player's name once, the first time the game runs on this
    /// machine, and writes it to <see cref="PlayerProfile"/>.
    ///
    /// Built at RUNTIME on its own overlay canvas, for the same reason
    /// ColorPickerPopup is: the menu scene is authored and a widget added to a
    /// builder would never appear in the running game. Its own canvas rather
    /// than the menu's, so it does not depend on that scene's hierarchy and
    /// draws over everything regardless of what is on screen.
    ///
    /// Shown only when <see cref="PlayerProfile.IsFirstRun"/> — the session
    /// that created settings.json. Delete that file and the game asks again,
    /// which is also the manual way to test this.
    /// </summary>
    public sealed class PlayerNamePrompt : MonoBehaviour
    {
        private const int SortOrder = 5000;   // over the menu, under nothing

        private TMP_InputField _field;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only at the menu: asking a player their name over a loading
            // screen or mid-match would be worse than not asking.
            if (scene.name != MainMenuBootstrap.MenuSceneName) return;
            if (!PlayerProfile.IsFirstRun) return;

            // Once per run, not once per return to the menu.
            PlayerProfile.Save();
            Show();
        }

        private static void Show()
        {
            if (FindAnyObjectByType<PlayerNamePrompt>() != null) return;

            var rootGo = new GameObject("PlayerNamePrompt",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                typeof(PlayerNamePrompt));
            DontDestroyOnLoad(rootGo);

            var canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortOrder;

            var scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight = 1f;   // match HEIGHT, like the menu canvas

            var prompt = rootGo.GetComponent<PlayerNamePrompt>();
            prompt.Build((RectTransform)rootGo.transform);
        }

        private void Build(RectTransform canvasRt)
        {
            // Full-screen dim that also swallows clicks aimed at the menu.
            var dim = Rect("Dim", canvasRt, Vector2.zero, Vector2.one);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.72f);

            var card = Rect("Card", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            card.sizeDelta = new Vector2(1200f, 520f);
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = new Color(0f, 0.231f, 0.294f, 1f);   // the lobby plate teal

            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(56, 56, 48, 48);
            v.spacing = 28f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;

            Label(card, Loc.T("WHAT SHOULD WE CALL YOU?"), 52f, FontStyles.Bold, 90f);
            Label(card, Loc.T("Other players see this name in multiplayer. " +
                              "You can change it any time in Settings."),
                  30f, FontStyles.Normal, 80f).color = new Color(1f, 1f, 1f, 0.7f);

            _field = Field(card, PlayerProfile.PlayerName);

            var confirm = TextButton(card, Loc.T("CONTINUE"));
            confirm.onClick.AddListener(Confirm);
        }

        private void Update()
        {
            // Enter confirms. The field is focused on open, so a player who
            // just types and hits return never touches the mouse.
            if (_field != null && _field.isFocused &&
                (UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                 UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)))
                Confirm();
        }

        private void Confirm()
        {
            string typed = _field != null ? _field.text : null;
            // An empty box keeps the suggested name rather than leaving the
            // player nameless — PlayerProfile falls back on its own, but being
            // explicit here means the value written is the value shown.
            if (!string.IsNullOrWhiteSpace(typed)) PlayerProfile.PlayerName = typed;
            else PlayerProfile.Save();

            Debug.Log($"[PlayerNamePrompt] Player name set to '{PlayerProfile.PlayerName}' " +
                      $"and saved to {PlayerProfile.Path}");
            Destroy(gameObject);
        }

        // ── Construction helpers ────────────────────────────────────────

        private static RectTransform Rect(string name, RectTransform parent,
                                          Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TMP_Text Label(RectTransform parent, string text, float size,
                                      FontStyles style, float height)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return t;
        }

        private static TMP_InputField Field(RectTransform parent, string initial)
        {
            var go = new GameObject("NameField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.055f, 0.09f, 0.118f, 1f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 96f;

            var area = Rect("Text Area", (RectTransform)go.transform, Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(24f, 8f);
            area.offsetMax = new Vector2(-24f, -8f);
            area.gameObject.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(area, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 40f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.characterLimit = 24;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial;
            input.ActivateInputField();
            return input;
        }

        private static Button TextButton(RectTransform parent, string label)
        {
            var go = new GameObject("Confirm", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.29f, 0.235f, 0.11f, 1f);   // the START-button fill
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 104f;

            var t = Label((RectTransform)go.transform, label, 40f, FontStyles.Bold, 104f);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            Destroy(t.GetComponent<LayoutElement>());

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            return button;
        }
    }
}
