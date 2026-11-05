// Monument.cs
// MONUMENTS (docs/Design/Monuments_And_Petriarchy.md, 2026-11-05): one in each
// starting curse territory. Conquered by standing on it, trickles religion
// points to its holder, and its ruins can be explored once, for a random buff.
// One hidden monument also releases the match's single Shardroot.
//
// Not a building on purpose: no BuildingTag and no Health, so nothing targets
// it, and territory, elimination and score never see it. FactionTag follows the
// holder so the selection panel treats a held monument as the player's own;
// Faction.Border means the curse holds it (or, with no curse, nobody does).

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

public struct MonumentTag : IComponentData { }

public struct MonumentState : IComponentData
{
    public int Territory;
    public Faction Holder;
    /// <summary>The capture meter, in population-seconds (0 to capturePopSeconds).</summary>
    public int Meter;
    public byte Explored;
    public byte HidesShardroot;
}

namespace TheWaningBorder.Entities
{
    public static class Monument
    {
        public const int PresentationID = 398;

        private static MonumentSystemConfig Cfg => MonumentSystemConfig.I;

        private static readonly ComponentType[] QT_Monuments =
        {
            ComponentType.ReadOnly<MonumentTag>(),
            ComponentType.ReadOnly<MonumentState>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Monuments;

        private static readonly ComponentType[] QT_Outcrops =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Outcrops;

        private static readonly ComponentType[] QT_CurseNodes =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_CurseNodes;

        private static readonly ComponentType[] QT_ShardrootState = { ComponentType.ReadWrite<ShardrootState>() };
        private static CachedEntityQuery QC_ShardrootState;

        /// <summary>Every monument, in entity order. Callers dispose.</summary>
        public static NativeArray<Entity> All(EntityManager em, Allocator allocator)
            => QC_Monuments.Get(em, QT_Monuments).ToEntityArray(allocator);

        /// <summary>
        /// Place the match's monuments. Idempotent: the caller runs it before and
        /// after raising the curse, because a map with no Veilstone rich territory
        /// only knows its curse territories once the nodes have risen.
        /// </summary>
        public static int SpawnAll(EntityManager em)
        {
            if (!RegionMap.Ready) return 0;
            if (!QC_Monuments.Get(em, QT_Monuments).IsEmptyIgnoreFilter) return 0;

            var territories = TerritoryResources.TerritoriesOf(
                TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType.VeilstoneRich);
            territories = new List<int>(territories);
            if (territories.Count == 0)
            {
                using var nodes = QC_CurseNodes.Get(em, QT_CurseNodes)
                    .ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < nodes.Length; i++)
                {
                    int t = RegionMap.NearestRegion(nodes[i].Position.x, nodes[i].Position.z);
                    if (!territories.Contains(t)) territories.Add(t);
                }
            }
            if (territories.Count == 0) return 0;
            territories.Sort();

            using var outcrops = QC_Outcrops.Get(em, QT_Outcrops)
                .ToComponentDataArray<LocalTransform>(Allocator.Temp);

            uint hash = (uint)GameSettings.SpawnSeed * 2654435761u + 131u;
            hash ^= hash >> 13; hash *= 0x5bd1e995u; hash ^= hash >> 15;
            int host = (int)(hash % (uint)territories.Count);

            for (int i = 0; i < territories.Count; i++)
            {
                float3 at = SpotIn(territories[i], outcrops);
                Create(em, at, territories[i], i == host);
            }
            UnityEngine.Debug.Log($"[Monument] {territories.Count} monuments placed; " +
                $"the Shardroot hides in territory {territories[host]}");
            return territories.Count;
        }

