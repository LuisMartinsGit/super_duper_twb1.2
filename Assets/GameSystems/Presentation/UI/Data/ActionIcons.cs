// ActionIcons.cs
// Rendered pictures for action buttons (2026-11-13): a unit as it looks at
// its building's level, and a weapon for each weapon tech. Developer: "use
// prints of the units (updated by level) and of the weapons for the weapon
// upgrade button". The pictures are made in the Editor by UnitGearIconRenderer
// (Waning Border > Art > Units > Render Unit and Weapon Icons) into
// Resources/UI/Icons/Units and Resources/UI/Icons/Tech; a missing picture is
// simply no icon, and the panel falls back to its symbol glyphs.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.UI.Data
{
    public static class ActionIcons
    {
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>The unit at a building level (0 = before a culture). Falls
        /// back to lower levels, then to none.</summary>
        public static Texture2D Unit(string unitId, int level)
        {
            for (int l = level; l >= 0; l--)
            {
                var tex = Load("UI/Icons/Units/" + unitId + "_L" + l);
                if (tex != null) return tex;
            }
            return null;
        }

        public static Texture2D Tech(string techId) => Load("UI/Icons/Tech/" + techId);

        /// <summary>The level-up cell: an arrow pointing up with the level it leads to.</summary>
        public static Texture2D Upgrade(int nextLevel) => Load("UI/Icons/Actions/Upgrade_L" + nextLevel);

        private static Texture2D Load(string path)
        {
            if (_cache.TryGetValue(path, out var tex)) return tex;
            tex = Resources.Load<Texture2D>(path);
            _cache[path] = tex;
            return tex;
        }
    }
}
