// RunaiTradeSystem.cs
// The Runai haul-and-tax economy (docs/Design/Age_1_Runai.md §3).
//
//   * A Runai territory's income is NOT credited to the bank. TerritoryIncomeSystem
//     hands it to Deposit(), which adds it to the stock of the Runai depots in
//     that territory (Trading Post = supplies, Mine = iron). A territory with
//     no depot pays nothing.
//   * Every depot keeps caravans (1, up to 5 with the Crossing research). A
//     caravan loads stock, travels the road graph to the best-paying market
//     (Fortress, Thessara's Crossing, Waystations after Market Rights), sells
//     the cargo x (1 + distance premium) x the market's multiplier, returns.
//   * A caravan passing one of its Waystations pays veilstone tax — once per
//     taxCooldownSeconds of travel, so Waystations must stand far apart.
//   * Every caravan travels with its escort squad (Escort I-III research).
//   * Trade paths: Swift Wheels, Wagon Archers (retaliating fire), Strong
//     Axles / Hardened Escorts (data techs), caravans per depot, Hunker Down.
//
// Lockstep: every decision reads simulation state only and walks entities in
// Entity.Index order (indices are allocated identically on every peer — the
// same assumption LandmarkAgeUp makes). The sale is paid through
// FactionEconomy.Add on every peer.

