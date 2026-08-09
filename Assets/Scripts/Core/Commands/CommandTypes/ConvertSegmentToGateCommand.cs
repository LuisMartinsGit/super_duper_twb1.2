// ConvertSegmentToGateCommand.cs
// Segment-level convert command — spends the flat gate-conversion cost and
// attaches a WallSegmentUpgradeState timer to the SEGMENT entity. When the
// timer expires WallUpgradeSystem (Loop 2) tags the centre-5 instances of
// the segment with WallGateRegionTag + WallGateGroup + WallGateTag.
// Location: Assets/Scripts/Core/Commands/CommandTypes/ConvertSegmentToGateCommand.cs
//
// Phase 6 of task-wall-system-bfme2-rework-109. Follows the same
// XxxCommand / XxxCommandHelper pattern established by ConvertHutCommand
// (Phase 2) and CancelTrainCommand. The struct exists for symmetry; the
// helper is invoked directly by CommandRouter.IssueConvertSegmentToGate
// and by LockstepManager's dispatcher case.

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Carries the centre-instance hint chosen by the player click. Marker
    /// for symmetry with the XxxCommand / XxxCommandHelper pattern — the
    /// helper is invoked directly and the component is never added to
    /// entities. (task-109 phase 6)
    /// </summary>
    public struct ConvertSegmentToGateCommand : IComponentData
    {
        /// <summary>The wall instance the player clicked on. May be
        /// Entity.Null — the segment midpoint will be used instead.</summary>
        public Entity FocusInstance;
    }

    /// <summary>
    /// Helper class for executing segment → 5-wide-gate conversions.
    /// </summary>
    public static class ConvertSegmentToGateCommandHelper
    {
        /// <summary>
        /// Conversion cost — 80 supplies flat per conversion. Canonical
        /// value lives in docs/Design/Age_1_Alanthor.md (Phase 1 of
        /// task-109). Note: the historical BuildCosts.cs entry for
        /// Alanthor_WallGate was per-instance (40s + 15i); the
        /// task-109 Phase 1 design supersedes it with a single flat
        /// payment per conversion. We carry the canonical value here
        /// so the UI extractor + JSX fallback both read the same number.
        /// </summary>
        public static readonly Cost ConversionCost = new Cost
        {
            Supplies = 80,
            Iron = 0,
            Veilstone = 0,
            Veilsteel = 0,
            Glow = 0,
        };

        /// <summary>
        /// Duration of the segment → gate conversion timer in seconds.
        /// Matches the 8-second value canonicalised in
        /// docs/Design/Age_1_Alanthor.md (Phase 1 of task-109).
        /// </summary>
        public const float ConversionDuration = 8f;

        /// <summary>
        /// Attempt to start a segment → 5-wide gate conversion.
        ///   1. Validate the segment exists, has a <see cref="WallSegmentTag"/>,
        ///      carries a <see cref="WallInstanceRef"/> buffer, and is not
        ///      already converting (no pre-existing
        ///      <see cref="WallSegmentUpgradeState"/>).
        ///   2. Spend the flat conversion cost from the segment's faction
        ///      bank. If the faction can't afford it, the call is a no-op
        ///      (returns false).
        ///   3. Attach <see cref="WallSegmentFocus"/> referencing
        ///      <paramref name="focusInstance"/> so the centre-5 picker
        ///      lands on the player's clicked instance.
        ///   4. Attach <see cref="WallSegmentUpgradeState"/> with
        ///      <c>UpgradeType = 2</c> (Gate) and the canonical
        ///      8-second timer.
        /// Returns true if conversion was successfully started.
        /// </summary>
        public static bool Execute(EntityManager em, Entity segment, Entity focusInstance)
        {
            if (segment == Entity.Null || !em.Exists(segment)) return false;
            if (!em.HasComponent<WallSegmentTag>(segment)) return false;
            if (!em.HasBuffer<WallInstanceRef>(segment)) return false;
            // Idempotent: double-clicks during the timer must not double-charge.
            if (em.HasComponent<WallSegmentUpgradeState>(segment)) return false;

            // Resolve owning faction so we can charge the bank. The segment
            // inherits its hub's faction at CreateSegment time.
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(segment))
                faction = em.GetComponentData<FactionTag>(segment).Value;

            if (!FactionEconomy.Spend(em, faction, ConversionCost))
                return false;

            // Stash the focus instance on the segment so PickGateRegionInstances
            // (Phase 5) lands on the player's clicked instance. Idempotent —
            // overwrites any stale value left by a previous hover-preview.
            if (em.HasComponent<WallSegmentFocus>(segment))
            {
                em.SetComponentData(segment, new WallSegmentFocus { Instance = focusInstance });
            }
            else
            {
                em.AddComponentData(segment, new WallSegmentFocus { Instance = focusInstance });
            }

            em.AddComponentData(segment, new WallSegmentUpgradeState
            {
                UpgradeType = 2, // Gate
                FocusInstance = focusInstance,
                Total = ConversionDuration,
                Remaining = ConversionDuration,
            });

            return true;
        }
    }
}
