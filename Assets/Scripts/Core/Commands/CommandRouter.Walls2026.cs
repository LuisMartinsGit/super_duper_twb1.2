// CommandRouter.Walls2026.cs
// The three orders the 2026-09-21 wall pass added
// (docs/Design/Age_1_Alanthor.md § Wall levels, the gate structure and
// emplacements):
//
//   SetGateLock     — seal a gate shut, or hand it back to proximity.
//   GarrisonWall    — put a foot unit inside a reinforced curtain module.
//   UngarrisonWall  — empty a module's slots back onto the ground.
//
// All three replicate. A sealed gate changes where an army can path and a
// garrisoned unit stops existing as a target, so a peer that missed one
// diverges within seconds. Each has a *Direct executor that every peer runs
// off the wire, and the issuing side never mutates the world itself.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        // ═══════════════════════════════════════════════════════════════
        // GATE SEAL
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Seal a gate shut (<paramref name="sealShut"/> true) or return it
        /// to the default, which opens for friendlies within the region
        /// radius. A sealed gate is shut to its OWN faction too — it is a
        /// pathing decision, not a defensive one.
        /// </summary>
        public static void IssueSetGateLock(EntityManager em, Entity gate, bool sealShut,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (gate == Entity.Null || !em.Exists(gate)) return;
            if (!em.HasComponent<WallGateTag>(gate)) return;
            if (IsBlockedByNotControllable(em, gate, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                int gateId = GetNetworkId(em, gate);
                if (gateId <= 0)
                {
                    if (!MayExecuteLocally(em, gate, "SetGateLock")) return;
                    SetGateLockDirect(em, gate, sealShut);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SetGateLock,
                    EntityNetworkId = gateId,
                    TargetEntityId = sealShut ? 1 : 0,
                });
            }
            else
            {
                SetGateLockDirect(em, gate, sealShut);
            }
        }

        /// <summary>Executor — runs on every peer.</summary>
        public static void SetGateLockDirect(EntityManager em, Entity gate, bool sealShut)
        {
            if (gate == Entity.Null || !em.Exists(gate)) return;
            if (!em.HasComponent<WallGateTag>(gate)) return;

            em.AddComponentData(gate, new WallGateLock { Sealed = (byte)(sealShut ? 1 : 0) });

            // Shut it on the spot rather than waiting for the next proximity
            // poll — the doors are what the player just clicked.
            if (sealShut)
            {
                if (em.HasComponent<WallGateState>(gate))
                {
                    var st = em.GetComponentData<WallGateState>(gate);
                    st.IsOpen = 0;
                    em.SetComponentData(gate, st);
                }
                TheWaningBorder.Systems.Navigation.GateStateSystem.SetGateOpen(em, gate, false);
            }
        }

        /// <summary>True when the gate is currently sealed by its owner.</summary>
        public static bool IsGateSealed(EntityManager em, Entity gate)
            => em.Exists(gate) && em.HasComponent<WallGateLock>(gate)
               && em.GetComponentData<WallGateLock>(gate).Sealed != 0;

        // ═══════════════════════════════════════════════════════════════
        // WALL GARRISON (reinforced walls only)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Put <paramref name="unit"/> into <paramref name="module"/>'s
        /// garrison. Only a reinforced curtain module has slots, and only a
        /// foot unit may take one (docs/Design/Age_1_Alanthor.md § Garrison
        /// slots).
        /// </summary>
        public static void IssueGarrisonWall(EntityManager em, Entity unit, Entity module,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (!WallGarrison.CanGarrison(em, unit, module)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                int unitId = GetNetworkId(em, unit);
                int moduleId = GetNetworkId(em, module);
                if (unitId <= 0 || moduleId <= 0)
                {
                    if (!MayExecuteLocally(em, unit, "GarrisonWall")) return;
                    WallGarrison.Enter(em, unit, module);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.GarrisonWall,
                    EntityNetworkId = unitId,
                    TargetEntityId = moduleId,
                });
            }
            else
            {
                WallGarrison.Enter(em, unit, module);
            }
        }

        /// <summary>Executor — runs on every peer.</summary>
        public static void GarrisonWallDirect(EntityManager em, Entity unit, Entity module)
            => WallGarrison.Enter(em, unit, module);

        /// <summary>Empty a module's garrison back onto the ground beside it.</summary>
        public static void IssueUngarrisonWall(EntityManager em, Entity module,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (module == Entity.Null || !em.Exists(module)) return;
            if (!em.HasBuffer<WallGarrisonSlot>(module)) return;
            if (IsBlockedByNotControllable(em, module, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                int moduleId = GetNetworkId(em, module);
                if (moduleId <= 0)
                {
                    if (!MayExecuteLocally(em, module, "UngarrisonWall")) return;
                    WallGarrison.EmptyModule(em, module);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.UngarrisonWall,
                    EntityNetworkId = moduleId,
                });
            }
            else
            {
                WallGarrison.EmptyModule(em, module);
            }
        }

        /// <summary>Executor — runs on every peer.</summary>
        public static void UngarrisonWallDirect(EntityManager em, Entity module)
            => WallGarrison.EmptyModule(em, module);

        /// <summary>
        /// The nearest reinforced module of <paramref name="faction"/> with a
        /// free slot, within <paramref name="maxDistance"/> of
        /// <paramref name="near"/>. This is how a right-click on a wall picks
        /// which module the men walk into: the one they clicked if it has
        /// room, otherwise its neighbour.
        /// </summary>
        public static Entity FindGarrisonModuleNear(EntityManager em, float3 near,
            Faction faction, float maxDistance)
        {
            var q = QC_GarrisonModules.Get(em, QT_GarrisonModules);
            if (q.IsEmptyIgnoreFilter) return Entity.Null;

            using var entities = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = maxDistance * maxDistance;
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                if (!WallGarrison.HasFreeSlot(em, e)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - near.x, dz = p.z - near.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        static readonly ComponentType[] QT_GarrisonModules =
        {
            ComponentType.ReadOnly<WallGarrisonSlot>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static TheWaningBorder.Core.CachedEntityQuery QC_GarrisonModules;
    }
}
