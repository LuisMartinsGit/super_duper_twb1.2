// MenuPanelsBuilder.cs (editor-only)
// ONE-TIME scaffolder: builds the Skirmish / Scenarios / Multiplayer panels
// as plain uGUI GameObjects under the MainMenu scene's Canvas, assigns every
// controller reference, and wires the blue menu's Menu_Item_* buttons to open
// them (persistent On Click () entries, visible in the Inspector).
//
// Sizing: every pixel value is multiplied by the canvas scale factor derived
// from the CanvasScaler reference resolution (the Synty menu canvas is
// 3840x2160, i.e. 2x a 1080p design).
// Detail: Synty "Interface Fantasy Menus" sprites — gradient plates, corner
// curlicues, arrow buttons, ornament lines under the titles and on primary
// buttons — all tinted to the blue menu's palette.
//
// Run: Tools ▸ Waning Border ▸ Menu ▸ Build Menu Panels (uGUI), then SAVE the
// scene. The GameObjects are then yours — the builder refuses to overwrite an
// existing panel (delete it manually to rebuild). Delete this file whenever;
// nothing at runtime depends on it.

#if UNITY_EDITOR
using TheWaningBorder.UI.Menus.Panels;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    internal static class MenuPanelsBuilder
    {
        private const string MenuPath = "Tools/Waning Border/Menu/Build Menu Panels (uGUI)";
        private const string SpriteRoot = "Assets/Synty/InterfaceFantasyMenus/Sprites/";

        /// <summary>Existence of this scene means the Skirmish screen has been
        /// split out of MainMenu.unity and this builder must not run.</summary>
        private const string SkirmishScenePath =
            "Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity";

        // Palette — sampled from the scene's blue menu.
        private static readonly Color PlateBlue   = new Color(0.1302f, 0.2707f, 0.3679f, 0.85f);
        private static readonly Color OverlayDark = new Color(0.02f, 0.045f, 0.06f, 0.92f);
        private static readonly Color WellDark    = new Color(0.06f, 0.12f, 0.16f, 0.95f);
        private static readonly Color RowDark     = new Color(0.039f, 0.055f, 0.067f, 0.9f);
        private static readonly Color TextMain    = new Color(0.886f, 0.910f, 0.898f);
        private static readonly Color TextDim     = new Color(0.784f, 0.824f, 0.808f, 0.60f);
        private static readonly Color Gold        = new Color(0.910f, 0.722f, 0.290f);
        private static readonly Color GoldDim     = new Color(0.690f, 0.525f, 0.173f);
        private static readonly Color GoldFaint   = new Color(0.910f, 0.722f, 0.290f, 0.65f);
        private static readonly Color HoverBlue   = new Color(0.25f, 0.41f, 0.53f, 0.38f);
        private static readonly Color PressBlue   = new Color(0.25f, 0.41f, 0.53f, 0.60f);

        // Canvas scale factor: reference height / 1080 (the design baseline).
        private static float S = 1f;

        private static Sprite _gradient, _curlicue, _arrowLeft, _arrowRight;
        private static Sprite _lineTop, _lineBottom, _rule;
        private static TMP_FontAsset _font;
        private static TMP_DefaultControls.Resources _tmpRes;

        [MenuItem(MenuPath)]
        private static void Build()
        {
            var canvas = FindCanvas();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Build Menu Panels",
                    "No Canvas found in the open scene. Open the MainMenu scene first.", "OK");
                return;
            }

            // Panel_Skirmish moved to SkirmishMenu.unity (SkirmishSceneSplit).
            // Without this guard the builder no longer finds it under the
            // MainMenu canvas and happily scaffolds a SECOND one there.
            if (System.IO.File.Exists(SkirmishScenePath))
            {
                EditorUtility.DisplayDialog("Build Menu Panels",
                    "The Skirmish screen lives in its own scene now " +
                    "(" + SkirmishScenePath + "), so this builder would create a " +
                    "duplicate under the MainMenu canvas. Delete that scene first " +
                    "if you really want to rebuild the panels from scratch.", "OK");
                return;
            }

            foreach (var name in new[] { "Panel_Skirmish", "Panel_Scenarios", "Panel_Multiplayer" })
            {
                if (canvas.transform.Find(name) != null)
                {
                    EditorUtility.DisplayDialog("Build Menu Panels",
                        $"'{name}' already exists under the Canvas. Delete it first if you " +
                        "want it rebuilt — the builder never overwrites your edits.", "OK");
                    return;
                }
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            S = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                ? Mathf.Max(1f, scaler.referenceResolution.y / 1080f)
                : Mathf.Max(1f, ((RectTransform)canvas.transform).rect.height / 1080f);
            // The panels shipped once at S=1 under a 3840x2160 canvas, i.e.
            // every pixel constant rendered at HALF its design size and the
            // whole screen was unreadable. If the canvas grows after a build,
            // re-run Tools > Waning Border > Menu > Rescale Menu Panels.
            Debug.Log($"[MenuPanelsBuilder] Canvas '{canvas.name}' reference " +
                      $"{(scaler != null ? scaler.referenceResolution.ToString() : "n/a")} " +
                      $"-> building at {S:0.##}x.");

            _gradient     = LoadSprite("General/SPR_FantasyMenus_Gradient_Vertical_01");
            _curlicue     = LoadSprite("FantasyMenus/SPR_FantasyMenus_Greeble_Curlicue_10");
            _arrowLeft    = LoadSprite("FantasyMenus/SPR_FantasyMenus_Button_Arrow_01_Left");
            _arrowRight   = LoadSprite("FantasyMenus/SPR_FantasyMenus_Button_Arrow_01_Right");
            _lineTop      = LoadSprite("FantasyMenus/SPR_FantasyMenus_Menu_Item_06_Top");
            _lineBottom   = LoadSprite("FantasyMenus/SPR_FantasyMenus_Menu_Item_06_Bottom");
            _rule         = LoadSprite("General/SPR_FantasyMenus_Line_Horizontal_01");
            _font = FindMenuFont();
            _tmpRes = new TMP_DefaultControls.Resources
            {
                standard   = Builtin("UISprite.psd"),
                background = Builtin("Background.psd"),
                inputField = Builtin("InputFieldBackground.psd"),
                knob       = Builtin("Knob.psd"),
                checkmark  = Builtin("Checkmark.psd"),
                dropdown   = Builtin("DropdownArrow.psd"),
                mask       = Builtin("UIMask.psd"),
            };

            var skirmish = BuildSkirmishPanel(canvas.transform);
            var scenarios = BuildScenariosPanel(canvas.transform);
            var multiplayer = BuildMultiplayerPanel(canvas.transform);

            WireOpenButton("Menu_Item_Skirmish", skirmish);
            WireOpenButton("Menu_Item_Scenarios", scenarios);
            WireOpenButton("Menu_Item_Multiplayer", multiplayer);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = skirmish;
            Debug.Log($"[MenuPanelsBuilder] Panels built at {S:0.##}x scale and menu buttons " +
                      "wired. SAVE THE SCENE. The GameObjects are yours to restyle; delete a " +
                      "panel and re-run the tool to rebuild it from scratch.");
        }

        // ─────────────────────────────────────────────────────────────────
        // SKIRMISH
        // ─────────────────────────────────────────────────────────────────

        private static GameObject BuildSkirmishPanel(Transform canvas)
        {
            var (root, panel) = PanelRoot<SkirmishPanel>(canvas, "Panel_Skirmish");
            Header(root, "SINGLE PLAYER", "SKIRMISH VS AI");

            // Left column — theatre bar / preview / options.
            var left = Column(root, "LeftColumn", 0.035f, 0.14f, 0.485f, 0.87f);

            var theatre = PlateRow(left, "TheatreBar", 64f);
            panel.PrevMapButton = ArrowButton(theatre, "PrevMapButton", _arrowLeft);
            var theatreText = VBox(theatre, "MapText", flexible: true);
            Label(theatreText, "MapEyebrow", "MAP", 13f, TextDim);
            var mapName = Label(theatreText, "MapName", "MAP NAME", 20f, Color.white, FontStyles.Bold);
            var mapTag = Label(theatre, "MapTag", "2P · SMALL", 13f, TextDim);
            AddLE(mapTag.rectTransform, -1f, 90f);
            panel.NextMapButton = ArrowButton(theatre, "NextMapButton", _arrowRight);

            var (_, previewWidget) = MapPreviewBlock(left, mapName, mapTag);
            panel.MapPreview = previewWidget;

            // Options plate — 2x2 grid of option cells.
            var options = Plate(left, "MapOptions", 190f);
            var optV = options.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(optV, 14, 14, 12, 12, 6);
            optV.childControlWidth = optV.childForceExpandWidth = true;
            optV.childControlHeight = true;
            Label(options, "OptionsHeader", "MAP OPTIONS", 13f, Gold, FontStyles.Bold);

            var row1 = HBox(options, "OptionsRow1", 70f);
            var row2 = HBox(options, "OptionsRow2", 70f);

            panel.ResourcesDropdown = OptionDropdownCell(row1, "OptResources",
                "STARTING RESOURCES", "Opening stockpile for all houses.");
            panel.AgeDropdown = OptionDropdownCell(row1, "OptAge",
                "STARTING AGE", "Begin with later units unlocked.");
            (panel.FogToggle, panel.FogState) = OptionPillCell(row2, "OptFog",
                "FOG OF WAR", "Scouts must uncover the map.");
            (panel.CurseToggle, panel.CurseState) = OptionPillCell(row2, "OptCurse",
                "CURSE NODES", "Capturable veilstone wells.");

            // Right column — roster.
            var right = Column(root, "RightColumn", 0.515f, 0.14f, 0.965f, 0.87f);
            var rosterPlate = Plate(right, "RosterPlate", 0f, flexible: true);
            var rosterV = rosterPlate.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(rosterV, 12, 12, 12, 12, 8);
            rosterV.childControlWidth = rosterV.childForceExpandWidth = true;
            rosterV.childControlHeight = true;

            var (scroll, content) = ScrollList(rosterPlate, "RosterScroll");
            AddLE((RectTransform)scroll.transform, 0f, -1f, flexibleH: 1f);
            panel.RosterContent = content;
            panel.RosterRowTemplate = RosterRowTemplate(content, withStrategy: true);
            panel.AddOpponentButton = TextButton(rosterPlate, "AddOpponentButton", "+  ADD OPPONENT", 17f, 40f);
            SetLabelColor(panel.AddOpponentButton, Gold);

            // Footer.
            var (back, error, begin) = Footer(root, "< MAIN MENU", "BEGIN SKIRMISH");
            panel.BackButton = back;
            panel.ErrorText = error;
            panel.BeginButton = begin;

            return root.gameObject;
        }

        // ─────────────────────────────────────────────────────────────────
        // SCENARIOS
        // ─────────────────────────────────────────────────────────────────

        private static GameObject BuildScenariosPanel(Transform canvas)
        {
            var (root, panel) = PanelRoot<ScenariosPanel>(canvas, "Panel_Scenarios");
            Header(root, "TRAINING GROUNDS", "SCENARIOS");

            // Left — the list.
            var left = Column(root, "ListColumn", 0.035f, 0.14f, 0.44f, 0.87f);
            var listPlate = Plate(left, "ListPlate", 0f, flexible: true);
            var listV = listPlate.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(listV, 10, 10, 10, 10, 4);
            listV.childControlWidth = listV.childForceExpandWidth = true;
            listV.childControlHeight = true;
            var (scroll, content) = ScrollList(listPlate, "ScenarioScroll");
            AddLE((RectTransform)scroll.transform, 0f, -1f, flexibleH: 1f);
            panel.ListContent = content;

            // Row template: transparent button, left-aligned label.
            var rowBtn = TextButton(content, "ScenarioRowTemplate", "Scenario", 16f, 40f);
            var rowLbl = rowBtn.GetComponentInChildren<TMP_Text>(true);
            rowLbl.alignment = TextAlignmentOptions.MidlineLeft;
            rowLbl.margin = new Vector4(14f * S, 0f, 0f, 0f);
            rowBtn.gameObject.SetActive(false);
            panel.ListRowTemplate = rowBtn.gameObject;

            // Right — the preview.
            var right = Column(root, "PreviewColumn", 0.475f, 0.14f, 0.965f, 0.87f);
            var prevPlate = Plate(right, "PreviewPlate", 0f, flexible: true);
            var prevV = prevPlate.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(prevV, 16, 16, 16, 16, 8);
            prevV.childControlWidth = prevV.childForceExpandWidth = true;
            prevV.childControlHeight = true;

            var thumbHolder = GO(prevPlate, "ThumbFrame");
            AddLE(thumbHolder, 300f);
            var thumbBg = thumbHolder.gameObject.AddComponent<Image>();
            thumbBg.color = WellDark;
            var thumb = Img(thumbHolder, "Thumbnail", Color.white);
            Stretch((RectTransform)thumb.transform);
            thumb.preserveAspect = true;
            panel.ThumbImage = thumb;
            var gem = Img(thumbHolder, "ThumbPlaceholder", GoldDim);
            var gemRt = (RectTransform)gem.transform;
            Center(gemRt, 22f, 22f);
            gemRt.localEulerAngles = new Vector3(0f, 0f, 45f);
            panel.ThumbPlaceholder = gem.gameObject;

            panel.NameText = Label(prevPlate, "ScenarioName", "SCENARIO", 24f, Color.white, FontStyles.Bold);
            Rule(prevPlate, "NameRule");
            var desc = Label(prevPlate, "ScenarioDescription", "", 15f, TextMain);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.alignment = TextAlignmentOptions.TopLeft;
            AddLE(desc.rectTransform, 0f, -1f, flexibleH: 1f);
            panel.DescriptionText = desc;

            var (back, _, start) = Footer(root, "< MAIN MENU", "START SCENARIO");
            panel.BackButton = back;
            panel.StartButton = start;

            return root.gameObject;
        }

        // ─────────────────────────────────────────────────────────────────
        // MULTIPLAYER
        // ─────────────────────────────────────────────────────────────────

        private static GameObject BuildMultiplayerPanel(Transform canvas)
        {
            var (root, panel) = PanelRoot<MultiplayerPanel>(canvas, "Panel_Multiplayer");
            Header(root, "CALL THE BANNERS", "MULTIPLAYER");

            // CHOICE pane.
            var choice = CenterPane(root, "Pane_Choice", 520f, 340f);
            panel.PaneChoice = choice.gameObject;
            panel.HostButton = OrnateButton(choice, "HostButton", "HOST GAME", 20f, 62f);
            Label(choice, "HostHint", "Open a LAN lobby others can join", 13f, TextDim)
                .alignment = TextAlignmentOptions.Center;
            panel.JoinButton = OrnateButton(choice, "JoinButton", "JOIN GAME", 20f, 62f);
            Label(choice, "JoinHint", "Browse games on the local network", 13f, TextDim)
                .alignment = TextAlignmentOptions.Center;

            // HOST SETUP pane.
            var setup = CenterPane(root, "Pane_HostSetup", 560f, 440f);
            panel.PaneHostSetup = setup.gameObject;
            Label(setup, "SetupHeader", "HOST GAME SETUP", 14f, Gold, FontStyles.Bold);
            Rule(setup, "SetupRule");
            panel.GameNameField = InputRow(setup, "GameNameRow", "GAME NAME");
            panel.PlayerNameField = InputRow(setup, "PlayerNameRow", "YOUR NAME");
            panel.PortField = InputRow(setup, "PortRow", "PORT");
            // No PLAYERS spinner. The lobby roster is the size control — an
            // eight-rung ladder whose top-most free rung adds a player, the
            // same shape the skirmish roster uses. See
            // MultiplayerPanel.RebuildSlots.
            panel.CreateButton = OrnateButton(setup, "CreateButton", "CREATE LOBBY", 18f, 56f);

            // BROWSE pane.
            var browse = CenterPane(root, "Pane_Browse", 680f, 500f);
            panel.PaneBrowse = browse.gameObject;
            Label(browse, "BrowseHeader", "AVAILABLE GAMES", 14f, Gold, FontStyles.Bold);
            Rule(browse, "BrowseRule");
            var (gamesScroll, gamesContent) = ScrollList(browse, "GamesScroll");
            AddLE((RectTransform)gamesScroll.transform, 0f, -1f, flexibleH: 1f);
            panel.GamesContent = gamesContent;

            var gameRow = GO(gamesContent, "GameRowTemplate");
            AddLE(gameRow, 48f);
            var gameRowImg = gameRow.gameObject.AddComponent<Image>();
            gameRowImg.color = RowDark;
            var gameRowH = gameRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            Pad(gameRowH, 12, 12, 6, 6, 10);
            gameRowH.childControlWidth = true;
            gameRowH.childControlHeight = true;
            gameRowH.childAlignment = TextAnchor.MiddleLeft;
            var gameLbl = Label(gameRow, "GameLabel", "Game", 16f, Color.white, FontStyles.Bold);
            AddLE(gameLbl.rectTransform, -1f, -1f, flexibleW: 1f);
            var joinBtn = TextButton(gameRow, "JoinButton", "JOIN", 15f, 36f, 110f);
            SetLabelColor(joinBtn, Gold);
            gameRow.gameObject.SetActive(false);
            panel.GameRowTemplate = gameRow.gameObject;

            Label(browse, "DirectHeader", "DIRECT CONNECT", 14f, Gold, FontStyles.Bold);
            var directRow = HBox(browse, "DirectRow", 46f);
            panel.IpField = InputInline(directRow, "IpField", "HOST IP", flexible: true);
            panel.DirectPortField = InputInline(directRow, "DirectPortField", "PORT", width: 150f);
            panel.DirectJoinButton = TextButton(directRow, "DirectJoinButton", "JOIN", 15f, 40f, 96f);
            SetLabelColor(panel.DirectJoinButton, Gold);

            // LOBBY pane — skirmish-style two columns.
            var lobby = GO(root, "Pane_Lobby");
            Anchor(lobby, 0f, 0f, 1f, 1f);
            panel.PaneLobby = lobby.gameObject;

            var left = Column(lobby, "LeftColumn", 0.035f, 0.14f, 0.485f, 0.87f);
            var theatre = PlateRow(left, "TheatreBar", 64f);
            panel.PrevMapButton = ArrowButton(theatre, "PrevMapButton", _arrowLeft);
            var theatreText = VBox(theatre, "MapText", flexible: true);
            Label(theatreText, "MapEyebrow", "MAP", 13f, TextDim);
            var mapName = Label(theatreText, "MapName", "MAP NAME", 20f, Color.white, FontStyles.Bold);
            var mapTag = Label(theatre, "MapTag", "", 13f, TextDim);
            AddLE(mapTag.rectTransform, -1f, 90f);
            panel.NextMapButton = ArrowButton(theatre, "NextMapButton", _arrowRight);

            var (_, previewWidget) = MapPreviewBlock(left, mapName, mapTag);
            panel.MapPreview = previewWidget;

            var options = Plate(left, "MatchOptions", 120f);
            var optV = options.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(optV, 14, 14, 12, 12, 6);
            optV.childControlWidth = optV.childForceExpandWidth = true;
            optV.childControlHeight = true;
            Label(options, "OptionsHeader", "MATCH OPTIONS", 13f, Gold, FontStyles.Bold);
            var optRow = HBox(options, "OptionsRow", 70f);
            (panel.FogToggle, panel.FogState) = OptionPillCell(optRow, "OptFog",
                "FOG OF WAR", "Scouts must uncover the map.");
            (panel.BorderToggle, panel.BorderState) = OptionPillCell(optRow, "OptBorder",
                "CURSE NODES", "Capturable veilstone wells.");

            var right = Column(lobby, "RightColumn", 0.515f, 0.14f, 0.965f, 0.87f);
            var slotsPlate = Plate(right, "SlotsPlate", 0f, flexible: true);
            var slotsV = slotsPlate.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(slotsV, 12, 12, 12, 12, 8);
            slotsV.childControlWidth = slotsV.childForceExpandWidth = true;
            slotsV.childControlHeight = true;
            panel.LobbyTitle = Label(slotsPlate, "LobbyTitle", "LOBBY", 14f, Gold, FontStyles.Bold);
            var (slotsScroll, slotsContent) = ScrollList(slotsPlate, "SlotsScroll");
            AddLE((RectTransform)slotsScroll.transform, 0f, -1f, flexibleH: 1f);
            panel.SlotsContent = slotsContent;
            panel.SlotRowTemplate = RosterRowTemplate(slotsContent, withStrategy: false);

            // CONNECTING pane.
            var connecting = CenterPane(root, "Pane_Connecting", 480f, 200f);
            panel.PaneConnecting = connecting.gameObject;
            Label(connecting, "ConnectingHeader", "CONNECTING...", 14f, Gold, FontStyles.Bold);
            panel.ConnectingLabel = Label(connecting, "ConnectingLabel", "Please wait...", 15f, TextMain);

            // Footer.
            var (back, error, start) = Footer(root, "< MAIN MENU", "START MATCH");
            panel.BackButton = back;
            panel.BackLabel = back.GetComponentInChildren<TMP_Text>(true);
            panel.ErrorText = error;
            panel.StartButton = start;

            // Non-default pane states.
            setup.gameObject.SetActive(false);
            browse.gameObject.SetActive(false);
            lobby.gameObject.SetActive(false);
            connecting.gameObject.SetActive(false);

            return root.gameObject;
        }

        // ─────────────────────────────────────────────────────────────────
        // Shared building blocks
        // ─────────────────────────────────────────────────────────────────

        private static (RectTransform, T) PanelRoot<T>(Transform canvas, string name)
            where T : Component
        {
            var root = GO(canvas, name);
            Stretch(root);
            var overlay = root.gameObject.AddComponent<Image>();
            overlay.color = OverlayDark; // also blocks clicks to the menu behind
            var panel = root.gameObject.AddComponent<T>();
            root.gameObject.SetActive(false); // panels open via SetActive(true)
            return (root, panel);
        }

        private static void Header(RectTransform root, string eyebrow, string title)
        {
            var header = GO(root, "Header");
            Anchor(header, 0.035f, 0.88f, 0.7f, 0.99f);
            var v = header.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.LowerLeft;
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.spacing = 2f * S;
            var eb = Label(header, "Eyebrow", eyebrow, 14f, GoldDim, FontStyles.Bold);
            eb.characterSpacing = 8f;
            var t = Label(header, "Title", title, 36f, Gold, FontStyles.Bold);
            t.characterSpacing = 6f;
            Rule(header, "TitleRule");
        }

        // Gold ornament rule (Synty horizontal line sprite).
        private static void Rule(RectTransform parent, string name)
        {
            var rule = GO(parent, name);
            AddLE(rule, 8f);
            var img = rule.gameObject.AddComponent<Image>();
            img.sprite = _rule;
            img.color = GoldFaint;
            img.raycastTarget = false;
            img.preserveAspect = false;
        }

        private static RectTransform Column(RectTransform root, string name,
            float minX, float minY, float maxX, float maxY)
        {
            var col = GO(root, name);
            Anchor(col, minX, minY, maxX, maxY);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12f * S;
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            return col;
        }

        // A blue gradient plate with Synty curlicue corner ornaments.
        private static RectTransform Plate(RectTransform parent, string name,
            float height, bool flexible = false)
        {
            var plate = GO(parent, name);
            var img = plate.gameObject.AddComponent<Image>();
            img.sprite = _gradient;
            img.type = Image.Type.Simple;
            img.color = PlateBlue;
            AddLE(plate, flexible ? 0f : height, -1f, flexibleH: flexible ? 1f : 0f);
            Corners(plate);
            return plate;
        }

        // Four gold curlicues pinned to the plate corners (mirrored by scale).
        private static void Corners(RectTransform plate)
        {
            if (_curlicue == null) return;
            float size = 44f * S;
            AddCorner(plate, "Corner_TL", new Vector2(0f, 1f), new Vector3(1f, 1f, 1f));
            AddCorner(plate, "Corner_TR", new Vector2(1f, 1f), new Vector3(-1f, 1f, 1f));
            AddCorner(plate, "Corner_BL", new Vector2(0f, 0f), new Vector3(1f, -1f, 1f));
            AddCorner(plate, "Corner_BR", new Vector2(1f, 0f), new Vector3(-1f, -1f, 1f));

            void AddCorner(RectTransform p, string n, Vector2 anchor, Vector3 scale)
            {
                var c = GO(p, n);
                c.anchorMin = c.anchorMax = c.pivot = anchor;
                c.sizeDelta = new Vector2(size, size);
                c.anchoredPosition = Vector2.zero;
                c.localScale = scale;
                var i = c.gameObject.AddComponent<Image>();
                i.sprite = _curlicue;
                i.color = GoldFaint;
                i.raycastTarget = false;
                i.preserveAspect = true;
            }
        }

        private static RectTransform PlateRow(RectTransform parent, string name, float height)
        {
            var plate = Plate(parent, name, height);
            var h = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            Pad(h, 12, 12, 8, 8, 10);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            return plate;
        }

        // Map preview plate + wired MapPreviewWidget. The thumbnail sits in an
        // upright square: a map thumbnail is rendered north-up, and tilting it
        // 45° made every landmark read at a diagonal for no gain. The node
        // names ("DiamondStage" / "Diamond") are kept - the scenes bind
        // MapPreviewWidget's fields to them by fileID, but MenuPanelTools and
        // the panel builders still look several of them up by name.
        private static (RectTransform, MapPreviewWidget) MapPreviewBlock(
            RectTransform parent, TMP_Text mapName, TMP_Text mapTag)
        {
            var plate = Plate(parent, "MapPreview", 0f, flexible: true);
            var v = plate.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(v, 14, 14, 12, 12, 6);
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;

            var stage = GO(plate, "DiamondStage");
            AddLE(stage, 300f);

            // Was 195 on a 45°-rotated square, i.e. 276 corner to corner. The
            // upright square keeps that on-screen footprint rather than the
            // old edge length, so un-tilting did not shrink the map.
            float side = 276f * S;
            var diamond = GO(stage, "Diamond");
            Center(diamond, side, side);
            var diamondBg = diamond.gameObject.AddComponent<Image>();
            diamondBg.color = WellDark;

            var thumbGo = new GameObject("Thumbnail", typeof(RectTransform), typeof(RawImage));
            var thumbRt = (RectTransform)thumbGo.transform;
            thumbRt.SetParent(diamond, false);
            Stretch(thumbRt);
            var thumb = thumbGo.GetComponent<RawImage>();

            // Placeholder shown when a map has no baked thumbnail. It used to
            // read as a gem for free, riding the 45° the whole preview carried;
            // on the upright square it has to tilt itself or it is just a box.
            var gem = Img(diamond, "Gem", GoldDim);
            var gemRt = (RectTransform)gem.transform;
            Center(gemRt, 20f, 20f);
            gemRt.localEulerAngles = new Vector3(0f, 0f, 45f);

            var markerLayer = GO(diamond, "MarkerLayer");
            Stretch(markerLayer);
            var marker = Img(markerLayer, "MarkerTemplate", Color.white);
            var markerRt = (RectTransform)marker.transform;
            markerRt.sizeDelta = new Vector2(10f * S, 10f * S);
            marker.gameObject.SetActive(false);

            // No frame ornament here: the Synty Frame_Diamond_01 art that used
            // to ring the tilted thumbnail is diamond-shaped and cannot sit on
            // a square. The MapPreview plate carries its own box frame
            // (SkirmishPanelChrome), which is the border the preview needs.

            var desc = Label(plate, "MapDescription", "", 13f, TextDim, FontStyles.Italic);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.alignment = TextAlignmentOptions.TopLeft;
            AddLE(desc.rectTransform, 58f);

            var legend = GO(plate, "Legend");
            AddLE(legend, 22f);
            var legendH = legend.gameObject.AddComponent<HorizontalLayoutGroup>();
            legendH.spacing = 14f * S;
            legendH.childControlWidth = true;
            legendH.childControlHeight = true;
            legendH.childForceExpandWidth = false;
            legendH.childAlignment = TextAnchor.MiddleLeft;

            var legendItem = GO(legend, "LegendTemplate");
            var itemH = legendItem.gameObject.AddComponent<HorizontalLayoutGroup>();
            itemH.spacing = 5f * S;
            itemH.childControlWidth = true;
            itemH.childControlHeight = true;
            itemH.childForceExpandWidth = false;
            itemH.childAlignment = TextAnchor.MiddleLeft;
            var swatch = Img(legendItem, "Swatch", Color.white);
            AddLE((RectTransform)swatch.transform, 8f, 8f);
            Label(legendItem, "LegendLabel", "STARTS (0)", 12f, TextDim);
            legendItem.gameObject.SetActive(false);

            var widget = plate.gameObject.AddComponent<MapPreviewWidget>();
            widget.MapName = mapName;
            widget.MapTag = mapTag;
            widget.Diamond = thumb;
            widget.DiamondGem = gem.gameObject;
            widget.MarkerLayer = markerLayer;
            widget.MarkerTemplate = marker.gameObject;
            widget.Description = desc;
            widget.LegendContainer = legend;
            widget.LegendTemplate = legendItem.gameObject;
            return (plate, widget);
        }

        // Option cell: label + caption left, TMP dropdown right.
        private static TMP_Dropdown OptionDropdownCell(RectTransform row, string name,
            string label, string caption)
        {
            var cell = OptionCell(row, name, label, caption);

            var ddGo = TMP_DefaultControls.CreateDropdown(_tmpRes);
            ddGo.name = "Dropdown";
            var ddRt = (RectTransform)ddGo.transform;
            ddRt.SetParent(cell, false);
            AddLE(ddRt, 36f, 130f);
            var dd = ddGo.GetComponent<TMP_Dropdown>();
            StyleControl(ddGo);
            return dd;
        }

        // Option cell with an ON/OFF pill button.
        private static (Button, TMP_Text) OptionPillCell(RectTransform row, string name,
            string label, string caption)
        {
            var cell = OptionCell(row, name, label, caption);

            var pillRow = GO(cell, "Pill");
            AddLE(pillRow, 26f, 96f);
            var h = pillRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f * S;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleRight;

            var state = Label(pillRow, "State", "ON", 14f, Gold, FontStyles.Bold);
            var track = Img(pillRow, "Track", GoldDim);
            AddLE((RectTransform)track.transform, 24f, 48f);
            var btn = track.gameObject.AddComponent<Button>();
            btn.targetGraphic = track;
            return (btn, state);
        }

        private static RectTransform OptionCell(RectTransform row, string name,
            string label, string caption)
        {
            var cell = GO(row, name);
            AddLE(cell, -1f, -1f, flexibleW: 1f);
            var h = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
            Pad(h, 4, 10, 4, 4, 8);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            var text = VBox(cell, "Text", flexible: true);
            Label(text, "Label", label, 14f, TextMain, FontStyles.Bold);
            var cap = Label(text, "Caption", caption, 12f, TextDim, FontStyles.Italic);
            cap.textWrappingMode = TextWrappingModes.Normal;
            return cell;
        }

        // Roster/slot row template (inactive): color strip, team chip, host
        // badge, name, dropdowns or cycle buttons, remove.
        private static GameObject RosterRowTemplate(RectTransform content, bool withStrategy)
        {
            var row = GO(content, "RosterRowTemplate");
            AddLE(row, 54f);
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = RowDark;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            Pad(h, 10, 10, 8, 8, 10);
            h.childControlWidth = true;
            h.childControlHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            var strip = Img(row, "ColorStrip", Color.white);
            var stripRt = (RectTransform)strip.transform;
            AddLE(stripRt, -1f, 6f);
            stripRt.sizeDelta = new Vector2(6f * S, 36f * S);
            var stripBtn = strip.gameObject.AddComponent<Button>();
            stripBtn.targetGraphic = strip;

            var chip = GO(row, "TeamChip");
            AddLE(chip, 30f, 86f);
            var chipImg = chip.gameObject.AddComponent<Image>();
            chipImg.color = new Color(0f, 0f, 0f, 0f);
            var chipOutline = chip.gameObject.AddComponent<Outline>();
            chipOutline.effectColor = GoldDim;
            chipOutline.effectDistance = new Vector2(1f, 1f) * S;
            var chipLbl = Label(chip, "ChipLabel", "TEAM 1", 13f, Gold, FontStyles.Bold);
            Stretch(chipLbl.rectTransform);
            chipLbl.alignment = TextAlignmentOptions.Center;

            var badge = GO(row, "HostBadge");
            AddLE(badge, 24f, 58f);
            var badgeImg = badge.gameObject.AddComponent<Image>();
            badgeImg.color = GoldDim;
            var badgeLbl = Label(badge, "BadgeLabel", "HOST", 12f, new Color(0.07f, 0.055f, 0.016f), FontStyles.Bold);
            Stretch(badgeLbl.rectTransform);
            badgeLbl.alignment = TextAlignmentOptions.Center;

            var name = Label(row, "NameLabel", "PLAYER", 16f, Color.white, FontStyles.Bold);
            AddLE(name.rectTransform, -1f, -1f, flexibleW: 1f);

            if (withStrategy)
            {
                var strat = TMP_DefaultControls.CreateDropdown(_tmpRes);
                strat.name = "StrategyDropdown";
                ((RectTransform)strat.transform).SetParent(row, false);
                AddLE((RectTransform)strat.transform, 36f, 165f);
                StyleControl(strat);

                var diff = TMP_DefaultControls.CreateDropdown(_tmpRes);
                diff.name = "DifficultyDropdown";
                ((RectTransform)diff.transform).SetParent(row, false);
                AddLE((RectTransform)diff.transform, 36f, 135f);
                StyleControl(diff);

                var remove = TextButton(row, "RemoveButton", "X", 14f, 32f, 36f);
                SetLabelColor(remove, TextDim);
            }
            else
            {
                var ai = TextButton(row, "AiButton", "AI", 14f, 32f, 76f);
                SetLabelColor(ai, Gold);
                var diffBtn = TextButton(row, "DifficultyButton", "NORMAL", 14f, 32f, 120f);
                SetLabelColor(diffBtn, TextMain);
            }

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static RectTransform CenterPane(RectTransform root, string name,
            float width, float height)
        {
            var pane = GO(root, name);
            pane.anchorMin = pane.anchorMax = pane.pivot = new Vector2(0.5f, 0.5f);
            pane.sizeDelta = new Vector2(width * S, height * S);
            var img = pane.gameObject.AddComponent<Image>();
            img.sprite = _gradient;
            img.color = PlateBlue;
            var v = pane.gameObject.AddComponent<VerticalLayoutGroup>();
            Pad(v, 24, 24, 20, 20, 10);
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            Corners(pane);
            return pane;
        }

        private static (Button back, TMP_Text error, Button primary) Footer(
            RectTransform root, string backText, string primaryText)
        {
            var footer = GO(root, "Footer");
            Anchor(footer, 0.035f, 0.03f, 0.965f, 0.11f);
            var h = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f * S;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            var back = TextButton(footer, "BackButton", backText, 17f, 52f, 240f);
            var error = Label(footer, "ErrorText", "", 15f, new Color(1f, 0.5f, 0.5f));
            error.alignment = TextAlignmentOptions.MidlineRight;
            AddLE(error.rectTransform, -1f, -1f, flexibleW: 1f);
            var primary = OrnateButton(footer, "PrimaryButton", primaryText, 18f, 52f, 300f);
            return (back, error, primary);
        }

        // ─────────────────────────────────────────────────────────────────
        // Primitives (every pixel value here is scaled by S)
        // ─────────────────────────────────────────────────────────────────

        private static RectTransform GO(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h); // caller pre-scales when needed
        }

        private static Image Img(Transform parent, string name, Color color)
        {
            var rt = GO(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text Label(Transform parent, string name, string text,
            float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var rt = GO(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = text;
            t.fontSize = size * S;
            t.color = color;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
            return t;
        }

        // Flat blue-menu style button: transparent at rest, steel-blue hover.
        private static Button TextButton(Transform parent, string name, string label,
            float fontSize, float height, float width = -1f)
        {
            var rt = GO(parent, name);
            AddLE(rt, height, width);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = HoverBlue;
            colors.pressedColor = PressBlue;
            colors.selectedColor = HoverBlue;
            colors.disabledColor = new Color(1f, 1f, 1f, 0f);
            btn.colors = colors;

            var lbl = Label(rt, "Label", label, fontSize, TextMain, FontStyles.Bold);
            Stretch(lbl.rectTransform);
            lbl.alignment = TextAlignmentOptions.Center;
            return btn;
        }

        // Primary action button dressed with the Synty menu-item ornament
        // lines above and below the label (the blue menu's selected look).
        private static Button OrnateButton(Transform parent, string name, string label,
            float fontSize, float height, float width = -1f)
        {
            var btn = TextButton(parent, name, label, fontSize, height, width);
            SetLabelColor(btn, Gold);
            var rt = (RectTransform)btn.transform;

            if (_lineTop != null)
            {
                var top = Img(rt, "OrnamentTop", GoldFaint);
                top.sprite = _lineTop;
                top.preserveAspect = true;
                var trt = (RectTransform)top.transform;
                trt.anchorMin = new Vector2(0.05f, 1f);
                trt.anchorMax = new Vector2(0.95f, 1f);
                trt.pivot = new Vector2(0.5f, 1f);
                trt.sizeDelta = new Vector2(0f, 12f * S);
                trt.anchoredPosition = Vector2.zero;
            }
            if (_lineBottom != null)
            {
                var bottom = Img(rt, "OrnamentBottom", GoldFaint);
                bottom.sprite = _lineBottom;
                bottom.preserveAspect = true;
                var brt = (RectTransform)bottom.transform;
                brt.anchorMin = new Vector2(0.05f, 0f);
                brt.anchorMax = new Vector2(0.95f, 0f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.sizeDelta = new Vector2(0f, 12f * S);
                brt.anchoredPosition = Vector2.zero;
            }
            return btn;
        }

        // Synty arrow sprite button (map prev/next).
        private static Button ArrowButton(Transform parent, string name, Sprite arrow)
        {
            var rt = GO(parent, name);
            AddLE(rt, 44f, 44f);
            var img = rt.gameObject.AddComponent<Image>();
            if (arrow != null)
            {
                img.sprite = arrow;
                img.color = Gold;
                img.preserveAspect = true;
            }
            else
            {
                img.color = new Color(1f, 1f, 1f, 0f);
            }
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = arrow != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1.1f, 1.05f, 0.9f, 1f);
            colors.pressedColor = new Color(0.8f, 0.75f, 0.6f, 1f);
            btn.colors = colors;
            if (arrow == null)
            {
                var lbl = Label(rt, "Label", name.Contains("Prev") ? "<" : ">", 26f, Gold, FontStyles.Bold);
                Stretch(lbl.rectTransform);
                lbl.alignment = TextAlignmentOptions.Center;
            }
            return btn;
        }

        private static void SetLabelColor(Button btn, Color color)
        {
            var t = btn.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.color = color;
        }

        private static RectTransform HBox(RectTransform parent, string name, float height)
        {
            var row = GO(parent, name);
            AddLE(row, height);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f * S;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            return row;
        }

        private static RectTransform VBox(RectTransform parent, string name, bool flexible)
        {
            var box = GO(parent, name);
            if (flexible) AddLE(box, -1f, -1f, flexibleW: 1f);
            var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            return box;
        }

        // ScrollRect (viewport = itself) + VerticalLayoutGroup content.
        private static (GameObject, RectTransform) ScrollList(RectTransform parent, string name)
        {
            var scrollGo = new GameObject(name,
                typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(parent, false);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(scrollRt, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            // A fresh RectTransform starts at 100x100. With the anchors
            // stretched horizontally that leaves the content 100px WIDER than
            // its viewport (rows overhang, the last control drifts off the
            // right edge). The vertical fitter drives y; x must be flush.
            content.sizeDelta = Vector2.zero;

            var v = contentGo.GetComponent<VerticalLayoutGroup>();
            v.childControlWidth = v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            Pad(v, 6, 6, 6, 6, 8);

            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.viewport = scrollRt;
            sr.content = content;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 45f * S;
            return (scrollGo, content);
        }

        // "LABEL  [input]" row using the TMP factory (full input hierarchy).
        private static TMP_InputField InputRow(RectTransform parent, string name, string label)
        {
            var row = HBox(parent, name, 46f);
            var lbl = Label(row, "Label", label, 13f, TextDim, FontStyles.Bold);
            AddLE(lbl.rectTransform, -1f, 130f);
            return MakeInput(row, flexible: true);
        }

        private static TMP_InputField InputInline(RectTransform parent, string name,
            string placeholder, bool flexible = false, float width = -1f)
        {
            var field = MakeInput(parent, flexible, width);
            field.gameObject.name = name;
            var ph = field.placeholder as TMP_Text;
            if (ph != null) ph.text = placeholder;
            return field;
        }

        private static TMP_InputField MakeInput(RectTransform parent, bool flexible, float width = -1f)
        {
            var go = TMP_DefaultControls.CreateInputField(_tmpRes);
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            AddLE(rt, 40f, width, flexibleW: flexible ? 1f : 0f);
            StyleControl(go);
            var img = go.GetComponent<Image>();
            if (img != null) img.color = WellDark;
            return go.GetComponent<TMP_InputField>();
        }

        // Apply the menu font + dark styling to a TMP factory control.
        private static void StyleControl(GameObject control)
        {
            foreach (var t in control.GetComponentsInChildren<TMP_Text>(true))
            {
                if (_font != null) t.font = _font;
                t.fontSize = 14f * S;
                t.color = TextMain;
            }
            foreach (var img in control.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject == control) img.color = WellDark;
                else if (img.gameObject.name == "Item Background" || img.gameObject.name == "Template")
                    img.color = WellDark;
            }
        }

        private static void AddLE(RectTransform rt, float height, float width = -1f,
            float flexibleW = 0f, float flexibleH = 0f)
        {
            var le = rt.gameObject.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            if (height >= 0f) { le.minHeight = le.preferredHeight = height * S; }
            if (width >= 0f) { le.minWidth = le.preferredWidth = width * S; }
            le.flexibleWidth = flexibleW;
            le.flexibleHeight = flexibleH;
        }

        private static void Pad(HorizontalOrVerticalLayoutGroup g,
            int left, int right, int top, int bottom, float spacing)
        {
            g.padding = new RectOffset(
                Mathf.RoundToInt(left * S), Mathf.RoundToInt(right * S),
                Mathf.RoundToInt(top * S), Mathf.RoundToInt(bottom * S));
            g.spacing = spacing * S;
        }

        // ─────────────────────────────────────────────────────────────────
        // Scene lookups + wiring
        // ─────────────────────────────────────────────────────────────────

        private static void WireOpenButton(string buttonName, GameObject panel)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != buttonName) continue;
                    var btn = t.GetComponent<Button>();
                    if (btn == null) continue;
                    UnityEventTools.AddBoolPersistentListener(btn.onClick, panel.SetActive, true);
                    EditorUtility.SetDirty(btn);
                    return;
                }
            }
            Debug.LogWarning($"[MenuPanelsBuilder] Button '{buttonName}' not found — wire its " +
                             $"On Click () to {panel.name}.SetActive(true) manually.");
        }

        private static Canvas FindCanvas()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<Canvas>(true);
                if (c != null) return c.rootCanvas != null ? c.rootCanvas : c;
            }
            return null;
        }

        private static TMP_FontAsset FindMenuFont()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    if (t.font != null) return t.font;
            return TMP_Settings.defaultFontAsset;
        }

        private static Sprite LoadSprite(string relativePath) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + relativePath + ".png");

        private static Sprite Builtin(string name) =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>($"UI/Skin/{name}");
    }
}
#endif
