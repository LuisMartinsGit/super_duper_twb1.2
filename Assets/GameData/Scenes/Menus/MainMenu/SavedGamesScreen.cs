// SavedGamesScreen.cs
// The main menu's Load Game screen: saved games to resume, and replays to
// watch. docs/Design/Replays_And_Saves.md
//
// Code-built uGUI on its own overlay canvas, opened by LoadGameMenuButton. The
// rows are read with SavedGames.ReadInfo, which parses only the header and the
// file's last few kilobytes, so a long list opens instantly. A file from a
// different build is listed but cannot be played — a replay re-simulates the
// match, and a different simulation would play a different game.

using System.Collections.Generic;

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Replay;
using TheWaningBorder.UI.Ingame;

namespace TheWaningBorder.UI.Menus
{
    public sealed class SavedGamesScreen : MonoBehaviour
    {
        private const float PanelWidth = 2100f;
        private const float PanelHeight = 1500f;
        private const float RowHeight = 176f;
        private const float ChipSize = 26f;

        private static SavedGamesScreen _instance;

        private bool _showReplays;
        private RectTransform _list;
        private TMP_Text _error;
        private TMP_Text _empty;
        private (Image bg, TMP_Text text) _tabSaves, _tabReplays;
        private string _armedDelete;
        /// <summary>Preview textures of the rows on screen, freed on every
        /// refresh and on close.</summary>
        private readonly List<Texture2D> _thumbs = new List<Texture2D>();

        /// <summary>Open the screen (on the replays tab when <paramref name="replays"/>).</summary>
        public static void Open(bool replays = false)
        {
            if (_instance == null)
            {
                var go = new GameObject("SavedGamesScreen");
                _instance = go.AddComponent<SavedGamesScreen>();
            }
            _instance._showReplays = replays;
            _instance.gameObject.SetActive(true);
            _instance.Refresh();
        }

        public static void Close()
        {
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        private void Awake() => Build();

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // ── Construction ───────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var root = GameUIKit.Rect(transform, "root");
            GameUIKit.Stretch(root);
            var scrim = GameUIKit.Image(root, "scrim", new Color(0f, 0f, 0f, 0.78f), raycast: true);
            GameUIKit.Stretch(scrim.rectTransform);

            var panel = GameUIKit.Rect(root, "panel");
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            GameUIKit.PanelChrome(panel);
            var v = GameUIKit.VStack(panel, 40f, 18f);
            v.childAlignment = TextAnchor.UpperCenter;

            var title = GameUIKit.Text(panel, "title", Loc.T("LOAD GAME"), 60f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 8f;
            GameUIKit.FixHeight(title.gameObject, 80f);

            var tabs = Row(panel, "tabs", 80f);
            _tabSaves = Button(tabs, "saves", Loc.T("Saved Games"), 1f, () => { _showReplays = false; Refresh(); });
            _tabReplays = Button(tabs, "replays", Loc.T("Replays"), 1f, () => { _showReplays = true; Refresh(); });

            // Scrolling list.
            var scrollRt = GameUIKit.Rect(panel, "scroll");
            var scrollLe = scrollRt.gameObject.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.minHeight = 600f;
            var scrollBg = GameUIKit.Image(scrollRt, "bg", new Color(0f, 0f, 0f, 0.25f), raycast: true);
            GameUIKit.Stretch(scrollBg.rectTransform);
            GameUIKit.IgnoreLayout(scrollBg.gameObject);
            var viewport = GameUIKit.Rect(scrollRt, "viewport");
            GameUIKit.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            _list = GameUIKit.Rect(viewport, "content");
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(0.5f, 1f);
            _list.sizeDelta = Vector2.zero;
            var lv = GameUIKit.VStack(_list, 12f, 10f);
            lv.childAlignment = TextAnchor.UpperCenter;
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _list;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 60f;

            _empty = GameUIKit.Text(scrollRt, "empty", "", 34f, GameUIKit.TextDim,
                TextAlignmentOptions.Center);
            GameUIKit.Stretch(_empty.rectTransform);
            GameUIKit.IgnoreLayout(_empty.gameObject);

            _error = GameUIKit.Text(panel, "error", "", 30f, new Color(0.95f, 0.45f, 0.38f),
                TextAlignmentOptions.Center);
            GameUIKit.FixHeight(_error.gameObject, 44f);

            var bottom = Row(panel, "bottom", 90f);
            Button(bottom, "folder", Loc.T("Open Folder"), 1f, OpenFolder);
            Button(bottom, "back", Loc.T("Back"), 1f, Close);
        }

        private static RectTransform Row(Transform parent, string name, float height)
        {
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, height);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            return rt;
        }

