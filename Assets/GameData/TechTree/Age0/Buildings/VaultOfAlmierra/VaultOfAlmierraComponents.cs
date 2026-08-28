// VaultOfAlmierraComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Resource vault building.</summary>
public struct VaultTag : IComponentData { }

/// <summary>
/// Resource storage with compound interest for Vault of Almiérra.
/// Only one resource type at a time. Locked after deposit/withdraw.
/// </summary>
public struct VaultStorage : IComponentData
{
    /// <summary>0=None, 1=Supplies, 2=Iron, 3=Veilstone, 4=Veilsteel, 5=Glow</summary>
    public int ResourceType;
    public float StoredAmount;
    public float InterestRate;   // Per minute (0.03 = 3%)
    public float LockTimer;      // Remaining lock seconds (0 = unlocked)
    public float LockDuration;   // Seconds to lock after deposit/withdraw (180 = 3 min)
}
