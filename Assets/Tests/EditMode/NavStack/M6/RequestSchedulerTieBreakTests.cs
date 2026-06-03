// RequestSchedulerTieBreakTests.cs
// task-112 M6 -- direct test of the scheduler's sort key tie-break.
// Two requests with identical Priority and identical EnqueueTick MUST
// release in Requester.Index ASCENDING order (DR-12 contract).
//
// Asserts both an actual sort-and-walk pass and the
// NavRequestSchedulerSystem.ComparePending strict-total-order
// helper.
//
// Location: Assets/Tests/EditMode/NavStack/M6/RequestSchedulerTieBreakTests.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Tests.EditMode.NavStack.M6
{
    public class RequestSchedulerTieBreakTests
    {
        [Test]
        public void EqualPriorityTick_RequesterIndexAscending_DispatchOrder()
        {
            // Insert HIGHER index first to prove the sort is what
            // imposes the order, not insertion.
            var pending = new NativeList<PendingNavRequest>(4, Allocator.Temp);
            pending.Add(new PendingNavRequest
            {
                Requester = new Entity { Index = 9000, Version = 1 },
                StartCell = new int2(0, 0), GoalCell = new int2(50, 50),
                ProfileHash = 0, Priority = 1, EnqueueTick = 7, Generation = 1,
            });
            pending.Add(new PendingNavRequest
            {
                Requester = new Entity { Index = 5000, Version = 1 },
                StartCell = new int2(0, 0), GoalCell = new int2(60, 60),
                ProfileHash = 0, Priority = 1, EnqueueTick = 7, Generation = 1,
            });

            // Insertion-sort using the system's comparator.
            for (int i = 1; i < pending.Length; i++)
            {
                var k = pending[i];
                int j = i - 1;
                while (j >= 0
                    && NavRequestSchedulerSystem.ComparePending(pending[j], k) > 0)
                {
                    pending[j + 1] = pending[j];
                    j--;
                }
                pending[j + 1] = k;
            }

            Assert.AreEqual(5000, pending[0].Requester.Index,
                "Lower Requester.Index must come first on tie");
            Assert.AreEqual(9000, pending[1].Requester.Index,
                "Higher Requester.Index follows");

            pending.Dispose();
        }

        [Test]
        public void PriorityBeatsEnqueueTick()
        {
            var pending = new NativeList<PendingNavRequest>(4, Allocator.Temp);
            // Older tick but lower priority -> later in dispatch.
            pending.Add(new PendingNavRequest
            {
                Requester = new Entity { Index = 1, Version = 1 },
                StartCell = new int2(0, 0), GoalCell = new int2(50, 50),
                ProfileHash = 0,
                Priority = PendingNavRequest.PriorityNormal, // 1
                EnqueueTick = 1, Generation = 1,
            });
            // Newer tick but higher priority -> first.
            pending.Add(new PendingNavRequest
            {
                Requester = new Entity { Index = 2, Version = 1 },
                StartCell = new int2(0, 0), GoalCell = new int2(60, 60),
                ProfileHash = 0,
                Priority = PendingNavRequest.PriorityUser, // 0 (highest)
                EnqueueTick = 99, Generation = 1,
            });

            for (int i = 1; i < pending.Length; i++)
            {
                var k = pending[i];
                int j = i - 1;
                while (j >= 0
                    && NavRequestSchedulerSystem.ComparePending(pending[j], k) > 0)
                {
                    pending[j + 1] = pending[j];
                    j--;
                }
                pending[j + 1] = k;
            }

            Assert.AreEqual(PendingNavRequest.PriorityUser, pending[0].Priority,
                "Lower priority value (=higher priority) must come first");
            pending.Dispose();
        }
    }
}
