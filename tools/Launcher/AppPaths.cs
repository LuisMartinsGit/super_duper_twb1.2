namespace TheWaningBorder.Launcher;

/// <summary>
/// The on-disk layout the updater maintains.
///
/// <code>
/// %LOCALAPPDATA%\Programs\Shardroot Entertainment\The Waning Border\
///   TWBLauncher.exe     the launcher; the GAME keeps it current (LauncherSelfUpdate)
///   version.txt         the installed build's version
///   launcher.log        one line per decision the updater took
///   game\               everything an update replaces
///   game.old\           the previous build, kept for rollback
///   game.new\           staging, transient
/// </code>
///
/// THE ROOT IS FIXED, NOT "WHEREVER THE EXE IS" (2026-09-23). It used to be
/// the launcher's own folder, which made every copy of the launcher a valid
/// install root — and every build carries a copy at game\TWBLauncher.exe so
/// the game can upgrade the real one. A tester who ran THAT copy (the folder
/// they see the game in, so the natural one to pin) installed a second full
/// build at game\game\, and every real update moved it into game.old and
/// left the inner launcher to install it again. Now every launcher, wherever
/// it is run from — Downloads, the desktop, inside game\ — resolves to this
/// one root, and InstallRoot.Prepare puts a launcher there on first run.
///
/// The launcher lives OUTSIDE <c>game\</c> on purpose: Windows locks a running
/// executable and its loaded DLLs, so an updater that sat beside the files it
/// replaces could never overwrite itself.
/// </summary>
internal static class AppPaths
{
    public const string Company = "Shardroot Entertainment";
    public const string Product = "The Waning Border";
    public const string LauncherFileName = "TWBLauncher.exe";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", Company, Product);

    public static string Game => Path.Combine(Root, "game");
    public static string Staging => Path.Combine(Root, "game.new");
    public static string Previous => Path.Combine(Root, "game.old");
    public static string VersionFile => Path.Combine(Root, "version.txt");
    public static string LogFile => Path.Combine(Root, "launcher.log");

    /// <summary>The launcher the root should hold and the shortcut points at.</summary>
    public static string LauncherExe => Path.Combine(Root, LauncherFileName);

    /// <summary>
    /// Where THIS process is actually running from. Not the root: a tester
    /// runs whatever copy they have, and the root is where it puts things.
    /// ProcessPath is the single-file host itself; BaseDirectory would be the
    /// same folder for a single-file publish but is kept as the fallback.
    /// </summary>
    public static string ExePath { get; } =
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, LauncherFileName);

    public static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>Start Menu > Programs > Shardroot Entertainment > The Waning Border.</summary>
    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), Company, Product + ".lnk");

    /// <summary>
    /// Settings live in %APPDATA%, not the install root, so wiping and
    /// re-copying the folder does not make a tester re-enter their key.
    /// </summary>
    public static string SettingsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheWaningBorder",
        "launcher.json");

    public static string DownloadCache => Path.Combine(Path.GetTempPath(), "TheWaningBorder");

    /// <summary>
    /// Finds the game executable without hardcoding a filename, so renaming
    /// the Unity product does not silently break the launcher. Unity's crash
    /// handler sits in the same folder and is explicitly skipped.
    /// </summary>
    public static string? FindGameExe()
    {
        if (!Directory.Exists(Game)) return null;

        var candidates = Directory.GetFiles(Game, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(p => !Path.GetFileName(p).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.Equals(Path.GetFileName(p), LauncherFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0) return null;

        return candidates.FirstOrDefault(p =>
                   string.Equals(Path.GetFileNameWithoutExtension(p), Product, StringComparison.OrdinalIgnoreCase))
               ?? candidates.OrderByDescending(p => new FileInfo(p).Length).First();
    }

    public static string? ReadInstalledVersion()
    {
        try
        {
            if (!File.Exists(VersionFile)) return null;
            var text = File.ReadAllText(VersionFile).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void WriteInstalledVersion(string version) =>
        File.WriteAllText(VersionFile, version);
}
