// EmplacementComponents.cs
// The emplacement pair (docs/Design/Age_1_Alanthor.md § Ballista and
// Trebuchet emplacements): a PLATFORM the player places and an ENGINE
// standing on it. They are two entities on purpose — the platform holds
// the ground, takes the repairs and owns the footprint; the engine shoots
// and can be killed off it without the position being lost. The engine
// never moves: it carries no MoveSpeed and no DesiredDestination, so no
// move order can reach it.
//
// Set-level file: both emplacements share it, so it sits one level above
// their folders (CLAUDE.md § Set-level code sits one level above).

using Unity.Collections;
using Unity.Entities;

/// <summary>Marks the PLATFORM half of an emplacement.</summary>
public struct EmplacementTag : IComponentData { }

/// <summary>
/// The platform's crew state. <see cref="Engine"/> is the live engine
/// entity, or Entity.Null while there is none; <see cref="Rebuild"/> counts
/// down the crew's free replacement after the engine is destroyed.
/// </summary>
public struct EmplacementCrew : IComponentData
{
    public Entity Engine;
    /// <summary>Seconds left before the crew raises a new engine. 0 = idle.</summary>
    public float Rebuild;
    /// <summary>Seconds a replacement takes.</summary>
    public float RebuildTime;
    /// <summary>Which engine this platform mounts (an "Alanthor_Emplaced*" unit id).</summary>
    public FixedString32Bytes EngineId;
    /// <summary>How high above the platform's origin the engine stands.</summary>
    public float MountHeight;
}

/// <summary>Marks the ENGINE half — an immobile siege piece bolted to a
/// platform. Used by the order paths to refuse a move, and by the
/// platform's death to take its engine with it.</summary>
public struct EmplacedEngineTag : IComponentData { }

/// <summary>Back-pointer from the engine to the platform it stands on.</summary>
public struct EmplacedOn : IComponentData
{
    public Entity Emplacement;
}
