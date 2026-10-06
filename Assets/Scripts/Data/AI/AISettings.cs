// AISettings.cs
// Static runtime accessor for AISettingsSO. Lazily loads
// Resources/AISettings.asset — the single source of every AI tuning number
// and of the personality table. A missing asset is a DATA bug: it is logged
// loudly and an empty instance is returned so callers do not NRE, but the AI
// then runs on zeroes and is visibly broken (CLAUDE.md: no silent fallback).

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
            Debug.LogError($"[AISettings] Resources/{ResourceName}.asset is missing. Every AI tuning number " +
                           "and the personality table live on it; the AI runs on an empty instance until it is restored.");
            _so = ScriptableObject.CreateInstance<AISettingsSO>();
            _so.personalities = new AISettingsSO.PersonalityBlock[0];
            return _so;
        }

        /// <summary>Drop the cached reference (after editing the asset).</summary>
        public static void Reload() => _so = null;
    }
}
