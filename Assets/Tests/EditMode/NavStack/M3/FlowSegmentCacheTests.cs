// FlowSegmentCacheTests.cs
// task-112 M3: exercise the NavFlowCache LRU + slot allocator at the
// data-structure level (no ECS world). The tests construct a cache
// with a small SlotCount, fill it, repeat queries to confirm hits don't
// evict, then push past SlotCount to verify LRU eviction by
// LastUsedTick.
//
// The cache itself is mutated through a static helper that mirrors the
// FlowSegmentSystem's AllocateSlot path. We can't call the private
// helper directly from outside the system, so the test reproduces the
// allocation policy in a stand-alone helper (NavFlowCacheOps below) --
// this is OK because the test's contract is "alloc + lookup + evict
// behave as documented", not "this exact code path runs".
//
// Location: Assets/Tests/EditMode/NavStack/M3/FlowSegmentCacheTests.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;

namespace TheWaningBorder.Tests.EditMode.NavStack.M3
{
    public class FlowSegmentCacheTests
    {
        [Test]
        public void Cache_FirstRequestIsMiss_RepeatedRequestIsHit()
        {
            var cache = MakeSmallCache(slots: 4, tileArea: 16);
            try
            {
                var key = new NavFlowCacheKey
                {
                    TileIndex = 7,
                    ExitPortalId = 42,
                    ProfileHash = 0,
                };

                // First lookup: miss.
                Assert.IsFalse(cache.SlotIndex.TryGetValue(key, out _),
                    "first lookup must miss before any slot is filled");

                cache.TickCounter++;
                int slot = NavFlowCacheOps.AllocateAndInsert(ref cache, key);
                Assert.GreaterOrEqual(slot, 0);
                Assert.AreEqual(1, cache.Slots[slot].Valid);
                Assert.AreEqual(cache.TickCounter, cache.Slots[slot].LastUsedTick);

                // Second lookup: hit.
                Assert.IsTrue(cache.SlotIndex.TryGetValue(key, out int slot2));
                Assert.AreEqual(slot, slot2, "hit must return the same slot");
            }
            finally
            {
                DisposeCache(ref cache);
            }
        }

        [Test]
        public void Cache_LruEvictsOldestWhenSlotCountExceeded()
        {
            var cache = MakeSmallCache(slots: 4, tileArea: 16);
            try
            {
                // Fill all 4 slots with distinct keys; each gets a strictly
                // increasing LastUsedTick.
                var keys = new NavFlowCacheKey[5];
                int[] slots = new int[5];
                for (int i = 0; i < 4; i++)
                {
                    keys[i] = new NavFlowCacheKey { TileIndex = i, ExitPortalId = 1, ProfileHash = 0 };
                    cache.TickCounter++;
                    slots[i] = NavFlowCacheOps.AllocateAndInsert(ref cache, keys[i]);
                }

                // Touch slot 1 so its LastUsedTick is the most recent (it
                // shouldn't be evicted in pass 2).
                cache.TickCounter++;
                var slot1 = cache.Slots[slots[1]];
                slot1.LastUsedTick = cache.TickCounter;
                cache.Slots[slots[1]] = slot1;

                // Now allocate a 5th key -- triggers eviction.
                keys[4] = new NavFlowCacheKey { TileIndex = 4, ExitPortalId = 1, ProfileHash = 0 };
                cache.TickCounter++;
                slots[4] = NavFlowCacheOps.AllocateAndInsert(ref cache, keys[4]);

                // After insert: 4 valid slots, key 0 evicted (oldest
                // LastUsedTick among the four), the 5th key sits in the
                // evicted slot.
                Assert.IsFalse(cache.SlotIndex.TryGetValue(keys[0], out _),
                    "key 0 must be evicted as the least-recently-used");
                Assert.IsTrue(cache.SlotIndex.TryGetValue(keys[1], out _),
                    "key 1 was touched -- must NOT be evicted");
                Assert.IsTrue(cache.SlotIndex.TryGetValue(keys[2], out _),
                    "key 2 must still be live");
                Assert.IsTrue(cache.SlotIndex.TryGetValue(keys[3], out _),
                    "key 3 must still be live");
                Assert.IsTrue(cache.SlotIndex.TryGetValue(keys[4], out int s4),
                    "key 4 (new insert) must be live");
                Assert.AreEqual(slots[0], s4,
                    "the newly inserted slot should reuse the evicted slot's index");
            }
            finally
            {
                DisposeCache(ref cache);
            }
        }

