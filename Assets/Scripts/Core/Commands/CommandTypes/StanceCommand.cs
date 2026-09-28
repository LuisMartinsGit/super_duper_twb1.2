// StanceCommand.cs
// Setting a unit's stance (docs/Design/Stances.md). Replicated through the
// lockstep SetStance command; this is the executor every peer runs.

using Unity.Entities;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    public static class StanceCommandHelper
    {
        /// <summary>
        /// Put a unit in a stance. Does NOT interrupt the unit's current order
        /// — a stance is a mode — except that Hold also stops the unit and
        /// plants its guard point where it stands (see
        /// <see cref="HoldPositionCommandHelper"/>).
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, UnitStanceMode mode)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (em.HasComponent<BuildingTag>(unit)) return;
            if (!em.HasComponent<UnitTag>(unit)) return;
            // An emplaced engine is bolted to its platform and holds for ever
            // (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet
            // emplacements) — no stance can unbolt it.
            if (em.HasComponent<EmplacedEngineTag>(unit)) return;

            if (mode == UnitStanceMode.Hold)
            {
                HoldPositionCommandHelper.Execute(em, unit);
                return;
            }

            bool wasHold = em.HasComponent<HoldPositionTag>(unit);
            Apply(em, unit, mode);

            // Leaving Hold: the spot it was holding is where it now guards
            // from, not wherever its last move happened to point.
            if (wasHold && em.HasComponent<LocalTransform>(unit))
            {
                var pos = em.GetComponentData<LocalTransform>(unit).Position;
                if (em.HasComponent<GuardPoint>(unit))
                    em.SetComponentData(unit, new GuardPoint { Position = pos, Has = 1 });
                else
                    em.AddComponentData(unit, new GuardPoint { Position = pos, Has = 1 });
            }
        }

        /// <summary>
        /// Write the stance and keep <see cref="HoldPositionTag"/> in step with
        /// it. No order side-effects — the one place both markers change.
        /// </summary>
        public static void Apply(EntityManager em, Entity unit, UnitStanceMode mode)
        {
            if (!em.Exists(unit)) return;
            // Emplaced engines ALWAYS hold — their factories add
            // HoldPositionTag and nothing may take it away.
            if (em.HasComponent<EmplacedEngineTag>(unit)) mode = UnitStanceMode.Hold;

            var stance = new UnitStance { Value = mode };
            if (em.HasComponent<UnitStance>(unit)) em.SetComponentData(unit, stance);
            else em.AddComponentData(unit, stance);

            bool hold = mode == UnitStanceMode.Hold;
            bool hasTag = em.HasComponent<HoldPositionTag>(unit);
            if (hold && !hasTag) em.AddComponent<HoldPositionTag>(unit);
            else if (!hold && hasTag) em.RemoveComponent<HoldPositionTag>(unit);
        }

        /// <summary>The stance a unit is actually in. HoldPositionTag wins —
        /// scenarios add it directly.</summary>
        public static UnitStanceMode Effective(EntityManager em, Entity unit)
        {
            if (em.HasComponent<HoldPositionTag>(unit)) return UnitStanceMode.Hold;
            if (em.HasComponent<UnitStance>(unit)) return em.GetComponentData<UnitStance>(unit).Value;
            return UnitStanceMode.Defensive;
        }
    }
}
