// File: Assets/Scripts/Entities/Buildings/CrystalMainNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Main Node - the central hive of the Crystal Curse faction.
    /// Spawned at map start, spreads cursed ground, and controls crystal AI behavior.
    /// Uses Faction.White so existing targeting treats it as enemy to all players.
    /// </summary>
    public static class CrystalMainNode
    {
        private const int DefaultHP = 1500;
        private const float DefaultRadius = 2.5f;
        private const float DefaultSpreadRadius = 15f;
        private const float DefaultSpreadPerTick = 1f;
        private const float DefaultTickInterval = 30f;
        private const float DefaultIncomePerTick = 0f;
        private const float DefaultHarassTimer = 120f;
        private const int DefaultBuildCost = 2000;
        private const int PresentationID = 310;

        /// <summary>
        /// Create CrystalMainNode using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.White)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(Health),
                typeof(Radius),
                typeof(BuildingTag),
                typeof(CrystalTag),
                typeof(CrystalMainNodeTag),
                typeof(CrystalNode),
                typeof(CrystalAIState),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalNode
            {
                IsMain = 1,
                SpreadPerTick = DefaultSpreadPerTick,
                SpreadRadius = DefaultSpreadRadius,
                IncomePerTick = DefaultIncomePerTick,
                TickInterval = DefaultTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            em.SetComponentData(entity, new CrystalAIState
            {
                HarassTimer = DefaultHarassTimer,
                BuildTimer = 0f,
                UnitSpawnTimer = 0f,
                Phase = 0
            });
            em.SetComponentData(entity, new CrystalResourceValue
            {
                BuildCost = DefaultBuildCost
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }

        /// <summary>
        /// Create CrystalMainNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalMainNodeTag>(entity);
            ecb.AddComponent(entity, new CrystalNode
            {
                IsMain = 1,
                SpreadPerTick = DefaultSpreadPerTick,
                SpreadRadius = DefaultSpreadRadius,
                IncomePerTick = DefaultIncomePerTick,
                TickInterval = DefaultTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            ecb.AddComponent(entity, new CrystalAIState
            {
                HarassTimer = DefaultHarassTimer,
                BuildTimer = 0f,
                UnitSpawnTimer = 0f,
                Phase = 0
            });
            ecb.AddComponent(entity, new CrystalResourceValue
            {
                BuildCost = DefaultBuildCost
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }
    }
}
