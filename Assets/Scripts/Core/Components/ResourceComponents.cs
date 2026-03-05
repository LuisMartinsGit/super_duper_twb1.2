// ResourceComponents.cs
// Components for resource nodes (iron mines, crystal deposits, etc.)
// All ECS components live in global namespace per project convention.

using Unity.Entities;

/// <summary>Marker tag for iron mine/deposit entities.</summary>
public struct IronMineTag : IComponentData { }

public struct IronDepositState : IComponentData
{
    public int RemainingIron;
    public byte Depleted;  // 1 = exhausted
}