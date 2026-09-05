// NotificationStackPrefabBuilder.cs
// Builds NotificationStack.prefab — the top-centre notification pills.

using UnityEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.UI.Ingame;

namespace TheWaningBorder.EditorTools
{
    public static class NotificationStackPrefabBuilder
    {
        const string Dir = "Assets/GameData/Scenes/Menus/GameUI/Notifications";
        const string Path = Dir + "/NotificationStack.prefab";
        const string PillSprite =
            "Assets/Synty/InterfaceFantasyMenus/Sprites/FantasyMenus/SPR_FantasyMenus_Frame_Bar_06.png";

        /// <summary>Clear of the resource bar at the top of the screen.</summary>
        const float TopOffset = 50f;
        const float PillHeight = 32f;

        [MenuItem("Waning Border/UI/Build Notification Stack Prefab")]
        public static void Build()
        {
            var root = new GameObject("NotificationStack", typeof(RectTransform));
            var rt = (RectTransform)root.transform;

            // Top-centre, growing downward.
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -TopOffset);
            rt.sizeDelta = new Vector2(500f, 0f);

            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The template the binder clones. Inactive in the prefab.
            var pill = new GameObject("PillTemplate", typeof(RectTransform));
            var prt = (RectTransform)pill.transform;
            prt.SetParent(rt, false);

            var bg = pill.AddComponent<Image>();
            bg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PillSprite);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;      // a toast must never eat a click
            if (bg.sprite == null) Debug.LogWarning($"[Notifications] sprite missing: {PillSprite}");

            pill.AddComponent<CanvasGroup>();

            var le = pill.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = PillHeight;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(prt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 0f);
            lrt.offsetMax = new Vector2(-16f, 0f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = string.Empty;

            pill.SetActive(false);

            root.AddComponent<PlayerNotificationSystem>();

            System.IO.Directory.CreateDirectory(Dir);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            Debug.Log($"[Notifications] built {Path} — assign it to GameUICatalog.notificationStack.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }
    }
}
