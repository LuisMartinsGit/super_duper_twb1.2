// SectAdoptionStateComponents.cs
// ECS components lifted out of SectAdoptionState.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using System;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Economy
{
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
