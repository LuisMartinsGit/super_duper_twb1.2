// ResourceComponents.cs
// Components for resource nodes (iron mines, veilstone deposits, etc.)
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

/// <summary>
/// Marker tag for the Veilsteel "Sharp Crystals" map resource node.
/// A veilsteel deposit reuses <see cref="IronDepositState"/> for its
/// remaining/initial amounts — it behaves exactly like an iron deposit
/// (fixed amount, mined until gone), only the resource credited to the
/// faction bank differs. Spawned by VeilsteelDepositBootstrap from
/// VeilsteelDepositMarker scene markers as a SINGLE node holding 1500 units.
/// </summary>
public struct VeilsteelDepositTag : IComponentData { }