// LookoutComponents.cs
// The Lookout's marker (docs/Design/Age_0.md § Lookout). Global namespace,
// like every other building tag.

using Unity.Entities;

/// <summary>
/// The Lookout: a cheap timber tower that only SEES (Age_0.md § Lookout) —
/// no attack, no garrison, no levels, and nothing converts it at age-up.
/// Distinct from <c>WatchTowerTag</c> (the Alanthor Watch Tower, which
/// shoots, garrisons and levels).
/// </summary>
public struct LookoutTag : IComponentData { }
