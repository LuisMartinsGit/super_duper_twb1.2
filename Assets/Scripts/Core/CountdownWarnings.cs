// CountdownWarnings.cs
// The warning schedule shared by the two timed victories (2026-10-19,
// Territory_Claims.md § 11.4 Sanctum dominion, Curse_And_Shardroot.md § 3.1c
// Shardroot ascension): a notice when the countdown starts, then one each
// time the time left crosses a multiple of the interval (2.5 min = 150 s by
// default: 7:30, 5:00, 2:30 of a 10:00 countdown), plus the final marks the
// config lists (1:00, 0:30). The interval and the final marks live in each
// victory's own config asset; this file only holds the arithmetic.
//
// Lockstep: the input is sim time only, and the mark it writes back is a
// plain int on the victory's ECS state, so every peer and every replay posts
// the same notices at the same tick. The notices themselves are presentation
// (SimSignals) and feed nothing back.

using Unity.Mathematics;

namespace TheWaningBorder.Core
{
    /// <summary>Which side of a timed victory the local screen is on.</summary>
    public enum CountdownSide : byte
    {
        /// <summary>Spectator or replay viewer: neutral wording.</summary>
        Neutral = 0,
        /// <summary>The local player or an ally of the winner-to-be.</summary>
        Own = 1,
        /// <summary>The local player stands to lose.</summary>
        Hostile = 2,
    }

    public static class CountdownWarnings
    {
        /// <summary>
        /// True when a warning is due this tick. <paramref name="lastMark"/> is
        /// the seconds-left mark of the last warning (negative = none yet, so
        /// the start notice fires); on true it is advanced and
        /// <paramref name="mark"/> is the time to print.
        /// </summary>
        public static bool Due(float left, float interval, float[] finalMarks,
                               ref int lastMark, out int mark)
        {
            mark = 0;
            if (left <= 0f) return false;
            if (lastMark < 0)
            {
                // Up to the next 5 s: the first tick has already taken a
                // moment off, and "9:59 remaining" reads as noise.
                mark = (int)math.ceil(left / 5f) * 5;
                lastMark = mark;
                return true;
            }

            // The smallest mark already crossed (left <= m) but not yet
            // announced (m < lastMark). Smallest, so a long tick that jumps
            // two marks says the nearer one once.
            int best = int.MaxValue;
            if (interval > 0f)
            {
                int k = (int)math.ceil(left / interval);
                int m = (int)math.round(k * interval);
                if (k >= 1 && m < lastMark && left <= m) best = m;
            }
            if (finalMarks != null)
                for (int i = 0; i < finalMarks.Length; i++)
                {
                    int m = (int)math.round(finalMarks[i]);
                    if (m > 0 && m < lastMark && left <= m && m < best) best = m;
                }
            if (best == int.MaxValue) return false;
            mark = best;
            lastMark = best;
            return true;
        }

        /// <summary>"7:30" — minutes and seconds for a notice.</summary>
        public static string Short(float seconds)
        {
            int s = (int)math.max(0f, math.ceil(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>"07:30" — the HUD's fixed-width form.</summary>
        public static string Clock(float seconds)
        {
            int s = (int)math.max(0f, math.ceil(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        /// <summary>Where the local screen stands relative to
        /// <paramref name="winner"/> (the winner's allies win with it).</summary>
        public static CountdownSide SideOf(Faction winner)
        {
            if (GameSettings.IsSpectating) return CountdownSide.Neutral;
            return Alliances.AreAllied(GameSettings.LocalPlayerFaction, winner)
                ? CountdownSide.Own : CountdownSide.Hostile;
        }
    }
}
