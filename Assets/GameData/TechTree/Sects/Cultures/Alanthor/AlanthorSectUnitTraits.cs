// AlanthorSectUnitTraits.cs
// The Alanthor sect units' signature traits (docs/Design/Sects.md §4):
//
//   Artificer    Stasis Bolt      its hit slows the target's move and attack
//   Scar Guard   Borrowed Breath  more damage the lower its own HP
//   Sallyguard   Stored Strike    first blow after charging out of combat x2
//                                 (+ bonus vs siege from its SO bonusVsTags)
//   Collector    Run Down         bonus vs slowed enemies and enemies < 50% HP
//
// plus the one incoming-damage mark a hero lays on enemies:
//
//   Catalogued (Archivist of Hours L1)  slowed enemies near it take +20%
//
// OnHit is called from CombatDamageHelper.ApplyBonusDamageOnHit, the single
// on-hit door melee AND projectile hits pass through, so every trait reads
// the same replicated state on every peer.

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Sect
{
    public static class AlanthorSectUnitTraits
    {
        /// <summary>Is <paramref name="e"/> slowed (a live move-speed debuff)?
        /// The one definition Catalogued and Run Down share.</summary>
        public static bool IsSlowed(EntityManager em, Entity e)
            => TransientState.Active<SpellDebuff>(em, e)
               && em.GetComponentData<SpellDebuff>(e).SpeedReduction > 0f;

        public static int OnHit(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, int damage)
        {
            float mult = 1f;

            if (em.HasComponent<BorrowedBreathTrait>(attacker) && em.HasComponent<Health>(attacker))
            {
                var hp = em.GetComponentData<Health>(attacker);
                if (hp.Max > 0)
                {
                    float missing = 1f - math.saturate((float)hp.Value / hp.Max);
                    mult *= 1f + em.GetComponentData<BorrowedBreathTrait>(attacker).MaxBonus * missing;
                }
            }

            if (em.HasComponent<StoredStrike>(attacker))
            {
                var ss = em.GetComponentData<StoredStrike>(attacker);
                if (ss.ChargeSeconds > 0f && ss.Charge >= ss.ChargeSeconds)
                {
                    mult *= ss.Multiplier;
                    ss.Charge = 0f;
                    ss.IdleTimer = 0f;
                    ecb.SetComponent(attacker, ss);
                }
            }

            if (em.HasComponent<RunDownTrait>(attacker))
            {
                bool wounded = em.HasComponent<Health>(target)
                    && em.GetComponentData<Health>(target).Max > 0
                    && em.GetComponentData<Health>(target).Value * 2 < em.GetComponentData<Health>(target).Max;
                if (wounded || IsSlowed(em, target))
                    mult *= 1f + em.GetComponentData<RunDownTrait>(attacker).Bonus;
            }

            if (TransientState.Active<CatalogedMark>(em, target))
                mult *= 1f + em.GetComponentData<CatalogedMark>(target).Bonus;

            if (em.HasComponent<StasisBoltTrait>(attacker) && em.HasComponent<UnitTag>(target))
                ApplyStasis(em, ecb, target, em.GetComponentData<StasisBoltTrait>(attacker));

            return mult == 1f ? damage : (int)math.round(damage * mult);
        }

        /// <summary>Slow a unit's movement (SpellDebuff) and its attacks (a
        /// sub-1 SectHaste) for the trait's duration, refreshing an existing
        /// slow rather than stacking.</summary>
        public static void ApplyStasis(EntityManager em, EntityCommandBuffer ecb, Entity target,
            StasisBoltTrait t)
        {
            var d = TransientState.Active<SpellDebuff>(em, target)
                ? em.GetComponentData<SpellDebuff>(target) : default;
            d.SpeedReduction = math.max(d.SpeedReduction, t.MoveSlow);
            d.TimeRemaining = math.max(d.TimeRemaining, t.Seconds);
            TransientState.Set(ecb, target, d);

            // A hasted target (Blood Rain, Sovereign Hour) keeps its haste;
            // the bolt only slows attacks that are not already sped up.
            if (!TransientState.Active<SectHaste>(em, target)
                || em.GetComponentData<SectHaste>(target).Multiplier < 1f)
                TransientState.Set(ecb, target, new SectHaste { Multiplier = t.AttackSlow, TimeRemaining = t.Seconds });
        }
    }

    /// <summary>
    /// Charges every Sallyguard's Stored Strike while it has no target, after
    /// a short out-of-combat delay. A Warden (sect hero, Drillmaster at L1)
    /// within 15 m doubles the rate. Sim-time, replicated state only.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class StoredStrikeChargeSystem : SystemBase
    {
        public const float DrillmasterRadius = 15f;
        public const float DrillmasterRate = 2f;

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            // Wardens on the field (few — one per faction at most).
            var wardens = new Unity.Collections.NativeList<float4>(4, Unity.Collections.Allocator.Temp);
            foreach (var (tag, tr, fac) in SystemAPI
                         .Query<RefRO<SectHeroTag>, RefRO<Unity.Transforms.LocalTransform>, RefRO<FactionTag>>())
            {
                if (tag.ValueRO.SectIndex != (byte)TheWaningBorder.Economy.SectConfig.IndexOf(TheWaningBorder.Economy.SectConfig.Fortitude)) continue;
                wardens.Add(new float4(tr.ValueRO.Position, (float)fac.ValueRO.Value));
            }

            foreach (var (ss, target, tr, fac) in SystemAPI
                         .Query<RefRW<StoredStrike>, RefRO<Target>, RefRO<Unity.Transforms.LocalTransform>, RefRO<FactionTag>>())
            {
                if (target.ValueRO.Value != Entity.Null)
                {
                    ss.ValueRW.IdleTimer = 0f;
                    continue;
                }
                ss.ValueRW.IdleTimer += dt;
                if (ss.ValueRO.IdleTimer < ss.ValueRO.OutOfCombatDelay) continue;
                if (ss.ValueRO.Charge >= ss.ValueRO.ChargeSeconds) continue;

                float rate = 1f;
                for (int i = 0; i < wardens.Length; i++)
                {
                    if ((int)wardens[i].w != (int)fac.ValueRO.Value) continue;
                    if (math.distancesq(wardens[i].xz, tr.ValueRO.Position.xz)
                        <= DrillmasterRadius * DrillmasterRadius) { rate = DrillmasterRate; break; }
                }
                ss.ValueRW.Charge = math.min(ss.ValueRO.ChargeSeconds, ss.ValueRO.Charge + dt * rate);
            }
            wardens.Dispose();
        }
    }
}
