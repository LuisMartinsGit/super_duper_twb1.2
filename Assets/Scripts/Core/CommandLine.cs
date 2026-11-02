// CommandLine.cs
// The one reader of the process command line: is a flag present, and what
// value follows a key.

using System;

namespace TheWaningBorder.Core
{
    /// <summary>
    /// Command-line lookups. Every reader used to carry its own copy of the
    /// IndexOf / TryParse pair — the two headless harnesses (three copies
    /// between them), the lockstep bootstrap, the lockstep manager and the
    /// launcher gate. This is that pair, once.
    /// </summary>
    public static class CommandLine
    {
        /// <summary>Is <paramref name="flag"/> among <paramref name="args"/>?</summary>
        public static bool Has(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

        /// <summary>Is <paramref name="flag"/> on the process command line?</summary>
        public static bool Has(string flag) => Has(Environment.GetCommandLineArgs(), flag);

        /// <summary>The argument after <paramref name="key"/>, or null when the
        /// key is absent or last.</summary>
        public static string Str(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>The integer after <paramref name="key"/>, or
        /// <paramref name="fallback"/> when the key is absent, last or not a
        /// number.</summary>
        public static int Int(string[] args, string key, int fallback)
        {
            int i = Array.IndexOf(args, key);
            return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v))
                ? v : fallback;
        }

        /// <summary><see cref="Int(string[], string, int)"/> over the process
        /// command line.</summary>
        public static int Int(string key, int fallback)
            => Int(Environment.GetCommandLineArgs(), key, fallback);
    }
}
