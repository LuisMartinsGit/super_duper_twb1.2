// Monument.cs
// MONUMENTS (docs/Design/Monuments_And_Petriarchy.md, 2026-11-05): one in each
// starting curse territory. Any unit in its ring for ten seconds takes it; it trickles religion
// points to its holder, and its ruins can be explored once, for its own buff.
// One hidden monument also releases the match's single Shardroot.
//
// Not a building on purpose: no BuildingTag and no Health, so nothing targets
// it, and territory, elimination and score never see it. FactionTag follows the
// holder so the selection panel treats a held monument as the player's own;
// Faction.Border means nobody holds it. The curse never does.

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
    /// <summary>Who is taking it right now (Border = nobody), and for how long.</summary>
    public Faction Capturer;
    public int CaptureSeconds;
    /// <summary>1 once anyone has explored it: its buff is then public.</summary>
    public byte Explored;
    /// <summary>Who has explored it, a bit per Faction: once per player.</summary>
    public uint ExploredBy;
    /// <summary>This monument's own buff: an index into the shuffled pool.</summary>
    public byte Buff;
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

        /// <summary>Random draws before giving up on the start clearance.</summary>
        private const int ShardrootPlacementTries = 512;

        private static readonly ComponentType[] QT_Halls =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Halls;

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

            // Every monument has its own buff: the pool, shuffled by the seed and
            // dealt out one each. A map with more monuments than buffs deals again.
            int poolSize = Cfg.buffTechIds.Length;
            var deal = new int[poolSize];
            for (int i = 0; i < poolSize; i++) deal[i] = i;
            var rng = new Unity.Mathematics.Random(hash | 1u);
            for (int i = poolSize - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (deal[i], deal[j]) = (deal[j], deal[i]);
            }

            for (int i = 0; i < territories.Count; i++)
            {
                float3 at = SpotIn(territories[i], outcrops);
                Create(em, at, territories[i], i == host, (byte)deal[i % poolSize]);
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

        private static Entity Create(EntityManager em, float3 position, int territory, bool hidesShardroot, byte buff)
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
                Capturer = Faction.Border,
                HidesShardroot = (byte)(hidesShardroot ? 1 : 0),
                Buff = buff,
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

        public static bool HasExplored(in MonumentState s, Faction faction)
            => (s.ExploredBy & (1u << (int)faction)) != 0;

        public static bool CanExplore(EntityManager em, Faction faction, int territory)
        {
            if (!TryFind(em, territory, out _, out var s)) return false;
            return s.Holder == faction && !HasExplored(s, faction)
                && FactionEconomy.CanAfford(em, faction, ExploreCost);
        }

        /// <summary>Executor side: every peer runs it at the same tick.</summary>
        public static bool TryExplore(EntityManager em, Faction faction, int territory)
        {
            if (!CanExplore(em, faction, territory)) return false;
            if (!TryFind(em, territory, out var entity, out var state)) return false;
            if (!FactionEconomy.Spend(em, faction, ExploreCost, SpendCategory.Religion)) return false;

            state.Explored = 1;
            state.ExploredBy |= 1u << (int)faction;
            em.SetComponentData(entity, state);

            string buff = GrantBuff(faction, state);
            string line = buff != null ? TechCatalog.GetTechnology(buff).name : null;
            UnityEngine.Debug.Log($"[Monument] {faction} explored the ruins in territory {territory}: " +
                $"buff {buff ?? "none left"}, Shardroot {(state.HidesShardroot != 0 ? "HERE" : "not here")}");
            if (faction == GameSettings.LocalPlayerFaction) SimSignals.Alert(SimAlertKind.Monument);
            if (faction == GameSettings.LocalPlayerFaction)
                SimSignals.Notify(line != null
                    ? string.Format(Loc.T("The ruins yield {0}."), line)
                    : Loc.T("The ruins hold nothing more for you."));

            if (state.HidesShardroot != 0)
                RevealShardroot(em, faction);
            return true;
        }

        /// <summary>This monument's own buff (a TechDefSO id). Hidden from the
        /// panels until its ruins are explored.</summary>
        public static string BuffOf(in MonumentState state)
        {
            var pool = Cfg.buffTechIds;
            return pool == null || pool.Length == 0 ? null : pool[state.Buff % pool.Length];
        }

        private static string GrantBuff(Faction faction, in MonumentState state)
        {
            string id = BuffOf(state);
            var research = FactionResearchState.Instance;
            if (id == null || research == null) return null;
            research.CompleteResearch(faction, id);
            return id;
        }

        /// <summary>Lay the match's single Shardroot, hidden, at a random walkable
        /// spot (Monuments_And_Petriarchy.md §3). Runs from MonumentSystem until the
        /// nav field exists; every peer draws the same spot from the match seed.</summary>
        public static void TryPlaceShardroot(EntityManager em)
        {
            var q = QC_ShardrootState.Get(em, QT_ShardrootState);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.Found != 0) return;

            if (!TryPickHiddenSpot(em, 0, out float3 at)) return;

            state.Found = 1;
            em.SetComponentData(ents[0], state);
            var pickup = ShardrootPickup.Create(em, at, RitualKind.ViolentExtraction, ShardrootState.ShardrootPower);
            em.AddComponent<ShardrootTag>(pickup);
            em.AddComponentData(pickup, default(ShardrootHidden));
            TheWaningBorder.Systems.Border.ShardrootSystem.MakePersistent(em, pickup);
            UnityEngine.Debug.Log($"[Shardroot] hidden at ({at.x:F0},{at.z:F0})");
        }

        /// <summary>A random spot reachable on foot from the first capital, clear of every
        /// capital and of every unit; the match seed and the move count pick it, so every
        /// peer draws the same one. False until the nav field and capitals exist.</summary>
        private static bool TryPickHiddenSpot(EntityManager em, int moves, out float3 at)
        {
            at = default;
            using var halls = QC_Halls.Get(em, QT_Halls).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hallFacs = QC_Halls.Get(em, QT_Halls).ToComponentDataArray<FactionTag>(Allocator.Temp);
            if (halls.Length == 0) return false;
            int first = 0;
            for (int i = 1; i < halls.Length; i++)
                if (hallFacs[i].Value < hallFacs[first].Value) first = i;
            var from = halls[first].Position;
            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.TryFloodWalkable(from.x, from.z, out var reach,
                    out var origin, out float cell, out int width, out int height)) return false;
            using var units = QC_PassUnits.Get(em, QT_PassUnits).ToComponentDataArray<LocalTransform>(Allocator.Temp);

            uint seed = (uint)GameSettings.SpawnSeed * 2654435761u + 977u + (uint)moves * 40503u;
            seed ^= seed >> 13; seed *= 0x5bd1e995u; seed ^= seed >> 15;
            var rng = new Unity.Mathematics.Random(seed | 1u);
            float hall2 = Cfg.shardrootStartClearance * Cfg.shardrootStartClearance;
            float pass2 = Cfg.shardrootPassRadius * Cfg.shardrootPassRadius;
            at = from;
            bool any = false;
            for (int t = 0; t < ShardrootPlacementTries; t++)
            {
                int cx = rng.NextInt(0, width), cz = rng.NextInt(0, height);
                if (!reach[cz * width + cx]) continue;
                float x = origin.x + (cx + 0.5f) * cell, z = origin.y + (cz + 0.5f) * cell;
                var p = new float2(x, z);
                bool clear = true;
                for (int i = 0; i < halls.Length && clear; i++)
                    clear = math.distancesq(halls[i].Position.xz, p) >= hall2;
                for (int i = 0; i < units.Length && clear; i++)
                    clear = math.distancesq(units[i].Position.xz, p) >= pass2;
                if (!clear && any) continue;
                at = new float3(x, TerrainUtility.GetHeight(x, z), z);
                any = true;
                if (clear) break;
            }
            return any;
        }

        /// <summary>A unit that passes without taking the hidden artifact sends it
        /// elsewhere (Monuments_And_Petriarchy.md §3): a side that was inside the circle
        /// at the last check and has nobody in it now has passed. Runs once a second.</summary>
        public static void TickHiddenShardroot(EntityManager em)
        {
            var q = QC_HiddenRoot.Get(em, QT_HiddenRoot);
            if (q.IsEmptyIgnoreFilter) return;
            using var roots = q.ToEntityArray(Allocator.Temp);
            var root = roots[0];
            var pos = em.GetComponentData<LocalTransform>(root);
            var h = em.GetComponentData<ShardrootHidden>(root);

            var uq = QC_PassUnits.Get(em, QT_PassUnits);
            using var xfs = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = uq.ToComponentDataArray<Health>(Allocator.Temp);
            float r2 = Cfg.shardrootPassRadius * Cfg.shardrootPassRadius;
            uint now = 0;
            for (int i = 0; i < xfs.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                int side = (int)facs[i].Value;
                if (side < 0 || side >= 32) continue;
                if (math.distancesq(xfs[i].Position.xz, pos.Position.xz) <= r2) now |= 1u << side;
            }
            uint passed = h.Near & ~now;
            h.Near = now;
            if (passed != 0 && TryPickHiddenSpot(em, h.Moves + 1, out float3 at))
            {
                h.Moves++;
                h.Near = 0;
                pos.Position = at;
                em.SetComponentData(root, pos);
                UnityEngine.Debug.Log($"[Shardroot] moved to ({at.x:F0},{at.z:F0})");
                if ((h.RevealedTo & (1u << (int)GameSettings.LocalPlayerFaction)) != 0)
                {
                    SimSignals.Alert(SimAlertKind.Shardroot);
                    SimSignals.Notify(Loc.T("Someone passed the SHARDROOT and it moved."));
                    SimSignals.Ping(at, SimPingKind.Discovery, 20f, big: true);
                }
            }
            em.SetComponentData(root, h);
        }

        private static readonly ComponentType[] QT_PassUnits =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        private static CachedEntityQuery QC_PassUnits;

        private static readonly ComponentType[] QT_HiddenRoot =
        {
            ComponentType.ReadOnly<ShardrootHidden>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_HiddenRoot;

        /// <summary>Logs who may see the hidden artifact, so a match's logs show
        /// whether its secret held (Monuments_And_Petriarchy.md §3).</summary>
        public static void LogHiddenShardroot(EntityManager em)
        {
            var q = QC_HiddenRoot.Get(em, QT_HiddenRoot);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var at = em.GetComponentData<LocalTransform>(ents[0]).Position;
            uint knows = em.GetComponentData<ShardrootHidden>(ents[0]).RevealedTo;
            string fog = "", known = "";
            for (int f = 0; f < GameSettings.TotalPlayers; f++)
            {
                if (TheWaningBorder.Systems.Visibility.FogOfWarSystem.IsRevealedToFaction((Faction)f, at)) fog += (Faction)f + " ";
                if ((knows & (1u << f)) != 0) known += (Faction)f + " ";
            }
            UnityEngine.Debug.Log($"[Shardroot] hidden at ({at.x:F0},{at.z:F0}); told: {(known == "" ? "nobody" : known)}; ground explored by: {(fog == "" ? "nobody" : fog)}");
        }

        /// <summary>The right monument's exploration tells whoever explored it.</summary>
        private static void RevealShardroot(EntityManager em, Faction faction)
        {
            if (!ShardrootPickup.TryReveal(em, faction, out float3 at)) return;
            UnityEngine.Debug.Log($"[Shardroot] {faction} learned the spot ({at.x:F0},{at.z:F0})");
            if (faction != GameSettings.LocalPlayerFaction) return;
            SimSignals.Alert(SimAlertKind.Shardroot);
            SimSignals.Announce(Loc.T("The ruins show you where the SHARDROOT lies."));
            SimSignals.Ping(at, SimPingKind.Discovery, 20f, big: true);
        }
    }
}
