namespace TheWaningBorder.Launcher;

/// <summary>
/// Which build this launcher installs. Baked in at publish time
/// (<c>publish.ps1 -Edition B</c> defines <c>TWB_EDITION_B</c>), never read
/// from settings: a tester must not be able to point the demo launcher at a
/// developer build by editing a JSON file. The server enforces the same split
/// independently (a key lists the editions it may download), so this constant
/// only decides which build the launcher ASKS for and where it installs it.
///
/// <code>
///   A   the demo           — Age 0 + Alanthor            tag v0.0.41
///   B   developer preview  — + Feraldis                  tag v0.0.41B
///   C   developer preview  — + Feraldis + Runai          tag v0.0.41C
/// </code>
///
/// Every edition gets its own install root and its own settings file, so the
/// three install side by side and a demo key stored by one never shadows a
/// developer key stored by another.
/// </summary>
internal static class Edition
{
#if TWB_EDITION_C
    public const string Id = "C";
#elif TWB_EDITION_B
    public const string Id = "B";
#else
    public const string Id = "A";
#endif

    public static bool IsDemo => Id == "A";

    /// <summary>"" for the demo, so its paths and names are exactly what
    /// every launcher before editions used — existing installs stay put.</summary>
    public static string Suffix => IsDemo ? "" : " " + Id;

    /// <summary>Shown in the window title, so two open launchers are distinguishable.</summary>
    public static string DisplayName => IsDemo ? "The Waning Border" : $"The Waning Border ({Id} - developer)";
}
