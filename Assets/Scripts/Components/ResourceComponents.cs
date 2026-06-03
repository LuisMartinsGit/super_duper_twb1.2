// ResourceComponents.cs
// Components for resource nodes (iron mines, crystal deposits, etc.)
// All ECS components live in global namespace per project convention.

using Unity.Entities;

/// <summary>Marker tag for iron mine/deposit entities.</summary>
public struct IronMineTag : IComponentData { }

public struct IronDepositState : IComponentData
{
    public int RemainingIron;
    /// <summary>
    /// Bootstrap-time deposit capacity. Set once by IronDepositBootstrap and
    /// never mutated. UI uses this as the denominator for "Remaining: N / Max"
    /// readouts and the world-space depletion bar (task-108). Pre-task-108
    /// saves load with InitialIron == 0; callers must fall back to
    /// RemainingIron in that case.
    /// </summary>
    public int InitialIron;
    public byte Depleted;  // 1 = exhausted
}