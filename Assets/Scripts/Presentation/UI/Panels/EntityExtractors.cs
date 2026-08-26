// EntityExtractors.cs
// Helper classes to extract UI display info from ECS entities
// Location: Assets/Scripts/UI/Common/EntityExtractors.cs
// Core file: GetDisplayInfo / GetActionInfo entry points, queue snapshot,
// faction-level query helpers, and shared cost/tooltip helpers. Sibling
// partials: .Names (display-name/id resolution), .Buildings (placement +
// conversion actions), .Training (training actions/state), .Research
// (research actions/state).

using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI
{
    /// <summary>
    /// Extracts display information from entities for EntityInfoPanel.
    /// </summary>
    public static partial class EntityInfoExtractor
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
                VeilstonePerMinute = 0,
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
                info.Defense = (int)def.Melee; // legacy single cell (web HUD)
                info.DefenseMelee = (int)def.Melee;
                info.DefenseRanged = (int)def.Ranged;
                info.DefenseSiege = (int)def.Siege;
                info.DefenseMagic = (int)def.Magic;
            }
            // else: leave info.Defense null.

            // Speed: hidden for buildings entirely (task-108 R5).
            if (!isBuilding && em.HasComponent<MoveSpeed>(entity))
            {
                info.Speed = em.GetComponentData<MoveSpeed>(entity).Value;
            }
            // else: leave info.Speed null.

            // ── Extended combat detail (2026-07-18 selection stats panel) ──
            if (info.Attack.HasValue)
            {
                if (em.HasComponent<AttackCooldown>(entity))
                    info.AttackCooldown = em.GetComponentData<AttackCooldown>(entity).Cooldown;

                // Ranged attackers carry ArcherState (units) or
                // BuildingRangedAttack (buildings); everyone else is melee
                // (fixed edge-aware reach) and leaves Range null.
                if (em.HasComponent<ArcherState>(entity))
                {
                    var archer = em.GetComponentData<ArcherState>(entity);
                    info.RangeMin = archer.MinRange;
                    info.RangeMax = archer.MaxRange;
                }
                else if (isBuilding && em.HasComponent<BuildingRangedAttack>(entity))
                {
                    info.RangeMin = 0f;
                    info.RangeMax = em.GetComponentData<BuildingRangedAttack>(entity).Range;
                }

                // DamageTypeData defaults to Melee when absent (combat rule).
                var dmgType = em.HasComponent<DamageTypeData>(entity)
                    ? em.GetComponentData<DamageTypeData>(entity).Value
                    : DamageType.Melee;
                info.DamageTypeName = dmgType.ToString();
            }

            // ArmorType defaults: InfantryLight for units, Structure for
            // buildings (mirrors CombatModifiers' absent-component default).
            // ArmorTypeName is a pure display field (rendered verbatim by the
            // stat chips) so it localizes HERE; DamageTypeName above must stay
            // ENGLISH — it is a GameUICatalog symbol key ("AttackType_" + name).
            if (em.HasComponent<ArmorTypeData>(entity))
                info.ArmorTypeName = Loc.T(ArmorTypeDisplayName(
                    em.GetComponentData<ArmorTypeData>(entity).Value));
            else if (isBuilding)
                info.ArmorTypeName = Loc.T("Structure");
            else if (info.HasCombatStats)
                info.ArmorTypeName = Loc.T(ArmorTypeDisplayName(ArmorType.InfantryLight));

            if (em.HasComponent<BonusVsTags>(entity))
            {
                var bonus = em.GetComponentData<BonusVsTags>(entity);
                if (!bonus.IsEmpty)
                    info.BonusVsText = BuildBonusText(bonus);
            }

            if (em.HasComponent<LineOfSight>(entity))
                info.SightRadius = em.GetComponentData<LineOfSight>(entity).Radius;

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
            if (em.HasComponent<VeilstoneIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.VeilstonePerMinute = em.GetComponentData<VeilstoneIncome>(entity).PerMinute;
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
            if (em.HasComponent<BorderMainNodeTag>(entity))
            {
                info.Type = "Veilstone Hive";
                info.Name = "Veilstone Main Node";
                if (em.HasComponent<BorderNodeLevel>(entity))
                {
                    int level = em.GetComponentData<BorderNodeLevel>(entity).Value;
                    string threat = level switch { 1 => "Low Threat", 2 => "Moderate Threat", _ => "High Threat" };
                    info.Description = $"Level {level} — {threat}";
                }
                if (em.HasComponent<BorderNode>(entity) && em.HasComponent<BorderSpreadState>(entity))
                {
                    var cn = em.GetComponentData<BorderNode>(entity);
                    var ss = em.GetComponentData<BorderSpreadState>(entity);
                    int pct = cn.SpreadRadius > 0 ? (int)(ss.CurrentRingRadius / cn.SpreadRadius * 100f) : 0;
                    info.Description += $"\nSpread: {pct}%";
                }
            }
            else if (em.HasComponent<BuildingTag>(entity))
            {
                info.Type = "Building";
                // Same resolver as the selection header, so the info panel and the
                // header can never disagree — and both pick up the DisplayName
                // stamped at creation instead of re-deriving it from tags.
                info.Name = GetSelectionDisplayName(entity, em);
            }
            else if (em.HasComponent<UnitTag>(entity))
            {
                info.Type = "Unit";
                info.Name = GetSelectionDisplayName(entity, em);
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
            else if (em.HasComponent<VeilsteelDepositTag>(entity))
            {
                info.Type = "Resource";
                info.Name = VeilsteelNodeName;
                info.HasResourceInfo = true;
                // Veilsteel nodes share IronDepositState (identical mining model).
                if (em.HasComponent<IronDepositState>(entity))
                {
                    var depState = em.GetComponentData<IronDepositState>(entity);
                    info.ResourceRemaining = depState.RemainingIron;
                    info.ResourceMax = depState.InitialIron > 0
                        ? depState.InitialIron
                        : depState.RemainingIron;
                    info.ResourceTypeName = "Veilsteel";
                    info.Description = depState.Depleted == 1 ? "Depleted" : "Harvestable veilsteel";
                }
            }
            else if (em.HasComponent<VeilstoneOutcroppingTag>(entity))
            {
                info.Type = "Resource";
                info.Name = "Veilstone Node";
                info.HasResourceInfo = true;
                if (em.HasComponent<VeilstoneOutcroppingState>(entity))
                {
                    var cadState = em.GetComponentData<VeilstoneOutcroppingState>(entity);
                    info.ResourceRemaining = cadState.RemainingVeilstone;
                    info.ResourceMax = cadState.MaxVeilstone > 0 ? cadState.MaxVeilstone : cadState.RemainingVeilstone;
                    info.ResourceTypeName = "Veilstone";
                    info.Description = cadState.Depleted == 1 ? "Depleted" : "Harvestable veilstone";
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

            // Forge passive generation info — output scales with the
            // Smelter's upgrade level (mirrors ForgeConversionSystem).
            if (em.HasComponent<ForgeStorage>(entity))
            {
                int interval = (int)TheWaningBorder.Systems.Economy.ForgeConversionSystem.GenerationInterval;
                int perTick = TheWaningBorder.Systems.Economy.ForgeConversionSystem.VeilsteelPerTick;
                int level = 1;
                if (em.HasComponent<BuildingUpgradeState>(entity))
                {
                    int lvl = em.GetComponentData<BuildingUpgradeState>(entity).Level;
                    if (lvl > 1) level = lvl;
                }
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Generating {level * perTick} veilsteel / {interval}s";
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

                if (miner.GatheringResource == 1)
                {
                    info.MinerResourceType = "Veilstone";
                    info.MinerExtractionRate = "1 veilstone / 1.5s";
                }
                else if (miner.GatheringResource == 2)
                {
                    info.MinerResourceType = "Veilsteel";
                    info.MinerExtractionRate = "1 veilsteel / 2s";
                }
                else
                {
                    info.MinerResourceType = "Iron";
                    info.MinerExtractionRate = "1 iron / 2s";
                }

                info.MinerState = miner.State switch
                {
                    MinerWorkState.Idle => "Idle",
                    MinerWorkState.MovingToDeposit => "Moving to resource",
                    MinerWorkState.Gathering => "Gathering",
                    _ => "Unknown"
                };
            }

            // task-108 phase 1: EntityKind discriminator. Drives JSX conditional
            // rendering (collapse speed cell for buildings, amber bar for resources).
            if (isBuilding)
            {
                info.EntityKind = "building";
            }
            else if (em.HasComponent<IronMineTag>(entity) || em.HasComponent<VeilstoneOutcroppingTag>(entity)
                     || em.HasComponent<VeilsteelDepositTag>(entity))
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
                arr[i].RefundVeilstone = cost.Veilstone;
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
            var eraQuery = _eraQuery.Get(em, EraQueryTypes);

            using var entities = eraQuery.ToEntityArray(Allocator.Temp);
            using var tags = eraQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var eras = eraQuery.ToComponentDataArray<FactionEra>(Allocator.Temp);

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
            var rpQuery = _rpQuery.Get(em, RpQueryTypes);

            using var entities = rpQuery.ToEntityArray(Allocator.Temp);
            using var tags = rpQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var rps = rpQuery.ToComponentDataArray<FactionReligionPoints>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                    return rps[i].Balance;
            }

            return 0;
        }

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] EraQueryTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionEra>(),
        };
        private static readonly ComponentType[] RpQueryTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionReligionPoints>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery _eraQuery;
        private static TheWaningBorder.Core.CachedEntityQuery _rpQuery;
    }

    /// <summary>
    /// Extracts action information from entities for EntityActionPanel.
    /// </summary>
    public static partial class EntityActionExtractor
    {
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
                        Label = Loc.T("Build Wall"),
                        Tooltip = Loc.T("Place a connected wall hub. Auto-builds in 30s with no builder."),
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
            //   - "Convert to Gate (Nx)" — 3-instance segment-level conversion
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
                        Label = Loc.T("Unpack"),
                        Tooltip = Loc.T("Unpack wagon back into Thessara's Bazaar"),
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

            // The Reliquary (Antiquity building lever): three triggered
            // intel abilities instead of training.
            if (em.HasComponent<ReliquaryTag>(entity) && em.HasComponent<ReliquaryState>(entity)
                && !em.HasComponent<UnderConstruction>(entity))
            {
                info.Type = ActionType.UnitTraining;   // reuses the action-grid panel
                info.Actions = GetReliquaryActions(entity, em);
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
                        Label = Loc.T("Pack"),
                        Tooltip = Loc.T("Pack Bazaar into a mobile wagon"),
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
                Veilstone = res.Veilstone,
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
            sb.Append($"<b>{Loc.T(name)}</b>");
            if (!string.IsNullOrEmpty(subtitle))
                sb.Append($"  <color=#b0a890>({Loc.T(subtitle)})</color>");

            // Cost line. The "\n" + Loc.T("Cost: ") composition is a CONTRACT:
            // ActionsPanelPrefabBinder.ExpandTooltip splits on the exact same
            // expression to splice the cost icons in. Keep them in lockstep.
            sb.Append("\n" + Loc.T("Cost: "));
            sb.Append(UIHelpers.FormatCostRich(cost, available));

            // Training/build time
            if (trainingTime > 0f)
                sb.Append("\n").Append(string.Format(Loc.T("Time: {0}s"), trainingTime.ToString("F0")));

            // Requirement (shown in red)
            if (!string.IsNullOrEmpty(requirement))
                sb.Append($"\n<color=#ff5555>{requirement}</color>");

            return sb.ToString();
        }
    }
}
