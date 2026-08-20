using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace TheWaningBorder.Launcher;

/// <summary>
/// Incremental updates: fetch only the files that changed, by HTTP range
/// request into the release zip we already publish.
///
/// WHY THIS IS WORTH IT. A build is 1.1 GB, of which resources.assets.resS is
/// 863 MB, and that file does not change unless an asset does. v0.0.10 and
/// v0.0.11 differed by a one-line code fix; their zips differed by 78 bytes,
/// and every tester downloaded 473 MB to get it.
///
/// WHY RANGES AND NOT PER-FILE ASSETS. The release stays exactly one zip plus
/// one manifest. GitHub's release CDN answers 206 with Accept-Ranges: bytes,
/// and the Worker already 302s straight to it, so nothing server-side changes
/// and no endpoint has to learn about individual files.
///
/// EVERY failure path falls back to the full download. A patch that cannot be
/// completed must cost a tester bandwidth, never a working install.
/// </summary>
internal static class Patcher
{
    /// <summary>
    /// Above this share of the archive, patching stops being worth it: the
    /// ranges stop being sparse, and one sequential download beats hundreds of
    /// seeks for the same bytes.
    /// </summary>
    private const double FullDownloadThreshold = 0.5;

    private const uint EndOfCentralDirectory = 0x06054b50;
    private const uint CentralFileHeader = 0x02014b50;
    private const uint LocalFileHeader = 0x04034b50;

    /// <summary>A file the patch has to fetch, or a stale one to remove.</summary>
    internal sealed record PatchPlan(
        IReadOnlyList<ManifestFile> Fetch,
        IReadOnlyList<string> Remove,
        long BytesToFetch)
    {
        public bool IsWorthwhile => Fetch.Count > 0 || Remove.Count > 0;
    }

