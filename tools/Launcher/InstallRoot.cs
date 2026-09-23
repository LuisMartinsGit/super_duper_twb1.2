namespace TheWaningBorder.Launcher;

/// <summary>
/// Makes the fixed install root real before the updater touches anything:
/// creates it, adopts an install from the old "beside the exe" layout, puts
/// a launcher in it, and gives it a Start Menu entry.
///
/// Everything here is best-effort and reported as log lines rather than
/// thrown: a tester whose Start Menu shortcut could not be written still
/// gets an update. Only a root that cannot be created at all is fatal, and
/// that one is thrown, because nothing after it can work.
/// </summary>
internal static class InstallRoot
{
    public static List<string> Prepare()
    {
        var notes = new List<string>();

        Directory.CreateDirectory(AppPaths.Root);

        MigrateLegacyLayout(notes);
        PlaceLauncher(notes);
        EnsureStartMenuShortcut(notes);

        return notes;
    }

    /// <summary>
    /// Until 2026-09-23 the root was the launcher's own folder, so an existing
    /// tester has version.txt + game\ beside whatever exe they ran, or (when
    /// they ran the copy the build carries) one level above it. Moving it is
    /// a rename on the same volume and takes no time; on a different volume,
    /// or with the game running, the move fails and the fresh install below
    /// downloads the build instead — slower, never wrong.
    /// </summary>
    private static void MigrateLegacyLayout(List<string> notes)
    {
        if (Directory.Exists(AppPaths.Game)) return;   // the new root is already populated

        var candidates = new[] { AppPaths.ExeDirectory, Path.GetDirectoryName(AppPaths.ExeDirectory) };

        foreach (var legacy in candidates)
        {
            if (string.IsNullOrEmpty(legacy)) continue;
            if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase)) continue;

            var legacyGame = Path.Combine(legacy, "game");
            var legacyVersion = Path.Combine(legacy, "version.txt");
            if (!Directory.Exists(legacyGame) || !File.Exists(legacyVersion)) continue;

            try
            {
                Directory.Move(legacyGame, AppPaths.Game);
                File.Move(legacyVersion, AppPaths.VersionFile, overwrite: true);
                notes.Add($"Adopted the install from {legacy} into {AppPaths.Root}.");

                // The rollback copy is worth keeping if it moves as cheaply;
                // if it does not, it is just disk the tester can reclaim.
                var legacyPrevious = Path.Combine(legacy, "game.old");
                if (Directory.Exists(legacyPrevious))
                {
                    try { Directory.Move(legacyPrevious, AppPaths.Previous); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        notes.Add($"Left the old rollback copy at {legacyPrevious}; it can be deleted.");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Could not move the install at {legacy} ({ex.Message}); " +
                          "a fresh copy will be downloaded. The old folder can be deleted.");
            }

            return;   // one legacy install is all there is
        }
    }

    /// <summary>
    /// The root has to hold a launcher: it is what the Start Menu entry runs
    /// and what LauncherSelfUpdate in the game replaces after each update.
    /// Ours is copied in when there is none, or when ours is newer — never
    /// over a newer one, so a stale copy kept on the desktop cannot roll the
    /// real launcher back.
    /// </summary>
    private static void PlaceLauncher(List<string> notes)
    {
        var ours = AppPaths.ExePath;
        var target = AppPaths.LauncherExe;

        if (string.Equals(Path.GetFullPath(ours), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(target) &&
                File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(ours))
                return;

            // Write beside, then swap, so a failure cannot leave a truncated
            // launcher — the same discipline LauncherSelfUpdate uses.
            var staged = target + ".new";
            File.Copy(ours, staged, overwrite: true);

            if (File.Exists(target)) File.Replace(staged, target, null, ignoreMetadataErrors: true);
            else File.Move(staged, target);

            notes.Add($"Placed the launcher at {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"Could not place the launcher in the install root ({ex.Message}).");
        }
    }

    /// <summary>
    /// Start Menu > Shardroot Entertainment > The Waning Border, pointing at
    /// the root launcher. Written through the shell's own shortcut object so
    /// the .lnk is exactly what Explorer would have made.
    /// </summary>
    private static void EnsureStartMenuShortcut(List<string> notes)
    {
        var link = AppPaths.StartMenuShortcut;
        if (File.Exists(link) || !File.Exists(AppPaths.LauncherExe)) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = AppPaths.LauncherExe;
            shortcut.WorkingDirectory = AppPaths.Root;
            shortcut.IconLocation = AppPaths.LauncherExe + ",0";
            shortcut.Description = AppPaths.Product;
            shortcut.Save();

            notes.Add("Added the Start Menu shortcut.");
        }
        catch (Exception ex)
        {
            notes.Add($"Could not add the Start Menu shortcut ({ex.GetType().Name}: {ex.Message}).");
        }
    }
}
