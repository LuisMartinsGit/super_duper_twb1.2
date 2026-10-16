// TargetingSystem.Guard.cs
// Return-to-guard behaviour: leashing idle units back to their guard point.
// Partial of TargetingSystem.cs -- split 2026-08-12 for readability.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct TargetingSystem : ISystem
    {
        // The return-to-guard pass now runs as GuardPlanJob
        // (TargetingSystem.Jobs.cs) and is applied by AutoAcquireAndReturnToGuard
        // (TargetingSystem.Acquire.cs). The rules it implements, in order:
        //
        //  - a unit with a Target only drops a stale GuardSuppressed (engaging is
        //    a fresh intent, so the leash re-arms once the fight ends);
        //  - no guard point, or acquired earlier this tick: nothing;
        //  - suppressed against THIS guard point by stuck recovery: nothing —
        //    re-issuing the destination it just gave up on was the endless
        //    circling at a target location (see GuardSuppressed);
        //  - scouts never snap back; a healing Litharch holds;
        //  - Hold and fixed mounts stay where they are;
        //  - attack-move / patrol RESUME their march (the guard point is the
        //    destination / waypoint) — only when no live destination exists,
        //    because re-stamping it every frame disabled every nav recovery;
        //  - an idle unit past GuardReturnThreshold re-engages on the way home
        //    BY STANCE (an AUTO target under the leash, docs/Design/Stances.md
        //    §3) on its stagger tick, else walks home if it is not already
        //    executing a movement intent.
        //
        // The query keeps its exclusions: AttackCommand, UserMoveOrder,
        // HealCommand (healers actively healing), PassiveWorkerTag, BuildCommand
        // (a committed worker is busy), WorkerTag and RitualState (a channelling
        // ritualist is busy — walking it home broke every channel, 2026-08-07).
    }
}