    /// <summary>
    /// Work out what a patch would involve, or null when the whole zip should
    /// be downloaded instead.
    ///
    /// Null is returned for a first install, a release published without a
    /// file list, and a change big enough that patching would not pay — all
    /// normal, none of them errors.
    /// </summary>
    public static PatchPlan? Plan(Manifest manifest, CancellationToken ct)
    {
        if (manifest.Files is not { Count: > 0 }) return null;
        if (!Directory.Exists(AppPaths.Game)) return null;

        var fetch = new List<ManifestFile>();
        long bytes = 0;

        foreach (var wanted in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();

            var local = Path.Combine(AppPaths.Game, wanted.Path);

            // Size first: it rejects a changed file without reading it, and
            // most changed files do change size.
            if (!File.Exists(local) ||
                new FileInfo(local).Length != wanted.Size ||
                !HashFile(local).Equals(wanted.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                fetch.Add(wanted);
                bytes += wanted.Size;
            }
        }

        // Anything on disk the new build no longer contains. logs\ is the
        // tester's, not ours — Installer.CarryOverLogs deliberately preserves
        // it, so it must not be swept up here.
        var keep = new HashSet<string>(
            manifest.Files.Select(f => Normalise(f.Path)), StringComparer.OrdinalIgnoreCase);

        var remove = new List<string>();

        foreach (var file in Directory.GetFiles(AppPaths.Game, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppPaths.Game, file);
            if (relative.StartsWith("logs", StringComparison.OrdinalIgnoreCase)) continue;
            if (!keep.Contains(Normalise(relative))) remove.Add(relative);
        }

        if (manifest.SizeBytes > 0 && bytes > manifest.SizeBytes * FullDownloadThreshold)
            return null;

        return new PatchPlan(fetch, remove, bytes);
    }

    /// <summary>
    /// Apply a plan into staging and swap it in. Throws on any problem; the
    /// caller is expected to fall back to the full download.
    /// </summary>
    public static async Task ApplyAsync(
        PatchPlan plan,
        Manifest manifest,
        string apiBase,
        string key,
        IProgress<DownloadProgress> progress,
        CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Add("User-Agent", "TWBLauncher/1.0");

        var url = await ResolveAssetUrlAsync(apiBase, key, ct).ConfigureAwait(false);
        var directory = await ReadCentralDirectoryAsync(http, url, ct).ConfigureAwait(false);

        // Reclaim the rollback copy first, exactly as the full install does,
        // so peak disk stays at two copies of the build rather than three.
        DeleteDirectory(AppPaths.Previous);
        DeleteDirectory(AppPaths.Staging);

        Installer.SeedStagingFromInstalled(ct);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        long done = 0;

        foreach (var wanted in plan.Fetch)
        {
            ct.ThrowIfCancellationRequested();

            // Normalise BOTH sides. The directory is keyed on forward slashes
            // and the manifest path carries the backslashes release.ps1 wrote,
            // so looking up the raw path missed every entry — which failed
            // safe, into a full download, and would have looked like the
            // feature simply not working.
            if (!directory.TryGetValue(Normalise(wanted.Path), out var entry))
                throw new UpdateException($"The update package has no entry for {wanted.Path}.");

            var bytes = await ReadEntryAsync(http, url, entry, ct).ConfigureAwait(false);

            // Verify before it lands. This is the per-file equivalent of the
            // whole-zip hash check, and the reason a torn range cannot install.
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            if (!actual.Equals(wanted.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException($"{wanted.Path} arrived corrupted.");

            var target = Path.Combine(AppPaths.Staging, wanted.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);

            done += wanted.Size;
            progress.Report(new DownloadProgress(done, plan.BytesToFetch, watch.Elapsed));
        }

        foreach (var relative in plan.Remove)
        {
            var stale = Path.Combine(AppPaths.Staging, relative);
            if (File.Exists(stale)) File.Delete(stale);
        }

        Installer.Swap();
    }

    // ── The release asset ────────────────────────────────────────────────

    /// <summary>
    /// The CDN URL behind /download. Taken WITHOUT following the redirect: the
    /// launcher's key header would otherwise ride along to a pre-signed URL
    /// that rejects a second credential, and the ranged reads below need the
    /// final address anyway.
    /// </summary>
    private static async Task<Uri> ResolveAssetUrlAsync(string apiBase, string key, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("X-TWB-Key", key);
        http.DefaultRequestHeaders.Add("User-Agent", "TWBLauncher/1.0");

        using var response = await http
            .GetAsync($"{apiBase.TrimEnd('/')}/download", HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Redirect
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.MovedPermanently)
        {
            var location = response.Headers.Location;
            if (location is not null) return location;
        }

        throw new UpdateException("The update server did not hand back a download location.");
    }

    private static async Task<byte[]> RangeAsync(
        HttpClient http, Uri url, long from, long length, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, from + length - 1);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        // A 200 here means the server ignored the range and is about to send
        // the whole 473 MB archive. Refuse rather than silently swallow it.
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new UpdateException(
                $"The download server does not support partial downloads ({(int)response.StatusCode}).");

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    // ── Zip reading, one range at a time ─────────────────────────────────

    private readonly record struct Entry(long LocalHeaderOffset, long CompressedSize, ushort Method);

    /// <summary>
    /// Map every entry name to where its bytes live, read from the archive's
    /// central directory rather than by scanning the file.
    /// </summary>
    private static async Task<Dictionary<string, Entry>> ReadCentralDirectoryAsync(
        HttpClient http, Uri url, CancellationToken ct)
    {
        // The end record is at most 22 bytes plus a 64 KB comment. Read the
        // tail in one request and find it there.
        const int TailSize = 66_000;

        long total = await ContentLengthAsync(http, url, ct).ConfigureAwait(false);
        int take = (int)Math.Min(TailSize, total);
        var tail = await RangeAsync(http, url, total - take, take, ct).ConfigureAwait(false);

        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EndOfCentralDirectory)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0) throw new UpdateException("The update package is not a readable archive.");

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12));
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));

        // 0xFFFFFFFF means the real values live in a Zip64 record. Our builds
        // are far below the 4 GB boundary that triggers it, so rather than
        // implement Zip64 for a case that should never happen, bail and let
        // the caller fall back to the full download.
        if (size == uint.MaxValue || offset == uint.MaxValue)
            throw new UpdateException("The update package uses Zip64.");

        var central = await RangeAsync(http, url, offset, size, ct).ConfigureAwait(false);
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        int p = 0;
        while (p + 46 <= central.Length &&
               BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(p)) == CentralFileHeader)
        {
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 10));
            uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(p + 20));
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 28));
            ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 30));
            ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(p + 32));
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(p + 42));

            var name = System.Text.Encoding.UTF8.GetString(central, p + 46, nameLen);

            if (compressed != uint.MaxValue && localOffset != uint.MaxValue)
                entries[Normalise(name)] = new Entry(localOffset, compressed, method);

            p += 46 + nameLen + extraLen + commentLen;
        }

        return entries;
    }

    private static async Task<long> ContentLengthAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        // One byte, for the Content-Range total. A HEAD would be tidier but is
        // not guaranteed on a pre-signed URL.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        var length = response.Content.Headers.ContentRange?.Length;

        if (length is null or 0)
            throw new UpdateException("The download server did not report the package size.");

        return length.Value;
    }

    /// <summary>Fetch one entry's bytes and decompress them.</summary>
    private static async Task<byte[]> ReadEntryAsync(
        HttpClient http, Uri url, Entry entry, CancellationToken ct)
    {
        // The local header repeats the name and extra fields, and its extra
        // length can differ from the central directory's — so the data offset
        // has to come from the local header, not be assumed.
        var header = await RangeAsync(http, url, entry.LocalHeaderOffset, 30, ct).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalFileHeader)
            throw new UpdateException("The update package is damaged.");

        ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));

        // An empty file has nothing to ask for, and Range: bytes=n-(n-1) is
        // not a request any server will answer.
        if (entry.CompressedSize == 0) return Array.Empty<byte>();

        long dataAt = entry.LocalHeaderOffset + 30 + nameLen + extraLen;
        var raw = await RangeAsync(http, url, dataAt, entry.CompressedSize, ct).ConfigureAwait(false);

        if (entry.Method == 0) return raw;   // stored

        using var compressed = new MemoryStream(raw);
        using var inflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        await inflate.CopyToAsync(plain, ct).ConfigureAwait(false);
        return plain.ToArray();
    }

    // ── Shared ───────────────────────────────────────────────────────────

    /// <summary>
    /// One spelling for a path. release.ps1 writes entry names with Windows
    /// separators; a zip may carry either, and the local walk produces the
    /// platform's. Comparing raw strings would miss files and re-download them.
    /// </summary>
    private static string Normalise(string path) => path.Replace('\\', '/');

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
