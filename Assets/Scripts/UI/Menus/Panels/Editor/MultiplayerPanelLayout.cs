// MultiplayerPanelLayout.cs (editor-only)
// The Skirmish layout pass, applied to Panel_Multiplayer in MainMenu.unity —
// plus the two scene edits the roster-ladder rework needs.
//
// Run: Tools > Waning Border > Menu > Organise Multiplayer Layout, with
// Assets/GameData/Scenes/Menus/MainMenu/MainMenu.unity open. Then SAVE.
//
// -- Why this pass is so much smaller than SkirmishPanelLayout -------------
// Panel_Multiplayer was never hand-edited. It is still exactly what
// MenuPanelsBuilder scaffolded: no baked absolute rects, no node left named
// "GameObject", no duplicated plate standing in as a frame, no plain Transform
// in the middle of a uGUI tree, no nested Canvas. So the whole structural half
// of the skirmish pass — the reparenting, the rect-to-anchor conversion — has
// nothing to do here, and doing it anyway would only invent churn.
//
// What it DOES share is the force-expand bug, and this panel has it in eleven
// horizontal groups. Unity's HorizontalOrVerticalLayoutGroup.GetChildSizes
// ends with `if (childForceExpand) flexible = Mathf.Max(flexible, 1)`, which
// overrules a LayoutElement that asked for flexibleWidth 0 — so every authored
// fixed width is ignored and grows with the display instead.
//
// The footer has it serialised in the scene, which is as close to a receipt as
// this gets: BackButton is stored 1153.03 wide against the 440 its own
// LayoutElement asks for, PrimaryButton 1273.03 against 560. The slack is
// simply split three ways because ErrorText, which carries flexibleWidth 1 and
// is the member that should absorb it, has no priority over the two buttons.
//
// Heights are left alone, matching what already shipped for the skirmish
// screen. The footer's childForceExpandHeight has the same flaw — those
// buttons are stored 172.8 tall against an authored 96, and SkirmishPanelChrome
// derives the Synty frame's pixels-per-unit from that 96, so the sliced end
// caps are being drawn at a size the multiplier was not computed for. That is
// reported, not changed: it would visibly shorten every footer button in both
// menus, which is a look decision rather than an aspect-ratio fix.
//
// -- The two scene edits --------------------------------------------------
//   PlayersRow    DELETED from Pane_HostSetup. The lobby size is the roster's
//                 job now (MultiplayerPanel.RebuildSlots builds an eight-rung
//                 ladder whose top-most free rung adds a player), and asking
//                 for a count up front was a second source of truth for it.
//   RemoveButton  ADDED to RosterRowTemplate, cloned from AiButton so it
//                 inherits whatever styling that button already carries —
//                 the same trick RosterTeamDropdown uses, and the only way to
//                 get a matching button without re-deriving Synty's slice
//                 multipliers by hand.
//
// Idempotent, registers Undo. Panel_Multiplayer ships inactive, so the pass
// activates it to measure, then puts it back exactly as it found it.

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
    internal static class MultiplayerPanelLayout
    {
        private const string MenuPath = "Tools/Waning Border/Menu/Organise Multiplayer Layout";
        private const string Undoing = "Organise Multiplayer Layout";

        /// <summary>
        /// Horizontal groups whose children carry authored fixed widths and one
        /// flexible member to absorb the slack.
        ///
        /// OptionsRow is deliberately absent: its children are the two option
        /// CELLS, which are meant to share the row equally, and that is exactly
        /// what force-expand is for. Legend already has the flag off.
        /// </summary>
        private static readonly string[] FixedWidthRows =
        {
            "Footer",            // CANCEL / error / START
            "TheatreBar",        // map arrows / name / tag
            "GameNameRow",       // label / input
            "PlayerNameRow",
            "PortRow",
            "DirectRow",         // ip / port / JOIN
            "GameRowTemplate",   // game name / JOIN
            "RosterRowTemplate", // the lobby row's columns
            "OptFog",            // caption / pill
            "OptBorder",
        };

        [MenuItem(MenuPath)]
        private static void Organise()
        {
            var panel = Find("Panel_Multiplayer");
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Organise Multiplayer Layout",
                    "No Panel_Multiplayer found in the open scene. Open " +
                    "Assets/GameData/Scenes/Menus/MainMenu/MainMenu.unity first.", "OK");
                return;
            }

            // The panel and every pane inside it ship inactive, so nothing has
            // ever laid out and the footer report below would read zeroes.
            var restore = new List<GameObject>();
            Activate(panel.gameObject, restore);
            foreach (var pane in new[] { "Pane_Lobby", "Pane_HostSetup" })
                if (FindDescendant(panel, pane) is RectTransform p) Activate(p.gameObject, restore);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            var report = new List<string>();
            StopForceExpandingFixedWidths(panel, report);
            DeletePlayersRow(panel, report);
            AddRemoveButton(panel, report);
            ReportFooterHeights(panel, report);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            // Exactly as found: the roster and game-row templates are stencils
            // and MUST stay off, and the panel itself is switched on by the
            // main menu's Multiplayer entry, not by being saved active.
            for (int i = restore.Count - 1; i >= 0; i--) restore[i].SetActive(false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = panel.gameObject;
            Debug.Log("[MultiplayerPanelLayout] " + string.Join("\n  ", report) +
                      "\n\nSAVE THE SCENE.");
        }

        // -----------------------------------------------------------------
        // 1. Fixed widths stop growing with the display
        // -----------------------------------------------------------------

        private static void StopForceExpandingFixedWidths(RectTransform panel, List<string> report)
        {
            var done = new List<string>();
            foreach (var name in FixedWidthRows)
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
                : $"Force-expand width off on {done.Count} row(s): " + string.Join(", ", done) +
                  ". Their fixed-width children hold their authored widths now instead of " +
                  "splitting the row's slack, at every display aspect.");
        }

        // -----------------------------------------------------------------
        // 2. The host-setup player count goes away
        // -----------------------------------------------------------------

        private static void DeletePlayersRow(RectTransform panel, List<string> report)
        {
            var setup = FindDescendant(panel, "Pane_HostSetup");
            var row = setup != null ? setup.Find("PlayersRow") : null;
            if (row == null)
            {
                report.Add("PlayersRow: already gone.");
                return;
            }

            Undo.DestroyObjectImmediate(row.gameObject);
            report.Add("PlayersRow: deleted from Pane_HostSetup (label, - / + buttons and the " +
                       "value). The roster ladder in the lobby sizes the match now — see " +
                       "MultiplayerPanel.AddPlayer / RemoveLastSlot.");
        }

        // -----------------------------------------------------------------
        // 3. The roster row gets a shrink handle
        // -----------------------------------------------------------------

        /// <summary>
        /// Clone AiButton into a RemoveButton. Building one from scratch would
        /// mean re-deriving the Synty sprite, its slice multiplier and the
        /// label's font off the surrounding row by hand; Instantiate inherits
        /// all of it and remaps the copy's own children for free.
        /// </summary>
        private static void AddRemoveButton(RectTransform panel, List<string> report)
        {
            var template = FindDescendant(panel, "RosterRowTemplate");
            if (template == null) { report.Add("RosterRowTemplate: not found, skipped."); return; }

            if (template.Find("RemoveButton") != null)
            {
                report.Add("RemoveButton: already on RosterRowTemplate.");
                return;
            }

            var source = template.Find("AiButton") as RectTransform;
            if (source == null)
            {
                report.Add("RemoveButton: no AiButton to clone from, skipped. The row's columns " +
                           "may have been renamed.");
                return;
            }

            var copy = Object.Instantiate(source.gameObject, template);
            Undo.RegisterCreatedObjectUndo(copy, Undoing);
            copy.name = "RemoveButton";
            copy.transform.SetAsLastSibling();

            // Narrow, square-ish: it is an X, not a word. LobbyRowLayout pins
            // the runtime width to ColRemove anyway, but the authored value is
            // what the Inspector and any future pass reads.
            if (copy.TryGetComponent(out LayoutElement le))
            {
                Undo.RecordObject(le, Undoing);
                le.minWidth = le.preferredWidth = 72f;
                le.minHeight = le.preferredHeight = 64f;
                le.flexibleWidth = le.flexibleHeight = 0f;
                EditorUtility.SetDirty(le);
            }

            var label = copy.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Undo.RecordObject(label, Undoing);
                label.text = "X";
                label.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(label);
            }

            // The clone carries AiButton's authored onClick; MultiplayerPanel
            // calls RemoveAllListeners before adding its own, but a persistent
            // call survives that, so switch any off here.
            if (copy.TryGetComponent(out Button button))
            {
                Undo.RecordObject(button, Undoing);
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                EditorUtility.SetDirty(button);
            }

            report.Add("RemoveButton: added to RosterRowTemplate (cloned from AiButton, label " +
                       "\"X\"). MultiplayerPanel shows it on the bottom rung only — see " +
                       "RemoveLastSlot for why it is not an X on every row like skirmish.");
        }

        // -----------------------------------------------------------------
        // 4. Reported, not changed
        // -----------------------------------------------------------------

        private static void ReportFooterHeights(RectTransform panel, List<string> report)
        {
            var footer = FindDescendant(panel, "Footer");
            if (footer == null) return;

            foreach (var name in new[] { "BackButton", "PrimaryButton" })
            {
                var rt = footer.Find(name) as RectTransform;
                var le = rt != null ? rt.GetComponent<LayoutElement>() : null;
                if (rt == null || le == null || le.preferredHeight <= 0f) continue;

                float actual = rt.rect.height;
                if (Mathf.Abs(actual - le.preferredHeight) < 1f) continue;

                report.Add($"NOT CHANGED — {name} renders {actual:F0}px tall against the " +
                           $"{le.preferredHeight:F0} its LayoutElement asks for, because the " +
                           "footer force-expands height too. SkirmishPanelChrome derives the " +
                           "Synty frame's pixels-per-unit from that authored height, so the " +
                           "sliced end caps are drawn at a size the multiplier was not computed " +
                           "for. Turning childForceExpandHeight off on the footer fixes it and " +
                           "visibly shortens the buttons in BOTH menus.");
            }
        }

        // -----------------------------------------------------------------
        // Shared
        // -----------------------------------------------------------------

        private static void Activate(GameObject go, List<GameObject> restore)
        {
            if (go == null || go.activeSelf) return;
            Undo.RecordObject(go, Undoing);
            go.SetActive(true);
            restore.Add(go);
        }

        private static RectTransform Find(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t as RectTransform;
            return null;
        }

        private static RectTransform FindDescendant(RectTransform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t as RectTransform;
            return null;
        }
    }
}
#endif
