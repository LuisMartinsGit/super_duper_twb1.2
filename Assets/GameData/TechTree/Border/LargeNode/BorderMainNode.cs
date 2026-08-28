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
            // The well is a structure, not a node — 3 x 3 build cells.
            // Bootstrap places wells directly rather than through
            // BuildingFactory, so the snap has to happen here.
            // docs/Design/Build_Grid.md
            var wellSize = BuildingSizeConfig.GetSize("BorderMainNode");
            position = BuildGrid.Snap(position, wellSize);

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
                typeof(VeilstoneWorth),
                typeof(BorderNodeState),
                typeof(NodeInvulnerabilityState),
                typeof(LastDamagedByFaction),
                typeof(LastAttackerEntity),
                typeof(Defense)
            );

            em.SetComponentData(entity, new PresentationId { Id = MainNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            em.SetComponentData(entity, new Radius { Value = MainNodeRadius });
            // The archetype has always included BuildingSize but never
            // assigned it, so it stayed {0,0}: the sized cost-field stamp
            // computed a zero half-extent and blocked a SINGLE nav cell, and
            // PassabilityBuildingSync took the rect branch with an empty rect
            // — a 6 m well that units walked straight through. Assign it.
            em.SetComponentData(entity, new BuildingSize
            {
                Width = wellSize.x,
                Height = wellSize.y
            });
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

            // NO self-defense turret (design 2026-08-11: curse nodes never
            // attack). The 18 m / 25 dmg / 1.2 s turret made every 35 s
            // purification channel at 6 m mathematically impossible — a 90 HP
            // Scholar died in under 5 seconds. Well pressure comes from the
            // ritual defender births and the Backlash, not from node fire.
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
            // Same 3 x 3-cell snap as the EntityManager overload.
            var wellSize = BuildingSizeConfig.GetSize("BorderMainNode");
            position = BuildGrid.Snap(position, wellSize);

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = MainNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            ecb.AddComponent(entity, new Radius { Value = MainNodeRadius });
            ecb.AddComponent(entity, new LineOfSight { Radius = MainNodeLineOfSight });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            // This overload never added BuildingSize at all, so wells created
            // through it fell into the DEFAULT 3x3 building stamp instead of
            // their real footprint. Match the EntityManager path.
            ecb.AddComponent(entity, new BuildingSize
            {
                Width = wellSize.x,
                Height = wellSize.y
            });
            ecb.AddComponent<BorderTag>(entity);
            ecb.AddComponent<BorderMainNodeTag>(entity);
            ecb.AddComponent(entity, new BorderNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            ecb.AddComponent(entity, new BorderSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            ecb.AddComponent(entity, new BorderNodeLevel { Value = 1 });
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

            // NO self-defense turret — see the EntityManager overload's note
            // (curse nodes never attack, 2026-08-11).
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
