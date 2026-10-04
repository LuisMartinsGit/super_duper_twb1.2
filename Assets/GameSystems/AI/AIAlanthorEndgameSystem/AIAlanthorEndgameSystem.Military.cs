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
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagLocalTransformFactionTagHealth =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTagLocalTransformFactionTagHealth;

        #endregion

        // ──────────────────────────────────────────────────────────────────
        // 7. ARMOURED-UNIT PRODUCTION
        // ──────────────────────────────────────────────────────────────────

        // REMOVED 2026-10-03: this pass queued a Cataphract (else an
        // Outrider) and a Trebuchet (else a Ballista) every think, beside and
        // ahead of SimpleAISystem's composition picker. It was a second unit
        // picker — the very thing AIComposition.cs exists to forbid — and it
        // spent the scarce veilstone first-come on whichever unit it named,
        // with no line targets, no siege variety and no King Lexor priority.
        // Cavalry and siege are now lines of the Alanthor army plan
        // (SimpleAISystem.Composition.cs), paid from the army's veilstone
        // earmark (AIBudget). Sect units (below) stay here: they are per-sect
        // uniques, not composition.
        // ──────────────────────────────────────────────────────────────────
        // 8. WORKER FLEE
        // ──────────────────────────────────────────────────────────────────

        // For every worker of this faction, scan for an enemy unit
        // within FleeRadius and — if found — issue a MoveCommand toward
        // the Hall. Throttled per-worker via FleeCooldownState so we don't
        // override a fresh order on the same tick.
        /// <summary>Host-local flee-reissue cooldowns (see catch #13 note in
        /// the body — never a component on the worker). Entries for dead
        /// workers are harmless stale keys; the map is tiny.</summary>
        private static readonly System.Collections.Generic.Dictionary<Entity, float>
            _fleeRetryAt = new();

        private static void HandleWorkerFlee(Faction faction, EntityManager em,
            float3 hallPos, float time)
        {
            // Collect enemy unit positions once per tick.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            {
                var enemyQuery = QC_UnitTagLocalTransformFactionTagHealth.Get(em, QT_UnitTagLocalTransformFactionTagHealth);
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

            float fleeRadiusSq = Cfg.fleeRadius * Cfg.fleeRadius;

            // Process workers.
            FleeWorkers<WorkerTag>(em, faction, enemyPositions, hallPos, fleeRadiusSq, time);
            // Process workers (CanBuild marker is what SimpleAISystem queries).
            FleeWorkers<CanBuild>(em, faction, enemyPositions, hallPos, fleeRadiusSq, time);

            enemyPositions.Dispose();
        }

        private static void FleeWorkers<TWorkerTag>(EntityManager em, Faction faction,
            NativeList<float3> enemyPositions, float3 hallPos, float fleeRadiusSq, float time)
            where TWorkerTag : unmanaged, IComponentData
        {
            var workerQuery = AIQueryCache.TagFactionXf<TWorkerTag>(em);
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
                // HOST-LOCAL bookkeeping, NOT a component on the worker
                // (2026-09-04, MP harness catch #13): this system is
                // host-gated, so AddComponentData here was a host-only
                // STRUCTURAL change on a networked sim entity — reshuffling
                // the host's chunk order (the catch-#6 enabler) for pure AI
                // bookkeeping.
                const float FleeReissueInterval = 4f;
                if (_fleeRetryAt.TryGetValue(worker, out float retryAt) && time < retryAt)
                    continue;
                _fleeRetryAt[worker] = time + FleeReissueInterval;

                // NO DIRECT STATE SURGERY (catch #13, the fork itself). This
                // host-gated system cleared WorkerState and REMOVED BuildOrder
                // directly — on the host alone. The clients kept building for
                // the ticks until the routed move below executed, so the
                // construction site's progress forked permanently (tick
                // 18019: host worker WorkTarget -1, client still on the site,
                // site hp 234 vs 235). The move command's own execution runs
                // CommandCleanup.ClearWorkOrders on EVERY peer at the SAME
                // tick — that is the only legal way to strip a sim order.

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