using System.Collections.Generic;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Regions;
using Unity.Collections;
using TheWaningBorder.Abilities;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Runai
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RunaiTradeSystem : SystemBase
    {
        private static RunaiTradeSystemConfig _cfg;
        public static RunaiTradeSystemConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RunaiTradeSystemConfig>());

        private SimCadence.Periodic _tick;
        private SimCadence.Periodic _tagTick;
        private SimCadence.Periodic _logTick;

        // ── queries ────────────────────────────────────────────────────
        static readonly ComponentType[] QT_Depots =
        {
            ComponentType.ReadWrite<RunaiDepot>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_Depots;

        static readonly ComponentType[] QT_DepotCandidates =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<RunaiDepot>(), ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_DepotCandidates;

        static readonly ComponentType[] QT_Caravans =
        {
            ComponentType.ReadWrite<RunaiCaravan>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Caravans;

        static readonly ComponentType[] QT_Escorts =
        {
            ComponentType.ReadWrite<RunaiEscort>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Escorts;

        // Who may cast The Call (§8.1): Workers, Litharchs and heroes.
        static readonly ComponentType[] QT_Callers =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Callers;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_Buildings;

        static readonly ComponentType[] QT_Masters =
        {
            ComponentType.ReadOnly<CaravanMasterTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Masters;

        // ── per-match state ────────────────────────────────────────────
        private static readonly float[,] _payCarry = new float[Alliances.MaxFactions, 4];
        private static int _epoch = -1;

        private static void ReSync()
        {
            if (_epoch == SimCadence.Epoch) return;
            _epoch = SimCadence.Epoch;
            System.Array.Clear(_payCarry, 0, _payCarry.Length);
        }

        // ── per-tick snapshots (2026-10-15, perf) ──────────────────────
        // Derived state, re-read from the simulation before every use, so
        // the decisions are the same as reading live: nothing this system
        // does changes a culture, a research or the depot set mid-pass.

        /// <summary>Each faction's completed culture, read once per update
        /// instead of two Hall-array copies per hut / unit / depot.</summary>
        private static readonly byte[] _cultureOf = new byte[32];

        private static void SnapshotCultures(EntityManager em) => CultureConfig.GetCompletedCultures(em, _cultureOf);

        private static bool IsRunaiNow(Faction f)
        {
            int i = (int)f;
            return i >= 0 && i < _cultureOf.Length && _cultureOf[i] == Cultures.Runai;
        }

        /// <summary>Per faction, this update: the research-derived numbers a
        /// depot and a caravan ask for, instead of hashing tech ids per entity.</summary>
        private struct TradeFlags
        {
            public int Cap, Guards, Archers, Riders;
            public bool SwiftWheels, SwiftWheels2, WagonArchers, HunkerDown;
        }
        private static readonly TradeFlags[] _flags = new TradeFlags[Alliances.MaxFactions];

        private static TradeFlags ReadFlags(Faction f)
        {
            var t = new TradeFlags { Cap = CaravanCap(f) };
            EscortSquad(f, out t.Guards, out t.Archers, out t.Riders);
            t.SwiftWheels = Researched(f, "Runai_SwiftWheels");
            t.SwiftWheels2 = Researched(f, "Runai_SwiftWheels2");
            t.WagonArchers = Researched(f, "Runai_WagonArchers");
            t.HunkerDown = Researched(f, "Runai_HunkerDown");
            return t;
        }

        private static void SnapshotFlags()
        {
            for (int fi = 0; fi < _flags.Length; fi++)
                _flags[fi] = IsRunaiNow((Faction)fi) ? ReadFlags((Faction)fi) : default;
        }

        /// <summary>The snapshot for a Runai faction (every caller has
        /// already tested IsRunaiNow, or owns a caravan only a Runai faction
        /// has); a live read for anything else.</summary>
        private static TradeFlags FlagsOf(Faction f)
        {
            int i = (int)f;
            return i >= 0 && i < _flags.Length && IsRunaiNow(f) ? _flags[i] : ReadFlags(f);
        }

        /// <summary>Every finished depot (kind != 0), its owner and territory,
        /// in Entity.Index order — read once per simulation step for all of
        /// that step's Deposit calls (one per Runai territory), each of which
        /// re-copied the whole depot query and re-resolved every territory.</summary>
        private struct DepotRow { public Entity E; public Faction F; public int Territory; public int Kind; }
        private static readonly List<DepotRow> _depotRows = new List<DepotRow>();
        private static long _depotRowsStep = -1;
        private static int _depotRowsEpoch = -1;

        private static void SnapshotDepots(EntityManager em)
        {
            if (_depotRowsStep == SimClock.Steps && _depotRowsEpoch == SimCadence.Epoch) return;
            _depotRowsStep = SimClock.Steps;
            _depotRowsEpoch = SimCadence.Epoch;
            _depotRows.Clear();
            var q = QC_Depots.Get(em, QT_Depots);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var lts = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                int kind = DepotKind(em, ents[i]);
                if (kind == 0) continue;
                var p = lts[i].Position;
                _depotRows.Add(new DepotRow { E = ents[i], F = facs[i].Value, Territory = RegionMap.RegionAt(p.x, p.z), Kind = kind });
            }
            _depotRows.Sort(DepotRowByIndex.Instance);
        }

        private sealed class DepotRowByIndex : IComparer<DepotRow>
        {
            public static readonly DepotRowByIndex Instance = new DepotRowByIndex();
            public int Compare(DepotRow a, DepotRow b) => a.E.Index.CompareTo(b.E.Index);
        }

        /// <summary>Entity.Index order. Indices are unique among live
        /// entities, so the unstable sort gives the one order every peer has.</summary>
        private struct ByIndex : IComparer<Entity>
        {
            public int Compare(Entity a, Entity b) => a.Index.CompareTo(b.Index);
        }

        // ═════════════════════════════ public rules ═════════════════════════

        /// <summary>True when the faction has COMPLETED its age-up into Runai.</summary>
        public static bool IsRunai(EntityManager em, Faction f)
            => CultureConfig.GetCompletedCulture(em, f) == Cultures.Runai;

        public static bool Researched(Faction f, string tech)
        {
            var r = FactionResearchState.Instance;
            return r != null && r.HasResearched(f, tech);
        }

        // The tech ids, not "Runai_Caravan" + k: that concat allocated a
        // string per level per depot per tick.
        private static readonly string[] CaravanTechs =
            { null, null, "Runai_Caravan2", "Runai_Caravan3", "Runai_Caravan4", "Runai_Caravan5" };

        /// <summary>Caravans each depot of this faction keeps.</summary>
        public static int CaravanCap(Faction f)
        {
            int n = 1;
            for (int k = 2; k <= 5; k++)
                if (Researched(f, CaravanTechs[k])) n = k;
            return math.min(n, math.max(1, Cfg.maxCaravansPerDepot));
        }

        /// <summary>The escort squad one caravan travels with, by Escort level (§3.4).</summary>
        public static void EscortSquad(Faction f, out int guards, out int archers, out int riders)
        {
            guards = archers = riders = 0;
            if (Researched(f, "Runai_Escort3")) { guards = 3; archers = 3; riders = 2; }
            else if (Researched(f, "Runai_Escort2")) { guards = 1; archers = 2; }
            else if (Researched(f, "Runai_Escort1")) { guards = 1; }
        }

        public static float Capacity(Faction f)
            => Cfg.caravanCapacity * (Researched(f, "Runai_PackAnimals") ? 1f + Cfg.packAnimalsCapacityPct / 100f : 1f);

        /// <summary>The depot kind a building is, by its tag: 0 none, 1 supplies, 2 iron.
        /// The Sanctuary is no longer a depot — it makes religion points (§5.1).</summary>
        public static int DepotKind(EntityManager em, Entity e)
        {
            if (em.HasComponent<GathererHutTag>(e)) return 1;
            if (em.HasComponent<MineTag>(e)) return 2;
            return 0;
        }

        /// <summary>A start territory hauls nothing (Age_1_Runai.md §3.1, 2026-10-14):
        /// no depot, no wagon — its income reaches the bank directly.</summary>
        public static bool IsHomeGround(int territory)
            => territory != RegionMap.None
               && TerritoryResources.TypeOf(territory) == TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType.Start;

        /// <summary>A Runai Waystation: the faction's Hut once it has aged into Runai.</summary>
        public static bool IsWaystation(EntityManager em, Entity e)
            => em.HasComponent<HutTag>(e) && !em.HasComponent<GathererHutTag>(e);

        /// <summary>Is this building a market of its faction? (§3.3)</summary>
        public static float MarketMultiplier(EntityManager em, Entity e, Faction f)
        {
            if (em.HasComponent<ThessarasCrossingTag>(e)) return Cfg.crossingMarketMultiplier;
            if (em.HasComponent<HallTag>(e) || em.HasComponent<FortressTag>(e)) return 1f;
            if (IsWaystation(em, e) && Researched(f, "Runai_MarketRights")) return Cfg.waystationMarketMultiplier;
            return 0f;
        }

        /// <summary>The distance premium over a depot-to-market road length.</summary>
        public static float Premium(Faction f, float distance)
        {
            bool lh = Researched(f, "Runai_LongHaulTariffs");
            float per = lh ? Cfg.longHaulPremiumPerMetre : Cfg.premiumPerMetre;
            float cap = lh ? Cfg.longHaulPremiumCap : Cfg.premiumCap;
            return math.clamp(distance * per, 0f, cap);
        }

        /// <summary>
        /// Territory income for a Runai faction (called by TerritoryIncomeSystem
        /// in place of the bank credit). Amounts are resource units for this
        /// tick. Returns false when the territory has no depot and the income
        /// is lost.
        /// </summary>
        public static bool Deposit(EntityManager em, int territory, Faction owner,
                                   float supplies, float iron, float veilstone, float veilsteel)
        {
            SnapshotDepots(em);
            _here.Clear();
            for (int i = 0; i < _depotRows.Count; i++)   // already in Entity.Index order
            {
                var row = _depotRows[i];
                if (row.F == owner && row.Territory == territory) _here.Add(row);
            }
            if (_here.Count == 0) return false;

            float cap = Capacity(owner) * math.max(1f, Cfg.stockCapLoads);
            Spread(em, _here, 1, cap, supplies, 0);
            Spread(em, _here, 2, cap, iron, 1);
            // No veilstone or veilsteel rides a caravan: the Runai get veilstone
            // from the Waystation tax and veilsteel from the Sanctuary trade
            // (§3.2, §5.1), so a territory's crystal income is not theirs.
            return true;
        }

        private static readonly List<DepotRow> _here = new List<DepotRow>();

        private static void Spread(EntityManager em, List<DepotRow> depots, int kind, float cap,
                                   float amount, int resource)
        {
            if (amount <= 0f) return;
            int matching = 0;
            for (int i = 0; i < depots.Count; i++) if (depots[i].Kind == kind) matching++;
            int n = matching > 0 ? matching : depots.Count;
            float share = amount / n;
            for (int i = 0; i < depots.Count; i++)
            {
                if (matching > 0 && depots[i].Kind != kind) continue;
                var e = depots[i].E;
                var d = em.GetComponentData<RunaiDepot>(e);
                Put(ref d, resource, math.min(share, math.max(0f, cap - d.Total)));
                em.SetComponentData(e, d);
            }
        }

        private static void Put(ref RunaiDepot d, int resource, float v)
        {
            switch (resource)
            {
                case 0: d.Supplies += v; break;
                case 1: d.Iron += v; break;
                case 2: d.Veilstone += v; break;
                default: d.Veilsteel += v; break;
            }
        }

        // ── footprint geometry (§3.1: arrive NEAR, never inside) ───────

        /// <summary>Half the longer side of a building's footprint, metres.</summary>
        public static float HalfExtent(EntityManager em, Entity building)
        {
            if (em.HasComponent<BuildingSize>(building))
            {
                var s = em.GetComponentData<BuildingSize>(building);
                return math.max(s.Width, s.Height) * BuildGrid.CellSize * 0.5f;
            }
            return em.HasComponent<Radius>(building) ? em.GetComponentData<Radius>(building).Value : 2f;
        }

        /// <summary>Distance from <paramref name="p"/> to the building's footprint square (0 inside).</summary>
        public static float DistanceToFootprint(EntityManager em, Entity building, float3 p)
        {
            var c = em.GetComponentData<LocalTransform>(building).Position;
            float h = HalfExtent(em, building);
            float dx = math.max(0f, math.abs(p.x - c.x) - h);
            float dz = math.max(0f, math.abs(p.z - c.z) - h);
            return math.sqrt(dx * dx + dz * dz);
        }

        /// <summary>The point just outside the footprint nearest <paramref name="from"/> —
        /// where a caravan parks to load or sell.</summary>
        public static float3 ApproachPoint(EntityManager em, Entity building, float3 from)
        {
            var c = em.GetComponentData<LocalTransform>(building).Position;
            float h = HalfExtent(em, building) + math.max(0.5f, Cfg.arriveSlack * 0.5f);
            float3 d = from - c; d.y = 0f;
            float m = math.max(math.abs(d.x), math.abs(d.z));
            if (m < 0.01f) d = new float3(1f, 0f, 0f); else d /= m;    // onto the unit square
            return new float3(c.x + d.x * h, c.y, c.z + d.z * h);
        }

        private static bool Arrived(EntityManager em, float3 pos, Entity building)
            => DistanceToFootprint(em, building, pos) <= Cfg.arriveSlack;

        // Was an insertion sort — O(n²) over every caravan and escort each
        // tick. Same order: the keys are unique.
        private static void SortByIndex(NativeList<Entity> list) => list.Sort(new ByIndex());

        private static NativeList<Entity> SortedEntities(EntityManager em, EntityQuery q)
        {
            using var arr = q.ToEntityArray(Allocator.Temp);
            var list = new NativeList<Entity>(arr.Length, Allocator.Temp);
            list.AddRange(arr);
            SortByIndex(list);
            return list;
        }

        // ═════════════════════════════ the tick ═════════════════════════════

        private struct Market { public Entity E; public Faction F; public float3 P; public float Mult; }
        private struct Waystation { public Faction F; public float2 P; }

        private readonly List<Market> _markets = new();
        private readonly List<Waystation> _waystations = new();
        private readonly List<(Faction f, float2 p)> _masters = new();

        protected override void OnUpdate()
        {
            ReSync();
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            SnapshotCultures(em);

            if (_tagTick.Due(dt, 1f)) { TagDepots(em); GrantTheCall(em); }
            if (_logTick.Due(dt, 60f)) LogTrade(em);

            float step = _tick.DueStep(dt, math.max(0.05f, Cfg.tickSeconds));
            if (step <= 0f) return;

            var dq = QC_Depots.Get(em, QT_Depots);
            if (dq.IsEmptyIgnoreFilter && QC_Caravans.Get(em, QT_Caravans).IsEmptyIgnoreFilter) return;

            CollectSites(em);
            SnapshotFlags();

            var depotList = SortedEntities(em, dq);
            var caravans = SortedEntities(em, QC_Caravans.Get(em, QT_Caravans));
            var escorts = SortedEntities(em, QC_Escorts.Get(em, QT_Escorts));

            SpawnMissing(em, depotList, caravans, escorts, step);
            depotList.Dispose(); caravans.Dispose(); escorts.Dispose();

            // Re-read after the spawns so the new ones act this tick.
            caravans = SortedEntities(em, QC_Caravans.Get(em, QT_Caravans));
            escorts = SortedEntities(em, QC_Escorts.Get(em, QT_Escorts));
            for (int i = 0; i < caravans.Length; i++) TickCaravan(em, caravans[i], step);
            TickEscorts(em, escorts, caravans, step);
            caravans.Dispose(); escorts.Dispose();
        }

        /// <summary>Once a minute, per Runai faction: depot stock by kind and
        /// what its caravans are doing. Observation only — headless batches read it.</summary>
        private static void LogTrade(EntityManager em)
        {
            var dq = QC_Depots.Get(em, QT_Depots);
            if (dq.IsEmptyIgnoreFilter) return;
            var depots = SortedEntities(em, dq);
            var cars = SortedEntities(em, QC_Caravans.Get(em, QT_Caravans));
            for (int fi = 0; fi < 8; fi++)
            {
                var f = (Faction)fi;
                if (!IsRunaiNow(f)) continue;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < depots.Length; i++)
                {
                    var e = depots[i];
                    if (em.GetComponentData<FactionTag>(e).Value != f) continue;
                    var d = em.GetComponentData<RunaiDepot>(e);
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    sb.Append($" [{(DepotKind(em, e) == 2 ? "mine" : "post")}@{p.x:F0},{p.z:F0} s{d.Supplies:F0} i{d.Iron:F0}]");
                }
                int load = 0, go = 0, back = 0; float cargo = 0f;
                for (int i = 0; i < cars.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(cars[i]).Value != f) continue;
                    var c = em.GetComponentData<RunaiCaravan>(cars[i]);
                    if (c.Leg == CaravanLeg.Loading) load++; else if (c.Leg == CaravanLeg.ToMarket) go++; else back++;
                    cargo += c.Cargo;
                    if (load + go + back <= 3)
                    {
                        var cp = em.GetComponentData<LocalTransform>(cars[i]).Position;
                        var dd = em.GetComponentData<DesiredDestination>(cars[i]);
                        float3 mp = c.Market != Entity.Null && em.Exists(c.Market)
                            ? em.GetComponentData<LocalTransform>(c.Market).Position : default;
                        sb.Append($" <car {c.Leg} @{cp.x:F0},{cp.z:F0} dd{dd.Has}:{dd.Position.x:F0},{dd.Position.z:F0}"
                                  + $" mkt {mp.x:F0},{mp.z:F0} path {c.PathId & 0xffff}/{(c.PathIndex >> 1) & 0x7fff}"
                                  + $" spd {em.GetComponentData<MoveSpeed>(cars[i]).Value:F1}>");
                    }
                }
                UnityEngine.Debug.Log($"[RunaiTrade] {f}: caravans load {load} market {go} back {back} cargo {cargo:F0} |{sb}");
            }
            depots.Dispose(); cars.Dispose();
        }

        /// <summary>The Call (§8.1) on every Worker, Litharch and hero of an
        /// aged-up Runai faction. Granted, not authored on the Age 0 SOs,
        /// because those units are every culture's.</summary>
        private static void GrantTheCall(EntityManager em)
        {
            int idx = AbilityCatalog.IndexOf("The Call");
            if (idx < 0) return;
            var q = QC_Callers.Get(em, QT_Callers);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var add = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!IsRunaiNow(facs[i].Value)) continue;   // the cheapest test first
                if (!em.HasComponent<WorkerTag>(e) && !em.HasComponent<LitharchTag>(e) && !em.HasComponent<HeroLevel>(e)) continue;
                if (em.HasComponent<UnitAbilities>(e))
                {
                    var ua = em.GetComponentData<UnitAbilities>(e);
                    if (ua.S0 == idx || ua.S1 == idx || ua.S2 == idx || ua.S3 == idx) continue;
                }
                add.Add(e);
            }
            SortByIndex(add);
            for (int i = 0; i < add.Length; i++) AbilityAssignment.AddAbility(em, add[i], idx);
            add.Dispose();
        }

        /// <summary>Give every finished extractor of an aged-up Runai faction its depot —
        /// the home territory's Age 0 huts and mines included (§3.1).</summary>
        private static void TagDepots(EntityManager em)
        {
            var q = QC_DepotCandidates.Get(em, QT_DepotCandidates);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var add = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (!IsRunaiNow(facs[i].Value)) continue;
                if (DepotKind(em, ents[i]) == 0) continue;
                var bp = em.GetComponentData<LocalTransform>(ents[i]).Position;
                if (IsHomeGround(RegionMap.RegionAt(bp.x, bp.z))) continue;   // no wagons at home
                add.Add(ents[i]);
            }
            for (int i = 0; i < add.Length; i++) em.AddComponentData(add[i], new RunaiDepot());
            add.Dispose();
        }

        private void CollectSites(EntityManager em)
        {
            _markets.Clear(); _waystations.Clear(); _masters.Clear();
            var ents = SortedEntities(em, QC_Buildings.Get(em, QT_Buildings));
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var f = em.GetComponentData<FactionTag>(e).Value;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                if (IsRunaiNow(f) && IsWaystation(em, e))
                    _waystations.Add(new Waystation { F = f, P = p.xz });
                float mult = MarketMultiplier(em, e, f);
                if (mult > 0f) _markets.Add(new Market { E = e, F = f, P = p, Mult = mult });
            }
            ents.Dispose();

            var ms = SortedEntities(em, QC_Masters.Get(em, QT_Masters));
            for (int i = 0; i < ms.Length; i++)
                if (em.GetComponentData<Health>(ms[i]).Value > 0)
                    _masters.Add((em.GetComponentData<FactionTag>(ms[i]).Value,
                                  em.GetComponentData<LocalTransform>(ms[i]).Position.xz));
            ms.Dispose();
        }

        // ── spawning ───────────────────────────────────────────────────

        private void SpawnMissing(EntityManager em, NativeList<Entity> depots, NativeList<Entity> caravans,
                                  NativeList<Entity> escorts, float step)
        {
            var caravanCount = new NativeHashMap<Entity, int>(depots.Length, Allocator.Temp);
            for (int i = 0; i < caravans.Length; i++)
            {
                if (em.GetComponentData<Health>(caravans[i]).Value <= 0) continue;
                var d = em.GetComponentData<RunaiCaravan>(caravans[i]).Depot;
                caravanCount.TryGetValue(d, out int n); caravanCount[d] = n + 1;
            }
            // escorts per depot per kind
            var escortCount = new NativeHashMap<long, int>(depots.Length * 3, Allocator.Temp);
            for (int i = 0; i < escorts.Length; i++)
            {
                if (em.GetComponentData<Health>(escorts[i]).Value <= 0) continue;
                var es = em.GetComponentData<RunaiEscort>(escorts[i]);
                long k = Key(es.Depot, es.Kind);
                escortCount.TryGetValue(k, out int n); escortCount[k] = n + 1;
            }

            var spawnCaravan = new NativeList<Entity>(Allocator.Temp);
            var spawnEscort = new NativeList<(Entity depot, EscortKind kind)>(Allocator.Temp);
            for (int i = 0; i < depots.Length; i++)
            {
                var e = depots[i];
                if (DepotKind(em, e) == 0) continue;
                var f = em.GetComponentData<FactionTag>(e).Value;
                if (!IsRunaiNow(f)) continue;
                var fl = FlagsOf(f);
                int cap = fl.Cap;
                var d = em.GetComponentData<RunaiDepot>(e);
                caravanCount.TryGetValue(e, out int haveC);
                if (haveC < cap)
                {
                    d.CaravanTimer -= step;
                    if (d.CaravanTimer <= 0f) { spawnCaravan.Add(e); d.CaravanTimer = Cfg.caravanRespawnSeconds; }
                }
                else d.CaravanTimer = 0f;

                int g = fl.Guards, a = fl.Archers, r = fl.Riders;
                bool owed = false;
                for (int kind = 0; kind < 3 && !owed; kind++)
                {
                    int want = cap * (kind == 0 ? g : kind == 1 ? a : r);
                    escortCount.TryGetValue(Key(e, (EscortKind)kind), out int have);
                    if (have < want)
                    {
                        owed = true;
                        d.WarriorTimer -= step;
                        if (d.WarriorTimer <= 0f)
                        {
                            spawnEscort.Add((e, (EscortKind)kind));
                            d.WarriorTimer = Cfg.warriorRespawnSeconds;
                        }
                    }
                }
                if (!owed) d.WarriorTimer = 0f;
                em.SetComponentData(e, d);
            }
            for (int i = 0; i < spawnCaravan.Length; i++) SpawnCaravan(em, spawnCaravan[i]);
            for (int i = 0; i < spawnEscort.Length; i++) SpawnEscort(em, spawnEscort[i].depot, spawnEscort[i].kind);
            spawnCaravan.Dispose(); spawnEscort.Dispose();
            caravanCount.Dispose(); escortCount.Dispose();
        }

        // Engine-side: how long a wagon may make no headway before it leaves the road.
        private const float StallSeconds = 20f;
        // Engine-side: how far an escort's slot beside its wagon may drift
        // from the goal it is walking to before it is handed a new one.
        private const float EscortRetargetMetres = 4f;

        private static long Key(Entity depot, EscortKind kind) => ((long)depot.Index << 3) | (long)kind;

        private static float3 SpawnPoint(EntityManager em, Entity depot, int ordinal)
        {
            var c = em.GetComponentData<LocalTransform>(depot).Position;
            float a = ordinal * 2.39996f;   // golden angle: spread, deterministic
            var dir = new float3(math.cos(a), 0f, math.sin(a));
            return ApproachPoint(em, depot, c + dir * 10f);
        }

        private static void SpawnCaravan(EntityManager em, Entity depot)
        {
            var f = em.GetComponentData<FactionTag>(depot).Value;
            var e = UnitFactory.Create(em, RunaiCaravanUnit.Id, SpawnPoint(em, depot, depot.Index), f);
            var c = em.GetComponentData<RunaiCaravan>(e);
            c.Depot = depot;
            c.BaseSpeed = em.GetComponentData<MoveSpeed>(e).Value;
            em.SetComponentData(e, c);
        }

        private static void SpawnEscort(EntityManager em, Entity depot, EscortKind kind)
        {
            var f = em.GetComponentData<FactionTag>(depot).Value;
            string id = kind == EscortKind.Guard ? RunaiEscortUnits.GuardId
                      : kind == EscortKind.Archer ? RunaiEscortUnits.ArcherId : RunaiEscortUnits.RiderId;
            var e = UnitFactory.Create(em, id, SpawnPoint(em, depot, depot.Index + 1 + (int)kind), f);
            var es = em.GetComponentData<RunaiEscort>(e);
            es.Depot = depot; es.Kind = kind; es.Caravan = Entity.Null;
            em.SetComponentData(e, es);
        }

        // ── caravans ───────────────────────────────────────────────────

        private void TickCaravan(EntityManager em, Entity e, float step)
        {
            var hp = em.GetComponentData<Health>(e);
            if (hp.Value <= 0) return;
            var c = em.GetComponentData<RunaiCaravan>(e);
            var f = em.GetComponentData<FactionTag>(e).Value;
            var pos = em.GetComponentData<LocalTransform>(e).Position;
            bool depotAlive = c.Depot != Entity.Null && em.Exists(c.Depot) && em.HasComponent<RunaiDepot>(c.Depot)
                              && em.GetComponentData<Health>(c.Depot).Value > 0;

            if (c.TaxCooldown > 0f) c.TaxCooldown = math.max(0f, c.TaxCooldown - step);
            if (c.Armed > 0f) c.Armed = math.max(0f, c.Armed - step);

            // ── Hunker Down (§7.2): stop at half health, armoured for a while.
            float frac = hp.Max > 0 ? (float)hp.Value / hp.Max : 1f;
            if (frac >= Cfg.hunkerHpFraction) c.HunkerSpent = 0;
            else if (c.HunkerSpent == 0 && FlagsOf(f).HunkerDown)
            {
                c.HunkerSpent = 1;
                c.Hunker = Cfg.hunkerSeconds;
                var b = TransientState.Active<SpellBuff>(em, e) ? em.GetComponentData<SpellBuff>(e) : default;
                b.ArmorPct = math.max(b.ArmorPct, Cfg.hunkerArmorPct);
                b.TimeRemaining = math.max(b.TimeRemaining, Cfg.hunkerSeconds);
                TransientState.Set(em, e, b);
            }
            if (c.Hunker > 0f)
            {
                c.Hunker = math.max(0f, c.Hunker - step);
                em.SetComponentData(e, new DesiredDestination { Position = pos, Has = 0 });
                em.SetComponentData(e, new MoveSpeed { Value = 0f });
                ApplyWeapons(em, e, f, c);
                em.SetComponentData(e, c);
                return;
            }

            if (!depotAlive && c.Leg != CaravanLeg.ToMarket)
            {
                // Its depot is gone: sell what it carries, else it has no reason to exist.
                if (c.Cargo > 0f && PickMarket(em, f, pos, c.BaseSpeed, out var m))
                { c.Market = m.E; c.Leg = CaravanLeg.ToMarket; GoTo(em, e, ref c, m.E, pos); }
                else { hp.Value = 0; em.SetComponentData(e, hp); return; }
            }

            switch (c.Leg)
            {
                case CaravanLeg.Loading:
                {
                    if (!Arrived(em, pos, c.Depot)) { GoTo(em, e, ref c, c.Depot, pos); break; }
                    c.Waited += step;
                    var d = em.GetComponentData<RunaiDepot>(c.Depot);
                    float room = CapacityAt(f, pos) - c.Cargo;
                    if (room > 0f && d.Total > 0f)
                    {
                        float take = math.min(room, d.Total);
                        float k = take / d.Total;
                        c.Supplies += d.Supplies * k; c.Iron += d.Iron * k;
                        c.Veilstone += d.Veilstone * k; c.Veilsteel += d.Veilsteel * k;
                        d.Supplies -= d.Supplies * k; d.Iron -= d.Iron * k;
                        d.Veilstone -= d.Veilstone * k; d.Veilsteel -= d.Veilsteel * k;
                        em.SetComponentData(c.Depot, d);
                    }
                    bool full = c.Cargo >= CapacityAt(f, pos) - 0.01f;
                    var dp = em.GetComponentData<LocalTransform>(c.Depot).Position;
                    if ((full || (c.Waited >= Cfg.loadSeconds && c.Cargo > 0f))
                        && PickMarket(em, f, dp, c.BaseSpeed, out var mk))
                    {
                        c.Market = mk.E; c.Leg = CaravanLeg.ToMarket; c.Waited = 0f; c.PathId = 0;
                        GoTo(em, e, ref c, mk.E, pos);
                    }
                    break;
                }
                case CaravanLeg.ToMarket:
                {
                    if (c.Market == Entity.Null || !em.Exists(c.Market) || em.GetComponentData<Health>(c.Market).Value <= 0)
                    {
                        c.PathId = 0;
                        if (PickMarket(em, f, pos, c.BaseSpeed, out var m2)) { c.Market = m2.E; GoTo(em, e, ref c, m2.E, pos); }
                        else if (depotAlive) { c.Leg = CaravanLeg.Returning; GoTo(em, e, ref c, c.Depot, pos); }
                        break;
                    }
                    if (!Arrived(em, pos, c.Market)) { GoTo(em, e, ref c, c.Market, pos); break; }
                    float3 from = depotAlive ? em.GetComponentData<LocalTransform>(c.Depot).Position : pos;
                    Sell(em, f, ref c, from, c.Market);
                    c.PathId = 0;
                    if (depotAlive) { c.Leg = CaravanLeg.Returning; GoTo(em, e, ref c, c.Depot, pos); }
                    else { em.SetComponentData(e, c); hp.Value = 0; em.SetComponentData(e, hp); return; }
                    break;
                }
                case CaravanLeg.Returning:
                {
                    if (Arrived(em, pos, c.Depot)) { c.Leg = CaravanLeg.Loading; c.Waited = 0f; c.PathId = 0; }
                    else GoTo(em, e, ref c, c.Depot, pos);
                    break;
                }
            }

            // ── The road tax (§3.2).
            if (c.TaxCooldown <= 0f && NearestWaystation(f, pos.xz, out float2 wsAt))
            {
                c.TaxCooldown = Cfg.taxCooldownSeconds;
                int fi = (int)f;
                if (fi >= 0 && fi < Alliances.MaxFactions)
                {
                    int v = Draw(fi, 2, Cfg.taxVeilstone);
                    if (v > 0)
                    {
                        FactionEconomy.Add(em, f, Cost.Of(veilstone: v), IncomeSource.Trade);
                        // The "+N" rises over the Waystation the wagon passed.
                        SimSignals.Gain(new float3(wsAt.x, pos.y, wsAt.y), f, Cost.Of(veilstone: v));
                    }
                }
            }

            ApplyWeapons(em, e, f, c);

            // Speed: Swift Wheels I-II, a Waystation, the Caravan Master.
            float mult = 1f;
            var fl = FlagsOf(f);
            if (fl.SwiftWheels) mult *= 1f + Cfg.swiftWheelsSpeedPct / 100f;
            if (fl.SwiftWheels2) mult *= 1f + Cfg.swiftWheels2SpeedPct / 100f;
            if (NearWaystation(f, pos.xz)) mult *= 1f + Cfg.waystationSpeedPct / 100f;
            if (NearMaster(f, pos.xz)) mult *= 1f + Cfg.caravanMasterSpeedPct / 100f;
            em.SetComponentData(e, new MoveSpeed { Value = c.BaseSpeed * mult });

            em.SetComponentData(e, c);
        }

        /// <summary>Caravan of Swords arms it; Wagon Archers lets it shoot back (§7.2).</summary>
        private static void ApplyWeapons(EntityManager em, Entity e, Faction f, in RunaiCaravan c)
        {
            if (!em.HasComponent<Damage>(e)) return;
            int want = c.Armed > 0f ? (int)Cfg.caravanOfSwordsDamage
                     : FlagsOf(f).WagonArchers ? (int)Cfg.wagonArcherDamage : 0;
            var dmg = em.GetComponentData<Damage>(e);
            if (dmg.Value != want) { dmg.Value = want; em.SetComponentData(e, dmg); }
        }

        private float CapacityAt(Faction f, float3 pos)
            => Capacity(f) * (NearMaster(f, pos.xz) ? 1f + Cfg.caravanMasterCapacityPct / 100f : 1f);

        /// <summary>The faction's Waystation within reach of <paramref name="p"/>, nearest first.</summary>
        private bool NearestWaystation(Faction f, float2 p, out float2 at)
        {
            at = default;
            float best = Cfg.waystationRadius * Cfg.waystationRadius;
            bool found = false;
            for (int i = 0; i < _waystations.Count; i++)
            {
                if (_waystations[i].F != f) continue;
                float d = math.distancesq(_waystations[i].P, p);
                if (d <= best) { best = d; at = _waystations[i].P; found = true; }
            }
            return found;
        }

        public bool NearWaystation(Faction f, float2 p)
        {
            float r2 = Cfg.waystationRadius * Cfg.waystationRadius;
            for (int i = 0; i < _waystations.Count; i++)
                if (_waystations[i].F == f && math.distancesq(_waystations[i].P, p) <= r2) return true;
            return false;
        }

        private bool NearMaster(Faction f, float2 p)
        {
            float r2 = Cfg.caravanMasterRadius * Cfg.caravanMasterRadius;
            for (int i = 0; i < _masters.Count; i++)
                if (_masters[i].f == f && math.distancesq(_masters[i].p, p) <= r2) return true;
            return false;
        }

        /// <summary>Send the caravan toward a building along the road graph, parking
        /// just outside its footprint.</summary>
        private static void GoTo(EntityManager em, Entity e, ref RunaiCaravan c, Entity building, float3 pos)
        {
            float3 goal = ApproachPoint(em, building, pos);
            var dd = em.GetComponentData<DesiredDestination>(e);

            // WATCHDOG: a wagon that has not got nearer its goal for
            // stallSeconds leaves the road for the rest of this leg and walks
            // there by the ordinary pathfinder — it always delivers.
            if (c.LegSeen != (byte)(c.Leg + 1)) { c.LegSeen = (byte)(c.Leg + 1); c.BestDist = float.MaxValue; c.Stall = 0f; c.OffRoad = 0; }
            float gd = math.distance(pos.xz, goal.xz);
            if (gd < c.BestDist - 1f) { c.BestDist = gd; c.Stall = 0f; }
            else c.Stall += Cfg.tickSeconds;
            if (c.Stall >= StallSeconds) c.OffRoad = 1;
            if (c.OffRoad != 0)
            {
                if (dd.Has == 0 || math.distancesq(dd.Position.xz, goal.xz) >= 1f)
                    em.SetComponentData(e, new DesiredDestination { Position = goal, Has = 1 });
                return;
            }

            // The mover dropped the order short of the waypoint (unreachable,
            // stuck): pass that waypoint rather than ask for it again forever.
            if (dd.Has == 0 && c.PathId != 0 && math.distancesq(dd.Position.xz, pos.xz) < 1f)
                TheWaningBorder.World.Roads.RoadGraphSystem.Skip(ref c.PathId, ref c.PathIndex);
            float3 to = TheWaningBorder.World.Roads.RoadGraphSystem.NextWaypoint(pos, goal, ref c.PathId, ref c.PathIndex);
            if (dd.Has != 0 && math.distancesq(dd.Position.xz, to.xz) < 1f) return;
            em.SetComponentData(e, new DesiredDestination { Position = to, Has = 1 });
        }

        /// <summary>The market paying the most per second of round trip from <paramref name="from"/>.</summary>
        private bool PickMarket(EntityManager em, Faction f, float3 from, float speed, out Market best)
        {
            best = default;
            float bestRate = -1f;
            float v = math.max(0.5f, speed);
            for (int i = 0; i < _markets.Count; i++)
            {
                var m = _markets[i];
                if (m.F != f || !em.Exists(m.E)) continue;
                float d = TheWaningBorder.World.Roads.RoadGraphSystem.RoadDistance(from, m.P);
                float rate = (1f + Premium(f, d)) * m.Mult / (2f * d / v + math.max(1f, Cfg.loadSeconds));
                if (rate > bestRate) { bestRate = rate; best = m; }
            }
            return bestRate >= 0f;
        }

        private void Sell(EntityManager em, Faction f, ref RunaiCaravan c, float3 from, Entity market)
        {
            var mp = em.GetComponentData<LocalTransform>(market).Position;
            float d = TheWaningBorder.World.Roads.RoadGraphSystem.RoadDistance(from, mp);
            float mult = (1f + Premium(f, d)) * MarketMultiplier(em, market, f);
            int fi = (int)f;
            if (fi < 0 || fi >= Alliances.MaxFactions) return;
            var pay = new Cost
            {
                Supplies  = Draw(fi, 0, c.Supplies * mult),
                Iron      = Draw(fi, 1, c.Iron * mult),
                Veilstone = Draw(fi, 2, c.Veilstone * mult),
                Veilsteel = Draw(fi, 3, c.Veilsteel * mult),
            };
            if (pay.Supplies > 0 || pay.Iron > 0 || pay.Veilstone > 0 || pay.Veilsteel > 0)
            {
                FactionEconomy.Add(em, f, pay, IncomeSource.Trade);
                SimSignals.Gain(mp, f, pay);   // over the market
            }
            c.Supplies = c.Iron = c.Veilstone = c.Veilsteel = 0f;
        }

        private static int Draw(int f, int r, float amount)
        {
            _payCarry[f, r] += amount;
            int whole = (int)math.floor(_payCarry[f, r]);
            _payCarry[f, r] -= whole;
            return whole;
        }

        // ── escorts ────────────────────────────────────────────────────

        private static void TickEscorts(EntityManager em, NativeList<Entity> escorts, NativeList<Entity> caravans, float step)
        {
            // How many of each kind already ride with each caravan.
            var riding = new NativeHashMap<long, int>(caravans.Length * 3 + 1, Allocator.Temp);
            for (int i = 0; i < escorts.Length; i++)
            {
                var es = em.GetComponentData<RunaiEscort>(escorts[i]);
                if (es.Caravan != Entity.Null && CaravanAlive(em, es.Caravan))
                {
                    long k = Key(es.Caravan, es.Kind);
                    riding.TryGetValue(k, out int n); riding[k] = n + 1;
                }
            }

            for (int i = 0; i < escorts.Length; i++)
            {
                var e = escorts[i];
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                var es = em.GetComponentData<RunaiEscort>(e);
                var f = em.GetComponentData<FactionTag>(e).Value;

                // The Call holds it: a controllable army until it runs out.
                if (es.Called > 0f)
                {
                    es.Called = math.max(0f, es.Called - step);
                    if (es.Called <= 0f) EndCall(em, e, ref es);
                    em.SetComponentData(e, es);
                    continue;
                }

                bool depotAlive = es.Depot != Entity.Null && em.Exists(es.Depot) && em.HasComponent<RunaiDepot>(es.Depot);
                if (!depotAlive) { hp.Value = 0; em.SetComponentData(e, hp); continue; }

                if (es.Caravan == Entity.Null || !CaravanAlive(em, es.Caravan))
                {
                    es.Caravan = Entity.Null;
                    EscortSquad(f, out int g, out int a, out int r);
                    int per = es.Kind == EscortKind.Guard ? g : es.Kind == EscortKind.Archer ? a : r;
                    for (int k = 0; k < caravans.Length && per > 0; k++)
                    {
                        var ce = caravans[k];
                        if (!CaravanAlive(em, ce) || em.GetComponentData<RunaiCaravan>(ce).Depot != es.Depot) continue;
                        long key = Key(ce, es.Kind);
                        riding.TryGetValue(key, out int have);
                        if (have >= per) continue;
                        es.Caravan = ce; riding[key] = have + 1; break;
                    }
                }

                var pos = em.GetComponentData<LocalTransform>(e).Position;
                float3 anchor = es.Caravan != Entity.Null
                    ? em.GetComponentData<LocalTransform>(es.Caravan).Position
                    : ApproachPoint(em, es.Depot, pos);
                float dist = math.distance(pos.xz, anchor.xz);
                if (es.Returning != 0 && dist <= Cfg.escortOffset * 2f) es.Returning = 0;
                bool fighting = es.Returning == 0 && em.HasComponent<Target>(e)
                                && em.GetComponentData<Target>(e).Value != Entity.Null;
                em.SetComponentData(e, es);
                if (fighting && dist <= Cfg.escortLeash) continue;
                if (fighting) em.SetComponentData(e, new Target { Value = Entity.Null });
                if (dist > Cfg.escortOffset * 1.5f)
                {
                    float ang = e.Index * 1.1f;
                    var to = new float3(anchor.x + math.cos(ang) * Cfg.escortOffset, anchor.y,
                                        anchor.z + math.sin(ang) * Cfg.escortOffset);
                    // HYSTERESIS (2026-10-15): a fresh goal only once the slot
                    // has drifted EscortRetargetMetres from the one being
                    // walked to. A new goal every tick behind a rolling wagon
                    // rebuilt the escort's flow-field goal every tick.
                    var dd = em.GetComponentData<DesiredDestination>(e);
                    if (dd.Has == 0 || math.distancesq(dd.Position.xz, to.xz) > EscortRetargetMetres * EscortRetargetMetres)
                        em.SetComponentData(e, new DesiredDestination { Position = to, Has = 1 });
                }
            }
            riding.Dispose();
        }

        private static bool CaravanAlive(EntityManager em, Entity c)
            => em.Exists(c) && em.HasComponent<RunaiCaravan>(c) && em.GetComponentData<Health>(c).Value > 0;

        private static void EndCall(EntityManager em, Entity e, ref RunaiEscort es)
        {
            es.Called = 0f;
            es.Returning = 1;
            if (!em.HasComponent<NotControllableTag>(e)) em.AddComponent<NotControllableTag>(e);
            if (em.HasComponent<Target>(e)) em.SetComponentData(e, new Target { Value = Entity.Null });
        }

        // ── ability hooks (RunaiAbilities) ─────────────────────────────

        /// <summary>Caravan of Swords: arm every caravan of the faction.</summary>
        public static void ArmCaravans(EntityManager em, Faction f, float seconds)
        {
            var q = QC_Caravans.Get(em, QT_Caravans);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != f) continue;
                var c = em.GetComponentData<RunaiCaravan>(ents[i]);
                c.Armed = math.max(c.Armed, seconds);
                em.SetComponentData(ents[i], c);
                var b = TransientState.Active<SpellBuff>(em, ents[i]) ? em.GetComponentData<SpellBuff>(ents[i]) : default;
                b.DamageTakenMultiplier = Cfg.caravanOfSwordsDamageTaken;
                b.TimeRemaining = math.max(b.TimeRemaining, seconds);
                TransientState.Set(em, ents[i], b);
            }
        }

        /// <summary>Scatter: every caravan within <paramref name="radius"/> runs to
        /// the nearest market now, with whatever it carries.</summary>
        public static void Scatter(EntityManager em, Faction f, float3 at, float radius)
        {
            var sys = em.World.GetExistingSystemManaged<RunaiTradeSystem>();
            if (sys == null) return;
            SnapshotCultures(em);   // called from an ability, outside OnUpdate
            sys.CollectSites(em);
            var ents = SortedEntities(em, QC_Caravans.Get(em, QT_Caravans));
            float r2 = radius * radius;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != f) continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                if (math.distancesq(p.xz, at.xz) > r2) continue;
                float bestD = float.MaxValue; Market best = default; bool found = false;
                foreach (var m in sys._markets)
                {
                    if (m.F != f) continue;
                    float d = math.distancesq(p.xz, m.P.xz);
                    if (d < bestD) { bestD = d; best = m; found = true; }
                }
                if (!found) continue;
                var c = em.GetComponentData<RunaiCaravan>(e);
                c.Market = best.E; c.Leg = CaravanLeg.ToMarket; c.Waited = 0f; c.PathId = 0;
                GoTo(em, e, ref c, best.E, p);
                em.SetComponentData(e, c);
            }
            ents.Dispose();
        }

        /// <summary>
        /// The Call (§8.1): every escort of the caster's faction in the caster's
        /// territory and the territories bordering it is teleported to the
        /// caster and becomes a controllable army for callSeconds. Their
        /// caravans travel unescorted meanwhile.
        /// </summary>
        public static int Call(EntityManager em, Entity caster)
        {
            if (!em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return 0;
            var f = em.GetComponentData<FactionTag>(caster).Value;
            var at = em.GetComponentData<LocalTransform>(caster).Position;
            int home = RegionMap.RegionAt(at.x, at.z);
            if (home == RegionMap.None) return 0;
            var ents = SortedEntities(em, QC_Escorts.Get(em, QT_Escorts));
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != f) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                var es = em.GetComponentData<RunaiEscort>(e);
                if (es.Called > 0f) continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t != home && (t == RegionMap.None || !RegionMap.AreAdjacent(home, t))) continue;

                float a = n * 2.39996f;
                float r = 3f + (n / 8) * 2f;
                var to = new float3(at.x + math.cos(a) * r, 0f, at.z + math.sin(a) * r);
                to.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(to.x, to.z);
                var xf = em.GetComponentData<LocalTransform>(e); xf.Position = to;
                em.SetComponentData(e, xf);
                em.SetComponentData(e, new DesiredDestination { Position = to, Has = 0 });
                if (em.HasComponent<Target>(e)) em.SetComponentData(e, new Target { Value = Entity.Null });
                if (em.HasComponent<NotControllableTag>(e)) em.RemoveComponent<NotControllableTag>(e);
                es.Called = Cfg.callSeconds; es.Returning = 0; es.Caravan = Entity.Null;
                em.SetComponentData(e, es);
                n++;
            }
            ents.Dispose();
            return n;
        }

        /// <summary>Living escorts of the faction not already called — what The Call would bring.</summary>
        public static int CallableEscorts(EntityManager em, Faction f)
        {
            var q = QC_Escorts.Get(em, QT_Escorts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == f && em.GetComponentData<RunaiEscort>(ents[i]).Called <= 0f
                    && em.GetComponentData<Health>(ents[i]).Value > 0) n++;
            return n;
        }

        /// <summary>Dismiss the Call early: every called escort of the faction walks back.</summary>
        public static void DismissCall(EntityManager em, Faction f)
        {
            var ents = SortedEntities(em, QC_Escorts.Get(em, QT_Escorts));
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != f) continue;
                var es = em.GetComponentData<RunaiEscort>(e);
                if (es.Called <= 0f) continue;
                EndCall(em, e, ref es);
                em.SetComponentData(e, es);
            }
            ents.Dispose();
        }
    }
}
