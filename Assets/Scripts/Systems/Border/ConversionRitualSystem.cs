// ConversionRitualSystem.cs
// Runai Conversion ritual (spec §5.4 + §5.5). Mirrors PurificationRitualSystem
// but for acolytes targeting active veilstone nodes; on completion the node
// becomes Converted (persists as Runai infrastructure) instead of Cleansed,
// and the border defenders that were converging on the ritualist flip to
// the acolyte's faction — the spec's signature "swarm turns to face Runai's
// enemies" cinematic.
//
// Mechanical difficulty differential vs Purification (spec §5.5: node fights
// enslavement hardest):
//   - Channel time: ConversionChannelTime (45s) vs Purification 35s
//   - RitualDefenseRunaiIntensity (1.6x) shrinks defender spawn interval
//   - Higher Glow yield (14 vs 10) compensates the difficulty
//
// Location: Assets/Scripts/Systems/Border/

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
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NodeStateReversionSystem))]
    public partial class ConversionRitualSystem : SystemBase
    {
        private void CancelRitual(EntityManager em, Entity acolyte, Entity node, string reason)
        {
            // Only a broken CHANNEL earns the Backlash, not an approach that
            // never started — RitualState is present exactly in the former
            // case (phase 1 queries WithNone<RitualState>). See canon §2.9.
            bool wasChannelling = em.Exists(acolyte) && em.HasComponent<RitualState>(acolyte);
            Faction provoker = em.Exists(acolyte) && em.HasComponent<FactionTag>(acolyte)
                ? em.GetComponentData<FactionTag>(acolyte).Value : Faction.Border;

            if (em.Exists(acolyte))
            {
                if (em.HasComponent<RitualState>(acolyte))       em.RemoveComponent<RitualState>(acolyte);
                if (em.HasComponent<ConvertNodeCommand>(acolyte)) em.RemoveComponent<ConvertNodeCommand>(acolyte);
            }
            if (em.Exists(node) && em.HasComponent<ActiveRitualOnNode>(node))
                em.RemoveComponent<ActiveRitualOnNode>(node);

            if (wasChannelling) RitualBacklashSystem.Arm(em, node, provoker);

            TWBLog.Log($"[Conversion] cancelled — {reason}");
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            var startList   = new NativeList<Entity>(2, Allocator.Temp);
            var startNodes  = new NativeList<Entity>(2, Allocator.Temp);
            var cancelList  = new NativeList<Entity>(2, Allocator.Temp);
            var cancelNodes = new NativeList<Entity>(2, Allocator.Temp);
            var moveList    = new NativeList<Entity>(2, Allocator.Temp);
            var moveTargets = new NativeList<float3>(2, Allocator.Temp);

            // Phase 1: approach.
            foreach (var (cmd, transform, health, entity) in SystemAPI
                .Query<RefRO<ConvertNodeCommand>, RefRO<LocalTransform>, RefRO<Health>>()
                .WithAll<AcolyteTag>()
                .WithNone<RitualState>()
                .WithEntityAccess())
            {
                var node = cmd.ValueRO.TargetNode;
                if (!em.Exists(node) || !em.HasComponent<BorderNodeState>(node))
                {
                    cancelList.Add(entity); cancelNodes.Add(Entity.Null); continue;
                }
                if (em.GetComponentData<BorderNodeState>(node).State != NodeState.Active)
                {
                    cancelList.Add(entity); cancelNodes.Add(node); continue;
                }
                if (health.ValueRO.Value <= 0)
                {
                    cancelList.Add(entity); cancelNodes.Add(node); continue;
                }
                if (em.HasComponent<ActiveRitualOnNode>(node))
                {
                    var occupant = em.GetComponentData<ActiveRitualOnNode>(node).Ritualist;
                    if (occupant != entity && em.Exists(occupant))
                    {
                        cancelList.Add(entity); cancelNodes.Add(Entity.Null); continue;
                    }
                }

                var nodePos = em.GetComponentData<LocalTransform>(node).Position;
                var myPos = transform.ValueRO.Position;
                float dist = math.distance(
                    new float2(nodePos.x, nodePos.z),
                    new float2(myPos.x, myPos.z));
                if (dist > RitualRange)
                {
                    // Beside the node, not on it — the node's own footprint is
                    // impassable and a goal there yields an empty flow field
                    // (see RitualApproach).
                    moveList.Add(entity);
                    moveTargets.Add(RitualApproach.StandPoint(nodePos, myPos));
                    continue;
                }
                startList.Add(entity); startNodes.Add(node);
            }

            // Phase 2: channel.
            var completeList = new NativeList<Entity>(2, Allocator.Temp);
            var completeNodes = new NativeList<Entity>(2, Allocator.Temp);
            var completeFactions = new NativeList<Faction>(2, Allocator.Temp);
            var completeCultures = new NativeList<byte>(2, Allocator.Temp);
            var completePositions = new NativeList<float3>(2, Allocator.Temp);

            foreach (var (ritualRW, transform, health, faction, entity) in SystemAPI
                .Query<RefRW<RitualState>, RefRO<LocalTransform>, RefRO<Health>, RefRO<FactionTag>>()
                .WithAll<AcolyteTag>()
                .WithEntityAccess())
            {
                ref var ritual = ref ritualRW.ValueRW;
                if (ritual.Kind != RitualKind.Conversion) continue;
                var node = ritual.TargetNode;

                if (!em.Exists(node) || !em.HasComponent<BorderNodeState>(node))
                {
                    cancelList.Add(entity); cancelNodes.Add(Entity.Null); continue;
                }
                if (em.GetComponentData<BorderNodeState>(node).State != NodeState.Active)
                {
                    cancelList.Add(entity); cancelNodes.Add(node); continue;
                }
                if (health.ValueRO.Value <= 0)
                {
                    cancelList.Add(entity); cancelNodes.Add(node); continue;
                }

                var nodePos = em.GetComponentData<LocalTransform>(node).Position;
                var myPos = transform.ValueRO.Position;
                float dist = math.distance(
                    new float2(nodePos.x, nodePos.z),
                    new float2(myPos.x, myPos.z));
                if (dist > RitualCancelRange)
                {
                    cancelList.Add(entity); cancelNodes.Add(node); continue;
                }

                ritual.Progress += dt;
                if (ritual.Progress >= ritual.TotalDuration)
                {
                    byte culture = Cultures.None;
                    if (em.HasComponent<FactionProgress>(entity))
                        culture = em.GetComponentData<FactionProgress>(entity).Culture;
                    if (culture == Cultures.None) culture = Cultures.Runai;

                    completeList.Add(entity);
                    completeNodes.Add(node);
                    completeFactions.Add(faction.ValueRO.Value);
                    completeCultures.Add(culture);
                    completePositions.Add(myPos);
                }
            }

            // Phase 1 apply: starts.
            for (int i = 0; i < startList.Length; i++)
            {
                Entity acolyte = startList[i];
                Entity node = startNodes[i];
                Faction f = em.HasComponent<FactionTag>(acolyte)
                    ? em.GetComponentData<FactionTag>(acolyte).Value : Faction.Blue;
                byte culture = em.HasComponent<FactionProgress>(acolyte)
                    ? em.GetComponentData<FactionProgress>(acolyte).Culture : Cultures.Runai;
                if (culture == Cultures.None) culture = Cultures.Runai;

                em.AddComponentData(acolyte, new RitualState
                {
                    Kind = RitualKind.Conversion,
                    TargetNode = node,
                    Progress = 0f,
                    TotalDuration = ConversionChannelTime,
                });

                var active = new ActiveRitualOnNode
                {
                    Ritualist = acolyte,
                    Kind = RitualKind.Conversion,
                    RitualistFaction = f,
                    RitualistCulture = culture,
                    DefenseSpawnTimer = RitualDefenseMaxInterval,
                    DefendersSpawned = 0,
                };
                if (em.HasComponent<ActiveRitualOnNode>(node))
                    em.SetComponentData(node, active);
                else
                    em.AddComponentData(node, active);

                if (em.HasComponent<DesiredDestination>(acolyte))
                    em.SetComponentData(acolyte, new DesiredDestination { Has = 0 });

                // THE WAKING (canon §2.8): touching a well wakes THAT well —
                // it starts feeding the veil and never sleeps again. Fires on
                // channel START, not completion, so an interrupted attempt has
                // still armed the region: no safe probe, no take-backs.
                CurseAwakeningHelper.Wake(em, node, f, SystemAPI.Time.ElapsedTime);

                TWBLog.Log($"[Conversion] channeling started by {f} on node");
            }

            // Phase 1 apply: moves.
            for (int i = 0; i < moveList.Length; i++)
            {
                Entity acolyte = moveList[i];
                if (em.HasComponent<DesiredDestination>(acolyte))
                    em.SetComponentData(acolyte, new DesiredDestination
                    {
                        Position = moveTargets[i],
                        Has = 1,
                    });
            }

            // Phase 2 apply: completions.
            for (int i = 0; i < completeList.Length; i++)
            {
                Entity acolyte = completeList[i];
                Entity node = completeNodes[i];
                Faction newOwner = completeFactions[i];

                BorderNodeStateHelper.SetState(em, node,
                    NodeState.Converted,
                    completeCultures[i],
                    newOwner);

                // Curse & Shardroot canon: per-node Glow rewards are GONE.
                // The claim's ongoing reward is the Pacified well's veilstone
                // trickle (WellHoldIncomeSystem); the only artifact reward is
                // the SHARDROOT if this was the seeded host well.
                bool isSecondary = em.HasComponent<SecondaryBorderLocationTag>(node);
                if (isSecondary)
                {
                    FactionReligionPointsHelper.Refund(em, newOwner, 1);
                }
                else
                {
                    TheWaningBorder.Systems.Border.ShardrootSystem.TryAward(
                        em, node, completePositions[i], RitualKind.Conversion);
                }

                if (em.HasComponent<RitualState>(acolyte))       em.RemoveComponent<RitualState>(acolyte);
                if (em.HasComponent<ConvertNodeCommand>(acolyte)) em.RemoveComponent<ConvertNodeCommand>(acolyte);
                if (em.HasComponent<ActiveRitualOnNode>(node))   em.RemoveComponent<ActiveRitualOnNode>(node);

                // The dramatic moment (spec §5.4): nearby border defenders that
                // were attacking the ritual flip to the new owner's faction.
                int flipped = FlipNearbyBorderDefenders(em, completePositions[i], newOwner);

                TWBLog.Log(isSecondary
                    ? $"[Conversion] secondary node converted by {newOwner} — +1 RP, {flipped} defenders flipped"
                    : $"[Conversion] complete — node converted by {newOwner}, {flipped} border defenders flipped, Glow pickup spawned");
            }

            // Apply cancellations.
            for (int i = 0; i < cancelList.Length; i++)
                CancelRitual(em, cancelList[i], cancelNodes[i], "interrupt / invalid target");

            // ORPHAN SWEEP (added 2026-08-07). This system was the only one of
            // the three verbs without one; CorruptionRitualSystem's header
            // calls out the gap here by name.
            //
            // The failure it guards against: an Acolyte that dies mid-channel
            // (or is cancelled by ClearAllCommands) leaves ActiveRitualOnNode
            // on the well. Every ritual system and every AI well-picker skips
            // an already-claimed node, so a stale claim removes that well from
            // the match for EVERY player, silently and permanently — an
            // unrecoverable denial of a shared victory objective.
            //
            // It is not currently reachable, and the reason is an accident
            // worth knowing about: PurificationRitualSystem's own sweep is
            // UNFILTERED (it ignores ActiveRitualOnNode.Kind) and that system
            // is ungated, so it has been quietly cleaning up after all three
            // verbs. That makes the whole Border stack's cleanup depend on a
            // side effect of the Alanthor system — one `RequireForUpdate
            // <ScholarTag>` added there as an obvious optimisation and every
            // verb loses orphan cleanup at once. This sweep is Kind-filtered
            // so Conversion owns its own claims either way.
            var orphanNodes = new NativeList<Entity>(2, Allocator.Temp);
            foreach (var (active, nodeEntity) in SystemAPI
                .Query<RefRO<ActiveRitualOnNode>>()
                .WithEntityAccess())
            {
                // Only our own verb's claims — purify/corrupt sweep their own.
                if (active.ValueRO.Kind != RitualKind.Conversion) continue;

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

        /// <summary>
        /// Flip every Faction.Border unit inside ConversionFlipRadius of the
        /// completion point to <paramref name="newOwner"/>. Removes their
        /// BorderWaveOrder + Target so they re-acquire targets based on the
        /// new faction. Returns the number flipped.
        /// </summary>
        private static int FlipNearbyBorderDefenders(EntityManager em, float3 center, Faction newOwner)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderUnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            using var transforms = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var factions = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var healths = q.ToComponentDataArray<Health>(Allocator.Temp);

            int flipped = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != Faction.Border) continue;
                if (healths[i].Value <= 0) continue;

                float dxz = math.distance(
                    new float2(transforms[i].Position.x, transforms[i].Position.z),
                    new float2(center.x, center.z));
                if (dxz > ConversionFlipRadius) continue;

                em.SetComponentData(entities[i], new FactionTag { Value = newOwner });
                if (em.HasComponent<BorderWaveOrder>(entities[i]))
                    em.RemoveComponent<BorderWaveOrder>(entities[i]);
                if (em.HasComponent<Target>(entities[i]))
                    em.SetComponentData(entities[i], new Target { Value = Entity.Null });
                flipped++;
            }
            return flipped;
        }
    }
}
