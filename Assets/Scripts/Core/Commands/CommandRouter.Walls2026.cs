// CommandRouter.Walls2026.cs
// The three orders the 2026-09-21 wall pass added
// (docs/Design/Age_1_Alanthor.md § Wall levels, the gate structure and
// emplacements):
//
//   SetGateLock     — seal a gate shut, or hand it back to proximity.
//   GarrisonWall    — put a foot unit inside a reinforced curtain module.
//   UngarrisonWall  — empty a module's slots back onto the ground.
//
// plus, 2026-09-25, ReplaceEquipment — pay to re-arm a wall emplacement whose
// engine was destroyed (the free auto-rebuild is gone).
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

        // ═══════════════════════════════════════════════════════════════
        // EMPLACEMENTS — wall-mount only, paid Replace Equipment (2026-09-25)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// The free-standing emplacement platforms. Emplacements are
        /// WALL-MOUNT ONLY (docs/Design/Age_1_Alanthor.md § Ballista and
        /// Trebuchet emplacements), so no placement path may create one; the
        /// ids stay registered only because their SOs carry the mount price.
        /// </summary>
        public static bool IsWallMountOnlyBuilding(string buildingId)
            => buildingId == BallistaEmplacement.Id || buildingId == TrebuchetEmplacement.Id;

        /// <summary>Metres between ownership samples along a wall line.</summary>
        private const float WallGroundSampleStep = 1f;

        /// <summary>
        /// A WALL STANDS ONLY ON GROUND ITS OWNER HOLDS (Territory_Claims.md §5,
        /// 2026-09-30) — every hub AND every metre of curtain between them. The
        /// player's wall tool checked this at the click; the executors did not,
        /// so the AI (which calls them directly) raised walls on enemy ground,
        /// and a curtain between two owned hubs could still cut across a
        /// neighbour's corner. Checked here, before any spend, on every peer,
        /// from replicated ownership. Fails open on a map with no partition,
        /// exactly as the ordinary build gate does.
        /// </summary>
        public static bool WallLineOnOwnGround(EntityManager em, Faction faction,
            System.Collections.Generic.IReadOnlyList<float3> line)
        {
            if (line == null || line.Count == 0) return true;
            if (!WallPointOnOwnGround(em, faction, line[0])) return false;
            for (int i = 1; i < line.Count; i++)
            {
                float3 a = line[i - 1], b = line[i];
                float len = math.distance(a.xz, b.xz);
                int steps = (int)math.ceil(len / WallGroundSampleStep);
                for (int s = 1; s <= steps; s++)
                    if (!WallPointOnOwnGround(em, faction, math.lerp(a, b, s / (float)steps)))
                        return false;
            }
            return true;
        }

        public static bool WallPointOnOwnGround(EntityManager em, Faction faction, float3 p)
            => TheWaningBorder.World.Regions.TerritoryOwnership.CanBuildAt(
                   em, faction, "Alanthor_Wall", p.x, p.z);

        // ── A wall is collision-aware along its WHOLE length (2026-10-02) ──
        //
        // Only the new hub points used to be tested; the curtain between them
        // was laid through buildings, nodes, other walls and cliffs. Every
        // drawn or extended wall is now tested along its full length against
        // the nav grid's occupancy (NavGridQuery.WallBlockedAt — the same
        // cells building placement tests walls against), plus resource nodes
        // and the owner's own plans, in the executor, before any spend, on
        // every peer. docs/Design/Build_Grid.md § Walls on the grid.

        // ── A wall is paid PER MODULE (2026-10-02) ────────────────────────
        //
        // The curtain used to be free: only hubs cost. Every 3 m module now
        // has its price (the curtain SO's cost — Segment/WallSegment.asset for
        // stone, Palisade/PalisadeSegment.asset for the fence), charged in
        // the executor for exactly the modules it lays (AlanthorWall.ModuleCount,
        // the factories' own rounding). docs/Design/Age_1_Alanthor.md § The
        // stone wall, Age_0.md § Palisade.

        /// <summary>The price of ONE curtain module of this kind.</summary>
        public static Cost WallModuleCost(bool palisade)
            => TheWaningBorder.Data.BuildCosts.TryGet(TheWaningBorder.Entities.AlanthorWall.SegmentIdFor(palisade), out var c) ? c : default;

        /// <summary>The curtain price of a run <paramref name="length"/> m long.</summary>
        public static Cost WallRunCost(bool palisade, float length)
            => WallModuleCost(palisade) * TheWaningBorder.Entities.AlanthorWall.ModuleCount(length);

        /// <summary>XZ length of a polyline.</summary>
        public static float PolylineLength(System.Collections.Generic.IReadOnlyList<float3> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += math.distance(pts[i - 1].xz, pts[i].xz);
            return l;
        }

        /// <summary>
        /// Everything a drawn wall will charge: its new hubs (NewHub points,
        /// and wall cells it converts — CellHub) at the hub price, and every
        /// curtain module of every run between two hub points. Split at the
        /// hub points exactly as <see cref="PlaceWallPathDirect"/> lays it, so
        /// the draw tool's price is the executor's.
        /// </summary>
        public static Cost WallPathCost(System.Collections.Generic.IReadOnlyList<float3> pts,
            System.Collections.Generic.IReadOnlyList<WallPathKind> kinds, bool palisade)
        {
            if (!TheWaningBorder.Data.BuildCosts.TryGet(TheWaningBorder.Entities.AlanthorWall.HubIdFor(palisade), out var hub)) hub = default;
            Cost total = default;
            float run = 0f;
            bool havePrev = false;
            for (int i = 0; i < pts.Count; i++)
            {
                var k = i < kinds.Count ? kinds[i] : WallPathKind.Point;
                if (havePrev) run += math.distance(pts[i - 1].xz, pts[i].xz);
                if (k == WallPathKind.Point) continue;
                if (k == WallPathKind.NewHub || k == WallPathKind.CellHub) total = total + hub;
                if (havePrev && run > 0.01f) total = total + WallRunCost(palisade, run);
                havePrev = true;
                run = 0f;
            }
            return total;
        }

        /// <summary>Within this of a STANDING hub or wall cell the new wall
        /// is joining, the line may touch wall: that is the joint.</summary>
        public static float WallJunctionClearance => TheWaningBorder.Entities.AlanthorWall.HubRadius + 2.5f;

        /// <summary>
        /// True when the wall's cross-section at <paramref name="p"/> (heading
        /// <paramref name="tangent"/>) is free on the grid: no building,
        /// obstacle, wall or impassable terrain under the visible wall.
        /// Grid-only, so the draw tool can ask it every frame.
        /// </summary>
        public static bool WallCrossSectionClear(float3 p, float3 tangent, bool palisade)
        {
            float3 t = new float3(tangent.x, 0f, tangent.z);
            t = math.lengthsq(t) > 1e-6f ? math.normalize(t) : new float3(0f, 0f, 1f);
            float3 right = new float3(t.z, 0f, -t.x);
            float half = TheWaningBorder.Entities.AlanthorWall.DepthOf(palisade) * 0.5f;
            for (float o = -half; o <= half + 1e-3f; o += 1f)
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.WallBlockedAt(p + right * o))
                    return false;
            return true;
        }

        /// <summary>
        /// The whole-length test: every metre of <paramref name="line"/>
        /// clear (<see cref="WallCrossSectionClear"/>), and — every two
        /// metres — no resource node and none of the owner's plans under it.
        /// Samples within <see cref="WallJunctionClearance"/> of a
        /// <paramref name="junctions"/> point (a standing hub or cell the wall
        /// attaches to) are exempt.
        /// </summary>
        public static bool WallLineClear(EntityManager em, Faction faction,
            System.Collections.Generic.IReadOnlyList<float3> line,
            System.Collections.Generic.IReadOnlyList<float3> junctions, bool palisade)
        {
            if (line == null || line.Count < 2) return true;
            float jr = WallJunctionClearance;
            float depth = TheWaningBorder.Entities.AlanthorWall.DepthOf(palisade);
            var boxSize = new int2(math.max(1, (int)math.ceil(depth)), math.max(1, (int)math.ceil(depth)));
            float sinceBox = 2f;
            for (int i = 1; i < line.Count; i++)
            {
                float3 a = line[i - 1], b = line[i];
                float len = math.distance(a.xz, b.xz);
                if (len < 1e-4f) continue;
                float3 tan = (b - a) / len;
                int steps = math.max(1, (int)math.ceil(len / WallGroundSampleStep));
                for (int s = 0; s <= steps; s++)
                {
                    float3 p = math.lerp(a, b, s / (float)steps);
                    if (NearJunction(p, junctions, jr)) continue;
                    if (!WallCrossSectionClear(p, tan, palisade)) return false;
                    sinceBox += len / steps;
                    if (sinceBox < 2f) continue;
                    sinceBox = 0f;
                    TheWaningBorder.Core.Commands.Types.BuildCommandHelper.FootprintAabb(p, boxSize, out float2 mn, out float2 mx);
                    if (TheWaningBorder.Entities.ResourceNodeSite.OverlapsNode(em, mn, mx, null)) return false;
                    if (TheWaningBorder.Entities.PlannedBuildings.OverlapsOwnPlan(em, faction, p, boxSize)) return false;
                }
            }
            return true;
        }

        static bool NearJunction(float3 p, System.Collections.Generic.IReadOnlyList<float3> junctions, float r)
        {
            if (junctions == null) return false;
            for (int i = 0; i < junctions.Count; i++)
                if (math.distancesq(p.xz, junctions[i].xz) < r * r) return true;
            return false;
        }

        /// <summary>
        /// Re-arm an EMPTY emplacement: pay the engine SO's cost and start its
        /// restore timer (trainingTime). The engine is raised by
        /// EmplacementCrewSystem when the timer ends; there is no worker.
        /// Replicates — the spend and the timer happen in the executor on
        /// every peer, never at the click site.
        /// </summary>
        public static void IssueReplaceEquipment(EntityManager em, Entity platform,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (!EmplacementEquipment.CanReplace(em, platform)) return;
            if (IsBlockedByNotControllable(em, platform, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                int platformId = GetNetworkId(em, platform);
                if (platformId <= 0)
                {
                    if (!MayExecuteLocally(em, platform, "ReplaceEquipment")) return;
                    ReplaceEquipmentDirect(em, platform);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.ReplaceEquipment,
                    EntityNetworkId = platformId,
                });
            }
            else
            {
                ReplaceEquipmentDirect(em, platform);
            }
        }

        /// <summary>
        /// Executor — runs on every peer. Validates (a finished, owned
        /// platform whose engine is gone and not already restoring) BEFORE the
        /// spend, so a replayed duplicate can never double-charge and a short
        /// bank rejects identically everywhere. Price and duration are read
        /// from the engine's SO, so they cannot differ between peers.
        /// </summary>
        public static bool ReplaceEquipmentDirect(EntityManager em, Entity platform)
        {
            if (!EmplacementEquipment.CanReplace(em, platform)) return false;

            string engineId = EmplacementEquipment.EngineIdOf(em, platform);
            if (string.IsNullOrEmpty(engineId)) return false;

            var faction = em.GetComponentData<FactionTag>(platform).Value;
            var cost = EmplacementEquipment.CostOf(engineId);
            if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Units))
                return false;

            float seconds = EmplacementEquipment.SecondsOf(engineId);
            var crew = em.GetComponentData<EmplacementCrew>(platform);
            crew.Engine = Entity.Null;
            // A zero trainingTime would read as "not restoring" and strand
            // the paid order; the crew system raises on the next tick instead.
            crew.Restore = seconds > 0f ? seconds : 1e-4f;
            crew.RestoreTime = crew.Restore;
            em.SetComponentData(platform, crew);
            return true;
        }
    }
}
