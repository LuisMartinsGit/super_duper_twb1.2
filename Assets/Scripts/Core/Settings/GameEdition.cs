// GameEdition.cs
// Which cultures this build ships. docs/Design/Alpha_Build.md § Editions.
//
//   A  the demo           Age 0 + Alanthor            0.0.41
//   B  developer preview  + Feraldis                  0.0.41B
//   C  developer preview  + Feraldis + Runai          0.0.41C
//
// A PLAYER build learns its edition from the TWB_EDITION_<X> define that
// PlayerBuild passes as an extra scripting define; the same build step strips
// the other cultures' data and art out of the build (Editor/EditionContent).
// The EDITOR has no define: it reads the edition picked under
// Waning Border > Edition, so the developer can play the demo or a preview
// from the same project. It defaults to A, which is exactly what the game
// offered before editions existed (Feraldis and Runai both "coming soon").
//
// CultureConfig.IsComingSoon is the one gate every culture choice reads (the
// age-up menu, the landmark buttons, the AI's pick, the lobby's start age,
// the age-up command itself), and it asks Allows() — so this file is the only
// place that decides what a build offers.

public static class GameEdition
{
#if TWB_EDITION_A
    public const char BuiltId = 'A';
#elif TWB_EDITION_B
    public const char BuiltId = 'B';
#elif TWB_EDITION_C
    public const char BuiltId = 'C';
#else
    public const char BuiltId = '\0';   // no define: the Editor (or a build that bypassed PlayerBuild)
#endif

    /// <summary>EditorPrefs key the Waning Border > Edition menu writes.</summary>
    public const string EditorPrefKey = "TWB.Edition";

    /// <summary>'A', 'B' or 'C'.</summary>
    public static char Id => BuiltId != '\0' ? BuiltId : _editorPick;

    // The Editor's pick, CACHED. Id must never call a Unity API: it is reached
    // from static initializers (LobbyOptions' cctor, run from SkirmishPanel's
    // field initializer on the loading thread), where EditorPrefs throws and
    // poisons both types for the whole domain. So the pref is read once, on
    // the main thread, at domain load — and the Edition menu writes through
    // SetEditorPick. A player build with no define skipped PlayerBuild and its
    // strip; 'A' (the demo) is the one safe reading of it.
    private static char _editorPick = 'A';

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void LoadEditorPick() =>
        _editorPick = Parse(UnityEditor.EditorPrefs.GetString(EditorPrefKey, "A"));

    /// <summary>Waning Border > Edition. Editor only.</summary>
    public static void SetEditorPick(string edition)
    {
        UnityEditor.EditorPrefs.SetString(EditorPrefKey, edition);
        _editorPick = Parse(edition);
    }

    private static char Parse(string s) => s == "B" ? 'B' : s == "C" ? 'C' : 'A';
#endif

    /// <summary>True when this edition ships <paramref name="culture"/>.
    /// Age 0 (None) and Alanthor ship in every edition.</summary>
    public static bool Allows(byte culture)
    {
        switch (culture)
        {
            case Cultures.Feraldis: return Id == 'B' || Id == 'C';
            case Cultures.Runai:    return Id == 'C';
            default:                return true;
        }
    }
}
