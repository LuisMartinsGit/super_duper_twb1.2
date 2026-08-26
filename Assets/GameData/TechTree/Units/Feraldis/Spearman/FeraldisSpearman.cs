using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Spearman — the Age 0 line-infantry chassis traded toward
    /// aggression: less HP, more attack (design 2026-08-05).
    ///
    /// Deliberately its OWN factory class rather than another id routed at
    /// the shared <see cref="Spearman"/> creator: that one reads the
    /// "Runai_Spearman" def, so reusing it would have silently given the
    /// Feraldis unit Runai stats.
    /// </summary>
    public static class FeraldisSpearman
    {
        private const float DefaultHP = 100f;
        private const float DefaultSpeed = 5.5f;
        private const float DefaultDamage = 13f;
        private const float DefaultLoS = 16f;
        private const float DefaultAttackCooldown = 1.5f;
        private const float DefaultRadius = 0.5f;
        public const int PresentationID = 330;

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

            if (TechCatalog.TryGetUnit("Feraldis_Spearman", out var def))
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
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent<SpearmanTag>(entity);
            creator.AddComponent<FeraldisSpearmanTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryHeavy });
            creator.AddComponent(entity, new Defense { Melee = 1, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
