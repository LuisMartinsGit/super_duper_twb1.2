using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TheWaningBorder.Launcher;

/// <summary>
/// Decides where this edition is installed, before anything reads
/// <see cref="AppPaths.Root"/>.
///
/// THE GAME INSTALLS IN PROGRAM FILES (2026-11-06), under Shardroot Entertainment\The
/// Waning Border, where other games install, and the player's own files are in their
/// user folder (AppPaths.UserData). Writing to Program Files takes administrator rights,
/// so the launcher does what game launchers do: the first time, it asks once through
/// Windows to create the folder and let ordinary users write to it, and every update
/// after that runs without asking. Nothing else ever runs elevated.
///
/// An older install is remembered in LauncherSettings.LegacyRoots and cleared of what
/// the game shipped (ShippedFileCleanup) once the new install works. If the player
/// declines the Windows prompt, the old install keeps working and the next start asks
/// again; with no old install there is nothing to run, and the launcher says so.
/// </summary>
internal static class InstallLocation
{
    public const string PrepareRootArg = "--prepare-root";
    public const string HandoverArg = "--handover";

    /// <summary>False when there is nowhere to install, and the player has been told.</summary>
    public static bool Resolve(LauncherSettings settings, IWin32Window owner)
    {
        var target = AppPaths.DefaultRoot;

        foreach (var old in new[] { settings.InstallRoot, AppPaths.LegacyDefaultRoot })
        {
            if (string.IsNullOrWhiteSpace(old) || SameFolder(old, target) || !LooksLikeInstall(old)) continue;
            if (!settings.LegacyRoots.Any(r => SameFolder(r, old))) settings.LegacyRoots.Add(Path.GetFullPath(old).TrimEnd('\\'));
        }

        if (CanWrite(target) || (RequestElevatedSetup() && CanWrite(target)))
        {
            AppPaths.UseRoot(target);
            settings.InstallRoot = AppPaths.Root;
            TrySave(settings);
            return true;
        }

        // Declined. Keep playing from the old install, and ask again next start.
        var fallback = settings.LegacyRoots.FirstOrDefault(LooksLikeInstall);
        TrySave(settings);
        if (fallback is not null)
        {
            AppPaths.UseRoot(fallback);
            return true;
        }

        MessageBox.Show(
            owner,
            $"The game installs in {target}, and Windows needs your permission to create that folder once. " +
            "Start the launcher again and choose Yes to install.",
            Edition.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    /// <summary>A folder the updater has already installed into.</summary>
    public static bool LooksLikeInstall(string folder) =>
        Directory.Exists(Path.Combine(folder, "game")) || File.Exists(Path.Combine(folder, "version.txt"));

    /// <summary>
    /// When running from anywhere but the install folder, starts the launcher there.
    /// True means that one has taken over and this one must close.
    /// </summary>
    public static bool HandOverToInstalledLauncher()
    {
        if (SameFolder(AppPaths.ExeDirectory, AppPaths.Root) || !File.Exists(AppPaths.LauncherExe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.LauncherExe, HandoverArg)
            {
                WorkingDirectory = AppPaths.Root,
                UseShellExecute = false,
            });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException) { return false; }
    }

    /// <summary>Copies settings, saves and replays out of each old install, once each.
    /// Copies only: those files are the player's, so they stay where they were too.</summary>
    public static void CopyUserFilesOut(LauncherSettings settings, Action<string> log)
    {
        var changed = false;
        foreach (var root in settings.LegacyRoots.ToArray())
        {
            if (settings.MigratedRoots.Any(r => SameFolder(r, root))) continue;
            try
            {
                var copied = UserDataMigration.Copy(root);
                log($"Copied {copied} of your file(s) from {root} to {AppPaths.UserData}.");
                settings.MigratedRoots.Add(root);
                changed = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log($"Could not copy your files from {root} ({ex.Message}); will try again next start.");
            }
        }
        if (changed) TrySave(settings);
    }

    /// <summary>
    /// The elevated half: creates the install folder and lets ordinary users write to
    /// it. Takes no path from the command line, so it can only ever touch that folder.
    /// </summary>
    public static bool PrepareRootElevated()
    {
        try
        {
            var dir = Directory.CreateDirectory(AppPaths.DefaultRoot);
            var acl = dir.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dir.SetAccessControl(acl);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            return false;
        }
    }

    /// <summary>The Windows prompt. False when declined or failed.</summary>
    private static bool RequestElevatedSetup()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(AppPaths.ExePath, PrepareRootArg)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception) { return false; }   // 1223: the player said no
    }

    /// <summary>The only honest test of "can I write here" is writing.</summary>
    private static bool CanWrite(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return false;   // creating it is the elevated step's job
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void TrySave(LauncherSettings settings)
    {
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal for this run; the next start works it out again.
        }
    }
}
