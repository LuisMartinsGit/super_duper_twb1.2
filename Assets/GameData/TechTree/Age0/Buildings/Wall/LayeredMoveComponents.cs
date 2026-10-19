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
/// <summary>
/// The rest of a unit's walk ALONG a wall-walk: the corner points of a path
/// over the rampart layer, nearest first (docs/Design/Age_1_Alanthor.md
/// § The stone wall). The integrator walks a deck unit in a straight line,
/// which jams on the first bend of a curved wall; LayeredMoveSystem plans the
/// path over the deck's cells and hands the unit one corner at a time.
/// </summary>
public struct DeckWaypoint : IBufferElementData
{
    public float3 Position;
}

public struct LayeredMoveOrder : IComponentData
{
    public float3 FinalDest;
    public byte TargetLayer;
    // 0 = choosing an access; 1 = transitioning (LERP TransStart -> TransEnd);
    // 2 = on the deck, walking to the access at TransStart to come down;
    // 3 = on the ground, walking to the access at TransEnd to climb — the
    //     one whose wall-walk joins FinalDest (2026-10-20).
    public byte Phase;
    public float3 TransStart;
    public float3 TransEnd;
    public float Progress;
}
