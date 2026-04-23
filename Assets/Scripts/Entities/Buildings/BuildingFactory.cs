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
                // Sect chapel buildings
                "Chapel_Sect_Renewal" => CreateChapel(em, SectConfig.Renewal, position, faction),
                "Chapel_Sect_Antiquity" => CreateChapel(em, SectConfig.Antiquity, position, faction),
                "Chapel_Sect_LivingStone" => CreateChapel(em, SectConfig.LivingStone, position, faction),
                "Chapel_Sect_VeiledMemory" => CreateChapel(em, SectConfig.VeiledMemory, position, faction),
                "Chapel_Sect_StillFlame" => CreateChapel(em, SectConfig.StillFlame, position, faction),
                "Chapel_Sect_QuietVault" => CreateChapel(em, SectConfig.QuietVault, position, faction),
                "Chapel_Sect_MirrorRite" => CreateChapel(em, SectConfig.MirrorRite, position, faction),
                "Chapel_Sect_ShardJudgment" => CreateChapel(em, SectConfig.ShardJudgment, position, faction),
                "Chapel_Sect_EmberAsh" => CreateChapel(em, SectConfig.EmberAsh, position, faction),
                "Chapel_Sect_HollowBrand" => CreateChapel(em, SectConfig.HollowBrand, position, faction),
                "Chapel_Sect_FlamewroughtChains" => CreateChapel(em, SectConfig.FlamewroughtChains, position, faction),
                "Chapel_Sect_UnmakersGrasp" => CreateChapel(em, SectConfig.UnmakersGrasp, position, faction),
                // Sect unique buildings
                "Sect_Sanctuary" => CreateSectSanctuary(em, position, faction),
                "Sect_ArchiveTower" => CreateSectArchiveTower(em, position, faction),
                "Sect_StoneheartBastion" => CreateSectStoneheartBastion(em, position, faction),
                "Sect_VeilSpire" => CreateSectVeilSpire(em, position, faction),
                "Sect_FlameBeacon" => CreateSectFlameBeacon(em, position, faction),
                "Sect_Strongbox" => CreateSectStrongbox(em, position, faction),
                "Sect_GlassSanctum" => CreateSectGlassSanctum(em, position, faction),
                "Sect_Tribunal" => CreateSectTribunal(em, position, faction),
                "Sect_WarPyre" => CreateSectWarPyre(em, position, faction),
                "Sect_DreadTotem" => CreateSectDreadTotem(em, position, faction),
                "Sect_BindingPillar" => CreateSectBindingPillar(em, position, faction),
                "Sect_PurgeAltar" => CreateSectPurgeAltar(em, position, faction),
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
                // Sect chapel buildings
                "Chapel_Sect_Renewal" => CreateChapelECB(ecb, SectConfig.Renewal, position, faction),
                "Chapel_Sect_Antiquity" => CreateChapelECB(ecb, SectConfig.Antiquity, position, faction),
                "Chapel_Sect_LivingStone" => CreateChapelECB(ecb, SectConfig.LivingStone, position, faction),
                "Chapel_Sect_VeiledMemory" => CreateChapelECB(ecb, SectConfig.VeiledMemory, position, faction),
                "Chapel_Sect_StillFlame" => CreateChapelECB(ecb, SectConfig.StillFlame, position, faction),
                "Chapel_Sect_QuietVault" => CreateChapelECB(ecb, SectConfig.QuietVault, position, faction),
                "Chapel_Sect_MirrorRite" => CreateChapelECB(ecb, SectConfig.MirrorRite, position, faction),
                "Chapel_Sect_ShardJudgment" => CreateChapelECB(ecb, SectConfig.ShardJudgment, position, faction),
                "Chapel_Sect_EmberAsh" => CreateChapelECB(ecb, SectConfig.EmberAsh, position, faction),
                "Chapel_Sect_HollowBrand" => CreateChapelECB(ecb, SectConfig.HollowBrand, position, faction),
                "Chapel_Sect_FlamewroughtChains" => CreateChapelECB(ecb, SectConfig.FlamewroughtChains, position, faction),
                "Chapel_Sect_UnmakersGrasp" => CreateChapelECB(ecb, SectConfig.UnmakersGrasp, position, faction),
                // Sect unique buildings
                "Sect_Sanctuary" => CreateSectSanctuaryECB(ecb, position, faction),
                "Sect_ArchiveTower" => CreateSectArchiveTowerECB(ecb, position, faction),
                "Sect_StoneheartBastion" => CreateSectStoneheartBastionECB(ecb, position, faction),
                "Sect_VeilSpire" => CreateSectVeilSpireECB(ecb, position, faction),
                "Sect_FlameBeacon" => CreateSectFlameBeaconECB(ecb, position, faction),
                "Sect_Strongbox" => CreateSectStrongboxECB(ecb, position, faction),
                "Sect_GlassSanctum" => CreateSectGlassSanctumECB(ecb, position, faction),
                "Sect_Tribunal" => CreateSectTribunalECB(ecb, position, faction),
                "Sect_WarPyre" => CreateSectWarPyreECB(ecb, position, faction),
                "Sect_DreadTotem" => CreateSectDreadTotemECB(ecb, position, faction),
                "Sect_BindingPillar" => CreateSectBindingPillarECB(ecb, position, faction),
                "Sect_PurgeAltar" => CreateSectPurgeAltarECB(ecb, position, faction),
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
                // Sect chapel buildings
                "Chapel_Sect_Renewal" => 390,
                "Chapel_Sect_Antiquity" => 391,
                "Chapel_Sect_LivingStone" => 392,
                "Chapel_Sect_VeiledMemory" => 393,
                "Chapel_Sect_StillFlame" => 394,
                "Chapel_Sect_QuietVault" => 395,
                "Chapel_Sect_MirrorRite" => 396,
                "Chapel_Sect_ShardJudgment" => 397,
                "Chapel_Sect_EmberAsh" => 398,
                "Chapel_Sect_HollowBrand" => 399,
                "Chapel_Sect_FlamewroughtChains" => 400,
                "Chapel_Sect_UnmakersGrasp" => 401,
                // Sect unique buildings
                "Sect_Sanctuary" => 410, "Sect_ArchiveTower" => 411, "Sect_StoneheartBastion" => 412,
                "Sect_VeilSpire" => 413, "Sect_FlameBeacon" => 414, "Sect_Strongbox" => 415,
                "Sect_GlassSanctum" => 416, "Sect_Tribunal" => 417, "Sect_WarPyre" => 418,
                "Sect_DreadTotem" => 419, "Sect_BindingPillar" => 420, "Sect_PurgeAltar" => 421,
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
                // Sect chapels can train their unique unit
                "Chapel_Sect_Renewal" or "Chapel_Sect_Antiquity" or
                "Chapel_Sect_LivingStone" or "Chapel_Sect_VeiledMemory" or
                "Chapel_Sect_StillFlame" or "Chapel_Sect_QuietVault" or
                "Chapel_Sect_MirrorRite" or "Chapel_Sect_ShardJudgment" or
                "Chapel_Sect_EmberAsh" or "Chapel_Sect_HollowBrand" or
                "Chapel_Sect_FlamewroughtChains" or "Chapel_Sect_UnmakersGrasp" => true,
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
                typeof(TrainingState)
            );

            em.SetComponentData(entity, new PresentationId { Id = 521 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Radius { Value = radius });
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
            float hp = 800f, los = 16f, radius = 1.0f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_TradingPost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 355 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Radius { Value = radius });
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
        // SECT CHAPEL BUILDINGS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the chapel presentation ID for a sect.
        /// Maps sect index in AllSectIds to PID 390-401.
        /// </summary>
        private static int GetChapelPresentationId(string sectId)
        {
            for (int i = 0; i < SectConfig.AllSectIds.Length; i++)
            {
                if (SectConfig.AllSectIds[i] == sectId) return 390 + i;
            }
            return 390;
        }

        /// <summary>
        /// Create a sect chapel building. All chapels share the same base stats.
        /// 600 HP, Radius 1.0, LOS 12, ArmorType StructureHuman.
        /// Trains the sect's unique unit and researches the sect's tech.
        /// </summary>
        private static Entity CreateChapel(EntityManager em, string sectId, float3 position, Faction faction)
        {
            float hp = 600f;
            float los = 12f;
            int pid = GetChapelPresentationId(sectId);

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

            em.SetComponentData(entity, new PresentationId { Id = pid });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Chapel_Small");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });

            em.AddComponentData(entity, new ChapelTag { SectId = new Unity.Collections.FixedString64Bytes(sectId) });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }

        /// <summary>
        /// Create a sect chapel building using EntityCommandBuffer.
        /// </summary>
        private static Entity CreateChapelECB(EntityCommandBuffer ecb, string sectId, float3 position, Faction faction)
        {
            float hp = 600f;
            float los = 12f;
            int pid = GetChapelPresentationId(sectId);

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = pid });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Chapel_Small");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            ecb.AddComponent(entity, new ChapelTag { SectId = new Unity.Collections.FixedString64Bytes(sectId) });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }

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
            ecb.AddComponent(entity, new Radius { Value = radius });
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
            float hp = 800f, los = 16f, radius = 1.0f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Runai_TradingPost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 355 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Radius { Value = radius });
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
        // TEMPLE CHAPEL SLOT HELPERS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Number of chapel build slots around a temple.</summary>
        public const int ChapelSlotCount = 7;

        /// <summary>Radius of the chapel slot ring around the temple center.</summary>
        private const float ChapelSlotRadius = 4.0f;

        /// <summary>
        /// Get the offset position for a chapel slot relative to the temple center.
        /// Slot 0 is at the top (north), arranged clockwise.
        /// </summary>
        public static float3 GetChapelSlotOffset(int slotIndex)
        {
            float angle = slotIndex * (2f * math.PI / ChapelSlotCount) - (math.PI / 2f);
            return new float3(math.cos(angle) * ChapelSlotRadius, 0, math.sin(angle) * ChapelSlotRadius);
        }

        /// <summary>
        /// Creates a chapel entity at a specific temple slot position.
        /// Does NOT add UnderConstruction — the temple handles the build timer internally via TempleChapelSlot.
        /// Adds TempleOwner to link the chapel back to its parent temple.
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="sectId">Sect identifier (e.g., "Sect_Renewal")</param>
        /// <param name="temple">The parent temple entity</param>
        /// <param name="slotIndex">Slot index (0-6)</param>
        /// <param name="faction">Owning faction</param>
        /// <returns>Created chapel entity</returns>
        public static Entity CreateChapelAtSlot(EntityManager em, string sectId, Entity temple, int slotIndex, Faction faction)
        {
            var templeTransform = em.GetComponentData<LocalTransform>(temple);
            float3 offset = GetChapelSlotOffset(slotIndex);
            float3 chapelPos = templeTransform.Position + offset;

            // Create the chapel via the existing factory method
            Entity chapel = CreateChapel(em, sectId, chapelPos, faction);

            // Add ChapelSmallTag (needed for RP bonus calculation in BuildingConstructionSystem)
            em.AddComponent<ChapelSmallTag>(chapel);

            // Link chapel to parent temple
            em.AddComponentData(chapel, new TempleOwner
            {
                Temple = temple,
                SlotIndex = slotIndex
            });

            return chapel;
        }
        // ═══════════════════════════════════════════════════════════════════════
        // SECT UNIQUE BUILDINGS — EntityManager versions
        // ═══════════════════════════════════════════════════════════════════════

        private static Entity CreateSectSanctuary(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_Sanctuary", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 410 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_Sanctuary");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectArchiveTower(EntityManager em, float3 position, Faction faction)
        {
            float hp = 900f, los = 18f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_ArchiveTower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 411 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_ArchiveTower");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectStoneheartBastion(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1200f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_StoneheartBastion", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 412 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_StoneheartBastion");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectVeilSpire(EntityManager em, float3 position, Faction faction)
        {
            float hp = 600f, los = 30f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_VeilSpire", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 413 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_VeilSpire");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectFlameBeacon(EntityManager em, float3 position, Faction faction)
        {
            float hp = 700f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_FlameBeacon", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 414 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_FlameBeacon");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectStrongbox(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1000f, los = 12f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_Strongbox", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 415 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_Strongbox");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponent<VaultTag>(entity);
            em.AddComponentData(entity, new VaultStorage
            {
                StoredAmount = 0f,
                InterestRate = 0.05f,
                ResourceType = 0,
                LockTimer = 0f,
                LockDuration = 180f
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectGlassSanctum(EntityManager em, float3 position, Faction faction)
        {
            float hp = 700f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_GlassSanctum", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 416 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_GlassSanctum");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectTribunal(EntityManager em, float3 position, Faction faction)
        {
            float hp = 900f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_Tribunal", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 417 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_Tribunal");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectWarPyre(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_WarPyre", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 418 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_WarPyre");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectDreadTotem(EntityManager em, float3 position, Faction faction)
        {
            float hp = 700f, los = 15f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_DreadTotem", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 419 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_DreadTotem");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectBindingPillar(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_BindingPillar", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 420 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_BindingPillar");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectPurgeAltar(EntityManager em, float3 position, Faction faction)
        {
            float hp = 900f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_PurgeAltar", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 421 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_PurgeAltar");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<SectUniqueBuildingTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SECT UNIQUE BUILDINGS — ECB versions
        // ═══════════════════════════════════════════════════════════════════════

        private static Entity CreateSectSanctuaryECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_Sanctuary", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 410 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_Sanctuary");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectArchiveTowerECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 18f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_ArchiveTower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 411 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_ArchiveTower");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectStoneheartBastionECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1200f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_StoneheartBastion", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 412 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_StoneheartBastion");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectVeilSpireECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 600f, los = 30f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_VeilSpire", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 413 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_VeilSpire");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectFlameBeaconECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 700f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_FlameBeacon", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 414 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_FlameBeacon");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectStrongboxECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1000f, los = 12f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_Strongbox", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 415 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_Strongbox");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent<VaultTag>(entity);
            ecb.AddComponent(entity, new VaultStorage
            {
                StoredAmount = 0f,
                InterestRate = 0.05f,
                ResourceType = 0,
                LockTimer = 0f,
                LockDuration = 180f
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectGlassSanctumECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 700f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_GlassSanctum", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 416 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_GlassSanctum");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectTribunalECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_Tribunal", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 417 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_Tribunal");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectWarPyreECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_WarPyre", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 418 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_WarPyre");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectDreadTotemECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 700f, los = 15f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_DreadTotem", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 419 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_DreadTotem");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectBindingPillarECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f, los = 14f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_BindingPillar", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 420 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_BindingPillar");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        private static Entity CreateSectPurgeAltarECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 16f;
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("Sect_PurgeAltar", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 421 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Sect_PurgeAltar");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<SectUniqueBuildingTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }
    }
    // TempleTag and VaultTag are defined in BuildingComponents.cs (global namespace)
}