// PlacementOverlayMaterial.cs
// Shared URP transparent-surface setup for the placement ground overlays.

using UnityEngine;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Switches a URP Lit/Unlit material to the Transparent surface so colour
    /// alpha actually blends. URP materials default to Opaque and silently
    /// discard alpha — the same dance BuilderCommandPanel does for the ghost
    /// mesh, applied here to the grid and footprint line overlays.
    /// </summary>
    internal static class PlacementOverlayMaterial
    {
        public static void MakeTransparent(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);   // 0 = Alpha
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }
    }
}
