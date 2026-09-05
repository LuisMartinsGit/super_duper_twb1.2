// AICommon.cs
// Helpers every AI system shares — culture-neutral AND phase-neutral.
//
// AIEndgameCommon exists for the same reason one level down: it holds what the
// two ENDGAME systems share. These are the ones that SimpleAISystem shares with
// them, so they cannot live there.
//
// Each of these was a copy-paste pair before 2026-09-03, and every pair had
// drifted. The worst was DispatchBuildersTo: the endgame copy called a worker
// "idle" when it had no BuildOrder, while SimpleAISystem also excluded workers
// carrying an in-flight BuildCommand or a RepairOrder. Since the endgame
// systems run [UpdateAfter(SimpleAISystem)] in the SAME frame on the SAME
// faction, SimpleAI's fresh BuildCommand had not become a BuildOrder yet — so
// the endgame re-dispatched a worker that was already on its way somewhere
// else, every tick it wanted a building.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    /// <summary>Helpers shared by every AI system, whatever culture or phase.</summary>
    public static class AICommon
    {
        static readonly ComponentType[] BuilderTypes =
        {
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery _builderQuery;

        static readonly ComponentType[] TrainQueueTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<TrainQueueItem>(),
        };
        static CachedEntityQuery _trainQueueQuery;

        #region Workers

        /// <summary>
        /// A worker that must not be pulled onto a new site.
        ///
        /// BuildCommand is the one that matters and the one the drifted copy
        /// missed: it is the order in flight, before the command system has
        /// turned it into a BuildOrder. Testing only BuildOrder means every
        /// worker dispatched this frame still looks idle.
        /// </summary>
        public static bool IsCommittedWorker(EntityManager em, Entity worker)
            => em.HasComponent<BuildCommand>(worker)
            || em.HasComponent<BuildOrder>(worker)
            || em.HasComponent<RepairOrder>(worker);

        /// <summary>
        /// Count the faction's idle builders. Cheap O(N) snapshot used as a
        /// pre-flight gate so a caller doesn't spend resources on a foundation
        /// that no builder will ever pick up. (task-062 G-2)
        /// </summary>
        public static int CountIdleBuilders(EntityManager em, Faction faction)
        {
            var query = _builderQuery.Get(em, BuilderTypes);
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (IsCommittedWorker(em, ents[i])) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Find up to <paramref name="maxBuilders"/> idle builders of the given
        /// faction and issue BuildCommand on each, pointing at
        /// <paramref name="site"/>, nearest first.
        /// </summary>
        /// <returns>Number of builders actually dispatched (0 = nobody available).</returns>
        public static int DispatchBuildersTo(EntityManager em, Faction faction, Entity site,
            string buildingId, float3 sitePos, int maxBuilders)
        {
            var query = _builderQuery.Get(em, BuilderTypes);
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs  = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var idle = new List<Candidate>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (IsCommittedWorker(em, ents[i])) continue;
                float dx = xfs[i].Position.x - sitePos.x;
                float dz = xfs[i].Position.z - sitePos.z;
                idle.Add(new Candidate { Entity = ents[i], DistSq = dx * dx + dz * dz });
            }

            idle.Sort((a, c) => a.DistSq.CompareTo(c.DistSq));

            int dispatched = 0;
            for (int i = 0; i < idle.Count && dispatched < maxBuilders; i++)
            {
                // CommandSource.AI, not the LocalPlayer default — mislabeled
                // AI orders ride the player's command stream. (audit F20)
                CommandRouter.IssueBuild(em, idle[i].Entity, site, buildingId, sitePos,
                    CommandSource.AI);
                dispatched++;
            }
            return dispatched;
        }

        struct Candidate
        {
            public Entity Entity;
            public float DistSq;
        }

        #endregion

        #region Production

        /// <summary>
        /// Is this unit already queued anywhere the faction owns?
        ///
        /// Queries by the TRAIN QUEUE rather than by BuildingTag: the two
        /// copies of this disagreed on exactly that, and a queue is a queue
        /// wherever it hangs. Anything without the buffer cannot match anyway.
        /// </summary>
        public static bool IsUnitQueued(EntityManager em, Faction faction, string unitId)
        {
            var q = _trainQueueQuery.Get(em, TrainQueueTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].UnitId.ToString() == unitId) return true;
            }
            return false;
        }

        /// <summary>
        /// How many of this unit sit in the faction's train queues right now.
        /// A "have N?" check that counts only ALIVE units re-orders another
        /// every think tick until the first one spawns — the scout corps
        /// logged 10-13 trains against a target of 2-6 that way, 30 supplies
        /// each, out of the very pot the claim saver was trying to fill.
        /// </summary>
        public static int CountQueued(EntityManager em, Faction faction, string unitId)
        {
            var q = _trainQueueQuery.Get(em, TrainQueueTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].UnitId.ToString() == unitId) n++;
            }
            return n;
        }

        #endregion

        #region Costs

        /// <summary>The catalog's authored cost block as the economy's Cost.</summary>
        public static Cost ToCost(CostBlock block)
        {
            if (block == null) return default;
            return new Cost
            {
                Supplies  = block.Supplies,
                Iron      = block.Iron,
                Veilstone = block.Veilstone,
                Veilsteel = block.Veilsteel,
            };
        }

        #endregion
    }
}
