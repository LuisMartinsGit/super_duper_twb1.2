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
