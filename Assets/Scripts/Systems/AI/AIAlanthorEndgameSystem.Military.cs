// AIAlanthorEndgameSystem.Military.cs
// Armoured-unit production and worker flee behaviour.
// Partial of AIAlanthorEndgameSystem.cs -- split 2026-08-12 for readability.

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
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        // Worker flee tuning. Miners and builders run home if any enemy
        // unit is within FleeRadius. Throttled per worker so we don't
        // spam MoveCommands every tick once a threat is committed.
        private const float FleeRadius = 14f;
        // ──────────────────────────────────────────────────────────────────
        // 7. ARMOURED-UNIT PRODUCTION
        // ──────────────────────────────────────────────────────────────────

        // Push the armoured lines into their production buildings' queues.
        // Same pattern SimpleAISystem uses for Age-1 units; the cost is
        // charged inside TrainCommandDirect on every peer
        // (docs/Multiplayer_LAN_Readiness.md) — TryQueueAt only CHECKS
        // affordability, so we don't double-deduct.
        //   Stable    — Cataphract first (the heavy line), Outrider filler.
        //   SiegeYard — Trebuchet when its level gate opens, else Ballista.
        //     (Was "Alanthor_Catapult" — a UnitFactory ALIAS the TechCatalog
        //     does not carry, so TryGetUnit failed and the AI shipped ZERO
        //     siege in every match up to 2026-08-11. The catalog id is
        //     "Alanthor_Ballista".)
        // Infantry/archer lines stay with SimpleAISystem's composition
        // picker — the Barracks queue belongs to it.
        private static void TryQueueArmouredUnits(Faction faction, EntityManager em)
        {
            if (!TryQueueAt<RoyalStableTag>(em, faction, "Alanthor_Cataphract"))
                TryQueueAt<RoyalStableTag>(em, faction, "Alanthor_Outrider");
            if (!TryQueueAt<SiegeYardTag>(em, faction, "Alanthor_Trebuchet"))
                TryQueueAt<SiegeYardTag>(em, faction, "Alanthor_Ballista");
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
                    // Allies are not enemies. docs/Design/Teams.md
                    if (!Alliances.AreHostile(faction,
                            em.GetComponentData<FactionTag>(eEnts[i]).Value)) continue;
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

                // Only flee once actually HURT. Proximity-fleeing made
                // sneak-mining the well crystal fields impossible — workers
                // oscillated between their gather order and the flee order
                // ("walking away from their destination") and never mined.
                // Canon (§2.1): mining under threat is intended; the worker
                // runs when the curse actually bites.
                if (em.HasComponent<Health>(worker))
                {
                    var whp = em.GetComponentData<Health>(worker);
                    if (whp.Max <= 0 || whp.Value >= (int)(whp.Max * 0.8f)) continue;
                }

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
                CommandRouter.IssueMove(em, worker, to, CommandSource.AI);
            }
        }
    }
}
