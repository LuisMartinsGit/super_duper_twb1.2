// MapMarkerRegistry.cs
// One-shot scan of the active scene for design-time spawn markers.
// Call Refresh() once after the Game scene loads and before the spawn
// bootstraps run (PlayerSpawnSystem, IronDeposit/CrystalPatch/CrystalNode).
// Each bootstrap checks the relevant Has* flag and uses the marker list
// instead of its procedural placement when markers exist.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    public static class MapMarkerRegistry
    {
        private static readonly List<PlayerStartMarker>  _players  = new();
        private static readonly List<IronPatchMarker>    _iron     = new();
        private static readonly List<CrystalPatchMarker> _crystal  = new();
        private static readonly List<CurseNodeMarker>    _curse    = new();

        public static IReadOnlyList<PlayerStartMarker>  PlayerStarts   => _players;
        public static IReadOnlyList<IronPatchMarker>    IronPatches    => _iron;
        public static IReadOnlyList<CrystalPatchMarker> CrystalPatches => _crystal;
        public static IReadOnlyList<CurseNodeMarker>    CurseNodes     => _curse;

        public static bool HasPlayerMarkers  => _players.Count  > 0;
        public static bool HasIronMarkers    => _iron.Count     > 0;
        public static bool HasCrystalMarkers => _crystal.Count  > 0;
        public static bool HasCurseMarkers   => _curse.Count    > 0;

        /// <summary>
        /// Rescan the active scene for markers. Idempotent — safe to call
        /// multiple times. Clears stale references first so a domain reload
        /// or scene reload doesn't accumulate destroyed entries.
        /// </summary>
        public static void Refresh()
        {
            _players.Clear();
            _iron.Clear();
            _crystal.Clear();
            _curse.Clear();

            _players.AddRange(Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None));
            _iron.AddRange(Object.FindObjectsByType<IronPatchMarker>(FindObjectsSortMode.None));
            _crystal.AddRange(Object.FindObjectsByType<CrystalPatchMarker>(FindObjectsSortMode.None));
            _curse.AddRange(Object.FindObjectsByType<CurseNodeMarker>(FindObjectsSortMode.None));

            TWBLog.Log($"[MapMarkerRegistry] Refresh — players={_players.Count} " +
                      $"iron={_iron.Count} crystal={_crystal.Count} curse={_curse.Count}");
        }

        /// <summary>Drop all references — call when leaving the Game scene
        /// so destroyed markers don't linger across game sessions.</summary>
        public static void Clear()
        {
            _players.Clear();
            _iron.Clear();
            _crystal.Clear();
            _curse.Clear();
        }

        /// <summary>Find the marker for a given faction (or null if none).</summary>
        public static PlayerStartMarker FindPlayerMarker(Faction faction)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i] != null && _players[i].Faction == faction)
                    return _players[i];
            return null;
        }
    }
}
