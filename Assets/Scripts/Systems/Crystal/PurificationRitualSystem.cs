// PurificationRitualSystem.cs
// Drives Alanthor's Purification ritual: scholar moves to a target node,
// channels for PurificationChannelTime seconds, completes by transitioning
// the node to Cleansed and spawning a Glow pickup.
//
// Lifecycle on the scholar entity:
//   PurifyCommand              — order received from CommandRouter.IssuePurify
//     ↓ (close enough)
//   PurifyCommand + RitualState — channeling (timer increments each frame)
//     ↓ (Progress >= TotalDuration)
//   (none)                     — ritual complete, components cleaned up
//
// Lifecycle on the node entity:
//   ActiveRitualOnNode added on channel start, removed on completion/cancel.
//
// Spec refs: §5.1 universal ritual properties, §5.2 Alanthor purification,
// §11 item 2 (Purification ritual mechanic).
//
// Location: Assets/Scripts/Systems/Crystal/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Channels and resolves Alanthor's Purification ritual. Future
    /// extraction/conversion rituals can reuse this control flow with a
    /// different RitualKind and target-state.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NodeStateReversionSystem))]
    public partial class PurificationRitualSystem : SystemBase
    {
        /// <summary>
        /// Cancel a ritual mid-channel and clean up node + scholar
        /// components. The scholar's PurifyCommand is removed so the
        /// system doesn't immediately retry on the next frame.
        /// </summary>
        private void CancelRitual(EntityManager em, Entity scholar, Entity node, string reason)
        {
            if (em.Exists(scholar))
            {
                if (em.HasComponent<RitualState>(scholar))   em.RemoveComponent<RitualState>(scholar);
                if (em.HasComponent<PurifyCommand>(scholar)) em.RemoveComponent<PurifyCommand>(scholar);
            }
            if (em.Exists(node) && em.HasComponent<ActiveRitualOnNode>(node))
                em.RemoveComponent<ActiveRitualOnNode>(node);

            Debug.Log($"[Purification] cancelled — {reason}");
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            // Phase 1: PurifyCommand without an active RitualState — approach
            // and start channeling.
            var startList   = new NativeList<Entity>(2, Allocator.Temp);
            var startNodes  = new NativeList<Entity>(2, Allocator.Temp);
            var cancelList  = new NativeList<Entity>(2, Allocator.Temp);
            var cancelNodes = new NativeList<Entity>(2, Allocator.Temp);
            var moveList    = new NativeList<Entity>(2, Allocator.Temp);
            var moveTargets = new NativeList<float3>(2, Allocator.Temp);

            foreach (var (purify, scholarTransform, scholarHealth, entity) in SystemAPI
                .Query<RefRO<PurifyCommand>, RefRO<LocalTransform>, RefRO<Health>>()
                .WithAll<ScholarTag>()
                .WithNone<RitualState>()
                .WithEntityAccess())
            {
                var node = purify.ValueRO.TargetNode;

                // Validate target node still exists and is in a state that
                // accepts purification (Active only — Cleansed/Converted/
                // Destroyed are not valid Alanthor targets).
                if (!em.Exists(node) || !em.HasComponent<CrystalNodeState>(node))
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(Entity.Null);
                    continue;
                }
                var nodeState = em.GetComponentData<CrystalNodeState>(node).State;
                if (nodeState != NodeState.Active)
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(node);
                    continue;
                }

                // Scholar already dying.
                if (scholarHealth.ValueRO.Value <= 0)
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(node);
                    continue;
                }

                // Only one ritual per node — first to claim wins.
                if (em.HasComponent<ActiveRitualOnNode>(node))
                {
                    var occupant = em.GetComponentData<ActiveRitualOnNode>(node).Ritualist;
                    if (occupant != entity && em.Exists(occupant))
                    {
                        cancelList.Add(entity);
                        cancelNodes.Add(Entity.Null); // don't clear someone else's claim
                        continue;
                    }
                }

                var nodePos = em.GetComponentData<LocalTransform>(node).Position;
                var scholarPos = scholarTransform.ValueRO.Position;
                float dist = math.distance(
                    new float2(nodePos.x, nodePos.z),
                    new float2(scholarPos.x, scholarPos.z));

                if (dist > RitualRange)
                {
                    // Move toward the node. SetComponent on the baked
                    // DesiredDestination — Scholar.Create added it.
                    moveList.Add(entity);
                    moveTargets.Add(nodePos);
                    continue;
                }

                // In range — start channeling.
                startList.Add(entity);
                startNodes.Add(node);
            }

            // Phase 2: active rituals — tick the timer + check interrupts.
            var completeList    = new NativeList<Entity>(2, Allocator.Temp);
            var completeNodes   = new NativeList<Entity>(2, Allocator.Temp);
            var completeFactions = new NativeList<Faction>(2, Allocator.Temp);
            var completeCultures = new NativeList<byte>(2, Allocator.Temp);
            var completePositions = new NativeList<float3>(2, Allocator.Temp);

            foreach (var (ritualRW, scholarTransform, scholarHealth, scholarFaction, entity) in SystemAPI
                .Query<RefRW<RitualState>, RefRO<LocalTransform>, RefRO<Health>, RefRO<FactionTag>>()
                .WithAll<ScholarTag>()
                .WithEntityAccess())
            {
                ref var ritual = ref ritualRW.ValueRW;
                var node = ritual.TargetNode;

                if (!em.Exists(node) || !em.HasComponent<CrystalNodeState>(node))
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(Entity.Null);
                    continue;
                }

                // Interrupt if the node state changed under us (someone else
                // converted/destroyed it).
                if (em.GetComponentData<CrystalNodeState>(node).State != NodeState.Active)
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(node);
                    continue;
                }

                // Interrupt if the scholar died.
                if (scholarHealth.ValueRO.Value <= 0)
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(node);
                    continue;
                }

                // Interrupt if the scholar wandered out of range — happens
                // when another command (Move, AttackMove, etc.) overwrites
                // DesiredDestination.
                var nodePos = em.GetComponentData<LocalTransform>(node).Position;
                var scholarPos = scholarTransform.ValueRO.Position;
                float dist = math.distance(
                    new float2(nodePos.x, nodePos.z),
                    new float2(scholarPos.x, scholarPos.z));
                if (dist > RitualCancelRange)
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(node);
                    continue;
                }

                ritual.Progress += dt;
                if (ritual.Progress >= ritual.TotalDuration)
                {
                    byte culture = Cultures.None;
                    if (em.HasComponent<FactionProgress>(entity))
                        culture = em.GetComponentData<FactionProgress>(entity).Culture;

                    // Local-player scholars during Timeless Age won't have
                    // a culture committed yet. Default to Alanthor for the
                    // Purification ritual — by design, only Alanthor
                    // scholars exist, so Cultures.None falls back here
                    // rather than producing an unattributable Cleansed state.
                    if (culture == Cultures.None) culture = Cultures.Alanthor;

                    completeList.Add(entity);
                    completeNodes.Add(node);
                    completeFactions.Add(scholarFaction.ValueRO.Value);
                    completeCultures.Add(culture);
                    completePositions.Add(scholarPos);
                }
            }

            // Apply Phase 1 starts
            for (int i = 0; i < startList.Length; i++)
            {
                Entity scholar = startList[i];
                Entity node = startNodes[i];
                Faction f = em.HasComponent<FactionTag>(scholar)
                    ? em.GetComponentData<FactionTag>(scholar).Value
                    : Faction.Blue;
                byte culture = em.HasComponent<FactionProgress>(scholar)
                    ? em.GetComponentData<FactionProgress>(scholar).Culture
                    : Cultures.Alanthor;
                if (culture == Cultures.None) culture = Cultures.Alanthor;

                em.AddComponentData(scholar, new RitualState
                {
                    Kind = RitualKind.Purification,
                    TargetNode = node,
                    Progress = 0f,
                    TotalDuration = PurificationChannelTime,
                });

                var active = new ActiveRitualOnNode
                {
                    Ritualist = scholar,
                    Kind = RitualKind.Purification,
                    RitualistFaction = f,
                    RitualistCulture = culture,
                    DefenseSpawnTimer = RitualDefenseMaxInterval, // first spawn after the slowest interval
                    DefendersSpawned = 0,
                };
                if (em.HasComponent<ActiveRitualOnNode>(node))
                    em.SetComponentData(node, active);
                else
                    em.AddComponentData(node, active);

                // Stop the scholar so they channel in place.
                if (em.HasComponent<DesiredDestination>(scholar))
                    em.SetComponentData(scholar, new DesiredDestination { Has = 0 });

                Debug.Log($"[Purification] channeling started by {f} on node");
            }

            // Apply Phase 1 moves (set destination toward target node)
            for (int i = 0; i < moveList.Length; i++)
            {
                Entity scholar = moveList[i];
                if (em.HasComponent<DesiredDestination>(scholar))
                    em.SetComponentData(scholar, new DesiredDestination
                    {
                        Position = moveTargets[i],
                        Has = 1,
                    });
            }

            // Apply completions
            for (int i = 0; i < completeList.Length; i++)
            {
                Entity scholar = completeList[i];
                Entity node = completeNodes[i];

                CrystalNodeStateHelper.SetState(em, node,
                    NodeState.Cleansed,
                    completeCultures[i],
                    completeFactions[i]);

                // Secondary curse-location main nodes yield 1 RP directly to the
                // purifying faction instead of dropping a Glow pickup.
                if (em.HasComponent<SecondaryCurseLocationTag>(node))
                {
                    FactionReligionPointsHelper.Refund(em, completeFactions[i], 1);
                    Debug.Log($"[Purification] secondary node cleansed by {completeFactions[i]} — +1 RP");
                }
                else
                {
                    GlowPickup.Create(em, completePositions[i], RitualKind.Purification);
                    Debug.Log($"[Purification] complete — node cleansed by {completeFactions[i]}, Glow pickup spawned");
                }

                if (em.HasComponent<RitualState>(scholar))   em.RemoveComponent<RitualState>(scholar);
                if (em.HasComponent<PurifyCommand>(scholar)) em.RemoveComponent<PurifyCommand>(scholar);
                if (em.HasComponent<ActiveRitualOnNode>(node)) em.RemoveComponent<ActiveRitualOnNode>(node);
            }

            // Apply cancellations
            for (int i = 0; i < cancelList.Length; i++)
            {
                CancelRitual(em, cancelList[i], cancelNodes[i], "interrupt / invalid target");
            }

            // Phase 3: orphan cleanup — nodes still flagged ActiveRitualOnNode
            // whose ritualist no longer has a RitualState (cancelled by
            // ClearAllCommands, died, or otherwise lost the channel). Without
            // this pass the node would stay flagged forever and block future
            // rituals.
            var orphanNodes = new NativeList<Entity>(2, Allocator.Temp);
            foreach (var (active, nodeEntity) in SystemAPI
                .Query<RefRO<ActiveRitualOnNode>>()
                .WithEntityAccess())
            {
                Entity ritualist = active.ValueRO.Ritualist;
                bool orphaned =
                    !em.Exists(ritualist) ||
                    !em.HasComponent<RitualState>(ritualist) ||
                    (em.HasComponent<Health>(ritualist) && em.GetComponentData<Health>(ritualist).Value <= 0);
                if (orphaned) orphanNodes.Add(nodeEntity);
            }
            for (int i = 0; i < orphanNodes.Length; i++)
                em.RemoveComponent<ActiveRitualOnNode>(orphanNodes[i]);
            orphanNodes.Dispose();

            startList.Dispose();
            startNodes.Dispose();
            cancelList.Dispose();
            cancelNodes.Dispose();
            moveList.Dispose();
            moveTargets.Dispose();
            completeList.Dispose();
            completeNodes.Dispose();
            completeFactions.Dispose();
            completeCultures.Dispose();
            completePositions.Dispose();
        }
    }
}
