// File: Assets/Scripts/Entities/Units/FeraldisRaider.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Marker for Feraldis auto-spawned Raider units (uncontrollable skirmishers).</summary>
public struct FeraldisRaiderTag : IComponentData { }

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Raider — uncontrollable infantry skirmisher.
    /// Auto-spawned by Feraldis Houses per design §5.3 (Complete.md). The
    /// player cannot select or command Raiders; an aggressive-patrol AI
    /// drives them at the nearest enemy. Stat block: HP 80, speed 6.0,
    /// light melee. No train button — spawn is driven by task-066 Phase 3.
    /// </summary>
    public static class FeraldisRaider
    {
        // Defaults used if TechTreeDB lookup fails. Match docs/Design Complete.md §5.3.
        private const float DefaultHP = 80f;
        private const float DefaultSpeed = 6.0f;
        private const float DefaultDamage = 8f;
        private const float DefaultLoS = 16f;
        private const float DefaultCooldown = 1.2f;
        private const float DefaultRadius = 0.5f;
        public const int PresentationID = 341;

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
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_Raider", out var def))
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

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            // Player cannot select or command Feraldis Raiders; FeraldisRaiderPatrolSystem drives them.
            creator.AddComponent<NotControllableTag>(entity);
            creator.AddComponent<FeraldisRaiderTag>(entity);

            return entity;
        }
    }
}
