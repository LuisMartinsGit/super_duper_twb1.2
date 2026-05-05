// WarSectCostHelper.cs
// Discount-helper for War's Lv I "Forged in Battle" passive (military -5% cost).
// Used at every unit-train Spend call site in the UI / AI to keep the
// discount uniform without sprinkling SectQuery checks everywhere.
//
// Per-level scaling (Phase 4):
//   Lv I  : -5% cost,  +15% train speed,  +0.5%/produced (cap +12%)
//   Lv II : -10% cost, +25% train speed,  +1%/produced  (cap +25%)
//   Lv III: -15% cost, +35% train speed,  +1.5%/produced (cap +35%)
// Phase 2d wires Lv I only — the cost discount + the train-speed discount
// (in TrainingSystem). The "+%/produced" bonus is deferred until Phase 4
// (it needs a per-faction produced-counter component).
//
// task-063 phase 2d.
//
// Location: Assets/Scripts/Economy/WarSectCostHelper.cs

using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Cost-modifier helpers for War's military discount. Stateless.
    /// </summary>
    public static class WarSectCostHelper
    {
        /// <summary>
        /// Per-level cost multiplier — Lv I/II/III give -5/-10/-15%.
        /// </summary>
        public static float CostMultiplierFor(byte level) => level switch
        {
            2 => 0.90f,
            3 => 0.85f,
            _ => 0.95f,
        };

        /// <summary>
        /// Per-level training-time multiplier — Lv I/II/III give -15/-25/-35%.
        /// Read by TrainingSystem when computing the per-unit train cooldown.
        /// </summary>
        public static float TrainTimeMultiplierFor(byte level) => level switch
        {
            2 => 0.75f,
            3 => 0.65f,
            _ => 0.85f,
        };

        /// <summary>
        /// Returns the cost the faction should be charged for training
        /// <paramref name="unitId"/>. Applies War's military discount if the
        /// faction has War adopted AND the unit is a military class.
        /// Non-military units (workers / scouts / support) pay the base cost.
        /// </summary>
        public static Cost MilitaryDiscount(EntityManager em, Faction faction, string unitId, in Cost baseCost)
        {
            if (!IsMilitaryUnit(unitId)) return baseCost;
            byte level = SectQuery.LevelOf(em, faction, SectConfig.War, SectLeverKind.Passive);
            if (level == 0) return baseCost;
            return Scale(baseCost, CostMultiplierFor(level));
        }

        /// <summary>
        /// True if a unit id is a military class (melee / ranged / siege).
        /// Sect units are special — see SectUniqueUnitTag check at runtime.
        /// </summary>
        public static bool IsMilitaryUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            var cls = TheWaningBorder.Entities.UnitFactory.GetUnitClass(unitId);
            return cls == UnitClass.Melee
                || cls == UnitClass.Ranged
                || cls == UnitClass.Siege;
        }

        private static Cost Scale(in Cost c, float mult)
        {
            return new Cost
            {
                Supplies  = (int)(c.Supplies  * mult),
                Iron      = (int)(c.Iron      * mult),
                Crystal   = (int)(c.Crystal   * mult),
                Veilsteel = (int)(c.Veilsteel * mult),
                Glow      = (int)(c.Glow      * mult),
            };
        }
    }
}
