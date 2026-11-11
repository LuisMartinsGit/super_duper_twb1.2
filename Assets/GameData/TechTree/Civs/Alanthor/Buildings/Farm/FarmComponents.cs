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
