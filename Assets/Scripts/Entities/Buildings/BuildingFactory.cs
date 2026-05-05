// File: Assets/Scripts/Entities/Buildings/BuildingFactory.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Unified factory for creating all building types.
    /// 
    /// Provides a single entry point for spawning buildings by ID,
    /// with automatic stat loading from TechTreeDB.
    /// 
    /// Usage:
    ///   Entity building = BuildingFactory.Create(em, "Barracks", position, faction);
    /// </summary>
    public static class BuildingFactory
    {
        /// <summary>
        /// Create a building by its ID string.
        /// Automatically loads stats from TechTreeDB if available.
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="buildingId">Building type: "Hall", "Barracks", "Hut", "GatherersHut", etc.</param>
        /// <param name="position">World position to spawn at</param>
        /// <param name="faction">Faction the building belongs to</param>
        /// <returns>Created entity</returns>
        public static Entity Create(EntityManager em, string buildingId, float3 position, Faction faction)
        {
            Entity entity = buildingId switch
            {
                "Hall" => Hall.Create(em, position, faction),
                "Barracks" => Barracks.Create(em, position, faction),
                "Hut" => Hut.Create(em, position, faction),
                "GatherersHut" => GatherersHut.Create(em, position, faction),
                "ShrineOfAhridan" => CreateShrineOfAhridan(em, position, faction),
                "TempleOfRidan" => CreateTempleOfRidanNew(em, position, faction),
                "VaultOfAlmierra" => CreateVaultOfAlmierra(em, position, faction),
                "FiendstoneKeep" => CreateFiendstoneKeep(em, position, faction),
                "Alanthor_Wall" => AlanthorWall.CreateHub(em, position, faction),
                "Alanthor_Smelter" => Smelter.Create(em, position, faction),
                // Runai culture buildings
                "Runai_Outpost" => CreateRunaiOutpost(em, position, faction),
                "Runai_TradeHub" => CreateRunaiTradeHub(em, position, faction),
                "Runai_TradingPost" => CreateRunaiTradingPost(em, position, faction),
                "ThessarasBazaar" => CreateRunaiBazaar(em, position, faction),
                "Runai_SiegeWorkshop" => CreateRunaiSiegeWorkshop(em, position, faction),
                "Runai_Vault" => CreateRunaiVault(em, position, faction),
                "Runai_VeilsteelFoundry" => CreateRunaiVeilsteelFoundry(em, position, faction),
                // Alanthor culture buildings
                "Alanthor_Tower" => CreateAlanthorWatchTower(em, position, faction),
                "Alanthor_Garrison" => CreateAlanthorGarrison(em, position, faction),
                "Alanthor_Stable" => CreateAlanthorRoyalStable(em, position, faction),
                "Alanthor_SiegeYard" => CreateAlanthorSiegeYard(em, position, faction),
                "KingsCourt" => CreateKingsCourt(em, position, faction),
                "Alanthor_Crucible" => CreateAlanthorCrucible(em, position, faction),
                // Feraldis culture buildings
                "Feraldis_HuntingLodge" => CreateFeraldisHuntingLodge(em, position, faction),
                "Feraldis_LoggingStation" => CreateFeraldisLoggingStation(em, position, faction),
                "Feraldis_Longhouse" => CreateFeraldisLonghouse(em, position, faction),
                "Feraldis_Tower" => CreateFeraldisTotemTower(em, position, faction),
                "Feraldis_SiegeYard" => CreateFeraldisSiegeYard(em, position, faction),
                "Feraldis_Foundry" => CreateFeraldisFoundry(em, position, faction),
                // task-063 phase 2a: 12 chapel building IDs (Chapel_Sect_Antiquity
                // .. Chapel_Sect_Wrath) all dispatch to a single uniform creator
                // that stamps ChapelTag.SectId from the chapel-id suffix. Visual
                // differentiation per sect lands in a Phase 5 polish pass.
                _ when SectConfig.IsChapelId(buildingId) => CreateChapel(em, position, faction, SectConfig.SectIdFromChapelId(buildingId)),
                _ => CreateDefault(em, buildingId, position, faction)
            };

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }

        /// <summary>
        /// Create a building using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, string buildingId, float3 position, Faction faction)
        {
            Entity entity = buildingId switch
            {
                "Hall" => Hall.Create(ecb, position, faction),
                "Barracks" => Barracks.Create(ecb, position, faction),
                "Hut" => Hut.Create(ecb, position, faction),
                "GatherersHut" => GatherersHut.Create(ecb, position, faction),
                "ShrineOfAhridan" => CreateShrineOfAhridanECB(ecb, position, faction),
                "TempleOfRidan" => CreateTempleOfRidanNewECB(ecb, position, faction),
                "VaultOfAlmierra" => CreateVaultOfAlmierraECB(ecb, position, faction),
                "FiendstoneKeep" => CreateFiendstoneKeepECB(ecb, position, faction),
                "Alanthor_Smelter" => Smelter.Create(ecb, position, faction),
                // Runai culture buildings
                "Runai_Outpost" => CreateRunaiOutpostECB(ecb, position, faction),
                "Runai_TradeHub" => CreateRunaiTradeHubECB(ecb, position, faction),
                "Runai_TradingPost" => CreateRunaiTradingPostECB(ecb, position, faction),
                "ThessarasBazaar" => CreateRunaiBazaarECB(ecb, position, faction),
                "Runai_SiegeWorkshop" => CreateRunaiSiegeWorkshopECB(ecb, position, faction),
                "Runai_Vault" => CreateRunaiVaultECB(ecb, position, faction),
                "Runai_VeilsteelFoundry" => CreateRunaiVeilsteelFoundryECB(ecb, position, faction),
                // Alanthor culture buildings
                "Alanthor_Tower" => CreateAlanthorWatchTowerECB(ecb, position, faction),
                "Alanthor_Garrison" => CreateAlanthorGarrisonECB(ecb, position, faction),
                "Alanthor_Stable" => CreateAlanthorRoyalStableECB(ecb, position, faction),
                "Alanthor_SiegeYard" => CreateAlanthorSiegeYardECB(ecb, position, faction),
                "KingsCourt" => CreateKingsCourtECB(ecb, position, faction),
                "Alanthor_Crucible" => CreateAlanthorCrucibleECB(ecb, position, faction),
                // Feraldis culture buildings
                "Feraldis_HuntingLodge" => CreateFeraldisHuntingLodgeECB(ecb, position, faction),
                "Feraldis_LoggingStation" => CreateFeraldisLoggingStationECB(ecb, position, faction),
                "Feraldis_Longhouse" => CreateFeraldisLonghouseECB(ecb, position, faction),
                "Feraldis_Tower" => CreateFeraldisTotemTowerECB(ecb, position, faction),
                "Feraldis_SiegeYard" => CreateFeraldisSiegeYardECB(ecb, position, faction),
                "Feraldis_Foundry" => CreateFeraldisFoundryECB(ecb, position, faction),
                // task-063 phase 2a: chapels dispatch to a single uniform ECB creator
                // (parameterised by sect id parsed from the chapel building id).
                _ when SectConfig.IsChapelId(buildingId) => CreateChapelECB(ecb, position, faction, SectConfig.SectIdFromChapelId(buildingId)),
                _ => CreateDefault(ecb, buildingId, position, faction)
            };

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }

        /// <summary>
        /// Get the PresentationId for a building type.
        /// </summary>
        public static int GetPresentationId(string buildingId)
        {
            return buildingId switch
            {
                "Hall" => 100,
                "Hut" => 102,
                "GatherersHut" => 101,
                "Barracks" => 510,
                "ShrineOfAhridan" => 520,
                "TempleOfRidan" => 521,
                "VaultOfAlmierra" => 530,
                "FiendstoneKeep" => 540,
                "Alanthor_Wall" => AlanthorWall.HubPresentationID,
                "Alanthor_Smelter" => Smelter.PresentationID,
                // Runai culture buildings
                "Runai_Outpost" => 350,
                "Runai_TradeHub" => 351,
                "ThessarasBazaar" => 352,
                "Runai_SiegeWorkshop" => 353,
                // Runai_TradingPost shares mesh 355 with Alanthor_Garrison.
                // (task-062 Q-39 — was missing, falling through to 100/default.)
                "Runai_TradingPost" => 355,
                // Alanthor culture buildings
                "Alanthor_Tower" => 354,
                "Alanthor_Garrison" => 355,
                "Alanthor_Stable" => 356,
                "Alanthor_SiegeYard" => 357,
                // Feraldis culture buildings
                "Feraldis_HuntingLodge" => 358,
                "Feraldis_LoggingStation" => 359,
                "Feraldis_Longhouse" => 360,
                "Feraldis_Tower" => 361,
                "Feraldis_SiegeYard" => 362,
                "KingsCourt" => 363,
                "Alanthor_Crucible" => 364,
                "Runai_Vault" => 365,
                "Runai_VeilsteelFoundry" => 366,
                "Feraldis_Foundry" => 367,
                // task-063 phase 2a: all 12 new chapels share PID 390 for now.
                // Phase 5 polish will introduce per-sect visual variation.
                _ when SectConfig.IsChapelId(buildingId) => 390,
                _ => 100
            };
        }

        /// <summary>
        /// Get population provided by a building type.
        /// </summary>
        public static int GetPopulationProvided(string buildingId)
        {
            return buildingId switch
            {
                "Hall" => 20,
                "Hut" => 10,
                "ThessarasBazaar" => 40,
                "Alanthor_Garrison" => 8,
                "KingsCourt" => 10,
                "Feraldis_HuntingLodge" => 10,
                "Feraldis_LoggingStation" => 10,
                _ => 0
            };
        }

        /// <summary>
        /// Check if building type can train units.
        /// </summary>
        public static bool CanTrainUnits(string buildingId)
        {
            return buildingId switch
            {
                "Hall" => true,
                "Barracks" => true,
                "ShrineOfAhridan" => true,
                "TempleOfRidan" => true,
                "Runai_TradeHub" => true,
                "ThessarasBazaar" => true,
                "Runai_SiegeWorkshop" => true,
                "Alanthor_Garrison" => true,
                "Alanthor_Stable" => true,
                "Alanthor_SiegeYard" => true,
                "Feraldis_Longhouse" => true,
                "Feraldis_SiegeYard" => true,
                // task-063 phase 1: old Chapel_Sect_<OldSectId> entries removed.
                // Phase 2 reintroduces chapel-trains-unique-unit per new sect.
                _ => false
            };
        }

        /// <summary>
        /// The 3 mutually exclusive choice buildings.
        /// </summary>
        private static readonly HashSet<string> ChoiceBuildingIds = new()
        {
            "ShrineOfAhridan", "VaultOfAlmierra", "FiendstoneKeep"
        };

        /// <summary>
        /// Returns true if the given building ID is one of the 3 choice buildings.
        /// </summary>
        public static bool IsChoiceBuilding(string buildingId) => ChoiceBuildingIds.Contains(buildingId);

        /// <summary>
        /// Check if a faction already has a choice building (built or under construction).
        /// Returns the building ID if one exists, null otherwise.
        /// </summary>
        public static string GetFactionChoiceBuilding(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                typeof(ChoiceBuildingTag), typeof(FactionTag), typeof(BuildingTag));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            string result = null;
            for (int i = 0; i < entities.Length; i++)
            {
                var fac = em.GetComponentData<FactionTag>(entities[i]).Value;
                if (fac == faction)
                {
                    result = GetBuildingIdFromEntity(em, entities[i]);
                    break;
                }
            }
            entities.Dispose();
            return result;
        }

        /// <summary>
        /// Count how many buildings of a given tag type a faction has (built or under construction).
        /// </summary>
        public static int GetFactionBuildingCount<T>(EntityManager em, Faction faction) where T : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(typeof(T), typeof(FactionTag), typeof(BuildingTag));
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) count++;
            }
            return count;
        }

        private static string GetBuildingIdFromEntity(EntityManager em, Entity entity)
        {
            if (em.HasComponent<ShrineTag>(entity)) return "ShrineOfAhridan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";
            return null;
        }

        /// <summary>
        /// Create a generic building with specified tag.
        /// </summary>
        private static Entity CreateGenericBuilding<T>(EntityManager em, string buildingId, float3 position, 
            Faction faction, float defaultHp, float defaultLoS, float defaultRadius, T tag) where T : unmanaged, IComponentData
        {
            float hp = defaultHp;
            float los = defaultLoS;
            float radius = defaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding(buildingId, out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = GetPresentationId(buildingId) });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize(buildingId);
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            // Add specific tag
            em.AddComponentData(entity, tag);

            return entity;
        }

        /// <summary>
        /// Create Fiendstone Keep (Feraldis capital).
        /// </summary>
        private static Entity CreateFiendstoneKeep(EntityManager em, float3 position, Faction faction)
        {
            float hp = 2000f;
            float los = 18f;
            float radius = 2.4f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("FiendstoneKeep", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(PopulationProvider)
            );

            em.SetComponentData(entity, new PresentationId { Id = 540 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 1 }); // Is a base building
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("FiendstoneKeep");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new PopulationProvider { Amount = 20 });

            em.AddComponent<FiendstoneKeepTag>(entity);
            em.AddComponent<ChoiceBuildingTag>(entity);
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 25f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 3
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });

            return entity;
        }

        /// <summary>
        /// Create Shrine of Ahridan — choice building that trains litharchs fast and grants +1 RP.
        /// One of three mutually exclusive choice buildings (Shrine/Vault/Keep).
        /// </summary>
        private static Entity CreateShrineOfAhridan(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f;
            float los = 16f;
            float radius = 1.8f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("ShrineOfAhridan", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(TrainingState)
            );

            em.SetComponentData(entity, new PresentationId { Id = 520 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });

            em.AddComponent<ShrineTag>(entity);
            em.AddComponent<ChoiceBuildingTag>(entity);
            em.AddComponentData(entity, new ShrineRPGranted { Granted = 0 });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
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
        private static Entity CreateChapel(EntityManager em, float3 position, Faction faction, string sectId)
        {
            const int ChapelPresentationId = 390; // shared mesh slot — Phase 5 will introduce per-sect variation
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

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>ECB-deferred variant of <see cref="CreateChapel"/>.</summary>
        private static Entity CreateChapelECB(EntityCommandBuffer ecb, float3 position, Faction faction, string sectId)
        {
            const int ChapelPresentationId = 390;
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

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Temple of Ridan — available to ALL cultures at Era 2.
        /// Has 8 BFME2-style expansion slots for sect chapels.
        /// Houses all sect unit training and tech research.
        /// </summary>
        private static Entity CreateTempleOfRidanNew(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1500f;
            float los = 18f;
            float radius = 2.5f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("TempleOfRidan", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(TrainingState)
            );

            em.SetComponentData(entity, new PresentationId { Id = 521 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            // Use BuildingSizeConfig so PassabilityBuildingSync blocks the full
            // 4x4 footprint instead of falling back to the legacy circular Radius
            // (which left walkable corners around the Temple).
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });

            em.AddComponent<TempleOfRidanTag>(entity);
            em.AddComponent<TempleTag>(entity); // Keep legacy tag for TempleUpgradeSystem compatibility
            em.AddComponentData(entity, new TempleLevel { Level = 1 });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Research state for sect techs
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Initialize 8 empty chapel slots (BFME2-style expansion plots)
            var slotBuffer = em.AddBuffer<TempleChapelSlot>(entity);
            for (int i = 0; i < 8; i++)
            {
                slotBuffer.Add(new TempleChapelSlot
                {
                    Chapel = Entity.Null,
                    SectId = default,
                    State = 0,
                    BuildProgress = 0f,
                    BuildTime = 0f
                });
            }

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Vault of Almiérra — compound interest resource storage.
        /// </summary>
        private static Entity CreateVaultOfAlmierra(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1200f;
            float los = 14f;
            float radius = 2.0f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("VaultOfAlmierra", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = 530 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("VaultOfAlmierra");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            em.AddComponent<VaultTag>(entity);
            em.AddComponent<ChoiceBuildingTag>(entity);
            em.AddComponentData(entity, new VaultStorage
            {
                ResourceType = 0,
                StoredAmount = 0f,
                InterestRate = 0.03f,
                LockTimer = 0f,
                LockDuration = 180f
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        // ==================== Runai Culture Buildings (EntityManager) ====================

        /// <summary>
        /// Runai Outpost — trade node endpoint with extended vision.
        /// </summary>
        private static Entity CreateRunaiOutpost(EntityManager em, float3 position, Faction faction)
        {
            float hp = 900f, los = 20f, radius = 1.0f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_Outpost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 350 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_Outpost");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<OutpostTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Runai Trade Hub — spawns caravans, defines trade routes. Training building.
        /// </summary>
        private static Entity CreateRunaiTradeHub(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1200f, los = 14f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_TradeHub", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 351 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_TradeHub");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<TradeHubTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Runai Trading Post — numbered chain node. Max 10 per faction.
        /// PostNumber assigned by TradingPostSystem on construction complete.
        /// </summary>
        private static Entity CreateRunaiTradingPost(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_TradingPost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(BuildingSize));
            em.SetComponentData(entity, new PresentationId { Id = 355 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            // Use BuildingSizeConfig footprint (was previously a 1m circular
            // Radius which left huge walkable corners around the building).
            var gridSize = BuildingSizeConfig.GetSize("Runai_TradingPost");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<TradingPostTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Runai Bazaar — mobile HQ. +40 pop. Dual training queue. Unique per player.
        /// </summary>
        private static Entity CreateRunaiBazaar(EntityManager em, float3 position, Faction faction)
        {
            float hp = 2700f, los = 35f, radius = 2.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("ThessarasBazaar", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius),
                typeof(TrainingState), typeof(PopulationProvider));
            em.SetComponentData(entity, new PresentationId { Id = 352 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 1 }); // Acts as a base
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("ThessarasBazaar");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.SetComponentData(entity, new PopulationProvider { Amount = 40 });
            em.AddComponent<BazaarTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(4f, 0, 4f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Runai Siege Workshop — trains Sand Ballista.
        /// </summary>
        private static Entity CreateRunaiSiegeWorkshop(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1100f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_SiegeWorkshop", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 353 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_SiegeWorkshop");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<SiegeWorkshopTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        // ==================== Alanthor Culture Buildings (EntityManager) ====================

        /// <summary>
        /// Alanthor Watch Tower — ranged defense (18u range, 14 dmg, 2.0s CD). Garrison 4.
        /// </summary>
        private static Entity CreateAlanthorWatchTower(EntityManager em, float3 position, Faction faction)
        {
            float hp = 950f, los = 22f, radius = 0.8f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Tower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 354 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Tower");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<WatchTowerTag>(entity);
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 18f, Damage = 14, Cooldown = 2.0f, Timer = 0f, MaxTargets = 1
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });
            return entity;
        }

        /// <summary>
        /// Alanthor Garrison — trains Sentinel+Crossbowman. +8 pop.
        /// </summary>
        private static Entity CreateAlanthorGarrison(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1500f, los = 14f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Garrison", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius),
                typeof(TrainingState), typeof(PopulationProvider));
            em.SetComponentData(entity, new PresentationId { Id = 355 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Garrison");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.SetComponentData(entity, new PopulationProvider { Amount = 8 });
            em.AddComponent<GarrisonTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Alanthor Royal Stable — trains Cataphract.
        /// </summary>
        private static Entity CreateAlanthorRoyalStable(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1300f, los = 14f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Stable", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 356 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Stable");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<RoyalStableTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Alanthor Siege Yard — trains Ballista.
        /// </summary>
        private static Entity CreateAlanthorSiegeYard(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1100f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_SiegeYard", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 357 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_SiegeYard");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<SiegeYardTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        // ==================== Feraldis Culture Buildings (EntityManager) ====================

        /// <summary>
        /// Feraldis Hunting Lodge — +10 pop. Passive income near wildlife areas.
        /// </summary>
        private static Entity CreateFeraldisHuntingLodge(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1000f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_HuntingLodge", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(PopulationProvider));
            em.SetComponentData(entity, new PresentationId { Id = 358 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_HuntingLodge");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new PopulationProvider { Amount = 10 });
            em.AddComponent<HuntingLodgeTag>(entity);
            em.AddComponentData(entity, new SuppliesIncome { PerTick = 15, Interval = 30f, Elapsed = 0f });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Feraldis Logging Station — +10 pop. Passive income near forest areas.
        /// </summary>
        private static Entity CreateFeraldisLoggingStation(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1000f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_LoggingStation", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(PopulationProvider));
            em.SetComponentData(entity, new PresentationId { Id = 359 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_LoggingStation");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new PopulationProvider { Amount = 10 });
            em.AddComponent<LoggingStationTag>(entity);
            em.AddComponentData(entity, new SuppliesIncome { PerTick = 15, Interval = 30f, Elapsed = 0f });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Feraldis Longhouse — batch-trains units. Has BatchTrainingTag.
        /// </summary>
        private static Entity CreateFeraldisLonghouse(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1400f, los = 14f, radius = 1.8f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_Longhouse", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 360 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Longhouse");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<LonghouseTag>(entity);
            em.AddComponent<BatchTrainingTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        /// <summary>
        /// Feraldis Totem Tower — ranged defense (15u range, 12 dmg, 2.0s CD).
        /// </summary>
        private static Entity CreateFeraldisTotemTower(EntityManager em, float3 position, Faction faction)
        {
            float hp = 900f, los = 18f, radius = 0.8f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_Tower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 361 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Tower");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<TotemTowerTag>(entity);
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 15f, Damage = 12, Cooldown = 2.0f, Timer = 0f, MaxTargets = 1
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });
            return entity;
        }

        /// <summary>
        /// Feraldis Siege Yard — trains Siege Ram.
        /// </summary>
        private static Entity CreateFeraldisSiegeYard(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1200f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_SiegeYard", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 362 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_SiegeYard");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<FerSiegeYardTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT CHAPEL BUILDINGS — task-063 phase 1
        // ═══════════════════════════════════════════════════════════════════
        // The 12 old Chapel_Sect_<OldSectId> creators (Renewal, Antiquity,
        // LivingStone, VeiledMemory, StillFlame, QuietVault, MirrorRite,
        // ShardJudgment, EmberAsh, HollowBrand, FlamewroughtChains,
        // UnmakersGrasp) lived here. They've been deleted along with their
        // shared CreateChapel / CreateChapelECB / GetChapelPresentationId
        // helpers. The 12 new chapel creators (Chapel_Sect_Antiquity,
        // Chapel_Sect_Renewal, Chapel_Sect_Fortitude, Chapel_Sect_Reclamation,
        // Chapel_Sect_Silence, Chapel_Sect_Justice, Chapel_Sect_Veneration,
        // Chapel_Sect_Witness, Chapel_Sect_War, Chapel_Sect_Ash,
        // Chapel_Sect_Ruin, Chapel_Sect_Wrath) are deferred to a follow-up
        // task per the user's instructions on this PR.

        // ═══════════════════════════════════════════════════════════════════
        // NEW CULTURE BUILDINGS (EntityManager)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// KingsCourt — Alanthor HQ. +10 pop. Research capable.
        /// </summary>
        private static Entity CreateKingsCourt(EntityManager em, float3 position, Faction faction)
        {
            float hp = 2100f, los = 26f, radius = 2.0f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("KingsCourt", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(PopulationProvider));
            em.SetComponentData(entity, new PresentationId { Id = 363 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("KingsCourt");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new PopulationProvider { Amount = 10 });
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new Defense { Melee = 2, Ranged = 2, Siege = 0, Magic = 1 });
            return entity;
        }

        /// <summary>
        /// Alanthor Crucible — advanced forge building.
        /// </summary>
        private static Entity CreateAlanthorCrucible(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1200f, los = 18f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Crucible", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 364 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Crucible");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<CrucibleTag>(entity);
            em.AddComponentData(entity, new ForgeStorage { Iron = 0, Crystal = 0, MaxIron = 100, MaxCrystal = 50, ConversionTimer = 0f });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        /// <summary>
        /// Runai Vault — resource storage with compound interest.
        /// </summary>
        private static Entity CreateRunaiVault(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1100f, los = 20f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_Vault", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 365 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_Vault");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<VaultTag>(entity);
            em.AddComponentData(entity, new VaultStorage
            {
                StoredAmount = 0f,
                InterestRate = 0.03f,
                ResourceType = 0,
                LockTimer = 0f,
                LockDuration = 180f
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        /// <summary>
        /// Runai Veilsteel Foundry — forge building (reuses Smelter tag).
        /// </summary>
        private static Entity CreateRunaiVeilsteelFoundry(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1500f, los = 20f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_VeilsteelFoundry", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 366 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_VeilsteelFoundry");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SmelterTag>(entity);
            em.AddComponentData(entity, new ForgeStorage { Iron = 0, Crystal = 0, MaxIron = 100, MaxCrystal = 50, ConversionTimer = 0f });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        /// <summary>
        /// Feraldis Foundry — weapon forge building.
        /// </summary>
        private static Entity CreateFeraldisFoundry(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1300f, los = 18f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_Foundry", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 367 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Foundry");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<WarbrandFoundryTag>(entity);
            em.AddComponentData(entity, new ForgeStorage { Iron = 0, Crystal = 0, MaxIron = 100, MaxCrystal = 50, ConversionTimer = 0f });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW CULTURE BUILDINGS (ECB)
        // ═══════════════════════════════════════════════════════════════════

        private static Entity CreateKingsCourtECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 2100f, los = 26f, radius = 2.0f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("KingsCourt", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 363 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("KingsCourt");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 10 });
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 2, Ranged = 2, Siege = 0, Magic = 1 });
            return entity;
        }

        private static Entity CreateAlanthorCrucibleECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1200f, los = 18f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Crucible", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 364 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Crucible");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<CrucibleTag>(entity);
            ecb.AddComponent(entity, new ForgeStorage { Iron = 0, Crystal = 0, MaxIron = 100, MaxCrystal = 50, ConversionTimer = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        private static Entity CreateRunaiVaultECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1100f, los = 20f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_Vault", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 365 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_Vault");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<VaultTag>(entity);
            ecb.AddComponent(entity, new VaultStorage
            {
                StoredAmount = 0f,
                InterestRate = 0.03f,
                ResourceType = 0,
                LockTimer = 0f,
                LockDuration = 180f
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        private static Entity CreateRunaiVeilsteelFoundryECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1500f, los = 20f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_VeilsteelFoundry", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 366 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_VeilsteelFoundry");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SmelterTag>(entity);
            ecb.AddComponent(entity, new ForgeStorage { Iron = 0, Crystal = 0, MaxIron = 100, MaxCrystal = 50, ConversionTimer = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        private static Entity CreateFeraldisFoundryECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1300f, los = 18f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_Foundry", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 367 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Foundry");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<WarbrandFoundryTag>(entity);
            ecb.AddComponent(entity, new ForgeStorage { Iron = 0, Crystal = 0, MaxIron = 100, MaxCrystal = 50, ConversionTimer = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        // ═══════════════════════════════════════════════════════════════════
        // DEFAULT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Default building creation for unknown types.
        /// </summary>
        private static Entity CreateDefault(EntityManager em, string buildingId, float3 position, Faction faction)
        {
            
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = 100 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = 500, Max = 500 });
            em.SetComponentData(entity, new LineOfSight { Radius = 10f });
            var gridSize = BuildingSizeConfig.GetSize(buildingId);
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            return entity;
        }

        /// <summary>
        /// Create Temple of Ridan using EntityCommandBuffer for deferred creation.
        /// </summary>
        private static Entity CreateShrineOfAhridanECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f;
            float los = 16f;
            float radius = 1.8f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("ShrineOfAhridan", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 520 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            ecb.AddComponent<ShrineTag>(entity);
            ecb.AddComponent<ChoiceBuildingTag>(entity);
            ecb.AddComponent(entity, new ShrineRPGranted { Granted = 0 });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        private static Entity CreateTempleOfRidanNewECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1500f;
            float los = 18f;
            float radius = 2.5f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("TempleOfRidan", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 521 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            // BuildingSize so PassabilityBuildingSync blocks the full footprint.
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            ecb.AddComponent<TempleOfRidanTag>(entity);
            ecb.AddComponent<TempleTag>(entity);
            ecb.AddComponent(entity, new TempleLevel { Level = 1 });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Initialize 8 empty chapel slots
            var slotBuffer = ecb.AddBuffer<TempleChapelSlot>(entity);
            for (int i = 0; i < 8; i++)
            {
                slotBuffer.Add(new TempleChapelSlot
                {
                    Chapel = Entity.Null,
                    SectId = default,
                    State = 0,
                    BuildProgress = 0f,
                    BuildTime = 0f
                });
            }

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Vault of Almierra using EntityCommandBuffer for deferred creation.
        /// </summary>
        private static Entity CreateVaultOfAlmierraECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1200f;
            float los = 14f;
            float radius = 2.0f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("VaultOfAlmierra", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 530 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("VaultOfAlmierra");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            ecb.AddComponent<VaultTag>(entity);
            ecb.AddComponent<ChoiceBuildingTag>(entity);
            ecb.AddComponent(entity, new VaultStorage
            {
                ResourceType = 0,
                StoredAmount = 0f,
                InterestRate = 0.03f,
                LockTimer = 0f,
                LockDuration = 180f
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Fiendstone Keep using EntityCommandBuffer for deferred creation.
        /// </summary>
        private static Entity CreateFiendstoneKeepECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 2000f;
            float los = 18f;
            float radius = 2.4f;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("FiendstoneKeep", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 540 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 1 }); // Is a base building
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("FiendstoneKeep");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 20 });

            ecb.AddComponent<FiendstoneKeepTag>(entity);
            ecb.AddComponent<ChoiceBuildingTag>(entity);
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 25f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 3
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

            return entity;
        }

        // ==================== Runai Culture Buildings (ECB) ====================

        private static Entity CreateRunaiOutpostECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 20f, radius = 1.0f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_Outpost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 350 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_Outpost");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<OutpostTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateRunaiTradeHubECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1200f, los = 14f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_TradeHub", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 351 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_TradeHub");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<TradeHubTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateRunaiTradingPostECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_TradingPost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 355 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            // Match the EM variant: use the BuildingSizeConfig footprint so
            // PassabilityBuildingSync blocks the rectangular footprint.
            var gridSize = BuildingSizeConfig.GetSize("Runai_TradingPost");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<TradingPostTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateRunaiBazaarECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 2700f, los = 35f, radius = 2.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("ThessarasBazaar", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 352 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 1 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("ThessarasBazaar");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 40 });
            ecb.AddComponent<BazaarTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(4f, 0, 4f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateRunaiSiegeWorkshopECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1100f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_SiegeWorkshop", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 353 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Runai_SiegeWorkshop");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<SiegeWorkshopTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        // ==================== Alanthor Culture Buildings (ECB) ====================

        private static Entity CreateAlanthorWatchTowerECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 950f, los = 22f, radius = 0.8f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Tower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 354 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Tower");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<WatchTowerTag>(entity);
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 18f, Damage = 14, Cooldown = 2.0f, Timer = 0f, MaxTargets = 1
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            return entity;
        }

        private static Entity CreateAlanthorGarrisonECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1500f, los = 14f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Garrison", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 355 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Garrison");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 8 });
            ecb.AddComponent<GarrisonTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateAlanthorRoyalStableECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1300f, los = 14f, radius = 1.5f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_Stable", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 356 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Stable");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<RoyalStableTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateAlanthorSiegeYardECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1100f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Alanthor_SiegeYard", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 357 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_SiegeYard");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<SiegeYardTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        // ==================== Feraldis Culture Buildings (ECB) ====================

        private static Entity CreateFeraldisHuntingLodgeECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1000f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_HuntingLodge", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 358 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_HuntingLodge");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 10 });
            ecb.AddComponent<HuntingLodgeTag>(entity);
            ecb.AddComponent(entity, new SuppliesIncome { PerTick = 15, Interval = 30f, Elapsed = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateFeraldisLoggingStationECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1000f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_LoggingStation", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 359 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_LoggingStation");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 10 });
            ecb.AddComponent<LoggingStationTag>(entity);
            ecb.AddComponent(entity, new SuppliesIncome { PerTick = 15, Interval = 30f, Elapsed = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateFeraldisLonghouseECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1400f, los = 14f, radius = 1.8f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_Longhouse", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 360 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Longhouse");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<LonghouseTag>(entity);
            ecb.AddComponent<BatchTrainingTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateFeraldisTotemTowerECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 18f, radius = 0.8f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_Tower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 361 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Tower");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<TotemTowerTag>(entity);
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 15f, Damage = 12, Cooldown = 2.0f, Timer = 0f, MaxTargets = 1
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            return entity;
        }

        private static Entity CreateFeraldisSiegeYardECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1200f, los = 14f, radius = 1.2f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Feraldis_SiegeYard", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 362 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_SiegeYard");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<FerSiegeYardTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateDefault(EntityCommandBuffer ecb, string buildingId, float3 position, Faction faction)
        {

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 100 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = 500, Max = 500 });
            ecb.AddComponent(entity, new LineOfSight { Radius = 10f });
            var gridSize = BuildingSizeConfig.GetSize(buildingId);
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            return entity;
        }

        // ═══════════════════════════════════════════════════════════════════
        // TEMPLE CHAPEL SLOT HELPERS — task-063 phase 1
        // ═══════════════════════════════════════════════════════════════════
        // GetChapelSlotOffset / CreateChapelAtSlot / ChapelSlotCount /
        // ChapelSlotRadius were tied to the old chapel creators (which
        // themselves referenced removed sect IDs). They've been deleted along
        // with CreateChapel / CreateChapelECB / GetChapelPresentationId. The
        // TempleChapelSlot dynamic buffer + 6-slot UI mechanic stay; new
        // chapel-creation helpers will land alongside the new chapel building
        // IDs in a follow-up task.

        // ═══════════════════════════════════════════════════════════════════════
        // SECT UNIQUE BUILDINGS — task-063 phase 1
        // ═══════════════════════════════════════════════════════════════════════
        // The 24 old creators (12 EM + 12 ECB) for the old sect-unique
        // buildings (Sect_Sanctuary / Sect_ArchiveTower / Sect_StoneheartBastion
        // / Sect_VeilSpire / Sect_FlameBeacon / Sect_Strongbox /
        // Sect_GlassSanctum / Sect_Tribunal / Sect_WarPyre / Sect_DreadTotem
        // / Sect_BindingPillar / Sect_PurgeAltar) are deleted. Phase 2 will
        // reintroduce the new sect-unique buildings (Reliquary / Workshop
        // Eternal / Oath-Stone / Crucible / Sepulchre / Tribunal /
        // Sanctified Pyre / Spire of Witness / War Forge / Furnace /
        // Desecrator / Hollow Altar) — one per new-roster sect.
        // SectUniqueBuildingTag is preserved for Phase 2 reuse.

    }
    // TempleTag and VaultTag are defined in BuildingComponents.cs (global namespace)
}