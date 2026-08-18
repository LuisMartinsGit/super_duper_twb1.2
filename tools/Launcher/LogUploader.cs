using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheWaningBorder.Launcher;

/// <summary>
/// Sends match logs the previous session left behind.
///
/// The game also uploads at match end, which is faster. This exists for the
/// case that path cannot cover: a crash or a hard quit mid-match, which is
/// precisely the match whose logs are worth the most. Running here means it
/// happens before the game is started again, from a process that already holds
/// the tester key.
///
/// Nothing is ever deleted locally. Uploading is a copy - the tester keeps
/// their folder, and the README still tells them where it is.
/// </summary>
internal sealed class LogUploader(string apiBase, string key)
{
    /// <summary>Matches the folder names MatchLogSession produces.</summary>
    private const string SummaryFileName = "Summary.txt";

    /// <summary>The server refuses larger; skip rather than waste the upload.</summary>
    private const long MaxBytes = 25L * 1024 * 1024;

    private readonly string _apiBase = apiBase.TrimEnd('/');

    public async Task<(int sent, int skipped)> SweepAsync(IProgress<string> status, CancellationToken ct)
    {
        var logsRoot = Path.Combine(AppPaths.Game, "logs");
        if (!Directory.Exists(logsRoot)) return (0, 0);

        var done = UploadLedger.Load();
        var pending = Directory.GetDirectories(logsRoot)
            .Where(d => File.Exists(Path.Combine(d, SummaryFileName)))
            .Where(d => !done.Contains(Path.GetFileName(d)))
            .OrderBy(d => d)
            .ToArray();

        if (pending.Length == 0) return (0, 0);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Add("X-TWB-Key", key);
        http.DefaultRequestHeaders.Add("User-Agent", "TWBLauncher/1.0");

        int sent = 0, skipped = 0;

        for (int i = 0; i < pending.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            status.Report($"Sending match logs ({i + 1} of {pending.Length})...");

            try
            {
                if (await SendAsync(http, pending[i], ct).ConfigureAwait(false))
                {
                    done.Add(Path.GetFileName(pending[i]));
                    sent++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-request timeout. Leave it unmarked and try next launch.
                skipped++;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        UploadLedger.Save(done);
        return (sent, skipped);
    }

    private async Task<bool> SendAsync(HttpClient http, string matchFolder, CancellationToken ct)
    {
        var name = Path.GetFileName(matchFolder);
        var zipPath = Path.Combine(Path.GetTempPath(), $"twblog-{name}.zip");

        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(matchFolder, zipPath, CompressionLevel.Optimal, false);

            var info = new FileInfo(zipPath);
            if (info.Length > MaxBytes) return false;

            var meta = MatchMetadata.FromSummary(matchFolder, name);

            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, ct).ConfigureAwait(false));
            content.Headers.Add("Content-Type", "application/zip");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBase}/logs") { Content = content };
            request.Headers.Add("X-TWB-Meta", meta.ToHeader());

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

            // 413 and 429 are permanent for this file: retrying next launch
            // would fail identically, so mark them done rather than looping.
            if (response.StatusCode is System.Net.HttpStatusCode.RequestEntityTooLarge
                                    or System.Net.HttpStatusCode.TooManyRequests)
            {
                return true;
            }

            return response.IsSuccessStatusCode;
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); }
            catch (IOException) { }
        }
    }
}

/// <summary>
/// What the server needs to describe a match without unzipping it. Parsed from
/// Summary.txt, which MatchLogSession writes as aligned "Key : value" lines.
/// </summary>
internal sealed class MatchMetadata
{
    public string Match { get; set; } = "";
    public string Map { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Version { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Duration { get; set; } = "";
    public int Exceptions { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }

    public static MatchMetadata FromSummary(string matchFolder, string folderName)
    {
        var meta = new MatchMetadata { Match = folderName };

        // Folder name is "yyyy-MM-dd_HH-mm-ss_Map" with an optional role
        // suffix, so the map is everything after the third underscore.
        var parts = folderName.Split('_');
        if (parts.Length >= 3) meta.Map = string.Join("_", parts.Skip(2));

        // Multiplayer matches are the only ones that write a lockstep log.
        meta.Mode = File.Exists(Path.Combine(matchFolder, "Lockstep.log")) ? "multiplayer" : "single";

        try
        {
            foreach (var line in File.ReadAllLines(Path.Combine(matchFolder, "Summary.txt")))
            {
                var split = line.IndexOf(':');
                if (split < 0) continue;

                var field = line[..split].Trim();
                var value = line[(split + 1)..].Trim();

                switch (field)
                {
                    case "Outcome": meta.Outcome = value; break;
                    case "Duration": meta.Duration = value; break;
                    case "Fingerprint": meta.Fingerprint = value; break;
                    case "Exceptions": meta.Exceptions = ParseCount(value); break;
                    case "Errors": meta.Errors = ParseCount(value); break;
                    case "Warnings": meta.Warnings = ParseCount(value); break;

                    // "The Waning Border 0.0.8" - the version is the last token.
                    case "Build":
                        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length > 0) meta.Version = tokens[^1];
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable summary still uploads; the zip is the payload and
            // the folder name alone identifies the match.
        }

        return meta;
    }

    private static int ParseCount(string value) => int.TryParse(value, out var n) ? n : 0;

    /// <summary>Base64 of the JSON, so it survives as a single HTTP header.</summary>
    public string ToHeader()
    {
        var payload = JsonSerializer.Serialize(new
        {
            match = Match,
            map = Map,
            mode = Mode,
            version = Version,
            fingerprint = Fingerprint,
            outcome = Outcome,
            duration = Duration,
            exceptions = Exceptions,
            errors = Errors,
            warnings = Warnings,
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }
}

/// <summary>
/// Remembers which matches have been sent, so a sweep does not re-zip and
/// re-post everything on every launch.
///
/// Losing this file is harmless rather than duplicating: the server dedups by
/// object key, so a re-send is answered with "duplicate" and changes nothing.
/// That is why it is only an optimisation and never a correctness dependency.
/// </summary>
internal static class UploadLedger
{
    private static string Path_ => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(AppPaths.SettingsFile)!, "uploaded-logs.json");

    public static HashSet<string> Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var names = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path_));
                if (names is not null) return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(HashSet<string> names)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(names.ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
