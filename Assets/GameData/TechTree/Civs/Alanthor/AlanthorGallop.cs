// AlanthorGallop.cs
// Galloping (docs/Design/Age_1_Alanthor.md § Galloping): the Royal Stable's
// Full Gallop research lets Alanthor cavalry that keep moving gallop. A
// galloping rider takes less damage, deals more, rides through enemy units
// trampling each once, and strikes in passing without stopping.
//
// The state lives in GallopState, stamped on the Outrider and the Cataphract
// when the research completes (TechEffectSystem) and at spawn after it
// (AlanthorActiveHelper.ApplySpawnPassives). Every number is the Full Gallop
// SO's effectsList, copied onto the component at the grant, the same way the
// Charge passive carries its tech's numbers.
//
// Read by: AbilityDamageHooks.IncomingMultiplier (damage taken),
// CombatDamageHelper.ApplyBonusDamageOnHit (damage dealt), SteeringSystem
// (rides through hostile units), TargetingSystem (no auto-acquire while
// galloping). Ticked by AlanthorGallopSystem (the clock, trample, strikes).

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    /// <summary>A rider that can gallop, and whether it is galloping now.</summary>
    public struct GallopState : IComponentData
    {
        // From FullGallop.asset effectsList.
        public float AfterSeconds;    // moving this long starts the gallop
        public float MinSpeedPct;     // "moving" = at least this % of own MoveSpeed
        public float GraceSeconds;    // slower than that this long ends it
        public float TakenPct;        // 30 = takes 30 % less damage
        public float DealtPct;        // 20 = deals 20 % more damage
        public float TramplePct;      // trample hit = this % of the rider's Damage
        public float TrampleReach;    // metres beyond the rider's radius

        // Runtime.
        public float MovingSeconds;
        public float StillSeconds;
        public float LastX, LastZ;
        public byte HasLast;
        /// <summary>1 while galloping.</summary>
        public byte Active;
    }

    /// <summary>The enemy units this gallop has already trampled; cleared when
    /// the gallop ends, so each is hit once per gallop.</summary>
    [InternalBufferCapacity(8)]
    public struct GallopTrampled : IBufferElementData
    {
        public Entity Value;
    }

    public static class AlanthorGallop
    {
        public const string TechId = "FullGallop";

        /// <summary>The state as the Full Gallop SO authors it, not galloping.</summary>
        public static GallopState FromTech() => new GallopState
        {
            AfterSeconds = TechCatalog.TechEffect(TechId, "GallopAfterSeconds"),
            MinSpeedPct = TechCatalog.TechEffect(TechId, "GallopMinSpeedPct"),
            GraceSeconds = TechCatalog.TechEffect(TechId, "GallopGraceSeconds"),
            TakenPct = TechCatalog.TechEffect(TechId, "GallopDamageTakenPct"),
            DealtPct = TechCatalog.TechEffect(TechId, "GallopDamageDealtPct"),
            TramplePct = TechCatalog.TechEffect(TechId, "TrampleDamagePct"),
            TrampleReach = TechCatalog.TechEffect(TechId, "TrampleReach"),
        };

        /// <summary>Give a rider the ability to gallop (research or spawn).</summary>
        public static void Grant(EntityManager em, Entity e)
        {
            if (!em.Exists(e)) return;
            var s = FromTech();
            if (em.HasComponent<GallopState>(e)) em.SetComponentData(e, s);
            else em.AddComponentData(e, s);
            if (!em.HasBuffer<GallopTrampled>(e)) em.AddBuffer<GallopTrampled>(e);
        }

        public static bool IsGalloping(EntityManager em, Entity e)
            => e != Entity.Null && em.HasComponent<GallopState>(e)
               && em.GetComponentData<GallopState>(e).Active != 0;

        /// <summary>Damage-taken multiplier: below 1 while galloping.</summary>
        public static float IncomingMultiplier(EntityManager em, Entity e)
        {
            if (e == Entity.Null || !em.HasComponent<GallopState>(e)) return 1f;
            var s = em.GetComponentData<GallopState>(e);
            return s.Active != 0 ? 1f - s.TakenPct / 100f : 1f;
        }

        /// <summary>Damage-dealt multiplier: above 1 while galloping.</summary>
        public static float OutgoingMultiplier(EntityManager em, Entity e)
        {
            if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<GallopState>(e)) return 1f;
            var s = em.GetComponentData<GallopState>(e);
            return s.Active != 0 ? 1f + s.DealtPct / 100f : 1f;
        }

        /// <summary>Burst form for jobs that hold a lookup.</summary>
        public static bool IsGalloping(in ComponentLookup<GallopState> lk, Entity e)
            => lk.TryGetComponent(e, out var s) && s.Active != 0;
    }
}
