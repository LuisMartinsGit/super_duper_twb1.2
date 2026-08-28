using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Worker unit — can both construct buildings and mine resource
    /// deposits. Per docs/Design/Complete.md §2.2 "Worker — Unified
    /// Builder + Miner", every Worker carries <see cref="CanBuild"/>,
    /// <see cref="MinerTag"/>, and <see cref="MinerState"/> so the same
    /// entity swaps between gather orders and build orders without
    /// re-training.
    /// </summary>
    public static class Worker
    {
        private const int PresentationID = 200;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Worker");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;

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
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            // MovementSystem requires DesiredDestination; AI build dispatch
            // calls SetComponent on it. Bake here so newly trained Workers
            // can move without a structural-change side-effect at first
            // dispatch. (task-062 G-3)
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Worker = Builder + Miner. Add MinerTag + MinerState so the
            // same entity can be issued a gather order and MiningSystem
            // picks it up. Without these, gather right-clicks on a deposit
            // would no-op because the targeting/mining systems filter on
            // MinerTag.
            // (declared in UnitComponents.cs)
            // Marks this worker a NON-COMBATANT: TargetingSystem skips
            // PassiveWorkerTag for auto-acquire and return-to-guard, so
            // builders never wander off to fight. Feraldis strips it —
            // their Workers are light infantry that also build.
            creator.AddComponent<PassiveWorkerTag>(entity);
            creator.AddComponent<MinerTag>(entity);
            creator.AddComponent(entity, new MinerState
            {
                AssignedDeposit = Entity.Null,
                GatherTimer = 0f,
                State = MinerWorkState.Idle,
                GatheringResource = 0,
                GatherSpeedMultiplier = 1.0f
            });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
}
