// ResourceComponents.cs
// Components for resource nodes (iron mines, crystal deposits, etc.)

using Unity.Entities;

namespace TheWaningBorder.AI  // Match the namespace used in AIEconomyManager
{
    /// <summary>Marker tag for iron mine/deposit entities.</summary>
    public struct IronMineTag : IComponentData { }
}

// Also define IronDepositState if not already somewhere:
public struct IronDepositState : IComponentData
{
    public int RemainingIron;
    public byte Depleted;  // 1 = exhausted
}