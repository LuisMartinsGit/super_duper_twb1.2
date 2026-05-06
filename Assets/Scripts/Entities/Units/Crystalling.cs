// File: Assets/Scripts/Entities/Units/Crystalling.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystalling unit - fast, weak melee crystal swarm unit.
    /// Cheap crystal-cost melee infantry for the Crystal faction (Faction.Curse).
    /// No population cost - crystal faction uses crystal resource economy.
    /// </summary>
    public static class Crystalling
    {
        /// <summary>
        /// Create Crystalling using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = CrystallingHP;
            float speed = CrystallingSpeed;
            float damage = CrystallingDamage;
            float los = CrystallingLoS;
            float cooldown = CrystallingAttackCooldown;
            float radius = CrystallingRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Crystalling", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(CrystalTag),
                typeof(CrystalUnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(AttackCooldown),
                typeof(LineOfSight),
                typeof(Target),
                typeof(Radius),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = CrystallingPresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Melee });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = radius });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = CrystallingBuildCost });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Siege });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            em.AddComponentData(entity, new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 1 });

            return entity;
        }

        /// <summary>
        /// Create Crystalling using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = CrystallingHP;
            float speed = CrystallingSpeed;
            float damage = CrystallingDamage;
            float los = CrystallingLoS;
            float cooldown = CrystallingAttackCooldown;
            float radius = CrystallingRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Crystalling", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = CrystallingPresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalUnitTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = radius });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = CrystallingBuildCost });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            ecb.AddComponent(entity, new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 1 });

            return entity;
        }
    }
}
