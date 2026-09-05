// FeraldisRaiderComponents.cs
// ECS components lifted out of FeraldisRaider.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

/// <summary>Marker for Feraldis Raider light cavalry.</summary>
public struct FeraldisRaiderTag : IComponentData { }
