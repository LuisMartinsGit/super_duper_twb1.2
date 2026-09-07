// MenuSceneBuilder.cs
// Builds the Scenarios and Settings menu SCENES, and strips the legacy
// in-scene panels out of MainMenu.unity.
//
// THE LOOK IS NOT DESCRIBED HERE, IT IS COPIED. Every screen the game has in
// the current style — Skirmish, and Multiplayer after it — was built once and
// then hand-finished in the editor, and the last two screens (Scenarios,
// Settings) were still drawn in the older eyebrow-and-gradient look inside
// MainMenu.unity. Rather than transcribe the skirmish style into constants
// that would go stale the next time someone nudged a plate by hand, this
// opens SkirmishMenu.unity and CLONES its parts into the new scene: the
// EventSystem and camera, the canvas and its background, the banner title,
// the two columns and their rects, the plate (frame + masked fill), the
// option cell with its label, caption and pill, the dropdown, the footer
// with its Synty buttons. What it cannot clone — a text field, a slider — it
// assembles from the sprites and fonts read off those same parts.
//
// So the new screens are the skirmish screen with different contents, by
// construction, and re-running this after the skirmish look changes brings
// them along. Both scenes stay hand-editable afterwards, exactly like the
// skirmish scene; re-running the builder REPLACES them.
//
// Node names are the contract with the binders: ScenariosPanel is wired by
// reference here, SettingsPanel finds its controls BY NAME in Awake.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.UI.Menus.Panels;

namespace TheWaningBorder.EditorTools
{
    public static class MenuSceneBuilder
    {
        const string MenusRoot      = "Assets/GameData/Scenes/Menus/";
        const string SkirmishScene  = MenusRoot + "SkirmishMenu/SkirmishMenu.unity";
        const string MainMenuScene  = MenusRoot + "MainMenu/MainMenu.unity";
        const string ScenariosScene = MenusRoot + "ScenariosMenu/ScenariosMenu.unity";
        const string SettingsScene  = MenusRoot + "SettingsMenu/SettingsMenu.unity";

        /// <summary>The Synty gem every button in the set carries; the slider
        /// knob, so the one control the skirmish screen never had still reads
        /// as part of the same family.</summary>
        const string GemSprite =
            "Assets/Synty/InterfaceFantasyMenus/Sprites/FantasyMenus/SPR_FantasyMenus_Menu_Button_15_Gem.png";

        /// <summary>Panels MainMenu.unity used to switch on with SetActive.
        /// Every one has been its own scene since 2026-09-07.</summary>
        static readonly string[] LegacyMainMenuPanels =
        {
            "Popup_Scenarios", "Panel_Scenarios", "Panel_Multiplayer", "OptionsPanel",
        };

        /// <summary>Width of a settings control — dropdown, text field, or
        /// slider plus its value — after the fixed text column. One number so
        /// the controls form a column of their own.</summary>
        const float ControlWidth = 900f;

        // Slider colours: the pill's gold for the filled part, the dropdown's
        // plate for the track, the lit knob colour MenuToggleSwitch uses.
        static readonly Color TrackColour = new Color(0.059f, 0.118f, 0.157f, 0.95f);
        static readonly Color FillColour  = new Color(0.690f, 0.525f, 0.173f);
        static readonly Color KnobColour  = new Color(0.980f, 0.800f, 0.360f);

        // ── Entry points ────────────────────────────────────────────────────

        [MenuItem("Waning Border/Menus/Build All Menu Scenes")]
        public static void BuildAll()
        {
            BuildScenariosScene();
            BuildSettingsScene();
            CleanMainMenu();
        }

