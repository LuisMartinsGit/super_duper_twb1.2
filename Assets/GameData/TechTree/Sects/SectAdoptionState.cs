// SectAdoptionState.cs
// Per-faction runtime state for the 12-sect adoption system. Lives on the
// faction's bank entity (same place as FactionResources / FactionPopulation
// / FactionReligionPoints).
//
// Holds, for each of the 12 sects, whether it's adopted and at what level
// each of the 4 levers stands. Adoption is instant
// (SectAdoption.TryAdopt, run by the SectAdopt command on every peer).
//
// Phase 1 (task-063): component layout + small read API. Effect dispatchers
// (Phase 2) read this state to decide whether to apply each sect's bonuses.

using System;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Single-sect adoption record. Stored as one slot in
    /// <see cref="SectAdoptionState"/>'s fixed-12 array.
    ///
    /// Age-gating (spec §2):
    ///  - Lv II requires the SECT to have been adopted in a previous age
    ///    → check <c>AdoptedAtAge &lt; currentAge</c>.
    ///  - Lv III requires the LEVER to have been at Lv II in a previous age
    ///    → check <c>level == 2 &amp;&amp; LevelAchievedAtAge &lt; currentAge</c>.
    /// </summary>
    [Serializable]
    public struct PerSectState
    {
        /// <summary>0 = not adopted; 1/2/3/4 = adopted on Age I/II/III/IV
        /// (only Age 2+ is reachable in practice — Temple is gated).</summary>
        public byte AdoptedAtAge;

        /// <summary>
        /// The sect's POWER level (I / II / III) — what its three actives scale
        /// on. docs/Design/Sects.md section 3: this is how many Temple upgrades
        /// happened WHILE the sect was already adopted, capped at 3.
        ///
        /// It is stored rather than derived on purpose. Deriving it from the
        /// current Temple level would hand a sect adopted at a maxed Temple an
        /// instant Lv III, which is precisely the thing the rule exists to
        /// prevent: early adoption is the reward, and a late pick-up stays a
        /// level-I sect for the rest of the match.
        ///
        /// Distinct from the four lever levels below, which still track the
        /// Temple directly and drive the passive / unit / building effects.
        /// </summary>
        public byte PowerLevel;

        /// <summary>
        /// Which of the sect's three actives this faction has unlocked, one
        /// bit per slot: 1 = A (comes with adoption), 2 = B, 4 = the wildcard.
        /// B and the wildcard are bought independently, in either order
        /// (docs/Design/Religion.md §3.1, 2026-10-15) — replaces the old
        /// sequential count.
        /// </summary>
        public byte UnlockMask;

        /// <summary>1 once the sect's hero has been recruited for its RP
        /// (Religion.md §4). A hero recruited again after dying pays only its
        /// resources — revival never costs RP.</summary>
        public byte HeroRecruited;

        /// <summary>0 = not yet purchased, 1/2/3 = Lv I/II/III. Adoption grants 1 on every lever automatically.</summary>
        public byte PassiveLevel;
        public byte BuildingLevel;
        public byte UnitLevel;
        public byte ActivePowerLevel;

        /// <summary>Age at which the corresponding lever last *increased* in level.
        /// Used to enforce "Lv III requires Lv II in a previous age" — Lv III is
        /// only buyable when <c>level == 2 &amp;&amp; LevelAchievedAtAge &lt; currentAge</c>.</summary>
        public byte PassiveLevelAchievedAtAge;
        public byte BuildingLevelAchievedAtAge;
        public byte UnitLevelAchievedAtAge;
        public byte ActivePowerLevelAchievedAtAge;

        /// <summary>True if this sect has been adopted.</summary>
        public bool IsAdopted => AdoptedAtAge != 0;

        /// <summary>Points this sect has received toward its ladder, in
        /// <see cref="SectProgression.UnitsPerPoint"/>ths of a religion point
        /// (decision 63). Only ever grows; the unlocks are derived from it.</summary>
        public ushort Progress;

        /// <summary>Is active <paramref name="slot"/> (1, 2, 3 = the wildcard)
        /// unlocked? Since decision 63 nothing comes with adoption: every
        /// slot waits for its step of the ladder.</summary>
        public bool IsSlotUnlocked(int slot)
            => IsAdopted && slot >= 1 && slot <= 3 && (UnlockMask & (1 << (slot - 1))) != 0;

        /// <summary>Read the level of a specific lever (returns 0 if not adopted).</summary>
        public byte LevelOf(SectLeverKind kind)
        {
            return kind switch
            {
                SectLeverKind.Passive     => PassiveLevel,
                SectLeverKind.Building    => BuildingLevel,
                SectLeverKind.Unit        => UnitLevel,
                SectLeverKind.ActivePower => ActivePowerLevel,
                _                         => 0,
            };
        }

        /// <summary>Read the achievement-age of a specific lever (0 if never raised).</summary>
        public byte LevelAchievedAtAgeOf(SectLeverKind kind)
        {
            return kind switch
            {
                SectLeverKind.Passive     => PassiveLevelAchievedAtAge,
                SectLeverKind.Building    => BuildingLevelAchievedAtAge,
                SectLeverKind.Unit        => UnitLevelAchievedAtAge,
                SectLeverKind.ActivePower => ActivePowerLevelAchievedAtAge,
                _                         => 0,
            };
        }

        /// <summary>Set the level of a specific lever and stamp it with the current age.</summary>
        public void SetLevel(SectLeverKind kind, byte level, byte currentAge)
        {
            switch (kind)
            {
                case SectLeverKind.Passive:
                    PassiveLevel = level;
                    PassiveLevelAchievedAtAge = currentAge;
                    break;
                case SectLeverKind.Building:
                    BuildingLevel = level;
                    BuildingLevelAchievedAtAge = currentAge;
                    break;
                case SectLeverKind.Unit:
                    UnitLevel = level;
                    UnitLevelAchievedAtAge = currentAge;
                    break;
                case SectLeverKind.ActivePower:
                    ActivePowerLevel = level;
                    ActivePowerLevelAchievedAtAge = currentAge;
                    break;
            }
        }
    }

    /// <summary>
    /// RELIGION PROGRESSES BY ITSELF (decision 63, 2026-11-15;
    /// docs/Design/Religion.md §3.1). The player only adopts. Every religion
    /// point a faction EARNS is shared equally between its adopted sects that
    /// have not finished their ladder, and a sect's unlocks follow from what
    /// it has received:
    ///
    ///   step 1  the first power (level I)
    ///   step 2  the second power
    ///   step 3  every power to level II (and the sect's hero may be recruited)
    ///   step 4  the third power, the wildcard
    ///   step 5  every power to level III
    ///
    /// The step sizes are FactionReligionPoints.asset's sectStepRp. Earning
    /// drives this; SPENDING does not: the balance is still the player's to
    /// spend on sect units, heroes and the Temple's trades, and spending it
    /// never slows a sect. A refund is not an earning.
    ///
    /// Deterministic integer math: a point is 60 units, which divides evenly
    /// by any number of sects from one to six.
    /// </summary>
    public static class SectProgression
    {
        public const int UnitsPerPoint = 60;
        public const int Steps = 5;

        /// <summary>Units a sect must have received to complete <paramref name="step"/> (1..5).</summary>
        public static int Threshold(int step)
        {
            var sizes = FactionReligionPointsHelper.Cfg.sectStepRp;
            int sum = 0;
            for (int i = 0; i < step && sizes != null && i < sizes.Length; i++) sum += sizes[i];
            return sum * UnitsPerPoint;
        }

        public static int StepsDone(in PerSectState s)
        {
            if (!s.IsAdopted) return 0;
            int n = 0;
            for (int step = 1; step <= Steps; step++)
                if (s.Progress >= Threshold(step)) n++;
            return n;
        }

        public static bool IsFinished(in PerSectState s) => s.IsAdopted && StepsDone(s) >= Steps;

        /// <summary>How far the sect is through its current step, 0..1, and
        /// which step that is (1..5; 0 when the ladder is finished).</summary>
        public static float StepProgress(in PerSectState s, out int step)
        {
            int done = StepsDone(s);
            if (done >= Steps) { step = 0; return 1f; }
            step = done + 1;
            int from = Threshold(done), to = Threshold(step);
            return to > from ? (s.Progress - from) / (float)(to - from) : 0f;
        }

        /// <summary>The power level a number of completed steps gives.</summary>
        public static byte LevelAt(int stepsDone) => (byte)(stepsDone >= 5 ? 3 : stepsDone >= 3 ? 2 : 1);

        /// <summary>The unlock mask a number of completed steps gives.</summary>
        public static byte MaskAt(int stepsDone)
            => (byte)((stepsDone >= 1 ? 1 : 0) | (stepsDone >= 2 ? 2 : 0) | (stepsDone >= 4 ? 4 : 0));

        /// <summary>Share <paramref name="points"/> earned religion points
        /// between the faction's unfinished sects. Simulation state; every
        /// lockstep peer runs it from the same earning.</summary>
        public static void OnEarned(EntityManager em, Faction faction, int points)
        {
            if (points <= 0) return;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;
            if (!em.HasComponent<SectAdoptionState>(bank)) return;
            var state = em.GetComponentData<SectAdoptionState>(bank);

            int open = 0;
            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                var s = state.Get(i);
                if (s.IsAdopted && !IsFinished(s)) open++;
            }
            if (open == 0) return;

            int share = points * UnitsPerPoint / open;
            int total = Threshold(Steps);
            bool local = !GameSettings.IsSpectating && faction == GameSettings.LocalPlayerFaction;
            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                var s = state.Get(i);
                if (!s.IsAdopted || IsFinished(s)) continue;
                int before = StepsDone(s);
                s.Progress = (ushort)Math.Min(total, s.Progress + share);
                int after = StepsDone(s);
                if (after != before)
                {
                    s.UnlockMask = (byte)(s.UnlockMask | MaskAt(after));
                    byte level = LevelAt(after);
                    if (level > s.PowerLevel) s.PowerLevel = level;
                    if (local) Announce(SectConfig.IdAt(i), before, after);
                }
                state.Set(i, s);
            }
            em.SetComponentData(bank, state);
        }

        /// <summary>Tell the local player what a sect just unlocked. Presentation only.</summary>
        private static void Announce(string sectId, int before, int after)
        {
            var L = (Func<string, string>)TheWaningBorder.Core.Localization.Loc.T;
            string sect = SectInfo.ShortName(sectId);
            for (int step = before + 1; step <= after; step++)
            {
                int slot = step == 1 ? 1 : step == 2 ? 2 : step == 4 ? 3 : 0;
                string text = slot > 0
                    ? string.Format(L("{0}: {1} unlocked"), sect, L(SectLeverEffects.CanonActive(sectId, slot, LevelAt(step)).Name))
                    : string.Format(L("{0}: its powers are now level {1}"), sect, step == 3 ? "II" : "III");
                TheWaningBorder.Core.SimSignals.Notify(text);
            }
        }
    }
}
