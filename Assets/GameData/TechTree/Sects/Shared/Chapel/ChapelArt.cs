// ChapelArt.cs
// A sect chapel's model, from ChapelArt.asset (ChapelArtConfig) — the SO a
// chapel's visual comes from (docs/Design/Art_Direction.md § 13). Replaces the
// presentation-id -> Resources path table PresentationSpawnSystem kept.

using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    public static class ChapelArt
    {
        static ChapelArtConfig _cfg;
        static ChapelArtConfig Cfg => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ChapelArtConfig>());

        /// <summary>The chapel model of <paramref name="sectId"/>, or null (a data bug).</summary>
        public static GameObject For(string sectId)
        {
            var c = Cfg;
            if (c == null || c.chapels == null || string.IsNullOrEmpty(sectId)) return null;
            foreach (var e in c.chapels)
                if (e.sectId == sectId) return e.prefab;
            return null;
        }
    }
}
