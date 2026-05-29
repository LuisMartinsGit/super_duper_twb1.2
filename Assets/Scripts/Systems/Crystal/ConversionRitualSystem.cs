// ConversionRitualSystem.cs
// Runai Conversion ritual (spec §5.4 + §5.5). Mirrors PurificationRitualSystem
// but for acolytes targeting active crystal nodes; on completion the node
// becomes Converted (persists as Runai infrastructure) instead of Cleansed,
// and the curse defenders that were converging on the ritualist flip to
// the acolyte's faction — the spec's signature "swarm turns to face Runai's
// enemies" cinematic.
//
// Mechanical difficulty differential vs Purification (spec §5.5: node fights
// enslavement hardest):
//   - Channel time: ConversionChannelTime (45s) vs Purification 35s
//   - RitualDefenseRunaiIntensity (1.6x) shrinks defender spawn interval
//   - Higher Glow yield (14 vs 10) compensates the difficulty
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
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NodeStateReversionSystem))]
    public partial class ConversionRitualSystem : SystemBase
    {
        private void CancelRitual(EntityManager em, Entity acolyte, Entity node, string reason)
        {
            if (em.Exists(acolyte))
            {
                if (em.HasComponent<RitualState>(acolyte))       em.RemoveComponent<RitualState>(acolyte);
                if (em.HasComponent<ConvertNodeCommand>(acolyte)) em.RemoveComponent<ConvertNodeCommand>(acolyte);
            }
            if (em.Exists(node) && em.HasComponent<ActiveRitualOnNode>(node))
                em.RemoveComponent<ActiveRitualOnNode>(node);

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
                if (!em.Exists(node) || !em.HasComponent<CrystalNodeState>(node))
                {
                    cancelList.Add(entity); cancelNodes.Add(Entity.Null); continue;
                }
                if (em.GetComponentData<CrystalNodeState>(node).State != NodeState.Active)
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
                    moveList.Add(entity); moveTargets.Add(nodePos); continue;
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

                if (!em.Exists(node) || !em.HasComponent<CrystalNodeState>(node))
                {
                    cancelList.Add(entity); cancelNodes.Add(Entity.Null); continue;
                }
                if (em.GetComponentData<CrystalNodeState>(node).State != NodeState.Active)
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

                CrystalNodeStateHelper.SetState(em, node,
                    NodeState.Converted,
                    completeCultures[i],
                    newOwner);

                // Secondary curse-location main nodes yield 1 RP directly to the
                // converting faction instead of dropping a Glow pickup.
                bool isSecondary = em.HasComponent<SecondaryCurseLocationTag>(node);
                if (isSecondary)
                {
                    FactionReligionPointsHelper.Refund(em, newOwner, 1);
                }
                else
                {
                    GlowPickup.Create(em, completePositions[i], RitualKind.Conversion,
                        ConversionGlowYield);
                }

                if (em.HasComponent<RitualState>(acolyte))       em.RemoveComponent<RitualState>(acolyte);
                if (em.HasComponent<ConvertNodeCommand>(acolyte)) em.RemoveComponent<ConvertNodeCommand>(acolyte);
                if (em.HasComponent<ActiveRitualOnNode>(node))   em.RemoveComponent<ActiveRitualOnNode>(node);

                // The dramatic moment (spec §5.4): nearby curse defenders that
                // were attacking the ritual flip to the new owner's faction.
                int flipped = FlipNearbyCurseDefenders(em, completePositions[i], newOwner);

                TWBLog.Log(isSecondary
                    ? $"[Conversion] secondary node converted by {newOwner} — +1 RP, {flipped} defenders flipped"
                    : $"[Conversion] complete — node converted by {newOwner}, {flipped} curse defenders flipped, Glow pickup spawned");
            }

            // Apply cancellations.
            for (int i = 0; i < cancelList.Length; i++)
                CancelRitual(em, cancelList[i], cancelNodes[i], "interrupt / invalid target");

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
        /// Flip every Faction.Curse unit inside ConversionFlipRadius of the
        /// completion point to <paramref name="newOwner"/>. Removes their
        /// CrystalWaveOrder + Target so they re-acquire targets based on the
        /// new faction. Returns the number flipped.
        /// </summary>
        private static int FlipNearbyCurseDefenders(EntityManager em, float3 center, Faction newOwner)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalUnitTag>(),
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
                if (factions[i].Value != Faction.Curse) continue;
                if (healths[i].Value <= 0) continue;

                float dxz = math.distance(
                    new float2(transforms[i].Position.x, transforms[i].Position.z),
                    new float2(center.x, center.z));
                if (dxz > ConversionFlipRadius) continue;

                em.SetComponentData(entities[i], new FactionTag { Value = newOwner });
                if (em.HasComponent<CrystalWaveOrder>(entities[i]))
                    em.RemoveComponent<CrystalWaveOrder>(entities[i]);
                if (em.HasComponent<Target>(entities[i]))
                    em.SetComponentData(entities[i], new Target { Value = Entity.Null });
                flipped++;
            }
            return flipped;
        }
    }
}
