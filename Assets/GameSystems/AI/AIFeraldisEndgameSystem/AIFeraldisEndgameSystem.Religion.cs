// AIFeraldisEndgameSystem.Religion.cs
// Sect adoption and temple levelling.
// Partial of AIFeraldisEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        /// <summary>
        /// Adopt a sect. Two bugs made the first version spam
        /// "adopting sect War" 80 times in one match without ever adopting:
        ///   1. It used bare ids ("War"), but the real ids are PREFIXED
        ///      ("Sect_War" — SectConfig.War), so nothing ever matched.
        ///   2. It called only CommandRouter.IssueSectAdoption, which is the
        ///      REPLICATION STAMP. The thing that actually performs an
        ///      adoption is SectAdoption.TryStartAdoption; without it
        ///      IsAdopted stayed false forever and the AI retried every tick.
        /// Priority follows the Feraldis cluster. The mechanics themselves are
        /// shared with Alanthor (AIEndgameCommon) — the priority order is the
        /// only culture input.
        /// </summary>
        private static void TryAdoptSect(EntityManager em, Faction faction)
            => AIEndgameCommon.TryAdoptNextSect(em, faction, FeraldisSectPriority);


        /// <summary>Feraldis sect cluster, in preference order. IDs are the
        /// PREFIXED SectConfig constants — bare names silently match nothing.</summary>
        private static readonly string[] FeraldisSectPriority =
        {
            SectConfig.War,     // smite + elite unit (implemented kit)
            SectConfig.Ash,
            SectConfig.Ruin,
            SectConfig.Wrath,
        };
        /// <summary>The Corruptor is gated at Temple Lv 3, so the Temple has
        /// to climb before the verb is even available.</summary>
        /// <summary>Climb the Temple toward L3 (the Corruptor gate).
        /// FIXED 2026-08-12: this used to re-issue the upgrade command every
        /// 5 s tick with no cost check, no in-progress guard and no
        /// UnderConstruction guard. It now shares Alanthor's guarded ladder.</summary>
        private static void TryLevelTemple(EntityManager em, Faction faction)
            => AIEndgameCommon.TryLevelTemple(em, faction);
    }
}
