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

        /// <summary>A resource's own picture (Resources/UI/Icons/Resoueces —
        /// the folder's spelling is the asset's): "Supplies", "Iron",
        /// "Veilstone" (drawn from Crystal.png).</summary>
        public static Texture2D Resource(string resource)
            => Load("UI/Icons/Resoueces/" + (resource == "Veilstone" ? "Crystal" : resource));

        private static Texture2D _plus, _minus;

        /// <summary>A plus sign, drawn once at runtime (the Market's and the
        /// Vault's step buttons, decision 59).</summary>
        public static Texture2D Plus() => _plus != null ? _plus : (_plus = Glyph(true));

        /// <summary>A minus sign, drawn once at runtime.</summary>
        public static Texture2D Minus() => _minus != null ? _minus : (_minus = Glyph(false));

        /// <summary>A white plus or minus with a dark rim, antialiased, on a
        /// transparent square, so it reads on any cell tint.</summary>
        private static Texture2D Glyph(bool plus)
        {
            const int N = 128;
            const float Half = 40f, Thick = 11f, Rim = 4f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = plus ? "Glyph_Plus" : "Glyph_Minus",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
                    // distance outside the rounded bar(s); <= 0 is inside
                    float d = Bar(dx, dy, Half, Thick);
                    if (plus) d = Mathf.Min(d, Bar(dy, dx, Half, Thick));
                    float fill = Mathf.Clamp01(0.5f - d);
                    float rim = Mathf.Clamp01(0.5f - (d - Rim));
                    float a = Mathf.Max(fill, rim * 0.75f);
                    byte v = (byte)Mathf.RoundToInt(255f * (a > 0f ? fill / a : 0f));
                    px[y * N + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Signed distance to a horizontal capsule of half-length
        /// <paramref name="half"/> and half-thickness <paramref name="thick"/>.</summary>
        private static float Bar(float dx, float dy, float half, float thick)
        {
            float ex = Mathf.Max(0f, dx - (half - thick));
            return Mathf.Sqrt(ex * ex + dy * dy) - thick;
        }

        private static Texture2D Load(string path)
        {
            if (_cache.TryGetValue(path, out var tex)) return tex;
            tex = Resources.Load<Texture2D>(path);
            _cache[path] = tex;
            return tex;
        }
    }
}
