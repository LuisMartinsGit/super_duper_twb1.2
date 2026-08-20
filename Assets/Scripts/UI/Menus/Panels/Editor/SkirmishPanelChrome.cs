// SkirmishPanelChrome.cs (editor-only)
// Dressing pass for the Skirmish panel built by MenuPanelsBuilder: the Synty
// title banner, the gold frames around the four content plates, and the
// CANCEL / START footer buttons.
//
// Run: Tools > Waning Border > Menu > Dress Skirmish Panel (Synty), then SAVE
// the scene.
//
// Why a separate pass instead of edits to MenuPanelsBuilder: the builder is a
// ONE-TIME scaffolder that refuses to touch a panel that already exists, and
// Panel_Skirmish has been hand-edited since (its TitleRule and the plates'
// curlicue corners were removed). This pass mutates the panel in place, keeps
// every Button, onClick entry and SkirmishPanel field reference intact, and is
// idempotent - running it twice changes nothing.
//
// Sizing note: the pass reads REAL laid-out rects rather than the values
// serialised in the scene. Panel_Skirmish ships inactive, so its layout groups
// have never run and every layout-driven child still carries the raw 200x200
// default in the YAML. The pass therefore activates the panel, force-rebuilds
// the layout, bakes its numbers from the result, and restores the active state.
//
// Slicing note: Synty UI art is authored at ~4x, and Frame_Box_Large_01 carries
// a 340px slice border. At the default pixels-per-unit multiplier of 1 those
// borders render as slabs; on a short plate (TheatreBar is 128px tall) even the
// standard 4x leaves the top and bottom borders overlapping. Every multiplier
// here is therefore DERIVED from the rect it has to fit, never hard-coded.

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    internal static class SkirmishPanelChrome
    {
        private const string MenuPath = "Tools/Waning Border/Menu/Dress Skirmish Panel (Synty)";
        private const string SpriteRoot = "Assets/Synty/InterfaceFantasyMenus/Sprites/FantasyMenus/";

        // Title banner. Menu_Button_01 is the ring-and-pennant plate: a round
        // boss on the left, a stretchable middle, a pointed tail (slice border
        // 500 / 0 / 130 / 0, so it stretches horizontally only). Swap this pair
        // for another Menu_Button_NN set to restyle the banner - anything with
        // a left/right-only border works unchanged.
        private const string BannerFill  = "SPR_FantasyMenus_Menu_Button_01_Background";
        private const string BannerFrame = "SPR_FantasyMenus_Menu_Button_01_Frame";

        // Footer buttons. Menu_Button_07 is the symmetric chevron plate
        // (border 450 / 0 / 450 / 0) and is pure gold, so it tints cleanly -
        // Menu_Button_12 bakes green into its ends and does not.
        private const string ButtonFill  = "SPR_FantasyMenus_Menu_Button_07_Background";
        private const string ButtonFrame = "SPR_FantasyMenus_Menu_Button_07_Frame";

        // Plates. Frame_Box_Large_01 is the ornate corner-curl box border.
        private const string PlateFill  = "SPR_FantasyMenus_Frame_Box_Large_01_Background";
        private const string PlateFrame = "SPR_FantasyMenus_Frame_Box_Large_01";

        /// <summary>Sibling index meaning "draw last", i.e. over everything
        /// else in the parent.</summary>
        private const int Last = -1;

        /// <summary>Node names this pass owns. Deleting them and re-running
        /// rebuilds the chrome from scratch.</summary>
        private const string BannerNode = "TitleBanner";
        private const string FrameNode  = "SyntyFrame";
        private const string FillNode   = "SyntyFill";

        // Palette - the builder's, so the dressing lands in the same key.
        private static readonly Color PlateBlue  = new Color(0.1302f, 0.2707f, 0.3679f, 0.85f);
        private static readonly Color BannerBlue = new Color(0.055f, 0.145f, 0.216f, 0.94f);
        private static readonly Color Gold       = new Color(0.910f, 0.722f, 0.290f);
        private static readonly Color GoldDim    = new Color(0.690f, 0.525f, 0.173f);
        private static readonly Color GoldFaint  = new Color(0.910f, 0.722f, 0.290f, 0.85f);

        // Button fills, applied through the ColorBlock so Unity's own tinting
        // drives the hover / press states.
        private static readonly Color CancelFill  = new Color(0.180f, 0.255f, 0.310f);
        private static readonly Color StartFill   = new Color(0.290f, 0.235f, 0.110f);
        private static readonly Color HoverBoost  = new Color(1.35f, 1.35f, 1.35f);
        private static readonly Color PressDamp   = new Color(0.75f, 0.75f, 0.75f);

        private static float S = 2f; // canvas scale (reference height / 1080)

        [MenuItem(MenuPath)]
        private static void Dress()
        {
            var panel = Find("Panel_Skirmish");
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Dress Skirmish Panel",
                    "No Panel_Skirmish found in the open scene. Open " +
                    "Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity " +
                    "first (the panel moved out of MainMenu.unity when the screens " +
                    "were split into separate scenes).", "OK");
                return;
            }

            var canvas = panel.GetComponentInParent<Canvas>();
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            S = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                ? Mathf.Max(1f, scaler.referenceResolution.y / 1080f)
                : 2f;

            var bannerFill  = Load(BannerFill);
            var bannerFrame = Load(BannerFrame);
            var buttonFill  = Load(ButtonFill);
            var buttonFrame = Load(ButtonFrame);
            var plateFill   = Load(PlateFill);
            var plateFrame  = Load(PlateFrame);
            if (bannerFill == null || bannerFrame == null || buttonFill == null ||
                buttonFrame == null || plateFill == null || plateFrame == null)
            {
                EditorUtility.DisplayDialog("Dress Skirmish Panel",
                    "One or more Synty sprites are missing under " + SpriteRoot +
                    ". Is the Interface Fantasy Menus package still imported?", "OK");
                return;
            }

            // Panel_Skirmish ships inactive; nothing below can measure a rect
            // until its layout groups have actually run once.
            bool wasActive = panel.gameObject.activeSelf;
            if (!wasActive)
            {
                Undo.RecordObject(panel.gameObject, "Dress Skirmish Panel");
                panel.gameObject.SetActive(true);
            }

            // Title margins first: they change the title's laid-out rect, and
            // the banner is baked FROM that rect.
            float ringInset = InsetForRing(bannerFrame);
            IndentHeaderText(panel, ringInset);
            Rebuild(panel);

            BuildBanner(panel, bannerFill, bannerFrame, ringInset);
            foreach (var name in new[] { "TheatreBar", "MapPreview", "MapOptions", "RosterPlate" })
                DressPlate(panel, name, plateFill, plateFrame);
            // Widths are design pixels, scaled by S like every builder constant.
            // Label colour is white on both: the screen's text is all white
            // now, so only the FRAME still separates primary from secondary.
            DressFooterButton(panel, "BackButton", "CANCEL", 220f,
                              CancelFill, GoldDim, Color.white, buttonFill, buttonFrame);
            DressFooterButton(panel, "PrimaryButton", "START", 280f,
                              StartFill, Gold, Color.white, buttonFill, buttonFrame);

            if (!wasActive) panel.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = panel.gameObject;
            Debug.Log("[SkirmishPanelChrome] Dressed Panel_Skirmish: title banner, four plate " +
                      "frames, CANCEL / START footer. SAVE THE SCENE.");
        }

        // ─────────────────────────────────────────────────────────────────
        // All-white text
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Repaint every authored label on the screen white.
        ///
        /// ALPHA IS PRESERVED. The screen leans on translucency for its
        /// hierarchy - captions under the option labels, the map blurb, the
        /// EMPTY roster placeholders are all part-transparent - and flattening
        /// those to solid white would erase the distinction between a heading
        /// and its explanation. A 60%-alpha white is still white.
        ///
        /// Separate menu item from the dressing pass because it is worth
        /// re-running on its own whenever new labels are added by hand.
        /// Inactive objects are included, so the row / legend / dropdown-item
        /// templates get whitened too and their clones come out white.
        /// </summary>
        [MenuItem("Tools/Waning Border/Menu/Whiten Skirmish Text")]
        private static void WhitenText()
        {
            var panel = Find("Panel_Skirmish");
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Whiten Skirmish Text",
                    "No Panel_Skirmish found in the open scene. Open " +
                    "Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity first.",
                    "OK");
                return;
            }

            int changed = 0, already = 0;
            foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                var c = label.color;
                if (c.r >= 1f && c.g >= 1f && c.b >= 1f) { already++; continue; }
                Undo.RecordObject(label, "Whiten Skirmish Text");
                label.color = new Color(1f, 1f, 1f, c.a);
                EditorUtility.SetDirty(label);
                changed++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[SkirmishPanelChrome] Whitened {changed} label(s); {already} " +
                      "were already white. Alpha left untouched. SAVE THE SCENE.");
        }

        // ─────────────────────────────────────────────────────────────────
        // 1. Title banner
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Left indent, in canvas pixels, that clears the banner's round boss.
        /// Derived from the sprite so a different banner sprite re-derives it:
        /// the multiplier is picked to keep the boss undistorted at the banner
        /// height, which makes the rendered boss width border / multiplier.
        /// </summary>
        private static float InsetForRing(Sprite frame)
        {
            float bannerH = BannerHeight();
            float multiplier = frame.rect.height / bannerH;
            return frame.border.x / multiplier + 24f * S;
        }

        /// <summary>Banner height: the 36pt title's cap height plus breathing
        /// room, in the same S-scaled units the builder used.</summary>
        private static float BannerHeight() => 62f * S;

        /// <summary>Push the eyebrow and title clear of the boss. TMP margins
        /// move the glyphs inside a layout-driven rect without fighting the
        /// VerticalLayoutGroup that owns that rect.</summary>
        private static void IndentHeaderText(RectTransform panel, float inset)
        {
            var header = panel.Find("Header");
            if (header == null) return;

            foreach (var name in new[] { "Eyebrow", "Title" })
            {
                var t = header.Find(name)?.GetComponent<TMP_Text>();
                if (t == null) continue;
                var m = t.margin;
                if (Mathf.Approximately(m.x, inset)) continue;
                Undo.RecordObject(t, "Dress Skirmish Panel");
                t.margin = new Vector4(inset, m.y, m.z, m.w);
                EditorUtility.SetDirty(t);
            }
        }

        private static void BuildBanner(RectTransform panel, Sprite fill, Sprite frame, float inset)
        {
            var header = panel.Find("Header") as RectTransform;
            var title = header != null ? header.Find("Title") as RectTransform : null;
            if (header == null || title == null) return;

            var banner = header.Find(BannerNode) as RectTransform;
            if (banner == null)
            {
                var go = new GameObject(BannerNode, typeof(RectTransform), typeof(LayoutElement));
                Undo.RegisterCreatedObjectUndo(go, "Dress Skirmish Panel");
                banner = (RectTransform)go.transform;
                banner.SetParent(header, false);
            }

            // Skipped by the header's VerticalLayoutGroup, and first in the
            // sibling order so the eyebrow and title draw over it.
            var le = banner.GetComponent<LayoutElement>()
                     ?? Undo.AddComponent<LayoutElement>(banner.gameObject);
            Undo.RecordObject(le, "Dress Skirmish Panel");
            le.ignoreLayout = true;
            banner.SetAsFirstSibling();

            // Bake the rect from the laid-out title: boss + glyphs + tail.
            float bannerH = BannerHeight();
            float textW = MeasureUnmargined(title.GetComponent<TMP_Text>(), title.rect.width * 0.4f);
            float tailW = frame.border.z / (frame.rect.height / bannerH);
            float bannerW = inset + textW + tailW + 28f * S;

            var titleCentre = HeaderLocalCentre(header, title);

            Undo.RecordObject(banner, "Dress Skirmish Panel");
            // Pinned to the header's LEFT edge, not its centre. The canvas
            // scaler matches on height, so the header's WIDTH moves with the
            // display aspect - a centre-anchored banner baked from the editor's
            // current game view would drift on any other monitor. Its height
            // and its distance from the header's mid-line do not move.
            banner.anchorMin = banner.anchorMax = banner.pivot = new Vector2(0f, 0.5f);
            banner.sizeDelta = new Vector2(bannerW, bannerH);
            banner.anchoredPosition = new Vector2(0f, titleCentre.y);

            float multiplier = frame.rect.height / bannerH;
            Layer(banner, FillNode,  fill,  BannerBlue, multiplier, 0);
            Layer(banner, FrameNode, frame, GoldFaint,  multiplier, 1);
        }

        /// <summary>
        /// Width of the glyphs alone, with the left indent taken back out.
        ///
        /// TMP folds the margin into its preferred width, and the indent this
        /// pass applies to the title is a margin - so measuring naively would
        /// count the boss twice on the first run and grow the banner by another
        /// boss-width on every re-run after that.
        /// </summary>
        private static float MeasureUnmargined(TMP_Text text, float fallback)
        {
            if (text == null) return fallback;
            var saved = text.margin;
            text.margin = new Vector4(0f, saved.y, saved.z, saved.w);
            text.ForceMeshUpdate();
            float width = text.GetPreferredValues().x;
            text.margin = saved;
            text.ForceMeshUpdate();
            return width;
        }

        /// <summary>Centre of <paramref name="child"/> expressed in
        /// <paramref name="header"/>'s local space, whatever anchors the layout
        /// group left on it.</summary>
        private static Vector2 HeaderLocalCentre(RectTransform header, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            var world = (corners[0] + corners[2]) * 0.5f;
            return header.InverseTransformPoint(world);
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. Plate frames
        // ─────────────────────────────────────────────────────────────────

        private static void DressPlate(RectTransform panel, string name, Sprite fill, Sprite frame)
        {
            var plate = FindDescendant(panel, name);
            if (plate == null) return;

            var bg = plate.GetComponent<Image>();
            if (bg == null) return;

            // The frame's slice border must fit twice over inside the plate or
            // the corner quads overlap - Unity's sliced Image does not clamp
            // them. TheatreBar is only 128px tall, which needs ~6x where the
            // taller plates are happy at the package-standard 4x. The border is
            // read off the sprite so swapping PlateFrame re-derives it.
            float border = Mathf.Max(Mathf.Max(frame.border.x, frame.border.y),
                                     Mathf.Max(frame.border.z, frame.border.w));
            float minDim = Mathf.Max(1f, Mathf.Min(plate.rect.width, plate.rect.height));
            float multiplier = Mathf.Max(4f, 2f * border / (0.9f * minDim));

            Undo.RecordObject(bg, "Dress Skirmish Panel");
            bg.sprite = fill;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = multiplier;
            bg.color = PlateBlue;
            EditorUtility.SetDirty(bg);

            // Two rules for a plate frame, both of which look fine in code:
            //  - ignoreLayout, or the plate's own layout group (a Vertical one
            //    on MapPreview / MapOptions / RosterPlate, a Horizontal one on
            //    TheatreBar) lays the stretched frame out as one more row;
            //  - LAST in the sibling order, so it draws over the plate's
            //    content. At index 0 it goes behind, and RosterPlate's scroll
            //    view paints its own background straight over the top of it.
            Layer(plate, FrameNode, frame, GoldFaint, multiplier, Last, ignoreLayout: true);
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. Footer buttons
        // ─────────────────────────────────────────────────────────────────

        private static void DressFooterButton(RectTransform panel, string node, string label,
            float width, Color fillTint, Color frameTint, Color labelTint,
            Sprite fill, Sprite frame)
        {
            var footer = panel.Find("Footer");
            var rt = footer != null ? footer.Find(node) as RectTransform : null;
            if (rt == null) return;

            var button = rt.GetComponent<Button>();
            var bg = rt.GetComponent<Image>();
            if (button == null || bg == null) return;

            var le = rt.GetComponent<LayoutElement>();
            float height = le != null && le.preferredHeight > 0f ? le.preferredHeight : rt.rect.height;
            if (height <= 1f) height = 48f * S;
            float multiplier = fill.rect.height / height;

            if (le != null)
            {
                Undo.RecordObject(le, "Dress Skirmish Panel");
                le.preferredWidth = width * S;
                EditorUtility.SetDirty(le);
            }

            // The button's OWN image is the fill, so the ColorBlock drives the
            // hover and press states for free. The frame rides above it as a
            // child, which keeps it out of the tint.
            Undo.RecordObject(bg, "Dress Skirmish Panel");
            bg.sprite = fill;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = multiplier;
            bg.color = Color.white;
            EditorUtility.SetDirty(bg);

            Undo.RecordObject(button, "Dress Skirmish Panel");
            var colors = button.colors;
            colors.normalColor = fillTint;
            colors.highlightedColor = fillTint * HoverBoost;
            colors.pressedColor = fillTint * PressDamp;
            colors.selectedColor = fillTint;
            colors.disabledColor = new Color(fillTint.r, fillTint.g, fillTint.b, 0.35f);
            button.colors = colors;
            button.targetGraphic = bg;
            EditorUtility.SetDirty(button);

            Layer(rt, FrameNode, frame, frameTint, multiplier, 0);

            var text = rt.Find("Label")?.GetComponent<TMP_Text>();
            if (text != null)
            {
                Undo.RecordObject(text, "Dress Skirmish Panel");
                text.text = label;
                text.color = labelTint;
                text.alignment = TextAlignmentOptions.Center;
                text.characterSpacing = 6f;
                EditorUtility.SetDirty(text);
                // Localisation reads the authored English string off the label
                // at runtime, so renaming here is the whole change - see
                // Loc.Pt.Menus.cs for the CANCEL / START entries.

            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Shared
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Create-or-update a stretched decorative Image child.</summary>
        private static void Layer(RectTransform parent, string name, Sprite sprite,
            Color tint, float multiplier, int siblingIndex, bool ignoreLayout = false)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(go, "Dress Skirmish Panel");
                rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
            }

            Undo.RecordObject(rt, "Dress Skirmish Panel");
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (siblingIndex == Last) rt.SetAsLastSibling();
            else rt.SetSiblingIndex(siblingIndex);

            if (ignoreLayout)
            {
                var le = rt.GetComponent<LayoutElement>();
                if (le == null) le = Undo.AddComponent<LayoutElement>(rt.gameObject);
                Undo.RecordObject(le, "Dress Skirmish Panel");
                le.ignoreLayout = true;
                EditorUtility.SetDirty(le);
            }

            var img = rt.GetComponent<Image>();
            if (img == null) img = Undo.AddComponent<Image>(rt.gameObject);
            Undo.RecordObject(img, "Dress Skirmish Panel");
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = multiplier;
            img.color = tint;
            img.raycastTarget = false;
            EditorUtility.SetDirty(img);
        }

        private static void Rebuild(RectTransform panel)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }

        private static RectTransform Find(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t as RectTransform;
            }
            return null;
        }

        private static RectTransform FindDescendant(RectTransform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t as RectTransform;
            return null;
        }

        private static Sprite Load(string file) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + file + ".png");
    }
}
#endif
