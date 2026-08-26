// Loc.cs
// The game's localization chokepoint: every user-facing string renders
// through Loc.T().

using System;
using UnityEngine;

namespace TheWaningBorder.Core.Localization
{
    /// <summary>
    /// Static localization table, keyed by the ENGLISH source string.
    ///
    /// English-as-key is deliberate: the UI was written with hundreds of
    /// literal English strings across IMGUI panels, TMP binders and authored
    /// prefab labels, and a key like "menu.options.title" would mean
    /// inventing and cross-referencing a name for every one of them. With
    /// the source string as the key, a call site changes from "Back" to
    /// Loc.T("Back") and stays readable, and a missing entry degrades to
    /// English instead of to a raw key id.
    ///
    /// The Portuguese table lives in Loc.Portuguese.cs (same partial class,
    /// kept separate so translation edits never touch logic).
    ///
    /// Strings with runtime values must pass the TEMPLATE through T and
    /// format afterwards: string.Format(Loc.T("Train {0}"), name) — never
    /// Loc.T($"Train {name}"), which can only miss the table.
    /// </summary>
    public static partial class Loc
    {
        /// <summary>PlayerPrefs key holding the active language code.</summary>
        /// <summary>Legacy PlayerPrefs key. Kept only because
        /// PlayerProfile migrates from it on the run that creates
        /// settings.json; nothing reads or writes it any more.</summary>
        public const string PrefKey = "language";

        public const string English = "en";
        public const string Portuguese = "pt";

        private static string _language;

        /// <summary>
        /// Raised after the active language changes. UI that caches rendered
        /// strings (authored TMP labels, built dropdown option lists)
        /// re-applies itself here; immediate-mode UI just picks the new
        /// strings up next frame.
        /// </summary>
        public static event Action LanguageChanged;

        /// <summary>Active language code: "en" or "pt". Persisted on set.</summary>
        public static string Language
        {
            get
            {
                if (_language == null)
                {
                    // settings.json beside the exe, not the registry — see
                    // PlayerProfile. Empty means "never chosen", which is
                    // English until the player says otherwise.
                    string saved = TheWaningBorder.Core.Config.PlayerProfile.Language;
                    _language = string.IsNullOrEmpty(saved) ? English : saved;
                }
                return _language;
            }
            set
            {
                string wanted = value == Portuguese ? Portuguese : English;
                if (_language == wanted) return;
                _language = wanted;
                TheWaningBorder.Core.Config.PlayerProfile.Language = wanted;
                try { LanguageChanged?.Invoke(); }
                catch (Exception e)
                {
                    // A broken subscriber must not stop the rest of the UI
                    // from re-localizing.
                    Debug.LogError($"[Loc] LanguageChanged subscriber threw: {e}");
                }
            }
        }

        public static bool IsPortuguese => Language == Portuguese;

        /// <summary>
        /// Translate an English source string into the active language.
        /// Unknown strings return unchanged, so untranslated corners of the
        /// UI show English rather than breaking.
        /// </summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            if (!IsPortuguese) return english;
            return Pt.TryGetValue(english, out string pt) ? pt : english;
        }
    }
}
