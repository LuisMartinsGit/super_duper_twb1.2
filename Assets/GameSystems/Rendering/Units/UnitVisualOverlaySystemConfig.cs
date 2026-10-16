using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Assets for <see cref="UnitVisualOverlaySystem"/>. The asset is
    /// UnitVisualOverlaySystem.asset, beside UnitVisualOverlaySystem.cs. No
    /// field initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Unit Visual Overlay System",
                     fileName = "UnitVisualOverlaySystem")]
    public sealed class UnitVisualOverlaySystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>URP Lit at its shader defaults with GPU Instancing ON
        /// (UnitOverlayInstanced.mat). The pips and shield bar are drawn with
        /// Graphics.RenderMeshInstanced, and a material created only at runtime
        /// would find its instancing shader variant stripped from the player
        /// build (Graphics settings: Instancing Variants = Strip Unused). This
        /// asset ships through the config catalog, which keeps that variant;
        /// the per-colour materials are runtime copies of it.</summary>
        public Material instancedLitBase;
    }
}
