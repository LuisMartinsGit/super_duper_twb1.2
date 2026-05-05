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
        // Stack count + duration are shared across levels; Phase 4 scales
        // only the per-stack bonus values. Lv III adds a movement-speed
        // stack — that lever is deferred to Phase 5 since the move-speed
        // read points aren't yet wired through SpellBuff.SpeedMultiplier.
        private const byte  MaxStacks     = 8;        // caps the multiplier at +(stack% × 8)
        private const float StackDuration = 3f;       // Lv III bumps to 4s — applied below.

        private static float DamageBonusPerStackFor(byte level) => level switch
        {
            2 => 0.05f,
            3 => 0.07f,
            _ => 0.03f,
        };
        private static float StackDurationFor(byte level) => level == 3 ? 4f : StackDuration;

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

                byte level = SectQuery.LevelOf(em, killerFaction,
                    SectConfig.Veneration, SectLeverKind.Passive);
                if (level == 0) continue;
                float perStack = DamageBonusPerStackFor(level);
                float duration = StackDurationFor(level);

                // Add or refresh the stack.
                if (em.HasComponent<VenerationFervor>(killer))
                {
                    var fervor = em.GetComponentData<VenerationFervor>(killer);
                    if (fervor.Stacks < MaxStacks) fervor.Stacks++;
                    fervor.TimeRemaining = duration;
                    em.SetComponentData(killer, fervor);
                }
                else
                {
                    em.AddComponentData(killer, new VenerationFervor
                    {
                        Stacks = 1,
                        TimeRemaining = duration,
                    });
                }

                // Mirror the bonus into SpellBuff so existing combat-pipeline
                // readers (CombatDamageHelper.ApplyBonusDamageOnHit reads
                // SpellBuff.DamageMultiplier) see the boost. SpellBuff is the
                // unified "outgoing damage modifier" channel; the +attack-
                // speed and Lv III +move halves remain Phase 5 work since
                // those read points aren't wired through SpellBuff.
                int newStacks = em.GetComponentData<VenerationFervor>(killer).Stacks;
                float dmgMult = 1f + perStack * newStacks;
                if (em.HasComponent<SpellBuff>(killer))
                {
                    var buff = em.GetComponentData<SpellBuff>(killer);
                    if (dmgMult > buff.DamageMultiplier) buff.DamageMultiplier = dmgMult;
                    if (duration > buff.TimeRemaining) buff.TimeRemaining = duration;
                    em.SetComponentData(killer, buff);
                }
                else
                {
                    em.AddComponentData(killer, new SpellBuff
                    {
                        DamageMultiplier = dmgMult,
                        TimeRemaining    = duration,
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
