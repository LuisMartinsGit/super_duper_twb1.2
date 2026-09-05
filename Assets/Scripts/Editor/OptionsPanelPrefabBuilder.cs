// OptionsPanelPrefabBuilder.cs
// Builds OptionsPanel.prefab — the authored Options screen — and saves it
// beside its binder.
//
// The hierarchy is constructed in code and handed to PrefabUtility, so Unity
// writes the asset. That is deliberate: a uGUI prefab's YAML is a web of
// fileIDs and sprite GUIDs that cannot be hand-written safely, and this way
// the result is a real authored prefab that can then be edited by hand like
// any other.
//
// Node names ARE the contract with OptionsPanelBinder. Renaming one here
// without renaming it there silently disables that control (the binder logs
// which node it could not find).

using UnityEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.EditorTools
{
    public static class OptionsPanelPrefabBuilder
    {
        const string Dir = "Assets/GameData/Scenes/Menus/MainMenu/Options";
        const string Path = Dir + "/OptionsPanel.prefab";
        const string Sprites = "Assets/Synty/InterfaceFantasyMenus/Sprites/FantasyMenus/";

        // The HUD prefabs already draw from this set, so the Options screen
        // reads as part of the same UI rather than a separate look.
        const string FrameSprite  = Sprites + "SPR_FantasyMenus_Frame_Box_Medium_01.png";
        const string ButtonSprite = Sprites + "SPR_FantasyMenus_Menu_Button_01_Background.png";
        const string BarSprite    = Sprites + "SPR_FantasyMenus_Scrollbar_Horizontal_01_Background.png";

        static readonly Color Gold     = new Color(0.909f, 0.835f, 0.627f);
        static readonly Color TextMain = new Color(0.92f, 0.90f, 0.84f);

        const float PanelW = 620f, PanelH = 700f, RowH = 34f, LabelW = 190f;

        [MenuItem("Waning Border/UI/Build Options Panel Prefab")]
        public static void Build()
        {
            var root = new GameObject("OptionsPanel", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(PanelW, PanelH);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var bg = root.AddComponent<Image>();
            bg.sprite = Load(FrameSprite);
            bg.type = Image.Type.Sliced;      // the frame stretches, the corners do not

            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 30, 30);
            layout.spacing = 10f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            Title(rt, "Title", "OPTIONS");

            RowWithInput(rt, "Player Name", "PlayerNameInput");
            RowWithDropdown(rt, "Graphics Quality", "QualityDropdown");
            RowWithDropdown(rt, "Resolution", "ResolutionDropdown");
            RowWithToggle(rt, "Fullscreen", "FullscreenToggle");
            RowWithSlider(rt, "Master Volume", "MasterSlider", "MasterValue");
            RowWithSlider(rt, "Music Volume", "MusicSlider", "MusicValue");
            LanguageRow(rt);

            var status = Label(rt, "Status", string.Empty, Gold);
            status.alignment = TextAlignmentOptions.Center;
            FixHeight(status.gameObject, 24f);

            ButtonRow(rt);

            root.AddComponent<OptionsPanelBinder>();

            System.IO.Directory.CreateDirectory(Dir);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            Debug.Log($"[Options] built {Path}");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }

        /// <summary>
        /// Drop the prefab into the open scene under its Canvas, inactive.
        /// Rule: what is authored becomes a prefab in the right folder AND an
        /// instance in the right scene — run this with MainMenu.unity open.
        /// </summary>
        [MenuItem("Waning Border/UI/Add Options Panel To Open Scene")]
        public static void AddToOpenScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null)
            {
                Debug.LogError("[Options] build the prefab first.");
                return;
            }

            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[Options] the open scene has no Canvas to mount under.");
                return;
            }

            foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
                if (t.name == "OptionsPanel")
                {
                    Debug.Log("[Options] the scene already has an OptionsPanel.");
                    Selection.activeGameObject = t.gameObject;
                    return;
                }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
            instance.name = "OptionsPanel";
            instance.SetActive(false);   // the Settings button switches it on
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;
            Debug.Log("[Options] added to " + instance.scene.name + " — save the scene.");
        }

        #region Rows

        static void Title(RectTransform parent, string node, string text)
        {
            var t = Label(parent, node, text, Gold);
            t.fontSize = 30f;
            t.alignment = TextAlignmentOptions.Center;
            FixHeight(t.gameObject, 46f);
        }

        static RectTransform Row(RectTransform parent, string label)
        {
            var row = Node(parent, label + "Row");
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childForceExpandWidth = false;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            FixHeight(row.gameObject, RowH);

            var caption = Label(row, "Label", label, TextMain);
            FixWidth(caption.gameObject, LabelW);
            return row;
        }

        static void RowWithInput(RectTransform parent, string label, string node)
        {
            var row = Row(parent, label);
            var field = Node(row, node);
            Frame(field, ButtonSprite);

            var textArea = Node(field, "Text Area");
            Stretch(textArea, 8f);

            var text = Label(textArea, "Text", string.Empty, TextMain);
            Stretch((RectTransform)text.transform, 0f);

            var input = field.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = text;
            Flexible(field.gameObject);
        }

        static void RowWithDropdown(RectTransform parent, string label, string node)
        {
            var row = Row(parent, label);
            var box = Node(row, node);
            Frame(box, ButtonSprite);

            var caption = Label(box, "Label", string.Empty, TextMain);
            Stretch((RectTransform)caption.transform, 10f);

            // A dropdown needs a template to clone; leaving it null throws on
            // first click, so build a minimal one and hide it.
            var template = Node(box, "Template");
            template.gameObject.SetActive(false);
            template.sizeDelta = new Vector2(0f, 120f);
            Frame(template, ButtonSprite);
            var viewport = Node(template, "Viewport");
            Stretch(viewport, 0f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            viewport.gameObject.AddComponent<Image>();
            var content = Node(viewport, "Content");
            content.sizeDelta = new Vector2(0f, RowH);
            var item = Node(content, "Item");
            item.sizeDelta = new Vector2(0f, RowH);
            item.gameObject.AddComponent<Toggle>();
            var itemLabel = Label(item, "Item Label", string.Empty, TextMain);
            Stretch((RectTransform)itemLabel.transform, 8f);

            var dd = box.gameObject.AddComponent<TMP_Dropdown>();
            dd.captionText = caption;
            dd.template = template;
            dd.itemText = itemLabel;
            Flexible(box.gameObject);
        }

        static void RowWithToggle(RectTransform parent, string label, string node)
        {
            var row = Row(parent, label);
            var box = Node(row, node);
            FixWidth(box.gameObject, RowH);
            var check = Frame(box, ButtonSprite);

            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = check;
            toggle.graphic = check;
        }

        static void RowWithSlider(RectTransform parent, string label, string node, string valueNode)
        {
            var row = Row(parent, label);

            var s = Node(row, node);
            Flexible(s.gameObject);
            var bg = Frame(s, BarSprite);

            var fillArea = Node(s, "Fill Area");
            Stretch(fillArea, 6f);
            var fill = Node(fillArea, "Fill");
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = Gold;

            var slider = s.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.targetGraphic = bg;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;

            var value = Label(row, valueNode, "0%", TextMain);
            FixWidth(value.gameObject, 54f);
        }

        static void LanguageRow(RectTransform parent)
        {
            var row = Row(parent, "Language");
            // Each language is named in ITS OWN language, so a player stuck in
            // the wrong one can still find the way back.
            TextButton(row, "EnglishButton", "English");
            TextButton(row, "PortugueseButton", "Português");
        }

        static void ButtonRow(RectTransform parent)
        {
            var row = Node(parent, "Buttons");
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            FixHeight(row.gameObject, 44f);

            TextButton(row, "BackButton", "Back");
            TextButton(row, "ApplyButton", "Apply");
        }

        #endregion

        #region Primitives

        static RectTransform Node(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static Image Frame(RectTransform rt, string spritePath)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Load(spritePath);
            img.type = Image.Type.Sliced;
            return img;
        }

        static TMP_Text Label(RectTransform parent, string node, string text, Color colour)
        {
            var rt = Node(parent, node);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.color = colour;
            t.fontSize = 20f;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            return t;
        }

        static void TextButton(RectTransform parent, string node, string text)
        {
            var rt = Node(parent, node);
            var img = Frame(rt, ButtonSprite);
            FixWidth(rt.gameObject, 140f);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;

            var label = Label(rt, "Label", text, Gold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch((RectTransform)label.transform, 0f);
        }

        static void Stretch(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        static void FixHeight(GameObject go, float h)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = h;
        }

        static void FixWidth(GameObject go, float w)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = w;
        }

        static void Flexible(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        static Sprite Load(string path)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) Debug.LogWarning($"[Options] sprite missing: {path}");
            return s;
        }

        #endregion
    }
}
