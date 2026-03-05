// File: Assets/Scripts/Systems/Crystal/CrystalAISystem.cs
// Standalone AI brain for the Crystal faction.
// Manages building, spawning, harassment, and expansion per CrystalMainNode.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using Cost = TheWaningBorder.Core.Cost;

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
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystalAISystem : ISystem
    {
        private float _decisionTimer;
        private const float DecisionInterval = 5.0f; // AI thinks every 5 seconds

        // Building costs (crystal)
        private const int ResourceNodeCost = 50;
        private const int TurretNodeCost = 100;
        private const int RestorationNodeCost = 120;
        private const int EnforcementNodeCost = 200;
        private const int SuppressionNodeCost = 200;
        private const int CrystallingCost = 35;
        private const int VeilstingerCost = 80;
        private const int GodsplinterCost = 350;
        private const int ExpansionCost = 2000;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalMainNodeTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _decisionTimer += SystemAPI.Time.DeltaTime;
            if (_decisionTimer < DecisionInterval) return;
            _decisionTimer = 0f;

            var em = state.EntityManager;

            // Get crystal bank balance
            if (!FactionEconomy.TryGetResources(em, Faction.White, out var resources))
                return;

            int crystalBank = resources.Crystal;

            // Get a random seed for this tick
            var random = new Unity.Mathematics.Random(
                (uint)(SystemAPI.Time.ElapsedTime * 1000 + 1));

            foreach (var (aiState, crystalNode, transform, entity) in SystemAPI
                .Query<RefRW<CrystalAIState>, RefRO<CrystalNode>, RefRO<LocalTransform>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                ref var ai = ref aiState.ValueRW;
                var nodePos = transform.ValueRO.Position;
                float spreadRadius = crystalNode.ValueRO.CurrentRingRadius;

                // Refresh bank balance (may have changed from previous node's spending)
                if (FactionEconomy.TryGetResources(em, Faction.White, out resources))
                    crystalBank = resources.Crystal;

                // Update phase based on spread and bank
                UpdatePhase(ref ai, spreadRadius, crystalBank);

                // === BUILD PHASE ===
                ai.BuildTimer -= DecisionInterval;
                if (ai.BuildTimer <= 0 && spreadRadius > 5f)
                {
                    TryBuildSubNode(ref state, em, ref ai, nodePos, spreadRadius,
                        ref random, ref crystalBank);
                    ai.BuildTimer = 25f + random.NextFloat(0, 10f); // 25-35s between builds
                }

                // === SPAWN PHASE ===
                ai.UnitSpawnTimer -= DecisionInterval;
                if (ai.UnitSpawnTimer <= 0)
                {
                    TrySpawnUnits(em, ref ai, nodePos, ref random, ref crystalBank);
                    ai.UnitSpawnTimer = 10f + random.NextFloat(0, 5f); // 10-15s between spawns
                }

                // === HARASSMENT PHASE ===
                ai.HarassTimer -= DecisionInterval;
                if (ai.HarassTimer <= 0)
                {
                    TrySendHarassWave(em, nodePos, ref random);
                    ai.HarassTimer = 120f + random.NextFloat(0, 60f); // 120-180s between waves
                }

                // === EXPANSION PHASE ===
                if (crystalBank >= ExpansionCost)
                {
                    TryExpand(em, nodePos, ref random, ref crystalBank);
                }
            }
        }

        private void UpdatePhase(ref CrystalAIState ai, float spreadRadius, int crystalBank)
        {
            if (spreadRadius < 10f || crystalBank < 100)
                ai.Phase = 0; // Early
            else if (spreadRadius < 20f || crystalBank < 500)
                ai.Phase = 1; // Mid
            else
                ai.Phase = 2; // Late
        }

        private void TryBuildSubNode(ref SystemState state, EntityManager em,
            ref CrystalAIState ai, float3 nodePos, float spreadRadius,
            ref Random random, ref int crystalBank)
        {
            // Find a position within cursed area
            float3 buildPos = FindCursedPosition(nodePos, spreadRadius, ref random);
            if (buildPos.x == float.MinValue) return; // No valid position found

            // Count existing sub-nodes of each type
            int resourceCount = 0, turretCount = 0, restorationCount = 0;
            int enforcementCount = 0, suppressionCount = 0;

            foreach (var subNode in SystemAPI.Query<RefRO<CrystalSubNodeTag>>())
            {
                switch (subNode.ValueRO.Type)
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
                float roll = random.NextFloat(0, 1);

                if (ai.Phase >= 2 && roll < 0.1f && crystalBank >= GodsplinterCost)
                {
                    // 10% chance for Godsplinter in late game
                    if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: GodsplinterCost)))
                    {
                        float3 spawnPos = nodePos + new float3(
                            random.NextFloat(-3, 3), 0, random.NextFloat(-3, 3));
                        Godsplinter.Create(em, spawnPos, Faction.White);
                        crystalBank -= GodsplinterCost;
                    }
                }
                else if (ai.Phase >= 1 && roll < 0.35f && crystalBank >= VeilstingerCost)
                {
                    // 25% chance for Veilstinger in mid/late
                    if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: VeilstingerCost)))
                    {
                        float3 spawnPos = nodePos + new float3(
                            random.NextFloat(-3, 3), 0, random.NextFloat(-3, 3));
                        Veilstinger.Create(em, spawnPos, Faction.White);
                        crystalBank -= VeilstingerCost;
                    }
                }
                else if (crystalBank >= CrystallingCost)
                {
                    // Default: Crystalling
                    if (FactionEconomy.Spend(em, Faction.White, Cost.Of(crystal: CrystallingCost)))
                    {
                        float3 spawnPos = nodePos + new float3(
                            random.NextFloat(-3, 3), 0, random.NextFloat(-3, 3));
                        Crystalling.Create(em, spawnPos, Faction.White);
                        crystalBank -= CrystallingCost;
                    }
                }
            }
        }

        private void TrySendHarassWave(EntityManager em, float3 nodePos, ref Random random)
        {
            // Find nearest non-crystal Hall
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var halls = hallQuery.ToEntityArray(Allocator.Temp);
            using var hallFactions = hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

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
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalUnitTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var units = unitQuery.ToEntityArray(Allocator.Temp);
            using var unitTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Count nearby units first
            int nearUnits = 0;
            for (int i = 0; i < units.Length; i++)
            {
                float dist = math.distance(nodePos, unitTransforms[i].Position);
                if (dist <= 30f) nearUnits++;
            }

            if (nearUnits == 0) return;

            // Send 30-60% of nearby units
            int waveSize = (int)(nearUnits * random.NextFloat(0.3f, 0.6f));
            if (waveSize < 2) waveSize = math.min(2, nearUnits); // Minimum wave of 2

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
            var nodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            using var nodeTransforms = nodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float spawnRange = 80f; // Try within 80 units of current node
            bool found = false;
            float3 expandPos = float3.zero;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                float dist = random.NextFloat(40, spawnRange);
                float3 candidate = currentNodePos + new float3(
                    math.cos(angle) * dist, 0, math.sin(angle) * dist);

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
            // Pick random point within spread radius
            // Cursed ground fills from center to CurrentRingRadius
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2);
                float dist = random.NextFloat(3f, math.max(4f, spreadRadius * 0.8f));
                float3 candidate = nodePos + new float3(
                    math.cos(angle) * dist, 0, math.sin(angle) * dist);
                candidate.y = nodePos.y; // Keep same height as node

                // Position is valid if within the spread radius
                if (dist <= spreadRadius)
                    return candidate;
            }

            return new float3(float.MinValue, 0, 0); // Signal failure
        }
    }
}
