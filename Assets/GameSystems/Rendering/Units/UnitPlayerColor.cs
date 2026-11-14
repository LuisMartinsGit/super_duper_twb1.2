// UnitPlayerColor.cs
// Opt-in player colour for a unit prefab whose kit is painted in the pack's
// saturated cloth colours on a shared atlas (the Ancient Empire Spearman: the
// legionary's red tunic and scutum face, the levy's blue tunic). A prefab that carries this marker gets, per faction
// colour, ONE shared copy of each of its materials whose atlas has every cloth
// texel swapped to the faction hue at the texel's own brightness — steel,
// skin, leather and the cream shield emblem are left as authored.
//
// Units without the marker keep their authored materials (the rule in
// PresentationSpawnSystem.ApplyFactionColor). The copies are shared between
// every unit of a faction, so batching is kept and nothing is made per unit.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    /// <summary>Marker: this unit's red cloth is drawn in its player's colour.</summary>
    [DisallowMultipleComponent]
    public sealed class UnitPlayerColor : MonoBehaviour
    {
        /// <summary>Authored on the prefab: on THIS material, texels of these
        /// atlas colours (within <see cref="garmentTolerance"/> a channel) are
        /// a garment drawn in the player's colour too — a swatch that is not
        /// saturated cloth, like the first-age Spearman's brown vest.</summary>
        public Material garmentMaterial;
        public Color32[] garmentColors;
        public int garmentTolerance;

        static readonly string[] AtlasProps = { "_Albedo_Map", "_BaseMap", "_MainTex" };

        // Keyed by (source, faction colour, garment or plain): the same
        // material is recoloured differently on a body that wears the garment
        // and on a prop that merely shares its atlas (the spear shaft).
        static readonly Dictionary<(Material, int, bool), Material> _materials = new();
        static readonly Dictionary<(Texture, int, bool), Texture2D> _atlases = new();

        /// <summary>Swap the red cloth of every renderer under this unit to <paramref name="color"/>.</summary>
        public void Apply(Color color)
        {
            int key = PackKey(color);
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var rend = renderers[r];
                if (rend == null || rend is ParticleSystemRenderer) continue;
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    // The garment is worn by the skinned body only.
                    bool worn = rend is SkinnedMeshRenderer && garmentMaterial != null && mats[m] == garmentMaterial;
                    var swapped = For(mats[m], color, key, worn ? this : null);
                    if (swapped != null && swapped != mats[m]) { mats[m] = swapped; changed = true; }
                }
                if (changed) rend.sharedMaterials = mats;
            }
        }

        static Material For(Material src, Color color, int key, UnitPlayerColor garment)
        {
            if (src == null) return null;
            if (_materials.TryGetValue((src, key, garment != null), out var done)) return done;

            Material copy = null;
            for (int p = 0; p < AtlasProps.Length; p++)
            {
                if (!src.HasProperty(AtlasProps[p])) continue;
                var atlas = src.GetTexture(AtlasProps[p]);
                if (atlas == null) continue;
                var recolored = Recolored(atlas, color, key, garment);
                if (recolored == null) continue;
                if (copy == null) copy = new Material(src) { name = src.name + "_PlayerColor" };
                copy.SetTexture(AtlasProps[p], recolored);
            }
            // No red on this material: remember that, and keep the original.
            _materials[(src, key, garment != null)] = copy != null ? copy : src;
            return _materials[(src, key, garment != null)];
        }

        static Texture2D Recolored(Texture atlas, Color color, int key, UnitPlayerColor garment)
        {
            if (_atlases.TryGetValue((atlas, key, garment != null), out var cached)) return cached;

            // The pack's atlases are not Read/Write enabled: read them back
            // through a render target instead of asking for their pixels.
            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, true, false);
            copy.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var px = copy.GetPixels32();
            Color32 target = color;
            float tMax = Mathf.Max(target.r, Mathf.Max(target.g, target.b), 1f);
            int matched = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (garment != null && garment.IsGarment(c, out float lift))
                {
                    // A dull swatch: take the player's colour at nearly full
                    // strength, keeping the swatch's own light and dark.
                    float gs = 0.92f * lift * 255f / tMax;
                    px[i] = new Color32(
                        (byte)Mathf.Min(255f, target.r * gs),
                        (byte)Mathf.Min(255f, target.g * gs),
                        (byte)Mathf.Min(255f, target.b * gs),
                        c.a);
                    matched++;
                    continue;
                }
                if (!IsCloth(c)) continue;
                // Keep the texel's own brightness (its shading), take the hue.
                float scale = Mathf.Max(c.r, c.b) / tMax;
                px[i] = new Color32(
                    (byte)Mathf.Min(255f, target.r * scale),
                    (byte)Mathf.Min(255f, target.g * scale),
                    (byte)Mathf.Min(255f, target.b * scale),
                    c.a);
                matched++;
            }
            if (matched == 0)
            {
                Destroy(copy);
                _atlases[(atlas, key, garment != null)] = null;
                return null;
            }
            copy.name = atlas.name + "_PlayerColor";
            copy.wrapMode = atlas.wrapMode;
            copy.filterMode = atlas.filterMode;
            copy.anisoLevel = atlas.anisoLevel;
            copy.SetPixels32(px);
            copy.Apply(true, true);
            _atlases[(atlas, key, garment != null)] = copy;
            return copy;
        }

        // The pack's cloth colours: saturated red (the soldier's tunic, the
        // scutum) and saturated blue (the unarmoured tunic). Skin, leather
        // and bronze carry far more green than the red test lets through;
        // steel is a grey-blue the blue test does not reach.
        static bool IsCloth(Color32 c)
        {
            bool red = c.r > 60 && c.r > c.g * 2 && c.r > c.b * 2;
            bool blue = c.b > 60 && c.b > c.g && c.g >= c.r && c.b * 10 > c.r * 17;
            return red || blue;
        }

        bool IsGarment(Color32 c, out float lift)
        {
            lift = 1f;
            if (garmentColors == null) return false;
            for (int g = 0; g < garmentColors.Length; g++)
            {
                var k = garmentColors[g];
                if (Mathf.Abs(c.r - k.r) > garmentTolerance || Mathf.Abs(c.g - k.g) > garmentTolerance
                    || Mathf.Abs(c.b - k.b) > garmentTolerance) continue;
                lift = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / (float)Mathf.Max(1, Mathf.Max(k.r, Mathf.Max(k.g, k.b)));
                return true;
            }
            return false;
        }

        static int PackKey(Color c)
        {
            Color32 k = c;
            return (k.r << 16) | (k.g << 8) | k.b;
        }
    }
}
