namespace TheWaningBorder.Launcher;

/// <summary>
/// The on-disk layout the updater maintains.
///
/// <code>
/// The Waning Border\
///   TWBLauncher.exe     the launcher, never touched by an update
///   version.txt         the installed build's version
///   game\               everything an update replaces
///   game.old\           the previous build, kept for rollback
///   game.new\           staging, transient
/// </code>
///
/// The launcher lives OUTSIDE <c>game\</c> on purpose: Windows locks a running
/// executable and its loaded DLLs, so an updater that sat beside the files it
/// replaces could never overwrite itself.
/// </summary>
internal static class AppPaths
{
    public static string Root { get; } =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string Game => Path.Combine(Root, "game");
    public static string Staging => Path.Combine(Root, "game.new");
    public static string Previous => Path.Combine(Root, "game.old");
    public static string VersionFile => Path.Combine(Root, "version.txt");

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
            .ToArray();

        if (candidates.Length == 0) return null;

        return candidates.FirstOrDefault(p =>
                   string.Equals(Path.GetFileNameWithoutExtension(p), "The Waning Border", StringComparison.OrdinalIgnoreCase))
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