        [MenuItem("Waning Border/Menus/Build Scenarios Scene")]
        public static void BuildScenariosScene()
        {
            var kit = Kit.Load();
            var scene = Shell(kit, "Panel_Scenarios", out var panel);

            var left = Column(kit, kit.LeftColumn, panel, keepTitle: true, "LeftColumn");
            SetText(left, "Title/TitleLabel", "SCENARIOS");
            var right = Column(kit, kit.RightColumn, panel, keepTitle: false, "RightColumn");

            // ── Left: the list ───────────────────────────────────────────
            var listPlate = Plate(kit, left, "ListPlate", keepScroll: true, fill: true);
            var header = Header(kit, listPlate, "Header", "SCENARIO LIST");
            header.SetSiblingIndex(1);   // after the plate's Background
            var hint = Caption(kit, listPlate, "Hint", "Pick a scenario to read its briefing.");
            hint.SetSiblingIndex(2);

            var scroll = Req(listPlate, "RosterScroll");
            scroll.name = "ListScroll";
            var content = Req(scroll, "Content");
            var row = Req(content, "RosterRowTemplate");
            row.name = "ListRowTemplate";
            for (int i = row.childCount - 1; i >= 0; i--)
                if (row.GetChild(i).name != "NameLabel")
                    Object.DestroyImmediate(row.GetChild(i).gameObject);
            var rowButton = row.gameObject.AddComponent<Button>();
            rowButton.targetGraphic = row.GetComponent<Image>();
            var rowLe = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
            rowLe.minHeight = rowLe.preferredHeight = 110f;
            var rowLabel = Req(row, "NameLabel");
            (rowLabel.GetComponent<LayoutElement>() ?? rowLabel.gameObject.AddComponent<LayoutElement>())
                .flexibleWidth = 1f;
            rowLabel.GetComponent<TMP_Text>().text = "SCENARIO NAME";
            row.gameObject.SetActive(false);

            // ── Right: the briefing ──────────────────────────────────────
            var preview = Plate(kit, right, "PreviewPlate", keepScroll: false, fill: true);
            Header(kit, preview, "Header", "BRIEFING");

            var thumbBox = Box(kit, preview, "ThumbBox", TrackColour);
            var thumbLe = thumbBox.GetComponent<LayoutElement>();
            thumbLe.preferredHeight = 600f;
            thumbLe.flexibleHeight = 0f;
            var thumb = new GameObject("ThumbImage", typeof(RectTransform), typeof(Image));
            thumb.transform.SetParent(thumbBox, false);
            Stretch((RectTransform)thumb.transform, 16f);
            var thumbImage = thumb.GetComponent<Image>();
            thumbImage.preserveAspect = true;
            thumbImage.raycastTarget = false;
            thumbImage.enabled = false;
            var placeholder = Clone(kit.MapDescription.gameObject, thumbBox, "ThumbPlaceholder");
            Stretch((RectTransform)placeholder.transform, 24f);
            var placeholderText = placeholder.GetComponent<TMP_Text>();
            placeholderText.text = "NO PREVIEW";
            placeholderText.alignment = TextAlignmentOptions.Center;
            Frame(kit, thumbBox);

            var nameText = Header(kit, preview, "NameText", "SCENARIO NAME");

            // The briefing scrolls: the tutorial's is twenty lines, and a box
            // that either clipped or spilled it over the footer (the first
            // build did the latter) is not a briefing. The roster's scroll
            // view, emptied, is the same scrolling the lobby already has.
            var descBox = Box(kit, preview, "DescriptionBox", kit.InnerBoxColour);
            descBox.GetComponent<LayoutElement>().flexibleHeight = 1f;
            Frame(kit, descBox);
            var descScroll = Clone(Req(kit.RosterPlate, "RosterScroll").gameObject, descBox, "DescriptionScroll");
            Stretch((RectTransform)descScroll.transform, 24f);
            var descScrollImage = descScroll.GetComponent<Image>();
            if (descScrollImage != null) descScrollImage.color = Color.clear;
            var descContent = Req(descScroll.transform, "Content");
            for (int i = descContent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(descContent.GetChild(i).gameObject);
            var desc = Clone(kit.MapDescription.gameObject, descContent, "DescriptionText");
            foreach (var stale in desc.GetComponents<LayoutElement>()) Object.DestroyImmediate(stale);
            var descText = desc.GetComponent<TMP_Text>();
            descText.text = string.Empty;
            descText.alignment = TextAlignmentOptions.TopLeft;
            descText.textWrappingMode = TextWrappingModes.Normal;
            descText.overflowMode = TextOverflowModes.Overflow;
            descText.raycastTarget = false;
            desc.transform.SetAsLastSibling();

            // ── Footer + binder ──────────────────────────────────────────
            var footer = Footer(kit, panel, "< MAIN MENU", "START SCENARIO");

            var binder = panel.gameObject.AddComponent<ScenariosPanel>();
            binder.ListContent = (RectTransform)content;
            binder.ListRowTemplate = row.gameObject;
            binder.ThumbImage = thumbImage;
            binder.ThumbPlaceholder = placeholder;
            binder.NameText = nameText.GetComponent<TMP_Text>();
            binder.DescriptionText = descText;
            binder.BackButton = Req(footer, "BackButton").GetComponent<Button>();
            binder.StartButton = Req(footer, "PrimaryButton").GetComponent<Button>();

            Save(scene, ScenariosScene);
        }

        [MenuItem("Waning Border/Menus/Build Settings Scene")]
        public static void BuildSettingsScene()
        {
            var kit = Kit.Load();
            var scene = Shell(kit, "Panel_Settings", out var panel);

            var left = Column(kit, kit.LeftColumn, panel, keepTitle: true, "LeftColumn");
            SetText(left, "Title/TitleLabel", "SETTINGS");
            var right = Column(kit, kit.RightColumn, panel, keepTitle: false, "RightColumn");

            // ── Left: profile + display ──────────────────────────────────
            var display = Plate(kit, left, "DisplayPlate", keepScroll: false, fill: false);

            Header(kit, display, "ProfileHeader", "PROFILE");
            var nameCell = Cell(kit, Row(kit, display, "PlayerNameRow"), "OptPlayerName",
                "PLAYER NAME", "Shown to other players in a lobby.", keepPill: false);
            InputField(kit, nameCell, "PlayerNameInput", "YOUR NAME");

            Header(kit, display, "DisplayHeader", "DISPLAY");
            var qualityCell = Cell(kit, Row(kit, display, "QualityRow"), "OptQuality",
                "GRAPHICS QUALITY", "Higher looks better and costs frames.", keepPill: false);
            Dropdown(kit, qualityCell, "QualityDropdown");

            var resolutionCell = Cell(kit, Row(kit, display, "ResolutionRow"), "OptResolution",
                "RESOLUTION", "Pick the mode that fills your monitor.", keepPill: false);
            Dropdown(kit, resolutionCell, "ResolutionDropdown");

            var fullscreenCell = Cell(kit, Row(kit, display, "FullscreenRow"), "OptFullscreen",
                "FULLSCREEN", "Borderless full screen, or a window.", keepPill: true);
            Req(fullscreenCell, "Pill/State").name = "FullscreenState";
            Req(fullscreenCell, "Pill/Track").name = "FullscreenToggle";

            Caption(kit, display, "ApplyHint", "Takes effect when you press APPLY.");

            // ── Right: audio + language ──────────────────────────────────
            var audio = Plate(kit, right, "AudioPlate", keepScroll: false, fill: false);

            Header(kit, audio, "AudioHeader", "AUDIO");
            var masterCell = Cell(kit, Row(kit, audio, "MasterRow"), "OptMaster",
                "MASTER VOLUME", "Everything the game plays.", keepPill: false);
            Slider(kit, masterCell, "MasterSlider", "MasterValue");

            var musicCell = Cell(kit, Row(kit, audio, "MusicRow"), "OptMusic",
                "MUSIC VOLUME", "The score only.", keepPill: false);
            Slider(kit, musicCell, "MusicSlider", "MusicValue");

            Header(kit, audio, "LanguageHeader", "LANGUAGE");
            var languageRow = Row(kit, audio, "LanguageRow");
            languageRow.GetComponent<LayoutElement>().minHeight = 140f;
            var languageCell = Cell(kit, languageRow, "OptLanguage",
                "LANGUAGE", "Shown in its own language, so you can always find the way back.",
                keepPill: false);
            // Own-language labels on purpose (see SettingsPanel.SetLanguage);
            // neither is a translation key, so the scene localizer leaves them.
            SmallButton(kit, languageCell, "EnglishButton", "English");
            SmallButton(kit, languageCell, "PortugueseButton", "Português");

            // ── Footer + binder ──────────────────────────────────────────
            var footer = Footer(kit, panel, "< MAIN MENU", "APPLY");
            Req(footer, "ErrorText").name = "Status";

            panel.gameObject.AddComponent<SettingsPanel>();

            Save(scene, SettingsScene);
        }

        /// <summary>
        /// Strip the legacy in-scene panels out of MainMenu.unity and add the
        /// two navigation links the new scene hooks read. Idempotent.
        /// </summary>
        [MenuItem("Waning Border/Menus/Clean Main Menu")]
        public static void CleanMainMenu()
        {
            var scene = EditorSceneManager.OpenScene(MainMenuScene, OpenSceneMode.Single);
            var canvas = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "UI_Canvas");
            if (canvas == null)
            {
                Debug.LogError("[MenuSceneBuilder] MainMenu.unity has no UI_Canvas root.");
                return;
            }

            // Matched by PREFIX: an instance whose prefab asset is gone is
            // renamed "OptionsPanel (Missing Prefab with guid: …)" and an
            // exact Find misses it — which is how the retired options prefab
            // instance survived the first clean and kept logging a missing-
            // prefab error on every load of the menu.
            int removed = 0;
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                var t = canvas.transform.GetChild(i);
                string hit = LegacyMainMenuPanels.FirstOrDefault(n =>
                    t.name == n || t.name.StartsWith(n + " (Missing Prefab", System.StringComparison.Ordinal));
                if (hit == null) continue;
                Object.DestroyImmediate(t.gameObject);
                removed++;
                Debug.Log($"[MenuSceneBuilder] MainMenu: removed legacy panel '{hit}'.");
            }

