// MenuPanelTools.cs (editor-only)
// Maintenance passes for the MenuPanelsBuilder-generated panels
// (Panel_Skirmish / Panel_Scenarios / Panel_Multiplayer under the MainMenu
// canvas). The builder bakes every pixel constant at S = referenceResolution.y
// / 1080 ONCE; nothing keeps those constants in step afterwards, so these two
// tools exist to repair a panel in place instead of deleting and rebuilding it
// (which would throw away hand edits).
//
// Tools ▸ Waning Border ▸ Menu ▸ Rescale Menu Panels…
//   Multiplies every pixel-valued property in the panel subtrees — TMP font
//   sizes and margins, LayoutElement min/preferred, layout-group padding /
//   spacing / cell size, RectTransform sizeDelta + anchoredPosition, Outline
//   distance, scroll sensitivity. Anchors are fractions and stay untouched, so
//   the LAYOUT is preserved exactly; only the sizes change.
//   The panels shipped at 1x under a 3840x2160 canvas — half their design
//   size, which is why the skirmish screen was unreadable. They were brought to
//   2x with this pass.
//
// Tools ▸ Waning Border ▸ Menu ▸ Apply Synty Frames
//   SCENARIOS AND MULTIPLAYER ONLY — Panel_Skirmish is dressed by
//   SkirmishPanelChrome (Tools ▸ Waning Border ▸ Menu ▸ Dress Skirmish Panel).
//   Swaps the flat gradient plates for the Synty sliced box frames and pins a
//   PIXELS PER UNIT MULTIPLIER of 4 on every sliced Synty image. Synty's UI
//   art is authored at ~4x (Frame_Box_Large_01 carries a 340px slice border);
//   at the default multiplier of 1 those borders render enormous, so 4 is what
//   makes them read as trim rather than as slabs. Idempotent — running it
//   twice changes nothing.
//
// Both passes register Undo, so a bad result is one Ctrl+Z away. SAVE THE
// SCENE afterwards.