        private static (Image bg, TMP_Text text) Button(Transform parent, string name, string label,
            float weight, System.Action click)
        {
            var rt = GameUIKit.Rect(parent, name);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = weight;
            var bg = GameUIKit.ButtonChrome(rt, raycast: true);
            var text = GameUIKit.Text(rt, "label", label, 32f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(text.rectTransform);
            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = click;
            relay.OnEnter = () => bg.color = GameUIKit.BarBlue * 0.5f;
            relay.OnExit = () => bg.color = GameUIKit.ButtonBg;
            return (bg, text);
        }

        // ── Content ────────────────────────────────────────────────────────

        private void Refresh()
        {
            _error.text = "";
            _armedDelete = null;
            _tabSaves.text.color = _showReplays ? GameUIKit.TextMain : GameUIKit.Gold;
            _tabReplays.text.color = _showReplays ? GameUIKit.Gold : GameUIKit.TextMain;

            FreeThumbs();
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                var c = _list.GetChild(i).gameObject;
                c.SetActive(false);
                Destroy(c);
            }

            List<SavedGameInfo> items = _showReplays ? SavedGames.ListReplays() : SavedGames.ListSaves();
            _empty.text = items.Count > 0 ? ""
                : _showReplays
                    ? Loc.T("No replays yet. Every skirmish you play is recorded here.")
                    : Loc.T("No saved games. Save a match from the pause menu (Esc).");
            foreach (var info in items) AddRow(info);
        }

        private void AddRow(SavedGameInfo info)
        {
            var row = GameUIKit.Rect(_list, "row");
            GameUIKit.FixHeight(row.gameObject, RowHeight);
            var bg = GameUIKit.Image(row, "bg", new Color(1f, 1f, 1f, 0.04f));
            GameUIKit.Stretch(bg.rectTransform);
            GameUIKit.IgnoreLayout(bg.gameObject);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(24, 24, 14, 14);
            h.spacing = 16f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            AddThumb(row, info);

            var textCol = GameUIKit.Rect(row, "text");
            textCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tv = GameUIKit.VStack(textCol, 0f, 4f);
            tv.childAlignment = TextAnchor.MiddleLeft;

            string map = MapName(info.Header.Map);
            string head = info.IsSave && !string.IsNullOrEmpty(info.Label) ? info.Label : map;
            var t1 = GameUIKit.Text(textCol, "head", head, 36f, GameUIKit.Gold, TextAlignmentOptions.Left, wrap: false);
            GameUIKit.FixHeight(t1.gameObject, 50f);

            string detail = string.Format("{0}  ·  {1}  ·  {2} {3}",
                info.Written.ToString("yyyy-MM-dd HH:mm"),
                Clock(info.Seconds),
                info.Header.TotalPlayers, Loc.T("players"));
            if (!info.IsSave && !string.IsNullOrEmpty(info.Outcome))
                detail += "  ·  " + info.Outcome;
            var t2 = GameUIKit.Text(textCol, "detail", detail, 28f, GameUIKit.TextMain,
                TextAlignmentOptions.Left, wrap: false);
            GameUIKit.FixHeight(t2.gameObject, 38f);

            AddPlayers(textCol, info.Header);

            if (!info.Compatible)
            {
                // Listed, greyed, with the reason — never hidden (2026-10-19:
                // an old-format save used to vanish from the list).
                bg.color = new Color(0f, 0f, 0f, 0.25f);
                t1.color = GameUIKit.TextLocked;
                t2.color = GameUIKit.TextLocked;
                var t3 = GameUIKit.Text(textCol, "build", info.IncompatibleReason ?? "",
                    26f, GameUIKit.TextLocked, TextAlignmentOptions.Left, wrap: false);
                t3.enableAutoSizing = true;
                t3.fontSizeMin = 18f;
                t3.fontSizeMax = 26f;
                t3.overflowMode = TextOverflowModes.Ellipsis;
                GameUIKit.FixHeight(t3.gameObject, 34f);
            }

            var play = RowButton(row, info.IsSave ? Loc.T("Load") : Loc.T("Watch"), 300f,
                () => Play(info));
            if (!info.Compatible) play.text.color = GameUIKit.TextLocked;

            (Image bg, TMP_Text text) del = default;
            del = RowButton(row, Loc.T("Delete"), 240f, () =>
            {
                if (_armedDelete != info.Path)
                {
                    _armedDelete = info.Path;
                    del.text.text = Loc.T("Sure?");
                    del.text.color = new Color(0.95f, 0.45f, 0.38f);
                    return;
                }
                SavedGames.Delete(info.Path);
                Refresh();
            });
        }

