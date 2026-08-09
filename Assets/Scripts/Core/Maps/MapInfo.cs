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
        public Vector2[] IronDeposits = new Vector2[0];
        public Vector2[] VeilstoneNodes = new Vector2[0];
        public Vector2[] VeilsteelNodes = new Vector2[0];
        public Vector2[] CurseNodes = new Vector2[0];
    }
}
