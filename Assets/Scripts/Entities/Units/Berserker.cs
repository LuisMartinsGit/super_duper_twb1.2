// File: Assets/Scripts/Entities/Units/Berserker.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Berserker unit — converted from miners at Fiendstone Keep.
    /// High attack and speed, no armor, low HP, impossible to heal.
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Berserker
    {
        private const float DefaultHP = 80f;
        private const float DefaultSpeed = 5.8f;
        private const float DefaultDamage = 14f;
        private const float DefaultLoS = 10f;
        private const float DefaultAttackCooldown = 1.0f;
        private const float DefaultRadius = 0.5f;
        private const int PresentationID = 210;

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

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_Berserker", out var def))
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
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent<BerserkerTag>(entity);
            creator.AddComponent<UnhealableTag>(entity);

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 2, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
