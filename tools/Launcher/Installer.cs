using System.IO.Compression;

namespace TheWaningBorder.Launcher;

internal readonly record struct ExtractProgress(long BytesDone, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesDone / TotalBytes, 0, 1) : 0;
}

/// <summary>
/// Swaps a downloaded build into place. See <see cref="AppPaths"/> for the
/// folder layout this maintains.
/// </summary>
internal static class Installer
{
    public static void Install(string zipPath, IProgress<ExtractProgress> progress, CancellationToken ct)
    {
        // Reclaiming the rollback copy BEFORE staging keeps peak disk at two
        // copies of the build rather than three.
        DeleteDirectory(AppPaths.Previous);
        DeleteDirectory(AppPaths.Staging);

        Extract(zipPath, AppPaths.Staging, progress, ct);
        FlattenSingleRoot(AppPaths.Staging);

        var hadPrevious = Directory.Exists(AppPaths.Game);

        if (hadPrevious)
        {
            CarryOverLogs();
            Directory.Move(AppPaths.Game, AppPaths.Previous);
        }

        try
        {
            Directory.Move(AppPaths.Staging, AppPaths.Game);
        }
        catch
        {
            // Never leave the tester without a game folder. Put the old build
            // back before surfacing the failure.
            if (hadPrevious && !Directory.Exists(AppPaths.Game) && Directory.Exists(AppPaths.Previous))
                Directory.Move(AppPaths.Previous, AppPaths.Game);

            throw;
        }
    }

    /// <summary>
    /// Match logs are the point of the alpha build, so they have to survive a
    /// wholesale folder swap. Moved late, once staging is known good.
    /// </summary>
    private static void CarryOverLogs()
    {
        var existing = Path.Combine(AppPaths.Game, "logs");
        if (!Directory.Exists(existing)) return;

        var incoming = Path.Combine(AppPaths.Staging, "logs");

        try
        {
            DeleteDirectory(incoming);
            Directory.Move(existing, incoming);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing logs is bad, but not bad enough to abort an update over.
            // The old build is kept as game.old, so they are still recoverable.
        }
    }

    private static void Extract(
        string zipPath, string destination, IProgress<ExtractProgress> progress, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        long total = archive.Entries.Sum(e => e.Length);
        long done = 0;

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));

            // Zip-slip guard. We build these archives ourselves, but an entry
            // that escapes the staging folder would write straight into the
            // install root, so the check is worth its four lines.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("The downloaded archive is malformed.");

            // A zero-length Name marks a directory entry.
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);

            done += entry.Length;
            progress.Report(new ExtractProgress(done, total));
        }
    }

    /// <summary>
    /// Tolerates a zip that wraps the build in one top-level folder, so it does
    /// not matter whether the release was zipped as the folder or its contents.
    /// </summary>
    private static void FlattenSingleRoot(string staging)
    {
        var directories = Directory.GetDirectories(staging);
        if (directories.Length != 1 || Directory.GetFiles(staging).Length != 0) return;

        var inner = directories[0];

        foreach (var directory in Directory.GetDirectories(inner))
            Directory.Move(directory, Path.Combine(staging, Path.GetFileName(directory)));

        foreach (var file in Directory.GetFiles(inner))
            File.Move(file, Path.Combine(staging, Path.GetFileName(file)));

        Directory.Delete(inner, recursive: true);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Usually a stale handle from a previous run. Renaming it aside
            // lets this update proceed; the next one clears the leftovers.
            var abandoned = $"{path}.{Guid.NewGuid():N}.delete";

            try
            {
                Directory.Move(path, abandoned);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                throw new UpdateException(
                    $"Could not clear {Path.GetFileName(path)}. Close the game and try again.");
            }
        }
    }

    /// <summary>Removes leftovers from earlier interrupted updates.</summary>
    public static void Sweep()
    {
        if (!Directory.Exists(AppPaths.Root)) return;

        foreach (var path in Directory.GetDirectories(AppPaths.Root, "*.delete"))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
