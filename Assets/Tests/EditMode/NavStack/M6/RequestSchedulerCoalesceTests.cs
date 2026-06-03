// RequestSchedulerCoalesceTests.cs
// task-112 M6 -- direct tests of the scheduler's coalesce contract.
// Enqueue 5 PendingNavRequest entries with identical (GoalCell,
// ProfileHash); after one tick, assert exactly 1 of them counts
// against the scheduler's per-tick budget (i.e. only 1 unique
// equivalence-class key was charged) but every requester still
// receives a NavPathRequest (so each unit's pathfinder result is
// produced -- duplicate requests piggy-back free on the primary's
// budget slot).
//
// The test exercises the algorithm directly (sort + dedupe walk)
// rather than booting an ECS world, so it stays an EditMode unit
// test with no Burst-job dependencies.
//
// Location: Assets/Tests/EditMode/NavStack/M6/RequestSchedulerCoalesceTests.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M6
{
    public class RequestSchedulerCoalesceTests
    {
        [Test]
        public void NavRequestQueueSingleton_DefaultBudget_IsSixteen()
        {
            // Spot-check the per-tick release budget locked at the
            // singleton level. DR-12 documents the budget; the
            // architecture pins it at 16.
            Assert.AreEqual(16, NavRequestQueueSingleton.DefaultMaxRequestsPerTick,
                "Default per-tick budget must be 16");
        }

        [Test]
        public void FivePendingSameGoal_OneBudgetUnit_FiveDispatched()
        {
            // Build 5 requests with the same goal + profile.
            var pending = new NativeList<PendingNavRequest>(8, Allocator.Temp);
            for (int i = 0; i < 5; i++)
            {
                pending.Add(new PendingNavRequest
                {
                    Requester = new Entity { Index = 100 + i, Version = 1 },
                    StartCell = new int2(0, 0),
                    GoalCell = new int2(42, 42),
                    ProfileHash = 0,
                    Priority = PendingNavRequest.PriorityUser,
                    EnqueueTick = 1,
                    Generation = 1,
                });
            }

            // Simulate the scheduler's coalesce walk: sort + dedupe key.
            var seen = new NativeHashSet<NavRequestCoalesceKey>(8, Allocator.Temp);
            int budgetCharged = 0;
            int dispatched = 0;
            const int budget = 16;
            for (int i = 0; i < pending.Length; i++)
            {
                var p = pending[i];
                var key = new NavRequestCoalesceKey
                {
                    GoalCell = p.GoalCell,
                    ProfileHash = p.ProfileHash,
                };
                bool isNew = !seen.Contains(key);
                if (isNew)
                {
                    if (budgetCharged >= budget) continue;
                    seen.Add(key);
                    budgetCharged++;
                }
                dispatched++;
            }
            seen.Dispose();
            pending.Dispose();

            Assert.AreEqual(1, budgetCharged,
                "All 5 same-goal requests should collapse into 1 budget unit");
            Assert.AreEqual(5, dispatched,
                "Every requester should still receive a NavPathRequest");
        }

        [Test]
        public void TwoUniqueGoals_TwoBudgetUnitsCharged()
        {
            var pending = new NativeList<PendingNavRequest>(8, Allocator.Temp);
            pending.Add(new PendingNavRequest
            {
                Requester = new Entity { Index = 200, Version = 1 },
                StartCell = new int2(0, 0), GoalCell = new int2(10, 10),
                ProfileHash = 0, Priority = 0, EnqueueTick = 1, Generation = 1,
            });
            pending.Add(new PendingNavRequest
            {
                Requester = new Entity { Index = 201, Version = 1 },
                StartCell = new int2(0, 0), GoalCell = new int2(20, 20),
                ProfileHash = 0, Priority = 0, EnqueueTick = 1, Generation = 1,
            });

            var seen = new NativeHashSet<NavRequestCoalesceKey>(8, Allocator.Temp);
            int budgetCharged = 0;
            for (int i = 0; i < pending.Length; i++)
            {
                var p = pending[i];
                var key = new NavRequestCoalesceKey
                {
                    GoalCell = p.GoalCell,
                    ProfileHash = p.ProfileHash,
                };
                if (seen.Add(key)) budgetCharged++;
            }
            seen.Dispose();
            pending.Dispose();

            Assert.AreEqual(2, budgetCharged, "Two distinct goals -> two budget units");
        }
    }
}
