// AIAlanthorEndgameSystem.cs
// Culture-specific endgame AI for Alanthor factions. Picks up after the
// SimpleAISystem build order finishes (Age 2+) and drives the late-game
// behaviour Alanthor should ship with: defensive tower clusters, sect
// adoption (Fortitude / Renewal cluster) AND active-power firing,
// veilsteel production via the Smelter (build + miner assignment),
// armoured unit production from the Stable / SiegeYard, worker flee
// from threats, and on-age-up strategy transition to Defensive.
//
// Scope: SELF-SUFFICIENT. The legacy AIBuildingManager / AIEconomyManager /
// AIMilitaryManager are all [DisableAutoCreation] (replaced by
// SimpleAISystem) and their BuildRequest / RecruitmentRequest buffers
// are dead code. So this system bypasses those buffers entirely and
// drives Era-2+ Alanthor behaviour with direct ECS calls — same pattern
// SimpleAISystem uses for Age-1 buildings (CommandRouter.PlaceBuildingDirect
// + DispatchBuildersTo, FactionEconomy.Spend, push directly into
// TrainQueueItem).
//
// Tick rate: 5 seconds (slow loop — strategic decisions, not micro).
//
// Phases (each tick):
//   1. HasAgedUp latch — first frame era >= 2 is observed on the Hall.
//      Also flips AIStrategyState.Current to Defensive after enough
//      armies have been lost since the last strategy switch (preserves
//      previous as Previous so future evaluators can diff).
//   2. Sect adoption — when a Temple of Ridan exists and RP / supplies /
//      crystal can afford a chapel, queue an adoption via SectAdoption.
//      Picks Alanthor-cluster sects in priority order (Fortitude first).
//   3. Sect active-power firing — for every adopted sect that has a
//      level-1+ Active-Power lever and is off cooldown, fire it at the
//      most useful target: offensive (Smite / Burning / Pyre) at enemy
//      clusters in/near our base; support (Heal / Armor / Damage / Speed)
//      on our own armies in combat; reveal at the last-known enemy
//      position.
//   4. Smelter construction — if no Alanthor_Smelter exists, build one
//      directly (FactionEconomy.Spend + CommandRouter.PlaceBuildingDirect
//      + DispatchBuildersTo).
//   5. Smelter miner assignment — port of AIEconomyManager.ManageSmelters.
//      Idle miners get a ForgeSupplyOrder pointing at our smelter so they
//      shuttle iron/crystal in and feed the veilsteel conversion.
//   6. Defensive tower spam — late-game (>5 min) build extra Alanthor_Towers
//      around the Hall up to a cap. Direct creation (was queueing into
//      the dead BuildRequest buffer; never actually built anything).
//   7. Armoured-unit production — when an Alanthor_Stable / Alanthor_SiegeYard
//      exists and its TrainQueue has room, push Cataphract / Ballista
//      directly into the queue (charges cost via FactionEconomy.Spend).
//   8. Worker flee — for every miner / builder of this faction with an
//      enemy unit within FleeRadius, issue a MoveCommand toward the
//      nearest own Hall. Cooldowned per-worker so we don't spam orders.
//
// Walls: deferred. Closing a wall ring around the base — required for
// the WallEnclosureIncomeSystem bonus — needs a perimeter pathfinder
// and per-segment WallHubLink wiring that is out of scope for this PR.
// Sect aura/passive effects are applied automatically by their dedicated
// systems once a chapel is adopted; nothing for the AI to do there.
//
// Location: Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        // Tick interval — slow strategic loop.
        private const float ThinkInterval = 5f;

        // Late-game tower cap — beyond this, the AI stops spamming towers.
        private const int LateGameTowerCap = 4;

        // When elapsed gameTime exceeds this, late-game behaviours kick in.
        private const float LateGameStart = 300f; // 5 min

        // Material cost the AI keeps in reserve before queuing chapel
        // adoption (so adoption doesn't bankrupt the economy).
        private const int ChapelReserveSupplies = 100;
        private const int ChapelReserveCrystal  = 40;

        // Smelter targets — same cadence as the parked AIEconomyManager so
        // tuning carries over. Two miners is enough to keep ForgeStorage fed
        // without starving the iron / crystal lines.
        private const int SmelterTargetMiners = 2;

        // Train-queue cap per Stable / SiegeYard. Mirrors SimpleAISystem.
        private const int MaxTrainQueue = 5;

        // Worker flee tuning. Miners and builders run home if any enemy
        // unit is within FleeRadius. Throttled per worker so we don't
        // spam MoveCommands every tick once a threat is committed.
        private const float FleeRadius = 14f;

        // Strategy switch threshold: number of armies lost without dealing
        // significant damage since the last strategy switch before we flip
        // to Defensive. Cheap signal — armies-lost is bumped by combat
        // bookkeeping elsewhere; we just react to it.
        private const int LossesBeforeDefensiveFlip = 2;

        // Alanthor-cluster sect priority (best-first). Fortitude buffs walls
        // and towers — fits defensive theme. Renewal auto-repairs buildings.
        // Antiquity gives per-class kill bonuses (works well with mixed army).
        // Reclamation is the last pick; it shines vs Crystal-Curse PvE which
        // the AI doesn't deeply engage.
        private static readonly string[] AlanthorSectPriority =
        {
            SectConfig.Fortitude,
            SectConfig.Renewal,
            SectConfig.Antiquity,
            SectConfig.Reclamation,
        };

        // All 12 sects — for the Active-Power firing pass (sect adoption is
        // not strictly Alanthor-cluster: a faction may adopt a non-cluster
        // sect too, and once adopted its Active Power should still fire).
        private static readonly string[] AllSects =
        {
            SectConfig.Antiquity, SectConfig.Renewal,    SectConfig.Fortitude,
            SectConfig.Reclamation, SectConfig.Silence,  SectConfig.Justice,
            SectConfig.Veneration, SectConfig.Witness,   SectConfig.War,
            SectConfig.Ash,        SectConfig.Ruin,      SectConfig.Wrath,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            // Snapshot brain entities first — we make structural changes
            // (adding ForgeSupplyOrder to miners, creating buildings) that
            // would invalidate a SystemAPI.Query iteration.
            var brainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AIBrain>());
            using var brainEntities = brainQuery.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int b = 0; b < brainEntities.Length; b++)
            {
                var entity = brainEntities[b];
                if (!em.Exists(entity)) continue;
                var brain = em.GetComponentData<AIBrain>(entity);
                if (brain.IsActive == 0) continue;

                Faction faction = brain.Owner;

                // Throttle: 5 s tick. Per-brain tick state, lazy-stamped on first sight.
                if (em.HasComponent<AIAlanthorTickState>(entity))
                {
                    var tick = em.GetComponentData<AIAlanthorTickState>(entity);
                    if (time < tick.NextThinkTime) continue;
                    tick.NextThinkTime = time + ThinkInterval;
                    em.SetComponentData(entity, tick);
                }
                else
                {
                    em.AddComponentData(entity, new AIAlanthorTickState
                    {
                        NextThinkTime = time + ThinkInterval,
                    });
                    continue; // skip first tick after stamp
                }

                // Find this faction's Hall and read culture/era.
                bool hasHall = false;
                byte culture = Cultures.None;
                int era = 1;
                float3 hallPos = float3.zero;
                Entity hallEntity = Entity.Null;
                {
                    var hallQuery = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HallTag>(),
                        ComponentType.ReadOnly<FactionTag>(),
                        ComponentType.ReadOnly<FactionProgress>(),
                        ComponentType.ReadOnly<LocalTransform>());
                    using var hallEnts = hallQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < hallEnts.Length; i++)
                    {
                        if (em.GetComponentData<FactionTag>(hallEnts[i]).Value != faction) continue;
                        culture  = em.GetComponentData<FactionProgress>(hallEnts[i]).Culture;
                        hallPos  = em.GetComponentData<LocalTransform>(hallEnts[i]).Position;
                        hallEntity = hallEnts[i];
                        hasHall  = true;
                        break;
                    }
                }
                if (!hasHall) continue;
                if (FactionEconomy.TryGetBank(em, faction, out var bank)
                    && em.HasComponent<FactionEra>(bank))
                    era = em.GetComponentData<FactionEra>(bank).Value;

                if (culture != Cultures.Alanthor) continue;
                if (era < 2) continue;

                // ─── 1. HasAgedUp latch + opportunistic strategy flip ─
                if (em.HasComponent<AIStrategyState>(entity))
                {
                    var ss = em.GetComponentData<AIStrategyState>(entity);
                    bool ssDirty = false;
                    if (ss.HasAgedUp == 0)
                    {
                        ss.HasAgedUp = 1;
                        ssDirty = true;
                        AILogger.Log(faction, "STRATEGY",
                            "Alanthor: aged up to era 2+ — endgame system engaged");
                    }
                    // Flip to Defensive if too many armies lost since the last
                    // switch. Cheap signal that doesn't require a full
                    // AIStrategyEvaluator (also [DisableAutoCreation]).
                    if (ss.Current != AIStrategy.Defensive
                        && ss.ArmiesLostSinceSwitch >= LossesBeforeDefensiveFlip)
                    {
                        ss.Previous = ss.Current;
                        ss.Current  = AIStrategy.Defensive;
                        ss.ArmiesLostSinceSwitch = 0;
                        ss.StrategyStartTime = time;
                        ssDirty = true;
                        AILogger.Log(faction, "STRATEGY",
                            $"Alanthor: switching to Defensive after {LossesBeforeDefensiveFlip}+ losses");
                    }
                    if (ssDirty) em.SetComponentData(entity, ss);
                }

                // ─── 2. Sect adoption ─────────────────────────────────
                TryAdoptNextSect(faction, em);

                // ─── 3. Sect active-power firing ──────────────────────
                TryFireSectPowers(faction, em, hallPos);

                // ─── 4. Smelter construction (one-shot) ───────────────
                TryBuildSmelter(faction, em, hallPos);

                // ─── 5. Smelter miner assignment ──────────────────────
                ManageSmelterMiners(faction, em, ecb);

                // ─── 6. Late-game defensive towers ────────────────────
                if (time > LateGameStart)
                    TryBuildDefensiveTower(faction, em, hallPos);

                // ─── 7. Armoured-unit production ──────────────────────
                TryQueueArmouredUnits(faction, em);

                // ─── 8. Worker flee ───────────────────────────────────
                HandleWorkerFlee(faction, em, hallPos, time);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────
        // 2. SECT ADOPTION
        // ──────────────────────────────────────────────────────────────────

        private static void TryAdoptNextSect(Faction faction, EntityManager em)
        {
            // Need a Temple of Ridan to host chapels.
            Entity temple = FindFactionBuilding<TempleOfRidanTag>(em, faction);
            if (temple == Entity.Null) return;

            for (int i = 0; i < AlanthorSectPriority.Length; i++)
            {
                string sectId = AlanthorSectPriority[i];
                if (SectQuery.IsAdopted(em, faction, sectId)) continue;

                if (!BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)) continue;

                if (FactionEconomy.TryGetResources(em, faction, out var res))
                {
                    if (res.Supplies < chapelCost.Supplies + ChapelReserveSupplies) return;
                    if (res.Crystal  < chapelCost.Crystal  + ChapelReserveCrystal)  return;
                }
                else return;

                var result = SectAdoption.TryStartAdoption(em, faction, sectId, chapelCost, temple);
                if (result == SectAdoptionResult.Ok)
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: adopting sect {sectId.Substring(5)}");
                    if (em.HasBuffer<TempleChapelSlot>(temple))
                    {
                        var slots = em.GetBuffer<TempleChapelSlot>(temple);
                        for (int s = 0; s < slots.Length; s++)
                        {
                            if (slots[s].State != 0) continue;
                            slots[s] = new TempleChapelSlot
                            {
                                Chapel        = Entity.Null,
                                SectId        = new FixedString64Bytes(sectId),
                                State         = 1,
                                BuildProgress = 0f,
                                BuildTime     = 30f,
                            };
                            break;
                        }
                    }
                    return; // one adoption per tick
                }
                if (result == SectAdoptionResult.NotEnoughRP) return; // wait for RP
                // For other failure modes (slot full, already adopted), try next priority.
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // 3. SECT ACTIVE-POWER FIRING
        // ──────────────────────────────────────────────────────────────────

        // Fire every adopted sect's Active Power that has a level-1+ lever
        // and an off-cooldown timer. Targeting depends on the power's
        // intent: offensive at enemy clusters near our base, support on
        // our own units (preferring those in combat), reveal at the
        // last-known enemy position.
        private static void TryFireSectPowers(Faction faction, EntityManager em, float3 hallPos)
        {
            for (int i = 0; i < AllSects.Length; i++)
            {
                string sectId = AllSects[i];
                if (!SectActivePowerHelper.CanFire(em, faction, sectId)) continue;

                var spec = SectLeverEffects.ActiveOf(sectId);
                float3 target;
                bool haveTarget;
                switch (spec.Kind)
                {
                    case SectActivePowerKind.SmiteCircle:
                    case SectActivePowerKind.BurningCircle:
                    case SectActivePowerKind.SpawnPyre:
                        haveTarget = TryPickEnemyClusterNearBase(em, faction, hallPos, spec.Radius, out target);
                        break;
                    case SectActivePowerKind.HealCircle:
                    case SectActivePowerKind.ArmorCircle:
                    case SectActivePowerKind.DamageCircle:
                    case SectActivePowerKind.SpeedCircle:
                        haveTarget = TryPickFriendlyArmy(em, faction, hallPos, spec.Radius, out target);
                        break;
                    case SectActivePowerKind.RevealCircle:
                        haveTarget = TryPickRevealTarget(em, faction, hallPos, out target);
                        break;
                    default:
                        continue;
                }
                if (!haveTarget) continue;

                if (SectActivePowerHelper.Fire(em, faction, sectId, target))
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: fired {sectId.Substring(5)} active power");
                }
            }
        }

        // Densest enemy cluster within ~80 m of the Hall. Returns the
        // grid cell with the most enemy units (4 m bucket size). Avoids
        // wasting a 60-100 cooldown power on a single straggler.
        private static bool TryPickEnemyClusterNearBase(
            EntityManager em, Faction faction, float3 hallPos, float castRadius,
            out float3 target)
        {
            target = default;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            const float scanRadius = 80f;
            float scanRadiusSq = scanRadius * scanRadius;

            // Snapshot enemy positions within scan radius.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value == faction) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                if (dx * dx + dz * dz > scanRadiusSq) continue;
                enemyPositions.Add(p);
            }

            if (enemyPositions.Length == 0) { enemyPositions.Dispose(); return false; }

            // Pick the densest cluster: for each candidate enemy, count
            // how many other enemies are within castRadius; pick the
            // enemy with the highest count. Ties broken by the first one
            // encountered. O(N²) but N is bounded by units within 80 m.
            float castRadiusSq = castRadius * castRadius;
            int bestCount = 0;
            int bestIdx = -1;
            for (int i = 0; i < enemyPositions.Length; i++)
            {
                int count = 0;
                for (int j = 0; j < enemyPositions.Length; j++)
                {
                    float dx = enemyPositions[j].x - enemyPositions[i].x;
                    float dz = enemyPositions[j].z - enemyPositions[i].z;
                    if (dx * dx + dz * dz <= castRadiusSq) count++;
                }
                if (count > bestCount) { bestCount = count; bestIdx = i; }
            }

            // Need at least 3 units in the cluster to justify a 60-150s cd power.
            if (bestCount < 3) { enemyPositions.Dispose(); return false; }
            target = enemyPositions[bestIdx];
            enemyPositions.Dispose();
            return true;
        }

        // Pick the centroid of our largest army group within ~120 m of the
        // Hall. Bias toward groups that are currently taking damage so the
        // heal/buff actually matters.
        private static bool TryPickFriendlyArmy(
            EntityManager em, Faction faction, float3 hallPos, float castRadius,
            out float3 target)
        {
            target = default;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            const float scanRadius = 120f;
            float scanRadiusSq = scanRadius * scanRadius;

            var positions = new NativeList<float3>(Allocator.Temp);
            var damaged   = new NativeList<bool>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                if (dx * dx + dz * dz > scanRadiusSq) continue;
                positions.Add(p);
                damaged.Add(hp.Value < hp.Max);
            }

            if (positions.Length == 0) { positions.Dispose(); damaged.Dispose(); return false; }

            // Score each unit by (cluster size in castRadius) + (2× damaged
            // friends in radius), so heals/buffs land where they help most.
            float castRadiusSq = castRadius * castRadius;
            float bestScore = 0f;
            int bestIdx = -1;
            for (int i = 0; i < positions.Length; i++)
            {
                float score = 0f;
                for (int j = 0; j < positions.Length; j++)
                {
                    float dx = positions[j].x - positions[i].x;
                    float dz = positions[j].z - positions[i].z;
                    if (dx * dx + dz * dz > castRadiusSq) continue;
                    score += 1f + (damaged[j] ? 2f : 0f);
                }
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            // Need at least 3 units (or 2 wounded) to justify the cooldown.
            bool worthwhile = bestScore >= 3f;
            float3 best = bestIdx >= 0 ? positions[bestIdx] : default;
            positions.Dispose();
            damaged.Dispose();
            if (!worthwhile) return false;
            target = best;
            return true;
        }

        // Reveal goes to the AISharedKnowledge.EnemyLastKnownPosition if
        // recent; otherwise we skip rather than blow the cooldown blind.
        private static bool TryPickRevealTarget(EntityManager em, Faction faction,
            float3 hallPos, out float3 target)
        {
            target = default;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<AISharedKnowledge>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<AIBrain>(ents[i]).Owner != faction) continue;
                var sk = em.GetComponentData<AISharedKnowledge>(ents[i]);
                // Only fire reveal if we've actually seen something — saves the
                // cooldown vs spraying it on the Hall position.
                if (sk.EnemyLastSeenTime <= 0) return false;
                target = sk.EnemyLastKnownPosition;
                return true;
            }
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // 4. SMELTER CONSTRUCTION (one-shot)
        // ──────────────────────────────────────────────────────────────────

        private static void TryBuildSmelter(Faction faction, EntityManager em, float3 hallPos)
        {
            const string smelterId = "Alanthor_Smelter";
            int existing = CountFactionBuildings(em, faction, smelterId);
            if (existing > 0) return; // already have one (built or under construction)

            if (!BuildCosts.TryGet(smelterId, out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;

            // Pre-flight: need an idle builder. Don't spend cost on a foundation
            // nobody will work on.
            if (CountIdleBuilders(em, faction) == 0) return;

            int2 smelterSize = BuildingSizeConfig.GetSize(smelterId);
            if (!TryFindBuildPositionRing(em, hallPos, smelterSize, 18f, 28f, out float3 pos)) return;

            if (!FactionEconomy.Spend(em, faction, cost)) return;

            Entity building = CommandRouter.PlaceBuildingDirect(em, smelterId, pos, faction);
            if (building == Entity.Null) { FactionEconomy.Add(em, faction, cost); return; }

            int dispatched = DispatchBuildersTo(em, faction, building, smelterId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return;
            }
            AILogger.Log(faction, "BUILDING", "Alanthor: queued Smelter construction (veilsteel pipeline)");
        }

        // ──────────────────────────────────────────────────────────────────
        // 5. SMELTER MINER ASSIGNMENT (port of AIEconomyManager.ManageSmelters)
        // ──────────────────────────────────────────────────────────────────

        private static void ManageSmelterMiners(Faction faction, EntityManager em, EntityCommandBuffer ecb)
        {
            // Find a completed smelter for this faction.
            Entity smelter = Entity.Null;
            {
                var smelterQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<SmelterTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<ForgeStorage>());
                using var sEnts = smelterQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < sEnts.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(sEnts[i]).Value != faction) continue;
                    if (em.HasComponent<UnderConstruction>(sEnts[i])) continue;
                    smelter = sEnts[i];
                    break;
                }
            }
            if (smelter == Entity.Null) return;

            // Count miners already supplying it.
            int assigned = 0;
            {
                var supQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<MinerTag>(),
                    ComponentType.ReadOnly<ForgeSupplyOrder>(),
                    ComponentType.ReadOnly<FactionTag>());
                using var sEnts = supQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < sEnts.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(sEnts[i]).Value == faction) assigned++;
                }
            }
            int needed = SmelterTargetMiners - assigned;
            if (needed <= 0) return;

            // Find idle miners not yet assigned to the smelter or to a build site.
            var idle = new NativeList<Entity>(Allocator.Temp);
            {
                var minerQuery = em.CreateEntityQuery(
                    new EntityQueryDesc
                    {
                        All = new[] {
                            ComponentType.ReadOnly<MinerTag>(),
                            ComponentType.ReadOnly<MinerState>(),
                            ComponentType.ReadOnly<FactionTag>()
                        },
                        None = new[] {
                            ComponentType.ReadOnly<ForgeSupplyOrder>(),
                            ComponentType.ReadOnly<BuildOrder>()
                        }
                    });
                using var mEnts = minerQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < mEnts.Length && idle.Length < needed; i++)
                {
                    if (em.GetComponentData<FactionTag>(mEnts[i]).Value != faction) continue;
                    if (em.GetComponentData<MinerState>(mEnts[i]).State != MinerWorkState.Idle) continue;
                    idle.Add(mEnts[i]);
                }
            }

            for (int i = 0; i < idle.Length; i++)
            {
                Entity miner = idle[i];
                // Reset miner state — pure data write, no archetype change.
                var ms = em.GetComponentData<MinerState>(miner);
                ms.State = MinerWorkState.Idle;
                ms.AssignedDeposit = Entity.Null;
                ms.DropoffTarget   = Entity.Null;
                em.SetComponentData(miner, ms);

                // Add ForgeSupplyOrder via ECB — adding the component is a
                // structural change and must not run inline against EM while
                // we're still iterating brains in the outer loop.
                ecb.AddComponent(miner, new ForgeSupplyOrder
                {
                    Forge        = smelter,
                    ResourceType = 0,
                    Phase        = 0,
                });
            }

            if (idle.Length > 0)
            {
                AILogger.Log(faction, "ECONOMY",
                    $"Alanthor: assigned {idle.Length} idle miners to Smelter ({assigned + idle.Length}/{SmelterTargetMiners})");
            }
            idle.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────
        // 6. DEFENSIVE TOWER SPAM (direct creation)
        // ──────────────────────────────────────────────────────────────────

        private static void TryBuildDefensiveTower(Faction faction, EntityManager em, float3 hallPos)
        {
            const string towerId = "Alanthor_Tower";
            int existing = CountFactionBuildings(em, faction, towerId);
            if (existing >= LateGameTowerCap) return;

            if (!BuildCosts.TryGet(towerId, out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (CountIdleBuilders(em, faction) == 0) return;

            int2 towerSize = BuildingSizeConfig.GetSize(towerId);
            if (!TryFindBuildPositionRing(em, hallPos, towerSize, 25f, 35f, out float3 pos)) return;
            if (!FactionEconomy.Spend(em, faction, cost)) return;

            Entity building = CommandRouter.PlaceBuildingDirect(em, towerId, pos, faction);
            if (building == Entity.Null) { FactionEconomy.Add(em, faction, cost); return; }

            int dispatched = DispatchBuildersTo(em, faction, building, towerId, pos, maxBuilders: 1);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return;
            }
            AILogger.Log(faction, "BUILDING",
                $"Alanthor late-game: building defensive tower {existing + 1}/{LateGameTowerCap}");
        }

        // ──────────────────────────────────────────────────────────────────
        // 7. ARMOURED-UNIT PRODUCTION
        // ──────────────────────────────────────────────────────────────────

        // Push Cataphract / Ballista directly into the Stable / SiegeYard
        // TrainQueue. Same pattern SimpleAISystem uses for Age-1 units.
        // Charges cost via FactionEconomy.Spend so we don't double-deduct.
        private static void TryQueueArmouredUnits(Faction faction, EntityManager em)
        {
            TryQueueAt<RoyalStableTag>(em, faction, "Alanthor_Cataphract");
            TryQueueAt<SiegeYardTag>  (em, faction, "Alanthor_Ballista");
        }

        private static void TryQueueAt<TBuildingTag>(EntityManager em, Faction faction, string unitId)
            where TBuildingTag : unmanaged, IComponentData
        {
            Entity trainer = FindFactionBuilding<TBuildingTag>(em, faction);
            if (trainer == Entity.Null) return;
            if (em.HasComponent<UnderConstruction>(trainer)) return;
            if (!em.HasBuffer<TrainQueueItem>(trainer)) return;
            var queue = em.GetBuffer<TrainQueueItem>(trainer);
            if (queue.Length >= MaxTrainQueue) return;

            if (TechTreeDB.Instance == null) return;
            if (!TechTreeDB.Instance.TryGetUnit(unitId, out var def) || def == null) return;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (!FactionEconomy.Spend(em, faction, cost)) return;

            queue.Add(new TrainQueueItem { UnitId = new FixedString64Bytes(unitId) });
            AILogger.Log(faction, "MILITARY", $"Alanthor: queued {unitId}");
        }

        // ──────────────────────────────────────────────────────────────────
        // 8. WORKER FLEE
        // ──────────────────────────────────────────────────────────────────

        // For every miner / builder of this faction, scan for an enemy unit
        // within FleeRadius and — if found — issue a MoveCommand toward
        // the Hall. Throttled per-worker via FleeCooldownState so we don't
        // override a fresh order on the same tick.
        private static void HandleWorkerFlee(Faction faction, EntityManager em,
            float3 hallPos, float time)
        {
            // Collect enemy unit positions once per tick.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            {
                var enemyQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<Health>());
                using var eEnts = enemyQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < eEnts.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(eEnts[i]).Value == faction) continue;
                    if (em.GetComponentData<Health>(eEnts[i]).Value <= 0) continue;
                    enemyPositions.Add(em.GetComponentData<LocalTransform>(eEnts[i]).Position);
                }
            }
            if (enemyPositions.Length == 0) { enemyPositions.Dispose(); return; }

            float fleeRadiusSq = FleeRadius * FleeRadius;

            // Process miners.
            FleeWorkers<MinerTag>(em, faction, enemyPositions, hallPos, fleeRadiusSq, time);
            // Process builders (CanBuild marker is what SimpleAISystem queries).
            FleeWorkers<CanBuild>(em, faction, enemyPositions, hallPos, fleeRadiusSq, time);

            enemyPositions.Dispose();
        }

        private static void FleeWorkers<TWorkerTag>(EntityManager em, Faction faction,
            NativeList<float3> enemyPositions, float3 hallPos, float fleeRadiusSq, float time)
            where TWorkerTag : unmanaged, IComponentData
        {
            var workerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<TWorkerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var wEnts = workerQuery.ToEntityArray(Allocator.Temp);

            for (int w = 0; w < wEnts.Length; w++)
            {
                var worker = wEnts[w];
                if (em.GetComponentData<FactionTag>(worker).Value != faction) continue;
                float3 wPos = em.GetComponentData<LocalTransform>(worker).Position;

                // Closest enemy in flee radius?
                bool threatNearby = false;
                for (int e = 0; e < enemyPositions.Length; e++)
                {
                    float dx = enemyPositions[e].x - wPos.x;
                    float dz = enemyPositions[e].z - wPos.z;
                    if (dx * dx + dz * dz <= fleeRadiusSq) { threatNearby = true; break; }
                }
                if (!threatNearby) continue;

                // Cooldown: don't re-issue inside FleeReissueInterval seconds.
                const float FleeReissueInterval = 4f;
                if (em.HasComponent<AIWorkerFleeState>(worker))
                {
                    var fs = em.GetComponentData<AIWorkerFleeState>(worker);
                    if (time < fs.NextRetryTime) continue;
                    fs.NextRetryTime = time + FleeReissueInterval;
                    em.SetComponentData(worker, fs);
                }
                else
                {
                    em.AddComponentData(worker, new AIWorkerFleeState
                    {
                        NextRetryTime = time + FleeReissueInterval,
                    });
                }

                // Drop any active gather/build order so the move sticks.
                if (em.HasComponent<MinerState>(worker))
                {
                    var ms = em.GetComponentData<MinerState>(worker);
                    ms.State            = MinerWorkState.Idle;
                    ms.AssignedDeposit  = Entity.Null;
                    em.SetComponentData(worker, ms);
                }
                if (em.HasComponent<BuildOrder>(worker))
                    em.RemoveComponent<BuildOrder>(worker);

                // Move toward Hall, biased a couple metres past so the
                // worker doesn't stop right at the threat boundary.
                float3 to = hallPos;
                float3 away = to - wPos;
                float len = math.length(new float2(away.x, away.z));
                if (len > 0.01f)
                {
                    away = math.normalize(new float3(away.x, 0f, away.z));
                    to = wPos + away * (len + 4f);
                }
                MoveCommandHelper.Execute(em, worker, to);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // BUILDER / PLACEMENT HELPERS (mirrors SimpleAISystem private helpers)
        // ──────────────────────────────────────────────────────────────────

        private static int CountIdleBuilders(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<BuildOrder>(ents[i])) continue;
                count++;
            }
            return count;
        }

        private static int DispatchBuildersTo(EntityManager em, Faction faction,
            Entity site, string buildingId, float3 sitePos, int maxBuilders)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            // Sort by distance ascending — pick the nearest few.
            var candidates = new NativeList<Candidate>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<BuildOrder>(ents[i])) continue;
                float3 p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - sitePos.x, dz = p.z - sitePos.z;
                candidates.Add(new Candidate { Entity = ents[i], DistSq = dx * dx + dz * dz });
            }

            // Insertion sort — list is short, no need for a comparer setup.
            for (int i = 1; i < candidates.Length; i++)
            {
                var key = candidates[i];
                int j = i - 1;
                while (j >= 0 && candidates[j].DistSq > key.DistSq)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }
                candidates[j + 1] = key;
            }

            int dispatched = 0;
            for (int i = 0; i < candidates.Length && dispatched < maxBuilders; i++)
            {
                CommandRouter.IssueBuild(em, candidates[i].Entity, site, buildingId, sitePos);
                dispatched++;
            }
            candidates.Dispose();
            return dispatched;
        }

        private struct Candidate
        {
            public Entity Entity;
            public float DistSq;
        }

        // Simple ring-scan placement: try angles around the anchor at radii
        // within [rmin, rmax]. Returns the first candidate that
        // BuildCommandHelper.IsValidBuildPosition accepts. Used for endgame
        // buildings (Smelter, towers) where SimpleAISystem's GH-spacing
        // and sand-spacing rules don't matter.
        private static bool TryFindBuildPositionRing(EntityManager em, float3 anchor,
            int2 buildingSize, float rmin, float rmax, out float3 pos)
        {
            const int angleSamples = 24;
            // Anchor-derived seed: stable per-Hall but varies between calls
            // because rmin/rmax produce different hashes for tower vs smelter.
            uint seed = math.hash(new float3(anchor.x, rmin, rmax));
            if (seed == 0) seed = 1u;
            var rng = new Unity.Mathematics.Random(seed);
            for (float r = rmin; r <= rmax; r += 4f)
            {
                int start = rng.NextInt(0, angleSamples);
                for (int i = 0; i < angleSamples; i++)
                {
                    int idx = (start + i) % angleSamples;
                    float angle = (idx / (float)angleSamples) * math.PI * 2f;
                    float3 candidate = new float3(
                        anchor.x + math.cos(angle) * r,
                        0f,
                        anchor.z + math.sin(angle) * r);
                    candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                    if (BuildCommandHelper.IsValidBuildPosition(em, candidate, buildingSize))
                    {
                        pos = candidate;
                        return true;
                    }
                }
            }
            pos = default;
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // GENERIC HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static int CountFactionBuildings(EntityManager em, Faction faction, string buildingId)
        {
            int pid = BuildingFactory.GetPresentationId(buildingId);
            int count = 0;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.GetComponentData<PresentationId>(ents[i]).Id != pid) continue;
                count++;
            }
            return count;
        }

        private static Entity FindFactionBuilding<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value == faction) return ents[i];
            }
            return Entity.Null;
        }

        private static Cost ToCost(CostBlock block)
        {
            if (block == null) return default;
            return new Cost
            {
                Supplies  = block.Supplies,
                Iron      = block.Iron,
                Crystal   = block.Crystal,
                Veilsteel = block.Veilsteel,
                Glow      = block.Glow,
            };
        }
    }

    /// <summary>
    /// Per-AIBrain tick state for the Alanthor endgame loop. Lazy-stamped
    /// the first time AIAlanthorEndgameSystem inspects a brain.
    /// </summary>
    public struct AIAlanthorTickState : IComponentData
    {
        public float NextThinkTime;
    }

    /// <summary>
    /// Per-worker (miner / builder) flee throttle. Stamped by
    /// AIAlanthorEndgameSystem.HandleWorkerFlee on first detection of a
    /// nearby threat; prevents the system from re-issuing MoveCommand
    /// every tick while the worker is already running home.
    /// </summary>
    public struct AIWorkerFleeState : IComponentData
    {
        public float NextRetryTime;
    }
}
