// SectVenerationFervorSystem.cs
// Implements the Veneration sect's Lv I "Fervor" passive: each kill grants
// the KILLER unit a stack of +3% damage / +3% attack speed for 3 seconds.
// Stacks refresh on subsequent kills. Capped at 8 stacks to keep the
// multiplier finite (Lv I: max +24%).
//
// Hook: runs BEFORE DeathSystem (mirrors PillageSystem / CaravanDeathSystem
// pattern) so it can read LastAttackerEntity off freshly-dead entities
// before they get the death-animation marker. WithNone<DeathAnimationState,
// BuildingCollapseState> ensures each kill is processed at most once.
//
// task-063 phase 2b — reference implementation. Phase 2c+ add the rest of
// the per-sect Lv I lever effects against the same SectQuery read pattern.
//
// Location: Assets/Scripts/Systems/Sect/SectVenerationFervorSystem.cs

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectVenerationFervorSystem : ISystem
    {
        // Lv I tuning. Kept inline; Phase 4 will pull from SectDefinition.
        private const byte  MaxStacks    = 8;        // caps the multiplier at +24% / +24%
        private const float StackDuration = 3f;
        private const float DamageBonusPerStack = 0.03f;
        private const float AttackSpeedBonusPerStack = 0.03f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Iterate freshly-dead entities (same pattern as PillageSystem).
            foreach (var (health, lastAttacker, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastAttackerEntity>>()
                .WithNone<DeathAnimationState, BuildingCollapseState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Entity killer = lastAttacker.ValueRO.Value;
                if (killer == Entity.Null || !em.Exists(killer)) continue;
                if (!em.HasComponent<FactionTag>(killer)) continue;

                Faction killerFaction = em.GetComponentData<FactionTag>(killer).Value;

                // Gate on Veneration adoption (any lever level on the Passive
                // counts; Phase 4 reads the level to scale per-stack values).
                if (!SectQuery.IsAdoptedAtLeast(em, killerFaction,
                        SectConfig.Veneration, SectLeverKind.Passive)) continue;

                // Add or refresh the stack.
                if (em.HasComponent<VenerationFervor>(killer))
                {
                    var fervor = em.GetComponentData<VenerationFervor>(killer);
                    if (fervor.Stacks < MaxStacks) fervor.Stacks++;
                    fervor.TimeRemaining = StackDuration;
                    em.SetComponentData(killer, fervor);
                }
                else
                {
                    em.AddComponentData(killer, new VenerationFervor
                    {
                        Stacks = 1,
                        TimeRemaining = StackDuration,
                    });
                }

                // Mirror the bonus into SpellBuff so existing combat-pipeline
                // readers (CombatDamageHelper.ApplyBonusDamageOnHit reads
                // SpellBuff.DamageMultiplier) see the boost. SpellBuff is the
                // unified "outgoing damage modifier" channel; SpeedMultiplier
                // doesn't directly affect attack speed in the current code,
                // so the +attack-speed half of Fervor is handled separately
                // in Phase 4 when the attack-cooldown read points get touched.
                int newStacks = em.GetComponentData<VenerationFervor>(killer).Stacks;
                float dmgMult = 1f + DamageBonusPerStack * newStacks;
                if (em.HasComponent<SpellBuff>(killer))
                {
                    var buff = em.GetComponentData<SpellBuff>(killer);
                    // Take the max so Fervor doesn't clobber a stronger
                    // existing buff (e.g. an ally's Crystal Communion zone).
                    if (dmgMult > buff.DamageMultiplier) buff.DamageMultiplier = dmgMult;
                    if (StackDuration > buff.TimeRemaining) buff.TimeRemaining = StackDuration;
                    em.SetComponentData(killer, buff);
                }
                else
                {
                    em.AddComponentData(killer, new SpellBuff
                    {
                        DamageMultiplier = dmgMult,
                        TimeRemaining    = StackDuration,
                    });
                }
            }

            // Tick down VenerationFervor — when TimeRemaining hits 0, remove it.
            // SpellBuff has its own ticker (SpellBuffSystem) so we don't tick that.
            float dt = SystemAPI.Time.DeltaTime;
            var ecb = new Unity.Collections.NativeList<Entity>(8, Unity.Collections.Allocator.Temp);
            foreach (var (fervor, entity) in SystemAPI
                .Query<RefRW<VenerationFervor>>()
                .WithEntityAccess())
            {
                fervor.ValueRW.TimeRemaining -= dt;
                if (fervor.ValueRO.TimeRemaining <= 0f)
                    ecb.Add(entity);
            }
            for (int i = 0; i < ecb.Length; i++)
            {
                if (em.Exists(ecb[i]) && em.HasComponent<VenerationFervor>(ecb[i]))
                    em.RemoveComponent<VenerationFervor>(ecb[i]);
            }
            ecb.Dispose();
        }
    }
}
