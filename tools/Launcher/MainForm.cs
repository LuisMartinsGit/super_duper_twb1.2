using System.Diagnostics;
using System.Drawing;

namespace TheWaningBorder.Launcher;

internal sealed class MainForm : Form
{
    private readonly Label _status;
    private readonly Label _detail;
    private readonly TextBox _notes;
    private readonly ProgressBar _progress;
    private readonly Button _play;

    private readonly CancellationTokenSource _cancellation = new();
    private LauncherSettings _settings = LauncherSettings.Load();

    public MainForm()
    {
        Text = "The Waning Border";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        // Font before AutoScaleMode, and the whole window sized from its
        // contents: the first version pinned every control to a pixel offset,
        // which clipped every label the moment the display was scaled.
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        // FlowLayoutPanel, undocked, positioned at the padding origin. A
        // docked panel takes its width FROM the form, so the form had nothing
        // to auto-size against and collapsed to 258px around 440px of content.
        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(24, 24),
            BackColor = Color.Transparent,
        };

        var title = Theme.Label("The Waning Border", Theme.Title, Theme.Accent);
        title.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(title);

        _status = Theme.Label("Starting up...", Theme.Body, Theme.Text);
        layout.Controls.Add(_status);

        _notes = new TextBox
        {
            Width = Theme.ContentWidth,
            Height = 120,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel,
            ForeColor = Theme.Muted,
            Font = Theme.Body,
            TabStop = false,
            Visible = false,
            Margin = new Padding(0, 6, 0, 10),
        };
        layout.Controls.Add(_notes);

        _progress = new ProgressBar
        {
            Width = Theme.ContentWidth,
            Height = 12,
            Maximum = 1000,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            Margin = new Padding(0, 4, 0, 6),
        };
        layout.Controls.Add(_progress);

        _detail = Theme.Label("", Theme.Small, Theme.Muted);
        // Reserved so the window does not resize every time this text appears
        // and disappears mid-download.
        _detail.MinimumSize = new Size(Theme.ContentWidth, 0);
        layout.Controls.Add(_detail);

        _play = new Button { Text = "Play", Enabled = false };
        var quit = new Button { Text = "Quit" };

        Theme.Style(_play, primary: true);
        Theme.Style(quit, primary: false);

        _play.Click += (_, _) => Launch();
        quit.Click += (_, _) => Close();

        layout.Controls.Add(Theme.ButtonRow(_play, quit));
        Controls.Add(layout);

