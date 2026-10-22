// MatchScore.cs
// THE SCORE (docs/Design/Score.md, 2026-10-05): one number per faction that
// says how the match is going for it — Economy + Strategy + Military —
// computed by MatchScoreSystem every sample, read by the stats board, the
// match metrics (Metrics_Score.csv), the match summary and the Muster Rolls
// viewer. Derived only: nothing in the simulation reads it back, so it is
// never part of the lockstep checksum.
//
// What it holds:
//   * the running totals the simulation hands it as events happen — kills,
//     deaths and razed buildings per faction (DeathSystem) — and the minute
//     each faction aged up;
//   * the latest ScoreRow per faction, replaced whole on every sample.
// The weights are MatchScoreSystem.asset (MatchScoreSystemConfig).

using System.Text;

namespace TheWaningBorder.Core.Diagnostics
{
    /// <summary>One faction's score at a sample.</summary>
    public struct ScoreRow
    {
        public bool Live;
        /// <summary>Named by the elimination record: every part reads 0.</summary>
        public bool Eliminated;
        /// <summary>Score = Units + Buildings + Stockpile (docs/Design/Score.md).</summary>
        public float Score, Units, Buildings, Stockpile;
        public int Territories, Fortresses, Techs;
        /// <summary>Shown, not scored.</summary>
        public int Kills, Deaths, Razed;
        /// <summary>Kills / max(1, deaths).</summary>
        public float Kd;
    }

    public static class MatchScore
    {
        public const int Factions = 8;

        private static readonly ScoreRow[] _rows = new ScoreRow[Factions];
        private static readonly int[] _kills = new int[Factions];
        private static readonly int[] _deaths = new int[Factions];
        private static readonly int[] _razed = new int[Factions];

        /// <summary>Sim time of the last sample, for readers that want to
        /// know how fresh the rows are.</summary>
        public static float LastSampleTime { get; private set; } = -1f;

        /// <summary>A new match: every total back to zero.</summary>
        public static void ResetForMatch()
        {
            System.Array.Clear(_rows, 0, _rows.Length);
            System.Array.Clear(_kills, 0, _kills.Length);
            System.Array.Clear(_deaths, 0, _deaths.Length);
            System.Array.Clear(_razed, 0, _razed.Length);
            LastSampleTime = -1f;
        }

        // ── events from the simulation ──

        /// <summary>A unit died. The killer is credited only when the death
        /// was attributed to a hostile faction.</summary>
        public static void NoteUnitDeath(Faction victim, Faction killer, bool attributed)
        {
            int v = (int)victim, k = (int)killer;
            if (v >= 0 && v < Factions) _deaths[v]++;
            if (attributed && k != v && k >= 0 && k < Factions) _kills[k]++;
        }

        /// <summary>A building fell; the razer is credited when known.</summary>
        public static void NoteBuildingRazed(Faction victim, Faction razer, bool attributed)
        {
            int v = (int)victim, k = (int)razer;
            if (attributed && k != v && k >= 0 && k < Factions) _razed[k]++;
        }

        public static int KillsOf(Faction f) => (int)f >= 0 && (int)f < Factions ? _kills[(int)f] : 0;
        public static int DeathsOf(Faction f) => (int)f >= 0 && (int)f < Factions ? _deaths[(int)f] : 0;
        public static int RazedOf(Faction f) => (int)f >= 0 && (int)f < Factions ? _razed[(int)f] : 0;

        // ── the rows ──

        /// <summary>MatchScoreSystem publishes a sample.</summary>
        public static void Publish(Faction faction, in ScoreRow row, float sampleTime)
        {
            int f = (int)faction;
            if (f < 0 || f >= Factions) return;
            _rows[f] = row;
            LastSampleTime = sampleTime;
        }

        public static bool TryGet(Faction faction, out ScoreRow row)
        {
            int f = (int)faction;
            if (f < 0 || f >= Factions) { row = default; return false; }
            row = _rows[f];
            return row.Live;
        }

        /// <summary>The CSV header MatchMetrics writes (Metrics_Score.csv).</summary>
        public const string CsvHeader =
            "t,faction,score,units,buildings,stockpile,eliminated,territories,fortresses,techs,kills,deaths,razed,kd\n";

        /// <summary>One CSV row per live faction, for the sample at <paramref name="t"/>.</summary>
        public static void AppendCsvRows(StringBuilder sb, float t)
        {
            string ts = t.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int f = 0; f < Factions; f++)
            {
                var r = _rows[f];
                if (!r.Live) continue;
                sb.Append(ts).Append(',').Append((Faction)f).Append(',')
                  .Append(r.Score.ToString("F0", ci)).Append(',')
                  .Append(r.Units.ToString("F0", ci)).Append(',')
                  .Append(r.Buildings.ToString("F0", ci)).Append(',')
                  .Append(r.Stockpile.ToString("F0", ci)).Append(',')
                  .Append(r.Eliminated ? 1 : 0).Append(',')
                  .Append(r.Territories).Append(',').Append(r.Fortresses).Append(',')
                  .Append(r.Techs).Append(',')
                  .Append(r.Kills).Append(',').Append(r.Deaths).Append(',').Append(r.Razed).Append(',')
                  .Append(r.Kd.ToString("F2", ci)).Append('\n');
            }
        }

        /// <summary>The "Score : ..." line for Summary.txt; null when nothing
        /// was ever sampled.</summary>
        public static string SummaryLine()
        {
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            bool any = false;
            for (int f = 0; f < Factions; f++)
            {
                var r = _rows[f];
                if (!r.Live) continue;
                if (any) sb.Append(", ");
                any = true;
                sb.Append((Faction)f).Append(' ').Append(r.Score.ToString("F0", ci))
                  .Append(" (units ").Append(r.Units.ToString("F0", ci))
                  .Append(" / buildings ").Append(r.Buildings.ToString("F0", ci))
                  .Append(" / stockpile ").Append(r.Stockpile.ToString("F0", ci))
                  .Append(r.Eliminated ? ", defeated)" : ")");
            }
            return any ? "Score       : " + sb : null;
        }
    }
}
