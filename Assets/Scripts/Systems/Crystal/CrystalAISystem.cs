// File: Assets/Scripts/Systems/Crystal/CrystalAISystem.cs
// Standalone AI brain for the Crystal faction.
// Manages building, spawning, harassment, and expansion per CrystalMainNode.

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
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Standalone AI brain for the Crystal faction.
    /// Manages each CrystalMainNode independently:
    /// - Builds sub-nodes in cursed areas
    /// - Spawns combat units from crystal bank
    /// - Sends harassment waves at player bases
    /// - Expands by placing new main nodes
    ///
    /// Does NOT use AIBrain/manager architecture -- crystal economy
    /// is fundamentally different (passive income, direct spawning).
    ///
    /// Uses SystemBase (not ISystem) because entity factory Create() methods
    /// perform structural changes (em.AddComponentData) that ISystem codegen blocks.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrystalAISystem : SystemBase
    {
        private float _decisionTimer;
        private int _decisionTick;  // Deterministic tick counter for multiplayer sync
        private const float DecisionInterval = 5.0f; // AI thinks every 5 seconds

        // Cached EntityQueries — initialized in OnCreate()
        private EntityQuery _mainNodeQuery;
        private EntityQuery _subNodeQuery;
        private EntityQuery _unitQuery;           // UnitTag + FactionTag + LocalTransform (intruder detection)
        private EntityQuery _crystalUnitQuery;    // CrystalUnitTag + LocalTransform
        private EntityQuery _hallQuery;           // HallTag + FactionTag + LocalTransform
        private EntityQuery _mainNodeTransformQuery; // CrystalMainNodeTag + LocalTransform (expansion)
        private EntityQuery _waveQuery;           // CrystalWaveState (attack waves)
        private EntityQuery _subNodeTransformQuery;  // CrystalSubNodeTag + LocalTransform (cursed position)

        // AI costs — centralised in CrystalConstants
        private const int ResourceNodeCost = AIResourceNodeCost;
        private const int TurretNodeCost = AITurretNodeCost;
        private const int RestorationNodeCost = AIRestorationNodeCost;
        private const int EnforcementNodeCost = AIEnforcementNodeCost;
        private const int SuppressionNodeCost = AISuppressionNodeCost;
        private const int CrystallingCost = AICrystallingCost;
        private const int VeilstingerCost = AIVeilstingerCost;
        private const int GodsplinterCost = AIGodsplinterCost;
        private const int ExpansionCost = AIExpansionCost;

        // Expansion pacing — new curse nodes start fast, slow logarithmically
        private const int MaxCurseNodes = 16;
        private const float BaseExpansionInterval = 30f;   // 30s at game start
        private const float ExpansionSlowdownRate = 20f;   // How fast it slows
        private const float MaxExpansionInterval = 300f;   // Never slower than 5 min

        // Hard caps on the curse footprint.
        public const int MaxCrystalUnits     = 100; // total live curse units (Crystalling + Veilstinger + Godsplinter)
        public const int MaxCrystalBuildings = 50;  // total curse buildings (main + sub nodes)

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
            RequireForUpdate<CrystalMainNodeTag>();

            _mainNodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<CrystalAIState>(),
                ComponentType.ReadOnly<CrystalNode>(),
                ComponentType.ReadOnly<CrystalNodeLevel>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<CrystalMainNodeTag>()
            );

            _subNodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalSubNodeTag>(),
                ComponentType.ReadOnly<OwnerNode>()
            );

            _unitQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _crystalUnitQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalUnitTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _hallQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _mainNodeTransformQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _waveQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<CrystalWaveState>()
            );

            _subNodeTransformQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalSubNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
        }

        protected override void OnUpdate()
        {
            _decisionTimer += World.Time.DeltaTime;
            if (_decisionTimer < DecisionInterval) return;
            _decisionTimer = 0f;

            var em = EntityManager;

            // Get crystal bank balance
            if (!FactionEconomy.TryGetResources(em, Faction.Curse, out var resources))
                return;

            int crystalBank = resources.Crystal;

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
            using var aiStates = _mainNodeQuery.ToComponentDataArray<CrystalAIState>(Allocator.Temp);
            using var crystalNodes = _mainNodeQuery.ToComponentDataArray<CrystalNode>(Allocator.Temp);
            using var nodeLevels = _mainNodeQuery.ToComponentDataArray<CrystalNodeLevel>(Allocator.Temp);
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

                // Refresh bank balance
                if (FactionEconomy.TryGetResources(em, Faction.Curse, out resources))
                    crystalBank = resources.Crystal;

                // Drive AI phase from elapsed game time
                if (elapsedMin >= 15f) ai.Phase = 2;
                else if (elapsedMin >= 5f) ai.Phase = 1;
                else ai.Phase = 0;

                // Update node level to match phase for display
                em.SetComponentData(entities[n], new CrystalNodeLevel { Value = ai.Phase + 1 });

                // === TRAINING TICK (one unit at a time per node) ===
                if (em.HasComponent<CrystalTrainingState>(entities[n]))
                {
                    var ts = em.GetComponentData<CrystalTrainingState>(entities[n]);
                    if (ts.TrainingUnitType != 0)
                    {
                        ts.TimeRemaining -= DecisionInterval;
                        if (ts.TimeRemaining <= 0f)
                        {
                            // Hard unit cap: if we've reached MaxCrystalUnits, drop the
                            // queued spawn (refund nothing — keeps the cap firm).
                            if (crystalUnitCount < MaxCrystalUnits)
                            {
                                float3 spawnPos = FindValidSpawnPos(nodePos, ref random);
                                if (spawnPos.x != float.MinValue)
                                {
                                    string unitName = ts.TrainingUnitType switch { 1 => "Crystalling", 2 => "Veilstinger", 3 => "Godsplinter", _ => "?" };
                                    AILogger.Log(Faction.Curse, "TRAINING", $"Spawned {unitName} at ({spawnPos.x:F0},{spawnPos.z:F0}), bank:{crystalBank}, pop:{crystalUnitCount + 1}/{MaxCrystalUnits}");
                                    switch (ts.TrainingUnitType)
                                    {
                                        case 1: Crystalling.Create(em, spawnPos, Faction.Curse); break;
                                        case 2: Veilstinger.Create(em, spawnPos, Faction.Curse); break;
                                        case 3: Godsplinter.Create(em, spawnPos, Faction.Curse); break;
                                    }
                                    crystalUnitCount++;
                                }
                            }
                            ts.TrainingUnitType = 0;
                            ts.TimeRemaining = 0f;
                            ts.TotalTime = 0f;
                        }
                        em.SetComponentData(entities[n], ts);
                    }
                    else if (crystalUnitCount < MaxCrystalUnits)
                    {
                        TryQueueUnit(em, entities[n], ref ai, ref random, ref crystalBank);
                    }
                }

                // === BUILD PHASE (sub-buildings within territory) ===
                ai.BuildTimer -= DecisionInterval;
                if (ai.BuildTimer <= 0)
                {
                    if (totalBuildings < MaxCrystalBuildings)
                    {
                        if (TryBuildSubNode(em, entities[n], ref ai, nodePos, territoryRadius,
                                ref random, ref crystalBank))
                            totalBuildings++;
                    }
                    ai.BuildTimer = 15f + random.NextFloat(0, 5f);
                }

                // === TERRITORIAL AGGRESSION ===
                AttackIntruders(em, nodePos, territoryRadius);

                // === EXPANSION PHASE (new curse nodes within territory) ===
                ai.ExpansionTimer -= DecisionInterval;
                if (ai.ExpansionTimer <= 0 && crystalBank >= ExpansionCost)
                {
                    int curseNodeCount = entities.Length;
                    if (curseNodeCount < MaxCurseNodes && totalBuildings < MaxCrystalBuildings)
                    {
                        if (TryExpand(em, nodePos, territoryRadius, ref random, ref crystalBank))
                            totalBuildings++;

                        float elapsedMinutes = (float)(World.Time.ElapsedTime / 60.0);
                        float expansionInterval = math.min(MaxExpansionInterval,
                            BaseExpansionInterval + ExpansionSlowdownRate * math.log2(1f + elapsedMinutes));
                        ai.ExpansionTimer = expansionInterval + random.NextFloat(0, expansionInterval * 0.3f);
                    }
                }

                // Write back modified AI state
                em.SetComponentData(entities[n], ai);
            }

            // === CENTRALIZED ATTACK WAVE SYSTEM ===
            UpdateAttackWaves(em, entities, transforms, ref random);
        }

        /// <summary>
        /// Try to build a sub-node for this main. Returns true if a building was created
        /// (caller increments the global building cap counter).
        /// </summary>
        private bool TryBuildSubNode(EntityManager em, Entity mainNode,
            ref CrystalAIState ai, float3 nodePos, float spreadRadius,
            ref Random random, ref int crystalBank)
        {
            // Count existing sub-nodes belonging to THIS main node
            int resourceCount = 0, turretCount = 0, restorationCount = 0;
            int enforcementCount = 0, suppressionCount = 0, totalCount = 0;

            using var subNodes = _subNodeQuery.ToComponentDataArray<CrystalSubNodeTag>(Allocator.Temp);
            using var owners = _subNodeQuery.ToComponentDataArray<OwnerNode>(Allocator.Temp);
            for (int i = 0; i < subNodes.Length; i++)
            {
                if (owners[i].Value != mainNode) continue;
                totalCount++;
                switch (subNodes[i].Type)
                {
                    case CrystalSubNodeType.Resource: resourceCount++; break;
                    case CrystalSubNodeType.Turret: turretCount++; break;
                    case CrystalSubNodeType.Restoration: restorationCount++; break;
                    case CrystalSubNodeType.Enforcement: enforcementCount++; break;
                    case CrystalSubNodeType.Suppression: suppressionCount++; break;
                }
            }

            // Hard cap: no more sub-nodes for this main node
            if (totalCount >= MaxSubNodesPerMain) return false;

            // Find a position within cursed area
            float3 buildPos = FindCursedPosition(nodePos, spreadRadius, ref random);
            if (buildPos.x == float.MinValue) return false;

            // Build decision tree (per-node limits)
            // Priority: Resource > Turret > Restoration > Enforcement > Suppression
            Entity created = Entity.Null;

            if (resourceCount < MaxResourceNodesPerMain && crystalBank >= ResourceNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: ResourceNodeCost)))
                {
                    created = CrystalResourceNode.Create(em, buildPos);
                    crystalBank -= ResourceNodeCost;
                }
            }
            else if (turretCount < MaxTurretNodesPerMain && crystalBank >= TurretNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: TurretNodeCost)))
                {
                    created = CrystalTurretNode.Create(em, buildPos);
                    crystalBank -= TurretNodeCost;
                }
            }
            else if (restorationCount < MaxRestorationNodesPerMain && crystalBank >= RestorationNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: RestorationNodeCost)))
                {
                    created = CrystalRestorationNode.Create(em, buildPos);
                    crystalBank -= RestorationNodeCost;
                }
            }
            else if (enforcementCount < MaxEnforcementNodesPerMain && crystalBank >= EnforcementNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: EnforcementNodeCost)))
                {
                    created = CrystalEnforcementNode.Create(em, buildPos);
                    crystalBank -= EnforcementNodeCost;
                }
            }
            else if (suppressionCount < MaxSuppressionNodesPerMain && crystalBank >= SuppressionNodeCost && ai.Phase >= 2)
            {
                if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: SuppressionNodeCost)))
                {
                    created = CrystalSuppressionNode.Create(em, buildPos);
                    crystalBank -= SuppressionNodeCost;
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
        /// Queue a single unit for training at a crystal main node.
        /// Cost paid upfront; unit spawns when timer expires. One at a time per node.
        /// </summary>
        private void TryQueueUnit(EntityManager em, Entity nodeEntity,
            ref CrystalAIState ai, ref Random random, ref int crystalBank)
        {
            float roll = random.NextFloat(0, 1);
            byte unitType = 0;
            int cost = 0;
            float trainTime = 0f;

            if (ai.Phase >= 2 && roll < 0.2f && crystalBank >= GodsplinterCost)
            { unitType = 3; cost = GodsplinterCost; trainTime = GodsplinterTrainTime; }
            else if (ai.Phase >= 1 && roll < 0.35f && crystalBank >= VeilstingerCost)
            { unitType = 2; cost = VeilstingerCost; trainTime = VeilstingerTrainTime; }
            else if (crystalBank >= CrystallingCost)
            { unitType = 1; cost = CrystallingCost; trainTime = CrystallingTrainTime; }

            if (unitType == 0) return;

            if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: cost)))
            {
                crystalBank -= cost;
                em.SetComponentData(nodeEntity, new CrystalTrainingState
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
            int half = GameSettings.MapHalfSize;

            // First pass: require connectivity (rejects beach pockets).
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float3 candidate = nodePos + new float3(
                    random.NextFloat(-5, 5), 0, random.NextFloat(-5, 5));

                if (math.abs(candidate.x) > half || math.abs(candidate.z) > half)
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
                if (math.abs(candidate.x) > half || math.abs(candidate.z) > half) continue;
                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);
                if (grid == null || grid.IsPassable(candidate)) return candidate;
            }
            return new float3(float.MinValue, 0, 0);
        }

        /// <summary>
        /// Territorial aggression: crystal units attack non-Crystal enemies
        /// that enter the node's spread radius. Idle units near the node
        /// are sent to intercept intruders.
        /// </summary>
        private void AttackIntruders(EntityManager em, float3 nodePos, float spreadRadius)
        {
            // Detect non-Crystal units within spread radius
            using var intruders = _unitQuery.ToEntityArray(Allocator.Temp);
            using var intruderFactions = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var intruderTransforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Find closest intruder within territory
            float3 closestIntruderPos = float3.zero;
            bool foundIntruder = false;
            float closestDist = float.MaxValue;

            for (int i = 0; i < intruders.Length; i++)
            {
                if (intruderFactions[i].Value == Faction.Curse) continue;
                float dist = math.distance(nodePos, intruderTransforms[i].Position);
                if (dist <= spreadRadius && dist < closestDist)
                {
                    closestDist = dist;
                    closestIntruderPos = intruderTransforms[i].Position;
                    foundIntruder = true;
                }
            }

            if (!foundIntruder) return;

            // Send idle crystal units near this node to intercept
            using var crystalUnits = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var crystalTransforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < crystalUnits.Length; i++)
            {
                float dist = math.distance(nodePos, crystalTransforms[i].Position);
                if (dist > spreadRadius + 5f) continue; // Only nearby crystal units

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

        private void UpdateAttackWaves(EntityManager em,
            NativeArray<Entity> mainNodes, NativeArray<LocalTransform> nodeTransforms,
            ref Random random)
        {
            if (_waveQuery.IsEmpty) return;

            using var waveEntities = _waveQuery.ToEntityArray(Allocator.Temp);
            var wave = em.GetComponentData<CrystalWaveState>(waveEntities[0]);

            int nodeCount = mainNodes.Length;
            float elapsedMinutes = (float)(World.Time.ElapsedTime / 60.0);

            // WaveInterval still scales with curse footprint, but it's no longer
            // the trigger for unit movement — pursuit is continuous, see below.
            // The interval now governs the rescue pass + logging cadence.
            wave.WaveInterval = math.max(25f, 120f - nodeCount * 8f - elapsedMinutes * 2f);

            // Continuous pursuit: every decision tick (5 s), every non-engaged
            // curse unit gets redirected to the nearest non-Crystal asset (any
            // building or unit, any player faction). Subsumes the old "pick one
            // player's halls and send a periodic wave" model — units now hunt
            // whatever's closest at all times.
            int reassigned = PursueAllPlayerAssets(em);

            wave.WaveTimer -= DecisionInterval;
            if (wave.WaveTimer <= 0)
            {
                // On the cadence tick, also rescue any units stranded far from
                // every main node (failed pathfind / abandoned beach) and log a
                // wave marker so the curse's pressure is visible in the AI log.
                RescueStrandedUnits(em, nodeTransforms, ref random);
                wave.WaveTimer = wave.WaveInterval;
                wave.WaveNumber++;
                AILogger.Log(Faction.Curse, "WAVE",
                    $"Pursuit wave #{wave.WaveNumber}: {reassigned} units retargeted (interval {wave.WaveInterval:F0}s)");
            }

            em.SetComponentData(waveEntities[0], wave);
        }

        /// <summary>
        /// Continuous pursuit pass. Each tick, redirects every non-engaged curse
        /// unit toward the nearest non-Crystal asset on the entire map — buildings
        /// AND units, any player faction. Engaged units (live Target component)
        /// are left alone so combat-in-progress isn't aborted. Returns the count
        /// of units retargeted, for logging.
        ///
        /// Targets are gathered from BuildingTag + UnitTag with FactionTag != White.
        /// Dead entities (Health.Value &lt;= 0) are excluded — pursuing a corpse
        /// would let them stack on a kill site instead of moving on.
        /// </summary>
        private int PursueAllPlayerAssets(EntityManager em)
        {
            // ── Gather target positions ──────────────────────────────────
            var targets = new NativeList<float3>(Allocator.Temp);

            // Buildings (every faction except Crystal).
            var buildingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using (var bEnts = buildingQuery.ToEntityArray(Allocator.Temp))
            using (var bFactions = buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var bTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var bHealth = buildingQuery.ToComponentDataArray<Health>(Allocator.Temp))
            {
                for (int i = 0; i < bEnts.Length; i++)
                {
                    if (bFactions[i].Value == Faction.Curse) continue;
                    if (bHealth[i].Value <= 0) continue;
                    targets.Add(bTransforms[i].Position);
                }
            }

            // Units (every faction except Crystal).
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using (var uEnts = unitQuery.ToEntityArray(Allocator.Temp))
            using (var uFactions = unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var uTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var uHealth = unitQuery.ToComponentDataArray<Health>(Allocator.Temp))
            {
                for (int i = 0; i < uEnts.Length; i++)
                {
                    if (uFactions[i].Value == Faction.Curse) continue;
                    if (uHealth[i].Value <= 0) continue;
                    targets.Add(uTransforms[i].Position);
                }
            }

            if (targets.Length == 0) { targets.Dispose(); return 0; }

            // ── Redirect every non-engaged crystal unit ──────────────────
            using var units = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var unitTransforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int reassigned = 0;
            for (int i = 0; i < units.Length; i++)
            {
                // Skip units in active combat — TargetingSystem set Target on them
                // and the combat systems will resolve it. Re-pathing here would
                // pull them off mid-swing.
                if (em.HasComponent<Target>(units[i]))
                {
                    var t = em.GetComponentData<Target>(units[i]);
                    if (t.Value != Entity.Null && em.Exists(t.Value)) continue;
                }

                // Find nearest target (sq-distance, no allocation).
                float3 unitPos = unitTransforms[i].Position;
                float bestDistSq = float.MaxValue;
                float3 bestTarget = targets[0];
                for (int t = 0; t < targets.Length; t++)
                {
                    float dx = targets[t].x - unitPos.x;
                    float dz = targets[t].z - unitPos.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestDistSq) { bestDistSq = d; bestTarget = targets[t]; }
                }

                if (em.HasComponent<DesiredDestination>(units[i]))
                    em.SetComponentData(units[i], new DesiredDestination { Position = bestTarget, Has = 1 });
                else
                    em.AddComponentData(units[i], new DesiredDestination { Position = bestTarget, Has = 1 });
                reassigned++;
            }

            targets.Dispose();
            return reassigned;
        }

        /// <summary>
        /// Find idle curse units stranded far from every main node (e.g. abandoned
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
                AILogger.Log(Faction.Curse, "RESCUE", $"Recalled {rescued} stranded units to nearest main node");
        }

        /// <summary>Try to seed a new main curse node. Returns true on creation.</summary>
        private bool TryExpand(EntityManager em, float3 currentNodePos,
            float territoryRadius, ref Random random, ref int crystalBank)
        {
            // New main nodes must spawn within the area of influence of an existing node.
            // Candidates are placed near the edge of the parent's territory (within 5 units).
            var grid = PassabilityGrid.Instance;
            int half = GameSettings.MapHalfSize;

            bool found = false;
            float3 expandPos = float3.zero;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                // Spawn near edge of parent territory (within last 5 units of radius)
                float dist = random.NextFloat(
                    math.max(1f, territoryRadius - 5f),
                    territoryRadius);
                float3 candidate = currentNodePos + new float3(
                    math.cos(angle) * dist, 0, math.sin(angle) * dist);

                if (math.abs(candidate.x) > half || math.abs(candidate.z) > half)
                    continue;

                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                if (grid != null && !grid.IsPassable(candidate))
                    continue;

                // Reject candidates surrounded by water/cliff — prevents new main
                // nodes from spawning on a beach pocket where their units would
                // be stranded.
                if (!HasOpenNeighbourhood(candidate, grid))
                    continue;

                expandPos = candidate;
                found = true;
                break;
            }

            if (!found) return false;

            if (FactionEconomy.Spend(em, Faction.Curse, Cost.Of(crystal: ExpansionCost)))
            {
                CrystalMainNode.Create(em, expandPos);
                crystalBank -= ExpansionCost;
                return true;
            }
            return false;
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
        /// Maximum distance a new sub-node can spawn from any existing crystal node.
        /// Keeps the crystal network clustered and prevents nodes appearing in player bases.
        /// </summary>
        private const float MaxDistFromExistingNode = 10f;

        /// <summary>
        /// Find a random position within the cursed area around a node.
        /// New positions must be within MaxDistFromExistingNode of at least one existing crystal node.
        /// </summary>
        private float3 FindCursedPosition(float3 nodePos, float spreadRadius,
            ref Random random)
        {
            var grid = PassabilityGrid.Instance;
            int half = GameSettings.MapHalfSize;

            // Gather all existing crystal node positions for proximity check
            using var subTransforms = _subNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var mainTransforms = _mainNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                float dist = random.NextFloat(3f, math.max(4f, spreadRadius * 0.8f));
                float3 candidate = nodePos + new float3(
                    math.cos(angle) * dist, 0, math.sin(angle) * dist);

                // Must be within map bounds
                if (math.abs(candidate.x) > half || math.abs(candidate.z) > half)
                    continue;

                // Set correct terrain height
                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                // Must be on passable terrain (not water, not steep slope)
                if (grid != null && !grid.IsPassable(candidate))
                    continue;

                if (dist > spreadRadius)
                    continue;

                // Must be within MaxDistFromExistingNode of at least one crystal node
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
