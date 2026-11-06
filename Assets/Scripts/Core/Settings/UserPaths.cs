// UserPaths.cs
// Where a player's own files live (docs/Design/Alpha_Build.md § Where things live).

using System;
using System.IO;
using UnityEngine;

namespace TheWaningBorder.Core.Config
{
    /// <summary>
    /// %APPDATA%\ShardrootEntertainment\TheWaningBorder holds settings, saves, replays and
    /// logs, and the launcher's own settings: an install under Program Files holds only
    /// what the game ships, and an update never touches this folder. The launcher names
    /// the same folder (tools/Launcher/AppPaths.UserData).
    ///
    /// The Editor and batch-mode test runs have no such folder (<see cref="Root"/> is
    /// null) and keep writing beside the project or the .exe, so a test never reads or
    /// writes a person's settings or saves.
    /// </summary>
    public static class UserPaths
    {
        /// <summary>The player's folder, or null in the Editor and in batch mode.</summary>
        public static string Root
        {
            get
            {
                if (Application.isEditor || Application.isBatchMode) return null;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ShardrootEntertainment", "TheWaningBorder");
            }
        }

        /// <summary>A file or folder inside <see cref="Root"/>, or null when there is no Root.</summary>
        public static string In(string name)
        {
            string root = Root;
            return root == null ? null : Path.Combine(root, name);
        }
    }
}
