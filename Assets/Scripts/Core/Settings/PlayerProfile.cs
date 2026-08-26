// PlayerProfile.cs
// The player's name and every persisted setting, in one JSON file.

using System;
using System.IO;
using UnityEngine;

namespace TheWaningBorder.Core.Config
{
    /// <summary>
    /// One file, next to the executable, holding who the player is and how
    /// they like the game set up.
    ///
    /// WHY A FILE AND NOT PlayerPrefs. Everything here used to live in
    /// PlayerPrefs, which on Windows means the registry
    /// (HKCU\Software\...\The Waning Border) — invisible, un-editable, not
    /// something a tester can send back with a bug report, and wiped by
    /// nothing the player can predict. The logs already sit beside the .exe
    /// for exactly that reason (see <see cref="LogPaths"/>); the settings now
    /// sit next to them.
    ///
    /// The file is CREATED WITH DEFAULTS the first time the game runs, so
    /// there is never a "no settings yet" state for callers to handle — read
    /// <see cref="PlayerName"/> and it is always a usable string.
    ///
    /// <see cref="IsFirstRun"/> is true for the session that had to create the
    /// file. That is what drives the one-time "what should we call you?"
    /// prompt; it does NOT mean the values are defaults, because an existing
    /// install's PlayerPrefs are migrated in at the same moment.
    /// </summary>
    public static class PlayerProfile
    {
        private const string FileName = "settings.json";

        [Serializable]
        private class Data
        {
            public string PlayerName = "";
            public string Language = "";          // "" = pick from the system
            public int GraphicsQuality = -1;      // -1 = leave the project default
            public int ResolutionWidth;
            public int ResolutionHeight;
            public int Fullscreen = -1;           // -1 = leave whatever the player has
            public float MasterVolume = 100f;
            public float MusicVolume = 50f;

            /// <summary>The player has been asked for a name and answered.
            /// Persisted, because "have we asked yet" has to survive the
            /// process that asked — see PlayerProfile.NameConfirmed.</summary>
            public bool NameConfirmed;
        }

        private static Data _data;
        private static string _path;

        /// <summary>
        /// True when this session created the settings file. Useful for logs;
        /// NOT the thing to gate the name prompt on. It stays true for the
        /// whole process, and every return to the main menu re-runs the scene
        /// hooks — which is exactly how that prompt ended up reappearing on
        /// every visit. Use <see cref="NameConfirmed"/>.
        /// </summary>
        public static bool IsFirstRun { get; private set; }

        /// <summary>
        /// Whether the player has actually given a name. Persisted rather than
        /// held in a static, so it survives the process, and false until they
        /// answer rather than until they are asked — a game closed on the
        /// prompt has not really been through first run, and should ask again.
        /// </summary>
        public static bool NameConfirmed => Load().NameConfirmed;

        /// <summary>Record the player's answer: name and the fact that it was
        /// given, in one write.</summary>
        public static void ConfirmName(string name)
        {
            var d = Load();
            if (!string.IsNullOrWhiteSpace(name)) d.PlayerName = Sanitize(name);
            d.NameConfirmed = true;
            Save();
        }

        /// <summary>Absolute path to the settings file, for logs and for
        /// telling a tester what to send back.</summary>
        public static string Path => _path ?? Resolve();

        // ── Values ──────────────────────────────────────────────────────

        public static string PlayerName
        {
            get => Load().PlayerName;
            set { Load().PlayerName = Sanitize(value); Save(); }
        }

        public static string Language
        {
            get => Load().Language;
            set { Load().Language = value ?? ""; Save(); }
        }

        public static int GraphicsQuality
        {
            get => Load().GraphicsQuality;
            set { Load().GraphicsQuality = value; }
        }

        public static int ResolutionWidth
        {
            get => Load().ResolutionWidth;
            set { Load().ResolutionWidth = value; }
        }

        public static int ResolutionHeight
        {
            get => Load().ResolutionHeight;
            set { Load().ResolutionHeight = value; }
        }

        /// <summary>-1 until the player has expressed a preference.</summary>
        public static int Fullscreen
        {
            get => Load().Fullscreen;
            set { Load().Fullscreen = value; }
        }

        /// <summary>0-100.</summary>
        public static float MasterVolume
        {
            get => Load().MasterVolume;
            set { Load().MasterVolume = value; }
        }

        /// <summary>0-100.</summary>
        public static float MusicVolume
        {
            get => Load().MusicVolume;
            set { Load().MusicVolume = value; }
        }

        // ── Storage ─────────────────────────────────────────────────────

        /// <summary>
        /// A name safe to put in a lobby, a log header and a delimited network
        /// field. The multiplayer protocol packs names into '|'-separated
        /// messages with ','-separated slot tuples, so those two characters
        /// would shift every field after them — sanitising at the source means
        /// no caller has to remember.
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Replace('|', ' ').Replace(',', ' ').Trim();
            return value.Length > 24 ? value.Substring(0, 24) : value;
        }

