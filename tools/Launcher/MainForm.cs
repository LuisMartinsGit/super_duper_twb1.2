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
    private readonly Button _quit;

    private readonly CancellationTokenSource _cancellation = new();
    private LauncherSettings _settings = LauncherSettings.Load();

    public MainForm()
    {
        Text = "The Waning Border";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        ClientSize = new Size(520, 322);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Font;

        Controls.Add(new Label
        {
            Text = "The Waning Border",
            Font = Theme.Title,
            ForeColor = Theme.Accent,
            Location = new Point(24, 20),
            Size = new Size(400, 28),
        });

        _status = new Label
        {
            Text = "Starting up...",
            Font = Theme.Body,
            ForeColor = Theme.Text,
            Location = new Point(24, 54),
            Size = new Size(472, 20),
        };
        Controls.Add(_status);

        _notes = new TextBox
        {
            Location = new Point(24, 82),
            Size = new Size(472, 122),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel,
            ForeColor = Theme.Muted,
            Font = Theme.Body,
            TabStop = false,
            Visible = false,
        };
        Controls.Add(_notes);

        _progress = new ProgressBar
        {
            Location = new Point(24, 216),
            Size = new Size(472, 12),
            Maximum = 1000,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };
        Controls.Add(_progress);

        _detail = new Label
        {
            Text = "",
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            Location = new Point(24, 234),
            Size = new Size(472, 18),
        };
        Controls.Add(_detail);

        _play = new Button { Text = "Play", Location = new Point(304, 268), Width = 100, Enabled = false };
        Theme.Style(_play, primary: true);
        _play.Click += (_, _) => Launch();
        Controls.Add(_play);

        _quit = new Button { Text = "Quit", Location = new Point(412, 268), Width = 84 };
        Theme.Style(_quit, primary: false);
        _quit.Click += (_, _) => Close();
        Controls.Add(_quit);

        Shown += async (_, _) => await RunAsync().ConfigureAwait(true);
        FormClosing += (_, _) => _cancellation.Cancel();
    }

    private async Task RunAsync()
    {
        Installer.Sweep();

        if (!await EnsureKeyAsync().ConfigureAwait(true)) return;

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
                SetStatus($"{ex.Message}", Theme.Danger);
                SetDetail("Starting the installed build instead.");
                _play.Enabled = true;
                await Task.Delay(2500, _cancellation.Token).ConfigureAwait(true);
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

            // The previous build is still on disk untouched, so offer it.
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

    /// <summary>Prompts until the stored key is accepted, or the tester quits.</summary>
    private async Task<bool> EnsureKeyAsync()
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

            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not fatal: the key just will not persist to the next run.
            }
        }

        await Task.CompletedTask.ConfigureAwait(true);
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
                    try { _settings.Save(); } catch (IOException) { }
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

                var rate = p.BytesPerSecond > 0 ? $"  -  {Theme.Bytes((long)p.BytesPerSecond)}/s" : "";
                var eta = p.Remaining is { } left ? $"  -  {Theme.Duration(left)}" : "";

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
        _progress.Visible = on || _progress.Visible;
        _progress.Style = on ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!on && _progress.Value == 0) _progress.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cancellation.Dispose();
        base.Dispose(disposing);
    }
}
