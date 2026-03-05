// File: Assets/Scripts/Entities/Units/Scout.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Scout unit - fast reconnaissance unit.
    /// High movement speed and line of sight, low combat stats.
    /// </summary>
    public static class Scout
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 40f;
        private const float DefaultSpeed = 6f;
        private const float DefaultDamage = 3f;
        private const float DefaultLoS = 20f;
        private const int PresentationID = 206;

        /// <summary>
        /// Create Scout using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Scout", out var def))
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
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(LineOfSight),
                typeof(Target),
                typeof(Radius),
                typeof(PopulationCost)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Scout });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            em.SetComponentData(entity, new PopulationCost { Amount = 1 });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Melee });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }

        /// <summary>
        /// Create Scout using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Scout", out var def))
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
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Scout });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = 0.5f });
            ecb.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
}