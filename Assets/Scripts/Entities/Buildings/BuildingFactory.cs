// File: Assets/Scripts/Entities/Buildings/BuildingFactory.cs
using System;
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
        private readonly struct BuildingRecipe
        {
            public readonly Func<EntityManager, float3, Faction, Entity> CreateEm;
            // Null = this building has no ECB construction path (e.g. Alanthor_Wall
            // hubs are EntityManager-only); the ECB Create falls back to CreateDefault.
            public readonly Func<EntityCommandBuffer, float3, Faction, Entity> CreateEcb;
            public readonly int PresentationId;

            public BuildingRecipe(Func<EntityManager, float3, Faction, Entity> createEm,
                                  Func<EntityCommandBuffer, float3, Faction, Entity> createEcb,
                                  int presentationId)
            {
                CreateEm = createEm;
                CreateEcb = createEcb;
                PresentationId = presentationId;
            }
        }

        /// <summary>
        /// Single source of truth: id -> (EM ctor, ECB ctor, presentation id).
        /// Chapel ids (task-063: Chapel_Sect_*) are handled separately in
        /// Create/GetPresentationId because they parameterize one shared creator
        /// by sect id. Unknown ids fall back to CreateDefault / PID 100.
        /// </summary>
        private static readonly Dictionary<string, BuildingRecipe> Recipes = BuildRecipes();

        private static Dictionary<string, BuildingRecipe> BuildRecipes()
        {
            return new Dictionary<string, BuildingRecipe>
            {
                ["Hall"]            = new BuildingRecipe(Hall.Create, Hall.Create, 100),
                ["Hut"]             = new BuildingRecipe(Hut.Create, Hut.Create, 102),
                ["GatherersHut"]    = new BuildingRecipe(GatherersHut.Create, GatherersHut.Create, 101),
                ["Barracks"]        = new BuildingRecipe(Barracks.Create, Barracks.Create, 510),
                ["ArcheryRange"]    = new BuildingRecipe(ArcheryRange.Create, ArcheryRange.Create, 511),
                ["ShrineOfRidan"]   = new BuildingRecipe(CreateShrineOfRidan, CreateShrineOfRidanECB, 520),
                // Legacy id alias — pre-rename build orders / saves still say
                // "ShrineOfAhridan"; keep them routing to the same creator.
                ["ShrineOfAhridan"] = new BuildingRecipe(CreateShrineOfRidan, CreateShrineOfRidanECB, 520),
                ["TempleOfRidan"]   = new BuildingRecipe(CreateTempleOfRidanNew, CreateTempleOfRidanNewECB, 521),
                ["VaultOfAlmierra"] = new BuildingRecipe(CreateVaultOfAlmierra, CreateVaultOfAlmierraECB, 530),
                ["FiendstoneKeep"]  = new BuildingRecipe(CreateFiendstoneKeep, CreateFiendstoneKeepECB, 540),
                ["Alanthor_Wall"]   = new BuildingRecipe(AlanthorWall.CreateHub, null, AlanthorWall.HubPresentationID),
                ["Alanthor_Smelter"] = new BuildingRecipe(Smelter.Create, Smelter.Create, Smelter.PresentationID),

                // Runai culture buildings
                ["Runai_Outpost"]      = new BuildingRecipe(CreateRunaiOutpost, CreateRunaiOutpostECB, 350),
                ["Runai_TradeHub"]     = new BuildingRecipe(CreateRunaiTradeHub, CreateRunaiTradeHubECB, 351),
                // (task-062 Q-39 — was missing, falling through to 100/default.)
                ["Runai_TradingPost"]  = new BuildingRecipe(CreateRunaiTradingPost, CreateRunaiTradingPostECB, 355),
                ["ThessarasBazaar"]    = new BuildingRecipe(CreateRunaiBazaar, CreateRunaiBazaarECB, 352),
                ["Runai_SiegeWorkshop"] = new BuildingRecipe(CreateRunaiSiegeWorkshop, CreateRunaiSiegeWorkshopECB, 353),
                ["Runai_Vault"]        = new BuildingRecipe(CreateRunaiVault, CreateRunaiVaultECB, 365),
                ["Runai_VeilsteelFoundry"] = new BuildingRecipe(CreateRunaiVeilsteelFoundry, CreateRunaiVeilsteelFoundryECB, 366),

                // Alanthor culture buildings. The Practice Range is the LEVELED
                // Archery Range (not a placeable building) and the Crucible was
                // deleted (the Smelter absorbs its veilsteel role) — calculator
                // consolidation 2026-08.
                ["Alanthor_Tower"]         = new BuildingRecipe(CreateAlanthorWatchTower, CreateAlanthorWatchTowerECB, 354),
                ["Alanthor_SiegeYard"]     = new BuildingRecipe(CreateAlanthorSiegeYard, CreateAlanthorSiegeYardECB, 357),
                ["KingsCourt"]             = new BuildingRecipe(CreateKingsCourt, CreateKingsCourtECB, 363),
                ["Alanthor_RoyalStable"]   = new BuildingRecipe(RoyalStable.Create, RoyalStable.Create, RoyalStable.PresentationID),

                // Feraldis culture buildings
                ["Feraldis_HuntingLodge"]   = new BuildingRecipe(CreateFeraldisHuntingLodge, CreateFeraldisHuntingLodgeECB, 358),
                ["Feraldis_LoggingStation"] = new BuildingRecipe(CreateFeraldisLoggingStation, CreateFeraldisLoggingStationECB, 359),
                ["Feraldis_Longhouse"]      = new BuildingRecipe(CreateFeraldisLonghouse, CreateFeraldisLonghouseECB, 360),
                ["Feraldis_Tower"]          = new BuildingRecipe(CreateFeraldisTotemTower, CreateFeraldisTotemTowerECB, 361),
                ["Feraldis_SiegeYard"]      = new BuildingRecipe(CreateFeraldisSiegeYard, CreateFeraldisSiegeYardECB, 362),
                ["Feraldis_Foundry"]        = new BuildingRecipe(CreateFeraldisFoundry, CreateFeraldisFoundryECB, 367),
                ["Feraldis_WarTotem"]       = new BuildingRecipe(WarTotem.Create, WarTotem.Create, WarTotem.PresentationID),
                ["Feraldis_Pasture"]        = new BuildingRecipe(Pasture.Create, Pasture.Create, Pasture.PresentationID),
                ["Mine"]                    = new BuildingRecipe(Mine.Create, Mine.Create, Mine.PresentationID),
            };
        }

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
            Entity entity;
            if (Recipes.TryGetValue(buildingId, out var recipe))
                entity = recipe.CreateEm(em, position, faction);
            // task-063 phase 2a: 12 chapel building IDs (Chapel_Sect_Antiquity
            // .. Chapel_Sect_Wrath) all dispatch to a single uniform creator
            // that stamps ChapelTag.SectId from the chapel-id suffix. Visual
            // differentiation per sect lands in a Phase 5 polish pass.
            else if (SectConfig.IsChapelId(buildingId))
                entity = CreateChapel(em, position, faction, SectConfig.SectIdFromChapelId(buildingId));
            else
                entity = CreateDefault(em, buildingId, position, faction);

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            em.AddComponentData(entity, MakeDisplayName(buildingId));

            return entity;
        }

        /// <summary>
        /// Record the exact name of what was asked for. The selection UI used to
        /// re-derive this from a tag-component ladder, which several buildings
        /// never appear in — KingsCourt carries no distinguishing tag at all, and
        /// all 12 chapels share one ChapelTag — so they displayed as bare
        /// "Building". The id the caller passed is unambiguous.
        /// </summary>
        private static DisplayName MakeDisplayName(string buildingId)
            => new DisplayName { Value = TheWaningBorder.Core.DisplayNames.ForBuildingFixed(buildingId) };

        /// <summary>
        /// Create a building using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, string buildingId, float3 position, Faction faction)
        {
            Entity entity;
            if (Recipes.TryGetValue(buildingId, out var recipe) && recipe.CreateEcb != null)
                entity = recipe.CreateEcb(ecb, position, faction);
            // task-063 phase 2a: chapels dispatch to a single uniform ECB creator
            // (parameterised by sect id parsed from the chapel building id).
            else if (SectConfig.IsChapelId(buildingId))
                entity = CreateChapelECB(ecb, position, faction, SectConfig.SectIdFromChapelId(buildingId));
            else
                entity = CreateDefault(ecb, buildingId, position, faction);

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            ecb.AddComponent(entity, MakeDisplayName(buildingId));

            return entity;
        }

        /// <summary>
        /// Get the PresentationId for a building type.
        /// </summary>
        public static int GetPresentationId(string buildingId)
        {
            if (Recipes.TryGetValue(buildingId, out var recipe)) return recipe.PresentationId;
            // task-063 phase 5 (2026-08-02): per-sect chapel visuals — pid by
            // sect index (390-399 for indices 0-9; 400-403 are taken by other
            // entries, so Ruin/Wrath sit at 410/411).
            // Phase 5 polish will introduce per-sect visual variation.
            if (SectConfig.IsChapelId(buildingId))
                return ChapelPidForSect(SectConfig.SectIdFromChapelId(buildingId));
            return 100;
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
                "ShrineOfRidan" or "ShrineOfAhridan" => true,
                "TempleOfRidan" => true,
                "Runai_TradeHub" => true,
                "ThessarasBazaar" => true,
                "Runai_SiegeWorkshop" => true,
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
            "ShrineOfRidan", "VaultOfAlmierra", "FiendstoneKeep",
            "ShrineOfAhridan", // legacy id alias (pre-rename callers)
        };

        /// <summary>
        /// Returns true if the given building ID is one of the 3 choice buildings.
        /// </summary>
        public static bool IsChoiceBuilding(string buildingId) => ChoiceBuildingIds.Contains(buildingId);

        /// <summary>
        /// Check if a faction already has a choice building (built or under construction).
        /// Returns the building ID if one exists, null otherwise.
        /// Use this for "can the player place a SECOND choice building?" gating —
        /// for the age-up gate, see <see cref="GetCompletedFactionChoiceBuilding"/>.
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
        /// Like <see cref="GetFactionChoiceBuilding"/>, but only returns a
        /// non-null result if the choice building is COMPLETED (no
        /// UnderConstruction component). Used by the age-up gate so players
        /// (and AI) can't research culture choice / advance era while the
        /// Shrine / Vault / Keep is still being built.
        /// </summary>
        public static string GetCompletedFactionChoiceBuilding(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                typeof(ChoiceBuildingTag), typeof(FactionTag), typeof(BuildingTag));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            string result = null;
            for (int i = 0; i < entities.Length; i++)
            {
                var fac = em.GetComponentData<FactionTag>(entities[i]).Value;
                if (fac != faction) continue;
                if (em.HasComponent<UnderConstruction>(entities[i])) continue;
                result = GetBuildingIdFromEntity(em, entities[i]);
                break;
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
            if (em.HasComponent<ShrineTag>(entity)) return "ShrineOfRidan";
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

            if (TechCatalog.TryGetBuilding(buildingId, out var def))
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

            if (TechCatalog.TryGetBuilding("FiendstoneKeep", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            // Reinforced Walls (Age 0 Keep tech): keeps built after the
            // research start with the +20% Max HP bonus (existing keeps are
            // bumped by TechEffectSystem when the research completes).
            var keepResearch = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (keepResearch != null && keepResearch.HasResearched(faction, "ReinforcedWalls"))
                hp *= 1.2f;

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
            // Auto-fire per Age 0 design (Q#3): 4 targets, 20 dmg / 2.0 s, range 30.
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 30f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 4
            });

            // Keep tech ladder (emplacements / towers / walls) researches here.
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Wing slots (choice-building leveling): the Keep levels by
            // building up to three wings — see KeepWingSystem.
            em.AddComponentData(entity, new KeepWings());

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });

            return entity;
        }

        /// <summary>
        /// Create Shrine of Ridan — choice building that trains litharchs fast and grants +1 RP.
        /// One of three mutually exclusive choice buildings (Shrine/Vault/Keep).
        /// </summary>
        private static Entity CreateShrineOfRidan(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f;
            float los = 16f;
            float radius = 1.8f;

            if (TechCatalog.TryGetBuilding("ShrineOfRidan", out var def))
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

            // Shrine tech ladder (masses / warrior priests) researches here.
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Simple upgrade ladder (heal aura + Litharchs + sect power CDR).
            em.AddComponent<BuildingUpgradeable>(entity);

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
        /// <summary>Per-sect chapel presentation id: 390 + sect index for
        /// indices 0-9; Ruin/Wrath (10, 11) sit at 410/411 because the
        /// 400-403 block is taken. Unknown sect falls back to 390.</summary>
        internal static int ChapelPidForSect(string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return 390;
            // 400-403 are Forest/Rock/deposits and 410 is the BazaarWagon —
            // Ruin/Wrath (indices 10/11) sit at 412/413.
            return idx < 10 ? 390 + idx : 402 + idx;
        }

        private static Entity CreateChapel(EntityManager em, float3 position, Faction faction, string sectId)
        {
            int ChapelPresentationId = ChapelPidForSect(sectId);
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
        private static Entity CreateChapelECB(EntityCommandBuffer ecb, float3 position, Faction faction, string sectId)
        {
            int ChapelPresentationId = ChapelPidForSect(sectId);
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

        /// <summary>
        /// The Reliquary — Sect of Antiquity's building lever (task-063,
        /// implemented 2026-07-05). One per faction; built from the Antiquity
        /// chapel's panel, spawns UNDER CONSTRUCTION beside the chapel and is
        /// finished by builders like any structure. Carries ReliquaryState
        /// (three ability cooldowns — see ReliquarySystem / ReliquaryHelper).
        /// Reuses the Antiquity chapel visual (resolved via ChapelPidForSect —
        /// after the 2026-08-02 pid rework Antiquity sits at 390, not 391).
        /// </summary>
        public static Entity CreateReliquaryUnderConstruction(EntityManager em, float3 position, Faction faction)
        {
            int PresentationId391 = ChapelPidForSect(SectConfig.Antiquity);
            const float Hp = 900f;
            const float Los = 16f;
            const float BuildTime = 40f;

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(ReliquaryTag),
                typeof(ReliquaryState),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(UnderConstruction),
                typeof(Buildable)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationId391 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new ReliquaryState());
            em.SetComponentData(entity, new Health { Value = 1, Max = (int)Hp });
            em.SetComponentData(entity, new LineOfSight { Radius = Los });

            var gridSize = BuildingSizeConfig.GetSize("Sect_Reliquary");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new UnderConstruction { Progress = 0f, Total = BuildTime });
            em.SetComponentData(entity, new Buildable { BuildTimeSeconds = BuildTime });

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

            if (TechCatalog.TryGetBuilding("TempleOfRidan", out var def))
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

            // Glow storage lives on the Temple per spec refinement #2
            // (the standalone GlowReliquary was deleted).
            em.AddComponentData(entity, new GlowStored { Amount = 0 });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Research state for sect techs
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Initialize 6 empty chapel slots — one per ground decal around the
            // Temple. Six matches SectConfig.MaxAdoptedSects (= the design cap).
            var slotBuffer = em.AddBuffer<TempleChapelSlot>(entity);
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
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

            if (TechCatalog.TryGetBuilding("VaultOfAlmierra", out var def))
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
                InterestRate = 0.25f,
                LockTimer = 0f,
                LockDuration = 180f
            });

            // Banking tech ladder (interest grades + resource unlocks) researches here.
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Simple upgrade ladder (interest yields + wall productivity).
            em.AddComponent<BuildingUpgradeable>(entity);

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
            if (TechCatalog.TryGetBuilding("Runai_Outpost", out var def))
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
            if (TechCatalog.TryGetBuilding("Runai_TradeHub", out var def))
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
            if (TechCatalog.TryGetBuilding("Runai_TradingPost", out var def))
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
            if (TechCatalog.TryGetBuilding("ThessarasBazaar", out var def))
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
            if (TechCatalog.TryGetBuilding("Runai_SiegeWorkshop", out var def))
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
            if (TechCatalog.TryGetBuilding("Alanthor_Tower", out var def))
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
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Tower").
            em.AddComponent<BuildingUpgradeable>(entity);
            return entity;
        }

        /// <summary>
        /// Alanthor Siege Yard — trains Ballista.
        /// </summary>
        private static Entity CreateAlanthorSiegeYard(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1100f, los = 14f, radius = 1.2f;
            if (TechCatalog.TryGetBuilding("Alanthor_SiegeYard", out var def))
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
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_SiegeYard").
            em.AddComponent<BuildingUpgradeable>(entity);
            return entity;
        }

        // ==================== Feraldis Culture Buildings (EntityManager) ====================

        /// <summary>
        /// Feraldis Hunting Lodge — +10 pop. Passive income near wildlife areas.
        /// </summary>
        private static Entity CreateFeraldisHuntingLodge(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1000f, los = 14f, radius = 1.2f;
            if (TechCatalog.TryGetBuilding("Feraldis_HuntingLodge", out var def))
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
            if (TechCatalog.TryGetBuilding("Feraldis_LoggingStation", out var def))
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
            // Doc §5.7 #11: cultured Barracks — base 800 × 1.10 = 880 at L1.
            float hp = 880f, los = 14f, radius = 1.8f;
            if (TechCatalog.TryGetBuilding("Feraldis_Longhouse", out var def))
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
            if (TechCatalog.TryGetBuilding("Feraldis_Tower", out var def))
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
            if (TechCatalog.TryGetBuilding("Feraldis_SiegeYard", out var def))
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
            if (TechCatalog.TryGetBuilding("KingsCourt", out var def))
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
        /// Runai Vault — resource storage with compound interest.
        /// </summary>
        private static Entity CreateRunaiVault(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1100f, los = 20f, radius = 1.5f;
            if (TechCatalog.TryGetBuilding("Runai_Vault", out var def))
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
                InterestRate = 0.25f,
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
            if (TechCatalog.TryGetBuilding("Runai_VeilsteelFoundry", out var def))
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
            em.AddComponentData(entity, new ForgeStorage { Iron = 0, Veilstone = 0, MaxIron = 100, MaxVeilstone = 50, ConversionTimer = 0f });
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
            if (TechCatalog.TryGetBuilding("Feraldis_Foundry", out var def))
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
            em.AddComponentData(entity, new ForgeStorage { Iron = 0, Veilstone = 0, MaxIron = 100, MaxVeilstone = 50, ConversionTimer = 0f });
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
            if (TechCatalog.TryGetBuilding("KingsCourt", out var def))
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

        private static Entity CreateRunaiVaultECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1100f, los = 20f, radius = 1.5f;
            if (TechCatalog.TryGetBuilding("Runai_Vault", out var def))
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
                InterestRate = 0.25f,
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
            if (TechCatalog.TryGetBuilding("Runai_VeilsteelFoundry", out var def))
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
            ecb.AddComponent(entity, new ForgeStorage { Iron = 0, Veilstone = 0, MaxIron = 100, MaxVeilstone = 50, ConversionTimer = 0f });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new Defense { Melee = 1, Ranged = 1, Siege = 0, Magic = 0 });
            return entity;
        }

        private static Entity CreateFeraldisFoundryECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1300f, los = 18f, radius = 1.5f;
            if (TechCatalog.TryGetBuilding("Feraldis_Foundry", out var def))
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
            ecb.AddComponent(entity, new ForgeStorage { Iron = 0, Veilstone = 0, MaxIron = 100, MaxVeilstone = 50, ConversionTimer = 0f });
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
        private static Entity CreateShrineOfRidanECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f;
            float los = 16f;
            float radius = 1.8f;

            if (TechCatalog.TryGetBuilding("ShrineOfRidan", out var def))
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

            // Shrine tech ladder (masses / warrior priests) researches here.
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Simple upgrade ladder (heal aura + Litharchs + sect power CDR).
            ecb.AddComponent<BuildingUpgradeable>(entity);

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        private static Entity CreateTempleOfRidanNewECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1500f;
            float los = 18f;
            float radius = 2.5f;

            if (TechCatalog.TryGetBuilding("TempleOfRidan", out var def))
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

            // Glow storage lives on the Temple per spec refinement #2.
            ecb.AddComponent(entity, new GlowStored { Amount = 0 });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Initialize 6 empty chapel slots — matches SectConfig.MaxAdoptedSects.
            var slotBuffer = ecb.AddBuffer<TempleChapelSlot>(entity);
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
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

            if (TechCatalog.TryGetBuilding("VaultOfAlmierra", out var def))
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
                InterestRate = 0.25f,
                LockTimer = 0f,
                LockDuration = 180f
            });

            // Banking tech ladder (interest grades + resource unlocks) researches here.
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Simple upgrade ladder (interest yields + wall productivity).
            ecb.AddComponent<BuildingUpgradeable>(entity);

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

            if (TechCatalog.TryGetBuilding("FiendstoneKeep", out var def))
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
            // Auto-fire per Age 0 design (Q#3): 4 targets, 20 dmg / 2.0 s, range 30.
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 30f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 4
            });

            // Keep tech ladder (emplacements / towers / walls) researches here.
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Wing slots (choice-building leveling): the Keep levels by
            // building up to three wings — see KeepWingSystem.
            ecb.AddComponent(entity, new KeepWings());

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

            return entity;
        }

        // ==================== Runai Culture Buildings (ECB) ====================

        private static Entity CreateRunaiOutpostECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 20f, radius = 1.0f;
            if (TechCatalog.TryGetBuilding("Runai_Outpost", out var def))
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
            if (TechCatalog.TryGetBuilding("Runai_TradeHub", out var def))
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
            if (TechCatalog.TryGetBuilding("Runai_TradingPost", out var def))
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
            if (TechCatalog.TryGetBuilding("ThessarasBazaar", out var def))
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
            if (TechCatalog.TryGetBuilding("Runai_SiegeWorkshop", out var def))
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
            if (TechCatalog.TryGetBuilding("Alanthor_Tower", out var def))
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
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Tower").
            ecb.AddComponent<BuildingUpgradeable>(entity);
            return entity;
        }

        private static Entity CreateAlanthorSiegeYardECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1100f, los = 14f, radius = 1.2f;
            if (TechCatalog.TryGetBuilding("Alanthor_SiegeYard", out var def))
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
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_SiegeYard").
            ecb.AddComponent<BuildingUpgradeable>(entity);
            return entity;
        }

        // ==================== Feraldis Culture Buildings (ECB) ====================

        private static Entity CreateFeraldisHuntingLodgeECB(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1000f, los = 14f, radius = 1.2f;
            if (TechCatalog.TryGetBuilding("Feraldis_HuntingLodge", out var def))
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
            if (TechCatalog.TryGetBuilding("Feraldis_LoggingStation", out var def))
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
            // Doc §5.7 #11: cultured Barracks — base 800 × 1.10 = 880 at L1.
            float hp = 880f, los = 14f, radius = 1.8f;
            if (TechCatalog.TryGetBuilding("Feraldis_Longhouse", out var def))
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
            if (TechCatalog.TryGetBuilding("Feraldis_Tower", out var def))
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
            if (TechCatalog.TryGetBuilding("Feraldis_SiegeYard", out var def))
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