        /// <summary>The territory's seed, or the nearest ring spot inside the
        /// territory that keeps clear of its veilstone outcrops.</summary>
        private static float3 SpotIn(int territory, NativeArray<LocalTransform> outcrops)
        {
            var seed = RegionMap.SeedOf(territory);
            float clear = Cfg.outcropClearance;
            for (int ring = 0; ring <= Cfg.placementRings; ring++)
            {
                int samples = ring == 0 ? 1 : 8 * ring;
                for (int s = 0; s < samples; s++)
                {
                    float angle = math.PI * 2f * s / samples;
                    float x = seed.x + math.cos(angle) * ring * Cfg.placementRingStep;
                    float z = seed.y + math.sin(angle) * ring * Cfg.placementRingStep;
                    if (RegionMap.NearestRegion(x, z) != territory) continue;
                    bool ok = true;
                    for (int n = 0; n < outcrops.Length && ok; n++)
                        ok = math.distance(outcrops[n].Position.xz, new float2(x, z)) >= clear;
                    if (ok) return new float3(x, TerrainUtility.GetHeight(x, z), z);
                }
            }
            return new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.y), seed.y);
        }

        private static Entity Create(EntityManager em, float3 position, int territory, bool hidesShardroot)
        {
            var e = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(MonumentTag),
                typeof(MonumentState),
                typeof(Radius));
            em.SetComponentData(e, new PresentationId { Id = PresentationID });
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(e, new FactionTag { Value = Faction.Border });
            em.SetComponentData(e, new Radius { Value = Cfg.selectionRadius });
            em.SetComponentData(e, new MonumentState
            {
                Territory = territory,
                Holder = Faction.Border,
                Meter = Cfg.capturePopSeconds,
                HidesShardroot = (byte)(hidesShardroot ? 1 : 0),
            });
            return e;
        }

        private static bool TryFind(EntityManager em, int territory, out Entity entity, out MonumentState state)
        {
            entity = Entity.Null;
            state = default;
            using var ents = All(em, Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var s = em.GetComponentData<MonumentState>(ents[i]);
                if (s.Territory != territory) continue;
                entity = ents[i];
                state = s;
                return true;
            }
            return false;
        }

        public static Cost ExploreCost => Cost.Of(
            supplies: Cfg.exploreSupplies, iron: Cfg.exploreIron, veilstone: Cfg.exploreVeilstone);

        public static bool CanExplore(EntityManager em, Faction faction, int territory)
        {
            if (!TryFind(em, territory, out _, out var s)) return false;
            return s.Holder == faction && s.Explored == 0
                && FactionEconomy.CanAfford(em, faction, ExploreCost);
        }

        /// <summary>Executor side: every peer runs it at the same tick.</summary>
        public static bool TryExplore(EntityManager em, Faction faction, int territory)
        {
            if (!CanExplore(em, faction, territory)) return false;
            if (!TryFind(em, territory, out var entity, out var state)) return false;
            if (!FactionEconomy.Spend(em, faction, ExploreCost, SpendCategory.Religion)) return false;

            state.Explored = 1;
            em.SetComponentData(entity, state);

            string buff = RollBuff(faction, territory);
            string line = buff != null ? TechCatalog.GetTechnology(buff).name : null;
            UnityEngine.Debug.Log($"[Monument] {faction} explored the ruins in territory {territory}: " +
                $"buff {buff ?? "none left"}, Shardroot {(state.HidesShardroot != 0 ? "HERE" : "not here")}");
            if (faction == GameSettings.LocalPlayerFaction)
                SimSignals.Notify(line != null
                    ? string.Format(Loc.T("The ruins yield {0}."), line)
                    : Loc.T("The ruins hold nothing more for you."));

            if (state.HidesShardroot != 0)
                Descend(em, em.GetComponentData<LocalTransform>(entity).Position);
            return true;
        }

        /// <summary>A buff this faction does not have yet, from the seed.</summary>
        private static string RollBuff(Faction faction, int territory)
        {
            var research = FactionResearchState.Instance;
            var pool = new List<string>();
            foreach (var id in Cfg.buffTechIds)
                if (research != null && !research.HasResearched(faction, id)) pool.Add(id);
            if (pool.Count == 0) return null;

            uint seed = (uint)GameSettings.SpawnSeed * 2654435761u
                + (uint)territory * 40503u + (uint)faction * 16777619u;
            var rng = new Unity.Mathematics.Random(seed | 1u);
            string pick = pool[rng.NextInt(0, pool.Count)];
            research.CompleteResearch(faction, pick);
            return pick;
        }

        /// <summary>The single Shardroot descends in front of this monument.</summary>
        private static void Descend(EntityManager em, float3 monument)
        {
            var q = QC_ShardrootState.Get(em, QT_ShardrootState);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.Found != 0) return;
            state.Found = 1;
            em.SetComponentData(ents[0], state);

            float3 at = monument + new float3(0f, 0f, Cfg.shardrootOffset);
            at.y = TerrainUtility.GetHeight(at.x, at.z);
            var pickup = ShardrootPickup.Create(em, at, RitualKind.ViolentExtraction, ShardrootState.ShardrootPower);
            em.AddComponent<ShardrootTag>(pickup);
            TheWaningBorder.Systems.Border.ShardrootSystem.MakePersistent(em, pickup);
            SimSignals.Announce(Loc.T("The SHARDROOT DESCENDS before the ruins!"));
            SimSignals.Ping(at, SimPingKind.Discovery, 20f, big: true);
            UnityEngine.Debug.Log($"[Shardroot] DESCENT -- the ruins are explored: the artifact descends at ({at.x:F0},{at.z:F0})");
        }
    }
}