            int added = 0;
            added += EnsureNav(scene, "MenuNav_Scenarios", SceneNames.Scenarios) ? 1 : 0;
            added += EnsureNav(scene, "MenuNav_Settings", SceneNames.Settings) ? 1 : 0;

            if (removed > 0 || added > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log($"[MenuSceneBuilder] MainMenu cleaned: -{removed} legacy panel(s), " +
                      $"+{added} nav link(s).");
        }

        // ── The kit: parts read off the skirmish scene ──────────────────────

        /// <summary>
        /// References into SkirmishMenu.unity, opened SINGLE so it is the only
        /// thing loaded. Every path here is a fact about that scene as saved;
        /// a rename there fails loudly here rather than producing a screen
        /// with a piece missing.
        /// </summary>
        sealed class Kit
        {
            public GameObject EventSystem, Camera, Canvas, Screenshot;
            public Image Scrim;
            public Transform LeftColumn, RightColumn, Footer;
            public Transform RosterPlate;
            public Transform OptionsHeader, OptionsRow, OptFog, OptResources;
            public Transform MapDescription;
            public Sprite InnerFrame;      // Frame_Box_Medium_06, the inner box border
            public Sprite Plate;           // built-in UISprite the dropdowns use
            public Color InnerBoxColour;   // the map description box's fill
            public Sprite Gem;

