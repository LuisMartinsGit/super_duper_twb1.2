// ShardrootEmpowerment.cs
// THE TEMPLE CHOICE EMPOWERS THE CIVILIZATION (docs/Design/Curse_And_Shardroot.md
// § 3.1b, 2026-10-07). While a faction's finished, living Temple of Ridan
// holds the enshrined Shardroot (ShardrootTag on the Temple, ShardrootStored
// .Amount > 0), every UNIT of that faction deals more damage and takes less,
// on top of the sect-cooldown cut.
//
//   ShardrootEmpowermentSystem  rebuilds the empowered-faction bitmask from
//                               scratch every tick (a plain scan, no hash
//                               iteration; the result is order-free) and
//                               copies the two numbers out of the config.
//   ShardrootEmpowerment        the read side, used by the damage hook
//                               AbilityDamageHooks: OutgoingMultiplier for the
//                               attacker, IncomingMultiplier for the victim.
//
// The mask is stamped with SimCadence.Epoch, so a value left over from a
// previous match in the same process is never read (the second-match desync
// class). Numbers: ShardrootEmpowerment.asset, never here.

using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Entities
{
    public static class ShardrootEmpowerment
    {
        static uint _mask;
        static int _epoch = int.MinValue;
        static float _damageBonus;
        static float _protection;

        /// <summary>Is <paramref name="f"/>'s Temple holding the Shardroot?</summary>
        public static bool IsEmpowered(Faction f)
        {
            if (_epoch != SimCadence.Epoch) return false;
            int i = (int)f;
            return i >= 0 && i < 32 && (_mask & (1u << i)) != 0;
        }

        /// <summary>Damage multiplier for a hit dealt by <paramref name="attacker"/>:
        /// 1 + damageBonus for a unit of an empowered faction, otherwise 1.</summary>
        public static float OutgoingMultiplier(EntityManager em, Entity attacker)
        {
            if (_mask == 0 || attacker == Entity.Null || !em.Exists(attacker)) return 1f;
            if (!em.HasComponent<UnitTag>(attacker) || !em.HasComponent<FactionTag>(attacker)) return 1f;
            return IsEmpowered(em.GetComponentData<FactionTag>(attacker).Value) ? 1f + _damageBonus : 1f;
        }

        /// <summary>Damage multiplier for a hit taken by <paramref name="victim"/>:
        /// 1 - protection for a unit of an empowered faction, otherwise 1.</summary>
        public static float IncomingMultiplier(EntityManager em, Entity victim)
        {
            if (_mask == 0 || victim == Entity.Null || !em.Exists(victim)) return 1f;
            if (!em.HasComponent<UnitTag>(victim) || !em.HasComponent<FactionTag>(victim)) return 1f;
            return IsEmpowered(em.GetComponentData<FactionTag>(victim).Value) ? 1f - _protection : 1f;
        }

        internal static void Publish(uint mask, float damageBonus, float protection)
        {
            _mask = mask;
            _epoch = SimCadence.Epoch;
            _damageBonus = damageBonus;
            _protection = protection;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ShardrootEmpowermentSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            ShardrootEmpowerment.Publish(0u, 0f, 0f);
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            uint mask = 0u;
            foreach (var (stored, hp, fac, entity) in SystemAPI
                .Query<RefRO<ShardrootStored>, RefRO<Health>, RefRO<FactionTag>>()
                .WithAll<TempleOfRidanTag, ShardrootTag>()
                .WithEntityAccess())
            {
                if (stored.ValueRO.Amount <= 0 || hp.ValueRO.Value <= 0) continue;
                if (em.HasComponent<UnderConstruction>(entity)) continue;
                int i = (int)fac.ValueRO.Value;
                if (fac.ValueRO.Value == Faction.Border || i < 0 || i >= 32) continue;
                mask |= 1u << i;
            }

            if (mask == 0u) { ShardrootEmpowerment.Publish(0u, 0f, 0f); return; }
            var cfg = ShardrootEmpowermentConfig.I;
            ShardrootEmpowerment.Publish(mask,
                Unity.Mathematics.math.max(0f, cfg.damageBonus),
                Unity.Mathematics.math.clamp(cfg.protection, 0f, 1f));
        }
    }
}
