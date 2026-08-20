// AIFeraldisEndgameSystem.Economy.cs
// Mines, war totems (the Feraldis territory verb), conscription and Age-2 build-out.
// Partial of AIFeraldisEndgameSystem.cs -- split 2026-08-12 for readability.

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
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        /// <summary>Builders a Feraldis AI keeps back for base expansion.
        /// Everyone else is a soldier.</summary>
        private const int KeepBuilders = 2;

        /// <summary>
        /// THE AGE-UP REWARD. Feraldis Workers cannot gather, so every worker
        /// beyond a small build crew is dead weight standing in the base —
        /// which is exactly what the 2026-08-05 match showed: yards full of
        /// idle workers and `military 0`.
        ///
        /// At age-up that surplus becomes a FREE ARMY instead. They are only
        /// light infantry (110 HP / 9 dmg), individually much worse than a
        /// trained Spearman, but they arrive instantly and for nothing — a
        /// weak rush force that is far better than the nothing Feraldis
        /// otherwise has the moment it ages up.
        ///
        /// They keep CanBuild, so they can still be pulled back to build; and
        /// FeraldisCultureRetrofitSystem has already stripped
        /// PassiveWorkerTag, so they hold ground and fight on their own.
        /// </summary>
        private static void ConscriptSurplusWorkers(EntityManager em, Faction faction, float3 hallPos)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FeraldisWorkerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);

            // Collect this faction's workers that are not already soldiering.
            var idle = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<ConscriptedTag>(ents[i])) continue;
                idle.Add(ents[i]);
            }

            // Leave the build crew behind.
            int conscript = idle.Length - KeepBuilders;
            if (conscript <= 0) { idle.Dispose(); return; }

            // Send them at whatever the AI is already fighting — the threat
            // hint if there is one, otherwise simply outward from the Hall so
            // they stop loitering in the base.
            float3 rally = hallPos;
            var sk = FindSharedKnowledge(em, faction);
            if (sk.EnemyLastSeenTime > 0f) rally = sk.EnemyLastKnownPosition;

            int sent = 0;
            for (int i = 0; i < idle.Length && sent < conscript; i++)
            {
                var w = idle[i];
                // Never conscript one that is mid-build.
                if (em.HasComponent<BuildCommand>(w)) continue;
                em.AddComponent<ConscriptedTag>(w);
                CommandRouter.IssueAttackMove(em, w, rally, CommandSource.AI);
                sent++;
            }
            if (sent > 0)
                AILogger.Log(faction, "MILITARY",
                    $"conscripted {sent} surplus Worker(s) as light infantry");
            idle.Dispose();
        }

        private static AISharedKnowledge FindSharedKnowledge(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<AISharedKnowledge>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<AIBrain>(ents[i]).Owner == faction)
                    return em.GetComponentData<AISharedKnowledge>(ents[i]);
            return default;
        }

        /// <summary>
        /// MINES ARE THE ONLY FERALDIS ORE. Feraldis Workers cannot gather,
        /// so without a Mine the faction's iron and veilstone stay at zero
        /// forever — which is exactly what the 2026-08-05 match showed: both
        /// Feraldis AIs ended on 13k-23k supplies and 0-1 iron, unable to
        /// afford a single building. This runs FIRST for that reason.
        /// </summary>
        private static void TryBuildMine(EntityManager em, Faction faction, float3 hallPos)
        {
            const int TargetMines = 2;
            if (CountFactionWith<MineTag>(em, faction) >= TargetMines) return;
            if (!BuildCosts.TryGet("Mine", out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;

            // A Mine only pays out next to ore, so search from the patches
            // rather than ringing the Hall.
            if (!TryFindOrePatch(em, hallPos, out float3 patch)) return;

            var size = BuildingSizeConfig.GetSize("Mine");
            if (!TryFindSpot(em, patch, size, 4f, 14f, out float3 pos)) return;
            // No AI-side Spend: PlaceBuildingDirect charges the cost on
            // every peer (docs/Multiplayer_LAN_Readiness.md).
            bool queued = CommandRouter.IssuePlaceBuilding(em, "Mine", pos, faction,
                out Entity site, CommandSource.AI);
            if (queued)
            {
                DispatchBuilders(em, faction, Entity.Null, "Mine", pos);
                AILogger.Log(faction, "BUILDING", $"Mine queued on ore at ({pos.x:0},{pos.z:0})");
                return;
            }
            // Null = the executor rejected — nothing spent, nothing to refund.
            if (site == Entity.Null) return;
            if (DispatchBuilders(em, faction, site, "Mine", pos) == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(site);
                return;
            }
            AILogger.Log(faction, "BUILDING", $"Mine placed on ore at ({pos.x:0},{pos.z:0})");
        }

        /// <summary>
        /// Nearest ore patch, IRON FIRST.
        ///
        /// The first version took whichever node was nearest of either kind,
        /// and the Mine diagnostic caught the result immediately: both of
        /// Blue's Mines reported "0 iron + 10 veilstone node(s) in range".
        /// They worked — just not on the resource the faction was starving
        /// for. Iron gates every building and unit; veilstone gates far less.
        /// So iron patches win outright, and veilstone is only a fallback
        /// when there is no reachable iron at all.
        /// </summary>
        private static bool TryFindOrePatch(EntityManager em, float3 hallPos, out float3 patch)
        {
            patch = default;

            if (TryNearestOf<IronMineTag>(em, hallPos, out patch)) return true;
            return TryNearestOf<VeilstoneOutcroppingTag>(em, hallPos, out patch);
        }

        private static bool TryNearestOf<T>(EntityManager em, float3 from, out float3 pos)
            where T : unmanaged, IComponentData
        {
            pos = default;
            float best = float.MaxValue;
            bool found = false;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                float d = math.distancesq(xfs[i].Position, from);
                if (d < best) { best = d; pos = xfs[i].Position; found = true; }
            }
            return found;
        }

        /// <summary>
        /// War Totems are Feraldis's ONLY territory — its ordinary buildings
        /// project no influence at all. The match log showed both Feraldis
        /// AIs at 0.0-2.1 % influence and the curse at 47 %: with no totems
        /// they had no curse suppression anywhere outside the Hall ring.
        /// Totems can only be planted on blood, so this looks for a pool.
        /// </summary>
        private static void TryPlantTotem(EntityManager em, Faction faction, float3 hallPos)
        {
            const int TargetTotems = 3;
            if (CountFactionWith<WarTotemTag>(em, faction) >= TargetTotems) return;
            if (!BuildCosts.TryGet("Feraldis_WarTotem", out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (!TheWaningBorder.Influence.BloodMap.Ready) return;

            if (!TryFindBloodSpot(em, faction, hallPos, out float3 spot)) return;

            var size = BuildingSizeConfig.GetSize("Feraldis_WarTotem");
            if (!BuildCommandHelper.IsValidBuildPosition(em, spot, size)) return;
            // No AI-side Spend: PlaceBuildingDirect charges the cost on
            // every peer (docs/Multiplayer_LAN_Readiness.md).
            bool tQueued = CommandRouter.IssuePlaceBuilding(em, "Feraldis_WarTotem", spot, faction,
                out Entity site, CommandSource.AI);
            if (tQueued)
            {
                DispatchBuilders(em, faction, Entity.Null, "Feraldis_WarTotem", spot);
                AILogger.Log(faction, "BUILDING", $"War Totem queued on blood at ({spot.x:0},{spot.z:0})");
                return;
            }
            // Null = the executor rejected — nothing spent, nothing to refund.
            if (site == Entity.Null) return;
            if (DispatchBuilders(em, faction, site, "Feraldis_WarTotem", spot) == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(site);
                return;
            }
            AILogger.Log(faction, "BUILDING", $"War Totem planted on blood at ({spot.x:0},{spot.z:0})");
        }

        /// <summary>
        /// Strongest blood ANYWHERE ON THE MAP, scanned off the blood grid
        /// itself rather than by ringing the Hall.
        ///
        /// The ring-the-Hall version never once planted a totem across five
        /// playtests, and the reason is structural: blood accumulates where
        /// fighting happens, fighting happens away from home, and blood
        /// inside your own influence FADES. So the one place guaranteed not
        /// to have a pool is the ground around your Hall. Reading the grid
        /// directly finds the battlefield instead.
        /// </summary>
        private static bool TryFindBloodSpot(EntityManager em, Faction faction,
            float3 hallPos, out float3 spot)
        {
            spot = default;
            if (!TheWaningBorder.Influence.PlayerInfluenceMap.Ready) return false;

            // Existing totems, so we don't stack. The 2026-08-06 match planted
            // TWENTY-SIX totems on the single cell (83,195): the scan returns
            // the global blood maximum, that maximum does not move, and every
            // totem placed there died before its builders arrived — so the
            // count never rose, and the AI re-placed on the same corpse pile
            // forever.
            var existing = new NativeList<float3>(Allocator.Temp);
            var tq = em.CreateEntityQuery(
                ComponentType.ReadOnly<WarTotemTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var ents = tq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                    if (em.GetComponentData<FactionTag>(ents[i]).Value == faction)
                        existing.Add(em.GetComponentData<LocalTransform>(ents[i]).Position);

            float best = TheWaningBorder.Core.Config.FeraldisConstants.TotemPlacementBloodThreshold;
            bool found = false;

            var wMin = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
            var wSize = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
            int res = TheWaningBorder.Influence.BloodMap.Resolution;
            float cellW = wSize.x / res;
            float cellH = wSize.y / res;

            // Row-major walk = deterministic tie-breaking for lockstep.
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float b = TheWaningBorder.Influence.BloodMap.CellValue(x, y);
                    if (b <= best) continue;

                    float wx = wMin.x + (x + 0.5f) * cellW;
                    float wz = wMin.y + (y + 0.5f) * cellH;

                    // Never re-plant on top of a totem we already have.
                    bool crowded = false;
                    for (int e = 0; e < existing.Length && !crowded; e++)
                    {
                        float ex = existing[e].x - wx, ez = existing[e].z - wz;
                        crowded = ex * ex + ez * ez < MinTotemSpacing * MinTotemSpacing;
                    }
                    if (crowded) continue;

                    best = b;
                    spot = new float3(wx, TerrainUtility.GetHeight(wx, wz), wz);
                    found = true;
                }
            }
            existing.Dispose();
            return found;
        }

        /// <summary>Totems must tile new ground, not stack on the bloodiest
        /// cell. Comfortably wider than a totem's own burn radius.</summary>
        private const float MinTotemSpacing = 36f;
        /// <summary>
        /// Raider Camps ARE the Feraldis economy — its huts gather nothing,
        /// so camp count is the only thing that scales income. Keep building
        /// Gatherer's Huts; they convert to camps automatically.
        /// </summary>
        private static void TryBuildEconomy(EntityManager em, Faction faction, float3 hallPos)
        {
            int camps = CountFactionWith<RaiderCampTag>(em, faction);
            if (camps >= TargetRaiderCamps) return;
            TryPlace(em, faction, "GatherersHut", hallPos, 24f, 90f,
                AIBudgetCategory.EconomyExpansion);
        }

        private static void TryBuildAge2(EntityManager em, Faction faction, float3 hallPos)
        {
            // NOTE: no early `return` between these. An earlier version bailed
            // out after the first attempt, so a Thrower Camp that could not be
            // placed (ring full of the AI's own Gatherer's Huts) silently
            // blocked the Pasture and the Temple behind it for the whole
            // match. Each one now gets its own shot every tick.
            //
            // Thrower Camp first — the match log was 17 x "floor unit Archer
            // has no trainer", i.e. the AI wanted ranged units for 30 minutes
            // and had nowhere to train them. The ring is widened well past
            // the hut belt for the same reason.
            if (CountFactionWith<ArcheryRangeTag>(em, faction) < 1)
                TryPlace(em, faction, "ArcheryRange", hallPos, 14f, 80f,
                    AIBudgetCategory.Military);

            if (CountFactionWith<PastureTag>(em, faction) < 1)
                TryPlace(em, faction, "Feraldis_Pasture", hallPos, 14f, 80f,
                    AIBudgetCategory.Military);

            if (CountFactionWith<TempleOfRidanTag>(em, faction) < 1)
                TryPlace(em, faction, "TempleOfRidan", hallPos, 14f, 80f,
                    AIBudgetCategory.Advancement);
        }
    }
}
