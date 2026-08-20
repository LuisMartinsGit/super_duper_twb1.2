// File: Assets/GameData/TechTree/Buildings/Sects/Chapel/Chapel.cs
// Sect chapels — one creator parameterised by sect id (12 building ids).
// Building a chapel in a Temple slot IS the sect-adoption mechanism.
//
// Extracted from BuildingFactory (2026-08-12): each building's creation
// code lives with its data, per the TechTree co-location convention.
// BuildingFactory keeps only the id -> recipe dispatch.

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    public static class Chapel
    {
        /// <summary>Per-sect chapel presentation id: 390 + sect index for
        /// indices 0-9; Ruin/Wrath (10, 11) sit at 410/411 because the
        /// 400-403 block is taken. Unknown sect falls back to 390.</summary>
        public static int PidForSect(string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return 390;
            // 400-403 are Forest/Rock/deposits and 410 is the BazaarWagon —
            // Ruin/Wrath (indices 10/11) sit at 412/413.
            return idx < 10 ? 390 + idx : 402 + idx;
        }

        /// <summary>
        /// Create a sect chapel inside a Temple slot. task-063 phase 2a:
        /// uniform creator for all 12 chapels (Chapel_Sect_Antiquity ..
        /// Chapel_Sect_Wrath) — visual differentiation per sect lands in
        /// Phase 5. The chapel acts as the *adoption marker* for its sect:
        /// TempleChapelBuildSystem fires SectAdoption.OnChapelCompleted on
        /// completion which credits the sect to the faction's
        /// SectAdoptionState (and deducts adoption RP).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction, string sectId)
        {
            int ChapelPresentationId = PidForSect(sectId);
            const float Hp = 350f;
            const float Los = 8f;

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(ChapelTag)
            );

            em.SetComponentData(entity, new PresentationId { Id = ChapelPresentationId });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)Hp, Max = (int)Hp });
            em.SetComponentData(entity, new LineOfSight { Radius = Los });

            // BuildingSizeConfig already returns (2, 2) for any Chapel_* via the
            // wildcard prefix branch — re-use that lookup for footprint + radius.
            var gridSize = BuildingSizeConfig.GetSize("Chapel_Sect_Antiquity"); // any chapel id matches the wildcard
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            em.SetComponentData(entity, new ChapelTag
            {
                SectId = new Unity.Collections.FixedString64Bytes(sectId ?? string.Empty)
            });

            // Chapels train their sect's unique unit (the Unit lever —
            // Lorekeeper / Tinker / etc.), so they carry a training queue
            // from birth. Sects whose unit isn't implemented yet simply
            // show no train button (GetChapelTrainingActions).
            em.AddComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>ECB-deferred variant of <see cref="CreateChapel"/>.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction, string sectId)
        {
            int ChapelPresentationId = PidForSect(sectId);
            const float Hp = 350f;
            const float Los = 8f;

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = ChapelPresentationId });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)Hp, Max = (int)Hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = Los });

            var gridSize = BuildingSizeConfig.GetSize("Chapel_Sect_Antiquity");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            ecb.AddComponent(entity, new ChapelTag
            {
                SectId = new Unity.Collections.FixedString64Bytes(sectId ?? string.Empty)
            });

            // Mirror CreateChapel: training queue for the sect's unique unit.
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
