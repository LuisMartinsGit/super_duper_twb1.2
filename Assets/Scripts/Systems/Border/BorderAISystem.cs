// File: Assets/Scripts/Systems/Border/BorderAISystem.cs
// Standalone AI brain for the Border faction.
// Manages building, spawning, harassment, and expansion per BorderMainNode.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.AI;
using Cost = TheWaningBorder.Core.Cost;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Standalone AI brain for the Border faction.
    /// Manages each BorderMainNode independently:
    /// - Builds sub-nodes in border areas
    /// - Spawns combat units from veilstone bank
    /// - Sends harassment waves at player bases
    /// - Expands by placing new main nodes
    ///
    /// Does NOT use AIBrain/manager architecture -- veilstone economy
    /// is fundamentally different (passive income, direct spawning).
    ///
    /// Uses SystemBase (not ISystem) because entity factory Create() methods
    /// perform structural changes (em.AddComponentData) that ISystem codegen blocks.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BorderAISystem : SystemBase
    {
        private float _decisionTimer;
        private int _decisionTick;  // Deterministic tick counter for multiplayer sync
        private const float DecisionInterval = 5.0f; // AI thinks every 5 seconds

        // Cached EntityQueries — initialized in OnCreate()
        private EntityQuery _mainNodeQuery;
        private EntityQuery _subNodeQuery;
        private EntityQuery _unitQuery;           // UnitTag + FactionTag + LocalTransform (intruder detection)
        private EntityQuery _crystalUnitQuery;    // BorderUnitTag + LocalTransform
        private EntityQuery _hallQuery;           // HallTag + FactionTag + LocalTransform
        private EntityQuery _mainNodeTransformQuery; // BorderMainNodeTag + LocalTransform (expansion)
        private EntityQuery _waveQuery;           // BorderWaveState (attack waves)
        private EntityQuery _subNodeTransformQuery;  // BorderSubNodeTag + LocalTransform (border position)

        // AI costs — centralised in BorderConstants
        private const int ResourceNodeCost = AIResourceNodeCost;
        private const int TurretNodeCost = AITurretNodeCost;
        private const int RestorationNodeCost = AIRestorationNodeCost;
        private const int EnforcementNodeCost = AIEnforcementNodeCost;
        private const int SuppressionNodeCost = AISuppressionNodeCost;
        private const int CrystallingCost = AICrystallingCost;
        private const int VeilstingerCost = AIVeilstingerCost;
        private const int GodsplinterCost = AIGodsplinterCost;
        private const int ExpansionCost = AIExpansionCost;

        // Expansion pacing — new border nodes start fast, slow logarithmically
        private const int MaxBorderNodes = 16;
        private const float BaseExpansionInterval = 30f;   // 30s at game start
        private const float ExpansionSlowdownRate = 20f;   // How fast it slows
        private const float MaxExpansionInterval = 300f;   // Never slower than 5 min

        // Hard caps on the border footprint.
        public const int MaxBorderUnits     = 100; // total live border units (Crystalling + Veilstinger + Godsplinter)
        public const int MaxBorderBuildings = 50;  // total border buildings (main + sub nodes)

        // Connectivity gate for spawn / placement. Reject candidates with fewer
        // than this many passable neighbours sampled around them — stops nodes
        // and units from being placed on a beach pocket between water and cliff.
        private const int MinPassableNeighbours = 6; // out of 8 sampled
        private const float ConnectivityProbeRadius = 10f;

        // Stranded-unit rescue: idle units more than this far from every main
        // node are teleported to the nearest one.
        private const float StrandedDistance = 60f;

        protected override void OnCreate()
        {
            // §2.5: the curse is a force, not a faction — its per-node brain
            // (sub-node building, unit training, harass waves, expansion) is
            // retired along with BorderArmyAISystem/BorderHordeSystem.
            Enabled = TheWaningBorder.Core.Config.BorderConstants.CurseFieldsArmies;
            RequireForUpdate<BorderMainNodeTag>();

            // WithNone<NodeDormant> so the AI stops driving training / expansion
            // ticks for nodes that have been destroyed by a Feraldis-style kill
            // and are sitting in the Destroyed state waiting for regrowth.
            _mainNodeQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<BorderAIState>(),
                    ComponentType.ReadOnly<BorderNode>(),
                    ComponentType.ReadOnly<BorderNodeLevel>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<BorderMainNodeTag>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<NodeDormant>(),
                },
            });

            _subNodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderSubNodeTag>(),
                ComponentType.ReadOnly<OwnerNode>()
            );

            _unitQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _crystalUnitQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderUnitTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _hallQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _mainNodeTransformQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _waveQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<BorderWaveState>()
            );

            _subNodeTransformQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderSubNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
        }

        protected override void OnUpdate()
        {
            _decisionTimer += World.Time.DeltaTime;
            if (_decisionTimer < DecisionInterval) return;
            _decisionTimer = 0f;

            var em = EntityManager;

            // Get veilstone bank balance
            if (!FactionEconomy.TryGetResources(em, Faction.Border, out var resources))
                return;

            int veilstoneBank = resources.Veilstone;

            // Deterministic random seed — in multiplayer, use lockstep tick (guaranteed
            // synchronized between host and client). In singleplayer, use local counter.
            if (GameSettings.IsMultiplayer && LockstepServiceLocator.IsActive)
                _decisionTick = LockstepServiceLocator.Instance.CurrentTick;
            else
                _decisionTick++;
            var random = new Unity.Mathematics.Random(
                (uint)(_decisionTick * 7919 + GameSettings.SpawnSeed + 1));

            // Collect query data into temp arrays to iterate safely
            using var entities = _mainNodeQuery.ToEntityArray(Allocator.Temp);
            using var aiStates = _mainNodeQuery.ToComponentDataArray<BorderAIState>(Allocator.Temp);
            using var crystalNodes = _mainNodeQuery.ToComponentDataArray<BorderNode>(Allocator.Temp);
            using var nodeLevels = _mainNodeQuery.ToComponentDataArray<BorderNodeLevel>(Allocator.Temp);
            using var transforms = _mainNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Time-based leveling (spread system is disabled)
            float elapsedMin = (float)(World.Time.ElapsedTime / 60.0);

            // Population caps: counted once per decision tick and shared across nodes.
            int crystalUnitCount = _crystalUnitQuery.CalculateEntityCount();
            int totalBuildings   = entities.Length + _subNodeQuery.CalculateEntityCount();

            for (int n = 0; n < entities.Length; n++)
            {
                var ai = aiStates[n];
                var nodePos = transforms[n].Position;
                float territoryRadius = crystalNodes[n].SpreadRadius; // Fixed territory radius

                // Read this node's state — Active and Converted both produce
                // units, but Converted spawns into the OwnerFaction (spec §5.4:
                // Runai-allied border). Cleansed nodes are inert; skip them.
                Faction spawnFaction = Faction.Border;
                if (em.HasComponent<BorderNodeState>(entities[n]))
                {
                    var nodeState = em.GetComponentData<BorderNodeState>(entities[n]);
                    if (nodeState.State == NodeState.Cleansed) continue;
                    if (nodeState.State == NodeState.Converted)
                        spawnFaction = nodeState.OwnerFaction;
                }

                // Refresh bank balance — Converted nodes still draw against
                // the Border bank for training cost. (Spec doesn't specify a
                // separate bank; using the existing one keeps the economy
                // single-pool for now.)
                if (FactionEconomy.TryGetResources(em, Faction.Border, out resources))
                    veilstoneBank = resources.Veilstone;

                // Drive AI phase from elapsed game time
                if (elapsedMin >= 15f) ai.Phase = 2;
                else if (elapsedMin >= 5f) ai.Phase = 1;
                else ai.Phase = 0;

                // Update node level to match phase for display
                em.SetComponentData(entities[n], new BorderNodeLevel { Value = ai.Phase + 1 });

                // === TRAINING TICK — MOVED to BorderArmyAISystem ===
                // Unit training is now owned by BorderArmyAISystem, which fields
                // two SO-tiered armies (defend + attack) per node out of the
                // node's PRIVATE veilstone bank (BorderNodeBank) and stamps each unit
                // with OwnerNode + BorderArmyRole. The old continuous one-unit
                // trickle (BorderTrainingState / TryQueueUnit) is retired so the
                // two systems don't both spawn into the same nodes.
                _ = crystalUnitCount; // (still computed for the build/expansion caps)

                // === BUILD PHASE (sub-buildings within territory) ===
                ai.BuildTimer -= DecisionInterval;
                if (ai.BuildTimer <= 0)
                {
                    if (totalBuildings < MaxBorderBuildings)
                    {
                        if (TryBuildSubNode(em, entities[n], ref ai, nodePos, territoryRadius,
                                ref random, ref veilstoneBank))
                            totalBuildings++;
                    }
                    ai.BuildTimer = 15f + random.NextFloat(0, 5f);
                }

                // === TERRITORIAL AGGRESSION ===
                // Border-unit movement & combat are now owned by BorderHordeSystem
                // (cohesive group advance + group-wide aggro + pathfinding march
                // to the player spawn). The old per-node intruder intercept set
                // bare DesiredDestination, which fought the horde controller, so
                // it's disabled here. Static defence still comes from turret nodes.
                // AttackIntruders(em, nodePos, territoryRadius);

                // === EXPANSION PHASE — REMOVED BY DESIGN ===
                // The Border CANNOT build new nodes. Nodes only enter the map
                // at bootstrap (scene markers). Sub-node construction within an
                // existing node's territory (TryBuildSubNode above) is
                // unaffected — those are buildings, not nodes.

                // Write back modified AI state
                em.SetComponentData(entities[n], ai);
            }

            // === BORDER-UNIT MOVEMENT & COMBAT: owned by BorderHordeSystem ===
            // The old wave / idle-pacing / stranded-rescue model is superseded
            // by BorderHordeSystem, which marches the whole horde cohesively to
            // the player spawn with real pathfinding and coordinates group-wide
            // attacks. Disabled here so the two don't fight over movement.
            // UpdateAttackWaves(em, entities, transforms, ref random);
        }

        /// <summary>
        /// Try to build a sub-node for this main. Returns true if a building was created
        /// (caller increments the global building cap counter).
        /// </summary>
        private bool TryBuildSubNode(EntityManager em, Entity mainNode,
            ref BorderAIState ai, float3 nodePos, float spreadRadius,
            ref Random random, ref int veilstoneBank)
        {
            // Count existing sub-nodes belonging to THIS main node
            int resourceCount = 0, turretCount = 0, restorationCount = 0;
            int enforcementCount = 0, suppressionCount = 0, totalCount = 0;

            using var subNodes = _subNodeQuery.ToComponentDataArray<BorderSubNodeTag>(Allocator.Temp);
            using var owners = _subNodeQuery.ToComponentDataArray<OwnerNode>(Allocator.Temp);
            for (int i = 0; i < subNodes.Length; i++)
            {
                if (owners[i].Value != mainNode) continue;
                totalCount++;
                switch (subNodes[i].Type)
                {
                    case BorderSubNodeType.Resource: resourceCount++; break;
                    case BorderSubNodeType.Turret: turretCount++; break;
                    case BorderSubNodeType.Restoration: restorationCount++; break;
                    case BorderSubNodeType.Enforcement: enforcementCount++; break;
                    case BorderSubNodeType.Suppression: suppressionCount++; break;
                }
            }

            // Hard cap: no more sub-nodes for this main node
            if (totalCount >= MaxSubNodesPerMain) return false;

            // Find a position within border area
            float3 buildPos = FindBorderPosition(nodePos, spreadRadius, ref random);
            if (buildPos.x == float.MinValue) return false;

            // Build decision tree (per-node limits)
            // Priority: Resource > Turret > Restoration > Enforcement > Suppression
            Entity created = Entity.Null;

            if (resourceCount < MaxResourceNodesPerMain && veilstoneBank >= ResourceNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.Border, Cost.Of(veilstone: ResourceNodeCost)))
                {
                    created = BorderResourceNode.Create(em, buildPos);
                    veilstoneBank -= ResourceNodeCost;
                }
            }
            else if (turretCount < MaxTurretNodesPerMain && veilstoneBank >= TurretNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.Border, Cost.Of(veilstone: TurretNodeCost)))
                {
                    created = BorderTurretNode.Create(em, buildPos);
                    veilstoneBank -= TurretNodeCost;
                }
            }
            else if (restorationCount < MaxRestorationNodesPerMain && veilstoneBank >= RestorationNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.Border, Cost.Of(veilstone: RestorationNodeCost)))
                {
                    created = BorderRestorationNode.Create(em, buildPos);
                    veilstoneBank -= RestorationNodeCost;
                }
            }
            else if (enforcementCount < MaxEnforcementNodesPerMain && veilstoneBank >= EnforcementNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.Border, Cost.Of(veilstone: EnforcementNodeCost)))
                {
                    created = BorderEnforcementNode.Create(em, buildPos);
                    veilstoneBank -= EnforcementNodeCost;
                }
            }
            else if (suppressionCount < MaxSuppressionNodesPerMain && veilstoneBank >= SuppressionNodeCost && ai.Phase >= 2)
            {
                if (FactionEconomy.Spend(em, Faction.Border, Cost.Of(veilstone: SuppressionNodeCost)))
                {
                    created = BorderSuppressionNode.Create(em, buildPos);
                    veilstoneBank -= SuppressionNodeCost;
                }
            }
            // No fallback — once all slots are filled, stop building

            // Tag the new sub-node with its parent main node
            if (created != Entity.Null)
            {
                if (em.HasComponent<OwnerNode>(created))
                    em.SetComponentData(created, new OwnerNode { Value = mainNode });
                    else
                        em.AddComponentData(created, new OwnerNode { Value = mainNode });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Queue a single unit for training at a veilstone main node.
        /// Cost paid upfront; unit spawns when timer expires. One at a time per node.
        /// </summary>
        private void TryQueueUnit(EntityManager em, Entity nodeEntity,
            ref BorderAIState ai, ref Random random, ref int veilstoneBank)
        {
            float roll = random.NextFloat(0, 1);
            byte unitType = 0;
            int cost = 0;
            float trainTime = 0f;

            if (ai.Phase >= 2 && roll < 0.2f && veilstoneBank >= GodsplinterCost)
            { unitType = 3; cost = GodsplinterCost; trainTime = GodsplinterTrainTime; }
            else if (ai.Phase >= 1 && roll < 0.35f && veilstoneBank >= VeilstingerCost)
            { unitType = 2; cost = VeilstingerCost; trainTime = VeilstingerTrainTime; }
            else if (veilstoneBank >= CrystallingCost)
            { unitType = 1; cost = CrystallingCost; trainTime = CrystallingTrainTime; }

            if (unitType == 0) return;

            if (FactionEconomy.Spend(em, Faction.Border, Cost.Of(veilstone: cost)))
            {
                veilstoneBank -= cost;
                em.SetComponentData(nodeEntity, new BorderTrainingState
                {
                    TrainingUnitType = unitType,
                    TimeRemaining = trainTime,
                    TotalTime = trainTime
                });
            }
        }

        /// <summary>
        /// Find a valid spawn position near a node — on passable terrain within map bounds.
        /// </summary>
        private float3 FindValidSpawnPos(float3 nodePos, ref Random random)
        {
            var grid = PassabilityGrid.Instance;

            // First pass: require connectivity (rejects beach pockets).
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float3 candidate = nodePos + new float3(
                    random.NextFloat(-5, 5), 0, random.NextFloat(-5, 5));

                if (OutsideMapBounds(candidate))
                    continue;

                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                if (grid != null && !grid.IsPassable(candidate))
                    continue;
                if (!HasOpenNeighbourhood(candidate, grid))
                    continue;

                return candidate;
            }

            // Fallback: relax the connectivity rule so units can still spawn from
            // a tightly-cornered node, but still require basic passability.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float3 candidate = nodePos + new float3(
                    random.NextFloat(-5, 5), 0, random.NextFloat(-5, 5));
                if (OutsideMapBounds(candidate)) continue;
                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);
                if (grid == null || grid.IsPassable(candidate)) return candidate;
            }
            return new float3(float.MinValue, 0, 0);
        }

        /// <summary>
        /// Territorial aggression: veilstone units attack non-Veilstone enemies
        /// that enter the node's spread radius. Idle units near the node
        /// are sent to intercept intruders.
        /// </summary>
        private void AttackIntruders(EntityManager em, float3 nodePos, float spreadRadius)
        {
            // Detect non-Veilstone units within spread radius
            using var intruders = _unitQuery.ToEntityArray(Allocator.Temp);
            using var intruderFactions = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var intruderTransforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Find closest intruder within territory
            float3 closestIntruderPos = float3.zero;
            bool foundIntruder = false;
            float closestDist = float.MaxValue;

            for (int i = 0; i < intruders.Length; i++)
            {
                if (intruderFactions[i].Value == Faction.Border) continue;
                float dist = math.distance(nodePos, intruderTransforms[i].Position);
                if (dist <= spreadRadius && dist < closestDist)
                {
                    closestDist = dist;
                    closestIntruderPos = intruderTransforms[i].Position;
                    foundIntruder = true;
                }
            }

            if (!foundIntruder) return;

            // Send idle veilstone units near this node to intercept
            using var crystalUnits = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var crystalTransforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < crystalUnits.Length; i++)
            {
                float dist = math.distance(nodePos, crystalTransforms[i].Position);
                if (dist > spreadRadius + 5f) continue; // Only nearby veilstone units

                // Check if unit is idle (no destination set)
                bool isIdle = true;
                if (em.HasComponent<DesiredDestination>(crystalUnits[i]))
                {
                    var dest = em.GetComponentData<DesiredDestination>(crystalUnits[i]);
                    if (dest.Has == 1) isIdle = false;
                }
                if (em.HasComponent<Target>(crystalUnits[i]))
                {
                    var target = em.GetComponentData<Target>(crystalUnits[i]);
                    if (target.Value != Entity.Null && em.Exists(target.Value)) isIdle = false;
                }

                if (!isIdle) continue;

                // Send toward intruder
                if (em.HasComponent<DesiredDestination>(crystalUnits[i]))
                {
                    em.SetComponentData(crystalUnits[i], new DesiredDestination
                    {
                        Position = closestIntruderPos,
                        Has = 1
                    });
                }
                else
                {
                    em.AddComponentData(crystalUnits[i], new DesiredDestination
                    {
                        Position = closestIntruderPos,
                        Has = 1
                    });
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CENTRALIZED ATTACK WAVE SYSTEM
        // ═══════════════════════════════════════════════════════════════════════

        // ─── Wave tuning ─────────────────────────────────────────────────
        private const float WaveIntervalMin = 180f;   // 3 min
        private const float WaveIntervalMax = 240f;   // 4 min
        private const int   WaveThresholdGrowth = 4;  // +4 units required per wave
        private const int   WaveThresholdMax = 80;    // never need more than 80 to fire
        private const float WaveTargetReachedDist = 8f;
        private const int   MaxIdlePacing = 10;       // at most 10 units visibly wander each tick
        private const float IdlePacingRadius = 5f;    // wander within 5m of nearest border building

        /// <summary>
        /// Wave-based attack system. Replaces the older continuous-pursuit model.
        ///
        /// Idle units linger near border buildings (only ~10 visibly pacing per
        /// tick to avoid a swarm look). When enough idle units have accumulated
        /// AND the wave timer has expired, every idle unit is given a
        /// BorderWaveOrder pointing at a random player Hall — they march there
        /// together, fighting whatever they bump into. The wave threshold grows
        /// each fire so later waves are bigger.
        /// </summary>
        private void UpdateAttackWaves(EntityManager em,
            NativeArray<Entity> mainNodes, NativeArray<LocalTransform> nodeTransforms,
            ref Random random)
        {
            if (_waveQuery.IsEmpty) return;

            using var waveEntities = _waveQuery.ToEntityArray(Allocator.Temp);
            var wave = em.GetComponentData<BorderWaveState>(waveEntities[0]);

            // 1. March any units already carrying a BorderWaveOrder. Clears the
            //    order when they reach the target zone or the wave is empty.
            int marching = MarchWaveOrders(em);

            // 2. Wave-fire check. We tick the timer down regardless of unit
            //    count so the player feels predictable rhythm; the threshold
            //    just gates whether a fire actually launches.
            wave.WaveTimer -= DecisionInterval;
            int idleAvailable = CountIdleBorderUnits(em);

            if (wave.WaveTimer <= 0f && idleAvailable >= wave.WaveThreshold && mainNodes.Length > 0)
            {
                int dispatched = FireWave(em, wave.WaveNumber + 1, ref random);
                if (dispatched > 0)
                {
                    wave.WaveNumber++;
                    wave.WaveInterval = random.NextFloat(WaveIntervalMin, WaveIntervalMax);
                    wave.WaveTimer = wave.WaveInterval;
                    wave.WaveThreshold = math.min(WaveThresholdMax,
                        wave.WaveThreshold + WaveThresholdGrowth);
                    AILogger.Log(Faction.Border, "WAVE",
                        $"Wave #{wave.WaveNumber} fired — {dispatched} units dispatched. " +
                        $"Next in {wave.WaveInterval:F0}s, threshold now {wave.WaveThreshold}.");
                }
            }
            else if (wave.WaveTimer <= 0f)
            {
                // Timer ready but not enough idle units — keep a small grace
                // window so the wave fires the moment the threshold is met,
                // without spamming the log.
                wave.WaveTimer = 0f;
            }

            // 3. Rescue stranded units (failed pathfind / abandoned beach).
            //    Cheap: only every wave tick equivalent (~1 min cadence).
            if ((wave.WaveNumber & 0x3) == (random.NextInt(0, 4)))
                RescueStrandedUnits(em, nodeTransforms, ref random);

            // 4. Idle pacing for units without a wave order.
            IdlePacing(em, ref random);

            em.SetComponentData(waveEntities[0], wave);
        }

        /// <summary>
        /// Pick a random non-Border player Hall and stamp every idle border unit
        /// (no BorderWaveOrder, no live Target) with a BorderWaveOrder pointed
        /// at it. Returns the count actually dispatched.
        /// </summary>
        private int FireWave(EntityManager em, int waveNumber, ref Random random)
        {
            // Gather target candidates: live non-Border Halls.
            var targetPositions = new NativeList<float3>(Allocator.Temp);
            using (var hallEnts = _hallQuery.ToEntityArray(Allocator.Temp))
            using (var hallFactions = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var hallTransforms = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < hallEnts.Length; i++)
                {
                    if (hallFactions[i].Value == Faction.Border) continue;
                    if (em.HasComponent<Health>(hallEnts[i]))
                    {
                        var h = em.GetComponentData<Health>(hallEnts[i]);
                        if (h.Value <= 0) continue;
                    }
                    targetPositions.Add(hallTransforms[i].Position);
                }
            }

            // Fallback: any non-Border building if no halls survive.
            if (targetPositions.Length == 0)
            {
                var anyBuilding = em.CreateEntityQuery(
                    ComponentType.ReadOnly<BuildingTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<Health>());
                using var bEnts = anyBuilding.ToEntityArray(Allocator.Temp);
                using var bFactions = anyBuilding.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var bTransforms = anyBuilding.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var bHealth = anyBuilding.ToComponentDataArray<Health>(Allocator.Temp);
                for (int i = 0; i < bEnts.Length; i++)
                {
                    if (bFactions[i].Value == Faction.Border) continue;
                    if (bHealth[i].Value <= 0) continue;
                    targetPositions.Add(bTransforms[i].Position);
                }
            }

            if (targetPositions.Length == 0) { targetPositions.Dispose(); return 0; }

            float3 waveTarget = targetPositions[random.NextInt(0, targetPositions.Length)];
            targetPositions.Dispose();

            // Stamp every idle unit with the wave order. TargetingSystem still
            // auto-acquires enemies in LOS — combat-on-the-way works for free.
            using var units = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            int dispatched = 0;
            for (int i = 0; i < units.Length; i++)
            {
                if (em.HasComponent<BorderWaveOrder>(units[i])) continue;
                if (em.HasComponent<Target>(units[i]))
                {
                    var t = em.GetComponentData<Target>(units[i]);
                    if (t.Value != Entity.Null && em.Exists(t.Value)) continue;
                }
                em.AddComponentData(units[i], new BorderWaveOrder
                {
                    Target = waveTarget,
                    WaveNumber = waveNumber,
                });
                dispatched++;
            }
            return dispatched;
        }

        /// <summary>
        /// For every unit currently carrying a BorderWaveOrder: if it has no
        /// live Target, push DesiredDestination back to the wave target so the
        /// march resumes after each kill. If it's reached the target, drop the
        /// order so it goes back to idle behavior.
        /// </summary>
        private int MarchWaveOrders(EntityManager em)
        {
            var orderQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderUnitTag>(),
                ComponentType.ReadOnly<BorderWaveOrder>(),
                ComponentType.ReadOnly<LocalTransform>());

            using var ents = orderQuery.ToEntityArray(Allocator.Temp);
            using var orders = orderQuery.ToComponentDataArray<BorderWaveOrder>(Allocator.Temp);
            using var xforms = orderQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int marched = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                // Reached target zone? Done with this wave.
                if (math.distance(xforms[i].Position, orders[i].Target) <= WaveTargetReachedDist)
                {
                    em.RemoveComponent<BorderWaveOrder>(ents[i]);
                    if (em.HasComponent<DesiredDestination>(ents[i]))
                        em.SetComponentData(ents[i], new DesiredDestination { Position = float3.zero, Has = 0 });
                    continue;
                }

                // Engaged in combat → leave alone. TargetingSystem owns the
                // unit until the swing resolves; we'll re-issue the march
                // next tick once Target clears.
                if (em.HasComponent<Target>(ents[i]))
                {
                    var t = em.GetComponentData<Target>(ents[i]);
                    if (t.Value != Entity.Null && em.Exists(t.Value)) continue;
                }

                if (em.HasComponent<DesiredDestination>(ents[i]))
                    em.SetComponentData(ents[i], new DesiredDestination
                    {
                        Position = orders[i].Target,
                        Has = 1,
                    });
                else
                    em.AddComponentData(ents[i], new DesiredDestination
                    {
                        Position = orders[i].Target,
                        Has = 1,
                    });
                marched++;
            }
            return marched;
        }

        /// <summary>Count border units that are idle (no wave order, no live target).</summary>
        private int CountIdleBorderUnits(EntityManager em)
        {
            using var ents = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            int idle = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<BorderWaveOrder>(ents[i])) continue;
                if (em.HasComponent<Target>(ents[i]))
                {
                    var t = em.GetComponentData<Target>(ents[i]);
                    if (t.Value != Entity.Null && em.Exists(t.Value)) continue;
                }
                idle++;
            }
            return idle;
        }

        /// <summary>
        /// Idle pacing: at most MaxIdlePacing border units per tick get a small
        /// random wander destination near their nearest border building. Keeps
        /// non-wave units alive on screen without making the whole pool swirl.
        /// </summary>
        private void IdlePacing(EntityManager em, ref Random random)
        {
            // Gather all border buildings (main + sub) as anchor points.
            var anchors = new NativeList<float3>(Allocator.Temp);
            using (var mains = _mainNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < mains.Length; i++) anchors.Add(mains[i].Position);
            using (var subs = _subNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < subs.Length; i++) anchors.Add(subs[i].Position);

            if (anchors.Length == 0) { anchors.Dispose(); return; }

            using var ents = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var xforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int paced = 0;
            // Random start offset so we don't always pick the same first units.
            int start = ents.Length > 0 ? random.NextInt(0, ents.Length) : 0;
            for (int k = 0; k < ents.Length && paced < MaxIdlePacing; k++)
            {
                int i = (start + k) % ents.Length;

                if (em.HasComponent<BorderWaveOrder>(ents[i])) continue;
                if (em.HasComponent<Target>(ents[i]))
                {
                    var t = em.GetComponentData<Target>(ents[i]);
                    if (t.Value != Entity.Null && em.Exists(t.Value)) continue;
                }
                // Skip if already walking somewhere.
                if (em.HasComponent<DesiredDestination>(ents[i]))
                {
                    var d = em.GetComponentData<DesiredDestination>(ents[i]);
                    if (d.Has == 1) continue;
                }

                // Pick nearest anchor and a small offset around it.
                float3 pos = xforms[i].Position;
                float3 nearest = anchors[0];
                float bestSq = float.MaxValue;
                for (int a = 0; a < anchors.Length; a++)
                {
                    float dx = anchors[a].x - pos.x;
                    float dz = anchors[a].z - pos.z;
                    float sq = dx * dx + dz * dz;
                    if (sq < bestSq) { bestSq = sq; nearest = anchors[a]; }
                }

                float angle = random.NextFloat(0f, math.PI * 2f);
                float radius = random.NextFloat(1f, IdlePacingRadius);
                float3 wander = nearest + new float3(
                    math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                wander.y = TerrainUtility.GetHeight(wander.x, wander.z);

                if (em.HasComponent<DesiredDestination>(ents[i]))
                    em.SetComponentData(ents[i], new DesiredDestination { Position = wander, Has = 1 });
                else
                    em.AddComponentData(ents[i], new DesiredDestination { Position = wander, Has = 1 });
                paced++;
            }

            anchors.Dispose();
        }

        /// <summary>
        /// Find idle border units stranded far from every main node (e.g. abandoned
        /// on a beach after a failed pathfind) and teleport them to a fresh spawn
        /// position near their nearest main node. Engaged units are left alone.
        /// </summary>
        private void RescueStrandedUnits(EntityManager em,
            NativeArray<LocalTransform> nodeTransforms, ref Random random)
        {
            if (nodeTransforms.Length == 0) return;

            using var units = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var unitTransforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int rescued = 0;
            for (int i = 0; i < units.Length; i++)
            {
                // Skip units that are still actively trying to do something.
                if (em.HasComponent<Target>(units[i]))
                {
                    var t = em.GetComponentData<Target>(units[i]);
                    if (t.Value != Entity.Null && em.Exists(t.Value)) continue;
                }
                if (em.HasComponent<DesiredDestination>(units[i]))
                {
                    var d = em.GetComponentData<DesiredDestination>(units[i]);
                    if (d.Has == 1) continue;
                }

                // Distance to nearest main node.
                var pos = unitTransforms[i].Position;
                float bestDist = float.MaxValue;
                int nearestIdx = -1;
                for (int n = 0; n < nodeTransforms.Length; n++)
                {
                    float d = math.distance(pos, nodeTransforms[n].Position);
                    if (d < bestDist) { bestDist = d; nearestIdx = n; }
                }

                if (bestDist < StrandedDistance || nearestIdx < 0) continue;

                float3 spawnPos = FindValidSpawnPos(nodeTransforms[nearestIdx].Position, ref random);
                if (spawnPos.x == float.MinValue) continue;

                var xf = unitTransforms[i];
                xf.Position = spawnPos;
                em.SetComponentData(units[i], xf);

                if (em.HasComponent<StuckState>(units[i]))
                    em.SetComponentData(units[i], new StuckState { Counter = 0, LastAttempt = 0 });
                rescued++;
            }
            if (rescued > 0)
                AILogger.Log(Faction.Border, "RESCUE", $"Recalled {rescued} stranded units to nearest main node");
        }

        // TryExpand REMOVED BY DESIGN: the Border cannot build new nodes.
        // Nodes only enter the map at bootstrap (scene markers).

        /// <summary>
        /// True when the candidate sits off the actual terrain rectangle.
        /// Unity terrains are corner-anchored (span position..position+size),
        /// so the old origin-centred |coord| &gt; MapHalfSize test was wrong
        /// on any hand-authored map.
        /// </summary>
        private static bool OutsideMapBounds(float3 candidate)
        {
            TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
            return candidate.x < bMin.x || candidate.x > bMax.x ||
                   candidate.z < bMin.y || candidate.z > bMax.y;
        }

        /// <summary>
        /// Sample <see cref="MinPassableNeighbours"/>+ passable cells in a ring around
        /// the given position. Used to reject "stranded" placements where the spot is
        /// passable but boxed in by water/cliff.
        /// </summary>
        private static bool HasOpenNeighbourhood(float3 pos, PassabilityGrid grid)
        {
            if (grid == null) return true; // No grid → can't check, assume OK
            int passable = 0;
            for (int d = 0; d < 8; d++)
            {
                float a = d * (math.PI * 2f / 8f);
                float3 sample = pos + new float3(
                    math.cos(a) * ConnectivityProbeRadius, 0f,
                    math.sin(a) * ConnectivityProbeRadius);
                sample.y = TerrainUtility.GetHeight(sample.x, sample.z);
                if (grid.IsPassable(sample)) passable++;
            }
            return passable >= MinPassableNeighbours;
        }

        /// <summary>
        /// Maximum distance a new sub-node can spawn from any existing veilstone node.
        /// Keeps the veilstone network clustered and prevents nodes appearing in player bases.
        /// </summary>
        private const float MaxDistFromExistingNode = 10f;

        /// <summary>
        /// Find a random position within the border area around a node.
        /// New positions must be within MaxDistFromExistingNode of at least one existing veilstone node.
        /// </summary>
        private float3 FindBorderPosition(float3 nodePos, float spreadRadius,
            ref Random random)
        {
            var grid = PassabilityGrid.Instance;

            // Gather all existing veilstone node positions for proximity check
            using var subTransforms = _subNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var mainTransforms = _mainNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                float dist = random.NextFloat(3f, math.max(4f, spreadRadius * 0.8f));
                float3 candidate = nodePos + new float3(
                    math.cos(angle) * dist, 0, math.sin(angle) * dist);

                // Must be within map bounds
                if (OutsideMapBounds(candidate))
                    continue;

                // Set correct terrain height
                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                // Must be on passable terrain (not water, not steep slope)
                if (grid != null && !grid.IsPassable(candidate))
                    continue;

                if (dist > spreadRadius)
                    continue;

                // Must be within MaxDistFromExistingNode of at least one veilstone node
                bool nearExisting = false;
                for (int i = 0; i < mainTransforms.Length; i++)
                {
                    if (math.distancesq(candidate, mainTransforms[i].Position) <=
                        MaxDistFromExistingNode * MaxDistFromExistingNode)
                    {
                        nearExisting = true;
                        break;
                    }
                }
                if (!nearExisting)
                {
                    for (int i = 0; i < subTransforms.Length; i++)
                    {
                        if (math.distancesq(candidate, subTransforms[i].Position) <=
                            MaxDistFromExistingNode * MaxDistFromExistingNode)
                        {
                            nearExisting = true;
                            break;
                        }
                    }
                }

                if (nearExisting)
                    return candidate;
            }

            return new float3(float.MinValue, 0, 0); // Signal failure
        }
    }
}
