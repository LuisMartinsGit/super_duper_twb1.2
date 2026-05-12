// File: Assets/Scripts/Entities/Buildings/GlowReliquary.cs
// Glow deposit building (spec §6.3). Carriers walking within
// GlowAutoDepositRadius automatically deliver their carried Glow into the
// reliquary's Stored amount; on the next tick GlowFlowSystem flushes
// Stored into the faction bank.
//
// When destroyed while holding Glow, the building emits an AOE blast
// (ReliquaryExplodeSystem). Empty reliquaries die quietly.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Stand-alone deposit building usable by any faction. Spec §6.4 calls
    /// out different faction biases for Glow-storage buildings — those
    /// faction-specific variants are a follow-up; for now the generic
    /// reliquary covers the core mechanic (deposit + explode on death).
    /// </summary>
    public static class GlowReliquary
    {
        public const int PresentationID = GlowReliquaryPresentationID;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(GlowReliquaryTag),
                typeof(GlowReliquaryStored),
                typeof(Health),
                typeof(LineOfSight),
                typeof(BuildingSize),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = GlowReliquaryHP, Max = GlowReliquaryHP });
            em.SetComponentData(entity, new LineOfSight { Radius = GlowReliquaryLoS });
            em.SetComponentData(entity, new GlowReliquaryStored { Amount = 0 });

            var gridSize = BuildingSizeConfig.GetSize("GlowReliquary");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });

            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<GlowReliquaryTag>(entity);
            ecb.AddComponent(entity, new GlowReliquaryStored { Amount = 0 });
            ecb.AddComponent(entity, new Health { Value = GlowReliquaryHP, Max = GlowReliquaryHP });
            ecb.AddComponent(entity, new LineOfSight { Radius = GlowReliquaryLoS });

            var gridSize = BuildingSizeConfig.GetSize("GlowReliquary");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
