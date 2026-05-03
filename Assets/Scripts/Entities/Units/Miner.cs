// File: Assets/Scripts/Entities/Units/Miner.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Miner unit - gathers iron from deposits.
    /// Economy class unit with MinerTag and MinerState components.
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Miner
    {
        private const float DefaultHP = 50f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 2f;
        private const float DefaultLoS = 10f;
        private const int PresentationID = 203;

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

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Miner", out var def))
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
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Miner });
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            // MovementSystem's query requires DesiredDestination. Without this baked in,
            // MiningSystem.ProcessIdleState would have to add it mid-iteration via
            // em.AddComponentData — a structural change inside a SystemAPI.Query foreach,
            // which invalidates the iterator and aborts OnUpdate with no miner movement.
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });
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
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
    // MinerTag, MinerWorkState, and MinerState are defined in Core/Components/UnitComponents.cs (global namespace)
}
