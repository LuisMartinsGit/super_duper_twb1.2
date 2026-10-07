// SimpleAISystem.BaseTemplate.cs
// The drawn base layout, as the site search uses it (2026-10-06,
// docs/Design/Game_AI.md § 6g; the layouts and their slots: AIBaseTemplate).
//
// A building the layout has a slot for takes the first free one of its kind,
// nearest the Fortress first. A slot that cannot be used as drawn — something
// already stands on it, a node, a cliff, cursed ground — moves to the nearest
// legal spot within slotSearchRadiusCells and is remembered there. Only when
// every slot of its kind is full or unusable does the building fall back to
// the ordinary ring search (which keeps off the free slots).

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        const int SlotNone = 0, SlotFound = 1, SlotBudgetSpent = 2;

        readonly List<int> _slotScratch = new List<int>(32);

        /// <summary>
        /// The template site for <paramref name="buildingId"/> in the territory
        /// <paramref name="anchor"/> stands in (or the locked territory), if
        /// its layout has a free slot for it.
        /// </summary>
        private int TryTemplateSlot(EntityManager em, Faction faction, string buildingId,
            int2 size, float3 anchor, out float3 pos)
        {
            pos = default;
            if (!AIBaseTemplate.Enabled || !TheWaningBorder.World.Regions.RegionMap.Ready) return SlotNone;
            int region = _siteRegionLock != TheWaningBorder.World.Regions.RegionMap.None
                ? _siteRegionLock
                : TheWaningBorder.World.Regions.RegionMap.RegionAt(anchor.x, anchor.z);
            if (!AIBaseTemplate.TryGetOrigin(em, faction, region, out float3 origin, out var layout))
                return SlotNone;

            AIBaseTemplate.CandidateSlots(faction, region, layout, buildingId, size, _slotScratch);
            if (_slotScratch.Count == 0) return SlotNone;
            var snap = BuildSiteSnapshot.Current(em);

            for (int k = 0; k < _slotScratch.Count; k++)
            {
                int i = _slotScratch[k];
                float3 p = AIBaseTemplate.SlotPosition(faction, region, layout, i, origin);
                if (AIBaseTemplate.IsFilled(snap, faction, p, size)) continue;

                // As drawn (or where it was moved to): the layout already
                // keeps its lanes, the wall line and the other slots clear, so
                // only the game's own placement rules apply.
                int v = SlotLegal(em, faction, buildingId, region, p, size, i, layoutRules: false);
                if (v == SlotBudgetSpent) return v;
                if (v == SlotFound)
                {
                    AILogger.Log(faction, "BUILD",
                        $"base layout: {buildingId} -> {(AIBaseTemplate.IsMain(layout) ? "main camp" : "outpost")} " +
                        $"slot {i} at ({p.x:F0},{p.z:F0})");
                    p.y = TerrainUtility.GetHeight(p.x, p.z);
                    pos = p;
                    return SlotFound;
                }

                // Blocked: the nearest legal spot round where it was drawn,
                // square rings outward, a fixed order on every peer.
                float3 drawn = AIBaseTemplate.DrawnPosition(faction, region, layout, i, origin);
                int R = math.max(0, AIBaseTemplate.SearchRadiusCells);
                float cs = BuildGrid.CellSize;
                for (int d = 1; d <= R; d++)
                {
                    for (int dz = -d; dz <= d; dz++)
                        for (int dx = -d; dx <= d; dx++)
                        {
                            if (math.max(math.abs(dx), math.abs(dz)) != d) continue;
                            float3 c = BuildGrid.Snap(new float3(drawn.x + dx * cs, 0f, drawn.z + dz * cs), size);
                            int w = SlotLegal(em, faction, buildingId, region, c, size, i, layoutRules: true);
                            if (w == SlotBudgetSpent) return w;
                            if (w != SlotFound) continue;
                            AIBaseTemplate.MoveSlot(faction, region, i, c);
                            AILogger.Log(faction, "BUILD",
                                $"base layout: {buildingId} slot {i} moved {d} cell(s) to ({c.x:F0},{c.z:F0}) — " +
                                $"blocked as drawn");
                            c.y = TerrainUtility.GetHeight(c.x, c.z);
                            pos = c;
                            return SlotFound;
                        }
                }
            }
            return SlotNone;
        }

        /// <summary>
        /// The placement rules a template site must pass. <paramref name="layoutRules"/>
        /// adds the base-layout keep-outs for a MOVED slot: the wall corridor,
        /// the other free slots, the reserved Fortress spots and the seal check.
        /// </summary>
        private int SlotLegal(EntityManager em, Faction faction, string buildingId, int region,
            float3 c, int2 size, int slot, bool layoutRules)
        {
            var snap = BuildSiteSnapshot.Current(em);
            if (TheWaningBorder.World.Regions.RegionMap.RegionAt(c.x, c.z) != region) return SlotNone;
            if (IsCursedGround(em, c)) return SlotNone;
            if (!TheWaningBorder.World.Regions.TerritoryOwnership.CanBuildAt(em, faction, buildingId, c.x, c.z))
                return SlotNone;
            if (!snap.OnFreeNodeFor(em, buildingId, c.x, c.z)) return SlotNone;
            if (snap.Overlaps(c, size, 0f, ignoreWalls: false)) return SlotNone;
            if (snap.OverlapsOwnPlan(faction, c, size)) return SlotNone;
            if (PlannedBuildings.IsRecentlyRefused(faction, c, Cfg.refusedSpotRadius, _thinkNow)) return SlotNone;
            if (layoutRules)
            {
                if (!AIWallCorridor.FootprintClear(em, faction, c, size)) return SlotNone;
                if (!AIBaseTemplate.FootprintClearOfFreeSlots(em, faction, c, size, exceptSlot: slot)) return SlotNone;
                if (!AIBaseLayout.FootprintClearOfFortressSpots(c, size, buildingId)) return SlotNone;
            }

            if (_siteValidationsLeft <= 0) return SlotBudgetSpent;
            _siteValidationsLeft--;
            c.y = TerrainUtility.GetHeight(c.x, c.z);
            if (!snap.IsValidBuildPosition(em, c, size, buildingId)) return SlotNone;

            if (layoutRules && Cfg.sealCheckEnabled)
            {
                if (_sealChecksLeft <= 0) return SlotBudgetSpent;
                _sealChecksLeft--;
                if (AIBaseLayout.WouldSeal(em, faction, c, size, buildingId, _thinkNow) != null) return SlotNone;
            }
            return SlotFound;
        }
    }
}
