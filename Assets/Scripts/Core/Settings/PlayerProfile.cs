// PlayerProfile.cs
// The player's name and every persisted setting, in one JSON file.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheWaningBorder.Core.Config
{
    /// <summary>
    /// One file holding who the player is and how they like the game set up.
    ///
    /// IT LIVES IN THE USER'S OWN FOLDER, UserPaths (2026-11-06). It used
    /// to sit beside the .exe, and the launcher replaces that whole folder on every
    /// update, so every update reset every setting; worse, the build's own file went
    /// out in every release, which is how the build machine's name reached players.
    /// The Editor and batch-mode test runs still use a file beside the project or the
    /// .exe, so tests never touch a person's settings. The match log names the path,
    /// for a tester to send back.
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
        private const int CurrentVersion = 1;

        /// <summary>What a nameless player is called. Never the computer's name.</summary>
        private const string FallbackName = "PLAYER";

        /// <summary>The build machine's name, shipped in every settings.json until
        /// 2026-11-06: nobody chose it.</summary>
        private const string ShippedName = "LUIS-INDIE";

        /// <summary>A key the player rebound: KeyBindings' action name and its KeyCode.</summary>
        [Serializable]
        private class KeyOverride
        {
            public string Action;
            public int Key;
        }

        [Serializable]
        private class Data
        {
            public List<KeyOverride> Keys = new List<KeyOverride>();
            public string PlayerName = "";
            /// <summary>1 once a file from before 2026-11-06 has been cleaned of
            /// the machine name it may carry. See Load.</summary>
            public int Version;
            public string Language = "";          // "" = pick from the system
            public int GraphicsQuality = -1;      // -1 = leave the project default
            public int ResolutionWidth;
            public int ResolutionHeight;
            public int Fullscreen = -1;           // -1 = leave whatever the player has
            public float MasterVolume = 100f;
            public float MusicVolume = 50f;
            /// <summary>Simulation rate. 1.0 by default (2026-10-16): game
            /// time is real time, and single-player runs at the same pace as
            /// multiplayer (which is always 1.0). It was 0.75, which ran every
            /// skirmish a third slower than the clock on the wall.</summary>
            public float GameSpeed = 1f;

            /// <summary>Whose floating health bars are drawn unprompted.
            /// Stored as the HealthBarMode ordinal; Smart (3) by default.</summary>
            public int HealthBars = 3;

            /// <summary>What a mixed drag-rectangle keeps. DragSelectionPriority
            /// ordinal; Military (1) by default, which is what the old
            /// SmartMilitaryDrag bool did when it was on.</summary>
            public int DragPriority = 1;

            /// <summary>Pencil ink outlines: -1 = never chosen (follow
            /// PencilOutline.asset), 0 = off, 1 = on.</summary>
            public int InkOutlines = -1;

            /// <summary>In-match HUD size multiplier (Settings > Gameplay >
            /// HUD Scale, 2026-10-24). 1.1 by default: a little larger than
            /// the authored 3840x2160 frame, which read small.</summary>
            public float HudScale = 1.1f;

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
            get { var n = Load().PlayerName; return string.IsNullOrWhiteSpace(n) ? FallbackName : n; }
            set { Load().PlayerName = Sanitize(value); Save(); }
        }

        /// <summary>The player's key for a command, if they rebound it.</summary>
        public static bool TryGetKey(string action, out int key)
        {
            key = 0;
            var keys = Load().Keys;
            if (keys == null) return false;
            foreach (var k in keys)
                if (k.Action == action) { key = k.Key; return true; }
            return false;
        }

        public static void SetKey(string action, int key)
        {
            var d = Load();
            if (d.Keys == null) d.Keys = new List<KeyOverride>();
            var hit = d.Keys.Find(k => k.Action == action);
            if (hit != null) hit.Key = key;
            else d.Keys.Add(new KeyOverride { Action = action, Key = key });
            Save();
        }

        public static void ClearKeys()
        {
            Load().Keys = new List<KeyOverride>();
            Save();
        }

        /// <summary>The name exactly as given, empty if none: what the prompt pre-fills.</summary>
        public static string ChosenName => Load().PlayerName ?? "";

        public static string Language
        {
            get => Load().Language;
            set { Load().Language = value ?? ""; Save(); }
        }

        public static int HealthBars
        {
            get => Load().HealthBars;
            set { Load().HealthBars = value; Save(); }
        }

        public static int DragPriority
        {
            get => Load().DragPriority;
            set { Load().DragPriority = value; Save(); }
        }

        /// <summary>-1 until the player has expressed a preference; then 0 / 1.</summary>
        public static int InkOutlines
        {
            get => Load().InkOutlines;
            set { Load().InkOutlines = value; Save(); }
        }

        /// <summary>HUD size multiplier, clamped to the Settings range.</summary>
        public static float HudScale
        {
            get => UnityEngine.Mathf.Clamp(Load().HudScale <= 0f ? 1.1f : Load().HudScale, 0.5f, 2f);
            set { Load().HudScale = UnityEngine.Mathf.Clamp(value, 0.5f, 2f); Save(); }
        }

        public static int GraphicsQuality
        {
            get => Load().GraphicsQuality;
            set { Load().GraphicsQuality = value; Save(); }
        }

        public static int ResolutionWidth
        {
            get => Load().ResolutionWidth;
            set { Load().ResolutionWidth = value; Save(); }
        }

        public static int ResolutionHeight
        {
            get => Load().ResolutionHeight;
            set { Load().ResolutionHeight = value; Save(); }
        }

        /// <summary>-1 until the player has expressed a preference.</summary>
        public static int Fullscreen
        {
            get => Load().Fullscreen;
            set { Load().Fullscreen = value; Save(); }
        }

        /// <summary>0-100.</summary>
        public static float MasterVolume
        {
            get => Load().MasterVolume;
            set { Load().MasterVolume = value; Save(); }
        }

        /// <summary>0-100.</summary>
        public static float MusicVolume
        {
            get => Load().MusicVolume;
            set { Load().MusicVolume = value; Save(); }
        }

        /// <summary>
        /// How fast the whole simulation runs. 1.0 is real time; 0.75 is the
        /// default because that is the pace the game reads best at.
        ///
        /// This is the honest version of "slow the game down". Cutting unit
        /// MOVE SPEED by 20% instead — which is what this replaced — left
        /// combat, training, construction and income running at full rate while
        /// units walked at 80%, so movement was slow RELATIVE to everything
        /// else and read as sluggish. Scaling time keeps every system in
        /// proportion, and it is one number rather than a value on all 57
        /// unit assets.
        /// </summary>
        public static float GameSpeed
        {
            get => Load().GameSpeed;
            set { Load().GameSpeed = UnityEngine.Mathf.Clamp(value, 0.25f, 2f); Save(); }
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
            // A player's file from before the move: the one beside the .exe, once.
            string source = File.Exists(path) ? path : OldPathIfAny(path);
            if (source != null)
            {
                try
                {
                    string raw = File.ReadAllText(source);
                    _data = JsonUtility.FromJson<Data>(raw) ?? new Data();

                    // A file written before NameConfirmed existed has no such
                    // key, and JsonUtility cannot tell absent from false. Its
                    // owner has already been through the prompt, so treat the
                    // missing key as "answered" rather than asking them again
                    // on the strength of a field they never had.
                    if (raw.IndexOf("NameConfirmed", StringComparison.Ordinal) < 0)
                        _data.NameConfirmed = true;

                    // The old 0.75 default was saved into every settings file,
                    // and no menu ever let a player choose a speed — so a
                    // stored 0.75 is that default, not a choice. Move it to
                    // the new default (2026-10-16).
                    if (Mathf.Approximately(_data.GameSpeed, 0.75f))
                        _data.GameSpeed = 1f;
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
                // Nobody chose the build machine's name or their own computer's:
                // ask once more, and never again after this.
                if (_data.Version < CurrentVersion)
                {
                    _data.Version = CurrentVersion;
                    if (IsMachineName(_data.PlayerName))
                    {
                        _data.PlayerName = "";
                        _data.NameConfirmed = false;
                    }
                    Save();
                }
                Debug.Log($"[PlayerProfile] Settings at {path}");
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
                return new Data();
            }

            IsFirstRun = true;
            seeded.Version = CurrentVersion;
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
            Language = PlayerPrefs.GetString("language", ""),
            GraphicsQuality = PlayerPrefs.GetInt("graphics_quality", -1),
            ResolutionWidth = PlayerPrefs.GetInt("resolution_width", 0),
            ResolutionHeight = PlayerPrefs.GetInt("resolution_height", 0),
            Fullscreen = PlayerPrefs.HasKey("fullscreen") ? PlayerPrefs.GetInt("fullscreen") : -1,
            MasterVolume = PlayerPrefs.GetFloat("master_volume", 100f),
            MusicVolume = PlayerPrefs.GetFloat("music_volume", 50f),
        };

        private static bool IsMachineName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (string.Equals(name, ShippedName, StringComparison.OrdinalIgnoreCase)) return true;
            try { return string.Equals(name, Sanitize(Environment.MachineName), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string BesideExe()
            => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", FileName));

        private static string OldPathIfAny(string current)
        {
            try
            {
                string old = BesideExe();
                return old != current && File.Exists(old) ? old : null;
            }
            catch { return null; }
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
        /// Where the file lives: the user's own folder, which an update never
        /// touches. The Editor and batch-mode test runs keep a file beside the
        /// project or the .exe, so a test never reads or writes a person's settings;
        /// the release leaves that file out.
        /// </summary>
        private static string Resolve()
        {
            if (!string.IsNullOrEmpty(_path)) return _path;
            try
            {
                string path = UserPaths.In(FileName) ?? BesideExe();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                _path = path;
                return _path;
            }
            catch
            {
                // Reached too early for the engine to answer. A bare filename is
                // good enough not to throw, and this is retried until a path caches.
                return FileName;
            }
        }
    }
}
