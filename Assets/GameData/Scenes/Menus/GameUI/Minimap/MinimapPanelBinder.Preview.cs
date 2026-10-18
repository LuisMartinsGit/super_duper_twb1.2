// MinimapPanelBinder.Preview.cs
// The minimap BAKE a saved game carries as its preview (2026-10-16): the
// elevation map with the live overlay (territory tints, blips, fog) composited
// on top — exactly what the player's minimap shows at the moment of saving,
// fog included, so a save never reveals more than its owner had seen.
// SaveGameFile stores it as preview.png; the Load Game list shows it.

using UnityEngine;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class MinimapPanelBinder
    {
        /// <summary>The minimap of the running match, or null (no HUD, menus).</summary>
        public static MinimapPanelBinder Active { get; private set; }

        private Color32[] _elevationPixels;
        private int _elevW, _elevH;

        private static readonly Color32 PreviewBackdrop = new Color32(12, 15, 30, 255);

        /// <summary>
        /// PNG of the minimap as it stands, longest side
        /// <paramref name="maxSize"/>, or null before the terrain bake is done.
        /// </summary>
        public byte[] BakePreviewPng(int maxSize = 256)
        {
            if (_elevationPixels == null || _elevW <= 0 || _elevH <= 0) return null;

            float scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(_elevW, _elevH));
            int w = Mathf.Max(8, Mathf.RoundToInt(_elevW * scale));
            int h = Mathf.Max(8, Mathf.RoundToInt(_elevH * scale));
            bool overlay = _overlayPixels != null && _ovW > 0 && _ovH > 0
                           && _overlayPixels.Length == _ovW * _ovH;

            var outPx = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                int ey = Mathf.Min(_elevH - 1, (int)(v * _elevH));
                int oy = overlay ? Mathf.Min(_ovH - 1, (int)(v * _ovH)) : 0;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    var c = Over(PreviewBackdrop, _elevationPixels[ey * _elevW + Mathf.Min(_elevW - 1, (int)(u * _elevW))]);
                    if (overlay)
                        c = Over(c, _overlayPixels[oy * _ovW + Mathf.Min(_ovW - 1, (int)(u * _ovW))]);
                    outPx[y * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(outPx);
                tex.Apply(false, false);
                return tex.EncodeToPNG();
            }
            finally
            {
                Destroy(tex);
            }
        }

        /// <summary>
        /// PNG of the minimap's TERRAIN alone (elevation, cliffs, water) at
        /// full resolution, over the preview backdrop — no territories, units
        /// or fog. Framed on the terrain's world bounds, north up: the map
        /// picture the Muster Rolls report draws its replay on (2026-10-17).
        /// Null before the terrain bake is done.
        /// </summary>
        public byte[] BakeTerrainPng()
        {
            if (_elevationPixels == null || _elevW <= 0 || _elevH <= 0) return null;
            var outPx = new Color32[_elevW * _elevH];
            for (int i = 0; i < outPx.Length; i++) outPx[i] = Over(PreviewBackdrop, _elevationPixels[i]);
            var tex = new Texture2D(_elevW, _elevH, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(outPx);
                tex.Apply(false, false);
                return tex.EncodeToPNG();
            }
            finally
            {
                Destroy(tex);
            }
        }

        /// <summary>Straight-alpha "src over dst", opaque result.</summary>
        private static Color32 Over(Color32 dst, Color32 src)
        {
            int a = src.a, ia = 255 - a;
            return new Color32(
                (byte)((src.r * a + dst.r * ia) / 255),
                (byte)((src.g * a + dst.g * ia) / 255),
                (byte)((src.b * a + dst.b * ia) / 255),
                255);
        }
    }
}
