// File: Assets/Scripts/Entities/Units/Veilstinger.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstinger unit - dual-laser ranged glass cannon for the Crystal faction.
    /// Fires at up to two targets simultaneously. Very fragile but high damage.
    /// Uses VeilstingerState instead of ArcherState for dual-target tracking.
    /// No population cost - crystal faction uses crystal resource economy.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Veilstinger
    {
        /// <summary>
        /// Create Veilstinger using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Veilstinger using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = VeilstingerHP;
            float speed = VeilstingerSpeed;
            float damage = VeilstingerDamage;
            float los = VeilstingerLoS;
            float minRange = VeilstingerMinRange;
            float maxRange = VeilstingerMaxRange;
            float radius = VeilstingerRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Veilstinger", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = VeilstingerPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<CrystalTag>(entity);
            creator.AddComponent<CrystalUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new CrystalResourceValue { BuildCost = VeilstingerBuildCost });

            // Veilstinger-specific dual-target state
            creator.AddComponent(entity, new VeilstingerState
            {
                Target1 = Entity.Null,
                Target2 = Entity.Null,
                AimTimer = 0,
                AimTimeRequired = VeilstingerAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsFiring = 0,
                NextGun = 0
            });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 3, Ranged = 2, Siege = 1, Magic = 2 });

            // Pre-allocate DesiredDestination — same race-fix as Crystalling.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            return entity;
        }
    }
}
