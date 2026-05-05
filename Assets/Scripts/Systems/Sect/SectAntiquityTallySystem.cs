// SectAntiquityTallySystem.cs
// Implements Antiquity's Lv I "Tally of the Lost" passive: each kill the
// attacker makes is logged into a per-UnitClass counter on the attacker
// (the AntiquityKills component). CombatDamageHelper reads those counters
// on every hit and grants +1% per logged kill of the *target's* class,
// capped at +10% (Lv I). Phase 4 raises both the per-kill bonus and the
// cap.
//
// Hook: same death-event pattern as SectVenerationFervorSystem. Runs
// before DeathSystem, scans entities at Health <= 0 with no death-marker,
// reads LastAttackerEntity, increments the killer's class counter for
// the dead unit's UnitClass.
//
// task-063 phase 2e.
//
// Location: Assets/Scripts/Systems/Sect/SectAntiquityTallySystem.cs

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectAntiquityTallySystem : ISystem
    {
        // Lv I cap. Phase 4: 16 / 25 for Lv II / Lv III.
        private const byte KillCap = 10;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (health, lastAttacker, victimUnit, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastAttackerEntity>, RefRO<UnitTag>>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Entity killer = lastAttacker.ValueRO.Value;
                if (killer == Entity.Null || !em.Exists(killer)) continue;
                if (!em.HasComponent<FactionTag>(killer)) continue;
                if (!em.HasComponent<UnitTag>(killer)) continue;

                Faction killerFaction = em.GetComponentData<FactionTag>(killer).Value;
                if (em.HasComponent<FactionTag>(entity)
                    && em.GetComponentData<FactionTag>(entity).Value == killerFaction) continue;

                if (!SectQuery.IsAdoptedAtLeast(em, killerFaction,
                        SectConfig.Antiquity, SectLeverKind.Passive)) continue;

                var victimClass = victimUnit.ValueRO.Class;

                // Stamp lazily on first relevant kill.
                if (!em.HasComponent<AntiquityKills>(killer))
                    em.AddComponentData(killer, new AntiquityKills());

                var kills = em.GetComponentData<AntiquityKills>(killer);
                Increment(ref kills, victimClass);
                em.SetComponentData(killer, kills);
            }
        }

        private static void Increment(ref AntiquityKills k, UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Melee:   if (k.Melee   < KillCap) k.Melee++;   break;
                case UnitClass.Ranged:  if (k.Ranged  < KillCap) k.Ranged++;  break;
                case UnitClass.Siege:   if (k.Siege   < KillCap) k.Siege++;   break;
                case UnitClass.Support: if (k.Support < KillCap) k.Support++; break;
                case UnitClass.Magic:   if (k.Magic   < KillCap) k.Magic++;   break;
                case UnitClass.Economy: if (k.Economy < KillCap) k.Economy++; break;
                case UnitClass.Miner:   if (k.Miner   < KillCap) k.Miner++;   break;
                case UnitClass.Scout:   if (k.Scout   < KillCap) k.Scout++;   break;
            }
        }

        /// <summary>
        /// Read the per-class kill count for a unit. Returns 0 if the attacker
        /// has no AntiquityKills component yet. Public so CombatDamageHelper
        /// can fold the bonus in without duplicating the switch.
        /// </summary>
        public static byte KillsAgainst(in AntiquityKills k, UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Melee:   return k.Melee;
                case UnitClass.Ranged:  return k.Ranged;
                case UnitClass.Siege:   return k.Siege;
                case UnitClass.Support: return k.Support;
                case UnitClass.Magic:   return k.Magic;
                case UnitClass.Economy: return k.Economy;
                case UnitClass.Miner:   return k.Miner;
                case UnitClass.Scout:   return k.Scout;
                default: return 0;
            }
        }
    }
}
