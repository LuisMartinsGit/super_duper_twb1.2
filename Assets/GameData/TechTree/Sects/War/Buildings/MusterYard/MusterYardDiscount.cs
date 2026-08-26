// The Muster Yard's effect: "every per-battalion upgrade you apply anywhere in
// the faction costs 50% less" (docs/Design/Sects.md section 6).
//
// It lives beside the building rather than in SectLeverEffects because it is
// the BUILDING's effect, not a lever number - it is live exactly while at
// least one completed Muster Yard stands, and dies with the last one.
//
// The discount deliberately does NOT stack. Five Muster Yards are five places
// to train Warbreakers and five things the enemy has to kill, not a 97% cut:
// the cap of 5 exists for every sect building, and a stacking economy effect
// behind it would make the cap a mandatory build order rather than a choice.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Economy
{
    public static class MusterYardDiscount
    {
        /// <summary>Half price. Flat and non-stacking - see the file header.</summary>
        public const float CostMultiplier = 0.5f;

        private static readonly ComponentType[] YardTypes =
        {
            ComponentType.ReadOnly<MusterYardTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static CachedEntityQuery _yardQuery;

        /// <summary>
        /// True if the faction has at least one COMPLETED Muster Yard standing.
        /// A foundation under construction does not count - the racks have to
        /// exist before they can kit anyone out.
        /// </summary>
        public static bool HasStandingYard(EntityManager em, Faction faction)
        {
            var q = _yardQuery.Get(em, YardTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Apply the Muster Yard discount to a per-battalion equipment upgrade
        /// cost. Returns the cost unchanged when no yard stands.
        /// </summary>
        public static Cost Apply(EntityManager em, Faction faction, in Cost baseCost)
        {
            if (!HasStandingYard(em, faction)) return baseCost;
            return Cost.Of(
                supplies:  (int)(baseCost.Supplies  * CostMultiplier),
                iron:      (int)(baseCost.Iron      * CostMultiplier),
                veilstone: (int)(baseCost.Veilstone * CostMultiplier),
                veilsteel: (int)(baseCost.Veilsteel * CostMultiplier),
                glow:      (int)(baseCost.Glow      * CostMultiplier));
        }
    }
}
