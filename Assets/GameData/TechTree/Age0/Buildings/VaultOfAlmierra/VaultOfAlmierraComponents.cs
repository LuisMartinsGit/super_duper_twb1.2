// VaultOfAlmierraComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Resource vault building.</summary>
public struct VaultTag : IComponentData { }

/// <summary>
/// Resource storage with SIMPLE, capped interest for Vault of Almiérra
/// (decision 41, 2026-10-05). Only one resource type at a time. Locked
/// after deposit/withdraw.
/// </summary>
public struct VaultStorage : IComponentData
{
    /// <summary>0=None, 1=Supplies, 2=Iron, 3=Veilstone, 4=Veilsteel, 5=Glow</summary>
    public int ResourceType;
    /// <summary>Principal plus the interest it has earned; what a withdrawal pays out.</summary>
    public float StoredAmount;
    /// <summary>What interest is computed on: the stored amount as of the last
    /// deposit (CommandRouter.VaultTransferDirect sets it; a withdrawal zeroes
    /// it). Interest never joins it, so the yield is simple, and only the part
    /// up to the Vault SO's interestPrincipalCap earns.</summary>
    public float Principal;
    public float InterestRate;   // Base per minute from the SO (0.05 = 5%)
    public float LockTimer;      // Remaining lock seconds (0 = unlocked)
    public float LockDuration;   // Seconds to lock after deposit/withdraw (180 = 3 min)
    public float EarningSeconds; // Seconds of interest paid since the last deposit (capped by the SO's interestMaxSeconds)
}
