// HeroProgressionConfig.cs
// Static tuning for hero levels 1..10. Canon: docs/Design/Heroes.md.
//
// Sits beside UnitRankConfig on purpose — they are the two progression
// ladders in the game and they are deliberately opposite:
//
//   UnitRank 1..5   is BOUGHT with resources, for any military unit.
//   HeroLevel 1..10 is EARNED from kills, for UniqueUnitTag heroes only.
//
// A hero left standing in your base stays level 1 no matter how rich you are.
// That is the whole point: it makes "where is my hero right now" a real
// question, and it makes the level-gated abilities a reward for using him
// rather than for out-earning the opponent.

using TheWaningBorder.Core;
using TheWaningBorder.Data;

namespace TheWaningBorder.Economy
{
    public static class HeroProgressionConfig
    {
        public const byte MinLevel = 1;
        public const byte MaxLevel = 10;

        /// <summary>Radius within which an allied hero shares in a kill it did
        /// not land, at <see cref="AssistShare"/> of the value. A hero present
        /// at the battle should grow even when the killing blow was somebody
        /// else's.</summary>
        public const float AssistRadius = 15f;
        public const float AssistShare = 0.5f;

        /// <summary>
        /// Cumulative XP required to REACH each level, indexed by level.
        /// Index 0 and 1 are zero (a hero enters at level 1).
        ///
        /// The step widens by 80 each time (120, 180, 260, 340 …) so the first
        /// levels land inside the opening engagements and level 10 is a
        /// whole-match project. First-pass balance — tuned on paper, per
        /// Heroes.md §5.
        /// </summary>
        private static readonly int[] _xpToReach =
        {
            0,      // (unused)
            0,      // Lv 1 — starting level
            120,    // Lv 2
            300,    // Lv 3
            560,    // Lv 4  <- Honour thy Pledge unlocks here
            900,    // Lv 5
            1320,   // Lv 6
            1820,   // Lv 7
            2400,   // Lv 8
            3060,   // Lv 9
            3800,   // Lv 10
        };

        /// <summary>Cumulative XP needed to reach <paramref name="level"/>.
        /// Levels past the cap return the cap's requirement.</summary>
        public static int XpToReach(int level)
        {
            if (level <= MinLevel) return 0;
            if (level >= MaxLevel) return _xpToReach[MaxLevel];
            return _xpToReach[level];
        }

        /// <summary>The level a given amount of banked XP earns.</summary>
        public static byte LevelForXp(int xp)
        {
            byte lvl = MinLevel;
            for (byte l = (byte)(MinLevel + 1); l <= MaxLevel; l++)
                if (xp >= _xpToReach[l]) lvl = l; else break;
            return lvl;
        }

        /// <summary>
        /// XP a kill is worth: the victim's total resource cost.
        ///
        /// Using cost rather than a flat number means a hero is rewarded for
        /// killing things that mattered — trading blows with militia never
        /// levels him the way breaking a siege train does.
        /// </summary>
        public static int XpForKill(CostBlock victimCost)
            => victimCost == null ? 0
             : victimCost.Supplies + victimCost.Iron
             + victimCost.Veilstone + victimCost.Veilsteel;

        // ── Revival (Heroes.md §4) ──────────────────────────────────────

        /// <summary>Levels lost by taking the cheap revival. Rallying a
        /// level-6 hero returns him at 3, which is BELOW Honour thy Pledge's
        /// unlock — that loss is the whole decision.</summary>
        public const int RallyLevelLoss = 3;

        /// <summary>Cost/time growth per level for reviving a hero AT the
        /// level he died at. 15 % per level over the first, so a level-10
        /// death costs 2.35x to undo in full.</summary>
        public const float FullHonoursPerLevel = 0.15f;

        /// <summary>Multiplier on both cost and training time for a
        /// Full Honours revival of a hero that died at this level.</summary>
        public static float FullHonoursMultiplier(int diedAtLevel)
            => 1f + FullHonoursPerLevel * (Clamp(diedAtLevel) - 1);

        /// <summary>The level a Rally the Oath revival returns him at.</summary>
        public static byte RallyLevel(int diedAtLevel)
        {
            int l = Clamp(diedAtLevel) - RallyLevelLoss;
            return (byte)(l < MinLevel ? MinLevel : l);
        }

        public static byte Clamp(int level)
            => (byte)(level < MinLevel ? MinLevel : level > MaxLevel ? MaxLevel : level);
    }
}
