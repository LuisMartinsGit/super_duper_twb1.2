// SkirmishPanelLayout.cs (editor-only)
// Structural pass over the Skirmish screen: puts the hierarchy back into a
// shape that reads, and re-expresses every hand-nudged rect as an anchor so the
// screen holds together at any display aspect.
//
// Run: Tools > Waning Border > Menu > Organise Skirmish Layout, with
// Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity open. Then SAVE.
//
// DELETES NOTHING. Every node the screen carries today survives the pass -
// including the empty MapDescription left inside the promoted options plate.
// Two nodes are renamed and two are reparented; the rest only changes anchors,
// LayoutElement numbers and layout-group flags. Idempotent, and every edit is
// registered with Undo.
//
// -- Why the screen drifts ----------------------------------------------
// The CanvasScaler matches HEIGHT (3840x2160 reference, m_MatchWidthOrHeight
// = 1), so the canvas is always 2160 units tall and its WIDTH is 2160 * aspect:
// 3840 at 16:9, 5040 at 21:9, 2880 at 4:3. Vertical geometry is therefore
// aspect-invariant and needs no defending; horizontal geometry is not, and
// every number this screen bakes was baked from a 16:9 game view. Everything
// below is one of three fixes for that:
//
//   1. an absolute rect that should track its parent's WIDTH -> anchors,
//      with the margins measured off the live rect so nothing moves at 16:9;
//   2. a node hanging off a container whose CENTRE moves with the aspect
//      -> re-based onto the panel's top-left corner, which does not;
//   3. childForceExpandWidth left on in a HORIZONTAL group -> off, because
//      Unity forces flexibleWidth >= 1 on every child when it is set
//      (HorizontalOrVerticalLayoutGroup.GetChildSizes), which overrides the
//      authored fixed widths and makes them grow with the display.
//
// -- What moves on screen -----------------------------------------------
// Fixing (3) snaps CANCEL / START, the map arrows and the option dropdowns
// back to their authored widths - at 16:9 the two footer buttons were
// rendering ~1278 and ~1398px wide against the 440 / 560 SkirmishPanelChrome
// asks for, because the footer's force-expand handed each of them a third of
// the row's slack. Legend and the options plate also settle onto the layout
// grid, which lifts a ~30px overlap between Legend and DiamondStage and a
// ~27px overhang of the options content past its own backing box. Everything
// else is pixel-identical at 16:9 by construction: the pass measures the live
// rect and bakes it back as anchor margins.
//
// -- The Title is deliberately left as a plain Transform -----------------
// Title carries a UnityEngine.Transform, not a RectTransform, so a uGUI layout
// group cannot see it and its five children anchor against a zero-size parent
// rect. Converting it would change what those children anchor to and move the
// Synty brackets; instead the pass WRAPS it in a Header RectTransform pinned to
// the panel's top-left corner. Title keeps its Transform, keeps its children's
// numbers, and stops riding LeftColumn's centre - which was sliding it 312px
// right at 21:9 and 250px left at 4:3, far enough to push the title label off
// the screen edge.
//
// -- The options plate ---------------------------------------------------
// MAP OPTIONS was authored as two siblings under MapPreview/Column: a second
// copy of MapDescriptionBox acting as the backing plate, and a node still
// called "GameObject" holding the header and the two option rows, lined up
// with the plate only by baked coordinates. The pass renames them MapOptions
// and OptionsContent and nests the second inside the first, so the plate now
// owns what it frames. The nested Canvas on OptionsContent is kept (it is what
// lifts the option rows over the plate) and gets the GraphicRaycaster it was
// missing - see AddMissingRaycaster.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    internal static class SkirmishPanelLayout
    {
        private const string MenuPath = "Tools/Waning Border/Menu/Organise Skirmish Layout";
        private const string Undoing = "Organise Skirmish Layout";

        /// <summary>Container the title is re-based onto. Named to match the
        /// node MenuPanelsBuilder and SkirmishPanelChrome already look for, so
        /// a future banner pass finds it where it expects to.</summary>
        private const string HeaderNode = "Header";

        /// <summary>
        /// Landmarks reported before / after, so the settle is auditable
        /// rather than something to take on trust. Second entry is the name
        /// the node had BEFORE this pass renamed it - without it the first
        /// run, the only one whose numbers matter, reports "not found" for the
        /// two nodes it is most worth checking.
        /// </summary>
        private static readonly (string Name, string WasCalled)[] Landmarks =
        {
            ("Title", null), ("TheatreBar", null), ("MapText", null), ("MapPreview", null),
            ("DiamondStage", null), ("Diamond", null), ("Legend", null),
            ("MapOptions", "MapDescriptionBox"), ("OptionsContent", "GameObject"),
            ("OptionsHeader", null), ("OptionsRow1", null), ("OptionsRow2", null),
            ("RosterPlate", null), ("BackButton", null), ("PrimaryButton", null),
        };

        [MenuItem(MenuPath)]
        private static void Organise()
        {
            var panel = Find("Panel_Skirmish");
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Organise Skirmish Layout",
                    "No Panel_Skirmish found in the open scene. Open " +
                    "Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity first.",
                    "OK");
                return;
            }

            // Nothing below can measure a rect until the layout groups have run
            // once. The panel is scene-loaded active in its own scene, but it
            // still ships inactive in older copies - same guard the chrome pass
            // uses, for the same reason.
            bool wasActive = panel.gameObject.activeSelf;
            if (!wasActive)
            {
                Undo.RecordObject(panel.gameObject, Undoing);
                panel.gameObject.SetActive(true);
            }
            Rebuild(panel);

            var before = Snapshot(panel);

            // Rebuilt between the steps that MEASURE and the steps that move
            // things: DriveMapPreviewColumn bakes preferred heights off live
            // rects, and a rect left stale by the reparent above it would be
            // baked in as if it were the authored value.
            var report = new List<string>();
            RebaseTitle(panel, report);
            PromoteOptionsPlate(panel, report);
            Rebuild(panel);
            AnchorDecorations(panel, report);
            Rebuild(panel);
            DriveMapPreviewColumn(panel, report);
            StopForceExpandingFixedWidths(panel, report);
            AddMissingRaycaster(panel, report);

            Rebuild(panel);
            if (!wasActive) panel.gameObject.SetActive(false);

            var after = Snapshot(panel);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = panel.gameObject;

            Debug.Log("[SkirmishPanelLayout] " + string.Join("\n  ", report) +
                      "\n\nLandmark rects (canvas px, before -> after):\n  " +
                      string.Join("\n  ", Diff(before, after)) +
                      "\n\nSAVE THE SCENE.");
        }

        // -----------------------------------------------------------------
        // 1. Title off LeftColumn's centre, onto the panel's top-left corner
        // -----------------------------------------------------------------

        private static void RebaseTitle(RectTransform panel, List<string> report)
        {
            var title = FindDescendantTransform(panel, "Title");
            if (title == null) { report.Add("Title: not found, skipped."); return; }

            var header = panel.Find(HeaderNode) as RectTransform;
            if (header == null)
            {
                var go = new GameObject(HeaderNode, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, Undoing);
                header = (RectTransform)go.transform;
                header.SetParent(panel, false);
            }

            // A zero-size rect pinned to the panel's top-left. Title's children
            // already anchor against a zero-size parent (Title is a plain
            // Transform), so parking Title inside a zero-size Header changes
            // nothing about how they resolve - only where the origin sits.
            Undo.RecordObject(header, Undoing);
            header.anchorMin = header.anchorMax = header.pivot = new Vector2(0f, 1f);
            header.sizeDelta = Vector2.zero;
            header.anchoredPosition = Vector2.zero;
            header.SetSiblingIndex(0);
            EditorUtility.SetDirty(header);

            if (title.parent == header)
            {
                report.Add("Title: already under " + HeaderNode + ".");
                return;
            }

            string from = title.parent != null ? title.parent.name : "(root)";
            Undo.SetTransformParent(title, header, Undoing);
            Undo.RecordObject(title, Undoing);
            // worldPositionStays kept the title exactly where it was; the only
            // edit left is dropping the stray z the plain Transform had picked
            // up, which does nothing on a Screen Space - Overlay canvas but
            // reads as a mistake every time someone opens the Inspector.
            var lp = title.localPosition;
            title.localPosition = new Vector3(lp.x, lp.y, 0f);
            EditorUtility.SetDirty(title);

            report.Add($"Title: moved {from} -> {HeaderNode} (panel top-left), " +
                       $"local ({lp.x:F1}, {lp.y:F1}); no longer rides LeftColumn's centre.");
        }

        // -----------------------------------------------------------------
        // 2. The options plate owns its own content
        // -----------------------------------------------------------------

        private static void PromoteOptionsPlate(RectTransform panel, List<string> report)
        {
            var column = FindDescendant(panel, "Column");
            if (column == null) { report.Add("MapPreview/Column: not found, skipped."); return; }

            var plate = DirectChild(column, "MapOptions") ?? DirectChild(column, "MapDescriptionBox");
            var content = DirectChild(column, "OptionsContent") ?? DirectChild(column, "GameObject")
                          ?? DirectChild(plate, "OptionsContent");
            if (plate == null || content == null)
            {
                report.Add("MapOptions: backing plate or options content not found, skipped.");
                return;
            }

            if (plate.name != "MapOptions")
            {
                Undo.RecordObject(plate.gameObject, Undoing);
                plate.name = "MapOptions";
                EditorUtility.SetDirty(plate.gameObject);
                report.Add("MapOptions: renamed from the duplicate MapDescriptionBox that was " +
                           "acting as the plate. SkirmishPanelChrome looks for a plate by this " +
                           "name, but bails on one with no Image of its own - this plate paints " +
                           "through a Background child - so re-running that pass stays a no-op " +
                           "here and will not stack a second frame on the hand-made one.");
            }

            if (content.name != "OptionsContent")
            {
                Undo.RecordObject(content.gameObject, Undoing);
                content.name = "OptionsContent";
                EditorUtility.SetDirty(content.gameObject);
                report.Add("OptionsContent: renamed from \"GameObject\".");
            }

            if (content.parent != plate)
            {
                Undo.SetTransformParent(content, plate, Undoing);
                report.Add("OptionsContent: nested inside MapOptions (was its sibling, aligned " +
                           "only by baked coordinates).");
            }
            content.SetAsLastSibling();

            // Flush to the plate; the vertical group's padding is what insets
            // the rows off the frame, which is the job a padding exists for.
            Undo.RecordObject(content, Undoing);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            EditorUtility.SetDirty(content);

            ClearIgnoreLayout(content);

            // Same padding the map-preview plate above it uses, so the two
            // plates on this column indent their contents identically.
            var v = content.GetComponent<VerticalLayoutGroup>();
            if (v != null)
            {
                Undo.RecordObject(v, Undoing);
                v.padding = new RectOffset(28, 28, 24, 24);
                v.spacing = 12f;
                v.childAlignment = TextAnchor.UpperLeft;
                v.childControlWidth = true;
                v.childForceExpandWidth = true;
                v.childControlHeight = true;
                v.childForceExpandHeight = false;
                EditorUtility.SetDirty(v);
            }

            foreach (var name in new[] { "OptionsHeader", "OptionsRow1", "OptionsRow2" })
            {
                var row = DirectChild(content, name);
                if (row != null) ClearIgnoreLayout(row);
            }
            report.Add("OptionsHeader / OptionsRow1 / OptionsRow2: ignoreLayout cleared - they " +
                       "are laid out by OptionsContent now instead of sitting at baked offsets.");
        }

        // -----------------------------------------------------------------
        // 3. Baked decoration rects -> anchors
        // -----------------------------------------------------------------

        /// <summary>
        /// Every plate background and gold frame on this screen was authored by
        /// typing a width that happened to be right at 16:9 - 1728 for a column
        /// plate, 1009 for a box frame. Re-expressed as a stretch with the
        /// measured margins, they land on the same pixels today and track their
        /// plate at every other aspect.
        ///
        /// The rotated nodes are skipped on purpose: GetWorldCorners returns
        /// the ROTATED quad, so an axis-aligned re-anchor of the 90-degree
        /// title plate or the 45-degree map gem would bake the bounding box of
        /// the rotation rather than the rect.
        /// </summary>
        private static void AnchorDecorations(RectTransform panel, List<string> report)
        {
            int flush = 0, margins = 0;

            // Plate backgrounds fill their plate exactly.
            foreach (var plate in new[] { "TheatreBar", "MapPreview", "RosterPlate", "MapOptions" })
            {
                var bg = DirectChild(FindDescendant(panel, plate), "Background");
                if (StretchFlush(bg)) flush++;
            }

            // The live description box beside the diamond is the reference for
            // how far a gold frame is meant to bleed past its box; the promoted
            // options plate gets the same bleed rather than the 33px asymmetric
            // one it had been nudged to.
            var liveBox = DirectChild(FindDescendant(panel, "DiamondStage"), "MapDescriptionBox");
            float bleed = FrameBleed(liveBox);

            if (StretchFlush(DirectChild(liveBox, "Background"))) flush++;
            if (StretchMargins(DirectChild(liveBox, "MapDescription"))) margins++;
            if (StretchBleed(DirectChild(liveBox, "Frame"), bleed)) flush++;
            if (StretchBleed(DirectChild(FindDescendant(panel, "Diamond"), "Frame"), bleed)) flush++;

            var options = FindDescendant(panel, "MapOptions");
            if (StretchBleed(DirectChild(options, "Frame"), bleed)) flush++;
            // The empty MapDescription the plate kept when it was cloned off
            // the description box. Nothing writes to it and it draws nothing,
            // but it is a node on this screen, so it gets an anchor like every
            // other node rather than being left to drift off the plate.
            if (StretchMargins(DirectChild(options, "MapDescription"))) margins++;

            report.Add($"Anchors: {flush} background/frame node(s) stretched to their parent, " +
                       $"{margins} with their measured margins baked in. Gold frames bleed " +
                       $"{bleed:F1}px, taken off the live map-description box.");
        }

        /// <summary>How far a frame overhangs the box it borders, measured off
        /// a frame that is already correct.</summary>
        private static float FrameBleed(RectTransform box)
        {
            var frame = DirectChild(box, "Frame");
            if (box == null || frame == null) return 4.5f;
            return Mathf.Max(0f, (frame.rect.width - box.rect.width) * 0.5f);
        }

        private static bool StretchFlush(RectTransform rt) => Stretch(rt, Vector2.zero, Vector2.zero);

        private static bool StretchBleed(RectTransform rt, float bleed) =>
            Stretch(rt, new Vector2(-bleed, -bleed), new Vector2(bleed, bleed));

        /// <summary>Stretch, keeping the inset the node currently renders at.</summary>
        private static bool StretchMargins(RectTransform rt)
        {
            if (rt == null || !(rt.parent is RectTransform parent)) return false;
            var target = RectIn(rt, parent);
            var p = parent.rect;
            return Stretch(rt, target.min - p.min, target.max - p.max);
        }

        private static bool Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (rt == null || !(rt.parent is RectTransform)) return false;
            if (IsRotated(rt)) return false;

            Undo.RecordObject(rt, Undoing);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            EditorUtility.SetDirty(rt);
            return true;
        }

        private static bool IsRotated(RectTransform rt) =>
            Quaternion.Angle(rt.localRotation, Quaternion.identity) > 0.5f;

        // -----------------------------------------------------------------
        // 4. MapPreview/Column lays its children out instead of watching them
        // -----------------------------------------------------------------

        /// <summary>
        /// Column carried a VerticalLayoutGroup with childControl off on both
        /// axes and three of its four children on ignoreLayout, so it was a
        /// layout group that laid out exactly one child while the rest sat at
        /// coordinates. Turning childControl on hands it the width - the one
        /// axis that moves with the aspect - and the preferred heights are
        /// baked off the live rects first, so the vertical rhythm the screen
        /// already has is what the group reproduces.
        /// </summary>
        private static void DriveMapPreviewColumn(RectTransform panel, List<string> report)
        {
            var column = FindDescendant(panel, "Column");
            if (column == null) return;

            var stage = DirectChild(column, "DiamondStage");
            var legend = DirectChild(column, "Legend");
            var options = DirectChild(column, "MapOptions");

            // Measured BEFORE childControl goes on, or the group has already
            // resized them by the time we read the rect.
            foreach (var child in new[] { stage, legend, options })
            {
                if (child == null) continue;
                BakeHeight(child, child.rect.height);
                ClearIgnoreLayout(child);
            }

            var v = column.GetComponent<VerticalLayoutGroup>();
            if (v != null)
            {
                Undo.RecordObject(v, Undoing);
                v.childControlWidth = true;
                v.childControlHeight = true;
                v.childForceExpandWidth = true;   // every plate spans the column
                v.childForceExpandHeight = false; // ... but keeps its own height
                v.childAlignment = TextAnchor.UpperLeft;
                EditorUtility.SetDirty(v);
            }

            report.Add("MapPreview/Column: childControl on, force-expand height off, " +
                       "ignoreLayout cleared on DiamondStage / Legend / MapOptions. Their " +
                       "widths follow the column now; heights were baked from the live rects.");

            // The diamond must stay square, so it is the fixed member of the
            // stage and the description box beside it absorbs the slack. With
            // childControlWidth off this row simply overflowed a narrow column:
            // 540 + 1000 + padding is 1600px of content in a 1240px column at
            // 4:3, and the description box ran out past the plate.
            var diamond = DirectChild(stage, "Diamond");
            var box = DirectChild(stage, "MapDescriptionBox");
            if (diamond != null) BakeWidth(diamond, diamond.rect.width, flexible: 0f);
            if (box != null) BakeWidth(box, Mathf.Min(480f, box.rect.width), flexible: 1f);

            var h = stage != null ? stage.GetComponent<HorizontalLayoutGroup>() : null;
            if (h != null)
            {
                Undo.RecordObject(h, Undoing);
                h.childControlWidth = true;
                h.childForceExpandWidth = false;
                EditorUtility.SetDirty(h);
            }

            report.Add("DiamondStage: childControlWidth on, force-expand off. Diamond holds its " +
                       "square at its measured width; MapDescriptionBox takes the remainder, so " +
                       "the row fits a narrow column instead of overflowing the plate.");
        }

        // -----------------------------------------------------------------
        // 5. Fixed widths stop growing with the display
        // -----------------------------------------------------------------

        /// <summary>
        /// childForceExpandWidth on a HORIZONTAL group is the bug: Unity's
        /// GetChildSizes does flexible = Mathf.Max(flexible, 1) for every
        /// child when it is set, which silently overrules a LayoutElement that
        /// asked for flexibleWidth 0. Every one of these rows has exactly one
        /// member that should absorb the slack and already carries
        /// flexibleWidth 1 - the error label, the map name, the option caption
        /// - so turning the flag off is the whole fix.
        ///
        /// OptionsRow1 / OptionsRow2 keep it ON: their children are the option
        /// CELLS, which are genuinely meant to share the row equally, and the
        /// observer cell SkirmishPanel adds to row 2 at runtime relies on that.
        /// </summary>
        private static void StopForceExpandingFixedWidths(RectTransform panel, List<string> report)
        {
            var fixedRows = new[] { "Footer", "TheatreBar", "OptResources", "OptAge", "OptFog", "OptCurse" };
            var done = new List<string>();

            foreach (var name in fixedRows)
            {
                var row = FindDescendant(panel, name);
                var h = row != null ? row.GetComponent<HorizontalLayoutGroup>() : null;
                if (h == null || !h.childForceExpandWidth) continue;

                Undo.RecordObject(h, Undoing);
                h.childForceExpandWidth = false;
                EditorUtility.SetDirty(h);
                done.Add(name);
            }

            report.Add(done.Count == 0
                ? "Force-expand: already off everywhere it needed to be."
                : "Force-expand width off on: " + string.Join(", ", done) +
                  ". CANCEL / START, the map arrows and the option dropdowns hold their " +
                  "authored widths now instead of splitting the row's slack.");
        }

        // -----------------------------------------------------------------
        // 6. The nested canvas can be clicked
        // -----------------------------------------------------------------

        /// <summary>
        /// A Graphic registers itself against the first ENABLED Canvas above it
        /// (Graphic.CacheCanvas), and a GraphicRaycaster only ever raycasts the
        /// Canvas on its own GameObject (GraphicRaycaster.Raycast ->
        /// GraphicRegistry.GetRaycastableGraphicsForCanvas). A nested Canvas
        /// with no raycaster of its own therefore takes every graphic below it
        /// out of UI_Canvas's registry and hands them to nobody - which is what
        /// the Canvas on OptionsContent was doing to both map-option dropdowns
        /// and both toggles.
        /// </summary>
        private static void AddMissingRaycaster(RectTransform panel, List<string> report)
        {
            foreach (var canvas in panel.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.transform == panel || canvas.GetComponent<GraphicRaycaster>() != null) continue;

                Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
                report.Add($"{canvas.name}: nested Canvas had no GraphicRaycaster, so nothing " +
                           "under it could be clicked. Added one (keeping the Canvas, which is " +
                           "what lifts these rows over the plate).");
            }
        }

        // -----------------------------------------------------------------
        // Shared
        // -----------------------------------------------------------------

        private static void BakeHeight(RectTransform rt, float height)
        {
            if (rt == null || height <= 1f) return;
            var le = rt.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(rt.gameObject);
            if (Mathf.Abs(le.preferredHeight - height) < 0.5f && le.flexibleHeight == 0f) return;

            Undo.RecordObject(le, Undoing);
            le.minHeight = -1f;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            EditorUtility.SetDirty(le);
        }

        private static void BakeWidth(RectTransform rt, float width, float flexible)
        {
            if (rt == null || width <= 1f) return;
            var le = rt.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(rt.gameObject);

            Undo.RecordObject(le, Undoing);
            le.minWidth = width;
            le.preferredWidth = flexible > 0f ? -1f : width;
            le.flexibleWidth = flexible;
            EditorUtility.SetDirty(le);
        }

        private static void ClearIgnoreLayout(RectTransform rt)
        {
            if (rt == null) return;
            foreach (var le in rt.GetComponents<LayoutElement>())
            {
                if (!le.ignoreLayout) continue;
                Undo.RecordObject(le, Undoing);
                le.ignoreLayout = false;
                EditorUtility.SetDirty(le);
            }
        }

        /// <summary><paramref name="child"/>'s rect expressed in
        /// <paramref name="parent"/>'s local space, whatever anchors either
        /// one is currently using.</summary>
        private static Rect RectIn(RectTransform child, RectTransform parent)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            var a = parent.InverseTransformPoint(corners[0]);
            var b = parent.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                   Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static Dictionary<string, Rect> Snapshot(RectTransform panel)
        {
            var map = new Dictionary<string, Rect>();
            var column = FindDescendant(panel, "Column");
            var corners = new Vector3[4];

            foreach (var (name, wasCalled) in Landmarks)
            {
                // The pre-pass names are looked up as DIRECT children of
                // Column, never by descendant search: "MapDescriptionBox"
                // matches the live box beside the diamond first, and reporting
                // that one as the options plate's old rect would be a lie.
                var t = FindDescendantTransform(panel, name)
                        ?? (wasCalled != null ? DirectChild(column, wasCalled) : null);
                if (t == null) continue;

                if (t is RectTransform rt)
                {
                    rt.GetWorldCorners(corners);
                    map[name] = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                }
                else
                {
                    // Title has a plain Transform and no rect of its own; its
                    // origin is the thing that was moving, so report that.
                    map[name] = new Rect(t.position.x, t.position.y, 0f, 0f);
                }
            }
            return map;
        }

        private static IEnumerable<string> Diff(Dictionary<string, Rect> before,
                                                Dictionary<string, Rect> after)
        {
            foreach (var (name, _) in Landmarks)
            {
                if (!before.TryGetValue(name, out var a) || !after.TryGetValue(name, out var b))
                {
                    yield return $"{name,-15} (not found)";
                    continue;
                }
                bool same = Mathf.Abs(a.xMin - b.xMin) < 0.5f && Mathf.Abs(a.yMin - b.yMin) < 0.5f &&
                            Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;
                yield return same
                    ? $"{name,-15} unchanged  ({a.width:F0}x{a.height:F0} @ {a.xMin:F0},{a.yMin:F0})"
                    : $"{name,-15} {a.width:F0}x{a.height:F0} @ {a.xMin:F0},{a.yMin:F0}" +
                      $"  ->  {b.width:F0}x{b.height:F0} @ {b.xMin:F0},{b.yMin:F0}";
            }
        }

        private static void Rebuild(RectTransform panel)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }

        private static RectTransform Find(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t as RectTransform;
            return null;
        }

        private static RectTransform FindDescendant(RectTransform root, string name) =>
            FindDescendantTransform(root, name) as RectTransform;

        private static Transform FindDescendantTransform(RectTransform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static RectTransform DirectChild(Transform parent, string name) =>
            parent == null ? null : parent.Find(name) as RectTransform;
    }
}
#endif
