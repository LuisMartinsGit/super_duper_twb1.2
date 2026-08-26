// WarSectCostHelper.cs
// Discount-helper for War's Lv I "Forged in Battle" passive (military -5% cost).
// Used at every unit-train Spend call site in the UI / AI to keep the
// discount uniform without sprinkling SectQuery checks everywhere.
//
// Canon numbers (docs/Design/Sects.md section 6, 2026-08-18): "your military
// units cost 10% less and train 20% faster." FLAT - the design gives a sect's
// passive one effect with no level ladder, so the per-level -5/-10/-15% and
// -15/-25/-35% schedule the code used to run is retired. The level argument
// survives on both methods so every call site keeps compiling and a future
// design that DOES level passives has somewhere to put the numbers.
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
        /// Military cost multiplier - a flat -10% at every level. The level
        /// argument is accepted and ignored; see the file header.
        /// </summary>
        public static float CostMultiplierFor(byte level) => 0.90f;

        /// <summary>
        /// Military training-time multiplier - a flat -20% (train 20% faster)
        /// at every level. Read by TrainingSystem when computing the per-unit
        /// train cooldown.
        /// </summary>
        public static float TrainTimeMultiplierFor(byte level) => 0.80f;

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

        /// <summary>
        /// Call to Arms' training-cost multiplier for one building - 1 when the
        /// building carries no boon. Separate from the War PASSIVE discount
        /// above: the passive is faction-wide and permanent, this is a timed
        /// area effect, and the two multiply.
        /// </summary>
        public static float TrainingBoonCostMultiplier(EntityManager em, Entity building)
        {
            if (building == Entity.Null) return 1f;
            if (!em.Exists(building)) return 1f;
            if (!em.HasComponent<SectTrainingBoon>(building)) return 1f;
            var boon = em.GetComponentData<SectTrainingBoon>(building);
            if (boon.TimeRemaining <= 0f) return 1f;
            return boon.CostMultiplier <= 0f ? 1f : boon.CostMultiplier;
        }

        /// <summary>
        /// Call to Arms' training-SPEED multiplier for one building (2 = double
        /// speed at Lv III). Read by TrainingSystem, which divides the training
        /// time by it.
        /// </summary>
        public static float TrainingBoonSpeedMultiplier(EntityManager em, Entity building)
        {
            if (building == Entity.Null) return 1f;
            if (!em.Exists(building)) return 1f;
            if (!em.HasComponent<SectTrainingBoon>(building)) return 1f;
            var boon = em.GetComponentData<SectTrainingBoon>(building);
            if (boon.TimeRemaining <= 0f) return 1f;
            return boon.SpeedMultiplier <= 0f ? 1f : boon.SpeedMultiplier;
        }

        /// <summary>
        /// Scale an already-discounted cost by a recorded multiplier. Used by
        /// the cancel path, which must refund exactly what was charged rather
        /// than what the current boon state would charge.
        /// </summary>
        public static Cost ApplyPaidMultiplier(in Cost c, float multiplier)
            => multiplier <= 0f || multiplier >= 1f ? c : Scale(c, multiplier);

        private static Cost Scale(in Cost c, float mult)
        {
            return new Cost
            {
                Supplies  = (int)(c.Supplies  * mult),
                Iron      = (int)(c.Iron      * mult),
                Veilstone   = (int)(c.Veilstone   * mult),
                Veilsteel = (int)(c.Veilsteel * mult),
                Glow      = (int)(c.Glow      * mult),
            };
        }
    }
}
