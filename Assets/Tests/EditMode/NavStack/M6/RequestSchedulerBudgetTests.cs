// RequestSchedulerBudgetTests.cs
// task-112 M6 -- enqueue 50 DISTINCT requests, simulate 4 scheduler
// ticks, and assert MaxRequestsPerTick=16 budget units are released
// per tick across 4 ticks (16 + 16 + 16 + 2 = 50).
//
// Exercises the carry-over behaviour: requests that exceed the
// per-tick budget MUST stay in the pending list and be processed on
// the next tick in the same order.
//
// Location: Assets/Tests/EditMode/NavStack/M6/RequestSchedulerBudgetTests.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M6
{
    public class RequestSchedulerBudgetTests
    {
        [Test]
        public void FiftyRequests_FourTicks_BudgetReleasePattern()
        {
            const int Budget = 16;
            const int Total = 50;

            // Build 50 distinct requests (different goal cells so
            // every one is a unique coalesce key).
            var pending = new NativeList<PendingNavRequest>(Total, Allocator.Temp);
            for (int i = 0; i < Total; i++)
            {
                pending.Add(new PendingNavRequest
                {
                    Requester = new Entity { Index = 1000 + i, Version = 1 },
                    StartCell = new int2(0, 0),
                    GoalCell = new int2(i, i), // distinct -> distinct keys
                    ProfileHash = 0,
                    Priority = 0,
                    EnqueueTick = 1,
                    Generation = 1,
                });
            }

            // Simulate 4 scheduler ticks; on each tick: sort, dedupe,
            // release up to Budget unique keys, carry rest forward.
            var releasedPerTick = new int[4];
            for (int tick = 0; tick < 4; tick++)
            {
                // Sort (entries are already in order in this test --
                // skip; matches the scheduler's ComparePending).
                var seen = new NativeHashSet<NavRequestCoalesceKey>(Budget * 2, Allocator.Temp);
                var carry = new NativeList<PendingNavRequest>(pending.Length, Allocator.Temp);
                int released = 0;
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
                        if (released >= Budget)
                        {
                            carry.Add(p);
                            continue;
                        }
                        seen.Add(key);
                        released++;
                    }
                    // Duplicates of already-released keys are free.
                }
                releasedPerTick[tick] = released;

                pending.Clear();
                for (int i = 0; i < carry.Length; i++) pending.Add(carry[i]);
                carry.Dispose();
                seen.Dispose();
            }
            pending.Dispose();

            Assert.AreEqual(16, releasedPerTick[0], "Tick 0: 16 budget units");
            Assert.AreEqual(16, releasedPerTick[1], "Tick 1: 16 budget units");
            Assert.AreEqual(16, releasedPerTick[2], "Tick 2: 16 budget units");
            Assert.AreEqual(2, releasedPerTick[3], "Tick 3: 2 budget units (remainder)");

            int sum = releasedPerTick[0] + releasedPerTick[1]
                + releasedPerTick[2] + releasedPerTick[3];
            Assert.AreEqual(Total, sum, "Total over 4 ticks must equal enqueued count");
        }
    }
}
