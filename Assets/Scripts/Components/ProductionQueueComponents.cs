// ProductionQueueComponents.cs
// ONE queue per building, for everything the building produces: the units it
// trains, the technologies it researches, and its own level-ups. Items run in
// the order they were queued, one at a time, off one clock
// (ProductionQueueSystem).
//
// This took two passes. The first merged research and level-ups (2026-09-07
// morning): research sat in a ResearchQueueItem buffer ticked by
// ResearchSystem, a level-up was a BuildingUpgrading component stamped straight
// onto the building with no queue at all — two timers, two price rules, two
// displays. The second pass, the same afternoon, brought training in. It had
// kept its own TrainQueueItem buffer and its own TrainingSystem, on the theory
// that a building should train and research at once; the player's verdict was
// that research "runs in parallel with unit training and does not display in
// the queue", which is the same complaint as the first pass with the last
// buffer still standing. So: one buffer, three kinds.
//
// CommandRouter.MaxProductionQueue always described this end state — "the
// player perceives them as one production queue" — and now the sim agrees.

using Unity.Collections;
using Unity.Entities;

/// <summary>What a queued production item does when it reaches the head.</summary>
public enum ProductionKind : byte
{
    /// <summary>A technology. <see cref="ProductionQueueItem.Id"/> is its tech id.</summary>
    Research = 0,

    /// <summary>This building's next level. Id is empty;
    /// <see cref="ProductionQueueItem.Level"/> is the target.</summary>
    BuildingUpgrade = 1,

    /// <summary>A unit. Id is its unit id; the unit spawns at the rally point
    /// when the item completes.</summary>
    Train = 2,
}

/// <summary>
/// One queued item. Cost is deducted at QUEUE time, whatever the kind, so
/// cancelling refunds and a queued item can never be unaffordable by the time
/// it starts.
/// </summary>
public struct ProductionQueueItem : IBufferElementData
{
    public ProductionKind Kind;

    /// <summary>Research: the tech id. Train: the unit id. BuildingUpgrade: empty.</summary>
    public FixedString64Bytes Id;

    /// <summary>
    /// BuildingUpgrade: the level this item was PRICED for. Recorded rather
    /// than recomputed at start so the refund matches the charge exactly, and
    /// so a level applied from somewhere else while the item waits
    /// (BuildingCultureAutoLevelSystem, StartAgePromoter) is DETECTED at start
    /// instead of silently applying a second time. 0 for the other kinds.
    /// </summary>
    public byte Level;

    /// <summary>
    /// Train: the Call to Arms cost multiplier this unit was CHARGED at (0 =
    /// no boon was up, meaning full price).
    ///
    /// Recorded rather than recomputed because the boon is a 15-30 s window:
    /// queue a unit at half price, let the boon lapse, then cancel, and a
    /// refund computed live would hand back the full cost and mint the
    /// difference. The refund reads this field instead. 0 for the other kinds.
    /// </summary>
    public float PaidCostMultiplier;
}

/// <summary>
/// The head item's timer. Busy = 1 while something is running.
///
/// <see cref="Total"/> is captured at START. It used to be absent, and the UI
/// recomputed it from the catalog every frame — which reported the wrong
/// progress for any item whose duration had been changed by a sect multiplier
/// after it started, and could not describe an upgrade at all.
///
/// A finished unit that the population cap will not admit holds here with
/// Busy = 1 and Remaining = 0 until room frees, then spawns — the item stays
/// at the head, paid for, and nothing behind it starts.
/// </summary>
public struct ProductionState : IComponentData
{
    /// <summary>0 = idle, 1 = the head item is running.</summary>
    public byte Busy;

    /// <summary>Seconds until the head item completes.</summary>
    public float Remaining;

    /// <summary>Seconds the head item takes in total, captured at start.</summary>
    public float Total;
}
