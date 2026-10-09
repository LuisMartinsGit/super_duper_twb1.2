// MatchRecord.cs
// The in-memory record of ONE match, written by MatchRecorder and read by the
// post-game Muster Rolls screen (docs/Design/Muster_Rolls_PostGame.md).
//
// Everything here is keyed on SIM time (seconds since the match clock
// started), never wall time, so a replayed match produces the same record as
// the match it replays.
//
// Shape, by what the screen draws:
//   * the map      world bounds, the lobby thumbnail, region names/seeds, the
//                  territory partition raster and every OWNER CHANGE as an
//                  event, the resource nodes;
//   * the charts   one sample per faction every series interval
//                  (RecordSeries: banks, population, army, score...);
//   * buildings    one BuildingRec per structure: born / completed / died,
//                  footprint, and level changes as events;
//   * units        one UnitTrack per unit: born / died, class and kind flags,
//                  and its positions -- stored only when they change, in
//                  decimetres, thinned evenly when the memory cap is reached;
//   * deaths       time, victim faction, where it fell.
//
// READ-ONLY to everyone but MatchRecorder: the mutators are internal to the
// Runtime assembly, the UI (Presentation) sees only the getters.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics.MatchRecording
{
    /// <summary>The per-faction series sampled every series interval.</summary>
    public enum RecordSeries : byte
    {
        Score, Economy, Strategy, Military, KillDeath, Kills, Deaths,
        Supplies, Iron, Veilstone, Veilsteel,
        Population, PopulationMax, Army, Workers, Buildings,
        Territories, Fortresses, Techs,
        Count,
    }

    /// <summary>One stored position of a unit. X/Z in DECIMETRES.</summary>
    public struct UnitSample
    {
        public float T;
        public int X, Z;
        /// <summary>UnitTrack.StateMoving | StateFormation | StateFighting.</summary>
        public byte State;
    }

    /// <summary>One unit's life on the map.</summary>
    public sealed class UnitTrack
    {
        // Kind flags (fixed at first sight).
        public const byte KindCavalry = 1;
        public const byte KindHero = 2;
        public const byte KindCurse = 4;
        // State flags (per sample).
        public const byte StateMoving = 1;
        public const byte StateFormation = 2;
        public const byte StateFighting = 4;

        public int Faction { get; internal set; }
        /// <summary>(int)UnitTag.Class, -1 when the unit has none.</summary>
        public int Class { get; internal set; }
        public byte Kind { get; internal set; }
        public string TypeId { get; internal set; }
        public float Born { get; internal set; }
        /// <summary>+Infinity while alive / when the record ended with it alive.</summary>
        public float Died { get; internal set; } = float.PositiveInfinity;

        internal readonly List<UnitSample> Samples = new List<UnitSample>(8);
        // A "still here" sample, held back while the unit does not move and
        // written just before the next real change so that interpolation does
        // not drift a stationary unit across the gap.
        internal UnitSample Pending;
        internal bool HasPending;

        public int SampleCount => Samples.Count;
        public UnitSample SampleAt(int i) => Samples[i];

        public bool AliveAt(float t) => t >= Born && t <= Died;

        /// <summary>Position (metres) and state at <paramref name="t"/>,
        /// interpolated between stored samples; false before the first.</summary>
        public bool TryGetAt(float t, out Vector2 pos, out byte state)
        {
            pos = default; state = 0;
            int n = Samples.Count;
            if (n == 0) return false;
            int lo = 0, hi = n - 1;
            if (t < Samples[0].T) { lo = 0; }
            else
            {
                // last sample with T <= t
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (Samples[mid].T <= t) lo = mid; else hi = mid - 1;
                }
            }
            var a = Samples[lo];
            state = a.State;
            if (lo + 1 < n && t > a.T)
            {
                var b = Samples[lo + 1];
                float span = b.T - a.T;
                float k = span > 1e-4f ? Mathf.Clamp01((t - a.T) / span) : 1f;
                pos = new Vector2(Mathf.Lerp(a.X, b.X, k), Mathf.Lerp(a.Z, b.Z, k)) * 0.1f;
            }
            else pos = new Vector2(a.X, a.Z) * 0.1f;
            return true;
        }
    }

    /// <summary>One structure's life on the map.</summary>
    public sealed class BuildingRec
    {
        public int Faction { get; internal set; }
        public string TypeId { get; internal set; }
        /// <summary>Centre, metres.</summary>
        public float X { get; internal set; }
        public float Z { get; internal set; }
        /// <summary>Footprint, metres (BuildingSize), before rotation.</summary>
        public float W { get; internal set; }
        public float H { get; internal set; }
        /// <summary>Degrees about +Y, 0 = facing +Z.</summary>
        public float Yaw { get; internal set; }
        public float Born { get; internal set; }
        /// <summary>When it stopped being a construction site; +Infinity if never.</summary>
        public float Completed { get; internal set; } = float.PositiveInfinity;
        public float Died { get; internal set; } = float.PositiveInfinity;

        internal readonly List<(float t, int level)> Levels = new List<(float, int)>(2);

        public bool AliveAt(float t) => t >= Born && t <= Died;
        public bool IsSiteAt(float t) => t < Completed;

        /// <summary>The building's level at <paramref name="t"/> (0 = base).</summary>
        public int LevelAt(float t)
        {
            int lv = 0;
            for (int i = 0; i < Levels.Count; i++)
            {
                if (Levels[i].t > t) break;
                lv = Levels[i].level;
            }
            return lv;
        }
    }

    public struct RegionRec
    {
        public string Name;
        public float X, Z;
    }

    public struct NodeRec
    {
        /// <summary>"iron", "veilstone" or "supply".</summary>
        public string Kind;
        public float X, Z;
    }

    public struct DeathRec
    {
        public float T;
        public int Faction;
        public float X, Z;
    }

    public struct OwnerEvent
    {
        public float T;
        public int Territory;
        /// <summary>A faction index, TerritoryOwnership.Curse (-2) or Natural (-1).</summary>
        public int Owner;
    }

    public sealed class MatchRecord
    {
        public const int Factions = 8;
        public const int OwnerNone = -1;
        public const int OwnerCurse = -2;

        /// <summary>The record of the current (or the last finished) match.
        /// Null before the first match starts recording.</summary>
        public static MatchRecord Current { get; internal set; }

        // ── header ──
        public string MapScene { get; internal set; } = "";
        public string MapName { get; internal set; } = "";
        /// <summary>The map's lobby thumbnail (framed on the terrain bounds), or null.</summary>
        public Texture2D MapThumbnail { get; internal set; }
        public Vector2 WorldMin { get; internal set; }
        public Vector2 WorldMax { get; internal set; }
        public int LocalFaction { get; internal set; }
        public bool Observer { get; internal set; }
        public readonly Color[] FactionColor = new Color[Factions];
        public readonly bool[] FactionPresent = new bool[Factions];

        // ── outcome ──
        /// <summary>Sim time of the last thing recorded.</summary>
        public float Duration { get; internal set; }
        public bool Ended { get; internal set; }
        public string Winner { get; internal set; } = "";

        // ── map ──
        internal readonly List<RegionRec> RegionList = new List<RegionRec>();
        internal readonly List<NodeRec> NodeList = new List<NodeRec>();
        internal readonly List<OwnerEvent> OwnerEventList = new List<OwnerEvent>();
        public IReadOnlyList<RegionRec> Regions => RegionList;
        public IReadOnlyList<NodeRec> Nodes => NodeList;
        public IReadOnlyList<OwnerEvent> OwnerEvents => OwnerEventList;

        /// <summary>Partition raster (RegionMap.RegionAt at cell centres),
        /// row-major from the south-west, -1 = no territory. Null until ready.</summary>
        public int[] TerritoryCells { get; internal set; }
        public int TerritoryW { get; internal set; }
        public int TerritoryH { get; internal set; }
        public float TerritoryCell { get; internal set; }
        public float TerritoryX0 { get; internal set; }
        public float TerritoryZ0 { get; internal set; }

        // ── things ──
        internal readonly List<BuildingRec> BuildingList = new List<BuildingRec>();
        internal readonly List<UnitTrack> UnitList = new List<UnitTrack>();
        internal readonly List<DeathRec> DeathList = new List<DeathRec>();
        public IReadOnlyList<BuildingRec> Buildings => BuildingList;
        public IReadOnlyList<UnitTrack> Units => UnitList;
        public IReadOnlyList<DeathRec> Deaths => DeathList;

        /// <summary>Unit samples held across every track (the memory cap's measure).</summary>
        public int UnitSampleTotal { get; internal set; }
        /// <summary>The position period currently in force (it doubles each thinning).</summary>
        public float PositionPeriod { get; internal set; }

        // ── series ──
        internal readonly List<float> SeriesTimeList = new List<float>();
        internal readonly List<float>[] SeriesValues;
        public IReadOnlyList<float> SeriesTimes => SeriesTimeList;
        public int SeriesCount => SeriesTimeList.Count;

        public MatchRecord()
        {
            int n = (int)RecordSeries.Count * Factions;
            SeriesValues = new List<float>[n];
            for (int i = 0; i < n; i++) SeriesValues[i] = new List<float>();
        }

        public float Series(RecordSeries s, int faction, int sample)
        {
            if (faction < 0 || faction >= Factions) return 0f;
            var list = SeriesValues[(int)s * Factions + faction];
            return sample >= 0 && sample < list.Count ? list[sample] : 0f;
        }

        /// <summary>The last sampled value, 0 when nothing was sampled.</summary>
        public float Last(RecordSeries s, int faction) => Series(s, faction, SeriesCount - 1);

        /// <summary>Largest value of a series over every present faction.</summary>
        public float Max(RecordSeries s)
        {
            float m = 0f;
            for (int f = 0; f < Factions; f++)
            {
                if (!FactionPresent[f]) continue;
                var list = SeriesValues[(int)s * Factions + f];
                for (int i = 0; i < list.Count; i++) if (list[i] > m) m = list[i];
            }
            return m;
        }

        /// <summary>Number of owner events with T &lt;= t -- a cheap version
        /// stamp for "has the territory picture changed".</summary>
        public int OwnerEventsUpTo(float t)
        {
            int lo = 0, hi = OwnerEventList.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (OwnerEventList[mid].T <= t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>Owner of every territory at <paramref name="t"/>. The
        /// array must be at least <see cref="Regions"/>.Count long.</summary>
        public void FillOwnersAt(float t, int[] owners)
        {
            for (int i = 0; i < owners.Length; i++) owners[i] = OwnerNone;
            int n = OwnerEventsUpTo(t);
            for (int i = 0; i < n; i++)
            {
                var e = OwnerEventList[i];
                if (e.Territory >= 0 && e.Territory < owners.Length) owners[e.Territory] = e.Owner;
            }
        }

        /// <summary>First index into <see cref="Deaths"/> with T &gt;= t.</summary>
        public int FirstDeathFrom(float t)
        {
            int lo = 0, hi = DeathList.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (DeathList[mid].T < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>"m:ss" (or "h:mm:ss") for a sim time.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            int s = (int)seconds;
            int h = s / 3600, m = (s / 60) % 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
        }
    }
}
