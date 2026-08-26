// Runtime state for the Sect of War's canon actives (docs/Design/Sects.md
// section 6). Components only — the tick lives in WarSectEffectSystem and the
// application in SectActivePowerSystem.War.cs.

using Unity.Entities;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Blood Rain's map-wide caster lockout. A SINGLETON: one entity carries it
    /// for the whole match, whoever cast it, because the design effect is "no
    /// ability or sect power can be cast anywhere" — it is not per-faction and
    /// it is deliberately not owner-exempt. The caster is silenced by their own
    /// Blood Rain along with everyone else.
    ///
    /// Enforced at the two cast gates rather than by stripping abilities:
    /// SectActivePowerHelper.Fire refuses to start a power, and
    /// AbilityLifecycleSystem drops an AbilityActivated before it charges a
    /// cooldown. Anything already in flight (a wound-up strike, a cast timer)
    /// resolves — silence stops NEW casts, it does not un-cast.
    /// </summary>
    public struct SectGlobalSilence : IComponentData
    {
        public float TimeRemaining;

        /// <summary>Which faction's Blood Rain is responsible. Presentation and
        /// the AI read it; the effect itself does not care.</summary>
        public Faction Source;
    }

    /// <summary>
    /// Call to Arms, stamped on one military BUILDING. Training cost is scaled
    /// by <see cref="CostMultiplier"/> and training time divided by
    /// <see cref="SpeedMultiplier"/> while this stands.
    ///
    /// It rides the building rather than the faction on purpose: the power is
    /// area-targeted, so two Barracks inside the circle get it and a third
    /// outside does not.
    /// </summary>
    public struct SectTrainingBoon : IComponentData
    {
        public float TimeRemaining;

        /// <summary>0.5 = half cost. Never 0 — that would make units free,
        /// which is exactly the version this design replaced.</summary>
        public float CostMultiplier;

        /// <summary>2 = double training speed (Lv III). 1 = unchanged.</summary>
        public float SpeedMultiplier;
    }
}
