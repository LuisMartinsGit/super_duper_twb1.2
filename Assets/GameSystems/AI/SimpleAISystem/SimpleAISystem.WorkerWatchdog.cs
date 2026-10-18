// SimpleAISystem.WorkerWatchdog.cs
// A worker that holds a build order and gets nowhere is released
// (2026-10-17, docs/Design/Game_AI.md § 3c).
//
// The AI counts a worker as busy while it carries a BuildCommand or a
// BuildOrder (AICommon.IsCommittedWorker). On Fiendstone Pass a whole crew
// finished a Gatherer's Hut, then stood on the spot for thirty minutes still
// holding orders that never advanced: "idle crew 0 of 3, 3 sites open" every
// minute, no Mine, no landmark, no age-up, and the faction died with ten
// thousand supplies in the bank. Whatever wedges an order, the AI must not
// wait on it forever: a committed worker that neither moves nor adds build
// progress for workerStallSeconds has its orders cleared (a Stop, through the
// router like any AI order), so it counts as idle and is dispatched afresh.
//
// Partial of SimpleAISystem.cs.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private struct WorkerWatch
        {
            public float3 Pos;
            public float Progress;
            public float Since;
        }

        private readonly Dictionary<Entity, WorkerWatch> _workerWatch = new Dictionary<Entity, WorkerWatch>(64);
        private readonly List<Entity> _workerWatchStale = new List<Entity>(16);

        /// <summary>Release this faction's committed workers that have made no
        /// progress for workerStallSeconds. Cheap: one pass over the
        /// faction's workers, a few lookups each.</summary>
        private void TickWorkerWatchdog(EntityManager em, Faction faction, float now)
        {
            float stall = Cfg.workerStallSeconds;
            if (stall <= 0f) return;

            var q = AIQueryCache.TagFactionXf<CanBuild>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var w = ents[i];
                // Only build work is watched: a Feraldis crew on an extractor
                // stands still by design.
                bool building = TransientState.Active<BuildCommand>(em, w) || TransientState.Active<BuildOrder>(em, w);
                if (!building) { _workerWatch.Remove(w); continue; }

                float progress = BuildProgressOf(em, w);
                float3 pos = xfs[i].Position;
                if (!_workerWatch.TryGetValue(w, out var watch)
                    || math.distancesq(watch.Pos.xz, pos.xz) > 1.5f * 1.5f
                    || progress > watch.Progress + 0.01f)
                {
                    _workerWatch[w] = new WorkerWatch { Pos = pos, Progress = progress, Since = now };
                    continue;
                }
                if (now - watch.Since < stall) continue;

                CommandRouter.IssueStop(em, w, CommandSource.AI);
                _workerWatch.Remove(w);
                AILogger.Log(faction, "WORKER",
                    $"stalled {now - watch.Since:0}s at ({pos.x:0},{pos.z:0}) on a build order with no progress — released");
            }

            // Forget workers that died or changed hands.
            _workerWatchStale.Clear();
            foreach (var kv in _workerWatch)
                if (!em.Exists(kv.Key)) _workerWatchStale.Add(kv.Key);
            for (int i = 0; i < _workerWatchStale.Count; i++) _workerWatch.Remove(_workerWatchStale[i]);
        }

        /// <summary>Construction progress of the site this worker's order names,
        /// or -1 when it names none yet (an unbroken plan).</summary>
        private static float BuildProgressOf(EntityManager em, Entity w)
        {
            Entity site = Entity.Null;
            if (TransientState.Active<BuildOrder>(em, w)) site = em.GetComponentData<BuildOrder>(w).Site;
            if (site == Entity.Null && TransientState.Active<BuildCommand>(em, w))
                site = em.GetComponentData<BuildCommand>(w).TargetBuilding;
            if (site == Entity.Null || !em.Exists(site) || !em.HasComponent<UnderConstruction>(site)) return -1f;
            return em.GetComponentData<UnderConstruction>(site).Progress;
        }
    }
}
