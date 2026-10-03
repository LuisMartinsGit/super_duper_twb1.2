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
    }
}
