// RosterTeamDropdown.cs (editor-only)
// Converts the roster row template's team CHIP - a small coloured square you
// clicked to cycle through the teams - into a TeamDropdown that matches the
// personality and difficulty columns beside it.
//
// Run: Tools > Waning Border > Menu > Convert Team Chip To Dropdown, with
// Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity open. Then SAVE.
//
// The new dropdown is a COPY of DifficultyDropdown rather than a fresh
// TMP_Dropdown: that is the only way to inherit the styling for free (including
// whatever MapOptionsChrome or a hand edit has done to it), and Instantiate
// remaps the dropdown's internal references - caption, item label, template -
// to the copy's own children, which hand-building would have to redo by hand.
//
// It only touches the TEMPLATE row. Every visible row is a clone of it
// (SkirmishPanel.BuildRosterRow), so one conversion reaches all eight.
//
// Idempotent: if TeamDropdown already exists the pass reports it and stops,
// so it cannot stack copies.

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.EditorTools
{
    internal static class RosterTeamDropdown
    {
        private const string MenuPath =
            "Tools/Waning Border/Menu/Convert Team Chip To Dropdown";

        private const string TemplateRow = "RosterRowTemplate";
        private const string ChipNode = "TeamChip";
        private const string SourceNode = "DifficultyDropdown";
        private const string TeamNode = "TeamDropdown";

        [MenuItem(MenuPath)]
        private static void Convert()
        {
            var row = Find(TemplateRow);
            if (row == null)
            {
                EditorUtility.DisplayDialog("Convert Team Chip",
                    $"No '{TemplateRow}' in the open scene. Open " +
                    "Assets/GameData/Scenes/Menus/SkirmishMenu/SkirmishMenu.unity first.",
                    "OK");
                return;
            }

            if (row.Find(TeamNode) != null)
            {
                EditorUtility.DisplayDialog("Convert Team Chip",
                    $"'{TeamNode}' already exists on {TemplateRow}. Nothing to do - " +
                    "delete it first if you want it rebuilt from the difficulty column.",
                    "OK");
                return;
            }

            var source = row.Find(SourceNode) as RectTransform;
            if (source == null || source.GetComponent<TMP_Dropdown>() == null)
            {
                EditorUtility.DisplayDialog("Convert Team Chip",
                    $"No '{SourceNode}' with a TMP_Dropdown on {TemplateRow}, so there " +
                    "is no style to copy. Nothing was changed.", "OK");
                return;
            }

            var chip = row.Find(ChipNode) as RectTransform;

            var copy = Object.Instantiate(source.gameObject, row);
            Undo.RegisterCreatedObjectUndo(copy, "Convert Team Chip");
            copy.name = TeamNode;
            copy.SetActive(true);

            // Stand exactly where the chip stood, at its width. The row's
            // layout group reads sibling order left to right, so the column
            // keeps its place between the name and the difficulty.
            var copyRt = (RectTransform)copy.transform;
            if (chip != null)
            {
                copyRt.SetSiblingIndex(chip.GetSiblingIndex());
                CopyWidth(chip, copyRt);
            }
            else
            {
                copyRt.SetSiblingIndex(source.GetSiblingIndex());
            }

            // The caption still reads whatever the difficulty column was
            // showing; SkirmishPanel fills the real options per row, but a
            // stale "EASY" sitting in the template is confusing to author over.
            var dropdown = copy.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string> { "NO TEAM" });
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();

            // The chip is replaced, not hidden - a dead node on every row is
            // exactly the clutter this screen has been shedding. SkirmishPanel
            // falls back to the chip only when no TeamDropdown exists, so
            // removing it here is what commits to the dropdown.
            if (chip != null) Undo.DestroyObjectImmediate(chip.gameObject);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = copy;
            Debug.Log($"[RosterTeamDropdown] {TeamNode} added to {TemplateRow} as a copy of " +
                      $"{SourceNode}" + (chip != null ? $", replacing {ChipNode}" : "") +
                      ". Every roster row is a clone of this template, so all eight " +
                      "follow. SAVE THE SCENE.");
        }

        /// <summary>Carry the chip's column width onto the dropdown, so the row
        /// keeps the widths authored on the template.</summary>
        private static void CopyWidth(RectTransform from, RectTransform to)
        {
            var src = from.GetComponent<LayoutElement>();
            if (src == null) return;

            var dst = to.GetComponent<LayoutElement>();
            if (dst == null) dst = Undo.AddComponent<LayoutElement>(to.gameObject);
            dst.minWidth = src.minWidth;
            dst.preferredWidth = src.preferredWidth;
            dst.flexibleWidth = src.flexibleWidth;
        }

        private static Transform Find(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            }
            return null;
        }
    }
}
#endif
