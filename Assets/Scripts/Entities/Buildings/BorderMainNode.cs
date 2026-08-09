// File: Assets/Scripts/Entities/Buildings/BorderMainNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstone Main Node - the central hive of the The Border faction.
    /// Spawned at map start, spreads border ground, and controls veilstone AI behavior.
    /// Uses Faction.Border so existing targeting treats it as enemy to all players.
    /// </summary>
    public static class BorderMainNode
    {
        /// <summary>
        /// Create BorderMainNode using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.Border)
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
                typeof(BorderTag),
                typeof(BorderMainNodeTag),
                typeof(BorderNode),
                typeof(BorderSpreadState),
                typeof(BorderNodeLevel),
                typeof(BorderAIState),
                typeof(BorderTrainingState),
                typeof(VeilstoneWorth),
                typeof(BorderNodeState),
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
            // Small LOS so Faction.Border "sees" the area around its own node and
            // can react to attackers; the FOW reveal radius for player factions
            // is independent and gated by their own scouts.
            em.SetComponentData(entity, new LineOfSight { Radius = MainNodeLineOfSight });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new BorderNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            em.SetComponentData(entity, new BorderSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            em.SetComponentData(entity, new BorderNodeLevel { Value = 1 });
            em.SetComponentData(entity, new BorderAIState
            {
                BuildTimer = 0f,
                Phase = 0
            });
            // Mirror the ECB Create overload so both paths initialize this
            // explicitly (currently all-zero; keeps them from silently diverging
            // if the ECB defaults ever change).
            em.SetComponentData(entity, new BorderTrainingState
            {
                TrainingUnitType = 0,
                TimeRemaining = 0f,
                TotalTime = 0f
            });
            em.SetComponentData(entity, new VeilstoneWorth
            {
                BuildCost = MainNodeBuildCost
            });
            em.SetComponentData(entity, new BorderNodeState
            {
                State = NodeState.Active,
                OwnerCulture = Cultures.None,
                OwnerFaction = Faction.Border,
                StateTimer = 0f,
            });
            em.SetComponentData(entity, new NodeInvulnerabilityState { LastObservedHealth = MainNodeHP });
            // Wells are never auto-acquired. Stamped at BIRTH rather than
            // waiting for NodeTargetabilitySystem's first pass, so there is
            // no frame in which a fresh well is a valid auto-target.
            // Removed only while a Feraldis Corruptor has it cracked open.
            em.AddComponent<NodeNoAutoAcquire>(entity);
            em.SetComponentData(entity, new LastDamagedByFaction { Value = Faction.Border });
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
            // Border nodes have no builders; BorderConstructionSystem advances Progress.
            em.AddComponentData(entity, new UnderConstruction { Progress = 0f, Total = 240f });

            return entity;
        }

        /// <summary>
        /// Create BorderMainNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.Border)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = MainNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            ecb.AddComponent(entity, new Radius { Value = MainNodeRadius });
            ecb.AddComponent(entity, new LineOfSight { Radius = MainNodeLineOfSight });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<BorderTag>(entity);
            ecb.AddComponent<BorderMainNodeTag>(entity);
            ecb.AddComponent(entity, new BorderNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            ecb.AddComponent(entity, new BorderSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            ecb.AddComponent(entity, new BorderNodeLevel { Value = 1 });
            ecb.AddComponent(entity, new BorderAIState
            {
                BuildTimer = 0f,
                Phase = 0
            });
            ecb.AddComponent(entity, new BorderTrainingState
            {
                TrainingUnitType = 0,
                TimeRemaining = 0f,
                TotalTime = 0f
            });
            ecb.AddComponent(entity, new VeilstoneWorth
            {
                BuildCost = MainNodeBuildCost
            });
            ecb.AddComponent(entity, new BorderNodeState
            {
                State = NodeState.Active,
                OwnerCulture = Cultures.None,
                OwnerFaction = Faction.Border,
                StateTimer = 0f,
            });
            ecb.AddComponent(entity, new NodeInvulnerabilityState { LastObservedHealth = MainNodeHP });
            // Wells enter play ASLEEP (canon §2.8). A dormant well pumps no
            // saturation — VeilFieldSystem's feeder query excludes it — so the
            // map does not creep until a player reaches for a verb on it. This
            // applies to BorderExtinctionSystem respawns too: a fresh well
            // nobody has touched is dormant, same as one at match start.
            ecb.AddComponent<WellDormant>(entity);
            ecb.AddComponent<NodeNoAutoAcquire>(entity);
            ecb.AddComponent(entity, new LastDamagedByFaction { Value = Faction.Border });
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
