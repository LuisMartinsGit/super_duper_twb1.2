// EntityExtractors.cs
// Helper classes to extract UI display info from ECS entities
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

namespace TheWaningBorder.UI.Data
{
    /// <summary>
    /// Extracts display information from entities for EntityInfoPanel.
    /// </summary>
    public static partial class EntityInfoExtractor
    {
        /// <summary>
        /// The entity's shield (equipment-tier ShieldBar): extra hit points
        /// every hit spends before Health (docs/Design/Combat_Pacing.md).
        /// False, with 0 / 0, when the entity carries no shield.
        /// </summary>
        public static bool TryGetShield(EntityManager em, Entity entity, out int current, out int max)
        {
            current = 0; max = 0;
            if (!em.Exists(entity) || !em.HasComponent<ShieldBar>(entity)) return false;
            var sb = em.GetComponentData<ShieldBar>(entity);
            if (sb.Max <= 0) return false;
            max = sb.Max;
            current = sb.Current < 0 ? 0 : (sb.Current > sb.Max ? sb.Max : sb.Current);
            return true;
        }

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
                ShardrootPerMinute = 0,
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
            TryGetShield(em, entity, out info.CurrentShield, out info.MaxShield);

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

            // THE POWER NUMBER (docs/Design/Unit_Power.md). Read off the unit's
            // def, not off the live entity: it is a statement about the unit
            // TYPE — what it is worth for what it costs — and reading a
            // veteran-ranked or buffed instance would make the same unit report
            // a different number depending on which one you clicked.
            if (!isBuilding && em.HasComponent<UnitTypeId>(entity))
            {
                string unitId = em.GetComponentData<UnitTypeId>(entity).Value.ToString();
                if (!string.IsNullOrEmpty(unitId)
                    && TechCatalog.TryGetUnit(unitId, out var unitDef) && unitDef != null)
                {
                    var power = TheWaningBorder.Data.UnitPower.Breakdown(unitDef);
                    if (power.Measurable) info.PowerRating = power.Power;
                }
            }

            // THE TERRITORY READOUT (docs/Design/Regions.md §4). The capital is
            // the home of a territory, so it is where that territory states
            // what it pays — per minute, by resource, with nothing left for the
            // player to infer by watching their bank tick.
            //
            // Read straight from TerritoryIncomeSystem.ComputeYield, the same
            // call the income tick pays out of. Two implementations would drift,
            // and a readout that lies about income is worse than no readout.
            if (em.HasComponent<HallTag>(entity)
                && em.HasComponent<Unity.Transforms.LocalTransform>(entity))
            {
                var hp = em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Position;
                int territory = TheWaningBorder.World.Regions.RegionMap.RegionAt(hp.x, hp.z);
                if (territory != TheWaningBorder.World.Regions.RegionMap.None)
                {
                    var faction = em.HasComponent<FactionTag>(entity)
                        ? em.GetComponentData<FactionTag>(entity).Value
                        : GameSettings.LocalPlayerFaction;
                    var ty = TheWaningBorder.Systems.World.TerritoryIncomeSystem
                        .ComputeYieldForDisplay(em, territory, faction);

                    info.HasResourceGeneration = true;
                    info.SuppliesPerMinute  = ty.Supplies;
                    info.IronPerMinute      = UnityEngine.Mathf.RoundToInt(ty.Iron);
                    info.VeilstonePerMinute = UnityEngine.Mathf.RoundToInt(ty.Veilstone);
                    info.VeilsteelPerMinute = UnityEngine.Mathf.RoundToInt(ty.Veilsteel);
                    info.YieldPerMinute     = ty.Supplies;
                    info.TerritoryName      =
                        TheWaningBorder.World.Regions.RegionMap.NameOf(territory);
                }
            }

