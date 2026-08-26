// AISettings.cs
// Static runtime accessor for AISettingsSO — mirrors BorderSettings. Lazily
// loads Resources/AISettings.asset; falls back to a defaults-seeded instance
// (field initializers ARE the defaults) so the game runs without the asset.

using UnityEngine;

namespace TheWaningBorder.Data.AI
{
    public static class AISettings
    {
        private const string ResourceName = "AISettings";

        private static AISettingsSO _so;

        /// <summary>The active AI settings. Never null.</summary>
        public static AISettingsSO Get()
        {
            if (_so != null) return _so;
            _so = Resources.Load<AISettingsSO>(ResourceName);
            if (_so != null) return _so;
            _so = ScriptableObject.CreateInstance<AISettingsSO>();
            return _so;
        }

        /// <summary>Drop the cached reference (after (re)generating the asset).</summary>
        public static void Reload() => _so = null;
    }
}
