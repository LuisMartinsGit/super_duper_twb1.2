// EditionBuildGate.cs
// Every player build must go through PlayerBuild, which picks an edition.
//
// A build started from File > Build Settings carries no TWB_EDITION_ define
// and skips the culture strip, so it would ship every culture's data under
// whatever version the project holds. GameEdition reads a define-less player
// as the demo, but the Feraldis and Runai assets would still be inside it.
// Refusing the build is the only safe answer.
//
// Build an edition with: the bridge `build` / `build-edition <A|B|C> <dir>`,
// or batch mode -executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build
// -buildPath <dir> -twbEdition <A|B|C>. docs/Design/Alpha_Build.md § Editions.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace TheWaningBorder.EditorTools
{
    public sealed class EditionBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PlayerBuild.ActiveEdition != null) return;

            throw new BuildFailedException(
                "Player builds must go through PlayerBuild so they get an EDITION (A demo / B / C) " +
                "and its culture strip. Use the editor bridge `build-edition <A|B|C> <dir>` or batch " +
                "mode -executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build -twbEdition <A|B|C>.");
        }
    }

    /// <summary>
    /// Waning Border > Edition: which edition the EDITOR plays as. A player
    /// build ignores this; its edition is the define PlayerBuild gives it.
    /// </summary>
    public static class EditionMenu
    {
        private const string Root = "Waning Border/Edition/";
        private const string MenuA = Root + "A - Demo (Alanthor)";
        private const string MenuB = Root + "B - Developer (+ Feraldis)";
        private const string MenuC = Root + "C - Developer (+ Feraldis + Runai)";

        [MenuItem(MenuA, priority = 1)] private static void PickA() => Pick("A");
        [MenuItem(MenuB, priority = 2)] private static void PickB() => Pick("B");
        [MenuItem(MenuC, priority = 3)] private static void PickC() => Pick("C");

        [MenuItem(MenuA, true)] private static bool CheckA() => Check(MenuA, "A");
        [MenuItem(MenuB, true)] private static bool CheckB() => Check(MenuB, "B");
        [MenuItem(MenuC, true)] private static bool CheckC() => Check(MenuC, "C");

        private static void Pick(string edition)
        {
            GameEdition.SetEditorPick(edition);
            UnityEngine.Debug.Log($"[Edition] The Editor now plays as edition {edition}. " +
                                  "Takes effect from the next match.");
        }

        private static bool Check(string menu, string edition)
        {
            Menu.SetChecked(menu, GameEdition.Id.ToString() == edition);
            return !EditorApplication.isPlaying;
        }
    }
}
