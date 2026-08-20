// BorderSettings.cs
// Static runtime accessor for the border ARMY settings — mirrors TechCatalog.
// Lazily loads Resources/BorderSettings.asset on first use and serves it to the
// border AI / movement systems. If the asset is missing it falls back to a
// runtime instance seeded with the shipped defaults, so the game still runs.
//
// Live-edit: Get() returns the loaded SO reference, so Inspector edits during
// Play apply on the next system tick (same "edit on the fly" behaviour as the
// TechTree SOs).
//
// Location: Assets/GameData/TechTree/Buildings/Border/BorderSettings.cs

using UnityEngine;

namespace TheWaningBorder.Data.Border
{
    public static class BorderSettings
    {
        private const string ResourceName = "BorderSettings";

        private static BorderSettingsSO _so;
        private static bool _warned;

        /// <summary>
        /// The active border settings. Never null: loads Resources/BorderSettings,
        /// or builds a defaults-seeded fallback instance the first time it can't.
        /// </summary>
        public static BorderSettingsSO Get()
        {
            if (_so != null) return _so;

            _so = UnityEngine.Resources.Load<BorderSettingsSO>(ResourceName);
            if (_so != null) return _so;

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning(
                    "[BorderSettings] No Resources/BorderSettings.asset found — using built-in " +
                    "defaults. Generate it via Waning Border ▸ Border ▸ Generate Border Settings " +
                    "to tweak army tiers / prices / economy in the Inspector.");
            }

            _so = ScriptableObject.CreateInstance<BorderSettingsSO>();
            _so.ResetToDefaults();
            return _so;
        }

        /// <summary>Drop the cached reference (e.g. after (re)generating the asset).</summary>
        public static void Reload()
        {
            _so = null;
            _warned = false;
        }
    }
}
