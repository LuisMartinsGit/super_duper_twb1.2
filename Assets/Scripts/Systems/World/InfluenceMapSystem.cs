// InfluenceMapSystem.cs
// Ticks the per-player influence map (docs/Design/Overview.md § The
// influence map):
//   - every channel decays slowly toward 0, so unclaimed ground returns
//     to neutral
//   - curse (Border) nodes and units slowly spread curse influence
//   - completed Age 1 Alanthor fortifications (wall hubs, wall instances,
//     watch towers) grant their owner's influence
// Age 0 buildings and units contribute nothing by design.
//
// Fixed 0.5 s tick with a fixed dt so accumulation is frame-rate
// independent; sources and positions are simulation state, so peers stay
// consistent in lockstep. The map itself is currently display-only
// (minimap overlay) — future gameplay hooks should read PlayerInfluenceMap.

using TheWaningBorder.Influence;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct InfluenceMapSystem : ISystem
    {
        // Fine-grained ticking (design 2026-07-06 rev.2): every tenth of a
        // second, with per-tick deposits a tenth of the old half-second
        // amounts — same shapes, much smoother accumulation.
        private const float TickInterval = 0.1f;

        // Decay — proportional, so territory whose source died collapses
        // back to neutral in tens of seconds (border gone in ~15 s, fully
        // invisible in ~45 s), plus a small linear term so cells truly reach
        // 0. Sources must continuously outpace this to hold ground.
        private const float DecayFractionPerSecond = 0.05f;
        private const float DecayLinearPerSecond = 0.1f;

        // Curse sources — deliberately slow accumulation ("slowly spread"):
        // near cells cross the display threshold quickly, the rim creeps out
        // over tens of seconds.
        // (Rates are per-second, boosted +50 % [2026-07-06 rev.3] to win back
        // the claimed area the proportional decay eats: a source plateaus
        // near rate·falloff/0.05, so the 0.5 border sits where
        // falloff ≥ 2.5/rate — higher rate → the border reaches further out.)
        private const float CurseMainRate = 6f;   private const float CurseMainRadius = 30f;
        private const float CurseUnitRate = 2.5f; private const float CurseUnitRadius = 8f;

        // Alanthor fortifications — claim distinctly faster than the curse creeps.
        // Watch towers are the long ARM of Alanthor territory (2026-07-24):
        // a much larger claim radius than any other fortification, so a
        // forward tower meaningfully extends the culture's ground.
        private const float HubRate      = 9f;  private const float HubRadius      = 18f;
        private const float InstanceRate = 9f;  private const float InstanceRadius = 9f;
        private const float TowerRate    = 12f; private const float TowerRadius    = 45f;

        // All remaining cultured buildings (economic / civic — everything
        // except Gatherer's Huts and the Hall) grant influence, at a much
        // lower rate than the culture-signature sources. Applies to every
        // culture (Alanthor / Runai / Feraldis) — Age 0 grants nothing.
        private const float CivicRate = 4.5f; private const float CivicRadius = 16f;

        // The Hall is the POTENT heart of the territory: a fresh Age 1 start
        // owns nothing but a Hall, and Alanthor building is gated on own
        // influence — so the Hall must claim a buildable bubble within
        // seconds or the start soft-locks.
        private const float HallRate = 16f; private const float HallRadius = 32f;

        // Runai signature — trade nodes (Trade Hubs / Bazaars / trading
        // Halls) claim strongly, and the LANES between same-faction nodes
        // deposit a corridor of influence (movement-embodied territory).
        private const float TradeNodeRate = 9f; private const float TradeNodeRadius = 18f;
        private const float LaneRate = 3f; private const float LaneRadius = 8f;
        private const float LaneStep = 6f;         // deposit spacing along a lane
        private const float LaneMaxLength = 220f;  // ignore absurdly long pairs

        // Feraldis signature — totem towers stake claims.
        private const float TotemRate = 9f; private const float TotemRadius = 20f;

        // Blood fades over ~3 minutes (design 2026-07-06): a saturated
        // stain (100) drops below the terrain-paint threshold (15) after
        // ~2 min and reaches zero at ~4½ min — reading as "about three
        // minutes of visible stain" after a battle ends.
        private const float BloodDecayFractionPerSecond = 0.015f;
        private const float BloodDecayLinearPerSecond = 0.03f;
        /// <summary>Influence strength at/above which ground counts as
        /// "tended" — blood fades there; below it blood is eternal (§2.5b
        /// rev.3). Lower than the 0.5 territory border so your whole claimed
        /// area (border included) self-cleans.</summary>
        private const float BloodCleanInfluenceThreshold = 0.2f;

        private double _lastTick;

        private EntityQuery _curseMainQ;
        private EntityQuery _curseUnitQ;
        private EntityQuery _hubQ;
        private EntityQuery _instanceQ;
        private EntityQuery _towerQ;
        private EntityQuery _civicQ;
        private EntityQuery _hallQ;
        private EntityQuery _tradeNodeQ;
        private EntityQuery _totemQ;
        private EntityQuery _warTotemQ;
        private EntityQuery _progressQ;

        // Per-tick "age-up COMPLETED" culture per faction (index = Faction).
        // Sourced from the ECS FactionProgress on the Hall — NOT
        // FactionColors, which is set at culture-pick CLICK time. Buildings
        // must not grant influence until the age-up research finishes.
        private static readonly byte[] _completedCulture = new byte[PlayerInfluenceMap.PlayerChannels];

        public void OnCreate(ref SystemState state)
        {
            // Fresh match world → fresh maps (the stores are static and would
            // otherwise leak the previous match's territory / blood).
            PlayerInfluenceMap.Reset();
            BloodMap.Reset();
            _lastTick = double.MinValue;

            _curseMainQ = state.GetEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _curseUnitQ = state.GetEntityQuery(
                ComponentType.ReadOnly<BorderUnitTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            _hubQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<WallHubTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);
            _instanceQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<WallInstanceTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);
            _towerQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<WatchTowerTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);

            // Every other completed building of a cultured faction grants
            // influence (2026-07-24: Gatherer's Huts included — ALL Alanthor
            // buildings claim ground). Excludes only Halls (own potent tier
            // below) and the fortification tags already covered above.
            _civicQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction, WallTag>()
                .WithNone<WallHubTag, WallInstanceTag, WallSegmentTag>()
                .WithNone<WatchTowerTag, HallTag>()
                .Build(ref state);

            _hallQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HallTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);

            _tradeNodeQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TradeNodeTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);

            _totemQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TotemTowerTag, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);

            // Feraldis War Totems — the culture's real territory engine
            // (docs/Design/Age_1_Feraldis.md). Strength scales with the
            // blood the totem has banked, so this query carries Fervor.
            _warTotemQ = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<WarTotemTag, TotemFervor, LocalTransform, FactionTag>()
                .WithNone<UnderConstruction>()
                .Build(ref state);

            _progressQ = state.GetEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!PlayerInfluenceMap.Ready && !TryConfigure()) return;

            double now = SystemAPI.Time.ElapsedTime;
            if (now - _lastTick < TickInterval) return;
            _lastTick = now;

            var perfSw = System.Diagnostics.Stopwatch.StartNew();

            // Fixed timestep: accumulation is tick-count based, not frame based.
            const float dt = TickInterval;

            // Culture snapshot FIRST — the decay pass below needs to know
            // which channels belong to Feraldis factions.
            RefreshCompletedCultures();

            // Feraldis territory never decays (docs/Design/Age_1_Feraldis.md
            // § Feraldis influence never decays): its channels sit out the
            // uniform decay entirely and instead erode only on cells where
            // another player or the curse matches or beats them. Same rates,
            // so contested ground still flips on the usual ~15 s / ~45 s
            // clock — it just never fades on its own.
            int feraldisMask = FeraldisChannelMask();
            PlayerInfluenceMap.Decay(DecayFractionPerSecond * dt,
                DecayLinearPerSecond * dt, feraldisMask);
            PlayerInfluenceMap.DecayOutranked(feraldisMask,
                DecayFractionPerSecond * dt, DecayLinearPerSecond * dt);

            // §2.5b rev.3: blood only fades on TENDED ground (inside any
            // player influence). Outside influence it is eternal — old
            // battlefields stay stained until a blood-curse spawn consumes
            // them (BloodCurseSpawnSystem).
            BloodMap.DecayInsideInfluence(BloodDecayFractionPerSecond * dt,
                BloodDecayLinearPerSecond * dt, BloodCleanInfluenceThreshold);

            // Curse — Border nodes + creatures spread the curse channel.
            // §2.5b escalation (2026-08-04): deposits strengthen very slowly
            // over the match so weak player-influence rims are eventually
            // contested away (VeilCrustConstants.CurseInfluenceGrowthPerMinute).
            var em = state.EntityManager;
            float curseGrowth = 1f + TheWaningBorder.Core.Config.VeilCrustConstants
                .CurseInfluenceGrowthPerMinute * (float)(now / 60.0);
            DepositCurseNodes(em, _curseMainQ, CurseMainRadius, CurseMainRate * dt * curseGrowth);
            // Creatures die and drop out of the query — no state to filter.
            DepositAll(_curseUnitQ, PlayerInfluenceMap.CurseChannel, CurseUnitRadius, CurseUnitRate * dt * curseGrowth);

            // Alanthor fortifications — per-owner channel, completed builds only.
            // (Walls/towers are Alanthor-only buildables, so no culture check.)
            DepositFaction(em, _hubQ,      HubRadius,      HubRate * dt);
            DepositFaction(em, _instanceQ, InstanceRadius, InstanceRate * dt);
            DepositFaction(em, _towerQ,    TowerRadius,    TowerRate * dt);

            // Economic / civic buildings of every cultured faction — much
            // weaker than the signature sources; Age 0 factions (culture
            // None) contribute nothing.
            //
            // Feraldis is EXCLUDED here (docs/Design/Age_1_Feraldis.md): its
            // ordinary buildings project no claim at all. Feraldis territory
            // comes from War Totems planted on blood, plus the universal
            // Hall anchor below — you claim ground by bleeding on it.
            DepositCivic(em, _civicQ, CivicRadius, CivicRate * dt, skipCulture: Cultures.Feraldis);

            // Halls — the potent territorial anchor for every culture (fresh
            // Age 1 starts own nothing else).
            DepositCivic(em, _hallQ, HallRadius, HallRate * dt);

            // Runai — trade nodes + the lanes between same-faction nodes.
            DepositCivic(em, _tradeNodeQ, TradeNodeRadius, TradeNodeRate * dt);
            DepositTradeLanes(_tradeNodeQ, dt);

            // Feraldis — totem-tower claims.
            DepositCivic(em, _totemQ, TotemRadius, TotemRate * dt);

            // Feraldis — War Totems, scaled by the blood they have banked.
            DepositWarTotems(dt);

            perfSw.Stop();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                "InfluenceTick", perfSw.Elapsed.TotalMilliseconds);
        }

        private static bool TryConfigure()
        {
            // Map bounds come from the baked terrain (every playable map is
            // hand-authored with one). Until it exists, stay dormant.
            var terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return false;

            Vector3 pos = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            PlayerInfluenceMap.Configure(
                new Vector2(pos.x, pos.z),
                new Vector2(size.x, size.z));
            BloodMap.Configure(
                new Vector2(pos.x, pos.z),
                new Vector2(size.x, size.z));
            return true;
        }

        private static void DepositAll(EntityQuery query, int channel, float radius, float amount)
        {
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
                PlayerInfluenceMap.Deposit(xfs[i].Position.x, xfs[i].Position.z, radius, channel, amount);
        }

        /// <summary>Curse NODE deposits, state-filtered (2026-08-04, "the
        /// curse lingers even if no nodes are near"): ONE rule for every
        /// node — only a node still feeding the curse (Active, awake)
        /// projects curse influence. Cleansed / Converted / Destroyed /
        /// dormant nodes deposit nothing, so their old purple footprint
        /// self-decays away with the map. Verb wells cannot be removed, so
        /// while ACTIVE their influence rightly never fades — that is the
        /// only exception, and it is the same rule, not a special one.</summary>
        private static void DepositCurseNodes(EntityManager em, EntityQuery query,
            float radius, float amount)
        {
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                if (em.HasComponent<BorderNodeState>(ents[i])
                    && em.GetComponentData<BorderNodeState>(ents[i]).State != NodeState.Active)
                    continue;
                if (em.HasComponent<NodeDormant>(ents[i])) continue;
                PlayerInfluenceMap.Deposit(xfs[i].Position.x, xfs[i].Position.z,
                    radius, PlayerInfluenceMap.CurseChannel, amount);
            }
        }

        // Building level ("leveling up Alanthor buildings increases their
        // influence", 2026-08-04): each applied upgrade level widens and
        // strengthens the claim. L3 ≈ double the rate at ~1.45× the radius —
        // a leveled core visibly pushes the curse where a fresh one holds it.
        private const float LevelRateBonus = 0.35f;
        private const float LevelRadiusBonus = 0.15f;

        private static void LevelMul(EntityManager em, Entity e,
            out float rateMul, out float radiusMul)
        {
            int level = em.HasComponent<BuildingUpgradeState>(e)
                ? em.GetComponentData<BuildingUpgradeState>(e).Level : 0;
            rateMul = 1f + LevelRateBonus * level;
            radiusMul = 1f + LevelRadiusBonus * level;
        }

        private static void DepositFaction(EntityManager em, EntityQuery query, float radius, float amount)
        {
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                int channel = (int)facs[i].Value;
                LevelMul(em, ents[i], out float rm, out float dm);
                PlayerInfluenceMap.Deposit(xfs[i].Position.x, xfs[i].Position.z,
                    radius * dm, channel, amount * rm);
            }
        }

        /// <summary>
        /// Rebuild the per-faction completed-culture snapshot from the Halls'
        /// FactionProgress. AgeUpSystem writes that only when the age-up
        /// research COMPLETES — so mid-research factions still read as
        /// Cultures.None here and their buildings grant no influence.
        /// </summary>
        private void RefreshCompletedCultures()
        {
            for (int i = 0; i < _completedCulture.Length; i++)
                _completedCulture[i] = Cultures.None;

            using var facs = _progressQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var prog = _progressQ.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
            {
                int f = (int)facs[i].Value;
                if (f < 0 || f >= _completedCulture.Length) continue;
                if (prog[i].Culture != Cultures.None)
                    _completedCulture[f] = prog[i].Culture;
            }
        }

        private static bool HasCompletedAgeUp(Faction faction)
        {
            int f = (int)faction;
            return f >= 0 && f < _completedCulture.Length
                && _completedCulture[f] != Cultures.None;
        }

        /// <summary>
        /// Weak per-building claim for cultured factions.
        /// <paramref name="skipCulture"/> excludes one culture entirely —
        /// used to keep Feraldis's ordinary buildings silent, since that
        /// culture claims ground with War Totems instead.
        /// </summary>
        private static void DepositCivic(EntityManager em, EntityQuery query, float radius, float amount,
            byte skipCulture = Cultures.None)
        {
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                var faction = facs[i].Value;
                // Completed-age-up factions only — Age 0 buildings grant
                // nothing, and neither do buildings while the age-up research
                // is still ticking.
                if (!HasCompletedAgeUp(faction)) continue;
                if (skipCulture != Cultures.None && CultureOf(faction) == skipCulture) continue;
                LevelMul(em, ents[i], out float rm, out float dm);
                PlayerInfluenceMap.Deposit(
                    xfs[i].Position.x, xfs[i].Position.z,
                    radius * dm, (int)faction, amount * rm);
            }
        }

        /// <summary>Bitmask of the influence channels owned by factions that
        /// have COMPLETED the Feraldis age-up — the channels exempt from
        /// decay. Mid-research factions still read as Cultures.None and
        /// decay normally; a faction that finishes the age-up freezes
        /// whatever it holds at that moment.</summary>
        private static int FeraldisChannelMask()
        {
            int mask = 0;
            for (int f = 0; f < _completedCulture.Length; f++)
                if (_completedCulture[f] == Cultures.Feraldis) mask |= 1 << f;
            return mask;
        }

        private static byte CultureOf(Faction faction)
        {
            int f = (int)faction;
            return f >= 0 && f < _completedCulture.Length
                ? _completedCulture[f] : Cultures.None;
        }

        /// <summary>
        /// Feraldis War Totems. Both the deposit rate and the radius ramp
        /// with banked Fervor (the blood the totem has drunk), so a totem
        /// planted on a massacre claims far more ground than one planted on
        /// a skirmish. WarTotemFervorSystem owns the banking.
        /// </summary>
        private void DepositWarTotems(float dt)
        {
            using var xfs = _warTotemQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = _warTotemQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var fervors = _warTotemQ.ToComponentDataArray<TotemFervor>(Allocator.Temp);

            for (int i = 0; i < xfs.Length; i++)
            {
                var faction = facs[i].Value;
                if (!HasCompletedAgeUp(faction)) continue;

                float t = Mathf.Clamp01(fervors[i].Value
                    / TheWaningBorder.Core.Config.FeraldisConstants.TotemFervorMax);
                float rate = Mathf.Lerp(
                    TheWaningBorder.Core.Config.FeraldisConstants.TotemInfluenceRateMin,
                    TheWaningBorder.Core.Config.FeraldisConstants.TotemInfluenceRateMax, t);
                float radius = Mathf.Lerp(
                    TheWaningBorder.Core.Config.FeraldisConstants.TotemInfluenceRadiusMin,
                    TheWaningBorder.Core.Config.FeraldisConstants.TotemInfluenceRadiusMax, t);

                PlayerInfluenceMap.Deposit(xfs[i].Position.x, xfs[i].Position.z,
                    radius, (int)faction, rate * dt);
            }
        }

        /// <summary>Runai lanes: every pair of same-faction trade nodes
        /// deposits a corridor of influence along the connecting line —
        /// stamped at LaneStep intervals with the lane radius/rate.</summary>
        private static void DepositTradeLanes(EntityQuery query, float dt)
        {
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            float amount = LaneRate * dt;

            for (int i = 0; i < xfs.Length; i++)
            {
                var faction = facs[i].Value;
                if (!HasCompletedAgeUp(faction)) continue;

                for (int j = i + 1; j < xfs.Length; j++)
                {
                    if (facs[j].Value != faction) continue;

                    var a = xfs[i].Position;
                    var b = xfs[j].Position;
                    float dx = b.x - a.x, dz = b.z - a.z;
                    float len = Unity.Mathematics.math.sqrt(dx * dx + dz * dz);
                    if (len < LaneStep || len > LaneMaxLength) continue;

                    int steps = (int)(len / LaneStep);
                    int channel = (int)faction;
                    for (int s = 1; s < steps; s++)
                    {
                        float t = s / (float)steps;
                        PlayerInfluenceMap.Deposit(
                            a.x + dx * t, a.z + dz * t, LaneRadius, channel, amount);
                    }
                }
            }
        }
    }
}
