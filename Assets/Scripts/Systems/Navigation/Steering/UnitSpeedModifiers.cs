// UnitSpeedModifiers.cs
// The multiplier UnitIntegratorSystem applies on top of a unit's base (or
// formation-override) speed: spell slows, veil / Suppression drag, Fortify,
// timed haste. Lifted out of the integrator so FormationGroupSystem can size
// the group speed from what its members can ACTUALLY do — a formation whose
// speed ignores a 40 % slow on one member leaves that member behind for the
// whole march (docs/Design/Navigation_And_Formations.md §2.10).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    public static class UnitSpeedModifiers
    {
        /// <summary>Product of every speed effect on <paramref name="e"/>.
        /// 0 when it cannot move at all (Fortified).</summary>
        public static float Multiplier(EntityManager em, Entity e)
        {
            float m = 1f;
            if (TransientState.Active<SpellDebuff>(em, e))
                m *= 1f - em.GetComponentData<SpellDebuff>(e).SpeedReduction;
            // The Veil / Suppression auras: BorderDebuff.SpeedPenalty was
            // authored but never consumed — units wading through veil crust
            // (or a Suppression field) now actually slow down.
            if (em.HasComponent<BorderDebuff>(e))
            {
                var bd = em.GetComponentData<BorderDebuff>(e);
                if (bd.SpeedPenalty > 0f)
                    m *= 1f - math.min(0.9f, bd.SpeedPenalty);
            }
            if (TransientState.Active<Fortified>(em, e)) return 0f;
            if (TransientState.Active<SpellBuff>(em, e))
            {
                var buff = em.GetComponentData<SpellBuff>(e);
                if (buff.SpeedMultiplier > 0f && buff.SpeedMultiplier != 1f)
                    m *= buff.SpeedMultiplier;
            }
            return m;
        }
    }
}
