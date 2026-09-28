// ShieldBarSystem.cs
// Veilstone+ tier units carry a shield (spec §4.2). The rule is
// docs/Design/Combat_Pacing.md, "Shield points are hit points": a shield is
// extra HP that EVERY source of damage spends first — melee, ranged,
// projectiles, splash, spells, damage over time, burning ground, bleed,
// curse exposure. Only what the shield cannot cover reaches Health.
//
// Two halves:
//   * ShieldDamage.Absorb — called AT THE POINT OF DAMAGE by the shared
//     damage paths (melee, cleave, projectile hit + splash, reflect, and
//     DamageOverTime.Commit, which is also SpellDamage's door). The shield is
//     spent before Health is touched, so Health never dips for absorbed
//     damage: no death check, kill-credit or death-reaction system can see a
//     shielded unit at 0 HP from a hit its shield covered, and overkill is
//     never lost to a clamp.
//   * ShieldDamage.RefundUnobserved — the BACKSTOP for any stray direct
//     Health write that has not been routed through Absorb yet. It compares
//     Health to LastObservedHealth and refunds the drop out of the shield.
//     It runs here AND at the top of DeathSystem's death check (the only
//     point guaranteed to follow every damage source in the frame), so even
//     a stray write can never kill a unit whose shield should have held.
//     It is exact for an unclamped write; a write that clamped HP at 0 has
//     already thrown its overkill away, which is why the real damage paths
//     go through Absorb instead.
//
// This system itself owns the lifecycle: sync ShieldBar presence / Max to
// the tier (+ siege AuraShieldBoost), the backstop pass, and regen (once per
// whole second after the regen-delay gate).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// The one place damage meets a shield. Deterministic integer math, plain
    /// non-structural EntityManager writes — safe inside a SystemAPI.Query
    /// iteration that does not itself hold ShieldBar.
    /// </summary>
    public static class ShieldDamage
    {
        /// <summary>
        /// Spend <paramref name="damage"/> against the victim's shield first and
        /// return what is left for Health. A victim with no ShieldBar returns
        /// the damage unchanged. Any hit (absorbed or not) resets the regen
        /// gate. The overflow is recorded as already-observed, so the backstop
        /// never mistakes it for a stray write.
        /// </summary>
        public static int Absorb(EntityManager em, Entity victim, int damage)
        {
            if (damage <= 0 || !em.HasComponent<ShieldBar>(victim)) return damage;

            var sb = em.GetComponentData<ShieldBar>(victim);
            int absorbed = math.min(damage, math.max(0, sb.Current));
            int overflow = damage - absorbed;
            sb.Current -= absorbed;
            sb.RegenDelayTimer = 0f;
            sb.LastObservedHealth -= overflow;
            em.SetComponentData(victim, sb);
            return overflow;
        }

        private static readonly ComponentType[] QT_Shielded =
        {
            ComponentType.ReadWrite<ShieldBar>(),
            ComponentType.ReadWrite<Health>(),
            ComponentType.Exclude<DeathAnimationState>(),
            ComponentType.Exclude<BuildingCollapseState>(),
        };
        private static CachedEntityQuery QC_Shielded;

        /// <summary>
        /// Backstop: refund any Health drop since the last observation that did
        /// not go through <see cref="Absorb"/>, out of the shield, and restamp
        /// LastObservedHealth. Writes Health only on a refund, so Health change
        /// filters downstream stay quiet.
        /// </summary>
        public static void RefundUnobserved(EntityManager em)
        {
            var q = QC_Shielded.Get(em, QT_Shielded);
            if (q.IsEmpty) return;

            using var ents = q.ToEntityArray(Allocator.Temp);
            using var shields = q.ToComponentDataArray<ShieldBar>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var sb = shields[i];
                var hp = hps[i];

                int delta = sb.LastObservedHealth - hp.Value;
                bool hpChanged = false;
                if (delta > 0)
                {
                    int absorbed = math.min(delta, math.max(0, sb.Current));
                    if (absorbed > 0)
                    {
                        sb.Current -= absorbed;
                        hp.Value = math.min(hp.Max, hp.Value + absorbed);
                        hpChanged = true;
                    }
                    // A hit with no shield left still closes the regen gate.
                    sb.RegenDelayTimer = 0f;
                }

                if (delta == 0) continue;
                sb.LastObservedHealth = hp.Value;
                em.SetComponentData(ents[i], sb);
                if (hpChanged) em.SetComponentData(ents[i], hp);
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EquipmentTierSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class ShieldBarSystem : SystemBase
    {
        // Shield regen lands once per whole second (match-phased). A per-frame
        // ceil added at least 1 point every frame, refilling the shield in
        // about a second and swallowing every damage-over-time tick.
        private SimCadence.Periodic _regenTimer;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ── Phase 1: sync ShieldBar presence + Max to current tier ──
            foreach (var (appliedRO, healthRO, entity) in SystemAPI
                .Query<RefRO<UnitEquipmentApplied>, RefRO<Health>>()
                .WithEntityAccess())
            {
                int target = EquipmentTierConfig.ShieldBarMax(appliedRO.ValueRO.Value);

                // Siege Veilstone+ aura boosts every ally's effective Max (spec §4.3).
                if (em.HasComponent<AuraShieldBoost>(entity))
                    target += em.GetComponentData<AuraShieldBoost>(entity).Amount;

                bool hasShield = em.HasComponent<ShieldBar>(entity);

                if (target <= 0)
                {
                    if (hasShield) ecb.RemoveComponent<ShieldBar>(entity);
                    continue;
                }

                if (!hasShield)
                {
                    ecb.AddComponent(entity, new ShieldBar
                    {
                        Current = target,
                        Max = target,
                        LastObservedHealth = healthRO.ValueRO.Value,
                        RegenDelayTimer = 0f,
                    });
                }
                else
                {
                    var sb = em.GetComponentData<ShieldBar>(entity);
                    if (sb.Max != target)
                    {
                        sb.Max = target;
                        if (sb.Current > sb.Max) sb.Current = sb.Max;
                        em.SetComponentData(entity, sb);
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();

            // ── Phase 2: backstop for stray direct Health writes ──
            // DeathSystem runs the same pass again right before its death check.
            ShieldDamage.RefundUnobserved(em);

            // ── Phase 3: regen (gated by RegenDelay since the last hit) ──
            bool regenDue = _regenTimer.Due(dt, 1f);
            int add = (int)EquipmentTierConfig.ShieldBarRegenPerSecond;
            foreach (var shieldRW in SystemAPI.Query<RefRW<ShieldBar>>())
            {
                ref var sb = ref shieldRW.ValueRW;
                if (sb.Current >= sb.Max) continue;
                sb.RegenDelayTimer += dt;
                if (regenDue && add > 0
                    && sb.RegenDelayTimer >= EquipmentTierConfig.ShieldBarRegenDelay)
                    sb.Current = math.min(sb.Max, sb.Current + add);
            }
        }
    }
}
