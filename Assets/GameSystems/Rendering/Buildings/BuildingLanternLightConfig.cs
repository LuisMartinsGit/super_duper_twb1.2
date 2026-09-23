using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="BuildingLanternLight"/>. The asset is
    /// BuildingLanternLight.asset, beside BuildingLanternLight.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Building Lantern Light",
                     fileName = "BuildingLanternLight")]
    public sealed class BuildingLanternLightConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>A building earns a lantern when its SHORTER footprint side
        /// is at least this many metres. 8 = 4x4 cells: Hall, Range, Barracks,
        /// Temple and the culture halls; huts and towers (4 m) get none.</summary>
        public int minFootprintMetres;

        /// <summary>Warm lantern colour (Art_Direction.md §4.4).</summary>
        public Color color;
        public float intensity;
        /// <summary>Metres.</summary>
        public float range;
        /// <summary>Height of the light above the building's pivot, metres.</summary>
        public float height;
        /// <summary>How often the construction gate re-checks the entity.</summary>
        public float constructionPollSeconds;
    }
}
