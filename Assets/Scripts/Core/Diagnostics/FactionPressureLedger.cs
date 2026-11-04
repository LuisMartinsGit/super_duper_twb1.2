// FactionPressureLedger.cs
// WHO IS HURTING WHOM (2026-11-03, docs/Design/Game_AI.md § 6x).
//
// A rolling record of attributed kills and razes between factions, kept for
// the last MaxWindowSeconds of simulation time. Two readers:
//   * the AI's "losing player" rule — a faction under heavier pressure than
//     it deals attacks whoever is attacking it and fortifies;
//   * the "threat" tier — a faction that has ELIMINATED someone (the top
//     damager of the dead faction over the ten minutes before its death)
//     is a danger to everyone at the table.
// Fed from MatchScore.NoteUnitDeath / NoteBuildingRazed (the same attribution
// the score uses: LastDamagedByFaction) and from EliminationSystem when a
// faction falls. Simulation-time only, no wall clock, no randomness.
using System.Collections.Generic;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class FactionPressureLedger
    {
        private const int Factions = 8;
        public const float MaxWindowSeconds = 600f;

        private struct Hit { public float T; public byte A, V; public float W; }
        private static readonly List<Hit> _hits = new List<Hit>(4096);
        private static readonly int[] _eliminatedBy = new int[Factions];
        private static readonly int[] _eliminations = new int[Factions];
        private static float _pruneAt;

        public static void ResetForMatch()
        {
            _hits.Clear();
            for (int i = 0; i < Factions; i++) { _eliminatedBy[i] = -1; _eliminations[i] = 0; }
            _pruneAt = 0f;
        }

        /// <summary>One attributed hit: a unit kill weighs 1, a razed building 4.</summary>
        public static void Note(Faction attacker, Faction victim, float weight, float now)
        {
            int a = (int)attacker, v = (int)victim;
            if (a < 0 || a >= Factions || v < 0 || v >= Factions || a == v || weight <= 0f) return;
            _hits.Add(new Hit { T = now, A = (byte)a, V = (byte)v, W = weight });
            if (now >= _pruneAt)
            {
                _pruneAt = now + 60f;
                int keep = 0;
                for (int i = 0; i < _hits.Count; i++)
                    if (now - _hits[i].T <= MaxWindowSeconds) _hits[keep++] = _hits[i];
                _hits.RemoveRange(keep, _hits.Count - keep);
            }
        }

        /// <summary>Weight <paramref name="attacker"/> dealt <paramref name="victim"/> in the last <paramref name="window"/> s.</summary>
        public static float PressureOn(Faction victim, Faction attacker, float window, float now)
        {
            int a = (int)attacker, v = (int)victim;
            float sum = 0f;
            for (int i = 0; i < _hits.Count; i++)
            {
                var h = _hits[i];
                if (h.V == v && h.A == a && now - h.T <= window) sum += h.W;
            }
            return sum;
        }

        /// <summary>Who hurt <paramref name="victim"/> most in the last <paramref name="window"/> s (Faction.Border when nobody).</summary>
        public static Faction TopAttacker(Faction victim, float window, float now, out float weight)
        {
            int v = (int)victim;
            var sums = new float[Factions];
            for (int i = 0; i < _hits.Count; i++)
            {
                var h = _hits[i];
                if (h.V == v && now - h.T <= window) sums[h.A] += h.W;
            }
            int best = -1; weight = 0f;
            for (int a = 0; a < Factions; a++)
                if (sums[a] > weight) { weight = sums[a]; best = a; }
            return best < 0 ? Faction.Border : (Faction)best;
        }

        /// <summary>Called once when a faction is eliminated: credits the top
        /// damager of the ten minutes before its death with the kill.</summary>
        public static void NoteEliminated(Faction victim, float now)
        {
            int v = (int)victim;
            if (v < 0 || v >= Factions) return;
            var a = TopAttacker(victim, MaxWindowSeconds, now, out float w);
            _eliminatedBy[v] = w > 0f ? (int)a : -1;
            if (w > 0f && (int)a >= 0 && (int)a < Factions) _eliminations[(int)a]++;
        }

        public static int EliminationsBy(Faction f)
            => (int)f >= 0 && (int)f < Factions ? _eliminations[(int)f] : 0;

        public static Faction EliminatedBy(Faction victim)
            => (int)victim >= 0 && (int)victim < Factions && _eliminatedBy[(int)victim] >= 0
                ? (Faction)_eliminatedBy[(int)victim] : Faction.Border;
    }
}
