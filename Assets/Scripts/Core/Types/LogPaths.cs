// LogPaths.cs
// One place that decides where the game writes its logs.

using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Resolves the folder every log file goes into, and guarantees it exists.
///
/// The alpha testers have to be able to FIND these and send them back, so the
/// folder sits next to the executable rather than in Unity's usual
/// <c>%USERPROFILE%\AppData\LocalLow\...</c> hole that nobody browses to.
///
/// <c>Application.dataPath</c> points at <c>&lt;Game&gt;_Data</c> in a Windows
/// player and at <c>&lt;project&gt;/Assets</c> in the editor, so "one level up,
/// then logs" lands on:
///   player : &lt;folder containing the .exe&gt;/logs
///   editor : &lt;project root&gt;/logs      (the existing logs/ folder)
/// — the same relative shape in both, which is why the editor logs and the
/// shipped logs stay comparable.
///
/// Falls back to <see cref="Application.persistentDataPath"/> if that folder
/// cannot be created: a build installed under Program Files may not be
/// writable, and losing the logs is worse than losing the convenient location.
/// </summary>
public static class LogPaths
{
    private const string FolderName = "logs";

    private static string _resolved;

    /// <summary>
    /// Absolute path to the logs folder, created on first use. Never returns
    /// null; on total failure it returns the persistent data path, which is
    /// always writable.
    /// </summary>
    public static string Directory
    {
        get
        {
            if (!string.IsNullOrEmpty(_resolved)) return _resolved;

            string preferred;
            try
            {
                preferred = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", FolderName));
            }
            catch
            {
                preferred = null;
            }

            if (TryEnsure(preferred)) { _resolved = preferred; return _resolved; }

            // Read-only install location (Program Files, a mounted image, …).
            string fallback = Path.Combine(Application.persistentDataPath, FolderName);
            if (TryEnsure(fallback)) { _resolved = fallback; return _resolved; }

            _resolved = Application.persistentDataPath;
            return _resolved;
        }
    }

    /// <summary>Full path for a log file inside <see cref="Directory"/>.</summary>
    public static string File(string fileName) => Path.Combine(Directory, fileName);

    // ═══════════════════════════════════════════════════════════════════
    // INSTANCE DISCRIMINATOR — for testing multiplayer on one machine
    // ═══════════════════════════════════════════════════════════════════
    //
    // Two copies of the game running side by side is how multiplayer gets
    // tested, and depending on how the second copy is made they may or may not
    // share this folder:
    //
    //   * ParrelSync clones live in <project>_clone_N, a different dataPath,
    //     so each gets its own logs/ — findable but scattered.
    //   * Unity 6's Multiplayer Play Mode virtual players share the project
    //     path exactly. Both processes resolve the SAME logs/Console.log, the
    //     second StreamWriter fails to open it, and one instance silently
    //     stops logging — which is the instance you needed.
    //   * Two copies of a built exe in one folder do the same thing.
    //
    // So: each process claims the lowest free slot by holding an exclusive
    // lock file for its lifetime. Slot 0 keeps the plain filenames (nothing
    // changes for single-player), slot 1+ gets a suffix. The lock releases
    // when the process exits, so slots are reused rather than climbing.

    private static int _instanceSlot = -1;
    private static FileStream _instanceLock;

    /// <summary>
    /// 0 for the first game process using this folder, 1 for the next, and so
    /// on. Stable for the life of the process.
    /// </summary>
    public static int InstanceSlot
    {
        get
        {
            if (_instanceSlot >= 0) return _instanceSlot;

            for (int slot = 0; slot < 8; slot++)
            {
                try
                {
                    _instanceLock = new FileStream(
                        Path.Combine(Directory, $".instance{slot}.lock"),
                        FileMode.Create, FileAccess.Write, FileShare.None);
                    _instanceSlot = slot;
                    return _instanceSlot;
                }
                catch (IOException)
                {
                    // Held by another running instance — try the next slot.
                }
                catch
                {
                    break;   // not a locking problem; stop trying
                }
            }

            // Could not claim any slot (read-only folder, exotic filesystem).
            // Fall back to slot 0 and accept that two instances would collide,
            // which is no worse than the behaviour this replaced.
            _instanceSlot = 0;
            return _instanceSlot;
        }
    }

    /// <summary>
    /// Appended to log file and folder names so two instances sharing this
    /// folder never write to the same file. Empty for the first instance.
    /// </summary>
    public static string InstanceSuffix => InstanceSlot == 0 ? "" : $"-{InstanceSlot + 1}";

    /// <summary>
    /// <paramref name="fileName"/> with the instance discriminator inserted
    /// before its extension: "Console.log" -> "Console-2.log" on the second
    /// instance, unchanged on the first.
    /// </summary>
    public static string InstanceFileName(string fileName)
    {
        string suffix = InstanceSuffix;
        if (suffix.Length == 0) return fileName;

        int dot = fileName.LastIndexOf('.');
        return dot <= 0
            ? fileName + suffix
            : fileName.Substring(0, dot) + suffix + fileName.Substring(dot);
    }

    private static bool TryEnsure(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Directory.Exists(dir);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Filename-safe timestamp, used to keep one match's logs separate from
    /// the next so a tester can send "the one where it broke".
    /// </summary>
    public static string TimestampNow()
        => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
}