            public static Kit Load()
            {
                var scene = EditorSceneManager.OpenScene(SkirmishScene, OpenSceneMode.Single);
                var k = new Kit();
                foreach (var root in scene.GetRootGameObjects())
                {
                    switch (root.name)
                    {
                        case "UI_Canvas":   k.Canvas = root; break;
                        case "EventSystem": k.EventSystem = root; break;
                        case "Main Camera": k.Camera = root; break;
                    }
                }
                if (k.Canvas == null || k.EventSystem == null || k.Camera == null)
                    throw new System.InvalidOperationException(
                        "SkirmishMenu.unity is missing UI_Canvas / EventSystem / Main Camera.");

                var canvas = k.Canvas.transform;
                k.Screenshot = Req(canvas, "SPR_Screenshot").gameObject;
                var panel = Req(canvas, "Panel_Skirmish");
                k.Scrim = panel.GetComponent<Image>();
                k.LeftColumn = Req(panel, "LeftColumn");
                k.RightColumn = Req(panel, "RightColumn");
                k.Footer = Req(panel, "Footer");
                k.RosterPlate = Req(panel, "RightColumn/RosterPlate");

                var options = Req(panel, "LeftColumn/MapPreview/Column/GameObject");
                k.OptionsHeader = Req(options, "OptionsHeader");
                k.OptionsRow = Req(options, "OptionsRow1");
                k.OptResources = Req(options, "OptionsRow1/OptResources");
                k.OptFog = Req(options, "OptionsRow2/OptFog");

                var descBox = Req(panel, "LeftColumn/MapPreview/Column/MapDescriptionBox");
                k.MapDescription = Req(descBox, "MapDescription");
                k.InnerFrame = Req(descBox, "Frame").GetComponent<Image>().sprite;
                k.InnerBoxColour = Req(descBox, "Background").GetComponent<Image>().color;
                k.Plate = Req(k.OptResources, "Dropdown").GetComponent<Image>().sprite;

                k.Gem = AssetDatabase.LoadAssetAtPath<Sprite>(GemSprite);
                if (k.Gem == null) Debug.LogWarning($"[MenuSceneBuilder] gem sprite missing: {GemSprite}");
                return k;
            }
        }

