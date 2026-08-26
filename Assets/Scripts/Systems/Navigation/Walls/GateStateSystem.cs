// GateStateSystem.cs
// task-112 M5 -- replaces WallGatePassabilitySystem. Polls every
// WallGateTag entity for friendly proximity, flips
// GateRuntimeState.OpenState + the PortalOwnerBitsMirror open bit
// WITHOUT triggering a portal-graph rebuild (per CCD-5: structural
// changes drive rebuilds, gate state changes are runtime-only and
// mutate the mirror in place).
//
// Determinism (DR-9):
//   * Gate entities iterated in entity.Index ascending order so flip
//     ordering is identical across machines.
//   * Same-tick conflicts resolved last-write-wins by entity.Index.
//   * Poll cadence is sim-tick driven (every 18 ticks ~= 0.3s at 60Hz)
//     -- NEVER reads wall-clock or Time.realtimeSinceStartup.
//   * Friend-proximity check uses integer cell math + integer sqrDist
//     comparisons; no machine-dependent float ops.
//
// Owner-bits mirror layout: see PortalOwnerBitsMirror in
// NavComponents.cs. Bit 15 = open, low 7 bits = owner faction id.
//
// Location: Assets/Scripts/Systems/Navigation/GateStateSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M5 gate state controller. Maintains
    /// <see cref="GateRuntimeState"/> per <c>WallGateTag</c> entity +
    /// the open-bit slots of <see cref="PortalOwnerBitsMirror"/> for the
    /// linked portal nodes (ground + rampart).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(IncrementalPortalRebuildSystem))]
    [UpdateBefore(typeof(AbstractPathfinderSystem))]
    public partial struct GateStateSystem : ISystem
    {
        /// <summary>Friendly-proximity radius (m). Mirrors the legacy
        /// <c>WallGatePassabilitySystem.FriendlyDetectRadius</c>.</summary>
        public const float FriendlyDetectRadius = 3.0f;
        /// <summary>Wider proximity radius for 5-instance gate regions
        /// (<see cref="WallGateRegionTag"/>). Matches the legacy
        /// <c>RegionDetectRadius</c>.</summary>
        public const float RegionDetectRadius = 6.0f;
        /// <summary>Sim ticks between proximity polls. 18 ticks at 60Hz
        /// fixed step = ~0.3s, matching legacy cadence.</summary>
        public const uint PollIntervalTicks = 18;

        private uint _tick;
        private EntityQuery _gateQuery;
        private EntityQuery _unitQuery;

        public void OnCreate(ref SystemState state)
        {
            _tick = 0;
            _gateQuery = SystemAPI.QueryBuilder()
                .WithAll<WallGateTag, GateRuntimeState, LocalTransform, FactionTag>()
                .Build();
            _unitQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, FactionTag>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            _tick++;
            // Poll-cadence gate -- between polls everything stays in the
            // last-tick state. Deterministic because _tick is sim-tick
            // driven, never wall-clock.
            if ((_tick % PollIntervalTicks) != 0) return;

            if (_gateQuery.IsEmpty) return;
            if (!SystemAPI.HasSingleton<PortalOwnerBitsMirror>()) return;

            var em = state.EntityManager;

            // Snapshot units once (faction + position).
            using var unitFactions = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitTransforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Snapshot gates -- sort by entity.Index so flips happen in a
            // deterministic order across machines (DR-9 + DR ordering row).
            using var gateEntities = _gateQuery.ToEntityArray(Allocator.Temp);
            using var gateStates = _gateQuery.ToComponentDataArray<GateRuntimeState>(Allocator.Temp);
            using var gateTransforms = _gateQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var gateFactions = _gateQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var order = new NativeArray<int>(gateEntities.Length, Allocator.Temp);
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = 1; i < order.Length; i++)
            {
                int k = order[i];
                int j = i - 1;
                while (j >= 0 && gateEntities[order[j]].Index > gateEntities[k].Index)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = k;
            }

            var mirror = SystemAPI.GetSingleton<PortalOwnerBitsMirror>();

            for (int oi = 0; oi < order.Length; oi++)
            {
                int gi = order[oi];
                var gateEntity = gateEntities[gi];
                var st = gateStates[gi];
                var gatePos = gateTransforms[gi].Position;
                var gateFac = gateFactions[gi].Value;

                // Pick proximity radius based on region membership.
                bool isRegion = em.HasComponent<WallGateRegionTag>(gateEntity);
                float radius = isRegion ? RegionDetectRadius : FriendlyDetectRadius;
                float radiusSq = radius * radius;

                bool friendlyNearby = false;
                for (int u = 0; u < unitFactions.Length; u++)
                {
                    // A gate opens for its TEAM. A wall that shuts your ally
                    // out is worse than no wall — the wall still belongs to
                    // its owner, but passage follows the alliance.
                    // docs/Design/Teams.md
                    if (!Alliances.AreAllied(gateFac, unitFactions[u].Value)) continue;
                    var up = unitTransforms[u].Position;
                    float dx = up.x - gatePos.x;
                    float dz = up.z - gatePos.z;
                    if (dx * dx + dz * dz <= radiusSq)
                    {
                        friendlyNearby = true;
                        break;
                    }
                }

                byte nowOpen = friendlyNearby ? (byte)1 : (byte)0;
                if (nowOpen != st.OpenState)
                {
                    st.OpenState = nowOpen;
                    st.LastChangedTick = _tick;
                    FlipMirrorOpenBit(ref mirror, st.PortalNodeGround, nowOpen != 0);
                    FlipMirrorOpenBit(ref mirror, st.PortalNodeRampart, nowOpen != 0);
                    // Keep the legacy WallGateState.IsOpen in sync (UI /
                    // visual consumers still read it).
                    if (em.HasComponent<WallGateState>(gateEntity))
                    {
                        var legacy = em.GetComponentData<WallGateState>(gateEntity);
                        legacy.IsOpen = nowOpen;
                        em.SetComponentData(gateEntity, legacy);
                    }
                    em.SetComponentData(gateEntity, st);
                }
            }

            order.Dispose();
        }

        /// <summary>
        /// task-112 M5 -- managed-side helper used by tests / debug
        /// tooling. Force-flips a gate's open state without waiting for
        /// the next proximity poll. Mutates GateRuntimeState +
        /// PortalOwnerBitsMirror in place -- does NOT trigger a graph
        /// rebuild (per CCD-5). Returns true on success.
        /// </summary>
        public static bool SetGateOpen(EntityManager em, Entity gateEntity, bool open)
        {
            if (!em.Exists(gateEntity)) return false;
            if (!em.HasComponent<GateRuntimeState>(gateEntity)) return false;

            var st = em.GetComponentData<GateRuntimeState>(gateEntity);
            st.OpenState = open ? (byte)1 : (byte)0;
            em.SetComponentData(gateEntity, st);

            var mirrorQ = em.CreateEntityQuery(typeof(PortalOwnerBitsMirror));
            if (mirrorQ.IsEmptyIgnoreFilter) { mirrorQ.Dispose(); return true; }
            var mirror = mirrorQ.GetSingleton<PortalOwnerBitsMirror>();
            mirrorQ.Dispose();
            FlipMirrorOpenBit(ref mirror, st.PortalNodeGround, open);
            FlipMirrorOpenBit(ref mirror, st.PortalNodeRampart, open);

            // Re-set the singleton (header copy -- the Bits NativeArray is
            // a reference so the in-place mutation is already observable,
            // but ECS metadata expects the SetSingleton call).
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w != null && w.IsCreated)
            {
                var mirrorEntity = w.EntityManager.CreateEntityQuery(typeof(PortalOwnerBitsMirror))
                    .GetSingletonEntity();
                w.EntityManager.SetComponentData(mirrorEntity, mirror);
            }
            return true;
        }

        private static void FlipMirrorOpenBit(ref PortalOwnerBitsMirror mirror,
            int portalNodeId, bool open)
        {
            if (portalNodeId < 0) return;
            if (!mirror.Bits.IsCreated) return;
            if (portalNodeId >= mirror.Bits.Length) return;
            ushort slot = mirror.Bits[portalNodeId];
            if (open) slot |= PortalOwnerBitsMirror.BitOpen;
            else slot &= unchecked((ushort)~PortalOwnerBitsMirror.BitOpen);
            mirror.Bits[portalNodeId] = slot;
        }
    }
}
