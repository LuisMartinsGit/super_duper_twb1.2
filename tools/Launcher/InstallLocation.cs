using System.Drawing;

namespace TheWaningBorder.Launcher;

/// <summary>
/// Decides where this edition is installed, before anything reads
/// <see cref="AppPaths.Root"/>.
///
/// THE TESTER CHOOSES THE FOLDER (2026-10-16). The root used to be fixed at
/// %LOCALAPPDATA%\Programs\...; now that path is only the suggestion the
/// first install offers. The choice is remembered in the settings file
/// (%APPDATA%, outside the root), in this order:
///
/// <code>
///   1. the remembered folder, if it still exists
///   2. the default folder, if it already holds an install — every tester
///      from before this change, who is never asked
///   3. otherwise ask — the first install, or a remembered folder that is
///      gone (moved, deleted, a drive unplugged); pointing at the moved
///      install adopts it as it is
/// </code>
///
/// The game finds its launcher relative to its own folder (root\game\), so
/// it needs no telling where the root went.
/// </summary>
internal static class InstallLocation
{
    /// <summary>False when the tester quit instead of choosing.</summary>
    public static bool Resolve(LauncherSettings settings, IWin32Window owner)
    {
        string? problem = null;

        if (!string.IsNullOrWhiteSpace(settings.InstallRoot))
        {
            if (Directory.Exists(settings.InstallRoot))
            {
                AppPaths.UseRoot(settings.InstallRoot);
                return true;
            }

            problem = $"The game was installed in {settings.InstallRoot}, which is no longer there. " +
                      "If you moved it, choose its new folder; otherwise choose where to install it again.";
        }
        else if (LooksLikeInstall(AppPaths.DefaultRoot))
        {
            Remember(settings, AppPaths.DefaultRoot);
            return true;
        }

        using var prompt = new InstallFolderForm(AppPaths.DefaultRoot, problem);
        if (prompt.ShowDialog(owner) != DialogResult.OK) return false;

        Remember(settings, prompt.Folder);
        return true;
    }

    /// <summary>A folder the updater has already installed into.</summary>
    public static bool LooksLikeInstall(string folder) =>
        Directory.Exists(Path.Combine(folder, "game")) || File.Exists(Path.Combine(folder, "version.txt"));

    /// <summary>
    /// Why <paramref name="folder"/> cannot be the install root, or null when
    /// it can. Creates the folder as part of the check: the only honest test
    /// of "can I write here" is writing.
    /// </summary>
    public static string? Validate(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            return "Enter a full path, such as D:\\Games\\The Waning Border.";

        string full;
        try { full = Path.GetFullPath(folder); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That is not a valid folder path.";
        }

        if (string.Equals(full.TrimEnd('\\'), Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return "Choose a folder, not the top of a drive.";

        // The updater swaps game\ out wholesale and sweeps leftovers from the
        // root, so it must not share the folder with anything else. An
        // existing install is the exception: choosing it adopts it.
        if (Directory.Exists(full) && !LooksLikeInstall(full) && Directory.EnumerateFileSystemEntries(full).Any())
            return "That folder already has other files in it. Choose an empty or new folder.";

        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot write to that folder ({ex.Message}). Folders such as Program Files need " +
                   "administrator rights; choose one you own.";
        }

        return null;
    }

    private static void Remember(LauncherSettings settings, string folder)
    {
        AppPaths.UseRoot(folder);
        settings.InstallRoot = AppPaths.Root;

        try
        {
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal for this run; the next start falls back to the
            // default-folder check or asks again.
        }
    }
}

/// <summary>Asks where to install. Shown on the first install only.</summary>
internal sealed class InstallFolderForm : Form
{
    private readonly TextBox _path;
    private readonly Label _space;
    private readonly Label _error;

    public string Folder => _path.Text.Trim();

    public InstallFolderForm(string suggestion, string? problem)
    {
        Text = Edition.DisplayName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        // Same construction as KeyPromptForm: font before AutoScaleMode, and
        // the dialog sized from an undocked auto-sizing layout.
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(22);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(22, 22),
            BackColor = Color.Transparent,
        };

        if (problem is not null)
        {
            var warning = Theme.Label(problem, Theme.Body, Theme.Danger);
            warning.Margin = new Padding(0, 0, 0, 14);
            layout.Controls.Add(warning);
        }

        layout.Controls.Add(Theme.Label("Choose where to install the game.", Theme.Body, Theme.Text));

        var hint = Theme.Label(
            "Updates are installed into the same folder. Choose an empty or new folder, " +
            "or the folder of an existing install.",
            Theme.Small, Theme.Muted);
        hint.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(hint);

        _path = new TextBox
        {
            Text = suggestion,
            Width = Theme.ContentWidth,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 4),
        };
        layout.Controls.Add(_path);

        var browse = new Button { Text = "Browse..." };
        Theme.Style(browse, primary: false);
        browse.Margin = new Padding(0, 4, 0, 4);
        browse.Click += (_, _) => Browse();
        layout.Controls.Add(browse);

        _space = Theme.Label("", Theme.Small, Theme.Muted);
        _space.MinimumSize = new Size(Theme.ContentWidth, 0);
        layout.Controls.Add(_space);

        _error = Theme.Label("", Theme.Small, Theme.Danger);
        _error.MinimumSize = new Size(Theme.ContentWidth, 0);
        layout.Controls.Add(_error);

        var install = new Button { Text = "Install" };
        var cancel = new Button { Text = "Quit", DialogResult = DialogResult.Cancel };

        Theme.Style(install, primary: true);
        Theme.Style(cancel, primary: false);

        layout.Controls.Add(Theme.ButtonRow(install, cancel));
        Controls.Add(layout);

        AcceptButton = install;
        CancelButton = cancel;

        // Not DialogResult.OK on the button: the dialog closes only once the
        // folder has passed validation.
        install.Click += (_, _) =>
        {
            var error = InstallLocation.Validate(Folder);
            _error.Text = error ?? "";
            if (error is not null) return;

            DialogResult = DialogResult.OK;
            Close();
        };

        _path.TextChanged += (_, _) =>
        {
            install.Enabled = Folder.Length > 0;
            _error.Text = "";
            ShowFreeSpace();
        };

        ShowFreeSpace();
    }

    /// <summary>
    /// A folder picked here is a PARENT ("D:\Games"), so the product folder
    /// is appended — unless the tester picked an existing install or the
    /// product folder itself.
    /// </summary>
    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for the game",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        var current = Folder;
        var start = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dialog.InitialDirectory = start;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var picked = dialog.SelectedPath;
        var named = string.Equals(Path.GetFileName(picked.TrimEnd('\\')), AppPaths.InstallName, StringComparison.OrdinalIgnoreCase);

        _path.Text = named || InstallLocation.LooksLikeInstall(picked)
            ? picked
            : Path.Combine(picked, AppPaths.InstallName);
    }

    private void ShowFreeSpace()
    {
        try
        {
            var drive = Path.GetPathRoot(Folder);
            _space.Text = string.IsNullOrEmpty(drive)
                ? ""
                : $"{Theme.Bytes(new DriveInfo(drive).AvailableFreeSpace)} free on {drive.TrimEnd('\\')}";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _space.Text = "";
        }
    }
}
