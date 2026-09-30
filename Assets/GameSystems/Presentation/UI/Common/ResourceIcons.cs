// ResourceIcons.cs
// The resource panel's own icons, usable INLINE in any TextMeshPro text —
// tooltip prices read "[supplies icon] 50" instead of "S 50".
//
// TMP only draws inline images out of a TMP_SpriteAsset, and the icons are
// plain Sprites on the authored ResourcePanel prefab. So GameUIManager hands
// each row's sprite over once the panel exists, a one-glyph sprite asset is
// built per resource at runtime, and TMP finds it by name through
// TMP_Text.OnSpriteAssetRequest when it meets <sprite="TWB_Iron" index=0>.
// Nothing global is touched: the project's TMP_Settings (an asset, which a
// play-mode write would dirty) is left alone.
//
// Until the panel has registered (menus, a scene with no HUD), the formatters
// fall back to the old letters.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace TheWaningBorder.UI.Common
{
    public static class ResourceIcons
    {
        public const string Supplies = "Supplies";
        public const string Iron = "Iron";
        public const string Veilstone = "Veilstone";
        public const string Veilsteel = "Veilsteel";

        private const string Prefix = "TWB_";
        private static readonly Dictionary<string, TMP_SpriteAsset> _assets =
            new Dictionary<string, TMP_SpriteAsset>();
        private static bool _hooked;

        /// <summary>Register (or replace) the icon for a resource.</summary>
        public static void Register(string resource, Sprite sprite)
        {
            if (string.IsNullOrEmpty(resource) || sprite == null || sprite.texture == null) return;
            string key = Prefix + resource;
            if (_assets.TryGetValue(key, out var existing) && existing != null
                && existing.spriteSheet == sprite.texture) return;

            _assets[key] = Build(key, sprite);
            if (!_hooked)
            {
                TMP_Text.OnSpriteAssetRequest += OnRequest;
                _hooked = true;
            }
        }

        /// <summary>The inline tag for a resource, or null when its icon
        /// was never registered.</summary>
        public static string Tag(string resource)
            => _assets.ContainsKey(Prefix + resource) ? $"<sprite=\"{Prefix}{resource}\" index=0>" : null;

        private static TMP_SpriteAsset OnRequest(int hashCode, string name)
            => name != null && _assets.TryGetValue(name, out var a) ? a : null;

        private static TMP_SpriteAsset Build(string name, Sprite sprite)
        {
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = name;
            asset.hideFlags = HideFlags.HideAndDontSave;

            // An asset with a material but no version string is "upgraded" on
            // its next lookup refresh, which clears the tables built below.
            typeof(TMP_SpriteAsset)
                .GetField("m_Version", System.Reflection.BindingFlags.Instance
                                       | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(asset, "1.1.0");

            asset.spriteSheet = sprite.texture;
            var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetTexture(ShaderUtilities.ID_MainTex, sprite.texture);
            asset.material = mat;

            // Sized by the text, not the texture: with no face info of its own
            // (pointSize 0) TMP scales a sprite to the font's ascent, so the
            // icon always matches the line it sits in.
            Rect r = sprite.textureRect;
            var metrics = new GlyphMetrics(r.width, r.height, 0f, r.height * 0.85f, r.width * 1.05f);
            var glyph = new TMP_SpriteGlyph(0, metrics,
                new GlyphRect((int)r.x, (int)r.y, (int)r.width, (int)r.height), 1f, 0);
            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, asset, glyph) { name = name, scale = 1f });
            asset.UpdateLookupTables();
            return asset;
        }
    }
}
