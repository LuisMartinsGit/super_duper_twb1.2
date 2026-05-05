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
//
// Location: Assets/Scripts/Economy/SectAdoptionState.cs

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

    /// <summary>
    /// Per-faction adoption state. Fixed 12 slots, indexed by
    /// <see cref="SectConfig.IndexOf"/>. Lives on the faction bank entity.
    /// </summary>
    public struct SectAdoptionState : IComponentData
    {
        // 12 fixed slots. We use FixedList for unmanaged-component compatibility
        // and so DOTS doesn't need to chase a managed array per faction.
        // Stored as a struct of 12 PerSectState fields wrapped in a fixed buffer.
        // Conceptually: Sects[i] = state of SectConfig.IdAt(i).
        //
        // PerSectState is 5 bytes; 12 × 5 = 60 bytes total (well under the
        // unmanaged-component budget). Inline the 12 slots directly to avoid
        // FixedList serialization quirks for buffer-of-struct.
        public PerSectState Sect00, Sect01, Sect02, Sect03;
        public PerSectState Sect04, Sect05, Sect06, Sect07;
        public PerSectState Sect08, Sect09, Sect10, Sect11;

        /// <summary>Read the state of a sect by [0..11] index.</summary>
        public PerSectState Get(int index)
        {
            return index switch
            {
                0  => Sect00, 1  => Sect01, 2  => Sect02, 3  => Sect03,
                4  => Sect04, 5  => Sect05, 6  => Sect06, 7  => Sect07,
                8  => Sect08, 9  => Sect09, 10 => Sect10, 11 => Sect11,
                _  => default,
            };
        }

        /// <summary>Write the state of a sect by [0..11] index.</summary>
        public void Set(int index, PerSectState s)
        {
            switch (index)
            {
                case 0:  Sect00 = s; break;
                case 1:  Sect01 = s; break;
                case 2:  Sect02 = s; break;
                case 3:  Sect03 = s; break;
                case 4:  Sect04 = s; break;
                case 5:  Sect05 = s; break;
                case 6:  Sect06 = s; break;
                case 7:  Sect07 = s; break;
                case 8:  Sect08 = s; break;
                case 9:  Sect09 = s; break;
                case 10: Sect10 = s; break;
                case 11: Sect11 = s; break;
            }
        }

        /// <summary>Convenience: read state by sect string id. Returns default if unknown.</summary>
        public PerSectState Get(string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            return idx < 0 ? default : Get(idx);
        }

        /// <summary>Count adopted sects (for the 6-cap check, though Temple slots enforce naturally).</summary>
        public int AdoptedCount()
        {
            int n = 0;
            if (Sect00.IsAdopted) n++;
            if (Sect01.IsAdopted) n++;
            if (Sect02.IsAdopted) n++;
            if (Sect03.IsAdopted) n++;
            if (Sect04.IsAdopted) n++;
            if (Sect05.IsAdopted) n++;
            if (Sect06.IsAdopted) n++;
            if (Sect07.IsAdopted) n++;
            if (Sect08.IsAdopted) n++;
            if (Sect09.IsAdopted) n++;
            if (Sect10.IsAdopted) n++;
            if (Sect11.IsAdopted) n++;
            return n;
        }

        /// <summary>True if any sect is adopted.</summary>
        public bool HasAnyAdopted()
        {
            return Sect00.IsAdopted || Sect01.IsAdopted || Sect02.IsAdopted || Sect03.IsAdopted
                || Sect04.IsAdopted || Sect05.IsAdopted || Sect06.IsAdopted || Sect07.IsAdopted
                || Sect08.IsAdopted || Sect09.IsAdopted || Sect10.IsAdopted || Sect11.IsAdopted;
        }
    }
}
