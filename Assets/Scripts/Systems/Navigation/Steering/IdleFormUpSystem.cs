// IdleFormUpSystem.cs
// Idle soldiers standing near each other fall into ranks (2026-09-03
// directive: "idle military units close to each other should form up, AI or
// not"). A post-battle survivor blob, a rally-point pile, a recalled army
// milling at home — all read as a mob until something orders them; this pass
// is that something: it issues a formation move onto the cluster's own
// centroid, so the group arranges itself in place. FormationSlotMemory keeps
// slots stable across re-forms, so a settled group barely moves.
//
// Runs for EVERY faction, human included — the visual language of "an army"
// should not depend on who owns it. It respects the player's choices
// (docs/Design/Navigation_And_Formations.md §2.13): a cluster re-forms in the
// shape its units last marched in (FormationSlotMemory.Shape), the members of
// one formation (FormationSlotMemory.GroupKey) are always clustered together
// and are never "new faces" to each other, and Hold-stance units are never
// moved (docs/Design/Stances.md §1).
//
// What it must never touch:
//   * anything with an order or a target in flight (command follow-through);
//   * casters and support (Magic/Support classes) — a channelling ritualist
//     has no visible order, and a form-up shove would break its channel;
//   * clumps in or near a live fight — re-arranging mid-battle is throwing
//     the fight; the enemy probe skips those clusters entirely;
//   * uncontrollable units (Plunderers etc.).
//
// Host-only, like every order-issuing brain (the commands flow out as
// ordinary formation moves, same as the AI's).

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Navigation
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class IdleFormUpSystem : SystemBase
    {
        /// <summary>Seconds between form-up sweeps (per match, all factions).</summary>
        private const float SweepInterval = 4f;
        /// <summary>Per-faction cooldown between form-ups.</summary>
        private const float FactionCooldown = 12f;
        /// <summary>Units within this range of a seed cluster together.</summary>
        private const float ClusterRadius = 14f;
        /// <summary>Fewer than this is a picket, not an army — leave it be.</summary>
        private const int MinClusterSize = 4;
        /// <summary>Enemy presence within this range of the centroid vetoes
        /// the form-up (never shuffle ranks inside a fight).</summary>
        private const float EnemyVetoRadius = 40f;
        /// <summary>A member this far off its guard point has drifted — the
        /// cluster re-forms even with no new faces.</summary>
        private const float DriftTolerance = 4f;

        private SimCadence.Periodic _acc;

        // Host-side bookkeeping (mirrors SimpleAISystem._missions): entities
        // already standing in a formed cluster. A cluster re-forms only when
        // it gains a NEW face or a member drifts — without this, every sweep
        // re-issued the same order to the same settled group forever.
        private readonly Dictionary<int, HashSet<Entity>> _formed
            = new Dictionary<int, HashSet<Entity>>();
        private readonly Dictionary<int, float> _nextFormTime
            = new Dictionary<int, float>();

        static readonly ComponentType[] QT_Idle =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
            // An emplaced engine is bolted to a wall: never part of a cluster.
            ComponentType.Exclude<EmplacedEngineTag>(),
        };
        static CachedEntityQuery QC_Idle;

        protected override void OnUpdate()
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, SweepInterval)) return;

            var em = EntityManager;
            float now = (float)SystemAPI.Time.ElapsedTime;

            var q = QC_Idle.Get(em, QT_Idle);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);

            // Idle soldiers, bucketed per faction (deterministic entity order).
            var idleByFaction = new Dictionary<int, List<int>>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                var cls = tags[i].Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged
                    && cls != UnitClass.Siege) continue;

                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (em.HasComponent<NotControllableTag>(e)) continue;
                if (em.HasComponent<PlundererTag>(e)) continue;
                if (em.HasComponent<FormationMemberState>(e)) continue;
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<MoveCommand>(em, e)) continue;
                if (TransientState.Active<AttackMoveCommand>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                if (TransientState.Active<DeathAnimationState>(em, e)) continue;
                if (em.HasComponent<BuildCommand>(e)) continue;
                if (em.HasComponent<PatrolTag>(e)) continue;
                // Hold Position never moves on its own — not even to tidy up.
                if (em.HasComponent<HoldPositionTag>(e)) continue;
                if (em.HasComponent<Target>(e)
                    && em.GetComponentData<Target>(e).Value != Entity.Null) continue;
                if (em.HasComponent<DesiredDestination>(e)
                    && em.GetComponentData<DesiredDestination>(e).Has != 0) continue;

                int f = (int)facs[i].Value;
                if (!idleByFaction.TryGetValue(f, out var list))
                    idleByFaction[f] = list = new List<int>();
                list.Add(i);
            }

            foreach (var kv in idleByFaction)
            {
                int fKey = kv.Key;
                if (_nextFormTime.TryGetValue(fKey, out float next) && now < next) continue;

                var idle = kv.Value;
                if (idle.Count < MinClusterSize) continue;
                if (!_formed.TryGetValue(fKey, out var formedSet))
                    _formed[fKey] = formedSet = new HashSet<Entity>();

                // Greedy clustering in entity order — deterministic, O(n^2)
                // over a per-faction idle count that is small by definition.
                var assigned = new bool[idle.Count];
                bool issuedAny = false;
                for (int s = 0; s < idle.Count; s++)
                {
                    if (assigned[s]) continue;
                    float3 seedPos = xfs[idle[s]].Position;

                    var members = new List<Entity>();
                    var memberIdx = new List<int>();
                    for (int j = s; j < idle.Count; j++)
                    {
                        if (assigned[j]) continue;
                        float dx = xfs[idle[j]].Position.x - seedPos.x;
                        float dz = xfs[idle[j]].Position.z - seedPos.z;
                        if (dx * dx + dz * dz > ClusterRadius * ClusterRadius) continue;
                        assigned[j] = true;
                        members.Add(ents[idle[j]]);
                        memberIdx.Add(idle[j]);
                    }
                    // ONE FORMATION IS ONE CLUSTER. A formation wider than
                    // ClusterRadius (a 30-unit line is 60 m across) used to be
                    // cut in two by the seed-radius test and re-formed as two
                    // armies. Pull in every idle unit that marched in the same
                    // formation as anyone already gathered, transitively.
                    {
                        var keys = new HashSet<uint>();
                        bool grew = true;
                        while (grew)
                        {
                            grew = false;
                            for (int j = 0; j < memberIdx.Count; j++)
                            {
                                uint k = GroupKeyOf(em, ents[memberIdx[j]]);
                                if (k != 0) keys.Add(k);
                            }
                            if (keys.Count == 0) break;
                            for (int j = s + 1; j < idle.Count; j++)
                            {
                                if (assigned[j]) continue;
                                if (!keys.Contains(GroupKeyOf(em, ents[idle[j]]))) continue;
                                assigned[j] = true;
                                members.Add(ents[idle[j]]);
                                memberIdx.Add(idle[j]);
                                grew = true;
                            }
                        }
                    }
                    if (members.Count < MinClusterSize) continue;

                    float3 centroid = float3.zero;
                    for (int j = 0; j < memberIdx.Count; j++)
                        centroid += xfs[memberIdx[j]].Position;
                    centroid /= memberIdx.Count;

                    // Never shuffle ranks near a live fight.
                    if (TheWaningBorder.AI.TacticalQuery.EnemyStrengthInRadius(
                            em, (Faction)fKey, centroid, EnemyVetoRadius) > 0) continue;

                    // The formation most of this cluster last marched in, and
                    // the shape it marched in. Ties resolve to the key seen
                    // first, in the deterministic member order.
                    uint dominantKey = 0;
                    FormationShape shape = FormationShape.Box;
                    {
                        var counts = new Dictionary<uint, int>();
                        int best = 0;
                        for (int j = 0; j < members.Count; j++)
                        {
                            uint k = GroupKeyOf(em, members[j]);
                            if (k == 0) continue;
                            counts.TryGetValue(k, out int c);
                            counts[k] = ++c;
                            if (c > best)
                            {
                                best = c;
                                dominantKey = k;
                                shape = em.GetComponentData<FormationSlotMemory>(members[j]).Shape;
                            }
                        }
                    }

                    // Re-form only for a new face or a drifted member — a
                    // settled group stays settled. A member of the cluster's
                    // own just-arrived formation is NOT a new face even though
                    // this system has never formed it: it already stands in
                    // the shape the player gave it.
                    bool worthIt = false;
                    for (int j = 0; j < members.Count && !worthIt; j++)
                    {
                        var e = members[j];
                        bool known = formedSet.Contains(e)
                            || (dominantKey != 0 && GroupKeyOf(em, e) == dominantKey);
                        if (!known) { worthIt = true; break; }
                        if (em.HasComponent<GuardPoint>(e))
                        {
                            var gp = em.GetComponentData<GuardPoint>(e);
                            float ddx = xfs[memberIdx[j]].Position.x - gp.Position.x;
                            float ddz = xfs[memberIdx[j]].Position.z - gp.Position.z;
                            if (gp.Has == 0 || ddx * ddx + ddz * ddz
                                    > DriftTolerance * DriftTolerance)
                                worthIt = true;
                        }
                    }
                    if (!worthIt) continue;

                    // CommandSource.AI, NOT System: this system is host-gated
                    // (ShouldRunAIBrains), and System-source commands execute
                    // immediately on the assumption the issuer runs on EVERY
                    // peer. Issued as System, the form-up moved units on the
                    // host only — the first thing the MP desync harness ever
                    // caught (tick 120, Pos/Nav forked, banks agreed). AI
                    // source rides the lockstep queue like every other
                    // host-authoritative order.
                    CommandRouter.IssueFormationMove(em, members, centroid,
                        shape, CommandSource.AI);
                    for (int j = 0; j < members.Count; j++) formedSet.Add(members[j]);
                    issuedAny = true;
                }

                if (issuedAny) _nextFormTime[fKey] = now + FactionCooldown;

                // Bounded prune: forget the dead so the set cannot grow all match.
                if (formedSet.Count > 400)
                    formedSet.RemoveWhere(e => !em.Exists(e));
            }
        }

        /// <summary>The formation a unit last marched in (0 = none / forgotten).</summary>
        private static uint GroupKeyOf(EntityManager em, Entity e)
        {
            if (!TransientState.Active<FormationSlotMemory>(em, e)) return 0;
            return em.GetComponentData<FormationSlotMemory>(e).GroupKey;
        }
    }
}
