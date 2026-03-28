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
                ComponentType.ReadOnly<CrystalSubNodeTag>()
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

            for (int n = 0; n < entities.Length; n++)
            {
                var ai = aiStates[n];
                var nodePos = transforms[n].Position;
                float spreadRadius = crystalNodes[n].CurrentRingRadius;
                int nodeLevel = nodeLevels[n].Value;

                // Refresh bank balance (may have changed from previous node's spending)
                if (FactionEconomy.TryGetResources(em, Faction.White, out resources))
                    crystalBank = resources.Crystal;

                // Drive AI phase from per-node level (level 1→phase 0, level 2→phase 1, level 3→phase 2)
                ai.Phase = (byte)math.max(0, nodeLevel - 1);

                // === BUILD PHASE (sub-buildings) ===
                // Rate limited only by crystal bank — fixed short interval
                ai.BuildTimer -= DecisionInterval;
                if (ai.BuildTimer <= 0 && spreadRadius > 5f)
                {
                    TryBuildSubNode(em, ref ai, nodePos, spreadRadius,
                        ref random, ref crystalBank);
                    ai.BuildTimer = 15f + random.NextFloat(0, 5f); // 15-20s between builds
                }

                // === SPAWN PHASE (units) ===
                // Rate limited only by crystal bank — fixed short interval
                ai.UnitSpawnTimer -= DecisionInterval;
                if (ai.UnitSpawnTimer <= 0)
                {
                    TrySpawnUnits(em, ref ai, nodePos, ref random, ref crystalBank);
                    ai.UnitSpawnTimer = 15f + random.NextFloat(0, 10f); // 15-25s between spawns
                }

                // === TERRITORIAL AGGRESSION ===
                AttackIntruders(em, nodePos, spreadRadius);

                // === HARASSMENT PHASE ===
                ai.HarassTimer -= DecisionInterval;
                if (ai.HarassTimer <= 0)
                {
                    TrySendHarassWave(em, nodePos, ref random);
                    ai.HarassTimer = 20f + random.NextFloat(0, 20f); // 20-40s between waves
                }

                // === EXPANSION PHASE (new curse nodes) ===
                // Ever-expanding cooldown: fast early, slows over time, max 16 nodes
                ai.ExpansionTimer -= DecisionInterval;
                if (ai.ExpansionTimer <= 0 && crystalBank >= ExpansionCost)
                {
                    // Count existing curse nodes — hard cap at 16
                    int curseNodeCount = entities.Length;
                    if (curseNodeCount < MaxCurseNodes)
                    {
                        TryExpand(em, nodePos, ref random, ref crystalBank);

                        // Cooldown grows with elapsed time
                        float elapsedMinutes = (float)(World.Time.ElapsedTime / 60.0);
                        float expansionInterval = math.min(MaxExpansionInterval,
                            BaseExpansionInterval + ExpansionSlowdownRate * math.log2(1f + elapsedMinutes));
                        ai.ExpansionTimer = expansionInterval + random.NextFloat(0, expansionInterval * 0.3f);
                    }
                }

                // Write back modified AI state
                em.SetComponentData(entities[n], ai);
            }
        }

        private void TryBuildSubNode(EntityManager em,
            ref CrystalAIState ai, float3 nodePos, float spreadRadius,
            ref Random random, ref int crystalBank)
        {
            // Find a position within cursed area
            float3 buildPos = FindCursedPosition(nodePos, spreadRadius, ref random);
            if (buildPos.x == float.MinValue) return; // No valid position found

            // Count existing sub-nodes of each type
            int resourceCount = 0, turretCount = 0, restorationCount = 0;
            int enforcementCount = 0, suppressionCount = 0;

            using var subNodes = _subNodeQuery.ToComponentDataArray<CrystalSubNodeTag>(Allocator.Temp);
            for (int i = 0; i < subNodes.Length; i++)
            {
                switch (subNodes[i].Type)
                {
                    case CrystalSubNodeType.Resource: resourceCount++; break;
                    case CrystalSubNodeType.Turret: turretCount++; break;
                    case CrystalSubNodeType.Restoration: restorationCount++; break;
                    case CrystalSubNodeType.Enforcement: enforcementCount++; break;
                    case CrystalSubNodeType.Suppression: suppressionCount++; break;
                }
            }

            // Build decision tree
            // Priority: Resource > Turret > Restoration > Enforcement > Suppression
            if (resourceCount < 3 && crystalBank >= ResourceNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: ResourceNodeCost)))
                {
                    CrystalResourceNode.Create(em, buildPos);
                    crystalBank -= ResourceNodeCost;
                }
            }
            else if (turretCount < resourceCount + 1 && crystalBank >= TurretNodeCost)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: TurretNodeCost)))
                {
                    CrystalTurretNode.Create(em, buildPos);
                    crystalBank -= TurretNodeCost;
                }
            }
            else if (restorationCount < 1 && crystalBank >= RestorationNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: RestorationNodeCost)))
                {
                    CrystalRestorationNode.Create(em, buildPos);
                    crystalBank -= RestorationNodeCost;
                }
            }
            else if (enforcementCount < 1 && crystalBank >= EnforcementNodeCost && ai.Phase >= 1)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: EnforcementNodeCost)))
                {
                    CrystalEnforcementNode.Create(em, buildPos);
                    crystalBank -= EnforcementNodeCost;
                }
            }
            else if (suppressionCount < 1 && crystalBank >= SuppressionNodeCost && ai.Phase >= 2)
            {
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: SuppressionNodeCost)))
                {
                    CrystalSuppressionNode.Create(em, buildPos);
                    crystalBank -= SuppressionNodeCost;
                }
            }
            else if (crystalBank >= ResourceNodeCost)
            {
                // Default: build more resource nodes
                if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: ResourceNodeCost)))
                {
                    CrystalResourceNode.Create(em, buildPos);
                    crystalBank -= ResourceNodeCost;
                }
            }
        }

        private void TrySpawnUnits(EntityManager em, ref CrystalAIState ai,
            float3 nodePos, ref Random random, ref int crystalBank)
        {
            int unitsToSpawn = 1 + ai.Phase; // 1 in early, 2 in mid, 3 in late

            for (int i = 0; i < unitsToSpawn; i++)
            {
                // Find valid spawn position near node
                float3 spawnPos = FindValidSpawnPos(nodePos, ref random);
                if (spawnPos.x == float.MinValue) continue;

                float roll = random.NextFloat(0, 1);

                if (ai.Phase >= 2 && roll < 0.2f && crystalBank >= GodsplinterCost)
                {
                    if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: GodsplinterCost)))
                    {
                        Godsplinter.Create(em, spawnPos, Faction.White);
                        crystalBank -= GodsplinterCost;
                    }
                }
                else if (ai.Phase >= 1 && roll < 0.35f && crystalBank >= VeilstingerCost)
                {
                    if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: VeilstingerCost)))
                    {
                        Veilstinger.Create(em, spawnPos, Faction.White);
                        crystalBank -= VeilstingerCost;
                    }
                }
                else if (crystalBank >= CrystallingCost)
                {
                    if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: CrystallingCost)))
                    {
                        Crystalling.Create(em, spawnPos, Faction.White);
                        crystalBank -= CrystallingCost;
                    }
                }
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

        private void TrySendHarassWave(EntityManager em, float3 nodePos, ref Random random)
        {
            // Find nearest non-crystal Hall
            using var halls = _hallQuery.ToEntityArray(Allocator.Temp);
            using var hallFactions = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity targetHall = Entity.Null;
            float bestDist = float.MaxValue;
            float3 targetPos = float3.zero;

            for (int i = 0; i < halls.Length; i++)
            {
                if (hallFactions[i].Value == Faction.White) continue; // Don't attack self
                float dist = math.distance(nodePos, hallTransforms[i].Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    targetHall = halls[i];
                    targetPos = hallTransforms[i].Position;
                }
            }

            if (targetHall == Entity.Null) return;

            // Gather idle crystal units near this node (within 30 units)
            using var units = _crystalUnitQuery.ToEntityArray(Allocator.Temp);
            using var unitTransforms = _crystalUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Count nearby units first
            int nearUnits = 0;
            for (int i = 0; i < units.Length; i++)
            {
                float dist = math.distance(nodePos, unitTransforms[i].Position);
                if (dist <= 30f) nearUnits++;
            }

            if (nearUnits == 0) return;

            // Send 50-80% of nearby units — aggressive waves
            int waveSize = (int)(nearUnits * random.NextFloat(0.5f, 0.8f));
            if (waveSize < 3) waveSize = math.min(3, nearUnits); // Minimum wave of 3

            int sentUnits = 0;
            for (int i = 0; i < units.Length && sentUnits < waveSize; i++)
            {
                float dist = math.distance(nodePos, unitTransforms[i].Position);
                if (dist > 30f) continue;

                // Set move destination toward target hall
                if (em.HasComponent<DesiredDestination>(units[i]))
                {
                    em.SetComponentData(units[i], new DesiredDestination
                    {
                        Position = targetPos,
                        Has = 1
                    });
                }
                else
                {
                    em.AddComponentData(units[i], new DesiredDestination
                    {
                        Position = targetPos,
                        Has = 1
                    });
                }

                sentUnits++;
            }
        }

        private void TryExpand(EntityManager em, float3 currentNodePos,
            ref Random random, ref int crystalBank)
        {
            // Get all existing main node positions to avoid placing too close
            using var nodeTransforms = _mainNodeTransformQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float spawnRange = 80f; // Try within 80 units of current node
            bool found = false;
            float3 expandPos = float3.zero;

            var grid = PassabilityGrid.Instance;
            int half = GameSettings.MapHalfSize;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                float dist = random.NextFloat(40, spawnRange);
                float3 candidate = currentNodePos + new float3(
                    math.cos(angle) * dist, 0, math.sin(angle) * dist);

                // Must be within map bounds
                if (math.abs(candidate.x) > half || math.abs(candidate.z) > half)
                    continue;

                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                // Must be on passable terrain
                if (grid != null && !grid.IsPassable(candidate))
                    continue;

                // Check distance from all existing main nodes (min 50 apart)
                bool tooClose = false;
                for (int n = 0; n < nodeTransforms.Length; n++)
                {
                    if (math.distance(candidate, nodeTransforms[n].Position) < 50f)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                expandPos = candidate;
                found = true;
                break;
            }

            if (!found) return;

            // Spend and create
            if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: ExpansionCost)))
            {
                CrystalMainNode.Create(em, expandPos);
                crystalBank -= ExpansionCost;
            }
        }

        /// <summary>
        /// Find a random position within the cursed area around a node.
        /// Picks a point within the current spread radius.
        /// </summary>
        private float3 FindCursedPosition(float3 nodePos, float spreadRadius,
            ref Random random)
        {
            var grid = PassabilityGrid.Instance;
            int half = GameSettings.MapHalfSize;

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

                if (dist <= spreadRadius)
                    return candidate;
            }

            return new float3(float.MinValue, 0, 0); // Signal failure
        }
    }
}
