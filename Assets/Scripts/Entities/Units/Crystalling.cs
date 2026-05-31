// File: Assets/Scripts/Entities/Units/Crystalling.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystalling unit - fast, weak melee crystal swarm unit.
    /// Cheap crystal-cost melee infantry for the Crystal faction (Faction.Curse).
    /// No population cost - crystal faction uses crystal resource economy.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Crystalling
    {
        /// <summary>
        /// Create Crystalling using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Crystalling using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = CrystallingHP;
            float speed = CrystallingSpeed;
            float damage = CrystallingDamage;
            float los = CrystallingLoS;
            float cooldown = CrystallingAttackCooldown;
            float radius = CrystallingRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Crystalling", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = CrystallingPresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            creator.AddComponent<CrystalTag>(entity);
            creator.AddComponent<CrystalUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = radius });
            creator.AddComponent(entity, new CrystalResourceValue { BuildCost = CrystallingBuildCost });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            creator.AddComponent(entity, new Defense { Melee = 2, Ranged = 1, Siege = 0, Magic = 1 });

            // Pre-allocate DesiredDestination — see comment in EntityManager
            // overload above for the race rationale.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            return entity;
        }
    }
}
