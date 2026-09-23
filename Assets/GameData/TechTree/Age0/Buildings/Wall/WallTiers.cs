// WallTiers.cs
// The three wall levels (docs/Design/Age_1_Alanthor.md § Wall levels, the
// gate structure and emplacements): 1 palisade — every culture, from Age 0;
// 2 crude stone and 3 reinforced — Alanthor only, bought at the Hall.
//
// A wall's level is a FACTION fact, not a per-wall one: everything the
// faction owns is re-clad the moment a tech lands, so there is never a
// patchwork. The level is stamped onto each piece as WallTier at creation
// (so the sim and the visuals read one value, not the research state on a
// hot path) and bumped in place by the tech applier.

using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Entities
{
    public static class WallTiers
    {
        /// <summary>Timber palisade — Age 0, every culture.</summary>
        public const byte Palisade = 1;
        /// <summary>Crude stone — granted by the Alanthor age-up, not researched.</summary>
        public const byte Stone = 2;
        /// <summary>Reinforced (shields + garrison slots) — Alanthor, ShieldedRamparts.</summary>
        public const byte Reinforced = 3;

        /// <summary>The only wall tech that is BOUGHT. Level 2 is not
        /// researched at all — choosing Alanthor at age-up IS the stone
        /// upgrade (docs/Design/Age_1_Alanthor.md § The three wall levels).</summary>
        public const string ReinforcedTechId = "ShieldedRamparts";

        /// <summary>Garrison slots a CURTAIN MODULE offers at this level.
        /// Hubs and gates take none — the men stand on the curtain.</summary>
        public const int ReinforcedGarrisonSlots = 2;

        /// <summary>HP multiplier over the SO's level-1 numbers.</summary>
        public static float HpMultiplier(byte level) => level switch
        {
            Stone => 1.6f,
            Reinforced => 2.3f,
            _ => 1f,
        };

        /// <summary>Garrison slots a curtain module of this level offers.</summary>
        public static int GarrisonSlots(byte level)
            => level >= Reinforced ? ReinforcedGarrisonSlots : 0;

        /// <summary>
        /// The level <paramref name="faction"/> builds at right now. Level 2
        /// comes from the CULTURE, not from research: aging up as Alanthor
        /// re-clads the whole wall in stone for free. Level 3 is the one
        /// bought thing. Falls back to the palisade when neither holds — which
        /// is every Age 0 faction, of any culture.
        /// </summary>
        public static byte LevelFor(EntityManager em, Faction faction)
        {
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null && research.HasResearched(faction, ReinforcedTechId)) return Reinforced;
            if (CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor) return Stone;
            return Palisade;
        }

        /// <summary>
        /// What the player calls this wall. The id stays `Alanthor_Wall`
        /// everywhere (renaming it would ripple through the recipe table,
        /// footprints, costs, build times, the name resolver and the AI), but
        /// an Age 0 palisade is NOT "the Alanthor Wall" and must not read as
        /// one — it is every culture's timber fence.
        /// </summary>
        public static string DisplayName(byte level) => level switch
        {
            Reinforced => "Reinforced Wall",
            Stone => "Stone Wall",
            _ => "Wooden Wall",
        };

        /// <summary>Towers are masonry: a timber palisade cannot carry one
        /// (docs/Design/Age_1_Alanthor.md § The three wall levels).</summary>
        public static bool AllowsTowers(byte level) => level >= Stone;

        /// <summary>The level a standing wall piece was clad at (1 when it
        /// carries no tier, which is every pre-2026-09-21 save).</summary>
        public static byte Of(EntityManager em, Entity wallPiece)
        {
            if (wallPiece == Entity.Null || !em.Exists(wallPiece)) return Palisade;
            if (!em.HasComponent<WallTier>(wallPiece)) return Palisade;
            byte lvl = em.GetComponentData<WallTier>(wallPiece).Level;
            return lvl < Palisade || lvl > Reinforced ? Palisade : lvl;
        }

        /// <summary>Scale a level-1 stat off the SO into this level's value.</summary>
        public static int ScaleHp(float baseHp, byte level)
            => Mathf.Max(1, Mathf.RoundToInt(baseHp * HpMultiplier(level)));
    }
}
