using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Brandbreaker unit - Hollow Brand sect siege infantry.
    /// Anti-structure melee unit with heavy armor and siege damage type.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via
    /// IEntityCreator instead of duplicating the EntityManager / EntityCommandBuffer
    /// bodies.
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
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float cooldown = DefaultAttackCooldown;
            float radius = DefaultRadius;

            if (TechCatalog.TryGetUnit("Sect_Brandbreaker", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            creator.AddComponent<SiegeTag>(entity);
            creator.AddComponent<SectUniqueUnitTag>(entity);
            creator.AddComponent(entity, new UnitAbility { Id = AbilityId.WarCry, CooldownDuration = 18f, CooldownRemaining = 0f, Range = 0f });

            return entity;
        }
    }
}
