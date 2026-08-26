// BuildFingerprint.cs
// A short checksum of the shipped game files, for identifying an exact build.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    /// <summary>
    /// An eight-character hash of the build actually sitting on disk.
    ///
    /// <see cref="Application.version"/> alone cannot answer "are these two
    /// installs the same build". It is a number a human types into Player
    /// Settings, so two different builds carry the same version whenever
    /// someone rebuilds without bumping it, and a half-finished update that
    /// left mismatched files behind still reports the version it claims to be.
    /// That is exactly the case the lobby handshake and tester bug reports need
    /// to distinguish, so the menu shows this next to the version.
    ///
    /// Cost is bounded on purpose. Only small, always-present build files are
    /// read; the IL2CPP blob contributes its length rather than its contents,
    /// because hashing a few hundred megabytes on every launch to catch a case
    /// the build GUID already covers is not a trade worth making.
    /// </summary>
    public static class BuildFingerprint
    {
        /// <summary>Shown instead of a hash when running in the editor.</summary>
        public const string EditorValue = "editor";

        /// <summary>Shown when the build files cannot be read at all.</summary>
        public const string UnknownValue = "unknown";

        /// <summary>Guards against a pathological file stalling startup.</summary>
        private const long MaxBytesPerFile = 64L * 1024 * 1024;

        private static string _short;

        /// <summary>
        /// Eight lowercase hex characters, or <see cref="EditorValue"/> /
        /// <see cref="UnknownValue"/>. Computed once on first access.
        /// </summary>
        public static string Short => _short ??= Compute();

        private static string Compute()
        {
            if (Application.isEditor) return EditorValue;

            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                // The build GUID is regenerated per build, so it separates two
                // builds even when every file below happens to be identical.
                Feed(hash, Application.version);
                Feed(hash, Application.buildGUID);

                var data = Application.dataPath;
                var root = Directory.GetParent(data)?.FullName ?? data;

                // Contents: small, present in every player, and covering both
                // build settings and the first scene.
                FeedFile(hash, Path.Combine(data, "globalgamemanagers"), contents: true);
                FeedFile(hash, Path.Combine(data, "level0"), contents: true);
                FeedFile(hash, Path.Combine(data, "Managed", "Assembly-CSharp.dll"), contents: true);

                // Length only: hundreds of megabytes, and a code change moves
                // both its size and the build GUID in practice.
                FeedFile(hash, Path.Combine(root, "GameAssembly.dll"), contents: false);

                return ToHex(hash.GetHashAndReset(), 4);
            }
            catch (Exception ex)
            {
                // A missing fingerprint must never stop the game reaching the
                // menu; it is a diagnostic, not a gate.
                Debug.LogWarning($"[BuildFingerprint] Could not compute: {ex.Message}");
                return UnknownValue;
            }
        }

        /// <summary>
        /// Hand-rolled rather than <c>Convert.ToHexString</c>: that is .NET 5+
        /// and Unity 6 compiles against .NET Standard 2.1, where it does not
        /// exist.
        /// </summary>
        private static string ToHex(byte[] bytes, int count)
        {
            const string Digits = "0123456789abcdef";
            var chars = new char[count * 2];

            for (int i = 0; i < count; i++)
            {
                chars[i * 2] = Digits[(bytes[i] >> 4) & 0xF];
                chars[i * 2 + 1] = Digits[bytes[i] & 0xF];
            }

            return new string(chars);
        }

        private static void Feed(IncrementalHash hash, string value) =>
            hash.AppendData(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static void FeedFile(IncrementalHash hash, string path, bool contents)
        {
            // The name is fed whether or not the file exists, so a build that
            // drops a file hashes differently from one that never had it.
            Feed(hash, Path.GetFileName(path));

            if (!File.Exists(path))
            {
                Feed(hash, "-");
                return;
            }

            var info = new FileInfo(path);
            Feed(hash, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!contents || info.Length > MaxBytesPerFile) return;

            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);

            var buffer = new byte[1 << 16];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
    }
}
