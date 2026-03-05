// File: Assets/Scripts/Entities/Units/Brandbreaker.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Brandbreaker unit - Hollow Brand sect siege infantry.
    /// Anti-structure melee unit with heavy armor and siege damage type.
    /// </summary>
    public static class Brandbreaker
    {
        private const float DefaultHP = 150f;
        private const float DefaultSpeed = 4.0f;
        private const float DefaultDamage = 12f;
        private const float DefaultLoS = 10f;
        private const float DefaultAttackCooldown = 1.5f;
        private const float DefaultRadius = 0.5f;
        private const int PresentationID = 379;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float cooldown = DefaultAttackCooldown;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Sect_Brandbreaker", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(UnitTag), typeof(Health), typeof(MoveSpeed), typeof(Damage),
                typeof(AttackCooldown), typeof(LineOfSight), typeof(Target),
                typeof(Radius), typeof(PopulationCost)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Siege });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = radius });
            em.SetComponentData(entity, new PopulationCost { Amount = 1 });

            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Siege });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            em.AddComponentData(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            em.AddComponent<SiegeTag>(entity);
            em.AddComponent<SectUniqueUnitTag>(entity);

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float cooldown = DefaultAttackCooldown;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Sect_Brandbreaker", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = radius });
            ecb.AddComponent(entity, new PopulationCost { Amount = 1 });

            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            ecb.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            ecb.AddComponent<SiegeTag>(entity);
            ecb.AddComponent<SectUniqueUnitTag>(entity);

            return entity;
        }
    }
}