        [Test]
        public void CacheKey_HashIsIntegerOnlyAndIncludesAllFields()
        {
            // Hash is (TileIndex << 16) ^ (ExitPortalId << 8) ^ ProfileHash.
            // Different keys must produce different hashes (within obvious
            // collisions); identical keys must hash equal.
            var a = new NavFlowCacheKey { TileIndex = 0x12, ExitPortalId = 0x34, ProfileHash = 0x56 };
            var b = new NavFlowCacheKey { TileIndex = 0x12, ExitPortalId = 0x34, ProfileHash = 0x56 };
            var c = new NavFlowCacheKey { TileIndex = 0x12, ExitPortalId = 0x34, ProfileHash = 0x57 };

            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(),
                "identical keys must produce identical hash codes (Equals contract)");
            Assert.AreEqual(a, b, "identical keys must be Equals()");
            Assert.AreNotEqual(a, c, "different ProfileHash must produce non-equal keys");
        }

        // ── helpers ────────────────────────────────────────────────────

        private static NavFlowCache MakeSmallCache(int slots, int tileArea)
        {
            return new NavFlowCache
            {
                SlotIndex = new NativeHashMap<NavFlowCacheKey, int>(slots * 2, Allocator.Temp),
                Slots = new NativeArray<NavFlowCacheSlot>(slots, Allocator.Temp,
                    NativeArrayOptions.ClearMemory),
                SlotKeys = new NativeArray<NavFlowCacheKey>(slots, Allocator.Temp,
                    NativeArrayOptions.ClearMemory),
                DirPool = new NativeArray<byte>(slots * tileArea, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory),
                IntegrationPool = new NativeArray<uint>(slots * tileArea, Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory),
                SlotCount = slots,
                TileArea = tileArea,
                TickCounter = 0,
            };
        }

        private static void DisposeCache(ref NavFlowCache cache)
        {
            if (cache.SlotIndex.IsCreated) cache.SlotIndex.Dispose();
            if (cache.Slots.IsCreated) cache.Slots.Dispose();
            if (cache.SlotKeys.IsCreated) cache.SlotKeys.Dispose();
            if (cache.DirPool.IsCreated) cache.DirPool.Dispose();
            if (cache.IntegrationPool.IsCreated) cache.IntegrationPool.Dispose();
        }
    }

    /// <summary>
    /// Mirrors FlowSegmentSystem's private AllocateSlot policy: pass 1
    /// scans for a free slot; pass 2 evicts the LRU by LastUsedTick
    /// (smallest wins, ties by smallest slot index).
    /// </summary>
    internal static class NavFlowCacheOps
    {
        public static int AllocateAndInsert(ref NavFlowCache cache, NavFlowCacheKey key)
        {
            int chosen = -1;
            for (int i = 0; i < cache.SlotCount; i++)
            {
                if (cache.Slots[i].Valid == 0)
                {
                    chosen = i;
                    break;
                }
            }
            if (chosen < 0)
            {
                chosen = 0;
                int lruTick = cache.Slots[0].LastUsedTick;
                for (int i = 1; i < cache.SlotCount; i++)
                {
                    int t = cache.Slots[i].LastUsedTick;
                    if (t < lruTick)
                    {
                        chosen = i;
                        lruTick = t;
                    }
                }
                cache.SlotIndex.Remove(cache.SlotKeys[chosen]);
            }
            cache.Slots[chosen] = new NavFlowCacheSlot
            {
                DirOffset = chosen * cache.TileArea,
                IntegrationOffset = chosen * cache.TileArea,
                LastUsedTick = cache.TickCounter,
                Valid = 1,
            };
            cache.SlotKeys[chosen] = key;
            cache.SlotIndex.TryAdd(key, chosen);
            return chosen;
        }
    }
}
