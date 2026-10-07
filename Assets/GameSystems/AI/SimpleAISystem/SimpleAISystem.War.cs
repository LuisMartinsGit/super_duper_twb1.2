// SimpleAISystem.War.cs
// ONE WAR AT A TIME (2026-10-07, docs/Design/Game_AI.md § 6i).
//
// Developer report: "Blue (southwest) attacks Red to the east and defeats its
// forces, then abandons the fight and marches diagonally to attack another
// faction. If it kept pressing it would have defeated Red." Two things did
// it: the wave doctrine re-picked its victim on every wave (the WEAKEST in
// the closeout, the leader otherwise), so a beaten army's own victory made
// another faction "weakest" by comparison; and the opportunity hijack, the
// fall-back retarget and the raze chain took any hostile's building.
//
// Now a faction that picks a victim keeps it: every wave, opportunity strike,
// retarget and chain stays on that victim until it is eliminated (no capital
// left) or warMaxFailures attacks on it in a row have failed (a mission timed
// out or an army gave its objective up). A razed objective resets the count.

using System.Collections.Generic;
using Unity.Entities;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private sealed class War
        {
            public Faction Victim;
            public float Since;
            public int Failures;
        }

        private readonly Dictionary<int, War> _wars = new Dictionary<int, War>();
        private int _warEpoch = int.MinValue;

        private void EnsureWarEpoch()
        {
            if (_warEpoch == SimCadence.Epoch) return;
            _warEpoch = SimCadence.Epoch;
            _wars.Clear();
        }

        /// <summary>The faction's current war victim, if it has one that is
        /// still in the game and has not beaten off too many attacks.</summary>
        private bool TryGetWarVictim(EntityManager em, Faction faction, out Faction victim)
        {
            EnsureWarEpoch();
            victim = faction;
            if (!_wars.TryGetValue((int)faction, out var w)) return false;
            if (BoardScore(em, w.Victim) < 0 || !Alliances.AreHostile(faction, w.Victim))
            {
                AILogger.Log(faction, "WAR", $"war on {w.Victim} over — {w.Victim} is out of the game");
                _wars.Remove((int)faction);
                return false;
            }
            if (w.Failures >= System.Math.Max(1, Cfg.warMaxFailures))
            {
                AILogger.Log(faction, "WAR",
                    $"war on {w.Victim} abandoned after {w.Failures} failed attack(s)");
                _wars.Remove((int)faction);
                return false;
            }
            victim = w.Victim;
            return true;
        }

        /// <summary>The wave's victim: the war's, if one is on; otherwise the
        /// doctrine's pick, which starts a war.</summary>
        private Faction CommitWarVictim(EntityManager em, Faction faction, Faction doctrinePick, float now)
        {
            // PILE-ON (§ 6i, 2026-10-07): a faction that is LOSING a war to
            // someone else draws this faction in too — two-on-one is what
            // ends a match; one-on-one wars between equal armies deadlocked.
            if (TryPileOnVictim(em, faction, out var losing, out var winner))
            {
                bool already = TryGetWarVictim(em, faction, out var cur) && cur == losing;
                if (!already)
                {
                    _wars[(int)faction] = new War { Victim = losing, Since = now };
                    AILogger.Log(faction, "WAR",
                        $"joins {winner}'s war on {losing} — {losing} is losing it (pile-on)");
                }
                return losing;
            }
            if (TryGetWarVictim(em, faction, out var held)) return held;
            if (doctrinePick == faction) return faction;
            _wars[(int)faction] = new War { Victim = doctrinePick, Since = now };
            AILogger.Log(faction, "WAR", $"war on {doctrinePick} — every wave stays on it until it falls");
            return doctrinePick;
        }

        /// <summary>
        /// A hostile faction another faction is at war with and beating: its
        /// board score at most pileOnLosingShare of its attacker's. The weakest
        /// such victim. Never this faction itself; nothing in a one-on-one
        /// (the only war there is this faction's own).
        /// </summary>
        private bool TryPileOnVictim(EntityManager em, Faction faction, out Faction victim, out Faction winner)
        {
            EnsureWarEpoch();
            victim = faction; winner = faction;
            if (Cfg.pileOnLosingShare <= 0f) return false;
            int best = int.MaxValue;
            foreach (var kv in _wars)
            {
                var attacker = (Faction)kv.Key;
                var v = kv.Value.Victim;
                if (attacker == faction || v == faction) continue;
                if (!Alliances.AreHostile(faction, v)) continue;
                int vs = BoardScore(em, v), ascore = BoardScore(em, attacker);
                if (vs < 0 || ascore <= 0) continue;
                if (vs > Cfg.pileOnLosingShare * ascore) continue;
                if (vs >= best) continue;
                best = vs; victim = v; winner = attacker;
            }
            return best < int.MaxValue;
        }

        /// <summary>An attack on the war victim failed (timed out, given up).</summary>
        private void NoteWarFailure(Faction faction, Faction against)
        {
            EnsureWarEpoch();
            if (_wars.TryGetValue((int)faction, out var w) && (against == faction || against == w.Victim))
                w.Failures++;
        }

        /// <summary>An objective of the war victim fell: the war is going well.</summary>
        private void NoteWarProgress(Faction faction)
        {
            EnsureWarEpoch();
            if (_wars.TryGetValue((int)faction, out var w)) w.Failures = 0;
        }

        /// <summary>May an army of <paramref name="faction"/> strike a building
        /// of <paramref name="owner"/>? Only its war victim's while a war is on.</summary>
        private bool WarAllows(EntityManager em, Faction faction, Faction owner)
            => !TryGetWarVictim(em, faction, out var v) || owner == v;
    }
}
