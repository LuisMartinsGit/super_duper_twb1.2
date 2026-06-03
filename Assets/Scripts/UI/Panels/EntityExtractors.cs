// EntityExtractors.cs
// Helper classes to extract UI display info from ECS entities
// Location: Assets/Scripts/UI/Common/EntityExtractors.cs

using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI
{
    /// <summary>
    /// Extracts display information from entities for EntityInfoPanel.
    /// </summary>
    public static class EntityInfoExtractor
    {
        public static EntityDisplayInfo GetDisplayInfo(Entity entity, EntityManager em)
        {
            var info = new EntityDisplayInfo
            {
                Name = "Unknown",
                Type = "Entity",
                Description = "",
                Portrait = null,
                // Null-by-default for stat fields per task-108 AD-3:
                // null = "no component" (renders as "—"), 0 = "component present
                // but value is zero" (renders as "0"). Health stays at 0 as the
                // existing contract treats it as required for any entity with
                // Health, and the panel guards via MaxHealth > 0.
                CurrentHealth = 0,
                MaxHealth = 0,
                Faction = "Neutral",
                HasCombatStats = false,
                Attack = null,
                Defense = null,
                Speed = null,
                HasResourceGeneration = false,
                SuppliesPerMinute = 0,
                IronPerMinute = 0,
                CrystalPerMinute = 0,
                VeilsteelPerMinute = 0,
                GlowPerMinute = 0,
                EntityKind = "unit",
                YieldPerMinute = null,
                QueueCapacity = null,
                Queue = null
            };

            if (!em.Exists(entity)) return info;

            bool isBuilding = em.HasComponent<BuildingTag>(entity);

            // Faction
            if (em.HasComponent<FactionTag>(entity))
                info.Faction = em.GetComponentData<FactionTag>(entity).Value.ToString();

            // Health
            if (em.HasComponent<Health>(entity))
            {
                var health = em.GetComponentData<Health>(entity);
                info.CurrentHealth = (int)health.Value;
                info.MaxHealth = (int)health.Max;
            }

            // task-109 Phase 5: aggregated Health bar for wall segments and
            // gate regions. Segments carry a placeholder Health{1,1} (they
            // are data-only graph edges); the meaningful HP is the sum
            // across the segment's WallInstanceRef buffer. We override the
            // values here so the Selection panel shows ONE aggregate bar
            // labelled "Wall Segment" (or "Wall Gate" when every member of
            // the buffer carries WallGateRegionTag — Phase 6 will refine
            // this with a dedicated label).
            //
            // Per-instance world-space floating bars (FloatingHealthBars)
            // continue to render per-entity unchanged — this only affects
            // the Selection-panel bar.
            //
            // Two surfaces hit the aggregate:
            //   (a) segment selected directly (player double-clicked a
            //       segment via the UI flow);
            //   (b) instance selected and Phase 6 resolves it to its
            //       parent segment for the action panel — but the
            //       Selection panel still shows segment-aggregate when
            //       the parent segment is the focus. We compute the
            //       aggregate here for both cases by detecting segment
            //       selection only; instance selection still shows the
            //       per-instance bar so individual instance health is
            //       still visible on click.
            if (em.HasComponent<WallSegmentTag>(entity) && em.HasBuffer<WallInstanceRef>(entity))
            {
                var refs = em.GetBuffer<WallInstanceRef>(entity);
                int sumHp = 0;
                int sumMax = 0;
                int alive = 0;
                int total = refs.Length;
                for (int i = 0; i < refs.Length; i++)
                {
                    var inst = refs[i].Instance;
                    if (!em.Exists(inst)) continue;
                    if (em.HasComponent<Health>(inst))
                    {
                        var h = em.GetComponentData<Health>(inst);
                        sumHp += (int)h.Value;
                        sumMax += (int)h.Max;
                        if (h.Value > 0) alive++;
                    }
                }
                // Only override when the aggregate is meaningful (avoid
                // emitting "0 / 0" if the buffer is empty, which would
                // make Selection.jsx render the bar as fully depleted).
                if (sumMax > 0)
                {
                    info.CurrentHealth = sumHp;
                    info.MaxHealth = sumMax;
                    info.Description = (info.Description != null && info.Description.Length > 0)
                        ? info.Description + $"\n{alive} / {total} intact"
                        : $"{alive} / {total} intact";
                }
            }

            // Combat stats (task-108 R5) — buildings read BuildingRangedAttack,
            // non-buildings read the unit-style Damage component. Defense and
            // Speed emit null when the component is absent so JSX can
            // discriminate "—" (missing) from "0" (zero-valued).
            if (isBuilding && em.HasComponent<BuildingRangedAttack>(entity))
            {
                info.HasCombatStats = true;
                info.Attack = em.GetComponentData<BuildingRangedAttack>(entity).Damage;
            }
            else if (!isBuilding && em.HasComponent<Damage>(entity))
            {
                info.HasCombatStats = true;
                info.Attack = (int)em.GetComponentData<Damage>(entity).Value;
            }
            // else: leave info.Attack null.

            if (em.HasComponent<Defense>(entity))
            {
                info.HasCombatStats = true;
                var def = em.GetComponentData<Defense>(entity);
                info.Defense = (int)def.Melee; // or average of all defense types
            }
            // else: leave info.Defense null.

            // Speed: hidden for buildings entirely (task-108 R5).
            if (!isBuilding && em.HasComponent<MoveSpeed>(entity))
            {
                info.Speed = em.GetComponentData<MoveSpeed>(entity).Value;
            }
            // else: leave info.Speed null.

            // Resource generation
            if (em.HasComponent<SuppliesIncome>(entity))
            {
                info.HasResourceGeneration = true;
                var si = em.GetComponentData<SuppliesIncome>(entity);
                info.SuppliesPerMinute = si.PerMinute;
                // task-108 R2: surface per-minute supplies as a dedicated yield
                // row for buildings (Hall trickle, GathererHut overlap yield).
                if (isBuilding) info.YieldPerMinute = si.PerMinute;
            }
            if (em.HasComponent<IronIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.IronPerMinute = em.GetComponentData<IronIncome>(entity).PerMinute;
            }
            if (em.HasComponent<CrystalIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.CrystalPerMinute = em.GetComponentData<CrystalIncome>(entity).PerMinute;
            }
            if (em.HasComponent<VeilsteelIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.VeilsteelPerMinute = em.GetComponentData<VeilsteelIncome>(entity).PerMinute;
            }
            if (em.HasComponent<GlowIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.GlowPerMinute = em.GetComponentData<GlowIncome>(entity).PerMinute;
            }

            // Type and name
            if (em.HasComponent<CrystalMainNodeTag>(entity))
            {
                info.Type = "Crystal Hive";
                info.Name = "Crystal Main Node";
                if (em.HasComponent<CrystalNodeLevel>(entity))
                {
                    int level = em.GetComponentData<CrystalNodeLevel>(entity).Value;
                    string threat = level switch { 1 => "Low Threat", 2 => "Moderate Threat", _ => "High Threat" };
                    info.Description = $"Level {level} — {threat}";
                }
                if (em.HasComponent<CrystalNode>(entity) && em.HasComponent<CrystalSpreadState>(entity))
                {
                    var cn = em.GetComponentData<CrystalNode>(entity);
                    var ss = em.GetComponentData<CrystalSpreadState>(entity);
                    int pct = cn.SpreadRadius > 0 ? (int)(ss.CurrentRingRadius / cn.SpreadRadius * 100f) : 0;
                    info.Description += $"\nSpread: {pct}%";
                }
            }
            else if (em.HasComponent<BuildingTag>(entity))
            {
                info.Type = "Building";
                info.Name = GetBuildingName(entity, em);
            }
            else if (em.HasComponent<UnitTag>(entity))
            {
                info.Type = "Unit";
                info.Name = GetUnitName(entity, em);
            }
            else if (em.HasComponent<IronMineTag>(entity))
            {
                info.Type = "Resource";
                info.Name = "Iron Deposit";
                info.HasResourceInfo = true;
                if (em.HasComponent<IronDepositState>(entity))
                {
                    var depState = em.GetComponentData<IronDepositState>(entity);
                    info.ResourceRemaining = depState.RemainingIron;
                    // task-108 R4: source max from the bootstrap-time InitialIron
                    // (added in this task). Pre-task-108 saves load with
                    // InitialIron == 0; fall back to RemainingIron so the bar
                    // reads "N / N" (100% full) until the deposit is mined.
                    info.ResourceMax = depState.InitialIron > 0
                        ? depState.InitialIron
                        : depState.RemainingIron;
                    info.ResourceTypeName = "Iron";
                    info.Description = depState.Depleted == 1 ? "Depleted" : "Active iron deposit";
                }
            }
            else if (em.HasComponent<CadaverTag>(entity))
            {
                info.Type = "Resource";
                info.Name = "Crystal Node";
                info.HasResourceInfo = true;
                if (em.HasComponent<CadaverState>(entity))
                {
                    var cadState = em.GetComponentData<CadaverState>(entity);
                    info.ResourceRemaining = cadState.RemainingCrystal;
                    info.ResourceMax = cadState.MaxCrystal > 0 ? cadState.MaxCrystal : cadState.RemainingCrystal;
                    info.ResourceTypeName = "Crystal";
                    info.Description = cadState.Depleted == 1 ? "Depleted" : "Harvestable crystal";
                }
            }

            // Shrine RP info
            if (em.HasComponent<ShrineTag>(entity))
            {
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + "Shrine of Ahridan — trains Litharchs, +1 RP";
                if (em.HasComponent<FactionTag>(entity))
                {
                    var faction = em.GetComponentData<FactionTag>(entity).Value;
                    int rp = GetFactionReligionPoints(em, faction);
                    if (rp > 0)
                        info.Description += $"\nReligion Points: {rp}";
                }
            }

            // Temple level and era info
            if (em.HasComponent<TempleOfRidanTag>(entity) && em.HasComponent<TempleLevel>(entity))
            {
                var templeLevel = em.GetComponentData<TempleLevel>(entity);
                int era = TempleLevelConfig.GetEraForLevel(templeLevel.Level);
                string levelStr = templeLevel.Level >= TempleLevelConfig.MaxLevel
                    ? $"Level {templeLevel.Level} (Max)"
                    : $"Level {templeLevel.Level}";
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Temple {levelStr} | Era {era}";

                // Show faction RP
                if (em.HasComponent<FactionTag>(entity))
                {
                    var faction = em.GetComponentData<FactionTag>(entity).Value;
                    int rp = GetFactionReligionPoints(em, faction);
                    if (rp > 0)
                        info.Description += $"\nReligion Points: {rp}";
                }
            }

            // Forge storage info
            if (em.HasComponent<ForgeStorage>(entity))
            {
                var forge = em.GetComponentData<ForgeStorage>(entity);
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Iron: {forge.Iron}/{forge.MaxIron}  Crystal: {forge.Crystal}/{forge.MaxCrystal}";
            }

            // Self-destruct timer
            if (em.HasComponent<SelfDestructTimer>(entity))
            {
                var timer = em.GetComponentData<SelfDestructTimer>(entity);
                int minutes = (int)(timer.TimeRemaining / 60f);
                int seconds = (int)(timer.TimeRemaining % 60f);
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Self-destructing in {minutes}m {seconds:D2}s";
            }

            // Miner info
            if (em.HasComponent<MinerTag>(entity) && em.HasComponent<MinerState>(entity))
            {
                var miner = em.GetComponentData<MinerState>(entity);
                info.HasMinerInfo = true;
                info.MinerCurrentLoad = miner.CurrentLoad;

                if (miner.GatheringResource == 1)
                {
                    info.MinerResourceType = "Crystal";
                    info.MinerMaxCarry = 10;
                    info.MinerExtractionRate = "1 crystal / 1.5s";
                }
                else
                {
                    info.MinerResourceType = "Iron";
                    info.MinerMaxCarry = 10;
                    info.MinerExtractionRate = "10 iron / 2s";
                }

                info.MinerState = miner.State switch
                {
                    MinerWorkState.Idle => "Idle",
                    MinerWorkState.MovingToDeposit => "Moving to resource",
                    MinerWorkState.Gathering => "Gathering",
                    MinerWorkState.ReturningToBase => "Delivering",
                    _ => "Unknown"
                };
            }

            // task-108 phase 1: EntityKind discriminator. Drives JSX conditional
            // rendering (collapse speed cell for buildings, amber bar for resources).
            if (isBuilding)
            {
                info.EntityKind = "building";
            }
            else if (em.HasComponent<IronMineTag>(entity) || em.HasComponent<CadaverTag>(entity))
            {
                info.EntityKind = "resource";
            }
            else
            {
                info.EntityKind = "unit";
            }

            // task-108 phase 1: training queue snapshot — 5-slot strip for any
            // building with a TrainingState + TrainQueueItem buffer. Slot 0
            // carries live progress when TrainingState.Busy == 1.
            if (isBuilding
                && em.HasComponent<TrainingState>(entity)
                && em.HasBuffer<TrainQueueItem>(entity))
            {
                info.QueueCapacity = TheWaningBorder.Core.Commands.CommandRouter.MaxProductionQueue;
                info.Queue = BuildQueueSnapshot(entity, em, info.QueueCapacity.Value);
            }

            return info;
        }

        /// <summary>
        /// Build a fixed-length snapshot of a building's training queue for the
        /// Web HUD selection topic. Always returns an array of length
        /// <paramref name="capacity"/> (matches CommandRouter.MaxProductionQueue);
        /// slots beyond the live buffer are marked Populated=false. Slot 0
        /// carries TrainingState.Busy/Remaining-derived progress so the JSX
        /// strip can render the in-production fill in lockstep with the
        /// existing TrainingInfo.Progress field.
        /// </summary>
        private static EntityQueueSlot[] BuildQueueSnapshot(Entity e, EntityManager em, int capacity)
        {
            var arr = new EntityQueueSlot[capacity];
            var buf = em.GetBuffer<TrainQueueItem>(e);
            var ts = em.GetComponentData<TrainingState>(e);

            // Total training time from TechTreeDB for slot 0 progress. Mirrors
            // the existing TrainingInfo.Progress derivation in EntityActionExtractor.
            float slot0Total = 1f;
            if (buf.Length > 0)
            {
                string slot0Id = buf[0].UnitId.ToString();
                if (TechCatalog.TryGetUnit(slot0Id, out var udef))
                    slot0Total = udef.trainingTime > 0 ? udef.trainingTime : 1f;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (i >= buf.Length)
                {
                    arr[i].Populated = false;
                    continue;
                }
                string uid = buf[i].UnitId.ToString();
                var cost = EntityActionExtractor.GetUnitCost(uid);
                arr[i].Populated = true;
                arr[i].UnitId = uid;
                arr[i].DisplayName = ResolveUnitDisplayName(uid);
                arr[i].RefundSupplies = cost.Supplies;
                arr[i].RefundIron = cost.Iron;
                arr[i].RefundCrystal = cost.Crystal;
                arr[i].RefundVeilsteel = cost.Veilsteel;
                arr[i].RefundGlow = cost.Glow;
                arr[i].IsInProduction = (i == 0 && ts.Busy != 0);
                if (arr[i].IsInProduction && slot0Total > 0f)
                {
                    float remaining = ts.Remaining > 0 ? ts.Remaining : 0f;
                    float p = 1f - (remaining / slot0Total);
                    if (p < 0f) p = 0f;
                    else if (p > 1f) p = 1f;
                    arr[i].Progress = p;
                }
                else
                {
                    arr[i].Progress = 0f;
                }
            }
            return arr;
        }

        /// <summary>
        /// Resolve a unit-id string (e.g. "Swordsman") to a human-readable
        /// display name. Prefers TechTreeDB.unit.name; falls back to the
        /// id itself when not registered.
        /// </summary>
        private static string ResolveUnitDisplayName(string unitId)
        {
            if (TechCatalog.TryGetUnit(unitId, out var udef)
                && !string.IsNullOrEmpty(udef.name))
                return udef.name;
            return unitId;
        }

        private static string GetBuildingName(Entity entity, EntityManager em)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<ArcheryRangeTag>(entity)) return "Archery Range";
            if (em.HasComponent<GathererHutTag>(entity)) return "Gatherer's Hut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<DepotTag>(entity)) return "Depot";
            if (em.HasComponent<WorkshopTag>(entity)) return "Workshop";
            if (em.HasComponent<ShrineTag>(entity)) return "Shrine of Ahridan";
            if (em.HasComponent<TempleOfRidanTag>(entity)) return "Temple of Ridan";
            if (em.HasComponent<VaultTag>(entity)) return "Vault of Almiérra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "Fiendstone Keep";
            // Display label changed Smelter → Forge per the user's UI request.
            // The ECS tag, building id ("Alanthor_Smelter"), factory, and the
            // ForgeStorage/ForgeConversionSystem pipeline are all unchanged.
            if (em.HasComponent<SmelterTag>(entity)) return "Forge";
            if (em.HasComponent<WallHubTag>(entity)) return "Wall Hub";
            if (em.HasComponent<WallTowerTag>(entity)) return "Wall Tower";
            if (em.HasComponent<WallGateTag>(entity)) return "Wall Gate";
            if (em.HasComponent<WallInstanceTag>(entity)) return "Wall";
            if (em.HasComponent<WallSegmentTag>(entity)) return "Wall Segment";
            // Runai culture buildings
            if (em.HasComponent<OutpostTag>(entity)) return "Runai Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Trade Hub";
            if (em.HasComponent<TradingPostTag>(entity)) return "Trading Post";
            if (em.HasComponent<BazaarTag>(entity)) return "Thessara's Bazaar";
            if (em.HasComponent<BazaarWagonTag>(entity)) return "Bazaar Wagon";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Siege Workshop";
            // Alanthor culture buildings
            if (em.HasComponent<WatchTowerTag>(entity)) return "Watch Tower";
            if (em.HasComponent<PracticeRangeTag>(entity)) return "Practice Range";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Siege Yard";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Royal Stable";
            // Feraldis culture buildings
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Hunting Lodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Logging Station";
            if (em.HasComponent<LonghouseTag>(entity)) return "Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Totem Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Siege Yard";
            // Crystal faction buildings
            if (em.HasComponent<CrystalMainNodeTag>(entity)) return "Crystal Hive";
            if (em.HasComponent<CrystalSubNodeTag>(entity))
            {
                var subType = em.GetComponentData<CrystalSubNodeTag>(entity).Type;
                return subType switch
                {
                    CrystalSubNodeType.Resource => "Crystal Wellspring",
                    CrystalSubNodeType.Enforcement => "Enforcement Spire",
                    CrystalSubNodeType.Suppression => "Suppression Spire",
                    CrystalSubNodeType.Restoration => "Restoration Bloom",
                    CrystalSubNodeType.Turret => "Crystal Turret",
                    _ => "Crystal Node"
                };
            }
            return "Building";
        }

        private static string GetUnitName(Entity entity, EntityManager em)
        {
            // Use PresentationId for precise unit identification
            if (em.HasComponent<PresentationId>(entity))
            {
                int pid = em.GetComponentData<PresentationId>(entity).Id;
                string name = GetUnitNameByPresentationId(pid);
                if (name != null) return name;
            }

            // Legacy fallback for units without PresentationId. Workers
            // (formerly Builder + Miner) now share a single display name
            // — the per-class branches just disambiguate combat units.
            if (em.HasComponent<CanBuild>(entity)) return "Worker";
            if (em.HasComponent<MinerTag>(entity)) return "Worker";

            if (em.HasComponent<UnitTag>(entity))
            {
                var unitTag = em.GetComponentData<UnitTag>(entity);
                return unitTag.Class switch
                {
                    UnitClass.Melee => "Swordsman",
                    UnitClass.Ranged => "Archer",
                    UnitClass.Scout => "Scout",
                    UnitClass.Support => "Litharch",
                    UnitClass.Siege => "Siege Unit",
                    UnitClass.Economy => "Worker",
                    UnitClass.Miner => "Worker",
                    _ => "Unit"
                };
            }

            return "Unit";
        }

        /// <summary>
        /// Map PresentationId to display name for all unit types.
        /// Returns null if the ID is not recognized.
        /// </summary>
        private static string GetUnitNameByPresentationId(int pid)
        {
            return pid switch
            {
                // Era 1 core units. PID 200 (former Builder) + 203
                // (former Miner) both render as "Worker" now that the
                // two specialists are unified — existing entities loaded
                // from older saves still display the new name.
                200 => "Worker",
                201 => "Swordsman",
                202 => "Archer",
                203 => "Worker",
                // task-110: Era 1 Archery Range tier units
                204 => "Crossbowman",
                205 => "Longbowman",
                206 => "Scout",
                207 => "Litharch",
                210 => "Berserker",
                // Crystal units
                320 => "Crystalling",
                321 => "Veilstinger",
                322 => "Godsplinter",
                // Runai culture units
                330 => "Spearman",
                331 => "Skirmisher",
                332 => "Raider",
                333 => "Catapult",
                // Alanthor culture units
                334 => "Sentinel",
                335 => "Crossbowman",
                336 => "Cataphract",
                337 => "Ballista",
                // Feraldis culture units
                338 => "Hunter",
                339 => "Warboar Rider",
                340 => "Siege Ram",
                // Sect unique units
                370 => "Scar Guard",
                371 => "Golem Autark",
                372 => "Stone Warden",
                373 => "Archivist Adept",
                374 => "Flame Warden",
                375 => "Vault Keeper",
                376 => "Glassmark Arcanist",
                377 => "Judicator",
                378 => "Ashblade",
                379 => "Brandbreaker",
                380 => "Chaincaster",
                381 => "Nullblade",
                _ => null
            };
        }

        /// <summary>
        /// Get the current era for a faction from its bank entity.
        /// Returns 1 if not found.
        /// </summary>
        public static int GetFactionEra(EntityManager em, Faction faction)
        {
            if (Unity.Entities.World.DefaultGameObjectInjectionWorld == null || !Unity.Entities.World.DefaultGameObjectInjectionWorld.IsCreated)
                return 1;
            if (em.Equals(default(EntityManager)))
                return 1;

            // Fix #206: cache query across OnGUI frames.
            if (!_eraQueryOwner.Equals(em))
            {
                _eraQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionEra>()
                );
                _eraQueryOwner = em;
            }

            using var entities = _eraQuery.ToEntityArray(Allocator.Temp);
            using var tags = _eraQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var eras = _eraQuery.ToComponentDataArray<FactionEra>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                    return eras[i].Value;
            }

            return 1;
        }

        /// <summary>
        /// Get the current religion points for a faction.
        /// Returns 0 if not found.
        /// </summary>
        public static int GetFactionReligionPoints(EntityManager em, Faction faction)
        {
            if (Unity.Entities.World.DefaultGameObjectInjectionWorld == null || !Unity.Entities.World.DefaultGameObjectInjectionWorld.IsCreated)
                return 0;
            if (em.Equals(default(EntityManager)))
                return 0;

            // Fix #206: cache query across OnGUI frames.
            // task-063: source of truth is FactionReligionPoints.Balance.
            if (!_rpQueryOwner.Equals(em))
            {
                _rpQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionReligionPoints>()
                );
                _rpQueryOwner = em;
            }

            using var entities = _rpQuery.ToEntityArray(Allocator.Temp);
            using var tags = _rpQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var rps = _rpQuery.ToComponentDataArray<FactionReligionPoints>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                    return rps[i].Balance;
            }

            return 0;
        }

        // Fix #206: per-query caches, invalidated on world change.
        private static EntityQuery _eraQuery;
        private static EntityManager _eraQueryOwner;
        private static EntityQuery _rpQuery;
        private static EntityManager _rpQueryOwner;
    }

    /// <summary>
    /// Extracts action information from entities for EntityActionPanel.
    /// </summary>
    public static class EntityActionExtractor
    {
        // Icon cache: loaded once from Resources/UI/Icons/Buildings/
        private static readonly Dictionary<string, UnityEngine.Texture2D> _buildingIconCache = new();

        /// <summary>
        /// Load a building icon from Resources/UI/Icons/Buildings/.
        /// Maps building IDs to icon filenames where they differ.
        /// Returns null if no icon exists for that building.
        /// </summary>
        private static UnityEngine.Texture2D GetBuildingIcon(string buildingId)
        {
            if (_buildingIconCache.TryGetValue(buildingId, out var cached))
                return cached;

            // Map building IDs to icon filenames where they differ
            string iconName = buildingId switch
            {
                "TempleOfRidan" => "ShrineOfAhridan",
                _ => buildingId
            };

            var tex = UnityEngine.Resources.Load<UnityEngine.Texture2D>($"UI/Icons/Buildings/{iconName}");
            _buildingIconCache[buildingId] = tex; // Cache even null to avoid repeated lookups
            return tex;
        }

        public static EntityActionInfo GetActionInfo(Entity entity, EntityManager em)
        {
            var info = new EntityActionInfo
            {
                Type = ActionType.None,
                Actions = new List<ActionButton>()
            };

            if (!em.Exists(entity)) return info;

            // Per-hub "Build Wall" action — surfaces on any completed wall
            // hub of the local faction. Clicking enters a hub-anchored
            // placement mode (BuilderCommandPanel.TriggerHubBuildWall) that
            // drops a new hub + auto-connecting segment with no builder
            // and a 30 s self-build timer. Cost is paid up-front when the
            // second hub is placed (not when the action button is shown),
            // so the button stays enabled regardless of current resources;
            // the placement step itself surfaces the "not enough resources"
            // notification if the player can't actually afford it.
            if (em.HasComponent<WallHubTag>(entity)
                && !em.HasComponent<UnderConstruction>(entity)
                && em.HasComponent<FactionTag>(entity)
                && em.GetComponentData<FactionTag>(entity).Value == GameSettings.LocalPlayerFaction)
            {
                Cost hubCost = default;
                BuildCosts.TryGet("Alanthor_Wall", out hubCost);
                bool canAfford = FactionEconomy.CanAfford(em,
                    GameSettings.LocalPlayerFaction, hubCost);

                info.Type = ActionType.HubBuildWall;
                info.Actions = new List<ActionButton>
                {
                    new ActionButton
                    {
                        Id = "BuildWall",
                        Label = "Build Wall",
                        Tooltip = "Place a connected wall hub. Auto-builds in 30s with no builder.",
                        Enabled = true,
                        Cost = hubCost,
                        CanAfford = canAfford,
                    }
                };
                return info;
            }

            // Check if this is an upgradeable wall instance (not already tower or gate).
            // task-109 phase 6: per-segment conversion actions live here.
            // Selection-panel data stays per-instance (clicking a wall shows the
            // single wall's HP) but the ACTIONS panel resolves to the parent
            // segment and surfaces:
            //   - "Convert to Gate (Nx)" — 5-instance segment-level conversion
            //     (Phase 5 WallSegmentUpgradeState path). N is the smaller of
            //     the segment's instance count or 5.
            //   - "Convert to Tower"     — single-instance legacy conversion
            //     (per-instance WallUpgradeState path; unchanged).
            // Instances that are already part of a gate region don't show
            // further upgrade actions (already converted).
            if (em.HasComponent<WallInstanceTag>(entity) &&
                !em.HasComponent<WallTowerTag>(entity) &&
                !em.HasComponent<WallGateTag>(entity) &&
                !em.HasComponent<WallGateRegionTag>(entity) &&
                !em.HasComponent<UnderConstruction>(entity))
            {
                info.Type = ActionType.WallInstanceUpgrade;
                info.Actions = BuildSegmentConversionActions(entity, em);
                return info;
            }

            // Alanthor age-up choice: Gatherer's Hut tagged with
            // GathererHutAgeUpChoice surfaces two large action cells (Wall
            // Hub / Watch Tower). Mid-conversion (GathererHutConverting) the
            // same type renders an empty action list — the JSX side reads
            // the progress data off the selection payload separately.
            // (task-109 phase 2)
            if (em.HasComponent<GathererHutAgeUpChoice>(entity)
                || em.HasComponent<GathererHutConverting>(entity))
            {
                info.Type = ActionType.GathererHutAgeUpChoice;
                info.Actions = GetHutAgeUpChoiceActions(entity, em);
                return info;
            }

            // Check if this is a Bazaar Wagon (packed Bazaar — show unpack button)
            if (em.HasComponent<BazaarWagonTag>(entity))
            {
                info.Type = ActionType.BazaarWagonUnpack;
                info.Actions = new List<ActionButton>
                {
                    new ActionButton
                    {
                        Id = "BazaarUnpack",
                        Label = "Unpack",
                        Tooltip = "Unpack wagon back into Thessara's Bazaar",
                        Enabled = true,
                        CanAfford = true
                    }
                };
                return info;
            }

            // Check if this is a builder (can place buildings)
            if (em.HasComponent<CanBuild>(entity))
            {
                info.Type = ActionType.BuildingPlacement;
                info.Actions = GetBuildingActions();
                return info;
            }

            // Check if this is a vault
            if (em.HasComponent<VaultTag>(entity) && em.HasComponent<VaultStorage>(entity))
            {
                info.Type = ActionType.VaultManagement;
                return info;
            }

            // Check if this is a shrine (simple training — litharchs only)
            if (em.HasComponent<ShrineTag>(entity) && em.HasComponent<TrainingState>(entity))
            {
                info.Type = ActionType.UnitTraining;
                info.Actions = GetTrainingActions(entity, em);
                info.TrainingState = GetTrainingInfo(entity, em);
                return info;
            }

            // Check if this is the Temple of Ridan (training + level-up + sect slots)
            if (em.HasComponent<TempleOfRidanTag>(entity) && em.HasComponent<TempleLevel>(entity)
                && em.HasComponent<TrainingState>(entity))
            {
                info.Type = ActionType.TempleUpgrade;
                info.Actions = GetTempleTrainingActions(entity, em);
                info.TrainingState = GetTrainingInfo(entity, em);
                if (em.HasComponent<ResearchState>(entity))
                    info.ResearchState = GetResearchInfo(entity, em);
                return info;
            }

            // Check if this is a training building (any building with a TrainingState)
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<TrainingState>(entity))
            {
                var trainingActions = GetTrainingActions(entity, em);
                bool hasResearch = em.HasComponent<ResearchState>(entity);

                // Bazaar: add Pack button to training actions
                if (em.HasComponent<BazaarTag>(entity) && !em.HasComponent<UnderConstruction>(entity))
                {
                    trainingActions.Add(new ActionButton
                    {
                        Id = "BazaarPack",
                        Label = "Pack",
                        Tooltip = "Pack Bazaar into a mobile wagon",
                        Enabled = true,
                        CanAfford = true
                    });
                }

                if (trainingActions.Count > 0)
                {
                    // Building can train and possibly research
                    info.Type = hasResearch ? ActionType.UnitTrainingAndResearch : ActionType.UnitTraining;
                    info.Actions = trainingActions;
                    info.TrainingState = GetTrainingInfo(entity, em);

                    if (hasResearch)
                        info.ResearchState = GetResearchInfo(entity, em);

                    return info;
                }
            }

            // Check if this is a research-only building
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<ResearchState>(entity))
            {
                info.Type = ActionType.UnitTrainingAndResearch;
                info.Actions = new List<ActionButton>();
                info.ResearchState = GetResearchInfo(entity, em);
                return info;
            }

            return info;
        }

        /// <summary>
        /// Build the two action cells surfaced on a Gatherer's Hut with the
        /// age-up choice marker. Both cells share the same canonical cost
        /// (40 supplies + 30 iron) and the same 5-second timer — only the
        /// outcome differs. While mid-conversion (GathererHutConverting
        /// present and the marker stripped) the helper returns an empty
        /// list, so the panel collapses to a progress display only.
        /// (task-109 phase 2)
        /// </summary>
        private static List<ActionButton> GetHutAgeUpChoiceActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            // Mid-conversion → no buttons (cannot cancel in v1, per Phase 1
            // canonical design).
            if (em.HasComponent<GathererHutConverting>(entity))
                return actions;

            if (!em.HasComponent<GathererHutAgeUpChoice>(entity))
                return actions;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            var cost = TheWaningBorder.Core.Commands.Types.ConvertHutCommandHelper.ConversionCost;
            bool canAfford = !em.Equals(default(EntityManager))
                ? FactionEconomy.CanAfford(em, faction, cost)
                : true;
            Cost available = GetFactionResourcesAsCost(em, faction);

            actions.Add(new ActionButton
            {
                Id = "ConvertToWallHub",
                Label = "Convert to Wall Hub",
                Tooltip = BuildTooltip(
                    "Convert to Wall Hub",
                    "Replaces the hut with a Wall Hub. Adjacent hubs auto-link into wall segments.",
                    cost,
                    available,
                    trainingTime: TheWaningBorder.Core.Commands.Types.ConvertHutCommandHelper.ConversionDuration
                ),
                Cost = cost,
                Enabled = true,
                CanAfford = canAfford,
                Icon = null,
            });

            actions.Add(new ActionButton
            {
                Id = "ConvertToWatchTower",
                Label = "Convert to Watch Tower",
                Tooltip = BuildTooltip(
                    "Convert to Watch Tower",
                    "Replaces the hut with a stand-alone Alanthor Watch Tower (ranged defense).",
                    cost,
                    available,
                    trainingTime: TheWaningBorder.Core.Commands.Types.ConvertHutCommandHelper.ConversionDuration
                ),
                Cost = cost,
                Enabled = true,
                CanAfford = canAfford,
                Icon = null,
            });

            return actions;
        }

        /// <summary>
        /// Build the action cells surfaced when the player selects a wall
        /// instance. Per task-109 Phase 6 the action panel resolves an
        /// instance click to its parent segment and presents:
        ///   - "Convert to Gate (Nx)" — segment-level 5-instance conversion
        ///     (task-109 Phase 5 path). N is min(instance count, 5); a short
        ///     segment is allowed but the label communicates the shortened
        ///     gate width and the helper surfaces a warning suffix.
        ///   - "Convert to Tower"     — per-instance legacy conversion
        ///     (single-instance WallUpgradeState path; cost from BuildCosts).
        /// Mid-conversion (parent segment carries WallSegmentUpgradeState)
        /// the Gate button drops out — only the Tower stays. (task-109 phase 6)
        /// </summary>
        private static List<ActionButton> BuildSegmentConversionActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();
            if (!em.HasComponent<WallInstanceTag>(entity)) return actions;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            Cost available = GetFactionResourcesAsCost(em, faction);

            // Resolve parent segment to derive the gate width label.
            Entity segment = Entity.Null;
            if (em.HasComponent<WallInstanceParent>(entity))
                segment = em.GetComponentData<WallInstanceParent>(entity).Segment;
            int segmentInstanceCount = 0;
            if (em.Exists(segment) && em.HasBuffer<WallInstanceRef>(segment))
                segmentInstanceCount = em.GetBuffer<WallInstanceRef>(segment).Length;
            int gateWidth = segmentInstanceCount > 0 ? System.Math.Min(segmentInstanceCount, 5) : 5;
            bool shortSegment = segmentInstanceCount > 0 && segmentInstanceCount < 5;
            bool segmentConverting = em.Exists(segment) && em.HasComponent<WallSegmentUpgradeState>(segment);

            // Gate cell — segment-level conversion. Drops out while the
            // segment is mid-conversion (no double-charge / double-stack).
            if (!segmentConverting)
            {
                var gateCost = TheWaningBorder.Core.Commands.Types
                    .ConvertSegmentToGateCommandHelper.ConversionCost;
                bool canAffordGate = !em.Equals(default(EntityManager))
                    ? FactionEconomy.CanAfford(em, faction, gateCost)
                    : true;
                string gateLabel = $"Convert to Gate ({gateWidth}x)";
                string gateSubtitle = shortSegment
                    ? $"Short segment — gate will span {gateWidth} instances. Groups wider than {gateWidth} may not fit."
                    : "5-instance opening. Units can path through.";

                actions.Add(new ActionButton
                {
                    Id = "WallSegmentToGate",
                    Label = gateLabel,
                    Tooltip = BuildTooltip(
                        gateLabel,
                        gateSubtitle,
                        gateCost,
                        available,
                        trainingTime: TheWaningBorder.Core.Commands.Types
                            .ConvertSegmentToGateCommandHelper.ConversionDuration
                    ),
                    Cost = gateCost,
                    Enabled = true,
                    CanAfford = canAffordGate,
                    Icon = null,
                });
            }

            // Tower cell — per-instance legacy conversion (unchanged from
            // the IMGUI reference at EntityActionPanel.cs:1641-1660).
            if (TheWaningBorder.Data.BuildCosts.TryGet("Alanthor_WallTower", out var towerCost))
            {
                bool canAffordTower = !em.Equals(default(EntityManager))
                    ? FactionEconomy.CanAfford(em, faction, towerCost)
                    : true;
                actions.Add(new ActionButton
                {
                    Id = "WallInstanceToTower",
                    Label = "Convert to Tower",
                    Tooltip = BuildTooltip(
                        "Convert to Tower",
                        "Reinforces this wall section into a watchtower (ranged defense).",
                        towerCost,
                        available,
                        trainingTime: 10f
                    ),
                    Cost = towerCost,
                    Enabled = true,
                    CanAfford = canAffordTower,
                    Icon = null,
                });
            }

            return actions;
        }

        /// <summary>
        /// Get the current faction resources as a Cost for rich tooltip formatting.
        /// </summary>
        private static Cost GetFactionResourcesAsCost(EntityManager em, Faction faction)
        {
            if (em.Equals(default(EntityManager))) return default;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return default;
            return new Cost
            {
                Supplies = res.Supplies,
                Iron = res.Iron,
                Crystal = res.Crystal,
                Veilsteel = res.Veilsteel,
                Glow = res.Glow
            };
        }

        /// <summary>
        /// Build a rich-text tooltip for an action button.
        /// Shows name, cost (color-coded), training time, and any requirement lines in red.
        /// </summary>
        private static string BuildTooltip(string name, string subtitle, Cost cost, Cost available, float trainingTime = 0f, string requirement = null)
        {
            var sb = new System.Text.StringBuilder(128);
            sb.Append($"<b>{name}</b>");
            if (!string.IsNullOrEmpty(subtitle))
                sb.Append($"  <color=#b0a890>({subtitle})</color>");

            // Cost line
            sb.Append("\nCost: ");
            sb.Append(UIHelpers.FormatCostRich(cost, available));

            // Training/build time
            if (trainingTime > 0f)
                sb.Append($"\nTime: {trainingTime:F0}s");

            // Requirement (shown in red)
            if (!string.IsNullOrEmpty(requirement))
                sb.Append($"\n<color=#ff5555>{requirement}</color>");

            return sb.ToString();
        }

        // Buildings the player can place via builder (excludes starting buildings and other-faction variants)
        //
        // task-109: Alanthor wall primitives — only "Alanthor_Wall" (hub) and "Alanthor_Tower"
        //           (standalone watch tower) are placeable. "Alanthor_WallTower" and
        //           "Alanthor_WallGate" are CONVERSION-ONLY (segment selection → Convert
        //           to Tower / Convert to Gate). They MUST NOT appear in this HashSet.
        //           See docs/Design/Age_1_Alanthor.md § Wall System (BFME2 hub-and-segment)
        //           and the static-ctor Debug.Assert guard below.
        private static readonly HashSet<string> BuildableBuildings = new()
        {
            "Hut", "GatherersHut", "Barracks", "ArcheryRange", "ShrineOfAhridan", "VaultOfAlmierra", "FiendstoneKeep",
            "TempleOfRidan",
            // Additional Halls — culture-gated (post-age-up only) and capped at
            // 6 per faction. The 6-cap and culture gate are enforced inside
            // GetBuildingActions; the runtime cap fallback lives in
            // BuilderCommandPanel.SpawnSelectedBuilding.
            "Hall",
            "Alanthor_Wall", "Alanthor_Smelter",
            // Runai culture buildings
            "Runai_Outpost", "Runai_TradeHub", "Runai_TradingPost", "ThessarasBazaar", "Runai_SiegeWorkshop",
            // Alanthor culture buildings
            "Alanthor_Tower", "Alanthor_PracticeRange", "Alanthor_SiegeYard", "Alanthor_RoyalStable", "Alanthor_Crucible",
            // Feraldis culture buildings
            "Feraldis_HuntingLodge", "Feraldis_LoggingStation", "Feraldis_Longhouse",
            "Feraldis_Tower", "Feraldis_SiegeYard"
        };

        // task-109: defensive boot-time guard. If a future PR accidentally adds
        // "Alanthor_WallTower" or "Alanthor_WallGate" to BuildableBuildings, this
        // static constructor will fire a Debug.Assert at first class touch (which
        // happens during the first build-action extraction on the local player
        // builder). Keeping the assertion close to the HashSet declaration makes
        // the contract self-documenting.
        static EntityActionExtractor()
        {
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_WallTower"),
                "task-109: Alanthor_WallTower must remain conversion-only (segment → Convert to Tower). Do not add it to BuildableBuildings.");
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_WallGate"),
                "task-109: Alanthor_WallGate must remain conversion-only (segment → Convert to Gate). Do not add it to BuildableBuildings.");
        }

        private static List<ActionButton> GetBuildingActions()
        {
            var actions = new List<ActionButton>();
            var faction = GameSettings.LocalPlayerFaction;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            EntityManager em = (world != null && world.IsCreated) ? world.EntityManager : default;

            // Check if faction already has a choice building (Shrine/Vault/Keep)
            string existingChoice = null;
            if (!em.Equals(default(EntityManager)))
                existingChoice = BuildingFactory.GetFactionChoiceBuilding(em, faction);

            // Determine local faction's culture from the Hall entity's FactionProgress
            byte factionCulture = Cultures.None;
            if (!em.Equals(default(EntityManager)))
            {
                var hallQuery = em.CreateEntityQuery(typeof(HallTag), typeof(FactionTag), typeof(FactionProgress));
                var hallEntities = hallQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < hallEntities.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(hallEntities[i]).Value == faction)
                    {
                        factionCulture = em.GetComponentData<FactionProgress>(hallEntities[i]).Culture;
                        break;
                    }
                }
                hallEntities.Dispose();
            }

            // Get faction era for era gating
            int factionEra = !em.Equals(default(EntityManager))
                ? EntityInfoExtractor.GetFactionEra(em, faction)
                : 1;

            // Get current resources for rich tooltip coloring
            Cost available = GetFactionResourcesAsCost(em, faction);

            // Per-faction caps — counted once so we don't re-query inside the
            // building loop. Halls cap at 6 (post-age-up expansion); Temple of
            // Ridan caps at 1.
            int hallCount = !em.Equals(default(EntityManager))
                ? BuildingFactory.GetFactionBuildingCount<HallTag>(em, faction) : 0;
            int templeCount = !em.Equals(default(EntityManager))
                ? BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(em, faction) : 0;
            const int HallCap = 6;
            const int TempleCap = 1;

            if (TechCatalog.IsReady)
            {
                foreach (var building in TechCatalog.GetAllBuildings())
                {
                    // Only show buildings the player can actually place
                    if (!BuildableBuildings.Contains(building.id)) continue;

                    // Choice building exclusion: if one is built, hide the other two
                    if (BuildingFactory.IsChoiceBuilding(building.id) && existingChoice != null)
                        continue;

                    // Hall: post-age-up expansion, capped at 6 per faction.
                    // Hide entirely pre-age-up (no Hall button until you've
                    // picked a culture) and once the cap is reached.
                    if (building.id == "Hall")
                    {
                        if (factionCulture == Cultures.None) continue;
                        if (hallCount >= HallCap) continue;
                    }

                    // Temple of Ridan: one per faction.
                    if (building.id == "TempleOfRidan" && templeCount >= TempleCap) continue;

                    // Data-driven culture gating: buildings with culture prefix require that culture
                    byte requiredCulture = GetRequiredCulture(building.id);
                    if (requiredCulture != Cultures.None && requiredCulture != factionCulture)
                        continue;

                    // Alanthor cannot build Gatherer's Huts (they use walls for income)
                    if (building.id == "GatherersHut" && factionCulture == Cultures.Alanthor)
                        continue;

                    // Runai cannot build Huts (population is set to 200 on age-up)
                    if (building.id == "Hut" && factionCulture == Cultures.Runai)
                        continue;

                    var cost = building.cost != null ? new Cost
                    {
                        Supplies = building.cost.Supplies,
                        Iron = building.cost.Iron,
                        Crystal = building.cost.Crystal
                    } : default;

                    bool canAfford = !em.Equals(default(EntityManager))
                        ? FactionEconomy.CanAfford(em, faction, cost)
                        : true;

                    // Era gating: show button disabled with requirement text instead of hiding
                    bool eraLocked = building.minEra > 0 && building.minEra > factionEra;
                    string requirement = eraLocked ? $"Requires: Era {building.minEra}" : null;

                    string tooltip = BuildTooltip(
                        building.name,
                        building.role,
                        cost,
                        available,
                        requirement: requirement
                    );

                    actions.Add(new ActionButton
                    {
                        Id = building.id,
                        Label = building.name,
                        Tooltip = tooltip,
                        Cost = cost,
                        Enabled = !eraLocked,
                        CanAfford = canAfford && !eraLocked,
                        Icon = GetBuildingIcon(building.id)
                    });
                }
            }

            return actions;
        }

        public static List<ActionButton> GetTrainingActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            // Get faction for affordability checks
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            // Chapel special case: training action derived from SectConfig, not TechTreeDB
            if (em.HasComponent<ChapelTag>(entity))
            {
                return GetChapelTrainingActions(entity, em, faction);
            }

            // Identify building type and look up its definition
            string buildingId = GetBuildingId(entity, em);
            if (buildingId == null || !TechCatalog.IsReady) return actions;

            if (!TechCatalog.TryGetBuilding(buildingId, out var buildingDef)) return actions;
            if (buildingDef.trains == null || buildingDef.trains.Length == 0) return actions;

            // Determine faction culture from the building's faction -> Hall -> FactionProgress
            byte factionCulture = Cultures.None;
            if (em.HasComponent<FactionTag>(entity))
            {
                var buildingFaction = em.GetComponentData<FactionTag>(entity).Value;
                var hallQuery = em.CreateEntityQuery(typeof(HallTag), typeof(FactionTag), typeof(FactionProgress));
                var hallEntities = hallQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < hallEntities.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(hallEntities[i]).Value == buildingFaction)
                    {
                        factionCulture = em.GetComponentData<FactionProgress>(hallEntities[i]).Culture;
                        break;
                    }
                }
                hallEntities.Dispose();
            }

            // Get current resources for rich tooltip coloring
            Cost available = GetFactionResourcesAsCost(em, faction);

            // Building level for advanced-unit gating. Default L1 for buildings
            // that haven't been stamped with BuildingUpgradeState yet.
            // Temples track their level via TempleLevel rather than
            // BuildingUpgradeState — read whichever is present so the
            // Scholar/Acolyte minBuildingLevel: 4 gate fires correctly
            // (spec refinement #5: ritualists train at a fully-leveled Temple).
            int buildingLevel = 1;
            if (em.HasComponent<BuildingUpgradeState>(entity))
            {
                int lv = em.GetComponentData<BuildingUpgradeState>(entity).Level;
                if (lv > buildingLevel) buildingLevel = lv;
            }
            if (em.HasComponent<TempleLevel>(entity))
            {
                int lv = em.GetComponentData<TempleLevel>(entity).Level;
                if (lv > buildingLevel) buildingLevel = lv;
            }

            // Only show units this building can train (from its "trains" array)
            foreach (var unitId in buildingDef.trains)
            {
                if (!TechCatalog.TryGetUnit(unitId, out var unit)) continue;

                // Culture gating: skip units that require a different culture
                byte requiredCulture = GetRequiredCultureForUnit(unitId);
                if (requiredCulture != Cultures.None && requiredCulture != factionCulture)
                    continue;

                // Building-level gating: advanced units (minBuildingLevel >= 2)
                // stay locked until the trainer reaches the required level.
                int minLv = unit.minBuildingLevel < 1 ? 1 : unit.minBuildingLevel;
                bool levelLocked = buildingLevel < minLv;

                var cost = unit.cost != null ? new Cost
                {
                    Supplies = unit.cost.Supplies,
                    Iron = unit.cost.Iron,
                    Crystal = unit.cost.Crystal,
                    Veilsteel = unit.cost.Veilsteel,
                } : default;

                string tooltip = BuildTooltip(
                    unit.name,
                    unit.unitClass,
                    cost,
                    available,
                    trainingTime: unit.trainingTime
                );
                if (levelLocked)
                    tooltip = $"Requires Lv {minLv} {buildingDef.name ?? buildingId}\n" + tooltip;

                actions.Add(new ActionButton
                {
                    Id = unit.id,
                    Label = levelLocked ? $"{unit.name}  (Lv {minLv})" : unit.name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = !levelLocked,
                    CanAfford = !levelLocked && FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }

            return actions;
        }

        /// <summary>
        /// Extract current training state from a building for the progress bar.
        /// </summary>
        private static TrainingInfo GetTrainingInfo(Entity entity, EntityManager em)
        {
            var tInfo = new TrainingInfo();

            if (!em.HasComponent<TrainingState>(entity)) return tInfo;

            var ts = em.GetComponentData<TrainingState>(entity);
            var queue = em.GetBuffer<TrainQueueItem>(entity);

            // Total items in buffer (including currently training)
            tInfo.QueueCapacity = queue.Length;

            if (ts.Busy != 0 && queue.Length > 0)
            {
                string unitId = queue[0].UnitId.ToString();
                tInfo.IsTraining = true;
                tInfo.CurrentUnitId = unitId;

                // Get total training time from TechTreeDB to compute progress
                float totalTime = 1f;
                if (TechCatalog.TryGetUnit(unitId, out var udef))
                    totalTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;

                tInfo.Total = totalTime;
                tInfo.TimeRemaining = ts.Remaining > 0 ? ts.Remaining : 0f;
                tInfo.Progress = totalTime > 0 ? 1f - (tInfo.TimeRemaining / totalTime) : 1f;
            }

            // Build queue display (excludes currently training item)
            if (queue.Length > 0)
            {
                int startIndex = ts.Busy != 0 ? 1 : 0; // skip current if training
                var queueList = new List<string>();
                for (int i = startIndex; i < queue.Length; i++)
                    queueList.Add(queue[i].UnitId.ToString());
                tInfo.Queue = queueList.ToArray();
            }
            else
            {
                tInfo.Queue = System.Array.Empty<string>();
            }

            return tInfo;
        }

        /// <summary>
        /// Look up a unit's training cost from TechTreeDB for refund purposes.
        /// Returns a zero cost if the unit is not found.
        /// </summary>
        public static TheWaningBorder.Core.Cost GetUnitCost(string unitId)
        {
            if (TechCatalog.TryGetUnit(unitId, out var udef) && udef.cost != null)
            {
                return new TheWaningBorder.Core.Cost
                {
                    Supplies = udef.cost.Supplies,
                    Iron = udef.cost.Iron,
                    Crystal = udef.cost.Crystal,
                    Veilsteel = udef.cost.Veilsteel,
                };
            }
            return default;
        }

        /// <summary>
        /// Get research action buttons for a building.
        /// Returns buttons for techs this building can research, with affordability and prerequisite checks.
        /// </summary>
        public static List<ActionButton> GetResearchActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            // Chapel special case: research action derived from SectConfig, not TechTreeDB
            if (em.HasComponent<ChapelTag>(entity))
            {
                return GetChapelResearchActions(entity, em, faction);
            }

            string buildingId = GetBuildingId(entity, em);
            if (buildingId == null || !TechCatalog.IsReady) return actions;

            if (!TechCatalog.TryGetBuilding(buildingId, out var buildingDef)) return actions;
            if (buildingDef.research == null || buildingDef.research.Length == 0) return actions;

            var researchState = TheWaningBorder.Economy.FactionResearchState.Instance;
            Cost available = GetFactionResourcesAsCost(em, faction);

            foreach (var techId in buildingDef.research)
            {
                if (!TechCatalog.TryGetTechnology(techId, out var tech)) continue;

                // Skip Research_Era2 — age-up is handled by DrawAgeUpSection + CultureChoicePopup
                if (techId == "Research_Era2") continue;

                // Skip already-researched techs
                bool alreadyResearched = researchState != null && researchState.HasResearched(faction, techId);
                if (alreadyResearched) continue;

                var cost = tech.cost != null ? new Cost
                {
                    Supplies = tech.cost.Supplies,
                    Iron = tech.cost.Iron,
                    Crystal = tech.cost.Crystal,
                    Veilsteel = tech.cost.Veilsteel,
                } : default;

                bool canAfford = FactionEconomy.CanAfford(em, faction, cost);
                bool meetsPrereqs = researchState == null || researchState.MeetsPrerequisites(faction, tech.prerequisites);

                string requirement = null;
                if (!meetsPrereqs && tech.prerequisites != null)
                    requirement = $"Requires: {string.Join(", ", tech.prerequisites)}";

                string tooltip = BuildTooltip(
                    tech.name,
                    tech.desc ?? tech.effect,
                    cost,
                    available,
                    trainingTime: tech.researchTime,
                    requirement: requirement
                );

                actions.Add(new ActionButton
                {
                    Id = tech.id,
                    Label = tech.name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = meetsPrereqs,
                    CanAfford = canAfford && meetsPrereqs,
                    Icon = null
                });
            }

            return actions;
        }

        /// <summary>
        /// Extract current research state from a building for the progress bar.
        /// </summary>
        private static ResearchInfo GetResearchInfo(Entity entity, EntityManager em)
        {
            var rInfo = new ResearchInfo();

            if (!em.HasComponent<ResearchState>(entity)) return rInfo;

            var rs = em.GetComponentData<ResearchState>(entity);
            var queue = em.GetBuffer<ResearchQueueItem>(entity);

            if (rs.Busy != 0 && queue.Length > 0)
            {
                string techId = queue[0].TechId.ToString();
                rInfo.IsResearching = true;
                rInfo.CurrentTechId = techId;

                // Get total research time from TechTreeDB to compute progress
                float totalTime = 30f;
                if (TechCatalog.TryGetTechnology(techId, out var techDef))
                {
                    totalTime = techDef.researchTime > 0 ? techDef.researchTime : 30f;
                    rInfo.CurrentTechName = techDef.name;
                }
                else
                {
                    rInfo.CurrentTechName = techId;
                }

                rInfo.Total = totalTime;
                rInfo.TimeRemaining = rs.Remaining > 0 ? rs.Remaining : 0f;
                rInfo.Progress = totalTime > 0 ? 1f - (rInfo.TimeRemaining / totalTime) : 1f;
            }

            // Build queue display
            if (queue.Length > 0)
            {
                int startIndex = rs.Busy != 0 ? 1 : 0;
                var queueList = new List<string>();
                for (int i = startIndex; i < queue.Length; i++)
                    queueList.Add(queue[i].TechId.ToString());
                rInfo.Queue = queueList.ToArray();
            }
            else
            {
                rInfo.Queue = System.Array.Empty<string>();
            }

            return rInfo;
        }

        /// <summary>
        /// Determine the required culture for a building based on its ID prefix.
        /// Buildings with "Alanthor_" prefix require Alanthor culture, etc.
        /// Returns Cultures.None for universal buildings (available to all cultures).
        /// </summary>
        private static byte GetRequiredCulture(string buildingId)
        {
            if (buildingId.StartsWith("Alanthor_")) return Cultures.Alanthor;
            if (buildingId.StartsWith("Feraldis_")) return Cultures.Feraldis;
            if (buildingId.StartsWith("Runai_")) return Cultures.Runai;
            // FiendstoneKeep is a choice building (like Temple/Vault) — available to all cultures
            if (buildingId == "FiendstoneKeep") return Cultures.None;
            // ThessarasBazaar is a Runai building (doesn't use Runai_ prefix)
            if (buildingId == "ThessarasBazaar") return Cultures.Runai;
            return Cultures.None; // universal
        }

        /// <summary>
        /// Determine the required culture for a unit based on its ID prefix.
        /// Units with "Alanthor_" prefix require Alanthor culture, etc.
        /// Returns Cultures.None for universal units (available to all cultures).
        /// </summary>
        private static byte GetRequiredCultureForUnit(string unitId)
        {
            if (unitId.StartsWith("Alanthor_")) return Cultures.Alanthor;
            if (unitId.StartsWith("Feraldis_")) return Cultures.Feraldis;
            if (unitId.StartsWith("Runai_")) return Cultures.Runai;
            return Cultures.None; // universal
        }

        /// <summary>
        /// Map entity to its TechTree building ID using tag components.
        /// </summary>
        // ═══════════════════════════════════════════════════════════════════
        // CHAPEL TRAINING & RESEARCH (from SectConfig, not TechTreeDB)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get training actions for a chapel entity.
        /// Each chapel trains one unique sect unit defined in SectConfig.
        /// </summary>
        private static List<ActionButton> GetChapelTrainingActions(Entity entity, EntityManager em, Faction faction)
        {
            // task-063 phase 1: chapel training actions previously read
            // SectConfig.GetSectUnitId / GetDisplayName for the deleted 12-sect
            // unique-unit mapping. Phase 2 reintroduces these via the new
            // sect's "Unit" lever (Lorekeeper / Tinker / Aegis-Bearer / etc.)
            // backed by SectAdoptionState.UnitLevel.
            _ = entity; _ = em; _ = faction;
            return new List<ActionButton>();
        }

        /// <summary>
        /// Get training actions for the Temple of Ridan.
        /// Returns training buttons for ALL adopted sect units (from completed chapel slots).
        /// Also includes Litharch as base temple unit.
        /// </summary>
        private static List<ActionButton> GetTempleTrainingActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            Cost available = GetFactionResourcesAsCost(em, faction);

            // Base temple unit: Litharch
            if (TechCatalog.TryGetUnit("Litharch", out var lithUnit))
            {
                var cost = lithUnit.cost != null ? new Cost
                {
                    Supplies = lithUnit.cost.Supplies,
                    Iron = lithUnit.cost.Iron,
                    Crystal = lithUnit.cost.Crystal
                } : default;

                actions.Add(new ActionButton
                {
                    Id = "Litharch",
                    Label = lithUnit.name,
                    Tooltip = BuildTooltip(lithUnit.name, lithUnit.unitClass, cost, available, trainingTime: lithUnit.trainingTime),
                    Cost = cost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }

            // task-063 phase 1: temple's "train every adopted sect's unique
            // unit" loop removed — relied on the deleted SectConfig.GetSectUnitId
            // mapping for the 12 old sects. Phase 2 reintroduces this against
            // the new sect roster + each sect's UnitLevel (Lv I/II/III).

            return actions;
        }

        /// <summary>
        /// Get research actions for a chapel entity.
        /// task-063 phase 1: stub. Sect tech research is gone in the redesign —
        /// each chapel exposes 4 lever-upgrade buttons (Passive / Building /
        /// Unit / Active Power) instead. Phase 2 reintroduces upgrade
        /// actions backed by SectAdoption.TryUpgradeLever; not Tech research.
        /// </summary>
        private static List<ActionButton> GetChapelResearchActions(Entity entity, EntityManager em, Faction faction)
        {
            _ = entity; _ = em; _ = faction;
            return new List<ActionButton>();
        }

        private static string GetBuildingId(Entity entity, EntityManager em)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<ArcheryRangeTag>(entity)) return "ArcheryRange";
            if (em.HasComponent<GathererHutTag>(entity)) return "GatherersHut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<ShrineTag>(entity)) return "ShrineOfAhridan";
            if (em.HasComponent<TempleOfRidanTag>(entity)) return "TempleOfRidan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";
            if (em.HasComponent<SmelterTag>(entity)) return "Alanthor_Smelter";
            // Runai culture buildings
            if (em.HasComponent<OutpostTag>(entity)) return "Runai_Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Runai_TradeHub";
            if (em.HasComponent<TradingPostTag>(entity)) return "Runai_TradingPost";
            if (em.HasComponent<BazaarTag>(entity)) return "ThessarasBazaar";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Runai_SiegeWorkshop";
            // Alanthor culture buildings
            if (em.HasComponent<WatchTowerTag>(entity)) return "Alanthor_Tower";
            if (em.HasComponent<PracticeRangeTag>(entity)) return "Alanthor_PracticeRange";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Alanthor_SiegeYard";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Alanthor_RoyalStable";
            // Feraldis culture buildings
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Feraldis_HuntingLodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Feraldis_LoggingStation";
            if (em.HasComponent<LonghouseTag>(entity)) return "Feraldis_Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Feraldis_Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Feraldis_SiegeYard";
            // Sect chapels — dynamic building ID based on sect
            if (em.HasComponent<ChapelTag>(entity))
            {
                var chapelTag = em.GetComponentData<ChapelTag>(entity);
                return "Chapel_" + chapelTag.SectId.ToString();
            }
            return null;
        }
    }
}