// VeilstoneMineComponents.cs
// Canon: docs/Design/Regions.md §4 (extraction buildings).

using Unity.Entities;

/// <summary>
/// A VEILSTONE MINE — the extraction building for a veilstone outcropping,
/// the way a Gatherer's Hut is for a supply site and a Mine is for iron.
///
/// It exists because the generic Mine did not distinguish node kinds: it
/// counted every node within 12 m, so one building extracted iron, veilstone
/// and veilsteel alike and the player never chose WHAT to invest in. Each
/// resource now has its own building, its own node to stand on, and its own
/// upgrade ladder — which is what makes a territory's contents matter.
///
/// It carries no state of its own. Territory income reads the building's
/// LEVEL off the node it stands on (TerritoryIncomeSystem.MineLevelsOn), so
/// there is nothing to keep here that the upgrade component does not already
/// hold.
/// </summary>
public struct VeilstoneMineTag : IComponentData { }
