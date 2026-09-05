// SelectionBoxPrefabBuilder.cs
// Builds SelectionBox.prefab — the drag-select frame — beside its binder.
//
// Same reasoning as OptionsPanelPrefabBuilder: the hierarchy is constructed in
// code and saved through PrefabUtility, so Unity writes the asset and the
// result is a real authored prefab.

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.UI.Ingame;

namespace TheWaningBorder.EditorTools
{
    public static class SelectionBoxPrefabBuilder
    {
        const string Dir = "Assets/GameData/Scenes/Menus/GameUI/SelectionBox";
        const string Path = Dir + "/SelectionBox.prefab";
        const string Sprites = "Assets/Synty/InterfaceFantasyMenus/Sprites/FantasyMenus/";

        // Frame_Bar, not Frame_Box_Large: the large boxes carry 340-450px
        // borders, and a drag box is often only ~80px across, so their corners
        // would be crushed into mush at real selection sizes.
        const string FrameSprite = Sprites + "SPR_FantasyMenus_Frame_Bar_06.png";

        static readonly Color Tint = new Color(0.55f, 1f, 0.6f, 0.9f);
        static readonly Color Fill = new Color(0.2f, 0.8f, 0.2f, 0.18f);

        [MenuItem("Waning Border/UI/Build Selection Box Prefab")]
        public static void Build()
        {
            var root = new GameObject("SelectionBox", typeof(RectTransform));
            var rt = (RectTransform)root.transform;

            // Bottom-left anchored: SelectionSystem publishes screen space with
            // that origin, so the binder can assign the rect straight across.
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;

            var fill = root.AddComponent<Image>();
            fill.color = Fill;
            fill.raycastTarget = false;

            var frameGo = new GameObject("Frame", typeof(RectTransform));
            var frt = (RectTransform)frameGo.transform;
            frt.SetParent(rt, false);
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var frame = frameGo.AddComponent<Image>();
            frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(FrameSprite);
            frame.type = Image.Type.Sliced;
            frame.color = Tint;
            frame.raycastTarget = false;
            if (frame.sprite == null)
                Debug.LogWarning($"[SelectionBox] sprite missing: {FrameSprite}");

            root.AddComponent<SelectionBoxBinder>();

            System.IO.Directory.CreateDirectory(Dir);
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            Debug.Log($"[SelectionBox] built {Path} — assign it to GameUICatalog.selectionBox.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        }
    }
}