            // Resource generation
            if (em.HasComponent<SuppliesIncome>(entity))
            {
                info.HasResourceGeneration = true;
                var si = em.GetComponentData<SuppliesIncome>(entity);
                info.SuppliesPerMinute = si.PerMinute;
                // task-108 R2: surface per-minute supplies as a dedicated yield
                // row for buildings (capital trickle, GathererHut overlap yield).
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
                info.Name = WithPurity(entity, em, "Iron Deposit");
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
                info.Name = WithPurity(entity, em, "Veilstone Node");
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
            else if (em.HasComponent<ShardrootPickupTag>(entity))
            {
                // The Shardroot lying on the ground (Curse_And_Shardroot.md
                // §3.1): owned by nobody, no health, nothing to fight.
                info.Type = "Artifact";
                info.Name = "Shardroot";
                info.Faction = "Neutral";
                info.CurrentHealth = null;
                info.MaxHealth = null;
                info.Description = Loc.T("The Shardroot lies here. Any unit that reaches it takes it at once -- right-click it with a unit to claim it. King Lexor bears it as the Shardbound King.");
            }

            // Temple: the faction's Religion Points (the Temple has no levels)
            if (em.HasComponent<TempleOfRidanTag>(entity) && em.HasComponent<FactionTag>(entity))
            {
                var faction = em.GetComponentData<FactionTag>(entity).Value;
                int rp = GetFactionReligionPoints(em, faction);
                if (rp > 0)
                    info.Description += (info.Description.Length > 0 ? "\n" : "")
                        + $"Religion Points: {rp}";
            }

            // Trading Outpost: the trade it runs, per minute
            // (docs/Design/Veilstone_Economy.md §3.1).
            if (em.HasComponent<TradingOutpostTag>(entity) && em.HasComponent<FactionTag>(entity))
            {
                // This post's own rate: recipe, research AND its level.
                TheWaningBorder.Systems.Economy.TradingOutpostSystem.PerMinuteFor(em, entity, out var spend, out var earn);
                string line = EntityActionExtractor.PerMinuteLine(spend, "-") + "  ->  "
                              + EntityActionExtractor.PerMinuteLine(earn, "+") + " /min";
                // Its place in the outcrop's cost ramp (level-ups are priced on it).
                int rampIndex = TheWaningBorder.Entities.TradingOutpost.RampIndexOf(em, entity);
                float rampMult = TheWaningBorder.Entities.TradingOutpost.RampMultiplier(rampIndex);
                line += "\n" + string.Format(Loc.T("Post {0} of {1} beside its outcrop — level-ups cost x{2}"),
                    rampIndex + 1, TheWaningBorder.Entities.TradingOutpost.SideCount, rampMult.ToString("0.##"));
                var op = em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Position;
                if (!TheWaningBorder.Entities.TradingOutpost.HasLiveOutcrop(em, op.x, op.z))
                    line += "\n" + Loc.T("Idle — its outcrop is cursed. Destroy the curse node to trade again.");
                info.Description += (info.Description.Length > 0 ? "\n" : "") + line;
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

            // Worker info
            if (em.HasComponent<WorkerTag>(entity) && em.HasComponent<WorkerState>(entity))
            {
                var worker = em.GetComponentData<WorkerState>(entity);
                info.HasWorkerInfo = true;

                if (worker.GatheringResource == 1)
                {
                    info.WorkerResourceType = "Veilstone";
                    info.WorkerExtractionRate = "1 veilstone / 1.5s";
                }
                else if (worker.GatheringResource == 2)
                {
                    info.WorkerResourceType = "Veilsteel";
                    info.WorkerExtractionRate = "1 veilsteel / 2s";
                }
                else
                {
                    info.WorkerResourceType = "Iron";
                    info.WorkerExtractionRate = "1 iron / 2s";
                }

                info.WorkerState = worker.State switch
                {
                    WorkerActivity.Idle => "Idle",
                    WorkerActivity.MovingToDeposit => "Moving to resource",
                    WorkerActivity.Gathering => "Gathering",
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

            // task-108 phase 1: production queue snapshot — a strip for any
            // building with a ProductionState + ProductionQueueItem buffer.
            // Slot 0 carries live progress when ProductionState.Busy == 1.
            if (isBuilding
                && em.HasComponent<ProductionState>(entity)
                && em.HasBuffer<ProductionQueueItem>(entity))
            {
                info.QueueCapacity = TheWaningBorder.Core.Commands.CommandRouter.MaxProductionQueue;
                info.Queue = BuildQueueSnapshot(entity, em, info.QueueCapacity.Value);
            }

            return info;
        }

        /// <summary>
        /// Build a fixed-length snapshot of a building's production queue for
        /// the Web HUD selection topic. Always returns an array of length
        /// <paramref name="capacity"/> (matches CommandRouter.MaxProductionQueue);
        /// slots beyond the live buffer are marked Populated=false. Slot 0
        /// carries ProductionState-derived progress so the JSX strip can render
        /// the in-production fill. Research and level-ups ride the same slots
        /// as units now; their refund columns stay zero — the strip's refund
        /// readout was only ever unit-priced.
        /// </summary>
        private static EntityQueueSlot[] BuildQueueSnapshot(Entity e, EntityManager em, int capacity)
        {
            var arr = new EntityQueueSlot[capacity];
            var buf = em.GetBuffer<ProductionQueueItem>(e);
            var ts = em.GetComponentData<ProductionState>(e);

            // Slot 0's total is the one the clock captured at start — the
            // catalog number it used to recompute ignored every sect and
            // level multiplier.
            float slot0Total = ts.Total;

            for (int i = 0; i < capacity; i++)
            {
                if (i >= buf.Length)
                {
                    arr[i].Populated = false;
                    continue;
                }
                bool isUnit = buf[i].Kind == ProductionKind.Train;
                string uid = buf[i].Id.ToString();
                var cost = isUnit ? EntityActionExtractor.GetUnitCost(uid) : default;
                arr[i].Populated = true;
                arr[i].UnitId = uid;
                arr[i].DisplayName = isUnit
                    ? ResolveUnitDisplayName(uid)
                    : EntityActionExtractor.DescribeProductionItem(buf[i]);
                arr[i].RefundSupplies = cost.Supplies;
                arr[i].RefundIron = cost.Iron;
                arr[i].RefundVeilstone = cost.Veilstone;
                arr[i].RefundVeilsteel = cost.Veilsteel;
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
            // placement mode (WorkerCommandPanel.TriggerHubBuildWall) that
            // drops a new hub + auto-connecting segment with no worker
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
                // The hub extends its OWN kind of wall. A palisade its owner
                // can no longer build (Alanthor / Runai after age-up) offers no
                // Build Wall; a palisade hub has no levels to research.
                // docs/Design/Age_0.md § Palisade
                bool palisadeHub = TheWaningBorder.Entities.AlanthorWall.IsPalisade(em, entity);
                Cost hubCost = default;
                BuildCosts.TryGet(TheWaningBorder.Entities.AlanthorWall.HubIdFor(palisadeHub), out hubCost);
                bool canAfford = FactionEconomy.CanAfford(em,
                    GameSettings.LocalPlayerFaction, hubCost);

                info.Type = ActionType.HubBuildWall;
                info.Actions = new List<ActionButton>();
                if (TheWaningBorder.Entities.WallTiers.CanBuild(em, GameSettings.LocalPlayerFaction, palisadeHub))
                    info.Actions.Add(new ActionButton
                    {
                        Id = "BuildWall",
                        Label = Loc.T("Build Wall"),
                        Tooltip = Loc.T("Place a connected wall hub. Auto-builds in 30s with no worker."),
                        Enabled = true,
                        Cost = hubCost,
                        CanAfford = canAfford,
                    });
                if (!palisadeHub)
                    AddWallLevelAction(info.Actions, entity, em,
                                       GameSettings.LocalPlayerFaction);
                ApplyWallLock(ref info, entity, em, GameSettings.LocalPlayerFaction);
                return info;
            }


            // A GATE: open / close it to friendly units
            // (docs/Design/Age_1_Alanthor.md § Opening and closing it). The
            // default is proximity — it opens for friendlies within 6 m — and
            // SEALED shuts it to its own faction too.
            if (em.HasComponent<WallGateTag>(entity)
                && !em.HasComponent<UnderConstruction>(entity)
                && em.HasComponent<FactionTag>(entity)
                && em.GetComponentData<FactionTag>(entity).Value == GameSettings.LocalPlayerFaction)
            {
                bool sealedShut = TheWaningBorder.Core.Commands.CommandRouter.IsGateSealed(em, entity);
                info.Type = ActionType.WallInstanceUpgrade;
                info.Actions = new List<ActionButton>
                {
                    new ActionButton
                    {
                        Id = sealedShut ? "GateOpen" : "GateClose",
                        Label = sealedShut ? Loc.T("Open Gate") : Loc.T("Close Gate"),
                        Tooltip = sealedShut
                            ? Loc.T("Hand the gate back to its garrison: it opens for friendly units that come near and closes behind them.")
                            : Loc.T("Bar the gate. It stays shut to your own units too — nothing routes through it while it is sealed."),
                        Enabled = true,
                        CanAfford = true,
                    }
                };
                ApplyWallLock(ref info, entity, em, GameSettings.LocalPlayerFaction);
                return info;
            }

            // A TRADING OUTPOST: its three trades in the top row, its research
            // (the two trade unlocks and the discount ladder) in the rows below.
            // docs/Design/Veilstone_Economy.md §3.1.
            if (em.HasComponent<TradingOutpostTag>(entity)
                && !em.HasComponent<UnderConstruction>(entity)
                && em.HasComponent<FactionTag>(entity)
                && em.GetComponentData<FactionTag>(entity).Value == GameSettings.LocalPlayerFaction)
            {
                var me = GameSettings.LocalPlayerFaction;
                var active = TheWaningBorder.Entities.TradingOutpost.RecipeOf(em, entity);
                info.Type = ActionType.UnitTrainingAndResearch;
                info.ProductionState = em.HasComponent<ProductionState>(entity)
                    ? GetProductionInfo(entity, em) : null;
                info.Actions = new List<ActionButton>
                {
                    OutpostRecipeButton(em, entity, me, TradeRecipe.BuyVeilstone, active, Loc.T("Buy Veilstone"), null),
                    OutpostRecipeButton(em, entity, me, TradeRecipe.ForgeVeilsteel, active, Loc.T("Forge Veilsteel"),
                        TheWaningBorder.Systems.Economy.TradingOutpostSystem.Cfg?.forgeTech),
                    OutpostRecipeButton(em, entity, me, TradeRecipe.SellVeilsteel, active, Loc.T("Sell Veilsteel"),
                        TheWaningBorder.Systems.Economy.TradingOutpostSystem.Cfg?.sellTech),
                    OutpostRecipeButton(em, entity, me, TradeRecipe.Hold, active, Loc.T("Hold Trade"), null),
                };
                return info;
            }

            // A REINFORCED curtain module with men inside it: let them out
            // (docs/Design/Age_1_Alanthor.md § Garrison slots).
            if (em.HasBuffer<WallGarrisonSlot>(entity)
                && em.HasComponent<FactionTag>(entity)
                && em.GetComponentData<FactionTag>(entity).Value == GameSettings.LocalPlayerFaction
                && TheWaningBorder.Entities.WallGarrison.OccupantCount(em, entity) > 0)
            {
                int manned = TheWaningBorder.Entities.WallGarrison.OccupantCount(em, entity);
                int slots = TheWaningBorder.Entities.WallGarrison.SlotCount(em, entity);
                info.Type = ActionType.WallInstanceUpgrade;
                info.Actions = new List<ActionButton>
                {
                    new ActionButton
                    {
                        Id = "WallUngarrison",
                        Label = string.Format(Loc.T("Empty ({0}/{1})"), manned, slots),
                        Tooltip = em.HasComponent<WatchTowerTag>(entity)
                            ? Loc.T("The men in this tower step back out beside it.")
                            : Loc.T("The men in this wall section step back down on the friendly side."),
                        Enabled = true,
                        CanAfford = true,
                    }
                };
                ApplyWallLock(ref info, entity, em, GameSettings.LocalPlayerFaction);
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
                if (em.HasComponent<FactionTag>(entity))
                    ApplyWallLock(ref info, entity, em,
                                  em.GetComponentData<FactionTag>(entity).Value);
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

            // Check if this is a worker (can place buildings)
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

            // Check if this is the Temple of Ridan (training + sect slots)
            if (em.HasComponent<TempleOfRidanTag>(entity) && em.HasComponent<ProductionState>(entity))
            {
                info.Type = ActionType.TempleTraining;
                info.Actions = GetTempleTrainingActions(entity, em);
                info.ProductionState = GetProductionInfo(entity, em);
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

            // A producing building. Training and research share the queue,
            // so the panel TYPE now turns on whether the building has a
            // roster to train — a research-only building falls through to
            // the block below.
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<ProductionState>(entity))
            {
                var trainingActions = GetTrainingActions(entity, em);
                bool hasResearch = true;

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
                    info.ProductionState = GetProductionInfo(entity, em);

                    return info;
                }
            }

            // Check if this is a research-only building
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<ProductionState>(entity))
            {
                info.Type = ActionType.UnitTrainingAndResearch;
                info.Actions = new List<ActionButton>();
                info.ProductionState = GetProductionInfo(entity, em);
                return info;
            }

            return info;
        }

        /// <summary>One Trading Outpost trade as a top-row button: its per-minute
        /// exchange in the tooltip, greyed while its research is missing, and
        /// marked (not clickable) while it is the trade being run.</summary>
        private static ActionButton OutpostRecipeButton(EntityManager em, Entity outpost, Faction me,
            TradeRecipe recipe, TradeRecipe active, string label, string requiredTech)
        {
            TheWaningBorder.Systems.Economy.TradingOutpostSystem.PerMinute(
                me, recipe, out var spend, out var earn);
            // This post's level scales its trade, both sides.
            float lvl = TheWaningBorder.Systems.Economy.TradingOutpostSystem.LevelMultiplier(em, outpost);
            if (lvl != 1f)
            {
                spend.Supplies *= lvl; spend.Iron *= lvl; spend.Veilstone *= lvl; spend.Veilsteel *= lvl;
                earn.Supplies *= lvl; earn.Iron *= lvl; earn.Veilstone *= lvl; earn.Veilsteel *= lvl;
            }
            bool unlocked = TheWaningBorder.Systems.Economy.TradingOutpostSystem.IsUnlocked(me, recipe);
            bool isActive = recipe == active;
            string tip = label + "\n" + PerMinuteLine(spend, "-") + "  ->  " + PerMinuteLine(earn, "+")
                         + " " + Loc.T("per minute");
            if (!unlocked && !string.IsNullOrEmpty(requiredTech))
            {
                string techName = TechCatalog.TryGetTechnology(requiredTech, out var t) && t != null
                    ? (t.name ?? requiredTech) : requiredTech;
                tip += "\n" + string.Format(Loc.T("Requires research: {0}"), techName);
            }
            else if (isActive) tip += "\n" + Loc.T("This Outpost is running this trade.");
            return new ActionButton
            {
                Id = "OutpostMode_" + (int)recipe,
                Label = isActive ? "> " + label : label,
                Tooltip = tip,
                Enabled = unlocked && !isActive,
                CanAfford = true,
            };
        }

        /// <summary>"-50 supplies, -50 iron" — one side of a per-minute exchange.</summary>
        internal static string PerMinuteLine(TheWaningBorder.Systems.World.TerritoryYield y, string sign)
        {
            var sb = new System.Text.StringBuilder();
            void Add(float v, string name)
            {
                int n = UnityEngine.Mathf.RoundToInt(v);
                if (n == 0) return;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(sign).Append(n).Append(' ').Append(name);
            }
            Add(y.Supplies, Loc.T("supplies"));
            Add(y.Iron, Loc.T("iron"));
            Add(y.Veilstone, Loc.T("veilstone"));
            Add(y.Veilsteel, Loc.T("veilsteel"));
            return sb.ToString();
        }

        /// <summary>
        /// The wall's own upgrade button, on the Wall Hub (2026-09-24).
        ///
        /// Lv0 timber -> Lv1 stone is the Alanthor culture pick and is free,
        /// so nothing is offered for it. The two BOUGHT levels are a chain in
        /// a single cell: Battlements while it is unclaimed, then Shielded
        /// Ramparts, then nothing. One purchase re-clads every wall the
        /// faction owns, so the button is identical on every hub and
        /// disappears from all of them the moment it is queued anywhere.
        /// docs/Design/Age_1_Alanthor.md § The four wall levels
        /// </summary>
        private static void AddWallLevelAction(List<ActionButton> into, Entity hub,
                                               EntityManager em, Faction faction)
        {
            // Alanthor's ladder only. A Runai player builds no walls at all
            // and a Feraldis one never leaves timber.
            if (CultureConfig.GetCompletedCulture(em, faction) != Cultures.Alanthor) return;

            var research = FactionResearchState.Instance;
            string techId = null;
            foreach (var id in WallLevelChain)
            {
                if (research != null && research.HasResearched(faction, id)) continue;
                // Already on its way: ApplyWallLock puts the progress cell and
                // the Cancel button where this one was. The runtime scan, not
                // IsTechQueued — that one only sees hosts with a ProductionState.
                if (TheWaningBorder.Core.Commands.CommandRouter.IsResearchQueued(
                        em, faction, id, out _, out _)) return;
                techId = id;
                break;
            }
            if (techId == null) return;                       // fully upgraded

            if (!TechCatalog.TryGetTechnology(techId, out var tech) || tech == null) return;

            var cost = tech.cost != null
                ? new Cost
                {
                    Supplies = tech.cost.Supplies, Iron = tech.cost.Iron,
                    Veilstone = tech.cost.Veilstone, Veilsteel = tech.cost.Veilsteel,
                }
                : default;

            into.Add(new ActionButton
            {
                Id = techId,                                  // ExecuteResearch routes on the id
                Label = tech.name ?? techId,
                Tooltip = BuildTooltip(tech.name ?? techId, tech.desc ?? tech.effect,
                                       cost, GetFactionResourcesAsCost(em, faction),
                                       trainingTime: tech.researchTime),
                Cost = cost,
                Enabled = true,
                CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                Icon = null,
            });
        }

        /// <summary>The wall actions the lock greys out: everything that
        /// changes or extends a standing wall piece. Gate open/close,
        /// ungarrison and Replace Equipment stay live — they change no wall.</summary>
        private static readonly HashSet<string> WallLockedActionIds = new HashSet<string>
        {
            "BuildWall", "WallSegmentToGate", "WallInstanceToTower",
            "WallToBallista", "WallToTrebuchet", "WallInstanceToHub",
        };

        /// <summary>
        /// THE WALL LOCK, as the panel shows it (docs/Design/Age_1_Alanthor.md
        /// § The four wall levels). From the moment a wall level is queued
        /// anywhere for <paramref name="faction"/> until it completes or is
        /// cancelled: every wall-changing action is greyed with the reason,
        /// a progress cell stands where the level button was, a Cancel cell
        /// refunds it, and the panel's progress bar follows the research on
        /// whichever hub is running it. The executors refuse the same actions
        /// on the same test (CommandRouter.WallsLockedForUpgrade), so the grey
        /// is a courtesy, not the lock. Nothing is stored: when the research
        /// ends or is cancelled the next refresh simply stops applying it.
        /// </summary>
        private static void ApplyWallLock(ref EntityActionInfo info, Entity entity,
                                          EntityManager em, Faction faction)
        {
            if (!TheWaningBorder.Entities.WallTiers.TryGetLevelResearch(
                    em, faction, out var host, out int slot, out var techId))
                return;

            string lockTip = Loc.T("Walls are being upgraded. Wall actions return when the upgrade finishes or is cancelled.");
            if (info.Actions == null) info.Actions = new List<ActionButton>();
            for (int i = 0; i < info.Actions.Count; i++)
            {
                var b = info.Actions[i];
                if (b.Id == null || !WallLockedActionIds.Contains(b.Id)) continue;
                b.Enabled = false;
                b.Tooltip = b.Label + "\n" + lockTip;
                info.Actions[i] = b;
            }

            var progress = GetProductionInfo(host, em);
            bool running = slot == 0 && progress.IsBusy;
            int pct = running ? (int)System.Math.Round(progress.Progress * 100f) : 0;
            string techName = TechCatalog.TryGetTechnology(techId, out var tech) && tech != null
                ? Loc.T(tech.name ?? techId) : techId;

            info.Actions.Add(new ActionButton
            {
                Id = "WallLevelResearching",
                Label = string.Format(Loc.T("Upgrading: {0} ({1}%)"), techName, pct),
                Tooltip = lockTip,
                Enabled = false,
                CanAfford = true,
                Icon = null,
            });

            if (faction == GameSettings.LocalPlayerFaction && !GameSettings.IsObserver)
                info.Actions.Add(new ActionButton
                {
                    Id = "CancelWallLevel",
                    Label = Loc.T("Cancel Upgrade"),
                    Tooltip = Loc.T("Stop the wall upgrade and refund its cost. Wall actions unlock again."),
                    Enabled = true,
                    CanAfford = true,
                    Icon = null,
                });

            // The bar follows the research wherever it runs. Only the host
            // itself shows its queue slots: the slot strip cancels on the
            // SELECTED entity, which on any other wall piece is the wrong one.
            if (entity != host) progress.Entries = System.Array.Empty<ProductionQueueEntry>();
            info.ProductionState = progress;
        }

        /// <summary>The bought half of the wall ladder, in order.</summary>
        private static readonly string[] WallLevelChain =
        {
            TheWaningBorder.Entities.WallTiers.BattlementsTechId,
            TheWaningBorder.Entities.WallTiers.ShieldedTechId,
        };

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
                Veilsteel = res.Veilsteel
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
