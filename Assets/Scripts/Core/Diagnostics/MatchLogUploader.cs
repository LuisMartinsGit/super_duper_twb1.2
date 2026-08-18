// MatchLogUploader.cs
// Sends a finished match's log folder to the update server.
// Location: Assets/Scripts/Core/Diagnostics/MatchLogUploader.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    /// <summary>
    /// Uploads a match log folder as soon as the match ends.
    ///
    /// This is the fast path only. It cannot cover a crash or an alt-F4, which
    /// is exactly when the logs matter most, so the launcher sweeps anything
    /// left behind on the next start. The two overlap deliberately; the server
    /// deduplicates by match name, so a match arriving twice is harmless.
    ///
    /// Everything here is best-effort and silent. A tester who cannot reach the
    /// internet still keeps their local folder, which is what the logs README
    /// points them at.
    /// </summary>
    public static class MatchLogUploader
    {
        /// <summary>The server refuses larger, so do not waste the upload.</summary>
        private const long MaxBytes = 25L * 1024 * 1024;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

        /// <summary>
        /// Fire-and-forget. Zipping and sending both happen off the main thread
        /// so the end of a match does not hitch.
        /// </summary>
        public static void Send(string matchFolder)
        {
            if (string.IsNullOrEmpty(matchFolder) || !Directory.Exists(matchFolder)) return;

            var credentials = LauncherCredentials.Read();
            if (credentials == null) return;   // Not installed via the launcher.

            // Captured now: Application.* is main-thread only.
            var version = Application.version;
            var fingerprint = BuildFingerprint.Short;

            Task.Run(() => Upload(matchFolder, credentials.Value, version, fingerprint));
        }

        private static void Upload(
            string matchFolder, (string ApiBase, string Key) credentials, string version, string fingerprint)
        {
            var name = Path.GetFileName(matchFolder.TrimEnd(Path.DirectorySeparatorChar));
            var zipPath = Path.Combine(Path.GetTempPath(), $"twblog-{name}.zip");

            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                // Fully qualified: UnityEngine declares its own CompressionLevel.
                ZipFile.CreateFromDirectory(
                    matchFolder, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);

                if (new FileInfo(zipPath).Length > MaxBytes) return;

                var meta = BuildMetadata(matchFolder, name, version, fingerprint);

                using (var content = new ByteArrayContent(File.ReadAllBytes(zipPath)))
                {
                    content.Headers.Add("Content-Type", "application/zip");

                    using (var request = new HttpRequestMessage(
                               HttpMethod.Post, credentials.ApiBase.TrimEnd('/') + "/logs"))
                    {
                        request.Content = content;
                        request.Headers.Add("X-TWB-Key", credentials.Key);
                        request.Headers.Add("X-TWB-Meta", meta);
                        request.Headers.Add("User-Agent", "TheWaningBorder/1.0");

                        Http.SendAsync(request).GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception)
            {
                // Deliberately silent. The launcher will retry this folder on
                // the next start, and a failed upload must never surface to a
                // player as an error.
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); }
                catch (IOException) { }
            }
        }

        private static string BuildMetadata(
            string matchFolder, string folderName, string version, string fingerprint)
        {
            string outcome = "", duration = "";
            int exceptions = 0, errors = 0, warnings = 0;

            try
            {
                foreach (var line in File.ReadAllLines(Path.Combine(matchFolder, "Summary.txt")))
                {
                    int split = line.IndexOf(':');
                    if (split < 0) continue;

                    var field = line.Substring(0, split).Trim();
                    var value = line.Substring(split + 1).Trim();

                    switch (field)
                    {
                        case "Outcome": outcome = value; break;
                        case "Duration": duration = value; break;
                        case "Exceptions": int.TryParse(value, out exceptions); break;
                        case "Errors": int.TryParse(value, out errors); break;
                        case "Warnings": int.TryParse(value, out warnings); break;
                    }
                }
            }
            catch (IOException) { }

            // Folder name is "yyyy-MM-dd_HH-mm-ss_Map" with an optional role
            // suffix, so the map is everything past the third underscore.
            var parts = folderName.Split('_');
            var map = parts.Length >= 3 ? string.Join("_", parts, 2, parts.Length - 2) : "";

            // Only multiplayer writes a lockstep log.
            var mode = File.Exists(Path.Combine(matchFolder, "Lockstep.log")) ? "multiplayer" : "single";

            var json = new StringBuilder();
            json.Append('{');
            Field(json, "match", folderName); json.Append(',');
            Field(json, "map", map); json.Append(',');
            Field(json, "mode", mode); json.Append(',');
            Field(json, "version", version); json.Append(',');
            Field(json, "fingerprint", fingerprint); json.Append(',');
            Field(json, "outcome", outcome); json.Append(',');
            Field(json, "duration", duration); json.Append(',');
            json.Append("\"exceptions\":").Append(exceptions).Append(',');
            json.Append("\"errors\":").Append(errors).Append(',');
            json.Append("\"warnings\":").Append(warnings);
            json.Append('}');

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToString()));
        }

        private static void Field(StringBuilder sb, string name, string value)
        {
            sb.Append('"').Append(name).Append("\":\"");

            foreach (var c in value ?? string.Empty)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c >= ' ') sb.Append(c);
            }

            sb.Append('"');
        }
    }

    /// <summary>
    /// Reads the endpoint and access key the launcher stored in %APPDATA%.
    ///
    /// The game deliberately holds no credential of its own: if it was not
    /// installed through the launcher there is nothing to read, and uploading
    /// is simply skipped.
    /// </summary>
    internal static class LauncherCredentials
    {
        public static (string ApiBase, string Key)? Read()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TheWaningBorder", "launcher.json");

                if (!File.Exists(path)) return null;

                var text = File.ReadAllText(path);
                var key = Extract(text, "key");
                var apiBase = Extract(text, "apiBase");

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(apiBase)) return null;
                return (apiBase, key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Hand-rolled rather than pulling in a JSON dependency for two flat
        /// string fields written by code we control.
        /// </summary>
        private static string Extract(string json, string field)
        {
            var marker = "\"" + field + "\"";
            int at = json.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return null;

            int colon = json.IndexOf(':', at + marker.Length);
            if (colon < 0) return null;

            int open = json.IndexOf('"', colon + 1);
            if (open < 0) return null;

            int close = json.IndexOf('"', open + 1);
            if (close < 0) return null;

            return json.Substring(open + 1, close - open - 1);
        }
    }
}
