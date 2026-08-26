using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Swordsman unit - Alanthor line infantry (Garrison Lv 1 unlock).
    /// Generalist: no anti-cavalry bonus, better all-round stats than the
    /// Spearman. Trained under id "Alanthor_Swordsman" (SO gates it at
    /// minBuildingLevel 1); also serves as UnitFactory's unknown-id fallback.
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Swordsman
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "Alanthor_Swordsman" — 145 HP / 14 dmg / 1.4 cd / 1.0 range).
        private const float DefaultHP = 145f;
        private const float DefaultSpeed = 5.5f;
        private const float DefaultDamage = 14f;
        private const float DefaultLoS = 16f;
        private const float DefaultAttackCooldown = 1.4f;
        private const float DefaultRadius = 0.5f;
        private const int PresentationID = 201;

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

            if (TechCatalog.TryGetUnit("Alanthor_Swordsman", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
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
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
