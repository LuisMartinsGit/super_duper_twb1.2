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
            if (!FactionEconomy.TryGetResources(em, Faction.White, out var resources))
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

            for (int n = 0; n < entities.Length; n++)
            {
                var ai = aiStates[n];
                var nodePos = transforms[n].Position;
                float territoryRadius = crystalNodes[n].SpreadRadius; // Fixed territory radius

                // Refresh bank balance
                if (FactionEconomy.TryGetResources(em, Faction.White, out resources))
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
                            float3 spawnPos = FindValidSpawnPos(nodePos, ref random);
                            if (spawnPos.x != float.MinValue)
                            {
                                switch (ts.TrainingUnitType)
                                {
                                    case 1: Crystalling.Create(em, spawnPos, Faction.White); break;
                                    case 2: Veilstinger.Create(em, spawnPos, Faction.White); break;
                                    case 3: Godsplinter.Create(em, spawnPos, Faction.White); break;
                                }
                            }
                            ts.TrainingUnitType = 0;
                            ts.TimeRemaining = 0f;
                            ts.TotalTime = 0f;
                        }
                        em.SetComponentData(entities[n], ts);
                    }
                    else
                    {
                        TryQueueUnit(em, entities[n], ref ai, ref random, ref crystalBank);
                    }
                }

                // === BUILD PHASE (sub-buildings within territory) ===
                ai.BuildTimer -= DecisionInterval;
                if (ai.BuildTimer <= 0)
                {
                    TryBuildSubNode(em, entities[n], ref ai, nodePos, territoryRadius,
                        ref random, ref crystalBank);
                    ai.BuildTimer = 15f + random.NextFloat(0, 5f);
                }

                // === TERRITORIAL AGGRESSION ===
                AttackIntruders(em, nodePos, territoryRadius);

                // === EXPANSION PHASE (new curse nodes within territory) ===
                ai.ExpansionTimer -= DecisionInterval;
                if (ai.ExpansionTimer <= 0 && crystalBank >= ExpansionCost)
                {
                    int curseNodeCount = entities.Length;
                    if (curseNodeCount < MaxCurseNodes)
                    {
                        TryExpand(em, nodePos, territoryRadius, ref random, ref crystalBank);

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

        private void TryBuildSubNode(EntityManager em, Entity mainNode,
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
            if (totalCount >= MaxSubNodesPerMain) return;

            // Find a position within cursed area
            float3 buildPos = FindCursedPosition(nodePos, spreadRadius, ref random);
            if (buildPos.x == float.MinValue) return;

            // Build decision tree (per-node limits)
            // Priority: Resource > Turret > Restoration > Enforcement > Suppression
            Entity created = Entity.Null;

            if (resourceCount < MaxResourceNodesPerMain && crystalBank >= ResourceNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: ResourceNodeCost)))
                {
                    created = CrystalResourceNode.Create(em, buildPos);
                    crystalBank -= ResourceNodeCost;
                }
            }
            else if (turretCount < MaxTurretNodesPerMain && crystalBank >= TurretNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: TurretNodeCost)))
                {
                    created = CrystalTurretNode.Create(em, buildPos);
                    crystalBank -= TurretNodeCost;
                }
            }
            else if (restorationCount < MaxRestorationNodesPerMain && crystalBank >= RestorationNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: RestorationNodeCost)))
                {
                    created = CrystalRestorationNode.Create(em, buildPos);
                    crystalBank -= RestorationNodeCost;
                }
            }
            else if (enforcementCount < MaxEnforcementNodesPerMain && crystalBank >= EnforcementNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: EnforcementNodeCost)))
                {
                    created = CrystalEnforcementNode.Create(em, buildPos);
                    crystalBank -= EnforcementNodeCost;
                }
            }
            else if (suppressionCount < MaxSuppressionNodesPerMain && crystalBank >= SuppressionNodeCost && ai.Phase >= 2)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: SuppressionNodeCost)))
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
            }
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

            if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: cost)))
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

            for (int attempt = 0; attempt < 10; attempt++)
            {
                float3 candidate = nodePos + new float3(
                    random.NextFloat(-5, 5), 0, random.NextFloat(-5, 5));

                if (math.abs(candidate.x) > half || math.abs(candidate.z) > half)
                    continue;

                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                if (grid != null && !grid.IsPassable(candidate))
                    continue;

                return candidate;
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
                if (intruderFactions[i].Value == Faction.White) continue;
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

            // Phase: 0=1 target, 1=2 targets, 2=all targets
            byte wavePhase;
            if (nodeCount <= 2 && elapsedMinutes < 10f) wavePhase = 0;
            else if (nodeCount <= 5 && elapsedMinutes < 20f) wavePhase = 1;
            else wavePhase = 2;

            wave.WaveInterval = math.max(25f, 120f - nodeCount * 8f - elapsedMinutes * 2f);

            wave.WaveTimer -= DecisionInterval;
            if (wave.WaveTimer <= 0)
            {
                SendWave(em, nodeTransforms, ref random, wavePhase, nodeCount);
                wave.WaveTimer = wave.WaveInterval;
                wave.WaveNumber++;
            }

            em.SetComponentData(waveEntities[0], wave);
        }

        private void SendWave(EntityManager em,
            NativeArray<LocalTransform> nodeTransforms,
            ref Random random, byte phase, int nodeCount)
        {
            using var halls = _hallQuery.ToEntityArray(Allocator.Temp);
            using var hallFactions = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Collect valid targets sorted by distance to nearest crystal node
            var targets = new NativeList<float3>(Allocator.Temp);
            var targetDists = new NativeList<float>(Allocator.Temp);

            for (int i = 0; i < halls.Length; i++)
            {
                if (hallFactions[i].Value == Faction.White) continue;
                float minDist = float.MaxValue;
                for (int n = 0; n < nodeTransforms.Length; n++)
                {
                    float d = math.distance(hallTransforms[i].Position, nodeTransforms[n].Position);
                    if (d < minDist) minDist = d;
                }
                targets.Add(hallTransforms[i].Position);
                targetDists.Add(minDist);
            }

            // If no halls found, target any non-Crystal building
            if (targets.Length == 0)
            {
                var buildingQuery = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<BuildingTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>()
                );
                using var buildings = buildingQuery.ToEntityArray(Allocator.Temp);
                using var buildingFactions = buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var buildingTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

                for (int i = 0; i < buildings.Length; i++)
                {
                    if (buildingFactions[i].Value == Faction.White) continue;
                    float minDist = float.MaxValue;
                    for (int n2 = 0; n2 < nodeTransforms.Length; n2++)
                    {
                        float d = math.distance(buildingTransforms[i].Position, nodeTransforms[n2].Position);
                        if (d < minDist) minDist = d;
                    }
                    targets.Add(buildingTransforms[i].Position);
                    targetDists.Add(minDist);
                    if (targets.Length >= 3) break; // Cap to nearest 3
                }
            }

            if (targets.Length == 0) { targets.Dispose(); targetDists.Dispose(); return; }

            // Sort by distance (nearest first)
            for (int i = 0; i < targets.Length - 1; i++)
            {
                int minIdx = i;
                for (int j = i + 1; j < targets.Length; j++)
                    if (targetDists[j] < targetDists[minIdx]) minIdx = j;
                if (minIdx != i)
                {
                    (targets[i], targets[minIdx]) = (targets[minIdx], targets[i]);
                    (targetDists[i], targetDists[minIdx]) = (targetDists[minIdx], targetDists[i]);
                }
            }

            int maxTargets = phase == 0 ? 1 : phase == 1 ? 2 : targets.Length;
            int targetCount = math.min(maxTargets, targets.Length);

            // Gather ALL idle crystal units
            using var units = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var unitTransforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var idleUnits = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < units.Length; i++)
            {
                bool isIdle = true;
                if (em.HasComponent<DesiredDestination>(units[i]))
                {
                    var dest = em.GetComponentData<DesiredDestination>(units[i]);
                    if (dest.Has == 1) isIdle = false;
                }
                if (isIdle && em.HasComponent<Target>(units[i]))
                {
                    var target = em.GetComponentData<Target>(units[i]);
                    if (target.Value != Entity.Null && em.Exists(target.Value)) isIdle = false;
                }
                if (isIdle) idleUnits.Add(i);
            }

            if (idleUnits.Length < 3)
            { idleUnits.Dispose(); targets.Dispose(); targetDists.Dispose(); return; }

            float waveFraction = math.min(0.9f, 0.5f + nodeCount * 0.05f);
            int waveSize = math.max(5, (int)(idleUnits.Length * waveFraction));
            waveSize = math.min(waveSize, idleUnits.Length);

            int unitsPerTarget = waveSize / targetCount;
            int remainder = waveSize % targetCount;
            int unitIdx = 0;

            for (int t = 0; t < targetCount && unitIdx < waveSize; t++)
            {
                int quota = unitsPerTarget + (t < remainder ? 1 : 0);
                float3 targetPos = targets[t];

                for (int q = 0; q < quota && unitIdx < waveSize; q++, unitIdx++)
                {
                    int idx = idleUnits[unitIdx];
                    if (em.HasComponent<DesiredDestination>(units[idx]))
                        em.SetComponentData(units[idx], new DesiredDestination { Position = targetPos, Has = 1 });
                    else
                        em.AddComponentData(units[idx], new DesiredDestination { Position = targetPos, Has = 1 });
                }
            }

            idleUnits.Dispose();
            targets.Dispose();
            targetDists.Dispose();
        }

        private void TryExpand(EntityManager em, float3 currentNodePos,
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

                expandPos = candidate;
                found = true;
                break;
            }

            if (!found) return;

            if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: ExpansionCost)))
            {
                CrystalMainNode.Create(em, expandPos);
                crystalBank -= ExpansionCost;
            }
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
