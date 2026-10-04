// AICommon.cs
// Helpers every AI system shares — culture-neutral AND phase-neutral.
//
// AIEndgameCommon exists for the same reason one level down: it holds what the
// two ENDGAME systems share. These are the ones that SimpleAISystem shares with
// them, so they cannot live there.
//
// Each of these was a copy-paste pair before 2026-09-03, and every pair had
// drifted. The worst was DispatchWorkersTo: the endgame copy called a worker
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
        /// <summary>
        /// HAS THIS FACTION EVER SEEN THIS GROUND? The AI's single fog-honest
        /// test (explored-map memory, not live vision: a node a scout found
        /// stays known after the scout leaves). Every lookup of map features
        /// the AI could not otherwise know — resource nodes, curse nodes — goes
        /// through it, so the AI discovers the map the way a player does.
        /// Fail-open when there is no fog manager (fog off, scenarios).
        /// </summary>
        /// <summary>
        /// CHAINED ORDERS — the AI's Shift+right-click. The first step is
        /// issued as an ordinary order (which also clears any old queue); the
        /// rest are appended to the unit's command queue through the same
        /// replicated path a player's Shift+click uses
        /// (CommandRouter.IssueQueuedWaypoint), and CommandQueueSystem runs
        /// them in order as each one completes. Lets the AI hand a unit a
        /// whole route in one decision instead of re-ordering it on arrival —
        /// no idle tick between legs. An attack-move step waits out any fight
        /// it picks up before the next step starts.
        /// </summary>
        public static void IssueChain(EntityManager em, Entity unit,
            System.Collections.Generic.IReadOnlyList<(QueuedCommandType type, float3 pos)> steps)
        {
            if (steps == null || steps.Count == 0 || unit == Entity.Null || !em.Exists(unit)) return;

            // A clean slate first: whatever the unit was doing — an old queue
            // included — is replaced by this route. (Stop keeps the stance.)
            CommandRouter.IssueStop(em, unit, CommandSource.AI);

            var first = steps[0];
            switch (first.type)
            {
                case QueuedCommandType.AttackMove:
                    CommandRouter.IssueAttackMove(em, unit, first.pos, CommandSource.AI); break;
                case QueuedCommandType.Patrol:
                    CommandRouter.IssuePatrol(em, unit, first.pos, CommandSource.AI); break;
                default:
                    CommandRouter.IssueMove(em, unit, first.pos, CommandSource.AI); break;
            }
            for (int i = 1; i < steps.Count; i++)
                CommandRouter.IssueQueuedWaypoint(em, unit, steps[i].type, steps[i].pos,
                    Entity.Null, CommandSource.AI);
        }

        /// <summary>
        /// A GROUP ORDER MARCHES IN FORMATION (2026-10-03, docs/Design/Game_AI.md
        /// § 6 Formation movement). Every AI decision that sends two or more
        /// soldiers to ONE destination goes through here, so they travel as
        /// one formation (virtual leader, type-ranked slots, slowest-member
        /// speed) exactly like an attack wave or a player's group order —
        /// not as a stream of per-unit orders, which is what the religion
        /// hunt, the reclaim squad and the claim squads used to send (traces
        /// measured those groups at a p90 nearest-neighbour gap of 44 m).
        /// A group of one is an ordinary per-unit order. Lockstep: the
        /// formation commands replicate as one FormationOrder, the same path
        /// the waves use (CommandRouter.Formation).
        /// Callers must not re-issue this every think to units already
        /// marching on it — each call re-plans the whole formation.
        /// </summary>
        public static void IssueGroupOrder(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, bool attackMove)
        {
            if (units == null || units.Count == 0) return;
            if (units.Count == 1)
            {
                if (attackMove) CommandRouter.IssueAttackMove(em, units[0], destination, CommandSource.AI);
                else CommandRouter.IssueMove(em, units[0], destination, CommandSource.AI);
                return;
            }
            if (attackMove)
                CommandRouter.IssueFormationAttackMove(em, units, destination, FormationShape.Box, CommandSource.AI);
            else
                CommandRouter.IssueFormationMove(em, units, destination, FormationShape.Box, CommandSource.AI);
        }

        /// <summary>
        /// <see cref="IssueGroupOrder(EntityManager, IReadOnlyList{Entity}, float3, bool)"/>
        /// for a draft that may include units ALREADY standing at the
        /// destination (within <paramref name="arrivedRadius"/>): those have
        /// nothing to march, so they get a plain per-unit re-poke — exactly
        /// what every unit used to get — and only the rest are planned into a
        /// formation. Planning arrivals in would re-slot them round the
        /// destination on every think that re-drafts them.
        /// </summary>
        public static void IssueGroupOrder(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, bool attackMove, float arrivedRadius)
        {
            if (units == null || units.Count == 0) return;
            var marchers = _groupMarchers;
            marchers.Clear();
            float r2 = arrivedRadius * arrivedRadius;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == Entity.Null || !em.Exists(u)) continue;
                bool arrived = em.HasComponent<LocalTransform>(u)
                    && math.distancesq(em.GetComponentData<LocalTransform>(u).Position.xz, destination.xz) <= r2;
                if (!arrived) { marchers.Add(u); continue; }
                if (attackMove) CommandRouter.IssueAttackMove(em, u, destination, CommandSource.AI);
                else CommandRouter.IssueMove(em, u, destination, CommandSource.AI);
            }
            // The router copies what it keeps (CommandRouter.Formation), so the
            // pooled list is safe to reuse.
            IssueGroupOrder(em, marchers, destination, attackMove);
        }

        /// <summary>Host scratch for <see cref="IssueGroupOrder(EntityManager, IReadOnlyList{Entity}, float3, bool, float)"/>
        /// (the AI think is main-thread only).</summary>
        private static readonly List<Entity> _groupMarchers = new List<Entity>();

        /// <summary>
        /// Members of an AI group travelling OUTSIDE a formation: under an
        /// order (DesiredDestination set) but with no FormationMemberState,
        /// and not fighting. These are the ones the plan's cohesion gate left
        /// out (too far from the centroid when the order went out) or stuck
        /// recovery dropped — the same count SimpleAISystem's wave straggler
        /// sweep uses.
        /// </summary>
        public static int CountLooseMembers(EntityManager em, IReadOnlyList<Entity> members)
        {
            int loose = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var u = members[i];
                if (!em.Exists(u) || em.HasComponent<FormationMemberState>(u)) continue;
                if (em.HasComponent<Target>(u) && em.GetComponentData<Target>(u).Value != Entity.Null) continue;
                if (em.HasComponent<DesiredDestination>(u)
                    && em.GetComponentData<DesiredDestination>(u).Has != 0) loose++;
            }
            return loose;
        }

        public static bool IsKnownGround(Faction faction, float3 pos)
        {
            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            return fog == null
                || fog.IsRevealed(faction, new UnityEngine.Vector3(pos.x, 0f, pos.z));
        }

        static readonly ComponentType[] WorkerTypes =
        {
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery _workerQuery;

        static readonly ComponentType[] TrainQueueTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<ProductionQueueItem>(),
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

        /// <summary>THE HOUSE QUARTER (2026-10-02): the centre of the faction's Houses (finished or rising);
        /// false — anchor left as <paramref name="fallback"/> — while it has
        /// none.</summary>
        public static bool TryHouseQuarterAnchor(EntityManager em, Faction faction,
            out float3 anchor, float3 fallback)
        {
            anchor = fallback;
            var q = AIQueryCache.TagFactionXf<HutTag>(em);
            if (q.IsEmptyIgnoreFilter) return false;
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float3 sum = float3.zero;
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                sum += xfs[i].Position;
                n++;
            }
            if (n == 0) return false;
            anchor = sum / n;
            return true;
        }

        /// <summary>
        /// Count the faction's idle workers. Cheap O(N) snapshot used as a
        /// pre-flight gate so a caller doesn't spend resources on a foundation
        /// that no worker will ever pick up. (task-062 G-2)
        /// </summary>
        public static int CountIdleWorkers(EntityManager em, Faction faction)
        {
            var query = _workerQuery.Get(em, WorkerTypes);
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
        /// Find up to <paramref name="maxWorkers"/> idle workers of the given
        /// faction and issue BuildCommand on each, pointing at
        /// <paramref name="site"/>, nearest first.
        /// </summary>
        /// <returns>Number of workers actually dispatched (0 = nobody available).</returns>
        public static int DispatchWorkersTo(EntityManager em, Faction faction, Entity site,
            string buildingId, float3 sitePos, int maxWorkers)
        {
            var query = _workerQuery.Get(em, WorkerTypes);
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
            for (int i = 0; i < idle.Count && dispatched < maxWorkers; i++)
            {
                // CommandSource.AI, not the LocalPlayer default — mislabeled
                // AI orders ride the player's command stream. (audit F20)
                CommandRouter.IssueBuild(em, idle[i].Entity, site, buildingId, sitePos,
                    CommandSource.AI);
                dispatched++;
            }
            return dispatched;
        }

        /// <summary>
        /// Give up to <paramref name="maxWorkers"/> BUSY workers
        /// <paramref name="site"/> as their next job, nearest first (ties by
        /// entity index, so lockstep peers agree). BuildCommandHelper queues a
        /// busy worker's new site behind its current one rather than replacing
        /// it, so nothing is abandoned; what this buys is a site that is
        /// certain to be picked up when no worker is idle. Only for an order
        /// that must not be orphaned — the lost-sole-trainer rebuild
        /// (docs/Design/Game_AI.md 6c).
        /// </summary>
        /// <returns>Number of workers given the site.</returns>
        public static int PullWorkersTo(EntityManager em, Faction faction, Entity site,
            string buildingId, float3 sitePos, int maxWorkers)
        {
            var query = _workerQuery.Get(em, WorkerTypes);
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs  = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var busy = new List<Candidate>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCommittedWorker(em, ents[i])) continue;
                float dx = xfs[i].Position.x - sitePos.x;
                float dz = xfs[i].Position.z - sitePos.z;
                busy.Add(new Candidate { Entity = ents[i], DistSq = dx * dx + dz * dz });
            }
            busy.Sort((a, c) =>
            {
                int d = a.DistSq.CompareTo(c.DistSq);
                return d != 0 ? d : a.Entity.Index.CompareTo(c.Entity.Index);
            });

            int pulled = 0;
            for (int i = 0; i < busy.Count && pulled < maxWorkers; i++)
            {
                CommandRouter.IssueBuild(em, busy[i].Entity, site, buildingId, sitePos,
                    CommandSource.AI);
                pulled++;
            }
            return pulled;
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
            // Compared as FixedStrings: a ToString per queue slot allocated a
            // managed string per item, per call, per think.
            var key = new FixedString64Bytes(unitId);
            var q = _trainQueueQuery.Get(em, TrainQueueTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].Kind == ProductionKind.Train
                        && buf[j].Id == key) return true;
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
            var key = new FixedString64Bytes(unitId);
            var q = _trainQueueQuery.Get(em, TrainQueueTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].Kind == ProductionKind.Train
                        && buf[j].Id == key) n++;
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