#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    internal static class MenuPanelTools
    {
        private const string SpriteRoot = "Assets/Synty/InterfaceFantasyMenus/Sprites/";

        /// <summary>Synty art is authored at ~4x; sliced borders need this or
        /// they render as slabs instead of trim.</summary>
        private const float SyntyPixelsPerUnitMultiplier = 4f;

        private static readonly string[] PanelNames =
            { "Panel_Skirmish", "Panel_Scenarios", "Panel_Multiplayer" };

        // Plate nodes built by MenuPanelsBuilder.Plate/PlateRow/CenterPane.
        private static readonly HashSet<string> PlateNames = new HashSet<string>
        {
            "TheatreBar", "MapPreview", "MapOptions", "RosterPlate",
            "ListPlate", "PreviewPlate", "MatchOptions", "SlotsPlate",
            "Pane_Choice", "Pane_HostSetup", "Pane_Browse", "Pane_Connecting",
        };

        // ─────────────────────────────────────────────────────────────────
        // Rescale
        // ─────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Waning Border/Menu/Rescale Menu Panels…")]
        private static void OpenRescale() => RescaleWindow.Open();

        internal sealed class RescaleWindow : EditorWindow
        {
            private float _factor = 2f;

            internal static void Open()
            {
                var w = GetWindow<RescaleWindow>(true, "Rescale Menu Panels");
                w.minSize = new Vector2(360f, 150f);
                w.Show();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Multiplies every pixel constant in the " +
                    "Skirmish / Scenarios / Multiplayer panels. Anchors (and therefore " +
                    "the layout) are untouched.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space();
                _factor = EditorGUILayout.FloatField("Scale factor", _factor);
                EditorGUILayout.HelpBox("The canvas reference is 3840x2160, so the design " +
                    "baseline is 2x a 1080p layout.", MessageType.Info);
                EditorGUILayout.Space();
                using (new EditorGUI.DisabledScope(_factor <= 0f || Mathf.Approximately(_factor, 1f)))
                {
                    if (GUILayout.Button("Rescale"))
                    {
                        Rescale(_factor);
                        Close();
                    }
                }
            }
        }

        private static void Rescale(float f)
        {
            int panels = 0, components = 0;
            foreach (var root in FindPanels())
            {
                panels++;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    components += RescaleNode(t, f);
            }

            if (panels == 0)
            {
                EditorUtility.DisplayDialog("Rescale Menu Panels",
                    "No Panel_Skirmish / Panel_Scenarios / Panel_Multiplayer found in the " +
                    "open scene. Open the MainMenu scene first.", "OK");
                return;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[MenuPanelTools] Rescaled {panels} panel(s), {components} component(s) " +
                      $"by {f:0.##}x. SAVE THE SCENE.");
        }

        private static int RescaleNode(Transform t, float f)
        {
            int touched = 0;

            if (t is RectTransform rt)
            {
                Undo.RecordObject(rt, "Rescale Menu Panels");
                rt.sizeDelta *= f;
                rt.anchoredPosition *= f;
                touched++;
            }

            foreach (var text in t.GetComponents<TMP_Text>())
            {
                Undo.RecordObject(text, "Rescale Menu Panels");
                text.fontSize *= f;
                text.fontSizeMin *= f;
                text.fontSizeMax *= f;
                text.margin *= f;
                touched++;
            }

            foreach (var le in t.GetComponents<LayoutElement>())
            {
                Undo.RecordObject(le, "Rescale Menu Panels");
                if (le.minWidth > 0f) le.minWidth *= f;
                if (le.minHeight > 0f) le.minHeight *= f;
                if (le.preferredWidth > 0f) le.preferredWidth *= f;
                if (le.preferredHeight > 0f) le.preferredHeight *= f;
                touched++;
            }

            foreach (var g in t.GetComponents<HorizontalOrVerticalLayoutGroup>())
            {
                Undo.RecordObject(g, "Rescale Menu Panels");
                g.padding = Scale(g.padding, f);
                g.spacing *= f;
                touched++;
            }

            foreach (var g in t.GetComponents<GridLayoutGroup>())
            {
                Undo.RecordObject(g, "Rescale Menu Panels");
                g.padding = Scale(g.padding, f);
                g.spacing *= f;
                g.cellSize *= f;
                touched++;
            }

            foreach (var s in t.GetComponents<Shadow>()) // covers Outline
            {
                Undo.RecordObject(s, "Rescale Menu Panels");
                s.effectDistance *= f;
                touched++;
            }

            foreach (var sr in t.GetComponents<ScrollRect>())
            {
                Undo.RecordObject(sr, "Rescale Menu Panels");
                sr.scrollSensitivity *= f;
                touched++;

                // The builder left scroll contents at the default 100 width,
                // i.e. 100px wider than the viewport — and rescaling doubles
                // the overhang. The vertical fitter drives height; pin width.
                if (sr.content != null && sr.content.anchorMin.x == 0f
                    && sr.content.anchorMax.x == 1f)
                {
                    Undo.RecordObject(sr.content, "Rescale Menu Panels");
                    var sd = sr.content.sizeDelta;
                    sd.x = 0f;
                    sr.content.sizeDelta = sd;
                }
            }

            return touched;
        }

        private static RectOffset Scale(RectOffset o, float f) => new RectOffset(
            Mathf.RoundToInt(o.left * f), Mathf.RoundToInt(o.right * f),
            Mathf.RoundToInt(o.top * f), Mathf.RoundToInt(o.bottom * f));

        // ─────────────────────────────────────────────────────────────────
        // Synty frames
        // ─────────────────────────────────────────────────────────────────

        [MenuItem("Tools/Waning Border/Menu/Apply Synty Frames")]
        private static void ApplySyntyFrames()
        {
            var boxFill  = LoadSprite("FantasyMenus/SPR_FantasyMenus_Frame_Box_Large_01_Background");
            var boxFrame = LoadSprite("FantasyMenus/SPR_FantasyMenus_Frame_Box_Large_01");
            if (boxFill == null || boxFrame == null)
            {
                EditorUtility.DisplayDialog("Apply Synty Frames",
                    "Synty Frame_Box_Large_01 sprites not found under " + SpriteRoot + ".", "OK");
                return;
            }

            int plates = 0, images = 0;
            foreach (var root in FindPanels())
            {
                // Panel_Skirmish has its own dressing pass (SkirmishPanelChrome),
                // which derives a per-plate pixels-per-unit multiplier instead of
                // pinning 4x. Letting this sweep run over it would flatten those
                // back to 4x and overlap the borders on the short TheatreBar.
                if (root.name == "Panel_Skirmish") continue;

                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (PlateNames.Contains(t.name) && Dress(t, boxFill, boxFrame)) plates++;
                }
                // Every sliced Synty image in the subtree gets the 4x
                // multiplier, including the ones the builder already placed.
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null || img.type != Image.Type.Sliced) continue;
                    if (Mathf.Approximately(img.pixelsPerUnitMultiplier,
                            SyntyPixelsPerUnitMultiplier)) continue;
                    Undo.RecordObject(img, "Apply Synty Frames");
                    img.pixelsPerUnitMultiplier = SyntyPixelsPerUnitMultiplier;
                    images++;
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[MenuPanelTools] Dressed {plates} plate(s), set the 4x pixels-per-unit " +
                      $"multiplier on {images} sliced image(s). SAVE THE SCENE.");
        }

        /// <summary>Swap a flat gradient plate for the Synty sliced box fill and
        /// add the matching ornate frame on top (once).</summary>
        private static bool Dress(Transform plate, Sprite fill, Sprite frame)
        {
            var bg = plate.GetComponent<Image>();
            if (bg == null) return false;

            Undo.RecordObject(bg, "Apply Synty Frames");
            bg.sprite = fill;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = SyntyPixelsPerUnitMultiplier;

            if (plate.Find("SyntyFrame") != null) return true;

            // The frame carries a LayoutElement with ignoreLayout: most plates
            // own a Vertical/HorizontalLayoutGroup, which would otherwise treat
            // the stretched frame as one more row and squash it into the stack.
            var go = new GameObject("SyntyFrame",
                typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Apply Synty Frames");
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.SetParent(plate, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling(); // behind the plate's content, over the fill

            var img = go.GetComponent<Image>();
            img.sprite = frame;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = SyntyPixelsPerUnitMultiplier;
            img.raycastTarget = false;
            img.color = new Color(0.910f, 0.722f, 0.290f, 0.85f);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────

        private static IEnumerable<Transform> FindPanels()
        {
            var found = new List<Transform>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var name in PanelNames)
                        if (t.name == name) found.Add(t);
                }
            }
            return found;
        }

        private static Sprite LoadSprite(string relativePath) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + relativePath + ".png");
    }
}
#endif
