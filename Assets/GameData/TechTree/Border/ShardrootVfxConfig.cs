using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="ShardrootVfx"/>. The asset is ShardrootVfx.asset,
    /// beside ShardrootVfx.cs. No field initialisers. The four prefabs are the
    /// Lana Studio loot effects, copied beside this file.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Shardroot Vfx",
                     fileName = "ShardrootVfx")]
    public sealed class ShardrootVfxConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Looping on the artifact while it lies on the ground.</summary>
        public GameObject idlePrefab;
        /// <summary>Played once where the artifact appears — unearthed from a
        /// well or fallen from a dead bearer.</summary>
        public GameObject dropPrefab;
        /// <summary>Played once on the unit that finishes attuning it.</summary>
        public GameObject pickUpPrefab;
        /// <summary>Looping on whoever carries it, so the bearer is findable.</summary>
        public GameObject carriedPrefab;
        /// <summary>Scale of the ground effects (idle, drop).</summary>
        public float groundScale;
        /// <summary>Bearer effect scale per metre of unit height.</summary>
        public float unitScalePerMetre;
        public float fadeSeconds;
        public float pollSeconds;
    }
}
