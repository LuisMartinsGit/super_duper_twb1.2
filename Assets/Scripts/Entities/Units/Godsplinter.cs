// File: Assets/Scripts/Entities/Units/Godsplinter.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Godsplinter unit - massive crystal siege monster.
    /// Hybrid siege/ranged combat: melee siege damage to buildings (2x),
    /// multi-target laser barrage at range. Slow but extremely durable.
    /// No population cost - crystal faction uses crystal resource economy.
    /// </summary>
    public static class Godsplinter
    {
        private const float DefaultHP = 1200f;
        private const float DefaultSpeed = 1.8f;
        private const float DefaultDamage = 40f;
        private const float DefaultLoS = 20f;
        private const float DefaultRadius = 1.5f;
        private const float DefaultSiegeRange = 4f;
        private const float DefaultLaserRange = 22f;
        private const int DefaultLaserMaxTargets = 4;
        private const int PresentationID = 322;

        /// <summary>
        /// Create Godsplinter using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Godsplinter", out var def))
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
                typeof(CrystalTag),
                typeof(CrystalUnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(LineOfSight),
                typeof(GodsplinterState),
                typeof(Target),
                typeof(Radius),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Siege });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = radius });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = 350 });

            // Godsplinter-specific siege/laser state
            em.SetComponentData(entity, new GodsplinterState
            {
                LaserCooldownTimer = 0,
                SiegeCooldownTimer = 0,
                SiegeRange = DefaultSiegeRange,
                LaserRange = DefaultLaserRange,
                LaserMaxTargets = DefaultLaserMaxTargets,
                IsSieging = 0
            });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Siege });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            em.AddComponentData(entity, new Defense { Melee = 10, Ranged = 8, Siege = 5, Magic = 5 });

            return entity;
        }

        /// <summary>
        /// Create Godsplinter using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Godsplinter", out var def))
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
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalUnitTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = radius });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = 350 });

            // Godsplinter-specific siege/laser state
            ecb.AddComponent(entity, new GodsplinterState
            {
                LaserCooldownTimer = 0,
                SiegeCooldownTimer = 0,
                SiegeRange = DefaultSiegeRange,
                LaserRange = DefaultLaserRange,
                LaserMaxTargets = DefaultLaserMaxTargets,
                IsSieging = 0
            });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            ecb.AddComponent(entity, new Defense { Melee = 10, Ranged = 8, Siege = 5, Magic = 5 });

            return entity;
        }
    }
}
