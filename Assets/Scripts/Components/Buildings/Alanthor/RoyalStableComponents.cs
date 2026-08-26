// RoyalStableComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Alanthor heavy-cavalry trainer. Trains Cataphract (and any future
/// cavalry units listed in the TechTree's "trains" array). Marked
/// individually so the trainer-resolution map in
/// CommandRouter.ResolveBuildingIdForTrainer can route TrainQueueItem
/// orders back to the canonical building id "Alanthor_RoyalStable".
/// </summary>
public struct RoyalStableTag : IComponentData { }
