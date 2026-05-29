// File: Assets/Scripts/Entities/Buildings/CrystalMainNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Main Node - the central hive of the Crystal Curse faction.
    /// Spawned at map start, spreads cursed ground, and controls crystal AI behavior.
    /// Uses Faction.Curse so existing targeting treats it as enemy to all players.
    /// </summary>
    public static class CrystalMainNode
    {
        /// <summary>
        /// Create CrystalMainNode using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.Curse)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(Health),
                typeof(Radius),
                typeof(LineOfSight),
                typeof(BuildingSize),
                typeof(BuildingTag),
                typeof(CrystalTag),
                typeof(CrystalMainNodeTag),
                typeof(CrystalNode),
                typeof(CrystalSpreadState),
                typeof(CrystalNodeLevel),
                typeof(CrystalAIState),
                typeof(CrystalTrainingState),
                typeof(CrystalResourceValue),
                typeof(CrystalNodeState),
                typeof(NodeInvulnerabilityState),
                typeof(LastDamagedByFaction),
                typeof(LastAttackerEntity),
                typeof(BuildingRangedAttack),
                typeof(Defense)
            );

            em.SetComponentData(entity, new PresentationId { Id = MainNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            em.SetComponentData(entity, new Radius { Value = MainNodeRadius });
            // Small LOS so Faction.Curse "sees" the area around its own node and
            // can react to attackers; the FOW reveal radius for player factions
            // is independent and gated by their own scouts.
            em.SetComponentData(entity, new LineOfSight { Radius = MainNodeLineOfSight });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            em.SetComponentData(entity, new CrystalSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            em.SetComponentData(entity, new CrystalNodeLevel { Value = 1 });
            em.SetComponentData(entity, new CrystalAIState
            {
                BuildTimer = 0f,
                Phase = 0
            });
            // Mirror the ECB Create overload so both paths initialize this
            // explicitly (currently all-zero; keeps them from silently diverging
            // if the ECB defaults ever change).
            em.SetComponentData(entity, new CrystalTrainingState
            {
                TrainingUnitType = 0,
                TimeRemaining = 0f,
                TotalTime = 0f
            });
            em.SetComponentData(entity, new CrystalResourceValue
            {
                BuildCost = MainNodeBuildCost
            });
            em.SetComponentData(entity, new CrystalNodeState
            {
                State = NodeState.Active,
                OwnerCulture = Cultures.None,
                OwnerFaction = Faction.Curse,
                StateTimer = 0f,
            });
            em.SetComponentData(entity, new NodeInvulnerabilityState { LastObservedHealth = MainNodeHP });
            em.SetComponentData(entity, new LastDamagedByFaction { Value = Faction.Curse });
            em.SetComponentData(entity, new LastAttackerEntity { Value = Entity.Null });

            // Self-defense turret
            em.SetComponentData(entity, new BuildingRangedAttack
            {
                Range = MainNodeAttackRange,
                Damage = MainNodeAttackDamage,
                Cooldown = MainNodeAttackCooldown,
                Timer = 0f,
                MaxTargets = MainNodeAttackMaxTargets
            });
            em.SetComponentData(entity, new Defense { Melee = 15, Ranged = 15, Siege = 10, Magic = 10 });

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Magic });

            // Long construction window — drives the staggered rise animation.
            // Curse nodes have no builders; CurseConstructionSystem advances Progress.
            em.AddComponentData(entity, new UnderConstruction { Progress = 0f, Total = 240f });

            return entity;
        }

        /// <summary>
        /// Create CrystalMainNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.Curse)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = MainNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            ecb.AddComponent(entity, new Radius { Value = MainNodeRadius });
            ecb.AddComponent(entity, new LineOfSight { Radius = MainNodeLineOfSight });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalMainNodeTag>(entity);
            ecb.AddComponent(entity, new CrystalNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            ecb.AddComponent(entity, new CrystalSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            ecb.AddComponent(entity, new CrystalNodeLevel { Value = 1 });
            ecb.AddComponent(entity, new CrystalAIState
            {
                BuildTimer = 0f,
                Phase = 0
            });
            ecb.AddComponent(entity, new CrystalTrainingState
            {
                TrainingUnitType = 0,
                TimeRemaining = 0f,
                TotalTime = 0f
            });
            ecb.AddComponent(entity, new CrystalResourceValue
            {
                BuildCost = MainNodeBuildCost
            });
            ecb.AddComponent(entity, new CrystalNodeState
            {
                State = NodeState.Active,
                OwnerCulture = Cultures.None,
                OwnerFaction = Faction.Curse,
                StateTimer = 0f,
            });
            ecb.AddComponent(entity, new NodeInvulnerabilityState { LastObservedHealth = MainNodeHP });
            ecb.AddComponent(entity, new LastDamagedByFaction { Value = Faction.Curse });
            ecb.AddComponent(entity, new LastAttackerEntity { Value = Entity.Null });

            // Self-defense turret
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = MainNodeAttackRange,
                Damage = MainNodeAttackDamage,
                Cooldown = MainNodeAttackCooldown,
                Timer = 0f,
                MaxTargets = MainNodeAttackMaxTargets
            });
            ecb.AddComponent(entity, new Defense { Melee = 15, Ranged = 15, Siege = 10, Magic = 10 });

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });

            // Long construction window — drives the staggered rise animation.
            ecb.AddComponent(entity, new UnderConstruction { Progress = 0f, Total = 240f });

            return entity;
        }
    }
}
