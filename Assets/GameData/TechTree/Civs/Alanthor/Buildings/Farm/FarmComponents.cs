// FarmComponents.cs
// The Alanthor Farm's ECS vocabulary (docs/Design/Age_1_Alanthor.md § The
// Farm; Unification decision 55 (c)). The Workers inside it are the wall
// garrison's machinery (WallGarrisonSlot / WallGarrisonedIn, see
// WallGarrison.cs): the Farm only adds who may go in and what they earn.

using Unity.Entities;

/// <summary>Marks an Alanthor Farm. A Farm's garrison slots take Workers
/// only (WallGarrison.MayEnter), and FarmSystem pays supplies per Worker
/// inside it.</summary>
public struct FarmTag : IComponentData { }

/// <summary>
/// A Farm's pay carry: supplies owed in SIXTIETHS. FarmSystem adds
/// (Workers inside x suppliesPerWorkerPerMinute) every second and pays the
/// whole units, so a rate that is not a multiple of 60 is never rounded
/// away. On the entity, so it rides a save snapshot and dies with the Farm.
/// </summary>
public struct FarmPay : IComponentData
{
    public int SuppliesSixtieths;
}

/// <summary>
/// A Farm's crop field (decision 57, FarmRing): the per-mille of its crop
/// ring's sample lattice that is free of obstacles. FarmSystem recomputes it
/// on a fixed cadence (FarmRing.FreePermille) and pays with it; the UI reads
/// it. On the entity, so it rides a save snapshot and every peer holds the
/// same value. <see cref="Computed"/> is 0 until the first computation.
/// </summary>
public struct FarmField : IComponentData
{
    public int FreePermille;
    public byte Computed;
}

// ── The level-3 low stone wall (docs/Design/Age_1_Alanthor.md § The level-3
// wall; decision 57 (f)). FarmStoneWallSystem owns all three. ──

/// <summary>
/// One piece of a Farm's level-3 low stone wall: a straight run A-B (world
/// XZ). It blocks movement (CostFieldStampSystem stamps it), has Health and
/// armour, is never auto-acquired (NodeNoAutoAcquire) and carries no
/// BuildingTag or WallTag — not a building, not covered by the Wall Rule, not
/// an obstacle for any Farm's yield. DeathSystem removes a dead one at once,
/// with no death record; its Farm's slot then waits to rebuild it.
/// </summary>
public struct FarmWallPiece : IComponentData
{
    public Entity Farm;
    public Unity.Mathematics.float2 A, B;
    /// <summary>Half the strip it blocks across the line, metres.</summary>
    public float HalfThickness;
}

/// <summary>
/// A Farm's wall bookkeeping, added the first time its faction holds level 3.
/// <see cref="Signature"/> hashes everything that shapes the layout (re-laid
/// only when it changes); <see cref="QuietTicks"/> counts wall ticks since the
/// Farm or one of its pieces last lost HP (the out-of-combat clock);
/// <see cref="LastFarmHp"/> is what the Farm had at the last tick.
/// </summary>
public struct FarmWallState : IComponentData
{
    public uint Signature;
    public int QuietTicks;
    public int LastFarmHp;
    public byte Laid;
}

/// <summary>
/// One place a wall piece stands (or will stand again) on a Farm, from the
/// layout. <see cref="Piece"/> is the live piece or Entity.Null while it is
/// down; <see cref="DownTicks"/> counts wall ticks since it fell (a new slot
/// starts ready); <see cref="LastHp"/> is the piece's HP at the last tick.
/// </summary>
[InternalBufferCapacity(0)]
public struct FarmWallSlot : IBufferElementData
{
    public Unity.Mathematics.float2 A, B;
    public Entity Piece;
    public int DownTicks;
    public int LastHp;
}
