using UnityEngine;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Per-power landing effects for <see cref="SectPowerVfx"/>. The asset is
    /// SectPowerVfx.asset, beside SectPowerVfx.cs. A power with no entry keeps
    /// its sect's shared effect. The prefabs live in the sect's own ability
    /// folders. No field initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Sect Power Vfx",
                     fileName = "SectPowerVfx")]
    public sealed class SectPowerVfxConfig : ScriptableObject, IComponentConfig
    {
        public Landing[] landings;

        [System.Serializable]
        public sealed class Landing
        {
            /// <summary>SectConfig id, e.g. "Renewal".</summary>
            public string sectId;
            public SectActivePowerKind kind;
            public GameObject prefab;
            /// <summary>Radius, metres, the prefab is authored at.</summary>
            public float authoredRadius;
            public float minScale;
            public float maxScale;
        }
    }
}
