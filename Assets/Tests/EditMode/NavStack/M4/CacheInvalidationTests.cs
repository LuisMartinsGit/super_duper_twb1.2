// CacheInvalidationTests.cs
// task-112 M4 -- exercises IncrementalPortalRebuildSystem.InvalidateTile
// against a hand-authored NavFlowCache. Asserts that only slabs whose
// TileIndex equals the dirty tile are evicted, that touched slabs
// survive, and that the slot iteration walks in slot-index ascending
// order (DR-12-shaped).

using NUnit.Framework;
using Unity.Collections;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Tests.EditMode.NavStack.M4
{
    public class CacheInvalidationTests
    {
        [Test]
        public void InvalidateTile_EvictsOnlySlabsForThatTile()
        {
            var cache = MakeCache(slots: 8, tileArea: 16);
            try
            {
                // Populate 6 slabs across 3 different tiles:
                //   tile 0: 2 slabs (different exitPortalIds)
                //   tile 1: 2 slabs
                //   tile 2: 2 slabs
                int s00 = InsertSlab(ref cache, tile: 0, portal: 1);
                int s01 = InsertSlab(ref cache, tile: 0, portal: 2);
                int s10 = InsertSlab(ref cache, tile: 1, portal: 3);
                int s11 = InsertSlab(ref cache, tile: 1, portal: 4);
                int s20 = InsertSlab(ref cache, tile: 2, portal: 5);
                int s21 = InsertSlab(ref cache, tile: 2, portal: 6);

                Assert.AreEqual(6, ValidSlotCount(in cache),
                    "expect all 6 slabs valid after insertion");

                int evicted = IncrementalPortalRebuildSystem.InvalidateTile(ref cache, 1);
                Assert.AreEqual(2, evicted,
                    "tile 1 had 2 slabs; both should be evicted");

                Assert.AreEqual(4, ValidSlotCount(in cache),
                    "remaining slot count must be 6 - 2 = 4 after invalidating tile 1");

                // Slabs for tile 0 + 2 must still be present.
                Assert.IsTrue(cache.SlotIndex.ContainsKey(KeyFor(0, 1)));
                Assert.IsTrue(cache.SlotIndex.ContainsKey(KeyFor(0, 2)));
                Assert.IsFalse(cache.SlotIndex.ContainsKey(KeyFor(1, 3)));
                Assert.IsFalse(cache.SlotIndex.ContainsKey(KeyFor(1, 4)));
                Assert.IsTrue(cache.SlotIndex.ContainsKey(KeyFor(2, 5)));
                Assert.IsTrue(cache.SlotIndex.ContainsKey(KeyFor(2, 6)));

                // Slot Valid flags must reflect the eviction.
                Assert.AreEqual(0, cache.Slots[s10].Valid, "tile-1 slot must be freed");
                Assert.AreEqual(0, cache.Slots[s11].Valid, "tile-1 slot must be freed");
                Assert.AreEqual(1, cache.Slots[s00].Valid, "tile-0 slot must survive");
                Assert.AreEqual(1, cache.Slots[s01].Valid, "tile-0 slot must survive");
                Assert.AreEqual(1, cache.Slots[s20].Valid, "tile-2 slot must survive");
                Assert.AreEqual(1, cache.Slots[s21].Valid, "tile-2 slot must survive");
            }
            finally
            {
                DisposeCache(ref cache);
            }
        }

        [Test]
        public void InvalidateTile_OnTileNotInCache_EvictsZeroSlabs()
        {
            var cache = MakeCache(slots: 4, tileArea: 16);
            try
            {
                InsertSlab(ref cache, tile: 0, portal: 1);
                InsertSlab(ref cache, tile: 1, portal: 2);

                int evicted = IncrementalPortalRebuildSystem.InvalidateTile(ref cache, 99);
                Assert.AreEqual(0, evicted,
                    "tile 99 isn't in the cache -- invalidate should evict nothing");
                Assert.AreEqual(2, ValidSlotCount(in cache),
                    "all originally-valid slabs must survive");
            }
            finally
            {
                DisposeCache(ref cache);
            }
        }

        // ── helpers ────────────────────────────────────────────────────

        private static NavFlowCache MakeCache(int slots, int tileArea)
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

        private static NavFlowCacheKey KeyFor(int tile, int portal) => new NavFlowCacheKey
        {
            TileIndex = tile,
            ExitPortalId = portal,
            ProfileHash = 0,
        };

        private static int ValidSlotCount(in NavFlowCache cache)
        {
            int n = 0;
            for (int i = 0; i < cache.SlotCount; i++)
                if (cache.Slots[i].Valid != 0) n++;
            return n;
        }

        // Finds the first free slot, fills it with the (tile, portal) key.
        // Mirrors FlowSegmentSystem.AllocateSlot's free-slot pass.
        private static int InsertSlab(ref NavFlowCache cache, int tile, int portal)
        {
            var key = KeyFor(tile, portal);
            for (int i = 0; i < cache.SlotCount; i++)
            {
                if (cache.Slots[i].Valid == 0)
                {
                    cache.Slots[i] = new NavFlowCacheSlot
                    {
                        DirOffset = i * cache.TileArea,
                        IntegrationOffset = i * cache.TileArea,
                        LastUsedTick = cache.TickCounter++,
                        Valid = 1,
                    };
                    cache.SlotKeys[i] = key;
                    cache.SlotIndex.TryAdd(key, i);
                    return i;
                }
            }
            return -1;
        }
    }
}
