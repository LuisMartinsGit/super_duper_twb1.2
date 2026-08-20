using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace TheWaningBorder.Launcher;

internal sealed class Manifest
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";

    /// <summary>
    /// Every file in the build, for incremental updates. Absent on releases
    /// published before 0.0.12, which is why every consumer treats null as
    /// "download the whole zip" rather than as an error.
    /// </summary>
    [JsonPropertyName("files")] public List<ManifestFile>? Files { get; set; }
}

internal sealed class ManifestFile
{
    /// <summary>Path relative to the game folder, and simultaneously the ZIP
    /// ENTRY NAME — release.ps1 writes the one string into both, so the
    /// patcher can look an entry up without guessing at separators.</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

internal readonly record struct DownloadProgress(long BytesRead, long TotalBytes, TimeSpan Elapsed)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesRead / TotalBytes, 0, 1) : 0;

    // Suppressed for the first half-second, where the sample is too short to
    // mean anything and the readout would just flail.
    public double BytesPerSecond => Elapsed.TotalSeconds > 0.5 ? BytesRead / Elapsed.TotalSeconds : 0;

    public TimeSpan? Remaining
    {
        get
        {
            var rate = BytesPerSecond;
            if (rate <= 0 || TotalBytes <= 0) return null;
            return TimeSpan.FromSeconds((TotalBytes - BytesRead) / rate);
        }
    }
}

/// <summary>Failures a tester can act on. The message is shown to them verbatim.</summary>
internal sealed class UpdateException(string message, bool isAuthFailure = false) : Exception(message)
{
    public bool IsAuthFailure { get; } = isAuthFailure;
}

internal sealed class UpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiBase;

    public UpdateClient(string apiBase, string key)
    {
        _apiBase = apiBase.TrimEnd('/');

        // No overall timeout: a multi-hundred-megabyte download on a slow line
        // would trip the 100 second default. Stalls are handled by cancellation.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // Deliberately a custom header rather than Authorization. HttpClient
        // forwards Authorization across redirects, and the /download hop lands
        // on a pre-signed CDN URL that rejects a second credential.
        _http.DefaultRequestHeaders.Add("X-TWB-Key", key);
        _http.DefaultRequestHeaders.Add("User-Agent", "TWBLauncher/1.0");
    }

    public async Task<Manifest> GetLatestAsync(CancellationToken ct)
    {
        // Cache-bust: the reply is tiny, and staleness here means telling a
        // tester they are up to date when they are not.
        var url = $"{_apiBase}/version?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        using var response = await SendAsync(url, ct).ConfigureAwait(false);

        var manifest = await response.Content.ReadFromJsonAsync<Manifest>(ct).ConfigureAwait(false)
                       ?? throw new UpdateException("The update server sent an unreadable reply.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new UpdateException("The update server did not report a version.");

        return manifest;
    }

    public async Task DownloadAsync(
        string destinationPath,
        Manifest manifest,
        IProgress<DownloadProgress> progress,
        CancellationToken ct)
    {
        using var response = await SendAsync(
            $"{_apiBase}/download", ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        var total = response.Content.Headers.ContentLength ?? manifest.SizeBytes;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var stopwatch = Stopwatch.StartNew();
        long read = 0;

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var destination = new FileStream(
                         destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[1 << 20];
            int count;

            while ((count = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                hasher.AppendData(buffer, 0, count);
                read += count;
                progress.Report(new DownloadProgress(read, total, stopwatch.Elapsed));
            }
        }

        // Verifying before extraction is the entire reason the manifest carries
        // a hash. A truncated download otherwise installs cleanly and surfaces
        // later as a pile of unexplainable crash reports.
        var actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var expected = manifest.Sha256.Trim().ToLowerInvariant();

        if (expected.Length > 0 && actual != expected)
        {
            TryDelete(destinationPath);
            throw new UpdateException("The download was corrupted in transit. Try again.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string url,
        CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        HttpResponseMessage response;

        try
        {
            response = await _http.GetAsync(url, completion, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Could not reach the update server. {ex.Message}");
        }

        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        var reported = await SafeReadErrorAsync(response).ConfigureAwait(false);
        response.Dispose();

        throw status switch
        {
            HttpStatusCode.Unauthorized => new UpdateException(
                reported ?? "Your access key was rejected.", isAuthFailure: true),

            // The Worker returns 503 for its own misconfiguration (expired
            // credential, no release published) with a message worth showing.
            HttpStatusCode.ServiceUnavailable => new UpdateException(
                reported ?? "The update server is not ready."),

            _ => new UpdateException(reported ?? $"The update server returned {(int)status}."),
        };
    }

    private static async Task<string?> SafeReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorBody>().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(payload?.Error) ? null : payload.Error;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    public void Dispose() => _http.Dispose();
}
