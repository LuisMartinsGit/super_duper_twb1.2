// File: Assets/Scripts/Entities/Units/Builder.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Worker unit (code id <c>Builder</c> for backward compatibility) — can
    /// both construct buildings and mine resource deposits. Per
    /// docs/Design/Complete.md §2.2 "Worker — Unified Builder + Miner", the
    /// two former specialist units now share one factory: every Worker
    /// carries <see cref="CanBuild"/>, <see cref="MinerTag"/>, and
    /// <see cref="MinerState"/> so the same entity swaps between gather
    /// orders and build orders without re-training. The legacy
    /// <see cref="Miner"/> factory is preserved for entities loaded from
    /// older saves, but new spawns route through here.
    /// </summary>
    public static class Builder
    {
        private const float DefaultHP = 60f;
        private const float DefaultSpeed = 4f;
        private const float DefaultDamage = 2f;
        private const float DefaultLoS = 12f;
        private const int PresentationID = 200;

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

            if (TechCatalog.TryGetUnit("Builder", out var def))
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
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new CanBuild { Value = true });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            // MovementSystem requires DesiredDestination; AI build dispatch
            // calls SetComponent on it. Bake here so newly trained Builders
            // can move without a structural-change side-effect at first
            // dispatch. Mirrors Miner.cs:54-58. (task-062 G-3)
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Worker = Builder + Miner. Add MinerTag + MinerState so the
            // same entity can be issued a gather order and MiningSystem
            // picks it up. Without these, gather right-clicks on a deposit
            // would no-op because the targeting/mining systems filter on
            // MinerTag.
            creator.AddComponent<MinerTag>(entity);
            creator.AddComponent(entity, new MinerState
            {
                AssignedDeposit = Entity.Null,
                CurrentLoad = 0,
                GatherTimer = 0f,
                State = MinerWorkState.Idle,
                GatheringResource = 0,
                DropoffTarget = Entity.Null,
                GatherSpeedMultiplier = 1.0f,
                CarryCapacityBonus = 0
            });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
}
