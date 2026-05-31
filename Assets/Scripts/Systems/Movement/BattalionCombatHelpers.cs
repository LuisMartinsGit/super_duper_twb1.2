// BattalionCombatHelpers.cs
// Combat state + per-member target assignment extracted from BattalionSyncSystem.
// Location: Assets/Scripts/Systems/Movement/BattalionCombatHelpers.cs
//
// Fix #218: kept as a static helper (not a separate ISystem) so the main
// BattalionSyncSystem can still own the per-frame member-position cache
// without round-tripping state through components.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Output state computed by <see cref="BattalionCombatHelpers.UpdateCombatState"/>.
    /// Consumed by the movement phases in BattalionSyncSystem.
    /// </summary>
    internal struct BattalionCombatState
    {
        public bool BattalionInCombat;
        public bool IsRangedBattalion;
        public float BattalionMaxRange;
        public bool InEncircleRange;
        public bool InFiringRange;
        public float3 OwnCenter;
    }

    internal static class BattalionCombatHelpers
    {
        /// <summary>Engagement distance used to decide when to release members from formation.</summary>
        public const float EncircleDistance = 7f;

        /// <summary>
        /// Clear the leader's stale Target + BattalionAttackTarget when the
        /// current target has died. Tries to reassign to another living
        /// member of the tracked enemy battalion before giving up.
        /// </summary>
        public static void ClearStaleLeaderTarget(
            EntityManager em,
            EntityCommandBuffer ecb,
            Entity leaderEntity)
        {
            if (!em.HasComponent<Target>(leaderEntity)) return;
            var lt = em.GetComponentData<Target>(leaderEntity);
            if (lt.Value == Entity.Null) return;

            bool targetGone = !em.Exists(lt.Value)
                || !em.HasComponent<Health>(lt.Value)
                || em.GetComponentData<Health>(lt.Value).Value <= 0;
            if (!targetGone) return;

            // Try to reassign leader target to next living enemy from same battalion
            bool reassigned = false;
            if (em.HasComponent<BattalionAttackTarget>(leaderEntity))
            {
                var bat = em.GetComponentData<BattalionAttackTarget>(leaderEntity);
                if (bat.EnemyLeader != Entity.Null && em.Exists(bat.EnemyLeader)
                    && em.HasBuffer<BattalionMember>(bat.EnemyLeader))
                {
                    var enemyBuf = em.GetBuffer<BattalionMember>(bat.EnemyLeader);
                    for (int ei = 0; ei < enemyBuf.Length; ei++)
                    {
                        var em2 = enemyBuf[ei].Value;
                        if (em2 != Entity.Null && em.Exists(em2)
                            && em.HasComponent<Health>(em2)
                            && em.GetComponentData<Health>(em2).Value > 0)
                        {
                            em.SetComponentData(leaderEntity, new Target { Value = em2 });
                            if (em.HasComponent<AttackCommand>(leaderEntity))
                                em.SetComponentData(leaderEntity, new AttackCommand { Target = em2 });
                            reassigned = true;
                            break;
                        }
                    }
                    if (!reassigned)
                    {
                        // All enemies from target battalion dead — clear tracking
                        ecb.RemoveComponent<BattalionAttackTarget>(leaderEntity);
                    }
                }
                else
                {
                    ecb.RemoveComponent<BattalionAttackTarget>(leaderEntity);
                }
            }

            if (!reassigned)
            {
                em.SetComponentData(leaderEntity, new Target { Value = Entity.Null });
                if (em.HasComponent<AttackCommand>(leaderEntity))
                    ecb.RemoveComponent<AttackCommand>(leaderEntity);
            }
        }

        /// <summary>
        /// Compute the own-battalion center of mass, detect whether the
        /// battalion is ranged (first alive ArcherTag member), and evaluate
        /// encirclement / firing-range state against the leader's current
        /// Target. When in range, also assigns per-member Target components
        /// (melee: nearest enemy, ranged: random spread).
        ///
        /// Returns the combat state the movement phases need downstream.
        /// </summary>
        public static BattalionCombatState UpdateCombatState(
            EntityManager em,
            Entity leaderEntity,
            float3 leaderPos,
            int memberCount,
            NativeArray<Entity> members,
            NativeArray<float3> memberPos,
            NativeArray<bool> memberAlive,
            int aliveCount)
        {
            var s = new BattalionCombatState { OwnCenter = leaderPos };

            // Compute own battalion center of mass
            if (aliveCount > 0)
            {
                float3 sum = float3.zero;
                for (int i = 0; i < memberCount; i++)
                    if (memberAlive[i]) sum += memberPos[i];
                s.OwnCenter = sum / aliveCount;
            }

            // Detect if this is a ranged battalion (first alive member has ArcherTag)
            s.BattalionMaxRange = 25f;
            for (int i = 0; i < memberCount; i++)
            {
                if (!memberAlive[i]) continue;
                if (em.HasComponent<ArcherTag>(members[i]))
                {
                    s.IsRangedBattalion = true;
                    if (em.HasComponent<ArcherState>(members[i]))
                        s.BattalionMaxRange = em.GetComponentData<ArcherState>(members[i]).MaxRange;
                    break;
                }
                break; // First alive member is not an archer — this is a melee battalion
            }

            if (!em.HasComponent<Target>(leaderEntity)) return s;
            var lt = em.GetComponentData<Target>(leaderEntity);
            if (lt.Value == Entity.Null || !em.Exists(lt.Value)
                || !em.HasComponent<Health>(lt.Value)
                || em.GetComponentData<Health>(lt.Value).Value <= 0)
                return s;

            s.BattalionInCombat = true;

            // Compute enemy battalion center of mass
            float3 enemyCenter = em.GetComponentData<LocalTransform>(lt.Value).Position;
            if (em.HasComponent<BattalionAttackTarget>(leaderEntity))
            {
                var bat = em.GetComponentData<BattalionAttackTarget>(leaderEntity);
                if (bat.EnemyLeader != Entity.Null && em.Exists(bat.EnemyLeader)
                    && em.HasBuffer<BattalionMember>(bat.EnemyLeader))
                {
                    var eBuf = em.GetBuffer<BattalionMember>(bat.EnemyLeader);
                    float3 eSum = float3.zero;
                    int eCnt = 0;
                    for (int ei = 0; ei < eBuf.Length; ei++)
                    {
                        var e2 = eBuf[ei].Value;
                        if (e2 != Entity.Null && em.Exists(e2) && em.HasComponent<LocalTransform>(e2)
                            && em.HasComponent<Health>(e2) && em.GetComponentData<Health>(e2).Value > 0)
                        {
                            eSum += em.GetComponentData<LocalTransform>(e2).Position;
                            eCnt++;
                        }
                    }
                    if (eCnt > 0) enemyCenter = eSum / eCnt;
                }
            }

            float centerDist = math.length(new float2(
                s.OwnCenter.x - enemyCenter.x,
                s.OwnCenter.z - enemyCenter.z));
            s.InEncircleRange = centerDist < EncircleDistance;
            s.InFiringRange = s.IsRangedBattalion && centerDist < s.BattalionMaxRange;

            // Determine engage range: ranged stops at firing range, melee at encircle range
            bool shouldStop = s.IsRangedBattalion ? s.InFiringRange : s.InEncircleRange;

            // Track enemy movement: leader destination follows the enemy battalion
            if (!shouldStop && em.HasComponent<DesiredDestination>(leaderEntity))
            {
                em.SetComponentData(leaderEntity, new DesiredDestination { Position = enemyCenter, Has = 1 });
            }
            else if (shouldStop && em.HasComponent<DesiredDestination>(leaderEntity))
            {
                em.SetComponentData(leaderEntity, new DesiredDestination { Has = 0 });
            }

            // Assign per-member targets when in engage range
            bool shouldAssignTargets = s.IsRangedBattalion ? s.InFiringRange : s.InEncircleRange;
            if (shouldAssignTargets)
            {
                AssignPerMemberTargets(em, leaderEntity, lt.Value,
                    s.IsRangedBattalion, s.OwnCenter, memberCount, members, memberPos, memberAlive);
            }

            return s;
        }

        // Cap how many MELEE attackers can share a single enemy. Once the cap
        // is hit, overflow members pick the next-nearest enemy and naturally
        // walk around the front-line clump to engage. Only applies to melee
        // — the ranged branch already random-spreads across the enemy
        // battalion via a hash of (leader, member-index).
        // SpreadDistRatio guards against members trekking far to a distant
        // under-cap target when a saturated one is right in front of them.
        private const int MeleeMaxAttackersPerEnemy = 8;
        private const float MeleeSpreadDistRatio = 1.5f;

        private static void AssignPerMemberTargets(
            EntityManager em,
            Entity leaderEntity,
            Entity fallbackTarget,
            bool isRangedBattalion,
            float3 ownCenter,
            int memberCount,
            NativeArray<Entity> members,
            NativeArray<float3> memberPos,
            NativeArray<bool> memberAlive)
        {
            // Check if enemy is a battalion (has BattalionAttackTarget with valid leader)
            bool enemyIsBattalion = false;
            DynamicBuffer<BattalionMember> enemyBuf = default;
            int livingEnemyCount = 0;

            if (em.HasComponent<BattalionAttackTarget>(leaderEntity))
            {
                var bat = em.GetComponentData<BattalionAttackTarget>(leaderEntity);
                if (bat.EnemyLeader != Entity.Null && em.Exists(bat.EnemyLeader)
                    && em.HasBuffer<BattalionMember>(bat.EnemyLeader))
                {
                    enemyIsBattalion = true;
                    enemyBuf = em.GetBuffer<BattalionMember>(bat.EnemyLeader);

                    for (int ei = 0; ei < enemyBuf.Length; ei++)
                    {
                        var enemy = enemyBuf[ei].Value;
                        if (enemy != Entity.Null && em.Exists(enemy)
                            && em.HasComponent<Health>(enemy) && em.GetComponentData<Health>(enemy).Value > 0)
                            livingEnemyCount++;
                    }
                }
            }

            // Build a per-enemy attacker count from our own members' current
            // targets so we can spread overflow attackers off the front rank.
            // Includes external attackers too (other battalions targeting the
            // same enemy battalion) so cross-battalion stacking is also capped.
            var attackerCount = new Dictionary<Entity, int>();
            if (enemyIsBattalion)
            {
                // Count current targeters across the entire world — picks up
                // both our own members and any allies already engaged.
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<Target>(), ComponentType.ReadOnly<UnitTag>());
                using var qTargets = q.ToComponentDataArray<Target>(Allocator.Temp);
                for (int qi = 0; qi < qTargets.Length; qi++)
                {
                    var tv = qTargets[qi].Value;
                    if (tv == Entity.Null) continue;
                    attackerCount.TryGetValue(tv, out int prev);
                    attackerCount[tv] = prev + 1;
                }
            }

            for (int i = 0; i < memberCount; i++)
            {
                if (!memberAlive[i]) continue;
                // Skip members with living targets
                if (em.HasComponent<Target>(members[i]))
                {
                    var curTgt = em.GetComponentData<Target>(members[i]);
                    if (curTgt.Value != Entity.Null && em.Exists(curTgt.Value)
                        && em.HasComponent<Health>(curTgt.Value)
                        && em.GetComponentData<Health>(curTgt.Value).Value > 0)
                        continue;
                }

                Entity assignedTarget = Entity.Null;

                if (enemyIsBattalion)
                {
                    if (isRangedBattalion && livingEnemyCount > 0)
                    {
                        // Ranged: assign random enemy to spread fire
                        int pick = (leaderEntity.Index * 31 + i * 7 + (int)(ownCenter.x * 100)) % livingEnemyCount;
                        int count = 0;
                        for (int ei = 0; ei < enemyBuf.Length; ei++)
                        {
                            var enemy = enemyBuf[ei].Value;
                            if (enemy == Entity.Null || !em.Exists(enemy)) continue;
                            if (!em.HasComponent<Health>(enemy) || em.GetComponentData<Health>(enemy).Value <= 0) continue;
                            if (count == pick) { assignedTarget = enemy; break; }
                            count++;
                        }
                    }
                    else
                    {
                        // Melee: prefer nearest enemy NOT already at the attacker
                        // cap (rank 2 picks a different enemy than rank 1 so they
                        // walk around the front-line clump). Only accept the
                        // under-cap candidate if it's within MeleeSpreadDistRatio
                        // of absolute nearest — otherwise dogpile nearest rather
                        // than trek across the field. Fall back to absolute
                        // nearest if every candidate is saturated.
                        float underBestD = float.MaxValue;
                        Entity underBest = Entity.Null;
                        float anyBestD = float.MaxValue;
                        Entity anyBest = Entity.Null;

                        for (int ei = 0; ei < enemyBuf.Length; ei++)
                        {
                            var enemy = enemyBuf[ei].Value;
                            if (enemy == Entity.Null || !em.Exists(enemy)) continue;
                            if (!em.HasComponent<Health>(enemy) || em.GetComponentData<Health>(enemy).Value <= 0) continue;
                            if (!em.HasComponent<LocalTransform>(enemy)) continue;
                            float3 ePos = em.GetComponentData<LocalTransform>(enemy).Position;
                            float3 diff = ePos - memberPos[i];
                            diff.y = 0;
                            float d = math.lengthsq(diff);

                            if (d < anyBestD) { anyBestD = d; anyBest = enemy; }
                            attackerCount.TryGetValue(enemy, out int curC);
                            if (curC < MeleeMaxAttackersPerEnemy && d < underBestD)
                            {
                                underBestD = d;
                                underBest = enemy;
                            }
                        }

                        // Distance comparisons here are squared, so the ratio
                        // also gets squared for an equivalent threshold.
                        const float ratioSq = MeleeSpreadDistRatio * MeleeSpreadDistRatio;
                        if (underBest != Entity.Null
                            && (anyBest == Entity.Null || underBestD <= anyBestD * ratioSq))
                        {
                            assignedTarget = underBest;
                        }
                        else
                        {
                            assignedTarget = anyBest;
                        }
                    }
                }
                else
                {
                    // Standalone enemy (ballista, litharch, etc.) — all members target it
                    assignedTarget = fallbackTarget;
                }

                if (assignedTarget != Entity.Null)
                {
                    em.SetComponentData(members[i], new Target { Value = assignedTarget });
                    // Track for subsequent members in this same pass so members
                    // 2 and 3 don't both pile on what was just assigned to 1.
                    attackerCount.TryGetValue(assignedTarget, out int prev);
                    attackerCount[assignedTarget] = prev + 1;
                }
            }
        }
    }
}
