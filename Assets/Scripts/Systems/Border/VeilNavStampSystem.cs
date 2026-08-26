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

using Unity.Collections;
using Unity.Entities;
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

        // Structural impassability we must never overwrite with crust nor revert
        // to walkable — a building/wall/gate owns those cells.
        private const byte Structural = (byte)(NavCostField.FlagBuildingFootprint
            | NavCostField.FlagStaticWall | NavCostField.FlagGate);

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
        private static byte TravelCost(byte sat, byte terrain)
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

            float veilCell = field.CellSize;
            float2 veilOrigin = field.Origin;
            bool changed = false;

            for (int nz = 0; nz < nav.Height; nz++)
            {
                float cz = navOrigin.z + (nz + 0.5f) * navCell;
                int vz = (int)math.floor((cz - veilOrigin.y) / veilCell);
                bool zIn = vz >= 0 && vz < field.Height;
                int navRow = nz * nav.Width;
                for (int nx = 0; nx < nav.Width; nx++)
                {
                    int idx = navRow + nx;
                    byte sat = 0;
                    if (zIn)
                    {
                        float cx = navOrigin.x + (nx + 0.5f) * navCell;
                        int vx = (int)math.floor((cx - veilOrigin.x) / veilCell);
                        if (vx >= 0 && vx < field.Width)
                            sat = field.Saturation[vz * field.Width + vx];
                    }
                    bool crust = sat >= VeilField.CrustThreshold;

                    bool was = _stampedCrust[idx] != 0;
                    bool structural = (nav.Flags[idx] & Structural) != 0;

                    if (crust && !structural)
                    {
                        // Wall mode: impassable. Travel-cost mode: finite,
                        // saturation-scaled (deepening crust re-stamps via the
                        // want-compare below). Re-apply on navWiped even if we
                        // think we already own it — the restamp may have
                        // cleared our flag.
                        byte want = TheWaningBorder.Core.Config.VeilCrustConstants
                            .CrustPhysical
                            ? NavCostField.CostImpassable
                            : TravelCost(sat, nav.TerrainCost.IsCreated
                                ? nav.TerrainCost[idx] : (byte)0);
                        if (!was || navWiped
                            || nav.Cost[idx] != want
                            || (nav.Flags[idx] & NavCostField.FlagCrust) == 0)
                        {
                            nav.Cost[idx] = want;
                            nav.Flags[idx] = (byte)(nav.Flags[idx] | NavCostField.FlagCrust);
                            changed = true;
                        }
                        _stampedCrust[idx] = 1;
                    }
                    else if (was)
                    {
                        // Crust receded here (or a building claimed the cell).
                        // Only touch Cost if WE own it via FlagCrust — never
                        // re-open a structural cell.
                        if ((nav.Flags[idx] & NavCostField.FlagCrust) != 0)
                        {
                            nav.Cost[idx] = nav.TerrainCost.IsCreated
                                ? nav.TerrainCost[idx] : (byte)0;
                            nav.Flags[idx] = (byte)(nav.Flags[idx] & ~NavCostField.FlagCrust);
                            changed = true;
                        }
                        _stampedCrust[idx] = 0;
                    }
                }
            }

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
}
