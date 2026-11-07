// AscendedHero.cs
// ASCENDED (docs/Design/Heroes.md §4). While a hero carries the Shardroot it is
// VERY HARD TO KILL:
//   * takes only damageTakenFraction of every hit -- applied at the single
//     incoming-damage hook, AbilityDamageHooks.IncomingMultiplier, which the
//     melee, projectile, splash, spell and damage-over-time paths all call;
//   * regenerates regenPerSecondOfMax of his max HP each second, only once no hit
//     has landed for outOfCombatSeconds (this file's system, sim dt, fractional
//     carry kept on the component so peers agree);
//   * CANNOT BE SWARMED (2026-10-09): he loses at most damageCapPerSecondOfMax
//     of his max HP a second, however many hit him. A budget refills at that
//     rate (one second's worth at most) and every hit spends it; what the
//     budget cannot pay is lost. Weapon and spell hits pay it in
//     AbilityDamageHooks.ScaleIncoming, damage over time in
//     DamageOverTime.Accrue / ScaleTick -- each hit is capped exactly once;
//   * cannot be thrown or disabled -- Shardbound Fury / the death detonation
//     skip him, Sew Disorder / Blinding Glare / Recall the Codex skip him at
//     their source. Slows (SpellDebuff) work on him since 2026-11-06.
// His cleave, Shardbound Fury and death detonation are ShardboundFury.cs.
// The numbers are AscendedHero.asset (AscendedHeroConfig), never here.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;

namespace TheWaningBorder.Entities
{
    /// <summary>A hero bearing the Shardroot: Ascended. Added and removed by
    /// AscendedHeroSystem only (one entity, at most once per holding).</summary>
    public struct AscendedHero : IComponentData
    {
        /// <summary>Fractional regeneration not yet paid as a whole hit point.</summary>
        public float RegenCarry;
        /// <summary>Hit points he may still lose this second (the swarm cap).</summary>
        public float DamageBudget;
        /// <summary>1 once his killer has been paid the bounty (§3.1c).</summary>
        public byte BountyPaid;
        /// <summary>Sim time of the last hit on him: regeneration waits on it.</summary>
        public float LastHitAt;
        /// <summary>Seconds until Shardbound Fury may fire by itself again.</summary>
        public float FuryCooldown;
    }

    public static class AscendedHeroRules
    {
        /// <summary>Multiplier on damage the king takes while Shardbound; 1
        /// for anyone else. Read by AbilityDamageHooks.IncomingMultiplier.</summary>
        public static float IncomingMultiplier(EntityManager em, Entity victim)
        {
            if (!em.HasComponent<AscendedHero>(victim)) return 1f;
            return math.clamp(AscendedHeroConfig.I.damageTakenFraction, 0f, 1f);
        }

        /// <summary>One second's worth of the swarm cap for this Health; 0
        /// when the cap is off.</summary>
        public static float BudgetMax(Health hp)
            => math.max(0f, AscendedHeroConfig.I.damageCapPerSecondOfMax) * hp.Max;

        /// <summary>The part of an already-scaled hit the Shardbound King
        /// actually loses: what is left of this second's budget, which the
        /// hit then spends. Anyone else, or a cap of 0, takes it whole.</summary>
        public static int CapIncoming(EntityManager em, Entity victim, int damage)
        {
            if (damage <= 0 || !em.HasComponent<AscendedHero>(victim)) return damage;
            if (AscendedHeroConfig.I.damageCapPerSecondOfMax <= 0f) return damage;
            var k = em.GetComponentData<AscendedHero>(victim);
            int allowed = math.max(0, math.min(damage, (int)k.DamageBudget));
            k.DamageBudget -= allowed;
            k.LastHitAt = SimClock.Now;
            em.SetComponentData(victim, k);
            return allowed;
        }
    }

    /// <summary>Regeneration and disable immunity of the Shardbound King.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct AscendedHeroRegenSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            var kings = new NativeList<Entity>(Allocator.Temp);
            foreach (var (_, entity) in SystemAPI.Query<RefRO<AscendedHero>>()
                         .WithAll<Health>().WithEntityAccess())
                kings.Add(entity);
            if (kings.Length == 0) { kings.Dispose(); return; }

            float rate = math.max(0f, AscendedHeroConfig.I.regenPerSecondOfMax);
            float cap = math.max(0f, AscendedHeroConfig.I.damageCapPerSecondOfMax);
            for (int i = 0; i < kings.Length; i++)
            {
                var e = kings[i];
                var hp = em.GetComponentData<Health>(e);
                var k = em.GetComponentData<AscendedHero>(e);
                // The swarm cap's budget refills every tick, wounded or not.
                k.DamageBudget = math.min(cap * hp.Max, k.DamageBudget + cap * hp.Max * dt);
                // THE BOUNTY (§3.1c, 2026-10-09): whoever brings the
                // Shardbound King down is paid, once — the "stop Sauron"
                // moment. Credit is the last hostile faction to hurt him.
                if (hp.Value <= 0 && k.BountyPaid == 0)
                {
                    k.BountyPaid = 1;
                    if (TransientState.Active<LastDamagedByFaction>(em, e))
                    {
                        var killer = em.GetComponentData<LastDamagedByFaction>(e).Value;
                        var owner = em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value : killer;
                        if (killer != Faction.Border && killer != owner && Alliances.AreHostile(killer, owner))
                        {
                            TheWaningBorder.Economy.FactionEconomy.Add(em, killer, AscendedHeroConfig.I.Bounty,
                                TheWaningBorder.Economy.IncomeSource.Loot);
                            SimSignals.Notify(string.Format(TheWaningBorder.Core.Localization.Loc.T(
                                "{0} has slain the Ascended hero of {1} — a king's ransom is theirs!"), killer, owner));
                            UnityEngine.Debug.Log($"[Shardbound] {killer} killed {owner}'s Ascended hero -- bounty paid");
                        }
                    }
                }
                if (hp.Value <= 0 || hp.Value >= hp.Max
                    || TransientState.Active<DeathAnimationState>(em, e))
                {
                    em.SetComponentData(e, k);
                    continue;
                }
                // No regeneration in combat: enough force must visibly work.
                if (SimClock.Now - k.LastHitAt < AscendedHeroConfig.I.outOfCombatSeconds)
                {
                    em.SetComponentData(e, k);
                    continue;
                }
                k.RegenCarry += hp.Max * rate * dt;
                int whole = (int)k.RegenCarry;
                if (whole > 0)
                {
                    k.RegenCarry -= whole;
                    hp.Value = math.min(hp.Max, hp.Value + whole);
                    em.SetComponentData(e, hp);
                }
                em.SetComponentData(e, k);
            }
            kings.Dispose();
        }
    }
}
