using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="SanctumCrystalVisual"/>. The asset is
    /// SanctumCrystalVisual.asset, beside SanctumCrystalVisual.cs. No field
    /// initialisers. The prefab is authored by
    /// <c>Waning Border &gt; Art &gt; Author Sanctum Crystal</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Sanctum Crystal Visual",
                     fileName = "SanctumCrystalVisual")]
    public sealed class SanctumCrystalVisualConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>The landmark (SanctumCrystal.prefab): plinth, stones, crystals, motes, light.</summary>
        public GameObject prefab;
        /// <summary>World scale applied to the prefab (authored in metres, so 1).</summary>
        public float scale;
        /// <summary>Spin of the floating shard above the spires, degrees per second.</summary>
        public float shardSpinDegreesPerSecond;
        /// <summary>Height of the floating shard's bob, metres.</summary>
        public float shardBobMetres;
        /// <summary>Seconds for one bob / one light breath.</summary>
        public float cycleSeconds;
        /// <summary>The point light's intensity swings this share either side of its authored value.</summary>
        public float lightPulseShare;
    }
}
