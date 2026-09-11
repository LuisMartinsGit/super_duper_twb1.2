// VeilNavStampSystem.cs
// Mirrors the Veil's crust (VeilField saturation >= CrustThreshold) into the
// deterministic nav cost field. Two modes (§2.5b, 2026-08-03 rev.2):
//
//   * TRAVEL COST (TravelCostEnabled, current) — crusted cells stamp a
//     FINITE cost scaled by saturation (TravelCostMin..Max): flow-field
//     routing automatically prefers clean ground and cuts through the
//     curse only when the detour is dearer than the exposure. Deep crust
//     is a soft wall; the map's topology degrades as the veil spreads.
//   * ABSOLUTE WALL (CrustPhysical, retired) — the old model: crusted
//     cells stamp CostImpassable and units path around, never through.
//
// With both constants false the system disables itself in OnCreate and the
// curse never touches the nav grid.
//
// Why a separate system (not part of CostFieldStampSystem):
//   * CostFieldStampSystem is change-GATED on the stampable ENTITY set
//     (buildings/walls/obstacles never move), and it CLEARS layer-0 back to
//     the baked TerrainCost on every restamp. The crust, by contrast, moves
//     every burst and is not an entity. Folding it into that system would
//     either be wiped by the clear or force a full re-stamp every crust tick.
//   * So we run AFTER CostFieldStampSystem and stamp ON TOP of its snapshot,
//     tracking our own cells via NavCostField.FlagCrust so we can REVERT a
//     cell to its terrain baseline the moment the crust recedes (mined out /
//     decayed / pushed back by player influence) without disturbing cells that
//     are impassable for a structural reason.
//
// Self-healing after a CostFieldStampSystem restamp: that pass clears Flags
// (dropping our FlagCrust) and resets Cost to TerrainCost, wiping the crust
// wall. We detect it via the cost field's Generation bump (navWiped) and
// re-apply every crusted cell that same frame — so the wall never blinks.
//
// Determinism: reads the lockstep-identical VeilField saturation + the nav
// cost field; integer cell math; no wall-clock. Every client stamps identically.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    // Must land after the building/terrain snapshot so the crust stamps on top
    // of it (and so we observe its Generation bump to re-heal after a restamp).
    [UpdateAfter(typeof(CostFieldStampSystem))]
    public partial class VeilNavStampSystem : SystemBase
    {
        // Which nav cells WE stamped as crust last pass (parallel to the cost
        // field, layer-0). Lets us revert exactly the cells that receded.
        private NativeArray<byte> _stampedCrust;
        private int _lastVeilGen = int.MinValue;
        private int _lastNavGen = int.MinValue;
        private byte _stampedOnce;
        private int _epoch = -1;

        protected override void OnCreate()
        {
            // Pure influence-only veil (both modes off): never stamp the nav
            // grid. (Assigning through Enabled keeps the compiler from folding
            // the consts into unreachable-code noise.)
            Enabled = TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                || TheWaningBorder.Core.Config.VeilCrustConstants.TravelCostEnabled;
            if (!Enabled) return;
            RequireForUpdate<VeilField>();
            RequireForUpdate<NavCostField>();
        }

        /// <summary>Finite travel cost for a crusted cell: saturation-scaled
        /// between TravelCostMin (at CrustThreshold) and TravelCostMax (at
        /// 255), never cheaper than the terrain baseline under it, and never
        /// downgrading genuinely impassable terrain.</summary>
        internal static byte TravelCost(byte sat, byte terrain)
        {
            if (terrain == NavCostField.CostImpassable) return terrain;
            float t = (sat - VeilField.CrustThreshold)
                / (float)(255 - VeilField.CrustThreshold);
            byte c = (byte)math.round(math.lerp(
                TheWaningBorder.Core.Config.VeilCrustConstants.TravelCostMin,
                TheWaningBorder.Core.Config.VeilCrustConstants.TravelCostMax,
                math.saturate(t)));
            return c > terrain ? c : terrain;
        }

        protected override void OnDestroy()
        {
            if (_stampedCrust.IsCreated) _stampedCrust.Dispose();
        }

        protected override void OnUpdate()
        {
            var field = SystemAPI.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return;

            var nav = SystemAPI.GetSingleton<NavCostField>();
            if (!nav.Cost.IsCreated) return;

            float navCell = SystemAPI.HasSingleton<NavGridSingleton>()
                ? SystemAPI.GetSingleton<NavGridSingleton>().CellSize : 1f;
            float3 navOrigin = SystemAPI.HasSingleton<NavGridSingleton>()
                ? SystemAPI.GetSingleton<NavGridSingleton>().Origin : float3.zero;

            int navCells = nav.Width * nav.Height;
            // Per-match reset. The bitmap below is only re-allocated when the
            // grid SIZE changes, so a peer whose previous match was on the
            // same map walked into the next one with the old crust bitmap
            // and the latches set — and only that peer. Headless warm-up
            // runs 2026-09-11: the one peer that had warmed on the MP map
            // forked its nav on the same tick with the same checksums every
            // time. Dropping the bitmap forces the full re-evaluation the
            // size-change branch already does.
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                if (_stampedCrust.IsCreated) _stampedCrust.Dispose();
                _stampedCrust = default;
                _stampedOnce = 0;
                _lastVeilGen = int.MinValue;
                _lastNavGen = int.MinValue;
            }
            if (!_stampedCrust.IsCreated || _stampedCrust.Length != navCells)
            {
                if (_stampedCrust.IsCreated) _stampedCrust.Dispose();
                _stampedCrust = new NativeArray<byte>(navCells, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                _stampedOnce = 0; // force a full re-evaluation against the fresh grid
            }

            // A CostFieldStampSystem restamp (buildings changed / terrain baked)
            // clears our crust wall; its Generation bump tells us to re-apply.
            bool navWiped = nav.Generation != _lastNavGen;
            bool veilChanged = field.Generation != _lastVeilGen;
            if (_stampedOnce != 0 && !navWiped && !veilChanged) return;

            // MP DESYNC INSTRUMENTATION (2026-09-04, temporary): one line per
            // crust pass so two peers' logs can be diffed — the tick-8600-class
            // fork was one peer stamping a pass the other skipped.
            if (TheWaningBorder.Multiplayer.LockstepManager.Instance != null
                && TheWaningBorder.Multiplayer.LockstepManager.Instance.IsSimulationRunning)
                UnityEngine.Debug.Log(
                    $"[VeilNavStamp] tick={TheWaningBorder.Multiplayer.LockstepManager.Instance.CurrentTick} " +
                    $"navWiped={navWiped}({nav.Generation}/{_lastNavGen}) " +
                    $"veilChanged={veilChanged}({field.Generation}/{_lastVeilGen}) once={_stampedOnce}");

            float veilCell = field.CellSize;
            float2 veilOrigin = field.Origin;

            // The full-grid stamp/heal pass is Burst-compiled via Run() —
            // as a managed SystemBase loop over 1M+ cells it cost 17-32 ms
            // and landed in the SAME frame as a building's cost restamp and
            // portal rebuild, stacking the hitch. Same-tick synchronous, so
            // lockstep timing is unchanged.
            bool emptyTerrain = !nav.TerrainCost.IsCreated;
            var terrainCost = emptyTerrain
                ? new NativeArray<byte>(0, Allocator.TempJob)
                : nav.TerrainCost;
            var changedRef = new NativeReference<byte>(Allocator.TempJob);
            // .Run() executes inline and does NOT wait on this system's
            // Dependency, so without this it raced the building-stamp chain
            // CostFieldStampSystem had just scheduled on worker threads —
            // both sides read and write nav.Cost, and which finished first
            // was wall-clock: a peer running several ticks a frame to catch
            // up saw a different field than one keeping pace (headless
            // warm-up runs 2026-09-11 forked on exactly these restamp ticks).
            Dependency.Complete();
            new StampCrustJob
            {
                Saturation = field.Saturation,
                VeilWidth = field.Width,
                VeilHeight = field.Height,
                VeilCell = veilCell,
                VeilOrigin = veilOrigin,
                NavCost = nav.Cost,
                NavFlags = nav.Flags,
                TerrainCost = terrainCost,
                HasTerrainCost = !emptyTerrain,
                NavWidth = nav.Width,
                NavHeight = nav.Height,
                NavCellSize = navCell,
                NavOrigin = navOrigin,
                StampedCrust = _stampedCrust,
                NavWiped = navWiped,
                CrustPhysical = TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical,
                Changed = changedRef,
            }.Run();
            bool changed = changedRef.Value != 0;
            changedRef.Dispose();
            if (emptyTerrain) terrainCost.Dispose();

            if (changed)
            {
                nav.Generation++;
                SystemAPI.SetSingleton(nav);
            }
            _lastNavGen = nav.Generation; // post-bump: our own bump is not a "wipe"
            _lastVeilGen = field.Generation;
            _stampedOnce = 1;
        }
    }

    /// <summary>
    /// The crust stamp/heal pass over the whole nav grid, Burst-compiled
    /// and invoked synchronously with Run(). Behaviour is identical to the
    /// managed loop it replaces (see the class header for the ownership
    /// rules); Changed reports whether any cell was written so the caller
    /// knows to bump NavCostField.Generation.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct StampCrustJob : IJob
    {
        // Structural impassability we must never overwrite with crust nor
        // revert to walkable — a building/wall/gate owns those cells.
        private const byte Structural = (byte)(NavCostField.FlagBuildingFootprint
            | NavCostField.FlagStaticWall | NavCostField.FlagGate);

        [ReadOnly] public NativeArray<byte> Saturation;
        public int VeilWidth;
        public int VeilHeight;
        public float VeilCell;
        public float2 VeilOrigin;
        public NativeArray<byte> NavCost;
        public NativeArray<byte> NavFlags;
        [ReadOnly] public NativeArray<byte> TerrainCost;
        public bool HasTerrainCost;
        public int NavWidth;
        public int NavHeight;
        public float NavCellSize;
        public float3 NavOrigin;
        public NativeArray<byte> StampedCrust;
        public bool NavWiped;
        public bool CrustPhysical;
        public NativeReference<byte> Changed;

        public void Execute()
        {
            bool changed = false;

            for (int nz = 0; nz < NavHeight; nz++)
            {
                float cz = NavOrigin.z + (nz + 0.5f) * NavCellSize;
                int vz = (int)math.floor((cz - VeilOrigin.y) / VeilCell);
                bool zIn = vz >= 0 && vz < VeilHeight;
                int navRow = nz * NavWidth;
                for (int nx = 0; nx < NavWidth; nx++)
                {
                    int idx = navRow + nx;
                    byte sat = 0;
                    if (zIn)
                    {
                        float cx = NavOrigin.x + (nx + 0.5f) * NavCellSize;
                        int vx = (int)math.floor((cx - VeilOrigin.x) / VeilCell);
                        if (vx >= 0 && vx < VeilWidth)
                            sat = Saturation[vz * VeilWidth + vx];
                    }
                    bool crust = sat >= VeilField.CrustThreshold;

                    bool was = StampedCrust[idx] != 0;
                    bool structural = (NavFlags[idx] & Structural) != 0;

                    if (crust && !structural)
                    {
                        // Wall mode: impassable. Travel-cost mode: finite,
                        // saturation-scaled (deepening crust re-stamps via the
                        // want-compare below). Re-apply on NavWiped even if we
                        // think we already own it — the restamp may have
                        // cleared our flag.
                        byte want = CrustPhysical
                            ? NavCostField.CostImpassable
                            : VeilNavStampSystem.TravelCost(sat, HasTerrainCost
                                ? TerrainCost[idx] : (byte)0);
                        if (!was || NavWiped
                            || NavCost[idx] != want
                            || (NavFlags[idx] & NavCostField.FlagCrust) == 0)
                        {
                            NavCost[idx] = want;
                            NavFlags[idx] = (byte)(NavFlags[idx] | NavCostField.FlagCrust);
                            changed = true;
                        }
                        StampedCrust[idx] = 1;
                    }
                    else if (was)
                    {
                        // Crust receded here (or a building claimed the cell).
                        // Only touch Cost if WE own it via FlagCrust — never
                        // re-open a structural cell.
                        if ((NavFlags[idx] & NavCostField.FlagCrust) != 0)
                        {
                            NavCost[idx] = HasTerrainCost
                                ? TerrainCost[idx] : (byte)0;
                            NavFlags[idx] = (byte)(NavFlags[idx] & ~NavCostField.FlagCrust);
                            changed = true;
                        }
                        StampedCrust[idx] = 0;
                    }
                }
            }

            Changed.Value = changed ? (byte)1 : (byte)0;
        }
    }
}
