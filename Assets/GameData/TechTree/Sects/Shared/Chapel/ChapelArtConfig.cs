using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// The chapel models, per sect (docs/Design/Art_Direction.md § 13). A
    /// chapel is not a catalog building (Chapel.Create owns its stats), so
    /// its visual comes from THIS SO — the presentation layer takes no
    /// building visual from anywhere but an SO. The asset is ChapelArt.asset
    /// beside ChapelArt.cs. No field initialisers.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Chapel Art", fileName = "ChapelArt")]
    public sealed class ChapelArtConfig : ScriptableObject, IComponentConfig
    {
        [System.Serializable]
        public struct Entry
        {
            /// <summary>The sect id (SectConfig, e.g. Sect_Renewal).</summary>
            public string sectId;
            public GameObject prefab;
        }

        public List<Entry> chapels;
    }
}
