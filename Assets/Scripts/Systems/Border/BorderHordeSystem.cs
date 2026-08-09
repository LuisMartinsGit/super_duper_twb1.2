// BorderHordeSystem.cs
// Movement / formation driver for the Border armies. Works hand-in-hand
// with BorderArmyAISystem, which fields two SO-tiered armies per node and stamps
// every unit with OwnerNode (its node) + BorderArmyRole (Defend | Attack).
//
// This system groups units by (OwnerNode, Role) so each node's two armies move
// independently, then drives each group:
//
//   ATTACK group:
//     * AttackState == Attacking  → march in cohesive formation to the node's
//       chosen player (nearest Hall of BorderNodeArmies.AttackTarget), attack-
//       moving anything in its path. Never "arrives" — it presses until the
//       army or the enemy is gone.
//     * Mustering / Recalling     → gather/return to the node and hold there
//       (so the AI can finish training or apply an upgrade).
//
//   DEFEND group:
//     * Hold within BorderSettings.defendHoldRadius of the node; units past it
//       attack-move back. They keep AttackMoveTag so TargetingSystem auto-
//       engages any intruder in their own LOS.
//
// Cohesion: a group advances at its slowest member's base pace; leaders that
// pull ahead of the (moving) centroid throttle down. STUCK and FIGHTING members
// are excluded from the centroid so neither freezes the pack — the wave never
// halts on combat; only the unit actually holding a target stops to fight.
//
// Determinism (lockstep): fixed-step World.Time only; stable chunk-ordered
// iteration; objectives chosen by nearest-distance.
//
// SystemBase (not ISystem) because AttackMoveCommandHelper does structural
// changes (AddComponent) that ISystem codegen blocks.
//
// Location: Assets/Scripts/Systems/Border/BorderHordeSystem.cs

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.Data.Border;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BorderHordeSystem : SystemBase
    {
        private const float ControlInterval = 0.5f;
        private const float CohesionTolerance = 8f;
        private const float RepathThreshold = 6f;
        private const float AheadThrottleFactor = 0.5f;
        private const float MinThrottleSpeed = 0.4f;
        private const float DefaultMarchSpeed = 3.5f;
        private const int StuckCounterThreshold = 8;

        private float _timer;
        private EntityQuery _hordeQuery;
        private EntityQuery _enemyUnitQuery;
        private EntityQuery _enemyBuildingQuery;
        private EntityQuery _hallQuery;

        // One movement group per (node, role).
        private struct GKey : IEquatable<GKey>
        {
            public Entity Node;
            public byte Role;
            public bool Equals(GKey o) => Node == o.Node && Role == o.Role;
            public override int GetHashCode() => (Node.GetHashCode() * 397) ^ Role;
        }

        protected override void OnCreate()
        {
            // §2.5: no curse army → nothing to march.
            Enabled = TheWaningBorder.Core.Config.BorderConstants.CurseFieldsArmies;
            RequireForUpdate<BorderUnitTag>();

            // Only units the army AI has assigned (OwnerNode + BorderArmyRole).
            _hordeQuery = GetEntityQuery(
                ComponentType.ReadOnly<BorderUnitTag>(),
                ComponentType.ReadOnly<BorderArmyRole>(),
                ComponentType.ReadOnly<OwnerNode>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            _enemyUnitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());

            _enemyBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());

            _hallQuery = GetEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
        }

        protected override void OnUpdate()
        {
            _timer += World.Time.DeltaTime;
            if (_timer < ControlInterval) return;
            _timer = 0f;

            var em = EntityManager;
            var settings = BorderSettings.Get();
            float defendHold = settings != null ? settings.defendHoldRadius : 18f;
            float recallArrive = settings != null ? settings.recallArriveRadius : 16f;

            using var hEnts = _hordeQuery.ToEntityArray(Allocator.Temp);
            using var hFac = _hordeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hXf = _hordeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hRole = _hordeQuery.ToComponentDataArray<BorderArmyRole>(Allocator.Temp);
            using var hOwner = _hordeQuery.ToComponentDataArray<OwnerNode>(Allocator.Temp);

            int count = hEnts.Length;

            // Per-member parallel lists (managed — this runs at 2 Hz, not Burst).
            var mEnt = new List<Entity>(count);
            var mPos = new List<float3>(count);
            var mEng = new List<bool>(count);
            var mGrp = new List<int>(count);

            // Per-group accumulators.
            // DETERMINISM: `index` is LOOKUP-ONLY (TryGetValue). Group indices are
            // assigned in query (chunk) order, and all per-group/per-unit work
            // iterates the ordered lists below — never enumerate this dict's
            // keys/values to drive a sim decision (managed-dict order desyncs).
            var index = new Dictionary<GKey, int>();
            var gNode = new List<Entity>();
            var gRole = new List<byte>();
            var gMarchSum = new List<float3>();
            var gMarchCount = new List<int>();
            var gSpeed = new List<float>();

            for (int i = 0; i < count; i++)
            {
                if (hFac[i].Value != Faction.Border) continue;
                var e = hEnts[i];
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;

                float3 p = hXf[i].Position;
                byte role = (byte)hRole[i].Role;
                Entity node = hOwner[i].Value;

                bool eng = false;
                if (em.HasComponent<Target>(e))
                {
                    var tg = em.GetComponentData<Target>(e).Value;
                    if (tg != Entity.Null && em.Exists(tg)
                        && !(em.HasComponent<Health>(tg) && em.GetComponentData<Health>(tg).Value <= 0))
                        eng = true;
                }
                bool stuck = em.HasComponent<StuckState>(e)
                    && em.GetComponentData<StuckState>(e).Counter > StuckCounterThreshold;

                float sp = DefaultMarchSpeed;
                if (em.HasComponent<MoveSpeed>(e))
                {
                    var ms = em.GetComponentData<MoveSpeed>(e);
                    if (ms.Value > 0f) sp = ms.Value;
                }

                var key = new GKey { Node = node, Role = role };
                if (!index.TryGetValue(key, out int gi))
                {
                    gi = gNode.Count;
                    index[key] = gi;
                    gNode.Add(node); gRole.Add(role);
                    gMarchSum.Add(float3.zero); gMarchCount.Add(0); gSpeed.Add(float.MaxValue);
                }

                mEnt.Add(e); mPos.Add(p); mEng.Add(eng); mGrp.Add(gi);
                if (!eng && !stuck) { gMarchSum[gi] += p; gMarchCount[gi]++; }
                if (sp < gSpeed[gi]) gSpeed[gi] = sp;
            }

            int groups = gNode.Count;
            if (groups == 0) return;

            // ── Resolve each group's objective + behaviour ─────────────────
            var gObj = new List<float3>(groups);
            var gHasObj = new List<bool>(groups);
            var gPress = new List<bool>(groups);   // true = keep marching (never arrive)
            var gArrive = new List<float>(groups);
            var gCentDist = new List<float>(groups);

            for (int gi = 0; gi < groups; gi++)
            {
                if (gSpeed[gi] == float.MaxValue) gSpeed[gi] = DefaultMarchSpeed;

                Entity node = gNode[gi];
                bool nodeValid = em.Exists(node) && em.HasComponent<LocalTransform>(node);
                float3 nodePos = nodeValid ? em.GetComponentData<LocalTransform>(node).Position : float3.zero;

                float3 centroid = gMarchCount[gi] > 0 ? gMarchSum[gi] / gMarchCount[gi] : nodePos;

                float3 obj; bool has; bool press; float arrive;

                if (gRole[gi] == (byte)BorderArmyRoleType.Defend)
                {
                    obj = nodePos; has = nodeValid; press = false; arrive = defendHold;
                }
                else // Attack
                {
                    var astate = BorderAttackState.Attacking;
                    Faction target = Faction.Border;
                    bool hasTarget = false;
                    if (nodeValid && em.HasComponent<BorderNodeArmies>(node))
                    {
                        var a = em.GetComponentData<BorderNodeArmies>(node);
                        astate = a.AttackState;
                        target = a.AttackTarget;
                        hasTarget = a.HasAttackTarget != 0;
                    }

                    if (astate == BorderAttackState.Attacking)
                    {
                        if (hasTarget && NearestHallOfFaction(target, centroid, out var hp)) { obj = hp; has = true; }
                        else if (NearestNonBorder(_hallQuery, centroid, out var hp2)) { obj = hp2; has = true; }
                        else if (NearestNonBorder(_enemyBuildingQuery, centroid, out var bp)) { obj = bp; has = true; }
                        else if (NearestNonBorder(_enemyUnitQuery, centroid, out var up)) { obj = up; has = true; }
                        else { obj = nodePos; has = nodeValid; }
                        press = true; arrive = 0f;
                    }
                    else // Mustering / Recalling → gather/return at the node
                    {
                        obj = nodePos; has = nodeValid; press = false; arrive = recallArrive;
                    }
                }

                if (has)
                {
                    NavGridQuery.SnapToWalkable(obj, out var snapped, out bool ok);
                    if (ok) obj = snapped;
                }

                gObj.Add(obj); gHasObj.Add(has); gPress.Add(press); gArrive.Add(arrive);
                gCentDist.Add(has ? math.distance(centroid, obj) : 0f);
            }

            // ── Per-unit drive ─────────────────────────────────────────────
            for (int i = 0; i < mEnt.Count; i++)
            {
                Entity u = mEnt[i];
                float3 p = mPos[i];
                int gi = mGrp[i];

                // In combat: the targeting + combat systems own the chase.
                if (mEng[i]) { SetSpeedOverride(em, u, 0f); continue; }

                if (!gHasObj[gi]) continue;

                float3 objective = gObj[gi];

                // Hold/return groups: stop once inside the arrive radius and idle
                // (still auto-engaging via AttackMoveTag).
                if (!gPress[gi] && math.distance(p, objective) <= gArrive[gi])
                {
                    SetSpeedOverride(em, u, 0f);
                    if (em.HasComponent<DesiredDestination>(u))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(u);
                        if (dd.Has != 0) { dd.Has = 0; em.SetComponentData(u, dd); }
                    }
                    continue;
                }

                // Cohesion throttle toward the objective.
                float centDist = gCentDist[gi];
                float groupSpeed = gSpeed[gi];
                float unitDist = math.distance(p, objective);
                if (unitDist < centDist - CohesionTolerance)
                    SetSpeedOverride(em, u, math.max(MinThrottleSpeed, groupSpeed * AheadThrottleFactor));
                else if (unitDist > centDist + CohesionTolerance)
                    SetSpeedOverride(em, u, 0f);
                else
                    SetSpeedOverride(em, u, groupSpeed);

                // Re-issue attack-move only when not already heading at the
                // objective (each call resets + re-enqueues a NavPathRequest).
                bool repath = true;
                if (em.HasComponent<DesiredDestination>(u))
                {
                    var dd = em.GetComponentData<DesiredDestination>(u);
                    if (dd.Has != 0 && math.distance(dd.Position, objective) <= RepathThreshold)
                        repath = false;
                }
                if (repath)
                    AttackMoveCommandHelper.Execute(em, u, objective);
            }
        }

        /// <summary>
        /// Nearest living Hall belonging to <paramref name="f"/> to
        /// <paramref name="from"/>. Returns false when that faction has none.
        /// </summary>
        private bool NearestHallOfFaction(Faction f, float3 from, out float3 pos)
        {
            using var fac = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xf = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hp = _hallQuery.ToComponentDataArray<Health>(Allocator.Temp);

            float best = float.MaxValue;
            pos = default;
            bool found = false;
            for (int i = 0; i < fac.Length; i++)
            {
                if (fac[i].Value != f) continue;
                if (hp[i].Value <= 0) continue;
                float d = math.distancesq(from, xf[i].Position);
                if (d < best) { best = d; pos = xf[i].Position; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Nearest living non-Border entity in <paramref name="q"/> to
        /// <paramref name="from"/>. Returns false when the query has none.
        /// </summary>
        private static bool NearestNonBorder(EntityQuery q, float3 from, out float3 pos)
        {
            using var fac = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xf = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hp = q.ToComponentDataArray<Health>(Allocator.Temp);

            float best = float.MaxValue;
            pos = default;
            bool found = false;
            for (int i = 0; i < fac.Length; i++)
            {
                if (fac[i].Value == Faction.Border) continue;
                if (hp[i].Value <= 0) continue;
                float d = math.distancesq(from, xf[i].Position);
                if (d < best) { best = d; pos = xf[i].Position; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Set the per-unit speed governor. Value 0 means "no throttle" — the
        /// integrator falls back to the unit's base MoveSpeed (it only applies
        /// FormationSpeedOverride when Value &gt; 0).
        /// </summary>
        private static void SetSpeedOverride(EntityManager em, Entity u, float v)
        {
            if (em.HasComponent<FormationSpeedOverride>(u))
                em.SetComponentData(u, new FormationSpeedOverride { Value = v });
            else
                em.AddComponentData(u, new FormationSpeedOverride { Value = v });
        }
    }
}
