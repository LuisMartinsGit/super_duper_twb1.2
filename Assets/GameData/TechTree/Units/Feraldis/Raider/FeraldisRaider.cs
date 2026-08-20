// File: Assets/GameData/TechTree/Units/Feraldis/Raider/FeraldisRaider.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

/// <summary>Marker for Feraldis Raider light cavalry.</summary>
public struct FeraldisRaiderTag : IComponentData { }

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Raider — LIGHT CAVALRY (design 2026-08-05 rev.2; was an
    /// uncontrollable infantry skirmisher). Fast harasser whose hits leave
    /// enemy BUILDINGS burning: it doesn't siege a structure down, it rides
    /// past and leaves it smouldering.
    ///
    /// ONE unit definition, TWO spawn paths:
    ///   * <see cref="Create"/>            — trained at the Pasture, player-controlled.
    ///   * <see cref="CreateUncontrolled"/> — the free wave a Feraldis House
    ///     spits out on build/upgrade, driven by FeraldisRaiderPatrolSystem.
    /// Keeping them one entity means a buff to the Raider is felt in both
    /// places, which is what the design always intended ("subset of the same
    /// Raider concept").
    /// </summary>
    public static class FeraldisRaider
    {
        private const float DefaultHP = 110f;
        private const float DefaultSpeed = 7.5f;
        private const float DefaultDamage = 10f;
        private const float DefaultLoS = 16f;
        private const float DefaultCooldown = 1.2f;
        private const float DefaultRadius = 0.5f;
        public const int PresentationID = 341;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction, controllable: true);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction, controllable: true);

        /// <summary>House-spawned wave: identical unit, but the player can
        /// neither select nor command it.</summary>
        public static Entity CreateUncontrolled(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction, controllable: false);

        /// <summary>House-spawned wave (ECB variant).</summary>
        public static Entity CreateUncontrolled(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction, controllable: false);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position,
            Faction faction, bool controllable)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float cooldown = DefaultCooldown;

            if (TechCatalog.TryGetUnit("Feraldis_Raider", out var def))
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
            // House-spawned waves are FREE (docs/Design/Age_1_Feraldis.md:
            // "uncontrollable, do not consume population", Pop: 0). Only a
            // raider you actually trained costs a slot. The shared creation
            // path used to stamp 1 for both, so every House wave quietly taxed
            // the population budget — and, because those raiders counted
            // toward the AI's army floor, made the AI think it had recruited.
            creator.AddComponent(entity, new PopulationCost { Amount = controllable ? 1 : 0 });
            creator.AddComponent<CavalryTag>(entity);

            // The signature: enemy structures it strikes keep burning.
            creator.AddComponent(entity, new InflictsBuildingBurn
            {
                DamagePerSecond = RaiderBuildingDotPerSecond,
                Duration = RaiderBuildingDotDuration,
            });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Cavalry });
            creator.AddComponent(entity, new Defense { Melee = 1, Ranged = 0, Siege = 0, Magic = 0 });

            creator.AddComponent(entity, new DesiredDestination { Has = 0 });
            creator.AddComponent<FeraldisRaiderTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);
            if (!controllable)
                creator.AddComponent<NotControllableTag>(entity);

            return entity;
        }
    }
}
