// KeepWingComponents.cs
// ECS components lifted out of KeepWingComponents.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;

/// <summary>
/// The Keep's built wings (up to three, each type at most once). Present on
/// every Fiendstone Keep from creation; slots fill as wings complete.
/// </summary>
public struct KeepWings : IComponentData
{
    public byte Wing0;
    public byte Wing1;
    public byte Wing2;

    public int Count =>
        (Wing0 != 0 ? 1 : 0) + (Wing1 != 0 ? 1 : 0) + (Wing2 != 0 ? 1 : 0);

    public bool Has(KeepWingType t)
    {
        byte b = (byte)t;
        return b != 0 && (Wing0 == b || Wing1 == b || Wing2 == b);
    }

    /// <summary>Fill the first empty slot. Returns false when all three are used.</summary>
    public bool Add(KeepWingType t)
    {
        byte b = (byte)t;
        if (Wing0 == 0) { Wing0 = b; return true; }
        if (Wing1 == 0) { Wing1 = b; return true; }
        if (Wing2 == 0) { Wing2 = b; return true; }
        return false;
    }
}

/// <summary>A wing under construction on the Keep (one at a time).</summary>
public struct KeepWingConstruction : IComponentData
{
    public byte Wing;       // KeepWingType being built
    public float Remaining; // seconds left
    public float Total;
}
