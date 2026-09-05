// PlanningModeHudPrefabBuilder.cs
// Builds PlanningModeHUD.prefab — the planning-mode banner and its waypoint
// marker template.

using UnityEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    public static class PlanningModeHudPrefabBuilder
    {
        const string Dir = "Assets/GameData/Scenes/Menus/GameUI/PlanningMode";
        const string Path = Dir + "/PlanningModeHUD.prefab";

        static readonly Color Accent = new Color(0.909f, 0.835f, 0.627f);

        [MenuItem("Waning Border/UI/Build Planning Mode HUD Prefab")]
        public static void Build()
        {
            var root = new GameObject("PlanningModeHUD", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            Stretch(rt);

            // Markers are positioned in SCREEN space by the binder, so the root
            // must span the canvas rather than be laid out.
            Text(rt, "Title", 24f, Accent, new Vector2(0f, -40f), 40f);
            Text(rt, "Count", 16f, Color.white, new Vector2(0f, -70f), 30f);

            var marker = new GameObject("MarkerTemplate", typeof(RectTransform));
            var mrt = (RectTransform)marker.transform;
            mrt.SetParent(rt, false);
            mrt.sizeDelta = new Vector2(28f, 28f);
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);

            var label = marker.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = "M";

            marker.SetActive(false);

            root.AddComponent<PlanningModeOverlay>();

            System.IO.Directory.CreateDirectory(Dir);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            Debug.Log($"[Planning] built {Path} — assign it to GameUICatalog.planningModeHud.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }

        static void Text(RectTransform parent, string node, float size, Color colour,
                         Vector2 offset, float height)
        {
            var go = new GameObject(node, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(0f, height);

            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = colour;
            t.alignment = TextAlignmentOptions.Top;
            t.raycastTarget = false;
            t.text = string.Empty;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
