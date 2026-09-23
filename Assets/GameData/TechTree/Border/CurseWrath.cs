// CurseWrath.cs
// The Wrath — how hard the curse answers, and who it answers.
// Canon: docs/Design/Curse_And_Shardroot.md §2.10.
//
// The curse used to escalate on a wall clock: the wave tier came straight out
// of elapsed match time and every curse territory fielded armies whether or
// not a single well had ever been touched. Nothing a player did was an input,
// so there was no way to provoke the curse and no way to avoid it — three
// logged four-AI matches ended as "the curse versus players who never reached
// each other".
//
// Wrath replaces that clock. It is a small per-faction counter:
//
//   * reaching INTO the curse raises the reacher's wrath (see Provoke)
//   * wrath picks the wave tier, so escalation measures how far you reached
//   * waves march on the highest-wrath faction, so the answer lands on whoever
//     earned it rather than on their quietest neighbour
//   * wrath COOLS while a faction reaches no further, so backing off works
//
// What does NOT cool is the well itself. §2.8's Waking is untouched: a woken
// well never sleeps and keeps feeding the veil forever. Terrain consequences
// are permanent; only the ARMIES stand down. That split is what lets this
// section and §2.8 both be true at once.
//
// DETERMINISM. This is lockstep simulation state. Every mutation runs from
// in-sim code on every peer (the three ritual systems via
// CurseAwakeningHelper, and CurseTerritorySystem's damage/anchor polls), and
// the clock is CurseTerritorySystem's tick-exact _matchElapsed rather than
// wall time. Nothing here may ever be called from UI, input or editor code.

using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Per-faction provocation level, driving how hard and at whom the curse
    /// fields its waves. See the file header and design §2.10.
    /// </summary>
    public static class CurseWrath
    {
        /// <summary>Player slots only (Blue..White). Faction.Border is not a
        /// player and can never provoke itself; lobby and colour tables are
        /// length 8 for the same reason.</summary>
        public const int PlayerFactions = 8;

        private static readonly int[] _level = new int[PlayerFactions];
        private static readonly double[] _lastProvokedAt = new double[PlayerFactions];
        /// <summary>
        /// A provocation is not forgotten before it has been ANSWERED
        /// (Curse_And_Shardroot.md §2.10.3). The first wave a territory
        /// fields waits out firstWaveDelaySeconds plus up to 80 s of stagger,
        /// which is longer than wrathCoolSeconds — so a single waking cooled
        /// back to nothing before the wave it earned was ever due, and a
        /// one-well map produced one conquest and no wave. Cooling waits
        /// until a wave has marched on the faction, and counts from then.
        /// </summary>
        private static readonly bool[] _answered = new bool[PlayerFactions];

        /// <summary>SimCadence epoch this state belongs to. The static lives
        /// longer than the match, so a carried-over level would hand the next
        /// match a curse that is already angry.</summary>
        private static int _epoch = -1;

        /// <summary>Wipe every faction's wrath. Idempotent per epoch, so
        /// callers may invoke it on any tick.</summary>
        public static void ResetIfNewMatch(int epoch)
        {
            if (_epoch == epoch) return;
            _epoch = epoch;
            for (int i = 0; i < PlayerFactions; i++)
            {
                _level[i] = 0;
                _lastProvokedAt[i] = 0.0;
                _answered[i] = true;
            }
        }

        private static bool IsPlayer(Faction f) => (byte)f < PlayerFactions;

        /// <summary>
        /// Register one act of reaching into the curse. Raises the faction's
        /// wrath by one and restarts its cooling clock.
        ///
        /// The three acts that qualify are listed in §2.10. Killing curse wave
        /// units is deliberately NOT one of them — defending your own walls
        /// must never escalate, or a faction provoked by somebody else spirals
        /// to the top tier simply for surviving.
        /// </summary>
        /// <param name="cap">Highest tier the settings ladder actually
        /// defines; wrath past the last tier would field nothing.</param>
        public static void Provoke(Faction faction, double now, int cap, string reason)
        {
            if (!IsPlayer(faction)) return;
            int i = (byte)faction;
            _lastProvokedAt[i] = now;
            _answered[i] = false;
            if (_level[i] >= cap) return;
            _level[i]++;
            TWBLog.Log($"[CurseWrath] {faction} provoked the curse ({reason}) — " +
                       $"wrath {_level[i]} at {now:0}s.");
        }

        /// <summary>
        /// Decay every faction that has reached no further for
        /// <paramref name="coolSeconds"/>. One step per cooling period, so a
        /// faction that took three wells walks back down three periods rather
        /// than being forgiven all at once.
        /// </summary>
        public static void Cool(double now, float coolSeconds)
        {
            if (coolSeconds <= 0f) return;
            for (int i = 0; i < PlayerFactions; i++)
            {
                if (_level[i] <= 0) continue;
                if (!_answered[i]) continue;   // owed a wave: hold the grudge
                if (now - _lastProvokedAt[i] < coolSeconds) continue;
                _level[i]--;
                _lastProvokedAt[i] = now;
                TWBLog.Log($"[CurseWrath] {(Faction)i} has not reached in for " +
                           $"{coolSeconds:0}s — wrath falls to {_level[i]}.");
            }
        }

        /// <summary>A wave has marched on this faction: the provocation is
        /// answered, and the cooling clock starts from this moment rather
        /// than from the provocation itself.</summary>
        public static void MarkAnswered(Faction faction, double now)
        {
            if (!IsPlayer(faction)) return;
            int i = (byte)faction;
            if (_answered[i]) return;
            _answered[i] = true;
            _lastProvokedAt[i] = now;
        }

        public static int LevelOf(Faction faction)
            => IsPlayer(faction) ? _level[(byte)faction] : 0;

        /// <summary>True once anybody has reached in and not yet cooled all
        /// the way back down.</summary>
        public static bool AnyProvoked
        {
            get
            {
                for (int i = 0; i < PlayerFactions; i++)
                    if (_level[i] > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// The angriest faction, which is who the waves march on. Ties break
        /// to the lowest faction index so every lockstep peer picks the same
        /// target from the same state.
        /// </summary>
        public static bool TryHighest(out Faction faction, out int level)
        {
            faction = Faction.Blue;
            level = 0;
            for (int i = 0; i < PlayerFactions; i++)
                if (_level[i] > level) { level = _level[i]; faction = (Faction)i; }
            return level > 0;
        }
    }
}
