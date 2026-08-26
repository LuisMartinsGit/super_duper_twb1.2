// AIEndgameCommon.cs
// Shared mechanics for the per-culture endgame systems.
//
// task-088 chose "spin per-culture peers" over "generalize the endgame
// driver", which left AIAlanthorEndgameSystem and AIFeraldisEndgameSystem
// carrying near-identical copies of the culture-NEUTRAL half of their work.
// The copies drifted: Feraldis's temple ladder was missing every guard its
// Alanthor twin had, so it re-issued an upgrade command every 5 s tick with
// no cost check. Anything in here is mechanics, not doctrine — the culture
// flavour stays in the caller (which sects, which ritualist, which target).
//
// Location: Assets/Scripts/Systems/AI/AIEndgameCommon.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    /// <summary>Culture-neutral endgame mechanics shared by the per-culture
    /// endgame systems. Not Bursted — the callers aren't either (managed
    /// throttle dictionaries), and these touch managed catalogs.</summary>
    public static class AIEndgameCommon
    {
        /// <summary>Supplies held back so chapel adoption never eats the
        /// economy's floor. Was duplicated verbatim in both systems.</summary>
        public const int ChapelReserveSupplies = 100;
        /// <summary>Veilstone counterpart of <see cref="ChapelReserveSupplies"/>.</summary>
        public const int ChapelReserveVeilstone = 40;

        /// <summary>Standoff radius for a ritual escort ring. Both cultures
        /// converged on 14 m after the 2026-08-07 FFA8 postmortem (escorts
        /// piled onto the well itself and blocked the ritualist); it lived as
        /// two separate literals until this was extracted.</summary>
        public const float EscortStandoffRadius = 14f;

        /// <summary>First completed-or-not building of a tag owned by the faction.</summary>
        public static Entity FindFactionBuilding<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == faction)
                    return ents[i];
            return Entity.Null;
        }

        // ──────────────────────────────────────────────────────────────────
        // TEMPLE LADDER
        // ──────────────────────────────────────────────────────────────────

        private static readonly Dictionary<Faction, int> _templeBlockTicks = new();

        /// <summary>
        /// Climb the Temple of Ridan one level, at most one attempt per tick.
        /// Era progression, sect levers and the culture's ritualist all hang
        /// off temple level, so this is the victory path for both cultures.
        ///
        /// Deliberately NOT budget-windowed (2026-08-11): the 500-1200 supply
        /// single spends starved inside the Advancement window's weighted
        /// share. Bank affordability still gates, and a short bank RESERVES
        /// the cost so discretionary spending holds until the lump forms.
        ///
        /// Every guard here is load-bearing — without the in-progress and
        /// UnderConstruction checks this re-fires the upgrade command on every
        /// think tick (the bug the Feraldis copy shipped with).
        /// </summary>
        public static void TryLevelTemple(EntityManager em, Faction faction)
        {
            Entity temple = FindFactionBuilding<TempleOfRidanTag>(em, faction);
            if (temple == Entity.Null
                || !em.HasComponent<TempleLevel>(temple)
                || em.HasComponent<UnderConstruction>(temple)
                || em.HasComponent<TempleUpgradeState>(temple)
                || em.GetComponentData<TempleLevel>(temple).Level >= TempleLevelConfig.MaxLevel)
            {
                // No fundable goal right now — never hold the economy for it.
                AIPivotalReserve.Clear(faction, "Temple");
                return;
            }

            int level = em.GetComponentData<TempleLevel>(temple).Level;
            var cost = TempleLevelConfig.GetUpgradeCost(level);
            // Affordability CHECK only — TempleUpgradeCommandDirect spends
            // on every peer (docs/Multiplayer_LAN_Readiness.md). The
            // reserve bookkeeping below is unchanged: a short bank still
            // holds the lump for the temple.
            if (!FactionEconomy.CanAfford(em, faction, cost))
            {
                AIPivotalReserve.Set(faction, "Temple", cost);
                _templeBlockTicks.TryGetValue(faction, out int ticks);
                if (++ticks >= 12)   // ~1 minute at the 5 s think interval
                {
                    ticks = 0;
                    AILogger.Log(faction, "BUILDING",
                        $"Temple L{level + 1} blocked ~1 min (bank short: " +
                        $"{cost.Supplies}s {cost.Iron}i {cost.Veilstone}v)");
                }
                _templeBlockTicks[faction] = ticks;
                return;
            }
            _templeBlockTicks.Remove(faction);
            AIPivotalReserve.Clear(faction, "Temple");

            CommandRouter.IssueTempleUpgrade(em, temple, CommandSource.AI);
            AILogger.Log(faction, "BUILDING", $"Temple upgrading to L{level + 1}");
        }

        // ──────────────────────────────────────────────────────────────────
        // SECT ADOPTION
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Adopt the first un-adopted sect in the caller's priority order —
        /// one adoption per tick. The priority array is the ONLY culture
        /// input; everything else (temple host, reserve floor, RP result
        /// handling, replicated slot stamp) is identical across cultures.
        /// </summary>
        public static void TryAdoptNextSect(EntityManager em, Faction faction, string[] priority)
        {
            Entity temple = FindFactionBuilding<TempleOfRidanTag>(em, faction);
            if (temple == Entity.Null) return;

            for (int i = 0; i < priority.Length; i++)
            {
                string sectId = priority[i];
                if (SectQuery.IsAdopted(em, faction, sectId)) continue;
                if (!BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)) continue;

                if (!FactionEconomy.TryGetResources(em, faction, out var res)) return;
                if (res.Supplies  < chapelCost.Supplies  + ChapelReserveSupplies)  return;
                if (res.Veilstone < chapelCost.Veilstone + ChapelReserveVeilstone) return;

                // Validate only — the RP + material SPEND happens inside
                // SectAdoptionCommandDirect on every peer, alongside the
                // slot stamp (docs/Multiplayer_LAN_Readiness.md).
                var result = SectAdoption.ValidateAdoption(em, faction, sectId, chapelCost, temple);
                if (result == SectAdoptionResult.Ok)
                {
                    // Replicated slot stamp (audit F7) — host-only writes left
                    // clients without the chapel or the sect bonuses.
                    CommandRouter.IssueSectAdoption(em, temple, sectId, -1, 30f, CommandSource.AI);
                    AILogger.Log(faction, "STRATEGY", $"adopting sect {sectId}");
                    return;
                }
                if (result == SectAdoptionResult.NotEnoughRP) return;   // wait for RP
                // slot full / already adopted -> try the next priority
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // ESCORT RING
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Position of escort slot <paramref name="index"/> on an evenly-spaced
        /// ring around <paramref name="center"/>. Both cultures screen a well
        /// ritualist exactly this way; sending escorts AT the well made them
        /// body-block the ritualist out of its own channel (2026-08-07 FFA8
        /// postmortem — see the dispatch sites for the measurements).
        /// <paramref name="slots"/> is the angular divisor, so a partly-filled
        /// escort keeps its spacing instead of bunching into one arc.
        /// </summary>
        public static float3 EscortSlot(float3 center, int index, int slots, float radius)
        {
            if (slots <= 0) slots = 1;
            float ang = (index / (float)slots) * 2f * math.PI;
            return center + new float3(
                math.cos(ang) * radius, 0f,
                math.sin(ang) * radius);
        }

        // ──────────────────────────────────────────────────────────────────
        // BUILD-SPOT RING SEARCH
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Walk rings outward from <paramref name="anchor"/> and return the
        /// first spot the placement rules accept. One algorithm, two tunings:
        /// the caller passes its own sample count / radius step so behaviour is
        /// unchanged from the two copies this replaces.
        ///
        /// <paramref name="seededStart"/> offsets the first angle by a hash of
        /// (anchor, rmin, rmax). It is deterministic — same inputs give the
        /// same offset on every lockstep peer — and it stops every building of
        /// a given kind from trying due-east first and clumping there.
        /// </summary>
        public static bool TryFindBuildSpotRing(EntityManager em, float3 anchor,
            int2 buildingSize, float rmin, float rmax,
            int angleSamples, float radiusStep, bool seededStart, out float3 pos)
        {
            pos = default;
            if (angleSamples <= 0 || radiusStep <= 0f) return false;

            var rng = default(Unity.Mathematics.Random);
            if (seededStart)
            {
                // Hash whole millimetres, not raw float bits: hashing the raw
                // anchor meant 1 ULP of drift picks a different scan start and
                // therefore a different placement cell.
                uint seed = math.hash(new int3(
                    (int)math.round(anchor.x * 1000f),
                    (int)math.round(rmin * 1000f),
                    (int)math.round(rmax * 1000f)));
                rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            }

            for (float r = rmin; r <= rmax; r += radiusStep)
            {
                int start = seededStart ? rng.NextInt(0, angleSamples) : 0;
                for (int i = 0; i < angleSamples; i++)
                {
                    int idx = (start + i) % angleSamples;
                    float angle = (idx / (float)angleSamples) * math.PI * 2f;
                    float x = anchor.x + math.cos(angle) * r;
                    float z = anchor.z + math.sin(angle) * r;
                    // Snap before validating — BuildingFactory snaps on spawn,
                    // so an unsnapped candidate validates a position the
                    // building will not occupy. docs/Design/Build_Grid.md
                    var candidate = BuildGrid.Snap(new float3(x, 0f, z), buildingSize);
                    candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                    if (BuildCommandHelper.IsValidBuildPosition(em, candidate, buildingSize))
                    {
                        pos = candidate;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
