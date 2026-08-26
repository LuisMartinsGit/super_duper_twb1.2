// Walkable-rampart garrison (W4). When units are ordered onto a wall and end up
// standing on a deck (elevated well above the terrain), spread them into ranks
// along the OUTER parapet edge of the nearest wall module so they read as manning
// the wall. Ordered back to the ground (no longer elevated), the garrison state
// clears and they move normally. See docs/Design/Age_1_Alanthor.md § Walkable
// Ramparts, Stairs & Garrison.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Buildings
{
    /// <summary>
    /// Stations on-wall units in ranks along the outer parapet. Runs after
    /// movement so it sees up-to-date positions. Deterministic: wall modules and
    /// units are visited in a stable entity-id order, so the same lockstep inputs
    /// produce the same slots on every peer.
    ///
    /// v1 scope / known tuning points (editor-verify):
    /// - O(units × wall-modules) per tick — gate to elevated units only if it
    ///   shows up in profiling.
    /// - Re-derives lanes each tick; a unit leaving mid-rank can reshuffle others.
    /// - May contend with battalion-follow formation while a unit is en route;
    ///   garrison takes over only once the unit is actually on a deck.
    /// </summary>
    // task-112 M4: UpdateAfter migrated from MovementSystem (deleted)
    // to UnitIntegratorSystem.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Navigation.UnitIntegratorSystem))]
    public partial class WallGarrisonSystem : SystemBase
    {
        private const float ElevatedThreshold = 2.0f; // (pos.y - terrainY) above this ⇒ on a wall deck
        private const float DeckY        = 4f;
        private const float DeckWalkHalf = 4f;
        private const float EdgeInset    = 0.7f;       // stand this far inside the outer parapet
        private const float FileSpacing  = 1.2f;
        private const float RankSpacing  = 1.2f;
        private const float ModuleLen    = 4f;
        private const float ArriveDist   = 1.2f;

        protected override void OnCreate()
        {
            // Only run when at least one wall exists in the world.
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<WallTag>()));
        }

        protected override void OnUpdate()
        {
            // Disabled: the wall top is now a freely-navigable layer driven by
            // LayeredMoveSystem (units are positioned by explicit move orders,
            // not auto-stationed). The old per-tick "spread elevated units
            // along the parapet" behaviour would override that free movement,
            // so it's removed. WallGarrisonState is retained as a (currently
            // unused) marker; reinstate this body if formation-on-wall returns.
        }
    }
}
