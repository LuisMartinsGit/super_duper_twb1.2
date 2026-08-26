// LayeredMoveComponents.cs
// ECS components lifted out of LayeredMoveSystem.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Terrain;

/// <summary>
/// Order to move a unit to <see cref="FinalDest"/> on <see cref="TargetLayer"/>
/// (0 = Ground, 1 = Wall deck), changing layers via a wall access point if
/// needed. Consumed by <see cref="TheWaningBorder.Systems.Buildings.LayeredMoveSystem"/>.
/// </summary>
public struct LayeredMoveOrder : IComponentData
{
    public float3 FinalDest;
    public byte TargetLayer;
    public byte Phase;        // 0 = en route to access; 1 = transitioning
    public float3 TransStart;
    public float3 TransEnd;
    public float Progress;
}
