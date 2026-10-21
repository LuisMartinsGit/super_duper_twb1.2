using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// The authored wall art: every <see cref="WallPieceArtSO"/> — one wall
    /// part at one wall level (0 Palisade, 1 Stone, 2 Battlemented,
    /// 3 Shielded), each in its own folder beside its .blend and .fbx
    /// (docs/Design/Art_Direction.md § 13). The asset is WallModuleArt.asset,
    /// beside WallModuleArt.cs; Waning Border > Art > Bind Building Art fills
    /// the list. A part with no piece at a level draws nothing and is logged.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Rendering/WallModuleArt",
                     fileName = "WallModuleArt")]
    public sealed class WallModuleArtConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Every wall piece, any part, any level.</summary>
        public WallPieceArtSO[] pieces;

        /// <summary>
        /// A CULTURE's own wall set, used in place of the level ladder above
        /// from wall level 1 up (the palisade, level 0, stays shared). One
        /// prefab per part: a culture whose wall has no further levels (the
        /// Feraldis sturdy log wall, docs/Design/Age_1_Feraldis.md) needs
        /// exactly one look. An empty slot falls back to the level ladder.
        /// Authored by Waning Border > Art > Author Feraldis Kit Art.
        /// </summary>
        [System.Serializable]
        public sealed class CultureSet
        {
            /// <summary>Cultures.Runai / Alanthor / Feraldis.</summary>
            public byte culture;
            public GameObject curtain;
            public GameObject hub;
            public GameObject gate;
            public GameObject tower;
            public GameObject ballistaMount;
            public GameObject trebuchetMount;
        }

        /// <summary>Per-culture wall sets (see <see cref="CultureSet"/>).</summary>
        public CultureSet[] cultureSets;
    }
}
