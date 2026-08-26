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

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Border
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
            // Was this a CHANNEL that broke, or an approach that never got
            // going? This method serves both paths, and only the former earns
            // the Backlash — RitualState is present exactly when the Scholar
            // was mid-rite (phase 1 queries WithNone<RitualState>).
            bool wasChannelling = em.Exists(scholar) && em.HasComponent<RitualState>(scholar);
            Faction provoker = em.Exists(scholar) && em.HasComponent<FactionTag>(scholar)
                ? em.GetComponentData<FactionTag>(scholar).Value : Faction.Border;

            if (em.Exists(scholar))
            {
                if (em.HasComponent<RitualState>(scholar))   em.RemoveComponent<RitualState>(scholar);
                if (em.HasComponent<PurifyCommand>(scholar)) em.RemoveComponent<PurifyCommand>(scholar);
            }
            if (em.Exists(node) && em.HasComponent<ActiveRitualOnNode>(node))
                em.RemoveComponent<ActiveRitualOnNode>(node);

            // THE BACKLASH (canon §2.9).
            if (wasChannelling) RitualBacklashSystem.Arm(em, node, provoker);

            TWBLog.Log($"[Purification] cancelled — {reason}");
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

                // Validate target node still exists and is purifiable.
                // Destruction rework (2026-07): Purification is the PERMANENT
                // removal, so it may target an Active node OR one lying in
                // rubble / rebuilding (Destroyed) — cleansing it stops the
                // rebuild for good. Only nodes already claimed by another
                // culture (Cleansed/Converted) are invalid.
                if (!em.Exists(node) || !em.HasComponent<BorderNodeState>(node))
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(Entity.Null);
                    continue;
                }
                var nodeState = em.GetComponentData<BorderNodeState>(node).State;
                if (nodeState == NodeState.Cleansed || nodeState == NodeState.Converted)
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
                    // Move toward a point BESIDE the node, never the node
                    // itself: its own building footprint is stamped
                    // impassable, and a goal on an impassable cell produces
                    // an empty flow field, which strands the Scholar walking
                    // on the spot. See RitualApproach.
                    moveList.Add(entity);
                    moveTargets.Add(RitualApproach.StandPoint(nodePos, scholarPos));
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

                if (!em.Exists(node) || !em.HasComponent<BorderNodeState>(node))
                {
                    cancelList.Add(entity);
                    cancelNodes.Add(Entity.Null);
                    continue;
                }

                // Interrupt only if another culture claimed the node under us
                // (Cleansed/Converted). Active or Destroyed(rubble/rebuild) are
                // both still valid purification targets, so don't interrupt for
                // a node that gets destroyed mid-channel.
                var midState = em.GetComponentData<BorderNodeState>(node).State;
                if (midState == NodeState.Cleansed || midState == NodeState.Converted)
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

                // THE WAKING (canon §2.8): touching a well wakes THAT well —
                // it starts feeding the veil and never sleeps again. Fires on
                // channel START, not completion, so an interrupted attempt has
                // still armed the region: no safe probe, no take-backs.
                CurseAwakeningHelper.Wake(em, node, f, SystemAPI.Time.ElapsedTime);

                TWBLog.Log($"[Purification] channeling started by {f} on node");
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

                BorderNodeStateHelper.SetState(em, node,
                    NodeState.Cleansed,
                    completeCultures[i],
                    completeFactions[i]);

                // Permanent cleanse: keep the node alive as an INERT husk
                // (Cleansed never reverts). SetState(Cleansed) from Destroyed
                // strips NodeDormant but leaves HP at 0 — restore it so
                // DeathSystem doesn't delete the entity (victory tracking
                // still needs to count it). Cancel any in-flight rebuild.
                // (Nodes carry no attack any more — curse nodes never attack,
                // 2026-08-11 — so there is no turret to silence here.)
                if (em.HasComponent<NodeRebuilding>(node))
                    em.RemoveComponent<NodeRebuilding>(node);
                if (em.HasComponent<Health>(node))
                {
                    var h = em.GetComponentData<Health>(node);
                    h.Value = h.Max;
                    em.SetComponentData(node, h);
                }

                // Curse & Shardroot canon: per-node Glow rewards are GONE.
                // Secondary nodes still grant +1 RP; a claimed main well's
                // only artifact reward is the SHARDROOT if this was the
                // seeded host (first verb wins it). The ongoing reward for
                // holding a Purified (Cleansed) well is its veilstone
                // income (WellHoldIncomeSystem).
                if (em.HasComponent<SecondaryBorderLocationTag>(node))
                {
                    FactionReligionPointsHelper.Refund(em, completeFactions[i], 1);
                    TWBLog.Log($"[Purification] secondary node cleansed by {completeFactions[i]} — +1 RP");
                }
                else
                {
                    TheWaningBorder.Systems.Border.ShardrootSystem.TryAward(
                        em, node, completePositions[i], RitualKind.Purification);
                    TWBLog.Log($"[Purification] complete — well purified by {completeFactions[i]}");
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
                // Only our own verb's claims — conversion/corruption sweep
                // theirs (they both filter on Kind; this one didn't, so a
                // Conversion or ViolentExtraction claim could be cleared by
                // the purify sweep the moment its ritualist blinked).
                if (active.ValueRO.Kind != RitualKind.Purification) continue;

                Entity ritualist = active.ValueRO.Ritualist;
                bool orphaned =
                    ritualist == Entity.Null ||
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