        Shown += async (_, _) => await RunAsync().ConfigureAwait(true);
        FormClosing += (_, _) => _cancellation.Cancel();
    }

    private async Task RunAsync()
    {
        Installer.Sweep();

        if (!EnsureKey()) return;

        // Before the update check, so a crashed session reports itself even if
        // the tester quits at the "update available" prompt. Costs nothing in
        // the common case, where there is nothing pending.
        await SweepLogsAsync().ConfigureAwait(true);

        Manifest manifest;

        try
        {
            manifest = await CheckAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (UpdateException ex)
        {
            // Fail open. An outage on my side must not stop a tester from
            // playing the build they already have.
            if (AppPaths.FindGameExe() is not null)
            {
                SetStatus(ex.Message, Theme.Danger);
                SetDetail("Starting the installed build instead.");
                _play.Enabled = true;

                try
                {
                    await Task.Delay(2500, _cancellation.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Launch();
            }
            else
            {
                Fail(ex.Message, "Nothing is installed yet, so the game cannot start.");
            }

            return;
        }

        if (manifest.Version == AppPaths.ReadInstalledVersion() && AppPaths.FindGameExe() is not null)
        {
            SetStatus($"Up to date - version {manifest.Version}.", Theme.Text);
            Launch();
            return;
        }

        try
        {
            await UpdateAsync(manifest).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            var message = ex is UpdateException ? ex.Message : $"The update failed. {ex.Message}";

            if (AppPaths.FindGameExe() is not null)
            {
                Fail(message, "You can still play the version you already have.");
                _play.Enabled = true;
            }
            else
            {
                Fail(message, "Nothing is installed yet, so the game cannot start.");
            }

            return;
        }

        SetStatus($"Updated to version {manifest.Version}.", Theme.Text);
        Launch();
    }

    /// <summary>
    /// Sends whatever the previous session left behind. Bounded and swallowed
    /// on purpose: a log upload must never delay or block someone starting the
    /// game, so a stall here costs 30 seconds and then gets dropped until the
    /// next launch.
    /// </summary>
    private async Task SweepLogsAsync()
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            var uploader = new LogUploader(_settings.ApiBase, _settings.Key);
            var status = new Progress<string>(text => SetDetail(text));

            var (sent, _) = await uploader.SweepAsync(status, timeout.Token).ConfigureAwait(true);

            SetDetail(sent > 0 ? $"Sent {sent} match log{(sent == 1 ? "" : "s")}." : "");
        }
        catch (Exception)
        {
            SetDetail("");
        }
    }

    /// <summary>Prompts until a key is entered, or the tester quits.</summary>
    private bool EnsureKey()
    {
        while (string.IsNullOrWhiteSpace(_settings.Key))
        {
            using var prompt = new KeyPromptForm();

            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                Close();
                return false;
            }

            _settings.Key = prompt.Key;
            TrySaveSettings();
        }

        return true;
    }

    private async Task<Manifest> CheckAsync()
    {
        SetStatus("Checking for updates...", Theme.Text);
        Marquee(true);

        try
        {
            while (true)
            {
                using var client = new UpdateClient(_settings.ApiBase, _settings.Key);

                try
                {
                    return await client.GetLatestAsync(_cancellation.Token).ConfigureAwait(true);
                }
                catch (UpdateException ex) when (ex.IsAuthFailure)
                {
                    // A rejected key is worth one retry with a fresh one rather
                    // than a dead end.
                    using var prompt = new KeyPromptForm(ex.Message);

                    if (prompt.ShowDialog(this) != DialogResult.OK) throw new OperationCanceledException();

                    _settings.Key = prompt.Key;
                    TrySaveSettings();
                }
            }
        }
        finally
        {
            Marquee(false);
        }
    }

    private async Task UpdateAsync(Manifest manifest)
    {
        var installed = AppPaths.ReadInstalledVersion();

        SetStatus(installed is null
            ? $"Installing version {manifest.Version}..."
            : $"Updating {installed} to {manifest.Version}...", Theme.Text);

        if (!string.IsNullOrWhiteSpace(manifest.Notes))
        {
            _notes.Text = manifest.Notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            _notes.Visible = true;
        }

        _progress.Visible = true;
        _progress.Value = 0;

        var zip = Path.Combine(AppPaths.DownloadCache, $"TheWaningBorder-{manifest.Version}.zip");

        using (var client = new UpdateClient(_settings.ApiBase, _settings.Key))
        {
            var lastPaint = Stopwatch.StartNew();

            var progress = new Progress<DownloadProgress>(p =>
            {
                // Skip redundant repaints; the final frame always draws.
                if (lastPaint.ElapsedMilliseconds < 50 && p.Fraction < 1) return;
                lastPaint.Restart();

                _progress.Value = (int)(p.Fraction * _progress.Maximum);

                var rate = p.BytesPerSecond > 0 ? $"   {Theme.Bytes((long)p.BytesPerSecond)}/s" : "";
                var eta = p.Remaining is { } left ? $"   {Theme.Duration(left)}" : "";

                SetDetail($"{Theme.Bytes(p.BytesRead)} of {Theme.Bytes(p.TotalBytes)}{rate}{eta}");
            });

            await client.DownloadAsync(zip, manifest, progress, _cancellation.Token).ConfigureAwait(true);
        }

        SetStatus("Verified. Installing...", Theme.Text);
        SetDetail("Do not close this window.");
        _progress.Value = 0;

        var extract = new Progress<ExtractProgress>(p => _progress.Value = (int)(p.Fraction * _progress.Maximum));
        var token = _cancellation.Token;

        // Off the UI thread: extraction is a long synchronous grind and would
        // otherwise freeze the window.
        await Task.Run(() => Installer.Install(zip, extract, token), token).ConfigureAwait(true);

        AppPaths.WriteInstalledVersion(manifest.Version);

        try
        {
            File.Delete(zip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _progress.Value = _progress.Maximum;
        SetDetail("");
    }

    private void TrySaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the key just will not persist to the next run.
        }
    }

    private void Launch()
    {
        var exe = AppPaths.FindGameExe();

        if (exe is null)
        {
            Fail("The game executable is missing.", "Re-run the launcher to reinstall.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = AppPaths.Game,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Fail("The game would not start.", ex.Message);
            return;
        }

        Close();
    }

    private void Fail(string message, string hint)
    {
        Marquee(false);
        _progress.Visible = false;
        SetStatus(message, Theme.Danger);
        SetDetail(hint);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
        _status.Refresh();
    }

    private void SetDetail(string text)
    {
        _detail.Text = text;
        _detail.Refresh();
    }

    private void Marquee(bool on)
    {
        if (on)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            return;
        }

        _progress.Style = ProgressBarStyle.Continuous;
        if (_progress.Value == 0) _progress.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cancellation.Dispose();
        base.Dispose(disposing);
    }
}
