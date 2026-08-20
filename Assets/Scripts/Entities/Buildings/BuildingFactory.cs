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
    /// Unified DISPATCH for creating buildings by id — one entry point, plus
    /// the cross-building queries (population, train-capability, choice-building
    /// gating) that need to answer for every id at once.
    ///
    /// The per-building creation code does NOT live here. Each building owns its
    /// own static class next to its data under
    /// Assets/GameData/TechTree/Buildings/&lt;Culture&gt;/&lt;Entity&gt;/, and the recipe
    /// table below just points at it. Adding a building = write its class in its
    /// folder, add one row here.
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
                ["ShrineOfRidan"]   = new BuildingRecipe(ShrineOfRidan.Create, ShrineOfRidan.Create, 520),
                // Legacy id alias — pre-rename build orders / saves still say
                // "ShrineOfAhridan"; keep them routing to the same creator.
                ["ShrineOfAhridan"] = new BuildingRecipe(ShrineOfRidan.Create, ShrineOfRidan.Create, 520),
                ["TempleOfRidan"]   = new BuildingRecipe(TempleOfRidan.Create, TempleOfRidan.Create, 521),
                ["VaultOfAlmierra"] = new BuildingRecipe(VaultOfAlmierra.Create, VaultOfAlmierra.Create, 530),
                ["FiendstoneKeep"]  = new BuildingRecipe(FiendstoneKeep.Create, FiendstoneKeep.Create, 540),
                ["Alanthor_Wall"]   = new BuildingRecipe(AlanthorWall.CreateHub, null, AlanthorWall.HubPresentationID),
                ["Alanthor_Smelter"] = new BuildingRecipe(Smelter.Create, Smelter.Create, Smelter.PresentationID),

                // Runai culture buildings
                ["Runai_Outpost"]      = new BuildingRecipe(RunaiOutpost.Create, RunaiOutpost.Create, 350),
                ["Runai_TradeHub"]     = new BuildingRecipe(TradeHub.Create, TradeHub.Create, 351),
                // (task-062 Q-39 — was missing, falling through to 100/default.)
                ["Runai_TradingPost"]  = new BuildingRecipe(TradingPost.Create, TradingPost.Create, 355),
                ["ThessarasBazaar"]    = new BuildingRecipe(ThessarasBazaar.Create, ThessarasBazaar.Create, 352),
                ["Runai_SiegeWorkshop"] = new BuildingRecipe(RunaiSiegeWorkshop.Create, RunaiSiegeWorkshop.Create, 353),
                ["Runai_Vault"]        = new BuildingRecipe(RunaiVault.Create, RunaiVault.Create, 365),
                ["Runai_VeilsteelFoundry"] = new BuildingRecipe(VeilsteelFoundry.Create, VeilsteelFoundry.Create, 366),

                // Alanthor culture buildings. The Practice Range is the LEVELED
                // Archery Range (not a placeable building) and the Crucible was
                // deleted (the Smelter absorbs its veilsteel role) — calculator
                // consolidation 2026-08.
                ["Alanthor_Tower"]         = new BuildingRecipe(WatchTower.Create, WatchTower.Create, 354),
                ["Alanthor_SiegeYard"]     = new BuildingRecipe(AlanthorSiegeYard.Create, AlanthorSiegeYard.Create, 357),
                ["KingsCourt"]             = new BuildingRecipe(KingsCourt.Create, KingsCourt.Create, 363),
                ["Alanthor_RoyalStable"]   = new BuildingRecipe(RoyalStable.Create, RoyalStable.Create, RoyalStable.PresentationID),

                // Feraldis culture buildings
                ["Feraldis_HuntingLodge"]   = new BuildingRecipe(HuntingLodge.Create, HuntingLodge.Create, 358),
                ["Feraldis_LoggingStation"] = new BuildingRecipe(LoggingStation.Create, LoggingStation.Create, 359),
                ["Feraldis_Longhouse"]      = new BuildingRecipe(Longhouse.Create, Longhouse.Create, 360),
                ["Feraldis_Tower"]          = new BuildingRecipe(TotemTower.Create, TotemTower.Create, 361),
                ["Feraldis_SiegeYard"]      = new BuildingRecipe(FeraldisSiegeYard.Create, FeraldisSiegeYard.Create, 362),
                ["Feraldis_Foundry"]        = new BuildingRecipe(FeraldisFoundry.Create, FeraldisFoundry.Create, 367),
                ["Feraldis_WarTotem"]       = new BuildingRecipe(WarTotem.Create, WarTotem.Create, WarTotem.PresentationID),
                ["Feraldis_Pasture"]        = new BuildingRecipe(Pasture.Create, Pasture.Create, Pasture.PresentationID),
                ["Mine"]                    = new BuildingRecipe(Mine.Create, Mine.Create, Mine.PresentationID),

                // Sect buildings — one per sect, capped at 5 per faction
                // (SectBuilding.CapPerFaction). Each trains its sect's unit and
                // sells its sect's research. Alanthor cluster shipped 2026-08-12;
                // the Runai and Feraldis eight land with their own pass.
                ["Sect_Reliquary"]          = new BuildingRecipe(Reliquary.Create, Reliquary.Create, Reliquary.PresentationID),
                ["Sect_MendingHall"]        = new BuildingRecipe(MendingHall.Create, MendingHall.Create, MendingHall.PresentationID),
                ["Sect_Stonehold"]          = new BuildingRecipe(Stonehold.Create, Stonehold.Create, Stonehold.PresentationID),
                ["Sect_Veilworks"]          = new BuildingRecipe(Veilworks.Create, Veilworks.Create, Veilworks.PresentationID),
                ["Sect_MusterYard"]         = new BuildingRecipe(MusterYard.Create, MusterYard.Create, MusterYard.PresentationID),
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
            // Build-grid snap (docs/Design/Build_Grid.md). This is the single
            // choke point every spawn path routes through — player placement,
            // AI, scenario seeding, bootstraps and lockstep replay — so
            // snapping HERE leaves no path that can author an off-grid
            // building. Wall SEGMENTS are the one deliberate exception and
            // never come through this dispatch; they are built directly by
            // AlanthorWall.CreateSegment/CreateInstance.
            position = BuildGrid.Snap(position, buildingId);

            Entity entity;
            if (Recipes.TryGetValue(buildingId, out var recipe))
                entity = recipe.CreateEm(em, position, faction);
            // task-063 phase 2a: 12 chapel building IDs (Chapel_Sect_Antiquity
            // .. Chapel_Sect_Wrath) all dispatch to a single uniform creator
            // that stamps ChapelTag.SectId from the chapel-id suffix. Visual
            // differentiation per sect lands in a Phase 5 polish pass.
            else if (SectConfig.IsChapelId(buildingId))
                entity = Chapel.Create(em, position, faction, SectConfig.SectIdFromChapelId(buildingId));
            else
                entity = CreateDefault(em, buildingId, position, faction);

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick
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
            // Same build-grid snap as the EntityManager overload above.
            position = BuildGrid.Snap(position, buildingId);

            Entity entity;
            if (Recipes.TryGetValue(buildingId, out var recipe) && recipe.CreateEcb != null)
                entity = recipe.CreateEcb(ecb, position, faction);
            // task-063 phase 2a: chapels dispatch to a single uniform ECB creator
            // (parameterised by sect id parsed from the chapel building id).
            else if (SectConfig.IsChapelId(buildingId))
                entity = Chapel.Create(ecb, position, faction, SectConfig.SectIdFromChapelId(buildingId));
            else
                entity = CreateDefault(ecb, buildingId, position, faction);

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick
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
                return Chapel.PidForSect(SectConfig.SectIdFromChapelId(buildingId));
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

        // SECT BUILDINGS (task-063) live in their own branch beside the
        // cultures: Assets/GameData/TechTree/Buildings/Sects/<Building>/.
        // Shipped so far: Chapel (the 12 adoption markers) and Reliquary
        // (Antiquity). The old roster's 24 creators are long deleted; the
        // remaining eleven sect-unique buildings (Workshop Eternal /
        // Oath-Stone / Crucible / Sepulchre / Tribunal / Sanctified Pyre /
        // Spire of Witness / War Forge / Furnace / Desecrator / Hollow Altar)
        // each get a folder there when they land — NOT a creator here.
        // SectUniqueBuildingTag is preserved for them.
    }
    // TempleTag and VaultTag are defined in BuildingComponents.cs (global namespace)
}