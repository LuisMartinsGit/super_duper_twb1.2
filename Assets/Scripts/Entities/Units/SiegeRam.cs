// File: Assets/Scripts/Entities/Units/SiegeRam.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Siege Ram unit - Feraldis culture melee siege unit.
    /// Anti-structure melee siege weapon. Very high HP, slow, devastating vs buildings.
    /// Uses melee pattern with SiegeTag and extended melee range (2.5).
    /// </summary>
    public static class SiegeRam
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 300f;
        private const float DefaultSpeed = 3.0f;
        private const float DefaultDamage = 34f;
        private const float DefaultLoS = 10f;
        private const float DefaultAttackCooldown = 3.0f;
        private const float DefaultRadius = 1.0f;
        private const int PresentationID = 340;

        /// <summary>
        /// Create SiegeRam using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float cooldown = DefaultAttackCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_SiegeRam", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(SiegeTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(AttackCooldown),
                typeof(LineOfSight),
                typeof(Target),
                typeof(Radius),
                typeof(PopulationCost)
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
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new PopulationCost { Amount = 2 });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Siege });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            em.AddComponentData(entity, new Defense { Melee = 4, Ranged = 2, Siege = 6, Magic = 0 });

            return entity;
        }

        /// <summary>
        /// Create SiegeRam using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float cooldown = DefaultAttackCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_SiegeRam", out var def))
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
            ecb.AddComponent<SiegeTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent(entity, new PopulationCost { Amount = 2 });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            ecb.AddComponent(entity, new Defense { Melee = 4, Ranged = 2, Siege = 6, Magic = 0 });

            return entity;
        }
    }
}
