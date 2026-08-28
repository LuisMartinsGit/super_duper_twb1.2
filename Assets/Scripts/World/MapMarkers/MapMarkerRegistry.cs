// MapMarkerRegistry.cs
// One-shot scan of the active scene for design-time spawn markers.
// Call Refresh() once after the Game scene loads and before the spawn
// bootstraps run (PlayerSpawnSystem, IronDeposit/VeilstoneOutcropping/BorderNode).
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
        private static readonly List<VeilstoneOutcroppingMarker> _crystal  = new();
        private static readonly List<VeilsteelDepositMarker> _veilsteel = new();
        private static readonly List<BorderNodeMarker>    _border    = new();
        private static readonly List<BlightPocketMarker>  _blight    = new();
        private static readonly List<NatureRegionMarker>  _nature    = new();
        private static readonly List<RegionSeedMarker>    _regions   = new();
        private static readonly List<SupplyNodeMarker>    _supply    = new();

        public static IReadOnlyList<PlayerStartMarker>  PlayerStarts   => _players;
        public static IReadOnlyList<IronPatchMarker>    IronPatches    => _iron;
        public static IReadOnlyList<VeilstoneOutcroppingMarker> VeilstoneOutcroppings => _crystal;
        public static IReadOnlyList<VeilsteelDepositMarker> VeilsteelDeposits => _veilsteel;
        public static IReadOnlyList<BorderNodeMarker>    BorderNodes     => _border;
        public static IReadOnlyList<BlightPocketMarker>  BlightPockets   => _blight;
        public static IReadOnlyList<NatureRegionMarker>  NatureRegions   => _nature;
        public static IReadOnlyList<RegionSeedMarker>    RegionSeeds     => _regions;
        public static IReadOnlyList<SupplyNodeMarker>    SupplyNodes     => _supply;

        public static bool HasPlayerMarkers  => _players.Count  > 0;
        public static bool HasIronMarkers    => _iron.Count     > 0;
        public static bool HasVeilstoneMarkers => _crystal.Count  > 0;
        public static bool HasVeilsteelMarkers => _veilsteel.Count > 0;
        public static bool HasBorderMarkers   => _border.Count    > 0;
        public static bool HasBlightMarkers   => _blight.Count    > 0;
        public static bool HasNatureRegions   => _nature.Count    > 0;
        public static bool HasRegionSeeds     => _regions.Count   > 0;
        public static bool HasSupplyNodes     => _supply.Count    > 0;

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
            _veilsteel.Clear();
            _border.Clear();
            _blight.Clear();
            _nature.Clear();
            _regions.Clear();
            _supply.Clear();

            _players.AddRange(Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None));
            _iron.AddRange(Object.FindObjectsByType<IronPatchMarker>(FindObjectsSortMode.None));
            _crystal.AddRange(Object.FindObjectsByType<VeilstoneOutcroppingMarker>(FindObjectsSortMode.None));
            _veilsteel.AddRange(Object.FindObjectsByType<VeilsteelDepositMarker>(FindObjectsSortMode.None));
            _border.AddRange(Object.FindObjectsByType<BorderNodeMarker>(FindObjectsSortMode.None));
            _blight.AddRange(Object.FindObjectsByType<BlightPocketMarker>(FindObjectsSortMode.None));
            _nature.AddRange(Object.FindObjectsByType<NatureRegionMarker>(FindObjectsSortMode.None));
            _regions.AddRange(Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None));
            _supply.AddRange(Object.FindObjectsByType<SupplyNodeMarker>(FindObjectsSortMode.None));

            // FindObjectsByType with SortMode.None is UNORDERED — with two
            // markers claiming the same faction (or leftover-marker
            // assignment, see PlayerSpawnSystem) the winner used to vary per
            // run, which read as "random spawn positions". Sort by faction,
            // then name, then position for a deterministic marker order.
            _players.Sort(ComparePlayerStarts);

            // EVERY other list gets the same treatment, and in multiplayer it
            // is not cosmetic: the spawn bootstraps consume these lists in
            // order while NetworkIds are handed out sequentially, so two peers
            // that enumerate markers differently pair the same IDs with
            // different deposits. Same entity count, different id-position
            // pairing, checksum fork at tick 0 (found 2026-08-16, the second
            // instant-desync of the first two-editor test day).
            _iron.Sort(CompareMarkers);
            _crystal.Sort(CompareMarkers);
            _veilsteel.Sort(CompareMarkers);
            _border.Sort(CompareMarkers);
            _blight.Sort(CompareMarkers);
            // Region seeds especially: the region INDEX is their list position,
            // so an unstable order would rename every region between peers.
            _nature.Sort(CompareMarkers);
            _regions.Sort(CompareMarkers);

            TWBLog.Log($"[MapMarkerRegistry] Refresh — players={_players.Count} " +
                      $"iron={_iron.Count} veilstone={_crystal.Count} veilsteel={_veilsteel.Count} " +
                      $"border={_border.Count} blight={_blight.Count} " +
                      $"nature={_nature.Count} regions={_regions.Count}");
        }

        /// <summary>Drop all references — call when leaving the Game scene
        /// so destroyed markers don't linger across game sessions.</summary>
        public static void Clear()
        {
            _players.Clear();
            _iron.Clear();
            _crystal.Clear();
            _veilsteel.Clear();
            _border.Clear();
            _blight.Clear();
            _nature.Clear();
            _regions.Clear();
        }

        /// <summary>
        /// THE canonical ordering of a map's player-start markers.
        ///
        /// FindObjectsByType with SortMode.None is UNORDERED — with two markers
        /// claiming the same faction (or leftover-marker assignment, see
        /// PlayerSpawnSystem) the winner used to vary per run, which read as
        /// "random spawn positions". Sort by faction, then name, then position.
        ///
        /// This is public and shared because the lobby's start-position picker
        /// depends on it: MapInfoBaker bakes MapInfo.PlayerStarts in THIS order,
        /// and PlayerSlot.StartIndex indexes into it. If the baker and the
        /// runtime registry ordered differently, picking start #2 in the lobby
        /// would spawn you at a different marker. Both must call this.
        /// docs/Design/Lobby_Setup.md
        /// </summary>
        public static int ComparePlayerStarts(PlayerStartMarker a, PlayerStartMarker b)
        {
            if (a == null || b == null) return (a == null).CompareTo(b == null);
            int byFaction = ((int)a.Faction).CompareTo((int)b.Faction);
            if (byFaction != 0) return byFaction;
            int byName = string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
            if (byName != 0) return byName;
            var pa = a.transform.position;
            var pb = b.transform.position;
            int byX = pa.x.CompareTo(pb.x);
            return byX != 0 ? byX : pa.z.CompareTo(pb.z);
        }

        /// <summary>
        /// Deterministic ordering for the non-player marker lists: name, then
        /// X, then Z, then Y. Scene names can repeat ("IronPatchMarker (3)" can
        /// be duplicated by hand), so position is the tie-breaker; two markers
        /// at the SAME name and position are genuinely interchangeable.
        /// </summary>
        private static int CompareMarkers<T>(T a, T b) where T : Component
        {
            if (a == null || b == null) return (a == null).CompareTo(b == null);
            int byName = string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
            if (byName != 0) return byName;
            var pa = a.transform.position;
            var pb = b.transform.position;
            int byX = pa.x.CompareTo(pb.x);
            if (byX != 0) return byX;
            int byZ = pa.z.CompareTo(pb.z);
            return byZ != 0 ? byZ : pa.y.CompareTo(pb.y);
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