        // ── Scene shell ─────────────────────────────────────────────────────

        /// <summary>
        /// A new scene holding clones of the skirmish EventSystem and camera,
        /// a canvas configured exactly like the skirmish one with the same
        /// background photo, and an empty scrimmed panel to build into.
        /// </summary>
        static Scene Shell(Kit kit, string panelName, out Transform panel)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            Clone(kit.EventSystem, null, "EventSystem");
            Clone(kit.Camera, null, "Main Camera");

            var canvas = new GameObject("UI_Canvas", typeof(RectTransform), typeof(Canvas),
                                        typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvas, scene);
            EditorUtility.CopySerialized(kit.Canvas.GetComponent<Canvas>(), canvas.GetComponent<Canvas>());
            EditorUtility.CopySerialized(kit.Canvas.GetComponent<CanvasScaler>(), canvas.GetComponent<CanvasScaler>());
            EditorUtility.CopySerialized(kit.Canvas.GetComponent<GraphicRaycaster>(), canvas.GetComponent<GraphicRaycaster>());

            Clone(kit.Screenshot, canvas.transform, "SPR_Screenshot");

            var panelGo = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)panelGo.transform, 0f);
            EditorUtility.CopySerialized(kit.Scrim, panelGo.GetComponent<Image>());
            panel = panelGo.transform;
            return scene;
        }

        /// <summary>A skirmish column with its rect and layout, emptied — the
        /// banner title kept when asked for.</summary>
        static Transform Column(Kit kit, Transform source, Transform panel, bool keepTitle, string name)
        {
            var col = Clone(source.gameObject, panel, name).transform;
            for (int i = col.childCount - 1; i >= 0; i--)
            {
                var c = col.GetChild(i);
                if (keepTitle && c.name == "Title") continue;
                Object.DestroyImmediate(c.gameObject);
            }
            return col;
        }

        /// <summary>The skirmish footer, relabelled. Node names are kept:
        /// MenuMotion animates BackButton / PrimaryButton by name.</summary>
        static Transform Footer(Kit kit, Transform panel, string back, string primary)
        {
            var f = Clone(kit.Footer.gameObject, panel, "Footer").transform;
            SetText(f, "BackButton/Label", back);
            SetText(f, "PrimaryButton/Label", primary);
            Req(f, "ErrorText").GetComponent<TMP_Text>().text = string.Empty;
            return f;
        }

        // ── Widgets ─────────────────────────────────────────────────────────

        /// <summary>
        /// The roster plate — gold frame over a masked teal fill, vertical
        /// layout inside — with or without its scroll view. <paramref name="fill"/>
        /// keeps the roster plate's claim on the column's spare height; off,
        /// the plate hugs its content and the column shows through beneath,
        /// the way the multiplayer screen's content-sized panes do. A
        /// settings plate holding four rows would otherwise be a frame around
        /// mostly nothing.
        /// </summary>
        static Transform Plate(Kit kit, Transform column, string name, bool keepScroll, bool fill)
        {
            var plate = Clone(kit.RosterPlate.gameObject, column, name).transform;
            var plateLe = plate.GetComponent<LayoutElement>() ?? plate.gameObject.AddComponent<LayoutElement>();
            plateLe.flexibleHeight = fill ? 1f : 0f;
            if (!fill)
            {
                // The roster plate's LayoutElement says preferredHeight = 0 —
                // a real zero, not "unset" — which never showed while
                // flexibleHeight handed it the column's spare height. Hugging
                // content, a zero preferred height wins over the layout group's
                // own measure: the frame collapsed to nothing while its rows
                // still laid out over the bare background.
                plateLe.minHeight = -1f;
                plateLe.preferredHeight = -1f;
            }
            // The authored fill is a fixed 1728x1577 tuned to the plate's laid
            // out size; stretched instead, so it follows whatever height this
            // column gives the plate.
            var bg = plate.Find("Background");
            if (bg != null) Stretch((RectTransform)bg, 0f);

            // The roster plate forces every child to expand into spare height,
            // which is invisible with its single scroll-view child and wrong
            // for a stack: it stretched a one-line caption into a 400-unit gap
            // and the thumbnail box to twice its size. Only what asks for
            // spare height (flexibleHeight) gets it.
            var v = plate.GetComponent<VerticalLayoutGroup>();
            if (v != null)
            {
                v.childForceExpandHeight = false;
                v.childControlHeight = true;
                v.childControlWidth = true;
                v.childForceExpandWidth = true;
            }
            if (!keepScroll)
            {
                var scroll = plate.Find("RosterScroll");
                if (scroll != null) Object.DestroyImmediate(scroll.gameObject);
            }
            return plate;
        }

        /// <summary>
        /// The "MAP OPTIONS" section header, made to take part in layout.
        ///
        /// The authored header sits OUTSIDE its container's layout
        /// (LayoutElement.ignoreLayout) at a hand-placed offset, which is fine
        /// for the one header the skirmish options box has. Cloned into a
        /// plate that stacks several things, every header landed on that same
        /// hand-placed spot — "BRIEFING" and the scenario name drew on top of
        /// each other.
        /// </summary>
        static Transform Header(Kit kit, Transform parent, string name, string text)
        {
            var h = Clone(kit.OptionsHeader.gameObject, parent, name).transform;
            h.GetComponent<TMP_Text>().text = text;
            var le = h.GetComponent<LayoutElement>() ?? h.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            le.minHeight = le.preferredHeight = 110f;
            le.flexibleHeight = 0f;
            le.flexibleWidth = 1f;
            return h;
        }

        /// <summary>The 24pt italic caption from under an option label.</summary>
        static Transform Caption(Kit kit, Transform parent, string name, string text)
        {
            var c = Clone(Req(kit.OptFog, "Text/Caption").gameObject, parent, name).transform;
            c.GetComponent<TMP_Text>().text = text;
            return c;
        }

        /// <summary>An options row (horizontal layout) with its authored
        /// children removed, sized to fit one option cell.</summary>
        static Transform Row(Kit kit, Transform plate, string name)
        {
            var row = Clone(kit.OptionsRow.gameObject, plate, name).transform;
            for (int i = row.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(row.GetChild(i).gameObject);
            // Hand-placed in the skirmish options box like its header, so it
            // ignores layout there; in a stacked plate every row would land
            // on the same spot (the first Settings build did exactly that).
            var le = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            le.minHeight = le.preferredHeight = 116f;
            le.flexibleHeight = 0f;
            le.flexibleWidth = 1f;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            if (h != null) { h.childForceExpandHeight = false; h.childAlignment = TextAnchor.MiddleLeft; }
            return row;
        }

        /// <summary>
        /// The fog option cell: bold label over an italic caption, then the
        /// control. The pill (state label + gold track button) is kept for a
        /// boolean option and dropped for everything else, which then adds its
        /// own control after the text column.
        /// </summary>
        static Transform Cell(Kit kit, Transform row, string name, string label, string caption, bool keepPill)
        {
            var cell = Clone(kit.OptFog.gameObject, row, name).transform;
            SetText(cell, "Text/Label", label);
            SetText(cell, "Text/Caption", caption);

            // The cell lays its children out at their PREFERRED widths, side
            // by side from the left: a fixed text column, then the control
            // hugging it. Left to the authored flags the control kept its
            // cloned width and drifted to the far edge, leaving a dead gap
            // after every label.
            var h = cell.GetComponent<HorizontalLayoutGroup>();
            if (h != null)
            {
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = false;
                h.childAlignment = TextAnchor.MiddleLeft;
            }

            // Labels line up across rows because every text column is the
            // same width, and so do the controls after them.
            var text = Req(cell, "Text");
            var textLe = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            textLe.minWidth = textLe.preferredWidth = 700f;
            textLe.flexibleWidth = 0f;

            var cellLe = cell.GetComponent<LayoutElement>() ?? cell.gameObject.AddComponent<LayoutElement>();
            cellLe.flexibleWidth = 1f;

            if (!keepPill)
            {
                var pill = cell.Find("Pill");
                if (pill != null) Object.DestroyImmediate(pill.gameObject);
            }
            return cell;
        }

        /// <summary>The skirmish dropdown, renamed, taking the cell's spare width.</summary>
        static TMP_Dropdown Dropdown(Kit kit, Transform cell, string name)
        {
            var dd = Clone(Req(kit.OptResources, "Dropdown").gameObject, cell, name);
            var le = dd.GetComponent<LayoutElement>() ?? dd.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 72f;
            le.minWidth = le.preferredWidth = ControlWidth;
            le.flexibleWidth = 0f;
            return dd.GetComponent<TMP_Dropdown>();
        }

        /// <summary>
        /// A text field in the dropdown's plate, with the dropdown's caption
        /// label for its text — the same construction as the multiplayer
        /// lobby's HOST IP field, which is the one authored text field in
        /// this look.
        /// </summary>
        static TMP_InputField InputField(Kit kit, Transform cell, string name, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(cell, false);
            var img = go.GetComponent<Image>();
            img.sprite = kit.Plate;
            img.type = Image.Type.Sliced;
            img.color = TrackColour;
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 72f;
            le.minWidth = le.preferredWidth = ControlWidth;
            le.flexibleWidth = 0f;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var areaRt = (RectTransform)area.transform;
            Stretch(areaRt, 0f);
            areaRt.offsetMin = new Vector2(24f, 0f);
            areaRt.offsetMax = new Vector2(-24f, 0f);

            var caption = Req(kit.OptResources, "Dropdown/Label").gameObject;

            var ph = Clone(caption, area.transform, "Placeholder");
            Stretch((RectTransform)ph.transform, 0f);
            var phText = ph.GetComponent<TMP_Text>();
            phText.text = placeholder;
            phText.fontStyle = FontStyles.Italic;
            phText.alpha = 0.6f;
            phText.raycastTarget = false;

            var text = Clone(caption, area.transform, "Text");
            Stretch((RectTransform)text.transform, 0f);
            var textTmp = text.GetComponent<TMP_Text>();
            textTmp.text = string.Empty;
            textTmp.raycastTarget = false;

            var field = go.AddComponent<TMP_InputField>();
            field.targetGraphic = img;
            field.textViewport = areaRt;
            field.textComponent = textTmp;
            field.placeholder = phText;
            field.characterLimit = 24;
            return field;
        }

        /// <summary>
        /// A uGUI slider dressed in the same parts as the rest of the screen:
        /// the dropdown's plate as the track, the pill's gold as the filled
        /// part, the set's gem as the knob. Followed by a value label cloned
        /// from the pill's ON/OFF state text.
        /// </summary>
        static Slider Slider(Kit kit, Transform cell, string name, string valueName)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(cell, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 56f;
            le.minWidth = le.preferredWidth = ControlWidth - 150f - 16f;   // room for the value label
            le.flexibleWidth = 0f;

            var slider = go.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;

            var track = go.transform.Find("Background").GetComponent<Image>();
            track.sprite = kit.Plate;
            track.type = Image.Type.Sliced;
            track.color = TrackColour;
            var trackRt = (RectTransform)track.transform;
            trackRt.anchorMin = new Vector2(0f, 0.2f);
            trackRt.anchorMax = new Vector2(1f, 0.8f);

            var fillArea = (RectTransform)go.transform.Find("Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.2f);
            fillArea.anchorMax = new Vector2(1f, 0.8f);
            var fill = fillArea.Find("Fill").GetComponent<Image>();
            fill.sprite = kit.Plate;
            fill.type = Image.Type.Sliced;
            fill.color = FillColour;

            var handle = go.transform.Find("Handle Slide Area/Handle").GetComponent<Image>();
            handle.sprite = kit.Gem;
            handle.type = Image.Type.Simple;
            handle.preserveAspect = true;
            handle.color = KnobColour;
            ((RectTransform)handle.transform).sizeDelta = new Vector2(56f, 0f);

            var value = Clone(Req(kit.OptFog, "Pill/State").gameObject, cell, valueName);
            var valueLe = value.GetComponent<LayoutElement>() ?? value.AddComponent<LayoutElement>();
            valueLe.minWidth = valueLe.preferredWidth = 150f;
            var valueText = value.GetComponent<TMP_Text>();
            valueText.text = "100%";
            valueText.alignment = TextAlignmentOptions.MidlineRight;
            return slider;
        }

        /// <summary>A footer button at option-row size, for the language pair.</summary>
        static Button SmallButton(Kit kit, Transform cell, string name, string label)
        {
            var b = Clone(Req(kit.Footer, "BackButton").gameObject, cell, name);
            SetText(b.transform, "Label", label);
            var le = b.GetComponent<LayoutElement>() ?? b.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 440f;
            le.minHeight = le.preferredHeight = 110f;
            le.flexibleWidth = 0f;
            return b.GetComponent<Button>();
        }

        /// <summary>A filled box that takes the plate's spare height by default.</summary>
        static Transform Box(Kit kit, Transform parent, string name, Color fill)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = kit.Plate;
            img.type = Image.Type.Sliced;
            img.color = fill;
            img.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;
            return go.transform;
        }

        /// <summary>The inner gold border (Frame_Box_Medium_06) over a box.</summary>
        static void Frame(Kit kit, Transform box)
        {
            var go = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(box, false);
            Stretch((RectTransform)go.transform, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = kit.InnerFrame;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        // ── Primitives ──────────────────────────────────────────────────────

        /// <summary>A deep copy of a skirmish node, into the active scene. The
        /// original is never touched; its scene is not even dirtied.</summary>
        static GameObject Clone(GameObject source, Transform parent, string name)
        {
            var go = parent != null ? Object.Instantiate(source, parent, false) : Object.Instantiate(source);
            if (parent == null) SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.name = name;
            go.SetActive(true);
            return go;
        }

        static Transform Req(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null)
                throw new System.InvalidOperationException(
                    $"SkirmishMenu.unity: '{root.name}' has no '{path}' — the builder's map of that " +
                    "scene is stale.");
            return t;
        }

        static void SetText(Transform root, string path, string text)
            => Req(root, path).GetComponent<TMP_Text>().text = text;

        static void Stretch(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        static void Save(Scene scene, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new System.InvalidOperationException($"could not save {path}");
            RegisterScene(path);
            Debug.Log($"[MenuSceneBuilder] built {path}");
        }

        /// <summary>
        /// Put the scene in Build Settings after the other menu scenes. Menus
        /// sit outside MapSceneSync's managed roots, so nothing else will.
        /// </summary>
        static void RegisterScene(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => string.Equals(s.path, path, System.StringComparison.OrdinalIgnoreCase)))
                return;
            int at = list.FindLastIndex(s => s.path.StartsWith(MenusRoot, System.StringComparison.OrdinalIgnoreCase));
            list.Insert(at < 0 ? 0 : at + 1, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[MenuSceneBuilder] Build Settings: added {path}");
        }

        static bool EnsureNav(Scene scene, string name, string sceneName)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) return false;
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<MenuSceneLink>().SceneName = sceneName;
            Debug.Log($"[MenuSceneBuilder] MainMenu: added {name} -> {sceneName}");
            return true;
        }
    }
}
