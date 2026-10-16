// RunaiSanctuarySystem.cs
// The Sanctuary and the curse ladder (docs/Design/Age_1_Runai.md §5).
//
// Once a second, on every peer:
//   * each finished Sanctuary pays its religion trickle — its ONLY output
//     (more on a Cursed outcrop). It never drains the outcrop. Veilsteel is
//     bought here with those points (Trade, §5.1);
//   * the Sanctuaries standing on Cursed outcrops are published to RunaiVeil
//     so the curse raises no garrison at those nodes (the node sleeps);
//   * each faction's ladder tier is recomputed from research and published to
//     RunaiVeil (read by targeting and the curse waves) and, at Voice of the
//     Veil, to Alliances as peace with the curse — lost while the faction
//     holds the Shardroot;
//   * Communion heals Runai units on curse-held ground;
//   * Veilbound turns the nearest curse unit near each Sanctuary into a
//     unit of the faction, every veilboundIntervalSeconds.

using System.Collections.Generic;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Border;
using TheWaningBorder.Systems.Economy;
using TheWaningBorder.World.Regions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Runai
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RunaiSanctuarySystem : SystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            // Runai ships only in edition C (docs/Design/Alpha_Build.md §4). Off
            // elsewhere: its configs are stripped from A/B builds, and running
            // it there threw every tick, in the menu and in every match.
            if (!GameEdition.Allows(Cultures.Runai)) { Enabled = false; return; }
        }

        private static RunaiSanctuarySystemConfig _cfg;
        public static RunaiSanctuarySystemConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<RunaiSanctuarySystemConfig>());

        private SimCadence.Periodic _tick;
        private float _veilboundClock;
        private int _epoch = -1;

        static readonly ComponentType[] QT_Sanctuaries =
        {
            ComponentType.ReadWrite<SanctuaryState>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_Sanctuaries;

        static readonly ComponentType[] QT_CurseUnits =
        {
            ComponentType.ReadOnly<BorderUnitTag>(), ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_CurseUnits;

        static readonly ComponentType[] QT_RunaiUnits =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadWrite<Health>(),
        };
        static CachedEntityQuery QC_RunaiUnits;

        static readonly ComponentType[] QT_Shardroot = { ComponentType.ReadOnly<ShardrootState>() };
        static CachedEntityQuery QC_Shardroot;

        private readonly List<float2> _sleeping = new();
        private static readonly HashSet<int> _sheltered = new();

        /// <summary>True when territory <paramref name="t"/> holds a finished
        /// Runai Sanctuary: no curse wave or conquest is sent at it (§5.1).</summary>
        public static bool Shelters(int t) => _sheltered.Contains(t);

        protected override void OnUpdate()
        {
            if (_epoch != SimCadence.Epoch) { _epoch = SimCadence.Epoch; _veilboundClock = 0f; _sheltered.Clear(); }
            float step = _tick.DueStep(SystemAPI.Time.DeltaTime, 1f);
            if (step <= 0f) return;
            var em = EntityManager;

            UpdateTiers(em);

            var sq = QC_Sanctuaries.Get(em, QT_Sanctuaries);
            using var sancs = sq.ToEntityArray(Allocator.Temp);
            var order = new NativeList<Entity>(sancs.Length, Allocator.Temp);
            for (int i = 0; i < sancs.Length; i++) order.Add(sancs[i]);
            Sort(order);

            _sleeping.Clear();
            _sheltered.Clear();
            for (int i = 0; i < order.Length; i++) Produce(em, order[i], step);
            RunaiVeilNodes.SetSleepingNodes(_sleeping);

            Communion(em, step);

            _veilboundClock += step;
            if (_veilboundClock >= math.max(1f, Cfg.veilboundIntervalSeconds))
            {
                _veilboundClock = 0f;
                for (int i = 0; i < order.Length; i++) Veilbind(em, order[i]);
            }
            order.Dispose();
        }

        // ── the ladder ─────────────────────────────────────────────────

        private static readonly byte[] _tiers = new byte[Alliances.MaxFactions];

        private static void UpdateTiers(EntityManager em)
        {
            var r = FactionResearchState.Instance;
            Faction holder = ShardrootHolder(em);
            for (int f = 0; f < 8; f++)
            {
                byte tier = 0;
                if (r != null)
                    for (int k = 0; k < RunaiVeilNodes.Ladder.Length; k++)
                    {
                        if (!r.HasResearched((Faction)f, RunaiVeilNodes.Ladder[k])) break;
                        tier = (byte)(k + 1);
                    }
                _tiers[f] = tier;
            }
            RunaiVeil.Publish(_tiers, Cfg.hushedScanMultiplier);
            // §5.2: the curse still hunts the Shardroot holder — no peace while holding it.
            for (int f = 0; f < 8; f++)
                Alliances.SetCursePeace((Faction)f, _tiers[f] >= RunaiVeil.VoiceOfTheVeil && holder != (Faction)f);
        }

        private static Faction ShardrootHolder(EntityManager em)
        {
            var q = QC_Shardroot.Get(em, QT_Shardroot);
            if (q.IsEmptyIgnoreFilter) return Faction.Border;
            using var arr = q.ToComponentDataArray<ShardrootState>(Allocator.Temp);
            return arr[0].Found != 0 ? arr[0].HolderFaction : Faction.Border;
        }

        // ── output ─────────────────────────────────────────────────────

        private void Produce(EntityManager em, Entity s, float step)
        {
            var st = em.GetComponentData<SanctuaryState>(s);
            var p = em.GetComponentData<LocalTransform>(s).Position;
            var f = em.GetComponentData<FactionTag>(s).Value;
            if (st.Outcrop == Entity.Null || !em.Exists(st.Outcrop))
            {
                VeilstoneNodeStateSystem.TryGetOutcropAt(em, p.x, p.z, Cfg.outcropSearchRadius, out st.Outcrop, out _);
            }
            if (st.Outcrop == Entity.Null) { em.SetComponentData(s, st); return; }

            // §5.1: the curse never targets a territory holding a Sanctuary.
            int terr = RegionMap.Ready ? RegionMap.NearestRegion(p.x, p.z) : RegionMap.None;
            if (terr != RegionMap.None) _sheltered.Add(terr);

            var kind = VeilstoneNodeStateSystem.KindOf(em, st.Outcrop);
            bool cursed = kind == VeilstoneNodeKind.Cursed;
            if (cursed) _sleeping.Add(p.xz);

            st.RpCarry += (cursed ? Cfg.cursedRpPerMinute : Cfg.rpPerMinute) * step / 60f;
            int whole = (int)math.floor(st.RpCarry);
            if (whole > 0)
            {
                st.RpCarry -= whole;
                FactionReligionPointsHelper.Refund(em, f, whole);
            }
            em.SetComponentData(s, st);
        }

        // ── Trade RP for veilsteel (§5.1) ───────────────────────────────

        /// <summary>True when <paramref name="sanctuary"/> is a finished Runai
        /// Sanctuary whose faction holds enough religion points for one trade.</summary>
        public static bool CanTrade(EntityManager em, Entity sanctuary)
        {
            if (!em.Exists(sanctuary) || !em.HasComponent<RunaiSanctuaryTag>(sanctuary)) return false;
            if (em.HasComponent<UnderConstruction>(sanctuary) || !em.HasComponent<FactionTag>(sanctuary)) return false;
            var f = em.GetComponentData<FactionTag>(sanctuary).Value;
            return FactionReligionPointsHelper.CanAfford(em, f, math.max(1, Cfg.rpPerTrade));
        }

        /// <summary>Spend rpPerTrade religion points for veilsteelPerTrade
        /// veilsteel. Runs on every peer from the lockstep order.</summary>
        public static void Trade(EntityManager em, Entity sanctuary)
        {
            if (!CanTrade(em, sanctuary)) return;
            var f = em.GetComponentData<FactionTag>(sanctuary).Value;
            if (!FactionReligionPointsHelper.TrySpend(em, f, math.max(1, Cfg.rpPerTrade))) return;
            FactionEconomy.Add(em, f, new Cost { Veilsteel = Cfg.veilsteelPerTrade }, IncomeSource.Trade);
        }

        // ── Communion ──────────────────────────────────────────────────

        private static void Communion(EntityManager em, float step)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            var q = QC_RunaiUnits.Get(em, QT_RunaiUnits);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var lts = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int heal = (int)math.round(Cfg.communionHealPerSecond * step);
            if (heal <= 0) return;
            for (int i = 0; i < ents.Length; i++)
            {
                if (RunaiVeil.TierOf(facs[i].Value) < RunaiVeil.Communion) continue;
                if (!em.HasComponent<RunaiUnitTag>(ents[i])) continue;
                var p = lts[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None || TerritoryOwnership.OwnerOf(t) != TerritoryOwnership.Curse) continue;
                var hp = em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0 || hp.Value >= hp.Max) continue;
                hp.Value = math.min(hp.Max, hp.Value + heal);
                em.SetComponentData(ents[i], hp);
            }
        }

        // ── Veilbound ──────────────────────────────────────────────────

        private static void Veilbind(EntityManager em, Entity sanctuary)
        {
            if (!em.Exists(sanctuary)) return;
            var f = em.GetComponentData<FactionTag>(sanctuary).Value;
            byte tier = RunaiVeil.TierOf(f);
            if (tier < RunaiVeil.Veilbound) return;
            float reach = tier >= RunaiVeil.VoiceOfTheVeil ? Cfg.voiceRadius : Cfg.veilboundRadius;
            var at = em.GetComponentData<LocalTransform>(sanctuary).Position;

            var q = QC_CurseUnits.Get(em, QT_CurseUnits);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var lts = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = reach * reach;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                float d = math.distancesq(lts[i].Position.xz, at.xz);
                if (d > bestD || (d == bestD && best != Entity.Null && ents[i].Index > best.Index)) continue;
                bestD = d; best = ents[i];
            }
            if (best == Entity.Null) return;

            // The curse unit joins the faction: no longer the curse's (its AI,
            // its cap, its garrison roll), now commandable, costing no population.
            em.SetComponentData(best, new FactionTag { Value = f });
            if (em.HasComponent<BorderUnitTag>(best)) em.RemoveComponent<BorderUnitTag>(best);
            if (em.HasComponent<BorderTag>(best)) em.RemoveComponent<BorderTag>(best);
            if (em.HasComponent<CurseLivingMember>(best)) em.RemoveComponent<CurseLivingMember>(best);
            if (!em.HasComponent<RunaiUnitTag>(best)) em.AddComponent<RunaiUnitTag>(best);
            if (em.HasComponent<Target>(best)) em.SetComponentData(best, new Target { Value = Entity.Null });
            if (em.HasComponent<DesiredDestination>(best))
                em.SetComponentData(best, new DesiredDestination { Position = at, Has = 0 });
            if (f == GameSettings.LocalPlayerFaction)
                SimSignals.Notify(TheWaningBorder.Core.Localization.Loc.T("A curse creature is Veilbound to you"));
        }

        private static void Sort(NativeList<Entity> list)
        {
            for (int i = 1; i < list.Length; i++)
            {
                var k = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].Index > k.Index) { list[j + 1] = list[j]; j--; }
                list[j + 1] = k;
            }
        }
    }
}
