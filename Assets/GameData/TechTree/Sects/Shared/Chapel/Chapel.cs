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

            var entity = EntityBuild.CreateEntity(em, 
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

            EntityBuild.Add(em, entity, new PresentationId { Id = ChapelPresentationId });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)Hp, Max = (int)Hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = Los });

            // BuildingSizeConfig already returns (2, 2) for any Chapel_* via the
            // wildcard prefix branch — re-use that lookup for footprint + radius.
            var gridSize = BuildingSizeConfig.GetSize("Chapel_Sect_Antiquity"); // any chapel id matches the wildcard
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            EntityBuild.Add(em, entity, new ChapelTag
            {
                SectId = new Unity.Collections.FixedString64Bytes(sectId ?? string.Empty)
            });

            // Chapels train their sect's unique unit (the Unit lever —
            // Lorekeeper / Tinker / etc.), so they carry a training queue
            // from birth. Sects whose unit isn't implemented yet simply
            // show no train button (GetChapelTrainingActions).
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);
            EntityBuild.Add(em, entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            // Combat type tags
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return EntityBuild.Done(entity);
        }

        /// <summary>ECB-deferred variant of <see cref="CreateChapel"/>.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction, string sectId)
        {
            int ChapelPresentationId = PidForSect(sectId);
            const float Hp = 350f;
            const float Los = 8f;

            var entity = EntityBuild.CreateEntity(ecb);

            EntityBuild.Add(ecb, entity, new PresentationId { Id = ChapelPresentationId });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)Hp, Max = (int)Hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = Los });

            var gridSize = BuildingSizeConfig.GetSize("Chapel_Sect_Antiquity");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            EntityBuild.Add(ecb, entity, new ChapelTag
            {
                SectId = new Unity.Collections.FixedString64Bytes(sectId ?? string.Empty)
            });

            // Mirror CreateChapel: training queue for the sect's unique unit.
            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);
            EntityBuild.Add(ecb, entity, new RallyPoint
            {
                Position = position + new float3(3f, 0, 3f),
                Has = 1
            });

            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return EntityBuild.Done(entity);
        }
    }
}