        /// <summary>The save's minimap bake on the left of the row (2026-10-16);
        /// an empty frame for a replay or an older save that carries none.</summary>
        private void AddThumb(RectTransform row, SavedGameInfo info)
        {
            float side = RowHeight - 28f;
            var frame = GameUIKit.Image(row, "thumb", new Color(0.05f, 0.06f, 0.12f, 1f));
            var le = frame.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = side;
            if (info.Preview == null || info.Preview.Length == 0) return;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(info.Preview)) { Destroy(tex); return; }
            tex.wrapMode = TextureWrapMode.Clamp;
            _thumbs.Add(tex);

            var raw = new GameObject("map", typeof(RectTransform), typeof(RawImage));
            raw.transform.SetParent(frame.transform, false);
            var img = raw.GetComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            // Fit inside the square, keeping the map's aspect.
            var rt = (RectTransform)raw.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            float aspect = (float)tex.width / Mathf.Max(1, tex.height);
            rt.sizeDelta = aspect >= 1f ? new Vector2(side, side / aspect) : new Vector2(side * aspect, side);
        }

        /// <summary>One colour chip + name per player in the match; the local
        /// player's name in gold (2026-10-16).</summary>
        private static void AddPlayers(RectTransform parent, ReplayHeader header)
        {
            if (header?.Slots == null || header.Slots.Count == 0) return;

            var line = GameUIKit.Rect(parent, "players");
            GameUIKit.FixHeight(line.gameObject, 36f);
            var h = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            foreach (var slot in header.Slots)
            {
                if (slot.Type != 1 && slot.Type != 2) continue;   // only Human (1) / AI (2) seats play
                if (header.ActiveSlots > 0 && slot.Index >= header.ActiveSlots) continue;

                var chip = GameUIKit.Image(line, "chip", ColorOf(slot.Color));
                var cle = chip.gameObject.AddComponent<LayoutElement>();
                cle.preferredWidth = cle.minWidth = ChipSize;
                cle.preferredHeight = cle.minHeight = ChipSize;

                bool local = slot.Faction == header.LocalFaction && slot.Type == 1;   // 1 = human seat
                string name = !string.IsNullOrEmpty(slot.Name) ? slot.Name : ColorName(slot.Color);
                var t = GameUIKit.Text(line, "name", name, 24f, local ? GameUIKit.Gold : GameUIKit.TextDim,
                    TextAlignmentOptions.Left, wrap: false);
                if (local) t.fontStyle = FontStyles.Bold;
                var tle = t.gameObject.AddComponent<LayoutElement>();
                tle.preferredWidth = Mathf.Min(220f, t.preferredWidth + 14f);
            }
        }

        private static Color ColorOf(int idx)
            => idx >= 0 && idx < FactionColors.ColorPool.Length ? FactionColors.ColorPool[idx] : Color.gray;

        private static string ColorName(int idx)
            => idx >= 0 && idx < FactionColors.ColorNames.Length ? Loc.T(FactionColors.ColorNames[idx]) : "?";

        private void FreeThumbs()
        {
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();
        }

        private void OnDestroy() => FreeThumbs();

        private static (Image bg, TMP_Text text) RowButton(Transform parent, string label, float width,
            System.Action click)
        {
            var b = Button(parent, "btn", label, 0f, click);
            var le = b.bg.transform.parent.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            return b;
        }

        private void Play(SavedGameInfo info)
        {
            if (!info.Compatible)
            {
                _error.text = info.IncompatibleReason ?? "";
                return;
            }
            var mode = info.IsSave ? ReplayMode.Resume : ReplayMode.Watch;
            if (!ReplaySession.Prepare(info.Path, mode, out string error))
            {
                _error.text = error;
                return;
            }
            UnityEngine.Debug.Log($"[SavedGamesScreen] {(info.IsSave ? "Loading save" : "Watching replay")} " +
                $"{System.IO.Path.GetFileName(info.Path)} on {GameSettings.SelectedMapScene}.");
            Close();
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        private void OpenFolder()
        {
            string dir = _showReplays ? SavedGames.ReplaysDirectory : SavedGames.SavesDirectory;
            try { Application.OpenURL("file:///" + dir.Replace('\\', '/')); }
            catch (System.Exception e) { _error.text = e.Message; }
        }

        private static string MapName(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return "?";
            // GetEntry falls back to the default map for an unknown scene, so
            // only trust it when the scene actually matches.
            var entry = TheWaningBorder.Core.Maps.MapRegistry.GetEntry(scene);
            return entry.SceneName == scene && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName : scene;
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }
}
