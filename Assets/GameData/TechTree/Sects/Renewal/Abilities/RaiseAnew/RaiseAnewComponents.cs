// RaiseAnewComponents.cs
// Tags for the three PERMANENT fortifications the Sect of Renewal's active
// power "Raise Anew" conjures, one per power level. Three distinct buildings
// with their own ids / SOs / visuals; NOT levels of Alanthor_Tower.

using Unity.Entities;

/// <summary>Raise Anew I: the Renewal Tower, a modest watch post.</summary>
public struct RenewalTowerTag : IComponentData { }

/// <summary>Raise Anew II: the Renewal Fortification, a walled strongpoint.</summary>
public struct RenewalFortificationTag : IComponentData { }

/// <summary>Raise Anew III: the Renewal Fortress, a keep that anchors a position.</summary>
public struct RenewalFortressTag : IComponentData { }
