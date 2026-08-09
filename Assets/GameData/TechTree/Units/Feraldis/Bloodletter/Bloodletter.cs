// File: Assets/GameData/TechTree/Units/Feraldis/Bloodletter/Bloodletter.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Bloodletter — low HP, low per-hit damage, high mobility.
    /// Its swing is a whirl: every enemy inside WhirlRadius is struck and
    /// left Bleeding. Built to open many small wounds at once, which is
    /// how Feraldis turns a skirmish into the blood its army frenzies on.
    ///
    /// Design: docs/Design/Age_1_Feraldis.md (2026-08-05).
    /// </summary>
    public static class Bloodletter
    {
        private const float DefaultHP = 90f;
        private const float DefaultSpeed = 6.8f;
        private const float DefaultDamage = 6f;
        private const float DefaultLoS = 16f;
        private const float DefaultAttackCooldown = 1.2f;
        private const float DefaultRadius = 0.5f;
        public const int PresentationID = 342;

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

            if (TechCatalog.TryGetUnit("Feraldis_Bloodletter", out var def))
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
            creator.AddComponent<BloodletterTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);
            creator.AddComponent(entity, new WhirlAttack
            {
                Radius = WhirlRadius,
                BleedDamagePerSecond = BleedDamagePerSecond,
                BleedDuration = BleedDuration,
            });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