        private static Data Load()
        {
            if (_data != null) return _data;

            string path = Resolve();
            if (File.Exists(path))
            {
                try
                {
                    string raw = File.ReadAllText(path);
                    _data = JsonUtility.FromJson<Data>(raw) ?? new Data();

                    // A file written before NameConfirmed existed has no such
                    // key, and JsonUtility cannot tell absent from false. Its
                    // owner has already been through the prompt, so treat the
                    // missing key as "answered" rather than asking them again
                    // on the strength of a field they never had.
                    if (raw.IndexOf("NameConfirmed", StringComparison.Ordinal) < 0)
                        _data.NameConfirmed = true;
                }
                catch (Exception e)
                {
                    // A corrupt or hand-edited file must not stop the game
                    // booting. Start from defaults and say so; the next Save
                    // overwrites the bad copy.
                    Debug.LogWarning($"[PlayerProfile] Could not read {path} ({e.Message}). " +
                                     "Starting from defaults.");
                    _data = new Data();
                }
                if (string.IsNullOrEmpty(_data.PlayerName)) _data.PlayerName = DefaultName();
                return _data;
            }

            // First run here. Seed from PlayerPrefs so an existing install
            // keeps the video and audio settings it already had — the file is
            // new, the player is not.
            Data seeded;
            try
            {
                seeded = FromLegacyPrefs();
            }
            catch (Exception e)
            {
                // Reached from a context the engine will not serve, the usual
                // one being a MonoBehaviour field initialiser (which runs in
                // the constructor, on the serialization thread).
                //
                // Deliberately NOT cached and NOT saved. Writing defaults from
                // here would create the file without the migration and throw
                // away an existing install's settings permanently. Hand back a
                // throwaway default set instead and let the next call — from
                // Awake, OnEnable, or a RuntimeInitializeOnLoadMethod — do the
                // real thing.
                Debug.LogWarning("[PlayerProfile] Settings read too early to migrate " +
                                 $"({e.GetType().Name}); using defaults for this call and " +
                                 "deferring file creation.");
                return new Data { PlayerName = DefaultName() };
            }

            IsFirstRun = true;
            _data = seeded;
            Save();
            Debug.Log($"[PlayerProfile] Created {path} with defaults.");
            return _data;
        }

        /// <summary>
        /// The pre-file settings, read out of PlayerPrefs. Every key here is
        /// dead everywhere else — see the Legacy notes on Loc.PrefKey and
        /// MusicManager.VolumePrefKey — and this is the only thing that still
        /// reads them.
        /// </summary>
        private static Data FromLegacyPrefs() => new Data
        {
            PlayerName = DefaultName(),
            Language = PlayerPrefs.GetString("language", ""),
            GraphicsQuality = PlayerPrefs.GetInt("graphics_quality", -1),
            ResolutionWidth = PlayerPrefs.GetInt("resolution_width", 0),
            ResolutionHeight = PlayerPrefs.GetInt("resolution_height", 0),
            Fullscreen = PlayerPrefs.HasKey("fullscreen") ? PlayerPrefs.GetInt("fullscreen") : -1,
            MasterVolume = PlayerPrefs.GetFloat("master_volume", 100f),
            MusicVolume = PlayerPrefs.GetFloat("music_volume", 50f),
        };

        /// <summary>Machine name as the pre-filled suggestion — a real word the
        /// player recognises, rather than an empty box or "Player 1".</summary>
        private static string DefaultName()
        {
            try { return Sanitize(Environment.MachineName); }
            catch { return "PLAYER"; }
        }

        public static void Save()
        {
            if (_data == null) return;
            try
            {
                File.WriteAllText(Resolve(), JsonUtility.ToJson(_data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerProfile] Could not write {_path} ({e.Message}). " +
                                 "Settings will not persist this session.");
            }
        }

        /// <summary>
        /// Where the file lives. Same shape as <see cref="LogPaths"/>:
        /// Application.dataPath is &lt;Game&gt;_Data in a player and
        /// &lt;project&gt;/Assets in the editor, so one level up lands beside
        /// the .exe and at the project root respectively. Falls back to
        /// persistentDataPath when the install folder is read-only — losing
        /// the convenient location beats losing the settings.
        /// </summary>
        private static string Resolve()
        {
            if (!string.IsNullOrEmpty(_path)) return _path;

            string preferred = null;
            try
            {
                preferred = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", FileName));
                // Prove it is writable HERE rather than discovering it at the
                // first Save, which would be after the player has changed
                // something and expects it kept.
                if (!File.Exists(preferred))
                {
                    File.WriteAllText(preferred, "{}");
                    File.Delete(preferred);
                }
                _path = preferred;
                return _path;
            }
            catch
            {
                try
                {
                    _path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
                }
                catch
                {
                    // Even persistentDataPath can refuse this early. A bare
                    // filename resolves against the working directory, which
                    // is the install folder for a player — good enough to not
                    // throw, and Resolve is retried until it caches a path.
                    return FileName;
                }
                return _path;
            }
        }
    }
}
