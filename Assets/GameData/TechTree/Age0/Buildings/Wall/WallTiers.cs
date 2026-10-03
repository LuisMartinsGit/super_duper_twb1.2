// WallTiers.cs
// The wall levels (docs/Design/Age_1_Alanthor.md § The stone wall). Level 0
// is the PALISADE — a separate building since 2026-10-02 (`Palisade`,
// docs/Design/Age_0.md § Palisade), which never changes level. The stone
// wall (`Alanthor_Wall`, Alanthor only) is 1 stone — granted free by the
// culture pick; 2 battlemented and 3 shielded — bought AT THE WALL HUB.
//
// Every stone level is the SAME wall (4 m deep, walkable, symmetrical): a
// level changes the wall's HP and what may be fitted to it, never its size.
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

        /// <summary>RETIRED 2026-10-02: the deck is walkable at every stone
        /// level, so units stand on the wall instead of vanishing into it.
        /// Kept at 0 so nothing creates a WallGarrisonSlot.</summary>
        public const int ReinforcedGarrisonSlots = 0;

        /// <summary>Armour a foot unit gains on a Shielded deck — melee and
        /// ranged both (docs/Design/Age_1_Alanthor.md § The stone wall).
        /// Playtest placeholder.</summary>
        public const int DeckArmorBonus = 3;

        /// <summary>HP multiplier over the SO's level-1 numbers.</summary>
        public static float HpMultiplier(byte level) => level switch
        {
            Stone => 1.6f,
            Battlemented => 2.3f,
            Shielded => 3.0f,
            _ => 1f,
        };

        /// <summary>Garrison slots a curtain module of this level offers —
        /// none, at any level (retired 2026-10-02).</summary>
        public static int GarrisonSlots(byte level) => 0;

        /// <summary>
        /// The level <paramref name="faction"/>'s STONE wall stands at right
        /// now — the highest wall tech it has researched, and never below
        /// Stone. A palisade does not ask: it is level 0 for its whole life
        /// (AlanthorWall reads the kind off the piece, not the faction).
        /// </summary>
        public static byte LevelFor(EntityManager em, Faction faction)
        {
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null)
            {
                if (research.HasResearched(faction, ShieldedTechId)) return Shielded;
                if (research.HasResearched(faction, BattlementsTechId)) return Battlemented;
            }
            return Stone;
        }

        /// <summary>The level a NEW piece of this kind is raised at.</summary>
        public static byte LevelFor(EntityManager em, Faction faction, bool palisade)
            => palisade ? Palisade : LevelFor(em, faction);

        /// <summary>
        /// May <paramref name="faction"/> raise NEW wall of this kind
        /// (docs/Design/Age_0.md § Palisade, Age_1_Alanthor.md § The stone
        /// wall)? The palisade is every culture's in Age 0 and Feraldis's
        /// after; Alanthor and Runai lose it at age-up. The stone wall is
        /// Alanthor's from the age-up on. Read by the build panel AND every
        /// wall executor, so a stale panel cannot raise the wrong wall.
        /// </summary>
        public static bool CanBuild(EntityManager em, Faction faction, bool palisade)
        {
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            if (palisade) return culture == Cultures.None || culture == Cultures.Feraldis;
            return culture == Cultures.Alanthor;
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
            _ => "Palisade",
        };

        /// <summary>Towers are masonry: a timber palisade cannot carry one;
        /// every stone level can (docs/Design/Age_1_Alanthor.md § The stone
        /// wall).</summary>
        public static bool AllowsTowers(byte level) => level >= Stone;

        /// <summary>A Ballista mount needs the Battlemented wall.</summary>
        public static bool AllowsBallista(byte level) => level >= Battlemented;

        /// <summary>A Trebuchet mount needs the Shielded wall.</summary>
        public static bool AllowsTrebuchet(byte level) => level >= Shielded;

        /// <summary>A Shielded wall's hubs are towers (they shoot).</summary>
        public static bool HubIsTower(byte level) => level >= Shielded;

        /// <summary>Only the stone wall has a wall-walk.</summary>
        public static bool IsWalkable(byte level) => level >= Stone;

        /// <summary>The level a standing wall piece was clad at (1 when it
        /// carries no tier, which is every pre-2026-09-21 save).</summary>
        public static byte Of(EntityManager em, Entity wallPiece)
        {
            if (wallPiece == Entity.Null || !em.Exists(wallPiece)) return Palisade;
            if (!em.HasComponent<WallTier>(wallPiece)) return Palisade;
            byte lvl = em.GetComponentData<WallTier>(wallPiece).Level;
            return lvl > MaxLevel ? Palisade : lvl;
        }

        /// <summary>True for the two BOUGHT wall levels.</summary>
        public static bool IsLevelTech(string techId)
            => techId == BattlementsTechId || techId == ShieldedTechId;

        /// <summary>
        /// THE WALL LOCK (docs/Design/Age_1_Alanthor.md § The four wall
        /// levels): true from the moment a wall level is queued anywhere for
        /// <paramref name="faction"/> until it completes or is cancelled.
        /// While it holds, no standing wall piece may be converted (tower,
        /// gate, hub, emplacement) or extended from, and the level button is
        /// replaced by the research's progress. The UI greys the actions and
        /// the executors refuse the commands on this same test, so the lock
        /// holds identically on every lockstep peer.
        /// </summary>
        public static bool LevelResearchActive(EntityManager em, Faction faction)
            => TryGetLevelResearch(em, faction, out _, out _, out _);

        /// <summary>
        /// The wall level in flight for <paramref name="faction"/>: which
        /// tech, the building whose queue holds it, and its slot there (for a
        /// cancel). False when none is queued.
        /// </summary>
        public static bool TryGetLevelResearch(EntityManager em, Faction faction,
            out Entity host, out int slot, out string techId)
        {
            techId = BattlementsTechId;
            if (TheWaningBorder.Core.Commands.CommandRouter.IsResearchQueued(
                    em, faction, BattlementsTechId, out host, out slot))
                return true;
            techId = ShieldedTechId;
            if (TheWaningBorder.Core.Commands.CommandRouter.IsResearchQueued(
                    em, faction, ShieldedTechId, out host, out slot))
                return true;
            techId = null;
            return false;
        }

        /// <summary>Scale a Lv0 (timber) stat off the SO into this level's value.</summary>
        public static int ScaleHp(float baseHp, byte level)
            => Mathf.Max(1, Mathf.RoundToInt(baseHp * HpMultiplier(level)));
    }
}
