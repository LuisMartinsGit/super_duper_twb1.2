using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// The authored wall art PER WALL LEVEL (docs/Design/Age_1_Alanthor.md §
    /// The four wall levels): index 0 Palisade, 1 Stone, 2 Battlemented,
    /// 3 Shielded. The asset is WallModuleArt.asset, beside WallModuleArt.cs.
    ///
    /// An empty slot falls back to the part's own SO prefab (the timber set) or,
    /// with none, to the procedural piece — so a level is authored simply by
    /// filling its slot. Built by Waning Border > Art > Author Castle Prefabs.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Rendering/WallModuleArt",
                     fileName = "WallModuleArt")]
    public sealed class WallModuleArtConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>The curtain module (3 m of wall), per level.</summary>
        public GameObject[] curtain;
        /// <summary>The hub (the round node a wall is drawn between), per level.</summary>
        public GameObject[] hub;
        /// <summary>The gatehouse (three modules wide), per level.</summary>
        public GameObject[] gate;
        /// <summary>The wall tower (a curtain module converted to a turret), per level.</summary>
        public GameObject[] tower;
        /// <summary>The wall-mounted ballista bastion, per level. Carries an
        /// empty child named "Deck" at the height the engine stands on.</summary>
        public GameObject[] ballistaMount;
        /// <summary>The wall-mounted trebuchet bastion, per level ("Deck" as above).</summary>
        public GameObject[] trebuchetMount;

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
