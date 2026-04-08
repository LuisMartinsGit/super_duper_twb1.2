// AICrystalHuntBehavior.cs
// Sends idle military units to hunt crystal faction entities near the AI base
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// AI behavior that hunts crystal faction entities.
    /// Finds Faction.White crystal entities within range of base and sends idle
    /// military units to attack them, creating cadavers for miners to harvest.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AIMilitaryManager))]
    public partial struct AICrystalHuntBehavior : ISystem
    {
        // Fix #231: constants moved to AITuning.CrystalHuntRange /
        // MaxCrystalHuntersPerTarget. Kept a local alias for HUNT_RANGE
        // because the field is read in a Burst-friendly hot path.
        private static readonly float HUNT_RANGE = AITuning.CrystalHuntRange;
        private static readonly int MAX_HUNTERS_PER_TARGET = AITuning.MaxCrystalHuntersPerTarget;

        private struct DeferredAttack
        {
            public Entity Unit;
            public Entity Target;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            var deferredAttacks = new NativeList<DeferredAttack>(Allocator.Temp);

            foreach (var (brain, huntState, entity)
                in SystemAPI.Query<RefRO<AIBrain>, RefRW<AICrystalHuntState>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                // Fix #201: write directly through ValueRW so LastHuntCheck persists.
                // Previously the code copied into a local struct and never wrote it back,
                // causing the throttle to be bypassed and the hunt to run every frame.
                if (time < huntState.ValueRO.LastHuntCheck + huntState.ValueRO.HuntCheckInterval) continue;
                huntState.ValueRW.LastHuntCheck = time;

                AssignHunters(ref state, brain.ValueRO.Owner, ref deferredAttacks);
            }

            // Execute all deferred attack commands after iteration
            for (int i = 0; i < deferredAttacks.Length; i++)
            {
                AICommandAdapter.IssueAttack(em, deferredAttacks[i].Unit, deferredAttacks[i].Target);
            }

            deferredAttacks.Dispose();
        }

        private void AssignHunters(ref SystemState state, Faction faction,
            ref NativeList<DeferredAttack> deferredAttacks)
        {
            var em = state.EntityManager;

            // Find base position
            float3 basePos = float3.zero;
            bool foundBase = false;
            foreach (var (factionTag, transform, building) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<BuildingTag>>())
            {
                if (factionTag.ValueRO.Value == faction && building.ValueRO.IsBase == 1)
                {
                    basePos = transform.ValueRO.Position;
                    foundBase = true;
                    break;
                }
            }
            if (!foundBase) return;

            // Collect crystal faction entities within hunt range of base
            // First: crystal units (primary targets)
            var creatures = new NativeList<Entity>(Allocator.Temp);
            var creaturePositions = new NativeList<float3>(Allocator.Temp);

            foreach (var (factionTag, transform, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<CrystalTag, UnitTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != Faction.White) continue;

                float dist = math.distance(basePos, transform.ValueRO.Position);
                if (dist <= HUNT_RANGE)
                {
                    creatures.Add(entity);
                    creaturePositions.Add(transform.ValueRO.Position);
                }
            }

            // Second: crystal buildings as fallback targets (anything crystal with Health)
            foreach (var (factionTag, transform, health, entity) in
                SystemAPI.Query<RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<Health>>()
                .WithAll<CrystalTag, BuildingTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != Faction.White) continue;

                float dist = math.distance(basePos, transform.ValueRO.Position);
                if (dist <= HUNT_RANGE)
                {
                    creatures.Add(entity);
                    creaturePositions.Add(transform.ValueRO.Position);
                }
            }

            if (creatures.Length == 0)
            {
                creatures.Dispose();
                creaturePositions.Dispose();
                return;
            }

            // Collect idle military units (not in army, no active target, no destination)
            var idleHunters = new NativeList<Entity>(Allocator.Temp);

            foreach (var (unitTag, factionTag, transform, entity) in
                SystemAPI.Query<RefRO<UnitTag>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithNone<ArmyTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                if (unitTag.ValueRO.Class != UnitClass.Melee &&
                    unitTag.ValueRO.Class != UnitClass.Ranged) continue;

                // Skip if already has a target
                if (em.HasComponent<Target>(entity))
                {
                    var target = em.GetComponentData<Target>(entity);
                    if (target.Value != Entity.Null && em.Exists(target.Value))
                        continue;
                }

                // Skip if already moving somewhere
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    var dest = em.GetComponentData<DesiredDestination>(entity);
                    if (dest.Has == 1)
                        continue;
                }

                idleHunters.Add(entity);
            }

            // Assign hunters to creatures (round-robin, max per target)
            int hunterIdx = 0;
            for (int c = 0; c < creatures.Length && hunterIdx < idleHunters.Length; c++)
            {
                for (int h = 0; h < MAX_HUNTERS_PER_TARGET && hunterIdx < idleHunters.Length; h++)
                {
                    deferredAttacks.Add(new DeferredAttack
                    {
                        Unit = idleHunters[hunterIdx],
                        Target = creatures[c]
                    });
                    hunterIdx++;
                }
            }

            creatures.Dispose();
            creaturePositions.Dispose();
            idleHunters.Dispose();
        }
    }
}
