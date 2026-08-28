// MapInfo.cs
// Per-map metadata ScriptableObject. One asset lives in each map folder
// (Assets/GameData/Scenes/Maps/<Map>/) next to the scene, holding the
// lobby-facing facts: display name, preset player count, size tag,
// description, the top-view thumbnail, and normalized marker positions
// for the lobby's map preview.
//
// Author it by hand or bake it from the open map scene via
//   Waning Border > Maps > Bake Map Info From Open Scene
// which fills player count + marker positions from the scene's MapMarkers
// and captures a top-down thumbnail.
//
// Runtime lookup goes through MapInfoIndex (a Resources asset referencing
// every MapInfo) because assets outside Resources are only built into the
// player when something references them.

using UnityEngine;

namespace TheWaningBorder.Core.Maps
{
    [CreateAssetMenu(fileName = "MapInfo", menuName = "Waning Border/Map Info")]
    public sealed class MapInfo : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Scene name as in Build Settings (no path, no .unity).")]
        public string SceneName;
        public string DisplayName;

        [Header("Lobby facts")]
        [Range(2, 8)] public int PlayerCount = 8;
        [Tooltip("Short size tag shown next to the player count, e.g. SMALL / OPEN.")]
        public string SizeTag = "OPEN";
        [TextArea(3, 6)] public string Description;

        [Header("Preview")]
        public Texture2D Thumbnail;

        // Normalized map coordinates (0..1 across the terrain;
        // x = west -> east, y = south -> north). Baked from scene markers.
        public Vector2[] PlayerStarts = new Vector2[0];

        /// <summary>
        /// The <c>Faction</c> field of each authored start marker, parallel to
        /// <see cref="PlayerStarts"/>. Baked so the lobby's start-position
        /// picker can resolve a chosen start to a REAL marker by identity
        /// rather than by array position — array order is fragile, and the
        /// baked order and the runtime registry order disagreed before both
        /// were pointed at MapMarkerRegistry.ComparePlayerStarts.
        ///
        /// Empty on MapInfo assets baked before this field existed; callers
        /// must fall back to positional indexing when it is.
        /// docs/Design/Lobby_Setup.md
        /// </summary>
        public Faction[] PlayerStartFactions = new Faction[0];

        /// <summary>
        /// Default team for each authored start, parallel to
        /// <see cref="PlayerStarts"/>. 0 is <c>Alliances.NoTeam</c> — a
        /// free-for-all, and the value every map had before this field existed.
        ///
        /// This belongs to the START, not to the lobby slot: the skirmish lobby
        /// hands out starts RANDOMLY (AssignMissingStarts), so a slot-indexed
        /// default would have put teammates on opposite shores. Keying it to
        /// the start means the preset follows a player when they are moved to a
        /// different position on the map preview.
        ///
        /// Twin Spans is drawn as 3v3 — three warbands to a shore, two bridges
        /// between them — but shipped with every slot on NoTeam, so a stock
        /// lobby played it as a six-way free-for-all and the three players
        /// sharing a shore fought each other. Nothing was wrong with the
        /// hostility rules; the map simply never said who was on whose side.
        /// docs/Design/Teams.md, docs/Design/Lobby_Setup.md
        ///
        /// Empty on assets baked before this field existed; callers must treat
        /// a missing or short array as "no preset".
        /// </summary>
        public int[] PlayerStartTeams = new int[0];

        /// <summary>
        /// The authored team for a start, or <c>0</c> (no team) when this map
        /// has no preset or the index is out of range.
        /// </summary>
        public byte TeamForStart(int startIndex)
        {
            if (PlayerStartTeams == null) return 0;
            if (startIndex < 0 || startIndex >= PlayerStartTeams.Length) return 0;
            int team = PlayerStartTeams[startIndex];
            return team < 0 ? (byte)0 : (byte)team;
        }

        /// <summary>True when this map ships a team layout worth applying.</summary>
        public bool HasTeamPreset
        {
            get
            {
                if (PlayerStartTeams == null) return false;
                for (int i = 0; i < PlayerStartTeams.Length; i++)
                    if (PlayerStartTeams[i] != 0) return true;
                return false;
            }
        }
        public Vector2[] IronDeposits = new Vector2[0];
        public Vector2[] VeilstoneNodes = new Vector2[0];
        public Vector2[] VeilsteelNodes = new Vector2[0];
        public Vector2[] CurseNodes = new Vector2[0];

        /// <summary>Supply nodes — the ground a Gatherer's Hut must be
        /// built on (docs/Design/Regions.md §4). Baked so the lobby
        /// preview can show how much supply economy a map's territories
        /// actually hold, which is now a real difference between maps
        /// rather than a constant.</summary>
        public Vector2[] SupplyNodes = new Vector2[0];

        /// <summary>
        /// Region seed positions, normalized 0..1 like every other marker array
        /// and in the same lockstep-stable order (the index IS the region id).
        ///
        /// Baked so the LOBBY can draw the partition before any world exists —
        /// RegionMap is built from scene markers at match start, which the menu
        /// scene has none of. docs/Design/Regions.md §1.
        /// </summary>
        public Vector2[] RegionSeeds = new Vector2[0];

        /// <summary>Display names parallel to <see cref="RegionSeeds"/>.</summary>
        public string[] RegionNames = new string[0];
    }
}
