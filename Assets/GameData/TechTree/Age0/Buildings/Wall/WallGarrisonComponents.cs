// WallGarrisonComponents.cs
// ECS components lifted out of WallGarrisonSystem.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using TheWaningBorder.World.Terrain;

/// <summary>Marks a unit currently garrisoning a wall deck; holds its assigned
/// outer-edge slot so it holds position there.</summary>
public struct WallGarrisonState : IComponentData
{
    public float3 Slot;
}

/// <summary>
/// A unit WALKING to a garrisonable building to go inside it (2026-10-20,
/// docs/Design/Age_1_Alanthor.md § Watch Tower). The garrison order is
/// issued at once, but the unit enters only on arrival — within the
/// building's footprint plus <c>WallGarrisonConfig.entryRange</c>.
/// WallGarrisonApproachSystem drives it; any later order (move, attack,
/// stop, …) removes it (CommandCleanup.ClearWorkOrders /
/// CommandHelper.ClearAllCommands), and it is dropped when the building
/// dies, changes hands or fills up.
///
/// ENABLEABLE (TransientState): cancelling only disables it, so an order's
/// cleanup stays non-structural — order helpers can run inside other
/// systems' loops.
/// </summary>
public struct GarrisonApproach : IComponentData, IEnableableComponent
{
    public Entity Building;
}
