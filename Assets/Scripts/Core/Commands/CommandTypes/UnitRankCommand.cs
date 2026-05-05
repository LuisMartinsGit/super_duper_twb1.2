// UnitRankCommand.cs
// Promote a single military unit to its next rank, charging the
// per-rank cost gate (Supplies / Crystal / Veilsteel / Glow).
// Audit fix #1.
//
// Location: Assets/Scripts/Core/Commands/CommandTypes/UnitRankCommand.cs

using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Result of a UnitRankCommandHelper.Execute call.
    /// </summary>
    public enum UnitRankPromoteResult
    {
        Ok = 0,
        NotMilitary,
        AlreadyMaxed,
        CannotAfford,
        Invalid,
    }

    /// <summary>
    /// Helper class for promoting units. The command path doesn't need an
    /// ECS component since promotion is a one-shot transaction (no per-frame
    /// consumer would inspect a queued component).
    /// </summary>
    public static class UnitRankCommandHelper
    {
        public static UnitRankPromoteResult Execute(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit)) return UnitRankPromoteResult.Invalid;
            if (!em.HasComponent<UnitTag>(unit)) return UnitRankPromoteResult.NotMilitary;

            var unitTag = em.GetComponentData<UnitTag>(unit);
            if (unitTag.Class != UnitClass.Melee
                && unitTag.Class != UnitClass.Ranged
                && unitTag.Class != UnitClass.Siege)
                return UnitRankPromoteResult.NotMilitary;

            byte currentRank = 1;
            if (em.HasComponent<UnitRank>(unit))
                currentRank = em.GetComponentData<UnitRank>(unit).Value;
            if (currentRank < 1) currentRank = 1;
            if (currentRank >= UnitRankConfig.MaxRank) return UnitRankPromoteResult.AlreadyMaxed;

            byte targetRank = (byte)(currentRank + 1);
            var cost = UnitRankConfig.CostFor(targetRank);

            if (!em.HasComponent<FactionTag>(unit)) return UnitRankPromoteResult.Invalid;
            var faction = em.GetComponentData<FactionTag>(unit).Value;

            if (!FactionEconomy.Spend(em, faction, cost))
                return UnitRankPromoteResult.CannotAfford;

            if (em.HasComponent<UnitRank>(unit))
                em.SetComponentData(unit, new UnitRank { Value = targetRank });
            else
                em.AddComponentData(unit, new UnitRank { Value = targetRank });

            return UnitRankPromoteResult.Ok;
        }
    }
}
