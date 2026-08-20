// MapOptionsChrome.cs (editor-only)
// Dresses the MAP OPTIONS section of the Skirmish screen in Synty "Interface
// Fantasy Menus" art: the two boolean options become sliding gem switches, the
// two dropdowns get the gold interlocking-diamond plate, and the section header
// gets a flourish under it.
//
// Run: Tools > Waning Border > Menu > Dress Map Options (Synty), with
// Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity open. Then SAVE.
//
// Idempotent - every node it makes is found by name and reused, so running it
// again only refreshes the art. Registers Undo.
//
// Structure it expects (built by MenuPanelsBuilder, since hand-edited):
//   OptionsHeader                        "MAP OPTIONS"
//   OptionsRow1 / OptionsRow2
//     OptResources / OptAge              Text(Label, Caption) + Dropdown
//     OptFog / OptCurse                  Text(Label, Caption) + Pill(State, Track)
// Anything it cannot find is skipped and reported, never guessed at - the
// skirmish screen has been restructured by hand more than once.
//
// The OBSERVER option is NOT here: SkirmishPanel builds it at runtime and
// borrows this pass's sprites off the fog switch, so it dresses itself.

#if UNITY_EDITOR
using System.Collections.Generic;
using TheWaningBorder.UI.Menus;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    internal static class MapOptionsChrome
    {
        private const string MenuPath = "Tools/Waning Border/Menu/Dress Map Options (Synty)";
        private const string SpriteRoot = "Assets/Synty/InterfaceFantasyMenus/Sprites/";

        // Toggle: Menu_Button_17 is the pill Synty's own settings toggle uses,
        // and it carries a full four-way slice border so it holds its corners
        // at any size. The knob is the Gem_Active sprite - near-white, so it
        // takes a tint cleanly where the plain Gem sprite is baked blue.
        private const string TrackFill = "FantasyMenus/SPR_FantasyMenus_Menu_Button_17_Frame_Background";
        private const string TrackOutline = "FantasyMenus/SPR_FantasyMenus_Menu_Button_17_Frame_Left";
        private const string Knob = "FantasyMenus/SPR_FantasyMenus_Menu_Button_15_Gem_Active";

        // Dropdown: Menu_Button_05, the interlocking-diamond plate. Horizontal
        // slice border only (350/0/350/0), so its multiplier comes from height.
        private const string DropFill = "FantasyMenus/SPR_FantasyMenus_Menu_Button_05_Background";
        private const string DropFrame = "FantasyMenus/SPR_FantasyMenus_Menu_Button_05_Frame";
        private const string DropArrow = "General/SPR_FantasyMenus_Arrow_Small_01";
        private const string ListFill = "FantasyMenus/SPR_FantasyMenus_Frame_Box_Large_01_Background";
        private const string ListFrame = "FantasyMenus/SPR_FantasyMenus_Frame_Box_Large_01";
        private const string HeaderRule = "FantasyMenus/SPR_FantasyMenus_Menu_Item_14";

        private const string FrameNode = "SyntyFrame";
        private const string RuleNode = "HeaderRule";

        private static readonly Color Gold      = new Color(0.910f, 0.722f, 0.290f);
        private static readonly Color GoldFaint = new Color(0.910f, 0.722f, 0.290f, 0.85f);
        private static readonly Color WellDark  = new Color(0.055f, 0.090f, 0.118f, 0.95f);
        private static readonly Color ListDark  = new Color(0.043f, 0.075f, 0.102f, 0.98f);

        private static float S = 2f;

        [MenuItem(MenuPath)]
        private static void Dress()
        {
            var panel = Find("Panel_Skirmish");
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Dress Map Options",
                    "No Panel_Skirmish in the open scene. Open " +
                    "Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity first.", "OK");
                return;
            }

            var canvas = panel.GetComponentInParent<Canvas>();
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            S = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                ? Mathf.Max(1f, scaler.referenceResolution.y / 1080f)
                : 2f;

            var art = new Dictionary<string, Sprite>();
            foreach (var path in new[] { TrackFill, TrackOutline, Knob, DropFill, DropFrame,
                                         DropArrow, ListFill, ListFrame, HeaderRule })
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + path + ".png");
                if (sprite == null)
                {
                    EditorUtility.DisplayDialog("Dress Map Options",
                        "Missing Synty sprite:\n" + SpriteRoot + path + ".png", "OK");
                    return;
                }
                art[path] = sprite;
            }

            // Panel_Skirmish is active in its own scene now, but guard anyway -
            // an inactive subtree has never run its layout, so every rect read
            // below would be the raw serialised default.
            bool wasActive = panel.gameObject.activeSelf;
            if (!wasActive) panel.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            var missing = new List<string>();
            int toggles = 0, drops = 0;

            foreach (var cell in new[] { "OptFog", "OptCurse" })
            {
                var track = Descendant(panel, cell, "Track");
                if (track == null) { missing.Add(cell + "/Pill/Track"); continue; }
                DressToggle(track, art);
                toggles++;
            }

            foreach (var cell in new[] { "OptResources", "OptAge" })
            {
                var dd = Descendant(panel, cell, "Dropdown");
                var dropdown = dd != null ? dd.GetComponent<TMP_Dropdown>() : null;
                if (dropdown == null) { missing.Add(cell + "/Dropdown"); continue; }
                DressDropdown(dropdown, art);
                drops++;
            }

            var header = FindDescendant(panel, "OptionsHeader");
            if (header != null) AddHeaderRule(header, art[HeaderRule]);
            else missing.Add("OptionsHeader");

            if (!wasActive) panel.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = panel.gameObject;

            string note = missing.Count == 0 ? "" :
                " NOT FOUND, so left alone: " + string.Join(", ", missing) + ".";
            Debug.Log($"[MapOptionsChrome] Dressed {toggles} switch(es) and {drops} " +
                      $"dropdown(s).{note} SAVE THE SCENE.");
        }

        // ─────────────────────────────────────────────────────────────────
        // Toggles
        // ─────────────────────────────────────────────────────────────────

        private static void DressToggle(RectTransform track, Dictionary<string, Sprite> art)
        {
            Undo.RegisterFullObjectHierarchyUndo(track.gameObject, "Dress Map Options");

            // Give the track a definite pill size. It sits in a horizontal
            // layout beside the ON/OFF caption, so the layout will not size it
            // on its own.
            float h = 34f * S;
            float w = 86f * S;
            var le = track.GetComponent<LayoutElement>();
            if (le == null) le = Undo.AddComponent<LayoutElement>(track.gameObject);
            le.minHeight = le.preferredHeight = h;
            le.minWidth = le.preferredWidth = w;
            le.flexibleWidth = 0f;

            MenuToggleSwitch.Attach(track,
                art[TrackFill], art[TrackOutline], art[Knob],
                knobWidth: 26f * S, knobInset: 4f * S, knobPadding: 4f * S);

            // Nothing tints the track itself any more - the fill child does -
            // so the Button's colour states have to leave it alone or they
            // would fade the whole switch on hover.
            var button = track.GetComponent<Button>();
            if (button != null)
            {
                var c = button.colors;
                c.normalColor = c.selectedColor = new Color(1f, 1f, 1f, 0f);
                c.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
                c.pressedColor = new Color(1f, 1f, 1f, 0.20f);
                c.disabledColor = new Color(1f, 1f, 1f, 0f);
                button.colors = c;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Dropdowns
        // ─────────────────────────────────────────────────────────────────

        private static void DressDropdown(TMP_Dropdown dropdown, Dictionary<string, Sprite> art)
        {
            var rt = (RectTransform)dropdown.transform;
            Undo.RegisterFullObjectHierarchyUndo(rt.gameObject, "Dress Map Options");

            float h = Mathf.Max(1f, rt.rect.height);
            var bg = dropdown.GetComponent<Image>();
            if (bg != null)
            {
                bg.sprite = art[DropFill];
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = Multiplier(art[DropFill], h);
                bg.color = WellDark;
            }

            // Gold plate over the fill, under the caption.
            var frame = Layer(rt, FrameNode, art[DropFrame], GoldFaint,
                              Multiplier(art[DropFrame], h), 0);
            frame.raycastTarget = false;

            // The interlocking diamonds eat the ends of the plate, so the
            // caption is inset past them or it reads as sitting on top of one.
            var caption = dropdown.captionText;
            if (caption != null)
            {
                float inset = art[DropFrame].border.x / Multiplier(art[DropFrame], h) * 0.75f;
                caption.margin = new Vector4(inset, 0f, inset, 0f);
                caption.color = Color.white;
                caption.alignment = TextAlignmentOptions.MidlineLeft;
            }

            var arrow = rt.Find("Arrow")?.GetComponent<Image>();
            if (arrow != null)
            {
                arrow.sprite = art[DropArrow];
                arrow.type = Image.Type.Simple;
                arrow.preserveAspect = true;
                arrow.color = Gold;
            }

            // The open list.
            var template = dropdown.template;
            if (template == null) return;
            var tplImg = template.GetComponent<Image>();
            if (tplImg != null)
            {
                tplImg.sprite = art[ListFill];
                tplImg.type = Image.Type.Sliced;
                tplImg.pixelsPerUnitMultiplier = 6f; // short list, tight border
                tplImg.color = ListDark;
            }
            Layer(template, FrameNode, art[ListFrame], GoldFaint, 6f, 0).raycastTarget = false;

            var itemLabel = dropdown.itemText;
            if (itemLabel != null) itemLabel.color = Color.white;

            // The item's own highlight swatch: gold wash instead of Unity blue.
            var item = template.Find("Viewport/Content/Item");
            var itemToggle = item != null ? item.GetComponent<Toggle>() : null;
            if (itemToggle != null)
            {
                var c = itemToggle.colors;
                c.normalColor = new Color(1f, 1f, 1f, 0f);
                c.highlightedColor = new Color(Gold.r, Gold.g, Gold.b, 0.22f);
                c.pressedColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
                c.selectedColor = new Color(Gold.r, Gold.g, Gold.b, 0.22f);
                itemToggle.colors = c;
            }
        }

        // ─────────────────────────────────────────────────────────────────

        private static void AddHeaderRule(RectTransform header, Sprite rule)
        {
            var rt = header.Find(RuleNode) as RectTransform;
            if (rt == null)
            {
                var go = new GameObject(RuleNode, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(go, "Dress Map Options");
                rt = (RectTransform)go.transform;
                rt.SetParent(header, false);
            }
            // Pinned under the header text, left-aligned with it, and kept out
            // of any layout group the header may sit in.
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = Undo.AddComponent<LayoutElement>(rt.gameObject);
            le.ignoreLayout = true;

            // Sized to the sprite's OWN aspect. Stretching it to the header's
            // full width and letting preserveAspect letterbox it would shrink
            // the flourish to a speck in the middle of the row; stretching it
            // without preserveAspect would pull the curls flat.
            float height = 14f * S;
            float width = height * (rule.rect.width / Mathf.Max(1f, rule.rect.height));
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, -3f * S);

            var img = rt.GetComponent<Image>();
            if (img == null) img = Undo.AddComponent<Image>(rt.gameObject);
            img.sprite = rule;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = GoldFaint;
            img.raycastTarget = false;
        }

        /// <summary>Slice multiplier that renders a horizontal-only border at
        /// the same scale the sprite is being stretched to vertically, so the
        /// end caps keep their proportions.</summary>
        private static float Multiplier(Sprite sprite, float rectHeight) =>
            Mathf.Max(1f, sprite.rect.height / Mathf.Max(1f, rectHeight));

        private static Image Layer(RectTransform parent, string name, Sprite sprite,
            Color tint, float multiplier, int siblingIndex)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(go, "Dress Map Options");
                rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
            }
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetSiblingIndex(siblingIndex);

            var img = rt.GetComponent<Image>();
            if (img == null) img = Undo.AddComponent<Image>(rt.gameObject);
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = multiplier;
            img.color = tint;
            return img;
        }

        /// <summary>Child <paramref name="child"/> of the descendant named
        /// <paramref name="cell"/>, at any depth under it.</summary>
        private static RectTransform Descendant(RectTransform root, string cell, string child)
        {
            var cellRt = FindDescendant(root, cell);
            return cellRt == null ? null : FindDescendant(cellRt, child);
        }

        private static RectTransform FindDescendant(RectTransform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t as RectTransform;
            return null;
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
    }
}
#endif
