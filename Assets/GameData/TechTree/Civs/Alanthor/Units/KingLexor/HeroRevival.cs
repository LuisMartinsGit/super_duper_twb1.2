// HeroRevival.cs
// What it costs to get a dead hero back, and how much of him returns.
// Canon: docs/Design/Heroes.md §4.
//
// SUPERSEDES HeroTrainLimit's flat respawn tax (+15 % training time per death,
// compounding forever). That tax punished dying without ever offering a
// decision, and it never scaled with what was actually lost — losing a level-1
// king and a level-9 king cost exactly the same.
//
// The player now picks how much of the man comes back:
//
//   Rally the Oath   he returns 3 levels down, at his normal price and time
//   Full Honours     he returns at the level he died at, for x(1 + 0.15/level)
//
// Rallying a level-6 king hands you back a level-3 king — BELOW Honour thy
// Pledge's unlock — which is what stops the cheap option being the obvious one.
//
// MULTIPLAYER: this is a static dictionary, exactly as HeroTrainLimit's
// respawn counter is, and carries the same caveat — it must move to
// per-faction ECS state for lockstep. Flagged in Heroes.md §5. The MODE is
// already replicated (it rides the train command), so what is unreplicated is
// only the recorded level.

using System.Collections.Generic;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Abilities
{
    /// <summary>How much of a dead hero the player is paying to get back.</summary>
    public enum HeroRevivalMode : byte
    {
        /// <summary>Not a revival at all — the hero's first training.</summary>
        None = 0,
        /// <summary>Cheap and quick, three levels down.</summary>
        RallyTheOath = 1,
        /// <summary>Everything he was, at a price that scales with it.</summary>
        FullHonours = 2,
    }

    public static class HeroRevival
    {
        /// <summary>Faction -> the level its hero died at. Absent = the hero
        /// has never died, so there is nothing to revive and both modes are
        /// meaningless.</summary>
        private static readonly Dictionary<int, byte> _diedAt = new Dictionary<int, byte>();

        /// <summary>Remember what was lost. Called from HeroLevelSystem the
        /// frame a UniqueUnitTag hero drops to zero HP.</summary>
        public static void RecordDeath(Faction faction, byte level)
        {
            _diedAt[(int)faction] = HeroProgressionConfig.Clamp(level);
        }

        /// <summary>The level this faction's hero died at, or 0 if it has
        /// never lost one.</summary>
        public static byte DiedAtLevel(Faction faction)
            => _diedAt.TryGetValue((int)faction, out var l) ? l : (byte)0;

        public static bool HasFallenHero(Faction faction) => DiedAtLevel(faction) > 0;

        /// <summary>Clear the ledger between matches — the statics outlive the
        /// scene, and an inherited death would price the next match's first
        /// king as a revival.</summary>
        public static void ResetAll() => _diedAt.Clear();

        /// <summary>
        /// The level the hero comes back at under this mode. Falls back to
        /// level 1 when there is no fallen hero to restore, which is the
        /// ordinary first training.
        /// </summary>
        public static byte ReturnLevel(Faction faction, HeroRevivalMode mode)
        {
            byte died = DiedAtLevel(faction);
            if (died == 0 || mode == HeroRevivalMode.None) return HeroProgressionConfig.MinLevel;
            return mode == HeroRevivalMode.FullHonours
                ? died
                : HeroProgressionConfig.RallyLevel(died);
        }

        /// <summary>
        /// Multiplier on BOTH the cost and the training time. Only Full
        /// Honours scales: Rally the Oath is deliberately priced at the
        /// hero's ordinary cost, because paying full price for a diminished
        /// man is not a choice anybody would take.
        /// </summary>
        public static float PriceMultiplier(Faction faction, HeroRevivalMode mode)
        {
            byte died = DiedAtLevel(faction);
            if (died == 0 || mode != HeroRevivalMode.FullHonours) return 1f;
            return HeroProgressionConfig.FullHonoursMultiplier(died);
        }
    }
}
