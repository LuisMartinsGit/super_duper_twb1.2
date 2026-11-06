using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace TheWaningBorder.Launcher;

/// <summary>
/// Clears a previous install out of a folder we no longer install into, deleting
/// ONLY files the game shipped.
///
/// THE GUARANTEE (developer, 2026-11-06: "DO NOT DELETE ANY OTHER FILES OTHER THAN
/// THE ONES SHIPPED BY THE GAME. if this is not guaranteed, do nothing instead").
/// A file goes only if its relative path, size and SHA-256 together appear in the
/// record of what releases shipped: <c>ShippedFiles.txt.gz</c> (every past release's
/// manifest, tools/gen-shipped-index.py) plus the manifest of the current one. So:
/// <list type="bullet">
/// <item>A file a player or anything else added has a path that is not listed.</item>
/// <item>A listed file someone changed has a hash that is not listed.</item>
/// <item>With no record to compare against, nothing at all is deleted.</item>
/// <item>Only game\, game.old\, game.new\ and the root launcher are looked at. Other
/// folders and files in the root are not read.</item>
/// <item>Links are never followed or deleted, and a folder goes only when empty.</item>
/// </list>
/// A file that cannot be deleted (locked, say) is left and retried on the next start.
/// </summary>
internal sealed class ShippedFileCleanup
{
    private static readonly string[] Scopes = { "game", "game.old", "game.new" };

    /// <summary>Path + size → the hashes shipped for it.</summary>
    private readonly Dictionary<(string Path, long Size), HashSet<string>> _shipped = new();

    public int Records => _shipped.Sum(p => p.Value.Count);

    public void Add(string relativePath, long size, string sha256)
    {
        var key = (Normalize(relativePath), size);
        if (!_shipped.TryGetValue(key, out var hashes)) _shipped[key] = hashes = new HashSet<string>();
        hashes.Add(sha256.Trim().ToLowerInvariant());
    }

    /// <summary>Loads the past releases' record embedded in this launcher. Returns false
    /// (and adds nothing) when it is missing or unreadable.</summary>
    public bool AddEmbeddedRecord()
    {
        try
        {
            var name = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("ShippedFiles.txt.gz", StringComparison.OrdinalIgnoreCase));
            if (name is null) return false;

            using var raw = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
            using var gz = new GZipStream(raw, CompressionMode.Decompress);
            using var reader = new StreamReader(gz);

            var lines = new List<(string, long, string)>();
            while (reader.ReadLine() is { } line)
            {
                // sha256 size path; the path may hold spaces, the first two cannot.
                var a = line.IndexOf(' ');
                var b = a < 0 ? -1 : line.IndexOf(' ', a + 1);
                if (b < 0 || !long.TryParse(line[(a + 1)..b], out var size)) return false;
                lines.Add((line[(b + 1)..], size, line[..a]));
            }

            foreach (var (path, size, sha) in lines) Add(path, size, sha);
            return lines.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return false; }
    }

    /// <summary>Is this a folder we may look inside at all?</summary>
    public static bool IsSafeRoot(string root, string currentRoot)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return false;

        var full = Path.GetFullPath(root).TrimEnd('\\');
        var current = Path.GetFullPath(currentRoot).TrimEnd('\\');

        // Never a drive root, and never the current install or anything around it.
        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(full, current, StringComparison.OrdinalIgnoreCase)) return false;
        if (current.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        if (full.StartsWith(current + "\\", StringComparison.OrdinalIgnoreCase)) return false;

        return Directory.Exists(full) && Directory.Exists(Path.Combine(full, "game"));
    }

    /// <summary>Deletes what is proven shipped under <paramref name="root"/>.</summary>
    /// <returns>Files deleted, and files that were shipped but could not be deleted.</returns>
    public (int Deleted, int Failed) Clean(string root, string? keepRunningExe)
    {
        if (_shipped.Count == 0) return (0, 0);   // nothing to prove anything against: do nothing

        int deleted = 0, failed = 0;

        foreach (var scope in Scopes)
        {
            var folder = Path.Combine(root, scope);
            if (!Directory.Exists(folder) || IsLink(folder)) continue;
            Sweep(folder, folder, keepRunningExe, ref deleted, ref failed);
            RemoveIfEmpty(folder);
        }

        // The launcher that sat in the root is a shipped file too.
        var launcher = Path.Combine(root, AppPaths.LauncherFileName);
        if (File.Exists(launcher)) TryDelete(launcher, AppPaths.LauncherFileName, keepRunningExe, ref deleted, ref failed);

        return (deleted, failed);
    }

    private void Sweep(string folder, string scopeRoot, string? keep, ref int deleted, ref int failed)
    {
        foreach (var file in Directory.GetFiles(folder))
            TryDelete(file, Path.GetRelativePath(scopeRoot, file), keep, ref deleted, ref failed);

        foreach (var sub in Directory.GetDirectories(folder))
        {
            if (IsLink(sub)) continue;
            Sweep(sub, scopeRoot, keep, ref deleted, ref failed);
            RemoveIfEmpty(sub);
        }
    }

    private void TryDelete(string file, string relative, string? keep, ref int deleted, ref int failed)
    {
        try
        {
            if (keep is not null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) return;

            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if (!_shipped.TryGetValue((Normalize(relative), info.Length), out var hashes)) return;

            string sha;
            using (var stream = File.OpenRead(file))
                sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!hashes.Contains(sha)) return;

            File.Delete(file);
            deleted++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed++;
        }
    }

    private static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (!IsLink(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}

/// <summary>
/// Copies a player's own files out of an old install into their user folder. Copies
/// only, and never over a file already there: the originals are not shipped files, so
/// they stay where they are.
/// </summary>
internal static class UserDataMigration
{
    private static readonly string[] Folders = { "Saves", "Replays" };

    public static int Copy(string root)
    {
        var game = Path.Combine(root, "game");
        if (!Directory.Exists(game)) return 0;

        int copied = 0;
        Directory.CreateDirectory(AppPaths.UserData);

        CopyFile(Path.Combine(game, "settings.json"), Path.Combine(AppPaths.UserData, "settings.json"), ref copied);

        foreach (var name in Folders)
        {
            var from = Path.Combine(game, name);
            if (!Directory.Exists(from)) continue;
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                CopyFile(file, Path.Combine(AppPaths.UserData, name, Path.GetRelativePath(from, file)), ref copied);
        }

        return copied;
    }

    private static void CopyFile(string from, string to, ref int copied)
    {
        try
        {
            if (!File.Exists(from) || File.Exists(to)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: false);
            copied++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
