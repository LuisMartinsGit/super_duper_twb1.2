// DecalHelper.cs
// Creates runtime materials and textures for URP DecalProjector usage.
// Provides square borders (buildings), circle borders (units), and dot markers.
// Location: Assets/Scripts/UI/HUD/DecalHelper.cs

using UnityEngine;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Creates runtime materials and textures for URP DecalProjector usage.
    /// Uses "Shader Graphs/Decal" which is built into URP 17.
    /// </summary>
    public static class DecalHelper
    {
        private static Material _selectionDecalMat;
        private static Material _circleDecalMat;
        private static Material _dotDecalMat;
        private static Texture2D _squareBorderTex;
        private static Texture2D _circleBorderTex;
        private static Texture2D _dotTex;

        // =====================================================================
        // MATERIALS
        // =====================================================================

        /// <summary>
        /// Get or create a decal material for square selection indicators (buildings).
        /// </summary>
        public static Material GetSquareSelectionMaterial()
        {
            if (_selectionDecalMat != null) return _selectionDecalMat;

            var shader = FindDecalShader();
            if (shader == null) return null;

            _selectionDecalMat = new Material(shader);
            _selectionDecalMat.SetTexture("Base_Map", GetSquareBorderTexture());
            _selectionDecalMat.enableInstancing = true;
            return _selectionDecalMat;
        }

        /// <summary>
        /// Get or create a decal material for circular selection indicators (units).
        /// </summary>
        public static Material GetCircleSelectionMaterial()
        {
            if (_circleDecalMat != null) return _circleDecalMat;

            var shader = FindDecalShader();
            if (shader == null) return null;

            _circleDecalMat = new Material(shader);
            _circleDecalMat.SetTexture("Base_Map", GetCircleBorderTexture());
            _circleDecalMat.enableInstancing = true;
            return _circleDecalMat;
        }

        /// <summary>
        /// Get or create a decal material for destination dot markers.
        /// </summary>
        public static Material GetDotMarkerMaterial()
        {
            if (_dotDecalMat != null) return _dotDecalMat;

            var shader = FindDecalShader();
            if (shader == null) return null;

            _dotDecalMat = new Material(shader);
            _dotDecalMat.SetTexture("Base_Map", GetDotTexture());
            _dotDecalMat.enableInstancing = true;
            return _dotDecalMat;
        }

        // =====================================================================
        // TEXTURES
        // =====================================================================

        /// <summary>
        /// Create a square border texture (transparent center, solid edges).
        /// 128x128, border thickness: 6 pixels.
        /// </summary>
        public static Texture2D GetSquareBorderTexture()
        {
            if (_squareBorderTex != null) return _squareBorderTex;

            const int size = 128;
            const int border = 6;

            _squareBorderTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _squareBorderTex.filterMode = FilterMode.Bilinear;
            _squareBorderTex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isBorder = x < border || x >= size - border ||
                                    y < border || y >= size - border;
                    pixels[y * size + x] = isBorder
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }
            _squareBorderTex.SetPixels32(pixels);
            _squareBorderTex.Apply();
            return _squareBorderTex;
        }

        /// <summary>
        /// Create a circular border texture for unit selection rings.
        /// 128x128, ring shape.
        /// </summary>
        public static Texture2D GetCircleBorderTexture()
        {
            if (_circleBorderTex != null) return _circleBorderTex;

            const int size = 128;
            const float outerRadius = 0.48f;
            const float innerRadius = 0.42f;
            const float center = 0.5f;

            _circleBorderTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _circleBorderTex.filterMode = FilterMode.Bilinear;
            _circleBorderTex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float dist = Mathf.Sqrt((u - center) * (u - center) + (v - center) * (v - center));
                    bool isRing = dist >= innerRadius && dist <= outerRadius;
                    pixels[y * size + x] = isRing
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }
            _circleBorderTex.SetPixels32(pixels);
            _circleBorderTex.Apply();
            return _circleBorderTex;
        }

        /// <summary>
        /// Create a small dot texture for path destination markers.
        /// 64x64, filled circle.
        /// </summary>
        public static Texture2D GetDotTexture()
        {
            if (_dotTex != null) return _dotTex;

            const int size = 64;
            const float center = 0.5f;
            const float radius = 0.4f;

            _dotTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _dotTex.filterMode = FilterMode.Bilinear;
            _dotTex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    float dist = Mathf.Sqrt((u - center) * (u - center) + (v - center) * (v - center));
                    byte alpha = dist <= radius ? (byte)255 : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            _dotTex.SetPixels32(pixels);
            _dotTex.Apply();
            return _dotTex;
        }

        // =====================================================================
        // GRID TEXTURE (for placement overlay)
        // =====================================================================

        /// <summary>
        /// Create a tileable grid-line texture for a single cell.
        /// When tiled via material UV, creates a repeating grid pattern.
        /// 64x64, thin lines on left and bottom edges.
        /// </summary>
        public static Texture2D CreateGridTexture()
        {
            const int size = 64;
            const int lineWidth = 2;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isLine = x < lineWidth || y < lineWidth;
                    byte alpha = isLine ? (byte)180 : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // =====================================================================
        // SHADER LOOKUP
        // =====================================================================

        private static Shader FindDecalShader()
        {
            var shader = Shader.Find("Shader Graphs/Decal");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Decal");
            if (shader == null)
            {
                Debug.LogWarning("[DecalHelper] Could not find URP Decal shader. " +
                    "Ensure Decal Renderer Feature is enabled in URP settings.");
            }
            return shader;
        }
    }
}
