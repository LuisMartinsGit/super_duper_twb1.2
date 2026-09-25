// WallTiers.cs
// The FOUR wall levels (docs/Design/Age_1_Alanthor.md § The four wall
// levels): 0 timber palisade — every culture, from Age 0; 1 stone — granted
// free by the Alanthor culture pick; 2 battlemented and 3 shielded —
// Alanthor only, bought AT THE WALL HUB.
//
// The ladder is deliberately the one every other building uses (Lv0 is the
// culture-less form, the culture pick grants Lv1, Lv2 and Lv3 are bought),
// which is why it is numbered from ZERO and not from one.
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
        public const byte Palisade = 0;
        /// <summary>Coursed stone — granted free by the Alanthor culture
        /// pick, exactly as a building's Lv1 is.</summary>
        public const byte Stone = 1;
        /// <summary>Merlons, arrow loops and hoardings — Alanthor,
        /// `Battlements`, bought at the Wall Hub.</summary>
        public const byte Battlemented = 2;
        /// <summary>Hung shields + garrison slots — Alanthor,
        /// `ShieldedRamparts`, bought at the Wall Hub.</summary>
        public const byte Shielded = 3;

        /// <summary>Kept so older call sites reading "the top level" still
        /// compile and still mean the top level.</summary>
        public const byte Reinforced = Shielded;

        /// <summary>The highest level a wall can reach.</summary>
        public const byte MaxLevel = Shielded;

        /// <summary>
        /// The two wall techs. BOTH are bought, at the Hall, Alanthor-gated
        /// (2026-09-24, docs/Design/Age_1_Alanthor.md § The three wall levels).
        ///
        /// Level 2 used to be free — committing to Alanthor at age-up WAS the
        /// stone upgrade. That left the player with nothing to press: they
        /// looked for the wall upgrade button after aging up, found none, and
        /// read the feature as missing. One visible, purchasable button that
        /// turns every wooden wall to stone at once is the whole point of a
        /// faction-wide wall tier, so that is what it is now.
        /// </summary>
        public const string BattlementsTechId = "Battlements";
        public const string ShieldedTechId = "ShieldedRamparts";

        /// <summary>Old name for <see cref="ShieldedTechId"/>.</summary>
        public const string ReinforcedTechId = ShieldedTechId;

        /// <summary>Garrison slots a CURTAIN MODULE offers at this level.
        /// Hubs and gates take none — the men stand on the curtain.</summary>
        public const int ReinforcedGarrisonSlots = 2;

        /// <summary>HP multiplier over the SO's level-1 numbers.</summary>
        public static float HpMultiplier(byte level) => level switch
        {
            Stone => 1.6f,
            Battlemented => 2.3f,
            Shielded => 3.0f,
            _ => 1f,
        };

        /// <summary>Garrison slots a curtain module of this level offers.</summary>
        public static int GarrisonSlots(byte level)
            => level >= Shielded ? ReinforcedGarrisonSlots : 0;

        /// <summary>
        /// The level <paramref name="faction"/> builds at right now — the
        /// highest wall tech it has researched. Falls back to the palisade,
        /// which is every Age 0 faction of any culture, and every Age 1
        /// faction that has not bought the upgrade yet.
        /// </summary>
        public static byte LevelFor(EntityManager em, Faction faction)
        {
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null)
            {
                if (research.HasResearched(faction, ShieldedTechId)) return Shielded;
                if (research.HasResearched(faction, BattlementsTechId)) return Battlemented;
            }
            // Lv1 comes from the CULTURE, not from research: picking Alanthor
            // re-clads the whole wall in stone for free, the same way every
            // other building takes its Lv1 form at the culture pick.
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
            Shielded => "Shielded Wall",
            Battlemented => "Battlemented Wall",
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
            return lvl > MaxLevel ? Palisade : lvl;
        }

        /// <summary>Scale a Lv0 (timber) stat off the SO into this level's value.</summary>
        public static int ScaleHp(float baseHp, byte level)
            => Mathf.Max(1, Mathf.RoundToInt(baseHp * HpMultiplier(level)));
    }
}
