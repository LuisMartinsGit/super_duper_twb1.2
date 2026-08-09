// ConvertHutCommand.cs
// Per-hut age-up choice command — converts an Alanthor-owned Gatherer's Hut
// either into a Wall Hub (the cylinder connection point that anchors wall
// segments) or into a Watch Tower (the stand-alone Alanthor ranged defense).
// Location: Assets/Scripts/Core/Commands/CommandTypes/ConvertHutCommand.cs
//
// Phase 2 of task-wall-system-bfme2-rework-109. Mirrors CancelTrainCommand's
// XxxCommand / XxxCommandHelper pattern: the marker struct exists for symmetry
// and to allow ECS query gating in tests; the helper consumes the call
// directly. The actual entity transformation (destroy hut + spawn target)
// runs in HutConversionSystem once the 5-second timer expires.

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Carries the conversion destination chosen by the player. Marker for
    /// symmetry with the XxxCommand / XxxCommandHelper pattern — the helper
    /// is invoked directly and the component is never added to entities.
    /// </summary>
    public struct ConvertHutCommand : IComponentData
    {
        public HutConversionTarget Target;
    }

    /// <summary>
    /// Helper class for executing hut age-up conversion commands.
    /// </summary>
    public static class ConvertHutCommandHelper
    {
        /// <summary>
        /// Conversion cost — 40 supplies + 30 iron for both targets in v1
        /// (canonicalised in Phase 1 → docs/Design/Age_1_Alanthor.md). When
        /// task-109 reaches its art / balance pass these may diverge per
        /// target; today they are shared so the helper carries a single cost
        /// constant.
        /// </summary>
        public static readonly Cost ConversionCost = new Cost
        {
            Supplies = 40,
            Iron = 30,
            Veilstone = 0,
            Veilsteel = 0,
            Glow = 0,
        };

        /// <summary>
        /// Duration of the conversion timer in seconds. Matches the
        /// 5-second value canonicalised in docs/Design/Age_1_Alanthor.md.
        /// </summary>
        public const float ConversionDuration = 5f;

        /// <summary>
        /// Attempt to start a hut → Wall Hub / Watch Tower conversion.
        ///   1. Validate the hut still carries <see cref="GathererHutAgeUpChoice"/>
        ///      (idempotent — duplicate calls drop silently).
        ///   2. Spend the conversion cost from the hut's faction bank. If the
        ///      faction can't afford it, the call is a no-op (returns false).
        ///   3. Remove <see cref="GathererHutAgeUpChoice"/> and add
        ///      <see cref="GathererHutConverting"/> with a 5-second timer.
        /// Returns true if conversion was successfully started.
        /// </summary>
        public static bool Execute(EntityManager em, Entity hut, HutConversionTarget target)
        {
            if (hut == Entity.Null || !em.Exists(hut)) return false;
            if (!em.HasComponent<GathererHutAgeUpChoice>(hut)) return false;
            if (target != HutConversionTarget.WallHub && target != HutConversionTarget.WatchTower)
                return false;

            // Resolve owning faction so we can charge the bank.
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(hut))
                faction = em.GetComponentData<FactionTag>(hut).Value;

            if (!FactionEconomy.Spend(em, faction, ConversionCost))
                return false;

            em.RemoveComponent<GathererHutAgeUpChoice>(hut);

            if (em.HasComponent<GathererHutConverting>(hut))
            {
                em.SetComponentData(hut, new GathererHutConverting
                {
                    Target = target,
                    Remaining = ConversionDuration,
                    Total = ConversionDuration,
                });
            }
            else
            {
                em.AddComponentData(hut, new GathererHutConverting
                {
                    Target = target,
                    Remaining = ConversionDuration,
                    Total = ConversionDuration,
                });
            }

            return true;
        }
    }
}
