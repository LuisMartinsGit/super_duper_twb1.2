// TutorialMissionState.cs
// What the running tutorial mission publishes: progress for the objectives
// panel, and which build, train and research buttons the UI may offer. The
// simulation writes it (TutorialMissionSystem); the UI only reads it, the same
// direction PresentationState works in. docs/Design/Tutorial_Missions.md

using System.Collections.Generic;

namespace TheWaningBorder.Tutorial
{
    public enum TutorialStage : byte
    {
        Rescue,   // the Litharch, the camera, orders; no economy on screen
        Camp,     // walk to the clearing, then build the Shelter
        Settle,   // Huts, Gatherer's Huts, mines, a Barracks
        Muster,   // Scouts, the lost Spearmen, training, research
    }

    /// <summary>The Alanthor mission's stages, in order.</summary>
    public enum AdvancedStage : byte
    {
        Vault, Economy, Military, Defense, Rival, Points, Religion, Sanctum, Ruins, Victory,
    }

    public static class TutorialMissionState
    {
        public static AdvancedStage AdvancedStage;
        public static bool ConquestOpen, SurveyDone, SectAdopted, NodeDead, MonumentHeld, MonumentExplored;
        /// <summary>Step 2b (decision 55, 2026-11-11): the faction's Mine level
        /// (replaces the retired Trade Charters step).</summary>
        public static int MineLevel, Ranges, Towers, Temples, Claimed, ReligionPoints;
        /// <summary>The Alanthor mission's enshrinement and Sanctum dominion countdown.</summary>
        public static float WinCountdownSeconds;

        public static TutorialStage Stage;
        public static bool CampFound;
        public static bool CurseOpen;
        public static bool Over;
        public static bool Won;

        // Latched by the system: a row never un-ticks.
        public static int Workers, Shelters, Huts, GatherersHuts, VeilstoneMines, IronMines, Barracks;
        public static int ScoutsTrained, SpearmenFound, SpearmenTrained;
        public static int WavesSent;
        public static float SecondsToNextWave;

        // Coach observations.
        public static bool LitharchMoved, ChainedOrders;

        private static readonly HashSet<string> _buildings = new HashSet<string>();
        private static readonly HashSet<string> _units = new HashSet<string>();
        private static readonly HashSet<string> _research = new HashSet<string>();

        public static void Reset()
        {
            Stage = TutorialStage.Rescue;
            CampFound = CurseOpen = Over = Won = false;
            Workers = Shelters = Huts = GatherersHuts = VeilstoneMines = IronMines = Barracks = 0;
            ScoutsTrained = SpearmenFound = SpearmenTrained = WavesSent = 0;
            SecondsToNextWave = 0f;
            LitharchMoved = ChainedOrders = false;
            AdvancedStage = AdvancedStage.Vault;
            ConquestOpen = SurveyDone = SectAdopted = NodeDead = MonumentHeld = MonumentExplored = false;
            MineLevel = Ranges = Towers = Temples = Claimed = ReligionPoints = 0;
            WinCountdownSeconds = 0f;
            _deniedBuildings.Clear();
            _deniedUnits.Clear();
            _buildings.Clear();
            _units.Clear();
            _research.Clear();
        }

        internal static void Offer(string[] buildings, string[] units, string[] research)
        {
            _buildings.Clear();
            _units.Clear();
            _research.Clear();
            if (buildings != null) _buildings.UnionWith(buildings);
            if (units != null) _units.UnionWith(units);
            if (research != null) _research.UnionWith(research);
        }

        private static readonly HashSet<string> _deniedBuildings = new HashSet<string>();
        private static readonly HashSet<string> _deniedUnits = new HashSet<string>();

        internal static void DenyBuildings(string[] ids)
        {
            _deniedBuildings.Clear();
            if (ids != null) _deniedBuildings.UnionWith(ids);
        }

        internal static void DenyUnits(string[] ids)
        {
            _deniedUnits.Clear();
            if (ids != null) _deniedUnits.UnionWith(ids);
        }

        /// <summary>The Basic mission offers a list; the Alanthor mission offers
        /// everything but a few; outside a mission everything is allowed.</summary>
        public static bool AllowsBuilding(string id)
            => GameSettings.TutorialBasicActive ? _buildings.Contains(id)
             : !GameSettings.TutorialAdvancedActive || !_deniedBuildings.Contains(id);
        public static bool AllowsUnit(string id)
            => GameSettings.TutorialBasicActive ? _units.Contains(id)
             : !GameSettings.TutorialAdvancedActive || !_deniedUnits.Contains(id);
        public static bool AllowsResearch(string id)
            => !GameSettings.TutorialBasicActive || _research.Contains(id);

        /// <summary>A win countdown, shortened during the Alanthor mission.</summary>
        public static float CountdownOr(float matchSeconds)
            => GameSettings.TutorialAdvancedActive && WinCountdownSeconds > 0f ? WinCountdownSeconds : matchSeconds;
    }
}
