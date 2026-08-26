// SectAdoptionState.cs
// Per-faction runtime state for the 12-sect adoption system. Lives on the
// faction's bank entity (same place as FactionResources / FactionPopulation
// / FactionReligionPoints).
//
// Holds, for each of the 12 sects, whether it's adopted and at what level
// each of the 4 levers stands. Adoption itself happens via a chapel building
// completing inside a Temple slot — see SectAdoption.OnChapelCompleted.
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

        /// <summary>True if this sect has been adopted (chapel built).</summary>
        public bool IsAdopted => AdoptedAtAge != 0;

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

}
