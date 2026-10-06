// SimpleAISystem.Military.cs
// Army missions, attack waves, reinforcement and corrupted-patch reclaim.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.World.Regions;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using UnityEngine;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_BuildingTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_SmallNodeTagLocalTransformHealth =
        {
            ComponentType.ReadOnly<SmallNodeTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_SmallNodeTagLocalTransformHealth;

        #endregion

        // ─────────────────────────────────────────────────────────────────
        // ARMY MISSIONS (AoE4-style encounters)
        //
        // Military is organized into persistent missions instead of a single
        // fire-and-forget blob: each mission owns its members, its objective,
        // and its own lifecycle (success -> regroup home; outmatched ->
        // retreat just that army; timeout -> disband). A small fast RAID
        // party harasses the enemy economy while the main ATTACK army pushes
        // the scored objective — mirroring the army/encounter structure of
        // the Relic-lineage RTS AIs (CoH / AoE4).
        //
        // Managed state: the AI runs host-only, nothing here replicates;
        // every effect flows out as ordinary unit commands.
        // ─────────────────────────────────────────────────────────────────

        private enum MissionType : byte { Attack, Raid }

        // Attack-mission phases (AoE4-plus: forward staging). Direct missions
        // march straight at the objective; staged missions (Hard+) first form
        // up at a point near the target on the home side, then commit at full
        // strength — fixing AoE4's documented always-rally-at-homebase habit.
        // Mustering (2026-09-07): forming up near home before the march —
        // see musterDistance. Every attack now runs Mustering -> Staging ->
        // Striking; Direct is kept for raids.
        private enum MissionPhase : byte { Direct = 0, Staging = 1, Striking = 2, Mustering = 3 }

        private sealed class Mission
        {
            public MissionType Type;
            public MissionPhase Phase;
            public Entity Target;
            public float3 TargetPos;
            public float3 StagePos;
            public float3 MusterPos;
            public float StartTime;
            /// <summary>When the current leg began — muster, stage or strike.
            /// Its own clock, so a long muster does not eat the stage timeout.</summary>
            public float LegStartTime;
            public float NextRegroupTime;
            /// <summary>Sim time the mission is over if its objective still
            /// stands: launch (or the last chain) + missionTimeoutSeconds +
            /// the march at missionMarchSpeedForTimeout. A flat clock from
            /// launch ran out on a 1024 m map before the army arrived
            /// (2026-10-05: 87 units stood down at 480 s, 0 on the objective).
            /// 0 = the flat clock (missions that never set it).</summary>
            public float Deadline;
            /// <summary>TryBreachWall: where the army last stood and since
            /// when it has not moved, and whether it is on a wall piece.</summary>
            public float3 LastCentroid;
            public float StalledSince;
            public bool Breaching;

            // ── Tactical state (SimpleAISystem.Tactics.cs). ──
            /// <summary>What the whole army is killing right now, so the
            /// tactical layer only re-orders everyone when this CHANGES —
            /// re-issuing every tick resets the chase and the army never
            /// reaches anybody.</summary>
            public Entity Focus;
            /// <summary>True while the army is in contact. The edges matter:
            /// entering is when it stops marching and starts concentrating,
            /// leaving is when it must be put back on the march as one body
            /// rather than left as idle units standing where the fight ended.
            /// </summary>
            public bool Engaged;
            public float NextTacticsTime;

            // ── In-fight skills (SimpleAISystem.Tactics.cs, Game_AI.md § 6e). ──
            /// <summary>Fast melee detached to hit the enemy's side / rear.
            /// Empty when no flank is out.</summary>
            public readonly System.Collections.Generic.List<Entity> Flankers
                = new System.Collections.Generic.List<Entity>();
            /// <summary>0 none, 1 swinging round to FlankPoint, 2 striking.</summary>
            public byte FlankPhase;
            public float3 FlankPoint;
            public float FlankStart;
            public float NextFlankTime;
            /// <summary>Falling back to regroup on FallbackPos (a friendly
            /// tower / Fortress, or toward the capital) after reading the
            /// fight as lost; re-engages when the odds turn.</summary>
            public bool FallingBack;
            public float3 FallbackPos;
            public float FallbackStart;

            // ── Many armies (Game_AI.md § 6f). ──
            /// <summary>The wave this army launched with (0 = none / a lone
            /// army). Sister armies of one group stage, then strike together.</summary>
            public int Group;
            /// <summary>The wave's main body: the one army whose objective
            /// the reinforcement stream (aiState.WaveTarget) follows.</summary>
            public bool Primary;
            /// <summary>A reinforcement column on its way to join an army —
            /// the only kind of mission TryMergeIntoArmy absorbs.</summary>
            public bool Column;
            /// <summary>When this army finished staging and began waiting
            /// for its sisters (0 = not yet).</summary>
            public float ReadyAt;

            public readonly System.Collections.Generic.List<Entity> Members
                = new System.Collections.Generic.List<Entity>();
        }

        /// <summary>A ranked income objective (RankIncomeTargets).</summary>
        private struct IncomeTarget
        {
            public Entity Ent;
            public float3 Pos;
            public float Score;
            public string What;
            public Faction Owner;
        }
        private readonly System.Collections.Generic.List<IncomeTarget> _scratchIncome
            = new System.Collections.Generic.List<IncomeTarget>();
        private int _nextArmyGroup;







        /// <summary>Has this faction ever SEEN this ground? Attack orders
        /// dispatch only at known positions (2026-08-31 directive: armies
        /// were marching on Halls nobody had scouted — the doctrine layers
        /// bypassed the fog-honest intel path). IsRevealed is explored-map
        /// memory, not live vision: knowing where a base IS survives the
        /// scout that found it. Fail-open without a fog manager.</summary>
        private static bool IsKnownGround(Faction faction, float3 pos)
            => AICommon.IsKnownGround(faction, pos);

        /// <summary>
        /// The victim's nearest KNOWN Hall — from OUR sighting buffer, not
        /// the live entity table (2026-08-31 intel-flow directive). The army
        /// marches at what the scouts REPORTED; the report ages, the enemy
        /// moves, the building may be rubble on arrival — that uncertainty
        /// is the game working as intended, and the arrival logic handles
        /// the empty site. Returns the sighting's entity when it still
        /// exists (normal objective) or Null for a location-only march.
        /// </summary>
        private static bool TryNearestHallSighting(EntityManager em, Entity brainEntity,
            Faction victim, float3 origin, out float3 pos, out float age, out Entity ent)
        {
            pos = default; age = 0f; ent = Entity.Null;
            if (brainEntity == Entity.Null
                || !em.HasBuffer<EnemySightingRecord>(brainEntity)) return false;
            var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
            float bestD2 = float.MaxValue;
            double now = 0;
            bool found = false;
            for (int i = 0; i < buf.Length; i++)
            {
                var s = buf[i];
                if (s.OwnerFaction != victim || s.Category != IntelCategory.Hall) continue;
                float dx = s.Position.x - origin.x, dz = s.Position.z - origin.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= bestD2) continue;
                bestD2 = d2;
                pos = s.Position;
                age = (float)(TheWaningBorder.Core.SimClock.Now - s.LastSeenTime);
                ent = (s.Enemy != Entity.Null && em.Exists(s.Enemy)) ? s.Enemy : Entity.Null;
                found = true;
            }
            return found;
        }

        /// <summary>Board score: territories weigh 8 (they are the economy),
        /// standing military 1. Negative when the faction has no Hall.</summary>
        private static int BoardScore(EntityManager em, Faction f)
        {
            if (FindFactionBuilding<HallTag>(em, f) == Entity.Null) return -1;
            return TheWaningBorder.World.Regions.TerritoryOwnership.CountOf(f) * 8
                 + CountAliveMilitary(em, f);
        }

        /// <summary>The hostile faction holding a REAL lead (see
        /// <see cref="LeadMargin"/>), or the caller when nobody does.</summary>
        private static Faction LeadingHostileFaction(EntityManager em, Faction mine)
        {
            Faction best = mine; int bestScore = -1, second = -1;
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f == mine || !Alliances.AreHostile(mine, f)) continue;
                int score = BoardScore(em, f);
                if (score < 0) continue;
                if (score > bestScore) { second = bestScore; bestScore = score; best = f; }
                else if (score > second) second = score;
            }
            if (bestScore < 0) return mine;
            // My own standing counts toward "second": a leader ahead of every
            // rival but behind ME is my problem to keep, not to attack.
            second = math.max(second, BoardScore(em, mine));
            return bestScore >= Cfg.leadMargin * math.max(1, second) ? best : mine;
        }

        /// <summary>The hostile faction with the LEAST on the board — the
        /// closeout target. Caller when no hostile Hall remains.</summary>
        private static Faction WeakestHostileFaction(EntityManager em, Faction mine)
        {
            Faction worst = mine; int worstScore = int.MaxValue;
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f == mine || !Alliances.AreHostile(mine, f)) continue;
                int score = BoardScore(em, f);
                if (score < 0 || score >= worstScore) continue;
                worstScore = score;
                worst = f;
            }
            return worst;
        }

        /// <summary>
        /// The enemy's nearest EXPANSION Hall — any Hall that is not their
        /// starting one (lowest NetworkId; ids are sequential from spawn).
        /// Null when they hold no expansion, which is when the home base
        /// becomes the target. The starve-then-storm doctrine's picker.
        /// </summary>
        private static Entity FindNearestExpansionHall(EntityManager em, Faction victim,
            Faction attacker, float3 origin, out float3 pos)
        {
            pos = default;
            var q = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Pass 1: the home Hall is the lowest NetworkId this faction owns.
            long homeNid = long.MaxValue;
            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != victim) continue;
                count++;
                long nid = em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i])
                    ? em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i]).NetworkId
                    : long.MaxValue - 1;
                if (nid < homeNid) homeNid = nid;
            }
            if (count <= 1) return Entity.Null;   // nothing to starve

            // Pass 2: nearest Hall that is NOT home.
            Entity best = Entity.Null;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != victim) continue;
                long nid = em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i])
                    ? em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i]).NetworkId
                    : long.MaxValue - 1;
                if (nid == homeNid) continue;
                if (!IsKnownGround(attacker, xfs[i].Position)) continue;
                float dx = xfs[i].Position.x - origin.x;
                float dz = xfs[i].Position.z - origin.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= bestD2) continue;
                bestD2 = d2;
                best = ents[i];
                pos = xfs[i].Position;
            }
            return best;
        }

        /// <summary>Nearest hostile building to a point, or Null. The chain
        /// target for a won assault — buildings are the lifelines the victory
        /// check counts, so razing them is what ENDS a match.</summary>
        /// <summary>A mission's deadline: the flat missionTimeoutSeconds plus
        /// the march from <paramref name="from"/> to <paramref name="to"/>
        /// at missionMarchSpeedForTimeout (0 = the flat clock).</summary>
        private float DeadlineFor(float now, float3 from, float3 to)
        {
            float t = math.max(1f, Cfg.missionTimeoutSeconds);
            if (Cfg.missionMarchSpeedForTimeout > 0f)
                t += math.distance(from.xz, to.xz) / Cfg.missionMarchSpeedForTimeout;
            return now + t;
        }

        /// <summary>Does <paramref name="owner"/>'s capital stand in territory
        /// <paramref name="r"/>? The AI walls its home territory and only
        /// that (AIWallPlanner.CollectWallTerritories), so this is "walled
        /// ground" for every doctrine that must not march into a wall.</summary>
        private static bool IsWalledGround(EntityManager em, Faction owner, int r)
        {
            if (r == RegionMap.None) return false;
            Entity hall = FindFactionBuilding<HallTag>(em, owner);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return false;
            var p = em.GetComponentData<LocalTransform>(hall).Position;
            if (RegionMap.RegionAt(p.x, p.z) != r) return false;
            // ...AND A WALL ACTUALLY STANDS THERE (2026-10-05, Game_AI.md 6f).
            // The capital's territory read as walled from the first second,
            // and on Mirror Marches the whole Age 0 economy sits in it — so
            // every early income building was unreachable by rule while the
            // ground was in fact open. Walled means wall pieces of the owner
            // stand in the territory; until they do, its holdings are fair.
            return HasWallPiecesIn(em, owner, r);
        }

        /// <summary>Does <paramref name="owner"/> have any wall piece
        /// (palisade or stone — both carry WallInstanceTag) standing in
        /// territory <paramref name="r"/>?</summary>
        private static bool HasWallPiecesIn(EntityManager em, Faction owner, int r)
        {
            var q = QC_WallPieceFactionHealthXf.Get(em, QT_WallPieceFactionHealthXf);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != owner) continue;
                if (RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z) == r) return true;
            }
            return false;
        }

        /// <summary>
        /// The victim's nearest KNOWN eco or military building on ground
        /// that does not hold its capital (see IsWalledGround), skipping
        /// razed sightings and ground a timed-out mission blacklisted.
        /// </summary>
        private bool TryNearestUnwalledSighting(EntityManager em, Entity brainEntity, Faction faction,
            Faction victim, float3 origin, float now, out float3 pos, out Entity ent, out string what)
        {
            pos = default; ent = Entity.Null; what = null;
            if (!em.HasBuffer<EnemySightingRecord>(brainEntity)) return false;
            var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
            float bestD2 = float.MaxValue;
            for (int i = 0; i < buf.Length; i++)
            {
                var s = buf[i];
                if (s.OwnerFaction != victim) continue;
                if (s.Category != IntelCategory.EcoBuilding && s.Category != IntelCategory.MilitaryBuilding) continue;
                if (s.Enemy != Entity.Null && !em.Exists(s.Enemy)) continue;   // razed since
                int r = RegionMap.RegionAt(s.Position.x, s.Position.z);
                if (IsWalledGround(em, victim, r)) continue;
                var (cx, cz) = WaveCell(s.Position);
                if (_waveBlocked.TryGetValue(((int)faction, cx, cz), out float until) && now < until) continue;
                float dx = s.Position.x - origin.x, dz = s.Position.z - origin.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= bestD2) continue;
                bestD2 = d2;
                pos = s.Position;
                ent = s.Enemy != Entity.Null && em.Exists(s.Enemy) ? s.Enemy : Entity.Null;
                what = s.Category == IntelCategory.EcoBuilding ? "eco building" : "military building";
            }
            return bestD2 < float.MaxValue;
        }

        /// <summary>
        /// THE ENEMY'S INCOME (2026-10-05, Game_AI.md 6f). Every known
        /// hostile building outside its owner's walls, ranked by what it is
        /// worth to the enemy: extractors first (the Gatherer's Hut, Mine,
        /// Veilstone Mine and Trading Outpost are where income is made),
        /// military buildings next, houses and the rest last — minus the
        /// target scorer's charges for distance, the garrison seen there
        /// and the age of the report. <paramref name="victim"/> equal to
        /// the caller means any hostile. Razed, walled, blacklisted and
        /// unseen ground is skipped. Highest score first.
        /// </summary>
        private void RankIncomeTargets(EntityManager em, Entity brainEntity, Faction faction,
            Faction victim, float3 origin, float now, float risk, AISettingsSO settings,
            System.Collections.Generic.List<IncomeTarget> into)
        {
            into.Clear();
            if (!em.HasBuffer<EnemySightingRecord>(brainEntity)) return;
            var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
            float simNow = (float)TheWaningBorder.Core.SimClock.Now;
            for (int i = 0; i < buf.Length; i++)
            {
                var s = buf[i];
                if (s.OwnerFaction == Faction.Border || !Alliances.AreHostile(faction, s.OwnerFaction)) continue;
                if (victim != faction && s.OwnerFaction != victim) continue;
                if (s.Category != IntelCategory.EcoBuilding && s.Category != IntelCategory.MilitaryBuilding) continue;
                if (s.Enemy == Entity.Null || !em.Exists(s.Enemy)) continue;        // razed since
                if (em.HasComponent<UnderConstruction>(s.Enemy)) continue;
                int r = RegionMap.RegionAt(s.Position.x, s.Position.z);
                if (IsWalledGround(em, s.OwnerFaction, r)) continue;
                if (WaveTargetBlocked(faction, s.Position, now)) continue;
                if (!IsKnownGround(faction, s.Position)) continue;

                float weight; string what;
                Entity e = s.Enemy;
                if (s.Category == IntelCategory.MilitaryBuilding) { weight = Cfg.incomeWeightMilitary; what = "military building"; }
                else if (em.HasComponent<TradingOutpostTag>(e)) { weight = Cfg.incomeWeightExtractor; what = "trading outpost"; }
                else if (em.HasComponent<VeilstoneMineTag>(e)) { weight = Cfg.incomeWeightExtractor; what = "veilstone mine"; }
                else if (em.HasComponent<MineTag>(e) || em.HasComponent<IronMineTag>(e)) { weight = Cfg.incomeWeightExtractor; what = "mine"; }
                else if (em.HasComponent<GathererHutTag>(e)) { weight = Cfg.incomeWeightExtractor; what = "gatherer's hut"; }
                else if (em.HasComponent<HutTag>(e)) { weight = Cfg.incomeWeightHouse; what = "house"; }
                else { weight = Cfg.incomeWeightHouse; what = "eco building"; }

                float dx = s.Position.x - origin.x, dz = s.Position.z - origin.z;
                float score = weight * 100f
                              - math.sqrt(dx * dx + dz * dz) * settings.travelCostPerMeter
                              - s.EstStrength * settings.riskPerDefenseStrength * risk
                              - math.max(0f, simNow - s.LastSeenTime) * settings.intelAgePenaltyPerSecond;
                into.Add(new IncomeTarget { Ent = e, Pos = s.Position, Score = score, What = what, Owner = s.OwnerFaction });
            }
            into.Sort((x, y) =>
            {
                int c = y.Score.CompareTo(x.Score);
                return c != 0 ? c : x.Ent.Index.CompareTo(y.Ent.Index);
            });
        }

        private readonly System.Collections.Generic.Dictionary<int, float> _incomeReconAt
            = new System.Collections.Generic.Dictionary<int, float>();
        private readonly System.Collections.Generic.Dictionary<int, int> _incomeReconLeg
            = new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>
        /// THE SCOUTS GO WHERE THE INCOME IS (2026-10-05, Game_AI.md 6f). An
        /// income-targeting tier cannot hit what it has not seen, and the
        /// zone director explores outward from home: in the v15 smoke match
        /// Expert's scouts spent nine minutes in its own corner and reported
        /// the first enemy eco building at minute 18. While fewer than
        /// incomeReconMinKnown hostile income buildings are known, this files
        /// a recon request every incomeReconIntervalSeconds at the nearest
        /// hostile START position (public knowledge) and then at points
        /// incomeReconSpreadMeters around it — the ground its economy grows
        /// into — so the scout director (which serves recon requests before
        /// exploration) walks the enemy's holdings early. Never overrides a
        /// request already filed.
        /// </summary>
        private void TickIncomeRecon(EntityManager em, Faction faction, ref SimpleAIState aiState,
            in AIDifficultyProfile profile, Entity brainEntity, float now)
        {
            if (!profile.IncomeTargeting || aiState.HasReconRequest != 0) return;
            int fk = (int)faction;
            if (_incomeReconAt.TryGetValue(fk, out float at) && now < at) return;
            _incomeReconAt[fk] = now + math.max(10f, Cfg.incomeReconIntervalSeconds);

            int known = 0;
            if (em.HasBuffer<EnemySightingRecord>(brainEntity))
            {
                var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
                for (int i = 0; i < buf.Length; i++)
                {
                    var s = buf[i];
                    if (s.Category != IntelCategory.EcoBuilding) continue;
                    if (s.OwnerFaction == Faction.Border || !Alliances.AreHostile(faction, s.OwnerFaction)) continue;
                    if (s.Enemy == Entity.Null || !em.Exists(s.Enemy)) continue;
                    known++;
                }
            }
            if (known >= math.max(1, Cfg.incomeReconMinKnown)) return;

            Entity myHall = FindFactionBuilding<HallTag>(em, faction);
            if (myHall == Entity.Null || !em.HasComponent<LocalTransform>(myHall)) return;
            float3 home = em.GetComponentData<LocalTransform>(myHall).Position;
            Entity start = FindEnemyStartHall(em, faction, home);
            if (start == Entity.Null || !em.HasComponent<LocalTransform>(start)) return;
            float3 sp = em.GetComponentData<LocalTransform>(start).Position;

            // Leg 0 the start itself, then the ring around it: toward home
            // (the ground between us, where its forward holdings are), then
            // the two flanks, then the far side.
            _incomeReconLeg.TryGetValue(fk, out int leg);
            _incomeReconLeg[fk] = leg + 1;
            float3 toHome = home - sp; toHome.y = 0f;
            float len = math.length(toHome);
            float3 dir = len > 1f ? toHome / len : new float3(1f, 0f, 0f);
            float3 side = new float3(-dir.z, 0f, dir.x);
            float r = math.max(0f, Cfg.incomeReconSpreadMeters);
            float3 p = (leg % 5) switch
            {
                0 => sp,
                1 => sp + dir * r,
                2 => sp + side * r,
                3 => sp - side * r,
                _ => sp - dir * r,
            };
            aiState.ReconTarget = p;
            aiState.HasReconRequest = 1;
            AILogger.Log(faction, "SCOUT",
                $"income recon: {known} hostile income building(s) known — " +
                $"scouting the enemy's holdings at ({p.x:0},{p.z:0})");
        }

        /// <summary>Compass bearing of <paramref name="p"/> from <paramref name="from"/>, degrees.</summary>
        private static float BearingDeg(float3 from, float3 p)
            => math.degrees(math.atan2(p.x - from.x, p.z - from.z));

        private static float BearingDiff(float a, float b)
        {
            float d = math.abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>
        /// MANY ARMIES, MANY DIRECTIONS (2026-10-05, Game_AI.md 6f). Up to
        /// <paramref name="want"/> further objectives for the wave's sister
        /// armies: the victim's income targets (RankIncomeTargets), each on
        /// an approach bearing — as seen from the victim's capital, or from
        /// home when its capital is unknown — at least armySeparationDegrees
        /// from every objective already chosen, at least armySeparationMeters
        /// from it, and a fight the army's share could take (unless the wave
        /// is overdue). The victim is the owner of the main objective.
        /// </summary>
        private void PickSisterTargets(EntityManager em, Entity brainEntity, Faction faction,
            float3 originPos, Entity mainTarget, float3 mainPos, Faction waveVictim, float now,
            System.Collections.Generic.List<Entity> idle, int bodies, int want,
            float risk, AISettingsSO settings, System.Collections.Generic.List<IncomeTarget> into)
        {
            into.Clear();
            Faction victim = waveVictim;
            if (mainTarget != Entity.Null && em.Exists(mainTarget) && em.HasComponent<FactionTag>(mainTarget))
                victim = em.GetComponentData<FactionTag>(mainTarget).Value;
            RankIncomeTargets(em, brainEntity, faction, victim, originPos, now, risk, settings, _scratchIncome);
            if (_scratchIncome.Count == 0) return;

            // Bearings are read from the victim's capital when the scouts
            // have reported it; otherwise from home, which still spreads
            // the approaches.
            float3 reference = originPos;
            if (victim != faction && TryNearestHallSighting(em, brainEntity, victim, originPos,
                    out float3 hallPos, out _, out _))
                reference = hallPos;

            var bearings = new System.Collections.Generic.List<float> { BearingDeg(reference, mainPos) };
            var positions = new System.Collections.Generic.List<float3> { mainPos };
            float sep2 = Cfg.armySeparationMeters * Cfg.armySeparationMeters;
            // A provisional share — the first 1/bodies of the draft — stands
            // in for each sister army's strength in the assessment.
            int share = math.max(1, idle.Count / math.max(1, bodies));
            var probe = new System.Collections.Generic.List<Entity>(share);
            for (int i = 0; i < share && i < idle.Count; i++) probe.Add(idle[i]);

            for (int i = 0; i < _scratchIncome.Count && into.Count < want; i++)
            {
                var t = _scratchIncome[i];
                bool apart = true;
                for (int k = 0; k < positions.Count && apart; k++)
                {
                    float dx = t.Pos.x - positions[k].x, dz = t.Pos.z - positions[k].z;
                    if (dx * dx + dz * dz < sep2) apart = false;
                    else if (BearingDiff(BearingDeg(reference, t.Pos), bearings[k]) < Cfg.armySeparationDegrees) apart = false;
                }
                if (!apart) continue;
                // NEVER WAIVED (2026-10-05): overdue used to skip this, and an
                // overdue Expert split 34 units into 11 + 11 + 12 against a
                // defender with 80 — a blob that is overdue goes as a blob.
                var a = AIEngagement.AssessAssault(em, faction, probe, t.Pos);
                if (!a.ShouldFight) continue;
                into.Add(t);
                bearings.Add(BearingDeg(reference, t.Pos));
                positions.Add(t.Pos);
            }
        }

        /// <summary>Does this mission's objective steer the reinforcement
        /// stream (aiState.WaveTarget)? The wave's primary army does; any
        /// other only while no primary army is alive.</summary>
        private static bool OwnsWaveTarget(System.Collections.Generic.List<Mission> missions, Mission mission)
        {
            if (mission.Primary) return true;
            for (int i = 0; i < missions.Count; i++)
                if (missions[i].Primary && missions[i] != mission) return false;
            return true;
        }

        /// <summary>STRIKE TOGETHER (Game_AI.md 6f): true when every sister
        /// army of <paramref name="mission"/>'s group has staged (or is
        /// already striking), or the wait has run past armySyncTimeoutSeconds.</summary>
        private bool GroupReadyToStrike(System.Collections.Generic.List<Mission> missions, Mission mission, float now)
        {
            for (int i = 0; i < missions.Count; i++)
            {
                var s = missions[i];
                if (s == mission || s.Group != mission.Group || s.Column || s.Type != MissionType.Attack) continue;
                if (s.Phase == MissionPhase.Striking || s.ReadyAt > 0f) continue;
                return now - mission.ReadyAt > Cfg.armySyncTimeoutSeconds;
            }
            return true;
        }

        /// <summary>
        /// Put an attack mission on the road: muster outside the gate, march
        /// to a stage point on its own origin→objective line, strike (the
        /// commit in UpdateMissions) — or strike direct when the objective
        /// is close. Sets the deadline, files the mission, logs the motive.
        /// </summary>
        private void DispatchArmy(EntityManager em, Faction faction, Mission attack, float3 originPos,
            float now, string doctrine, int ofBodies)
        {
            float3 targetPos = attack.TargetPos;
            float3 fromTarget = originPos - targetPos;
            fromTarget.y = 0f;
            float approachDist = math.length(fromTarget);
            // STAGING IS FOR EVERYONE (2026-08-30 directive — was Hard+ via
            // personality.forwardStaging, and the default headless tier is
            // Normal, so batch armies attack-moved across the whole map).
            // The approach leg is a plain formation MARCH, not an attack-
            // move: an attack-moving army peels at every skirmish it passes
            // and arrives as stragglers. It forms up at the stage point,
            // then strikes as one body (the commit in TickMissions).
            if (approachDist > Cfg.stagingDistance * 2f)
            {
                attack.StagePos = targetPos + (fromTarget / approachDist) * Cfg.stagingDistance;
                // MUSTER FIRST (2026-09-07). The army was dispatched from
                // wherever it stood, and the formation plan makes members
                // only of units already close to the centroid — so a base
                // full of rally points sent most of the army to the stage
                // point one by one. Form up outside the gate, THEN march.
                // Same shape the curse waves get from spawning compact.
                attack.Phase = MissionPhase.Mustering;
                attack.MusterPos = originPos - (fromTarget / approachDist) * Cfg.musterDistance;
                attack.LegStartTime = now;
                CommandRouter.IssueFormationMove(
                    em, attack.Members, attack.MusterPos, FormationShape.Box, CommandSource.AI);
            }
            else
            {
                attack.Phase = MissionPhase.Striking;
                attack.LegStartTime = now;
                CommandRouter.IssueFormationAttackMove(
                    em, attack.Members, targetPos, FormationShape.Box, CommandSource.AI);
            }
            attack.Deadline = DeadlineFor(now, originPos, targetPos);
            attack.LastCentroid = originPos;
            attack.StalledSince = now;
            MissionsFor(faction).Add(attack);
            // THE MOTIVATION LINE (2026-08-31 audit directive): every launch
            // says what it attacks and WHY, so a march without a credible
            // motive is visible in the log rather than only on the map.
            AILogger.Log(faction, "WAVE",
                (ofBodies > 1 ? (attack.Primary ? "main army: " : "sister army: ") : "") +
                $"objective ({targetPos.x:0},{targetPos.z:0}) [{doctrine}] " +
                $"army {attack.Members.Count}, " +
                (attack.Phase == MissionPhase.Mustering
                    ? $"mustering at ({attack.MusterPos.x:0},{attack.MusterPos.z:0}), " +
                      $"staging at ({attack.StagePos.x:0},{attack.StagePos.z:0})"
                    : "striking direct"));
        }

        static readonly ComponentType[] QT_WallPieceFactionHealthXf =
        {
            ComponentType.ReadOnly<WallInstanceTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_WallPieceFactionHealthXf;
        private readonly System.Collections.Generic.List<Entity> _breachFoot =
            new System.Collections.Generic.List<Entity>();

        /// <summary>
        /// THE WALL IN THE WAY (2026-10-05, Game_AI.md 6a). A striking army
        /// that has stood still short of its objective for
        /// wallBreachAfterSeconds, with a hostile wall piece within
        /// wallBreachRadius, is stopped by that wall: the Wall Rule
        /// (Combat_Pacing.md) lets only siege hurt it. With siege along, the
        /// engines are set on the nearest piece and the rest attack-move to
        /// it (the raze chain then presses on through the breach); with none,
        /// the mission ends now instead of standing under the towers for the
        /// rest of its clock.
        /// </summary>
        private void TryBreachWall(EntityManager em, Faction faction, ref SimpleAIState aiState,
            Mission mission, float3 centroid, float now)
        {
            if (mission.Breaching) return;
            float ar = Cfg.waveArrivedRadius;
            if (math.distancesq(centroid.xz, mission.TargetPos.xz) <= ar * ar) return;
            if (math.distancesq(centroid.xz, mission.LastCentroid.xz) > 36f)
            {
                mission.LastCentroid = centroid;
                mission.StalledSince = now;
                return;
            }
            if (now - mission.StalledSince < math.max(5f, Cfg.wallBreachAfterSeconds)) return;

            Entity best = Entity.Null;
            float3 bestPos = default;
            float bestD2 = Cfg.wallBreachRadius * Cfg.wallBreachRadius;
            {
                var q = QC_WallPieceFactionHealthXf.Get(em, QT_WallPieceFactionHealthXf);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (hps[i].Value <= 0f || !Alliances.AreHostile(faction, facs[i].Value)) continue;
                    float d2 = math.distancesq(xfs[i].Position.xz, centroid.xz);
                    if (d2 >= bestD2) continue;
                    bestD2 = d2;
                    best = ents[i];
                    bestPos = xfs[i].Position;
                }
            }
            mission.StalledSince = now;
            mission.LastCentroid = centroid;
            if (best == Entity.Null) return;   // stalled on something else: the deadline decides

            int siege = 0;
            _breachFoot.Clear();
            for (int i = 0; i < mission.Members.Count; i++)
            {
                var e = mission.Members[i];
                if (!em.Exists(e)) continue;
                if (em.HasComponent<SiegeTag>(e)) siege++; else _breachFoot.Add(e);
            }
            if (siege < math.max(1, Cfg.wallBreachMinSiege))
            {
                mission.Deadline = now;
                AILogger.Log(faction, "WAVE",
                    $"wall in the way at ({bestPos.x:0},{bestPos.z:0}) and no siege in the army — " +
                    $"mission ends ({mission.Members.Count} alive); the next wave needs engines");
                return;
            }
            mission.Breaching = true;
            mission.Target = best;
            mission.TargetPos = bestPos;
            aiState.WaveTarget = bestPos;
            mission.Deadline = now + math.max(1f, Cfg.missionTimeoutSeconds);
            for (int i = 0; i < mission.Members.Count; i++)
            {
                var e = mission.Members[i];
                if (em.Exists(e) && em.HasComponent<SiegeTag>(e))
                    CommandRouter.IssueAttack(em, e, best, CommandSource.AI);
            }
            if (_breachFoot.Count > 0)
                CommandRouter.IssueFormationAttackMove(em, _breachFoot, bestPos, FormationShape.Box, CommandSource.AI);
            AILogger.Log(faction, "WAVE",
                $"wall in the way — breaching at ({bestPos.x:0},{bestPos.z:0}) with {siege} siege engine(s), " +
                $"{_breachFoot.Count} covering");
        }

        private static Entity FindNearestHostileBuilding(EntityManager em, Faction faction,
            float3 around, float radius, out float3 pos)
        {
            pos = default;
            var q = QC_BuildingTagFactionTagLocalTransform.Get(em, QT_BuildingTagFactionTagLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD2 = radius * radius;
            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(faction, facs[i].Value)) continue;
                // A wave's follow-up objective is a PLAYER's building, never
                // the curse's (Game_AI.md § 6a, 2026-10-05): Red's raid on a
                // stray eco building "pressed on" to the curse nodes 30 m
                // away, six times, and fed 6-7 units at a time into
                // garrisons it never assessed. Curse nodes fall only to the
                // margin-checked paths (first-RP hunt, reclaim, curse clear,
                // claim curse assault).
                if (facs[i].Value == Faction.Border) continue;
                float dx = xfs[i].Position.x - around.x;
                float dz = xfs[i].Position.z - around.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= bestD2) continue;
                bestD2 = d2;
                best = ents[i];
                pos = xfs[i].Position;
            }
            return best;
        }


        /// <summary>Host-only scratch set for the "who already serves in a
        /// mission" tests (wave draft, reinforcement sweep) — one HashSet per
        /// call used to be allocated. Cleared at each use; never held across
        /// a call.</summary>
        private readonly System.Collections.Generic.HashSet<Entity> _scratchEnrolled =
            new System.Collections.Generic.HashSet<Entity>();
        private readonly System.Collections.Generic.List<Entity> _regroupBody =
            new System.Collections.Generic.List<Entity>(32);

        private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Mission>> _missions
            = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Mission>>();

        /// <summary>Ground a wave marched to and could not resolve, keyed by
        /// faction and a 40 m cell, held until the stored match time.
        ///
        /// THE SAME HOLE AS `_siteBlocked` IN THE CLAIM PLANNER (2026-09-12):
        /// a mission times out, its army walks home, nothing anywhere records
        /// that the trip failed, so the next wave scores the same sighting
        /// highest and marches to the same place. Yellow spent ninety minutes
        /// doing exactly this. A timeout is evidence; store it.</summary>
        private readonly System.Collections.Generic.Dictionary<(int faction, int cx, int cz), float> _waveBlocked
            = new System.Collections.Generic.Dictionary<(int, int, int), float>();

        /// <summary>Two mission timeouts. Long enough that the army fights
        /// somewhere else first, short enough that a real base does not become
        /// permanently invisible to the target scorer.</summary>
        private const float WaveBlockSeconds = 960f;

        /// <summary>40 m cell — a base is bigger than one building, so
        /// blocking a single position would just pick its neighbour.</summary>
        private static (int, int) WaveCell(float3 p)
            => ((int)math.floor(p.x / 40f), (int)math.floor(p.z / 40f));

        private bool WaveTargetBlocked(Faction f, float3 p, float now)
        {
            var (cx, cz) = WaveCell(p);
            if (!_waveBlocked.TryGetValue(((int)f, cx, cz), out float until)) return false;
            if (now < until) return true;
            _waveBlocked.Remove(((int)f, cx, cz));
            return false;
        }

        /// <summary>Per faction: the 40 m cell of the unseen wave objective it
        /// is holding for, and since when (Game_AI.md 6a — the hold for intel
        /// is bounded by waveIntelHoldMaxSeconds).</summary>
        private readonly System.Collections.Generic.Dictionary<int, (int cx, int cz, float Since)> _waveIntelHold
            = new System.Collections.Generic.Dictionary<int, (int, int, float)>();

        /// <summary>Seconds this faction's waves have been holding for intel
        /// on <paramref name="target"/>'s cell (0 the first time; a new cell
        /// restarts the clock).</summary>
        private float NoteWaveIntelHold(Faction f, float3 target, float now)
        {
            var (cx, cz) = WaveCell(target);
            if (_waveIntelHold.TryGetValue((int)f, out var h) && h.cx == cx && h.cz == cz)
                return now - h.Since;
            _waveIntelHold[(int)f] = (cx, cz, now);
            return 0f;
        }

        /// <summary>Is <paramref name="p"/> a player start position (within
        /// startHallKnownRadius of a start marker)? Start positions are public
        /// knowledge — the lobby shows them to everyone.</summary>
        private static bool IsStartPosition(float3 p)
        {
            var starts = TheWaningBorder.World.MapMarkers.MapMarkerRegistry.PlayerStarts;
            if (starts == null) return false;
            float r2 = Cfg.startHallKnownRadius * Cfg.startHallKnownRadius;
            for (int i = 0; i < starts.Count; i++)
            {
                var sm = starts[i];
                if (sm == null) continue;
                var sp = sm.WorldPosition;
                float dx = p.x - sp.x, dz = p.z - sp.z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        private System.Collections.Generic.List<Mission> MissionsFor(Faction f)
        {
            int key = (int)f;
            if (!_missions.TryGetValue(key, out var list))
            {
                list = new System.Collections.Generic.List<Mission>();
                _missions[key] = list;
            }
            return list;
        }
        // ─────────────────────────────────────────────────────────────────
        // LAUNCH ATTACK
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Send all idle military (battalion leaders + loose units, not members)
        /// to attack-move toward the best-scored enemy target (AI plan M2):
        /// candidates come from the brain's intel buffer and are scored by
        /// TargetScorer (type value - defense risk - travel - staleness), with
        /// the legacy nearest-by-priority ladder as fallback when the AI has
        /// no intel yet. Returns false (blocks the build order) until at least
        /// <paramref name="minUnits"/> idle units are available, while the
        /// posture forbids attacking, or while the chosen assault target needs
        /// a recon pass first (scout-then-strike, M3).
        /// </summary>
        /// <summary><paramref name="launchedSize"/> reports how many bodies
        /// actually marched. THE LOG USED TO CARRY ONLY THE THRESHOLD, so
        /// every readout of "wave size" in this project was really a readout
        /// of the launch bar, and a wave of 40 and a wave of 4 logged the same
        /// number whenever the bar was 4 (operator question, 2026-09-12:
        /// "what does wave size 6 mean?" -- it meant nothing about the wave).
        /// </summary>
        /// <summary>
        /// The best known estimate of what defends <paramref name="pos"/>
        /// (Game_AI.md 6a, the strength-gated wave): the larger of the live
        /// read (hostile army + static defences in the assess radius,
        /// AIEngagement) and what the scouts reported there -- the strongest
        /// garrison recorded around a sighted building, or the sum of the
        /// mobile sightings, whichever is larger -- plus the static defences.
        /// Mobile sightings older than opportunityMaxAgeSeconds no longer
        /// describe the place and are ignored. Pure reads, no iteration order
        /// dependence (the buffer is walked in index order).
        /// </summary>
        private static int KnownDefenceAt(EntityManager em, Entity brainEntity, Faction faction,
            float3 pos, in EngagementAssessment live)
        {
            int best = live.EnemyPower;
            if (!em.HasBuffer<EnemySightingRecord>(brainEntity)) return best;
            var sbuf = em.GetBuffer<EnemySightingRecord>(brainEntity);
            float simNow = (float)TheWaningBorder.Core.SimClock.Now;
            float r = AIEngagement.DefaultAssessRadius;
            float r2 = r * r;
            int garrison = 0, mobile = 0;
            for (int i = 0; i < sbuf.Length; i++)
            {
                var s = sbuf[i];
                if (!Alliances.AreHostile(faction, s.OwnerFaction)) continue;
                float dx = s.Position.x - pos.x, dz = s.Position.z - pos.z;
                if (dx * dx + dz * dz > r2) continue;
                if (s.Category == IntelCategory.MilitaryUnit)
                {
                    if (simNow - s.LastSeenTime <= Cfg.opportunityMaxAgeSeconds)
                        mobile += math.max(0, s.EstStrength);
                }
                else if ((s.Category == IntelCategory.Hall
                          || s.Category == IntelCategory.MilitaryBuilding
                          || s.Category == IntelCategory.EcoBuilding)
                         && s.EstStrength > garrison)
                    garrison = s.EstStrength;   // a building's tally IS its garrison
            }
            int scouted = math.max(garrison, mobile) + live.EnemyStaticPower;
            return math.max(best, scouted);
        }

        /// <param name="strengthGate">Past strengthWaveAfterSeconds the wave
        /// launches only when the army's power reaches strengthWaveRatio x the
        /// best known defence at the objective, overdue or not (Game_AI.md
        /// 6a).</param>
        private bool TryLaunchAttack(EntityManager em, Entity brainEntity, Faction faction, int minUnits,
            bool strengthGate, ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now, out int launchedSize)
        {
            launchedSize = 0;
            // TUTORIAL: the AI never takes the offensive. The tutorial is a
            // real match on the shipped map, so without this the coach was
            // walking a first-time player through worker allocation while a
            // full attack wave arrived — they lose the base before reaching
            // the chapter that explains soldiers.
            //
            // Only OFFENSIVE missions are suppressed. The AI still builds,
            // researches and defends itself, so the "Take the fight out" step
            // has a real enemy that fights back when the player attacks it.
            if (GameSettings.TutorialActive) return false;

            // AGE 0 IS FOR THE AGE-UP (2026-10-02, docs/Design/Age_0.md § The
            // AI and the age-up): before it has aged up, only an Aggressive or
            // Rush AI attacks, and only ONCE — then it saves for its landmark
            // like everyone else.
            if (!HasAgedUp(em, faction))
            {
                var p = em.GetComponentData<AIBrain>(brainEntity).Personality;
                if (!AttacksInAge0(p) || aiState.WaveNumber >= 1) return false;
            }

            // Difficulty knob: no offensive missions before the tier's first-
            // attack time (AoE4: first Hardest attack ≈ 8 min, later on lower
            // tiers). Defense (posture engine) is unaffected.
            if (now < profile.FirstAttackEarliestSeconds) return false;

            // Only DEFEND holds the army home. Rebuild used to hold too, and
            // it is the batch-proven hour-long wave freeze: Rebuild means
            // "army below desired", and with desired armies of 90-160 that is
            // the PERMANENT state of a healthy faction — Red (the rusher!)
            // logged "wave 1 BLOCKED, posture Rebuild" from 360 s to 1624 s
            // while 50+ soldiers stood at home. Relentless cadence directive
            // (2026-08-30): a wave launches with what exists; rebuilding
            // happens behind it, not instead of it.
            // A wave is OVERDUE when none has launched for a full cadence
            // window. Overdue waves stop being choosy: the engagement and
            // recon holds below are quality filters, and a filter that holds
            // the army past the cadence IS the "armies trained but never
            // used" bug, so an overdue wave attacks the best target it has.
            bool overdue = now - aiState.WaveStartTime > Cfg.waveOverdueSeconds;

            // DEFEND HOLDS THE ARMY HOME — BUT NOT FOREVER (2026-08-31
            // balance investigation). Under sustained raiding a victim sits
            // in Defend permanently, and a permanent wave veto is the
            // passive-victim loop: Green logged ZERO waves in eight matches
            // while being farmed for 242 kills. Past the overdue window the
            // veto yields — a counter-raid at the raider's own ground is
            // usually the best defence there is.
            if (aiState.Posture == AIPosture.Defend && !overdue)
                return false;

            // Pressure posture commits with a slightly smaller wave.
            if (aiState.Posture == AIPosture.Pressure)
                minUnits = math.max(2, minUnits - 1);

            // Need a Hall to know where the army is staging from (used as the
            // "origin" for picking the closest enemy). If no Hall exists we
            // can't pick a target meaningfully — fail silently.
            Entity myHall = FindFactionBuilding<HallTag>(em, faction);
            if (myHall == Entity.Null) return false;
            if (!em.HasComponent<LocalTransform>(myHall)) return false;
            float3 originPos = em.GetComponentData<LocalTransform>(myHall).Position;

            // Find idle military: any UnitTag with a combat class, this faction,
            // no active commands, not currently a battalion *member* (members
            // follow their leader; we issue to the leader only).
            var militaryQuery = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = militaryQuery.ToEntityArray(Allocator.Temp);
            using var tags = militaryQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = militaryQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Units already enrolled in a living mission are never re-drafted.
            var enrolled = _scratchEnrolled;   // pooled, cleared per use
            enrolled.Clear();
            foreach (var m in MissionsFor(faction))
                foreach (var member in m.Members)
                    enrolled.Add(member);

            var idleMilitary = new System.Collections.Generic.List<Entity>();
            // STANDING ARMY (Game_AI.md 6a): every draftable combat unit not
            // already serving a mission — fighting at home and walking
            // included. The tier's standing floor is kept out of it.
            int standing = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;   // ritualists are not army
                if (IsClaimSquadMember(e)) continue;   // holding ground for a claim
                // Uncommandable bodies (Feraldis House Raiders) cannot be sent
                // anywhere — FeraldisRaiderPatrolSystem owns them and
                // overrides any order the same frame. Drafting them inflates
                // the wave's apparent strength with units that never march.
                if (em.HasComponent<NotControllableTag>(e)) continue;
                // THE MISSION ROSTER IS THE AUTHORITY ON WHO IS BUSY
                // (2026-09-12). This used to exclude anything carrying a move
                // order of any kind, and that quietly starved every wave:
                //
                //   * a mission that times out sends its whole army home with
                //     a formation ATTACK-MOVE, so the release path re-tagged
                //     every member and kept them ineligible for the length of
                //     the walk home -- on Veilmarch, minutes;
                //   * ordinary AI repositioning sets MoveCommand, so a unit
                //     walking anywhere at all was unavailable.
                //
                // Yellow had 137 units and could not find the EIGHT idle it
                // needed to launch. Moving is not busy. Fighting is busy, and
                // a player's own order is untouchable; everything else is a
                // body this brain already owns and may re-task.
                if (enrolled.Contains(e)) continue;          // serving already
                standing++;
                if (TransientState.Active<AttackCommand>(em, e)) continue;  // in a fight
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;  // player's
                idleMilitary.Add(e);
            }

            // A WAVE IS A PUSH, NOT A FEEDER (2026-10-05, Game_AI.md 6a): on
            // a tier with waveMinArmyFraction the wave is at least that share
            // of the standing army, so frequent waves stay real pushes.
            if (profile.WaveMinArmyFraction > 0f)
                minUnits = math.max(minUnits,
                    (int)math.ceil(standing * math.saturate(profile.WaveMinArmyFraction)));

            // THE STANDING FLOOR IS NEVER DRAFTED (2026-10-05, developer:
            // "Having less time between attacks in higher difficulties should
            // not come at the cost of army count"). A wave draws only the
            // surplus above the tier's standing floor; the units nearest home
            // stay.
            int floor = StandingArmyFloor(faction, profile, aiState.DesiredMilitary);
            _lastFloorKept = 0;
            _lastFloor = floor;
            if (floor > 0)
            {
                int canDraft = math.max(0, standing - floor);
                if (idleMilitary.Count > canDraft)
                {
                    _lastFloorKept = idleMilitary.Count - canDraft;
                    // Farthest from home first; the tail (nearest home) stays.
                    var dist = new System.Collections.Generic.Dictionary<Entity, float>(idleMilitary.Count);
                    for (int i = 0; i < idleMilitary.Count; i++)
                    {
                        var u = idleMilitary[i];
                        dist[u] = em.HasComponent<LocalTransform>(u)
                            ? math.distancesq(em.GetComponentData<LocalTransform>(u).Position, originPos) : 0f;
                    }
                    idleMilitary.Sort((a, b) =>
                    {
                        int c = dist[b].CompareTo(dist[a]);
                        return c != 0 ? c : a.Index.CompareTo(b.Index);
                    });
                    idleMilitary.RemoveRange(canDraft, _lastFloorKept);
                }
            }

            if (idleMilitary.Count < minUnits) return false; // wait for the army

            // M2: scored target selection from intel; legacy ladder fallback.
            Entity target = ChooseAttackTargetScored(
                em, brainEntity, faction, originPos, settings, personality, now,
                out float intelAge, out IntelCategory category);
            bool scored = target != Entity.Null;

            // Wells are never plain-army targets (2026-07-12): the culture's
            // ritualist works them with the army as escort (per-culture
            // endgame systems). A scored BorderNode pick is discarded here —
            // raw waves at wells only fed the crystal spread.
            if (scored && category == IntelCategory.BorderNode)
            {
                target = Entity.Null;
                scored = false;
            }

            if (!scored)
                target = ChooseAttackTarget(em, faction, originPos);
            float3 targetPos = target != Entity.Null
                && em.HasComponent<LocalTransform>(target)
                ? em.GetComponentData<LocalTransform>(target).Position
                : default;

            // GANG UP ON THE LEADER (2026-08-31, equal-win-rate directive).
            // Attack-the-nearest let the strongest faction snowball
            // uncontested: Red won six of the first seven decided matches
            // while its victims fought whoever happened to be adjacent. The
            // wave's designated victim is now the STRONGEST hostile faction
            // — territory weighs heaviest, because territory is the economy
            // — which makes every lead a magnet for pressure from the whole
            // table. The starve doctrine below then picks WHICH of the
            // leader's holdings to hit.
            // …but ONLY against a REAL lead, and never in the closeout
            // (2026-08-31 rev.2). Unconditional gang-up produced 12 quits in
            // 12 matches: any marginal leader absorbed three-way pressure and
            // dropped back, a rubber band that stabilised the table into
            // permanent war. Now: a faction 35%+ ahead of second place draws
            // the table's aggression; otherwise waves fight their own wars —
            // and past the closeout time they hunt the WEAKEST instead, so
            // late games converge on eliminations rather than equilibrium.
            bool sightingObjective = false;

            // OPPORTUNITY OUTRANKS DOCTRINE (2026-08-31 directive: "if they
            // see a stray Hall or resource building it should be targeted").
            // Sightings carry VALUE (category) and STRENGTH (the garrison
            // around a building when it was seen); a high-value report with
            // next to no defense is taken the moment it is known — fresh
            // reports only, since a stray report going on four minutes old
            // describes a base that may have grown teeth.
            // Closeout state is computed FIRST because it gates the
            // opportunity hijack below (2026-09-03): a finishing push must
            // not be diverted to a stray eco hut — in the logged 51-minute
            // match, exactly ONE of Blue's 8 launches used closeout doctrine
            // because the opportunity block ran first and overwrote it.
            int hostiles = 0;
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f != faction && Alliances.AreHostile(faction, f)
                    && FindFactionBuilding<HallTag>(em, f) != Entity.Null)
                    hostiles++;
            }
            bool closeout = hostiles <= 1 || now > Cfg.closeoutAfterSeconds;

            bool opportunity = false;
            if (!closeout && em.HasBuffer<EnemySightingRecord>(brainEntity))
            {
                var sbuf = em.GetBuffer<EnemySightingRecord>(brainEntity);
                float simNow = (float)TheWaningBorder.Core.SimClock.Now;
                int bestVal = 0; float bestD2 = float.MaxValue;
                EnemySightingRecord pick = default;
                for (int i = 0; i < sbuf.Length; i++)
                {
                    var s = sbuf[i];
                    if (!Alliances.AreHostile(faction, s.OwnerFaction)) continue;
                    int val = s.Category == IntelCategory.Hall ? 2
                            : s.Category == IntelCategory.EcoBuilding ? 1 : 0;
                    if (val == 0) continue;
                    if (s.EstStrength > Cfg.strayDefenseMax) continue;
                    if (simNow - s.LastSeenTime > Cfg.opportunityMaxAgeSeconds) continue;
                    float dx = s.Position.x - originPos.x, dz = s.Position.z - originPos.z;
                    float d2 = dx * dx + dz * dz;
                    if (val < bestVal || (val == bestVal && d2 >= bestD2)) continue;
                    bestVal = val; bestD2 = d2; pick = s;
                }
                if (bestVal > 0)
                {
                    target = (pick.Enemy != Entity.Null && em.Exists(pick.Enemy))
                        ? pick.Enemy : Entity.Null;
                    targetPos = pick.Position;
                    scored = false;
                    sightingObjective = true;
                    opportunity = true;
                    _lastDoctrine = $"opportunity: stray " +
                        (pick.Category == IntelCategory.Hall ? "capital" : "eco building") +
                        $" of {pick.OwnerFaction}, garrison {pick.EstStrength}, " +
                        $"sighted {(int)(simNow - pick.LastSeenTime)}s ago";
                }
            }

            Faction waveVictim = faction;   // the wave's designated victim (6f sisters)
            if (!opportunity)
            {
                // A DUEL IS ALWAYS THE CLOSEOUT (2026-08-31 rev.3): with one
                // hostile side left there is nobody else to police, and the
                // clock threshold alone armed too late under batch CPU
                // contention (the AI clock runs slow when six matches share
                // a machine) — 11 of 12 matches quit with duels unresolved.
                // (hostiles/closeout computed above the opportunity block.)
                _lastDoctrine = "own war (no real lead)";
                Faction victim = closeout
                    ? WeakestHostileFaction(em, faction)
                    : LeadingHostileFaction(em, faction);
                waveVictim = victim;
                if (victim != faction)
                {
                    // The KNOWN world only: the victim's Hall as the scouts
                    // last reported it. No sighting means the scouts owe us
                    // intel before the army owes anyone a march.
                    // THE HOLDINGS OUTSIDE THE WALLS FIRST (2026-10-05,
                    // Game_AI.md 6a). Every capital on a developed board sits
                    // behind 150-240 wall pieces and the Wall Rule keeps
                    // infantry off them, so a wave sent at a Hall stood under
                    // the towers until its clock ran out (M6: 87 units, 480 s,
                    // 3 kills). The victim's eco and military buildings on
                    // ground without its capital are what pay for that wall;
                    // they are reachable, and the raze chain presses on from
                    // there. The Hall is the objective only when nothing else
                    // of the victim's is known.
                    if (TryNearestUnwalledSighting(em, brainEntity, faction, victim, originPos, now,
                            out float3 uPos, out Entity uEnt, out string uWhat))
                    {
                        target = uEnt;
                        targetPos = uPos;
                        scored = false;
                        sightingObjective = true;
                        _lastDoctrine = (closeout
                            ? $"closeout: {victim} is weakest"
                            : $"{victim} leads the board")
                            + $", {uWhat} outside its walls";
                    }
                    else if (TryNearestHallSighting(em, brainEntity, victim, originPos,
                            out float3 sPos, out float sAge, out Entity sEnt))
                    {
                        target = sEnt;          // may be Null: location march
                        targetPos = sPos;
                        scored = false;
                        sightingObjective = true;
                        _lastDoctrine = (closeout
                            ? $"closeout: {victim} is weakest"
                            : $"{victim} leads the board")
                            + $", Hall sighted {(int)sAge}s ago";
                    }
                    else if (closeout)
                    {
                        // FINISH ANYWAY (2026-09-03). In the closeout, "no
                        // sighting" used to fall through to the generic target
                        // ladder — whose first rung is enemy WORKERS — so the
                        // "kill the weakest" wave marched at a random midfield
                        // worker while Blue logged "want Red but no Hall
                        // sighting — scouts first" seven times and never
                        // finished a 135-vs-29 board. Start positions are
                        // public knowledge (the start-hall fallback already
                        // exists for exactly this), so the killing push goes
                        // to the nearest hostile start Hall.
                        Entity sh = FindEnemyStartHall(em, faction, originPos);
                        if (sh != Entity.Null && em.HasComponent<LocalTransform>(sh))
                        {
                            target = sh;
                            targetPos = em.GetComponentData<LocalTransform>(sh).Position;
                            scored = false;
                            sightingObjective = true;
                            _lastDoctrine = $"closeout: no sighting of {victim} — " +
                                "marching on the nearest hostile start Hall";
                        }
                        else
                        {
                            aiState.HasReconRequest = 1;
                            aiState.ReconTarget = originPos;
                            AILogger.Log(faction, "WAVE",
                                $"closeout wants {victim} but no Hall stands at any start — scouts first");
                        }
                    }
                    else
                    {
                        aiState.HasReconRequest = 1;
                        aiState.ReconTarget = originPos;   // director picks the zone
                        AILogger.Log(faction, "WAVE",
                            $"want {victim} but no Hall sighting — scouts first");
                    }
                }

                // THE ENEMY'S INCOME, NOT THE NEAREST THING (2026-10-05,
                // Game_AI.md 6f). On an income-targeting tier the main
                // objective is the most valuable known income building of
                // the victim (any hostile when nobody leads) — an extractor
                // outside its walls before a house, a house before a march
                // on the capital. The late-game finishing doctrine (past
                // closeoutAfterSeconds) keeps precedence; the hostile-count
                // closeout does NOT, because a duel is "closeout" from the
                // first second and the smoke match showed Expert marching
                // blind on the start Hall for 25 minutes with the doctrine
                // never firing. The opportunity hijack above is already an
                // income strike.
                if (profile.IncomeTargeting && now <= Cfg.closeoutAfterSeconds)
                {
                    RankIncomeTargets(em, brainEntity, faction, victim, originPos, now,
                        personality != null ? personality.riskMultiplier : 1f, settings, _scratchIncome);
                    if (_scratchIncome.Count > 0)
                    {
                        var top = _scratchIncome[0];
                        target = top.Ent;
                        targetPos = top.Pos;
                        scored = false;
                        sightingObjective = true;
                        _lastDoctrine = $"income: {top.Owner}'s {top.What}" +
                            (victim != faction ? $" ({victim} leads the board)" : "");
                    }
                }
            }

            // (Starve-then-storm now emerges from the sighting picker: the
            // NEAREST known Hall of the victim is naturally the border
            // expansion, so waves eat outward income first without any
            // omniscient expansion lookup — 2026-08-31 intel-flow rev.)

            // ── CAN WE ACTUALLY WIN THERE? ──────────────────────────────
            // The wave gate above is a COUNT ("do I have minUnits idle"),
            // which says nothing about what is waiting. Assess the objective:
            // hostile army AND hostile buildings, because a Hall is a
            // multi-target gun on 2400 HP and used to score zero
            // (AIEngagement). Refusing here keeps the army home to grow
            // instead of feeding it in piecemeal — the "attacks next to the
            // enemy Hall and is always outnumbered" report, 2026-08-18.
            // Nothing scored, nothing sighted: there is no war to march to.
            if (target == Entity.Null && !sightingObjective) return false;

            // NO BLIND DISPATCH (2026-08-31 directive). Whatever picked the
            // objective, the army does not march on ground nobody has seen:
            // the wave converts into a recon request instead, and the scout
            // director earns the intel first.
            //
            // ...BUT NEVER FOR LONG (2026-10-05, Game_AI.md 6a). Yellow, the
            // last strong side on SunderedCrown, held 193 units at home for
            // the rest of the match logging "holding — no intel on (75,75)"
            // every think: the scouts could not get through the curse to the
            // last enemy's START position — ground every player knows. A
            // start position is public knowledge, so the wave advances at
            // once; any other unseen objective holds for recon at most
            // waveIntelHoldMaxSeconds, then the army advances itself and
            // fights what it meets on the way (the tactical layer engages
            // the curse like anyone else). Recon keeps running alongside.
            bool blindAdvance = false;
            if (!IsKnownGround(faction, targetPos))
            {
                aiState.ReconTarget = targetPos;
                aiState.HasReconRequest = 1;
                bool publicStart = IsStartPosition(targetPos);
                float heldFor = NoteWaveIntelHold(faction, targetPos, now);
                if (!publicStart && heldFor < Cfg.waveIntelHoldMaxSeconds)
                {
                    AILogger.Log(faction, "WAVE",
                        $"holding — no intel on ({targetPos.x:0},{targetPos.z:0}); recon requested");
                    return false;
                }
                blindAdvance = true;
                AILogger.Log(faction, "WAVE",
                    $"advancing without intel on ({targetPos.x:0},{targetPos.z:0}) after {(int)heldFor}s" +
                    (publicStart ? " (a start position: public knowledge)" : " (recon timed out)"));
            }
            else _waveIntelHold.Remove((int)faction);

            var assault = AIEngagement.AssessAssault(em, faction, idleMilitary, targetPos);

            // THE LATE WAVE IS A STRENGTH TEST (2026-10-04, Game_AI.md 6a).
            // Past strengthWaveAfterSeconds the army goes when its power beats
            // the best known defence by strengthWaveRatio (scaled by the
            // personality's riskMultiplier, the same appetite for risk the
            // target scorer reads) -- and not before, overdue or not.
            if (strengthGate)
            {
                int known = KnownDefenceAt(em, brainEntity, faction, targetPos, assault);
                // ...and by the tier's strengthWaveRatioScale (Game_AI.md § 2):
                // a slow tier waits for a bigger edge, Expert goes sooner.
                float ratio = Cfg.strengthWaveRatio
                              * math.max(0.1f, ProfileOf(faction).StrengthWaveRatioScale)
                              * math.max(0.1f, personality != null ? personality.riskMultiplier : 1f);
                float need = known * ratio;
                // An army at the population ceiling cannot grow into the
                // ratio, so it goes with everything it has -- what the old
                // full-population rule asked for, kept as the release valve.
                PopulationHelper.TryGetFactionPopulation(faction, out int sgPop, out int sgMax);
                bool atCeiling = sgMax > 0 && sgPop >= sgMax;
                if (assault.MyPower < need && !atCeiling)
                {
                    // At least as large as the defence demands, so the army
                    // the faction keeps growing is the one that can win.
                    int perUnit = math.max(1, assault.MyPower / math.max(1, idleMilitary.Count));
                    int want = CountAliveMilitary(em, faction)
                               + math.max(1, (int)math.ceil((need - assault.MyPower) / perUnit));
                    // ...never past the tier's army cap (Game_AI.md 2): this
                    // is how Easy's "cap of 60" read 83 in the v16 pair.
                    want = math.min(want, math.max(1, profile.SustainArmyCap));
                    if (aiState.DesiredMilitary < want) aiState.DesiredMilitary = want;
                    if ((int)(now / 120f) != (int)((now - Cfg.waveRetrySeconds) / 120f))
                        AILogger.Log(faction, "WAVE",
                            $"wave {aiState.WaveNumber + 1} HELD at {(int)now}s — strength " +
                            $"{assault.MyPower} ({idleMilitary.Count} idle) vs known defence {known} " +
                            $"at ({targetPos.x:0},{targetPos.z:0}) needs x{ratio:0.00} = {need:0}");
                    return false;
                }
                AILogger.Log(faction, "WAVE", assault.MyPower >= need
                    ? $"strength {assault.MyPower} beats known defence {known} x{ratio:0.00} — launching"
                    : $"strength {assault.MyPower} short of {need:0} but population is full " +
                      $"({sgPop}/{sgMax}) — launching with everything");
            }
            else if (target != Entity.Null && !assault.ShouldFight && !overdue)
            {
                AILogger.Log(faction, "WAVE",
                    $"hold — assault at ({targetPos.x:0},{targetPos.z:0}) unfavourable: " +
                    $"mine {assault.MyPower} vs {assault.EnemyPower} " +
                    $"(army {assault.EnemyMobilePower} + defences {assault.EnemyStaticPower}), " +
                    $"ratio {assault.Ratio:0.00}");
                return false;
            }
            else if (!assault.ShouldFight && Cfg.waveOverdueMaxRatio > 0f && assault.Ratio > Cfg.waveOverdueMaxRatio)
            {
                // OVERDUE IS NOT SUICIDAL (2026-10-05, Game_AI.md 6a). The
                // overdue release used to override the assessment outright:
                // Expert sent its first wave, 13 units, into a base it had
                // read at 1,790 power against its 701 (ratio 2.34) and lost
                // the lot at minute 15. Past the cap the wave keeps holding
                // and the army keeps growing; the strength gate takes over
                // past strengthWaveAfterSeconds.
                AILogger.Log(faction, "WAVE",
                    $"overdue, but the assault at ({targetPos.x:0},{targetPos.z:0}) reads {assault.Ratio:0.00} " +
                    $"against, past the overdue cap {Cfg.waveOverdueMaxRatio:0.00} — holding");
                return false;
            }
            else if (!assault.ShouldFight)
                AILogger.Log(faction, "WAVE",
                    $"overdue — attacking anyway at ratio {assault.Ratio:0.00} " +
                    $"(cadence beats caution past {(int)Cfg.waveOverdueSeconds}s)");

            // PURSUE THE CURSE (2026-08-04): when the corridor to the enemy
            // is buried under deep crust, a wave dies mid-field without ever
            // fighting (log-proven stall — both armies bleeding out between
            // the bases). The blocking anchor IS the objective — but the
            // anchor an army can actually KILL depends on culture: only
            // Feraldis breaks wells (rev.2 same day: wells are untargetable
            // for everyone else); Age 0 / Alanthor / Runai waves reroute
            // onto the nearest live SmallNode instead — its death collapses
            // its held crust (the tether) all the same.
            bool rerouted = false;
            if (CurseBlocksCorridor(em, originPos, targetPos))
            {
                float3 mid = (originPos + targetPos) * 0.5f;
                Entity anchor;
                float3 anchorPos;
                if (IsFeraldisCulture(em, faction))
                    anchor = FindNearestActiveWell(em, mid, out anchorPos);
                else
                    anchor = FindNearestSmallNode(em, faction, mid, out anchorPos);
                if (anchor != Entity.Null)
                {
                    target = anchor;
                    targetPos = anchorPos;
                    scored = false;
                    rerouted = true;
                    AILogger.Log(faction, "WAVE",
                        $"corridor cursed — wave rerouted to the curse anchor at ({anchorPos.x:0},{anchorPos.z:0})");
                }
            }

            // M3 scout-then-strike: assault targets (halls / military
            // buildings) with stale intel get a recon pass first — march in
            // blind and the army may walk into a fresh garrison.
            // ANTI-STAGNATION: only when a living scout exists to serve the
            // request — with all scouts dead this gate deadlocked the build
            // order at its LaunchAttack step forever.
            if (scored && !overdue && !blindAdvance
                && (category == IntelCategory.Hall || category == IntelCategory.MilitaryBuilding)
                && intelAge > settings.reconMaxIntelAge
                && CountScouts(em, faction) > 0)
            {
                aiState.ReconTarget = targetPos;
                aiState.HasReconRequest = 1;
                return false;
            }

            // ── Raid split (AoE4-style harass encounter) ──
            // With enough surplus beyond the wave threshold, peel off the
            // fastest few units as a raid party aimed at the enemy ECONOMY
            // (workers / eco buildings) while the main army takes the scored
            // objective. Two simultaneous pressure points instead of one blob.
            var missions = MissionsFor(faction);
            if (!rerouted && personality.raidingEnabled
                && idleMilitary.Count >= minUnits + Cfg.raidPartySize + Cfg.raidSurplus)
            {
                Entity raidTarget = ChooseAttackTargetScored(
                    em, brainEntity, faction, originPos, settings, personality, now,
                    out _, out _, ecoOnly: true);
                if (raidTarget != Entity.Null && raidTarget != target
                    && em.HasComponent<LocalTransform>(raidTarget))
                {
                    // Fastest units make the raid party.
                    idleMilitary.Sort((a, b) =>
                    {
                        float sa = em.HasComponent<MoveSpeed>(a) ? em.GetComponentData<MoveSpeed>(a).Value : 0f;
                        float sb = em.HasComponent<MoveSpeed>(b) ? em.GetComponentData<MoveSpeed>(b).Value : 0f;
                        return sb.CompareTo(sa);
                    });
                    float3 raidPos = em.GetComponentData<LocalTransform>(raidTarget).Position;
                    var raid = new Mission
                    {
                        Type = MissionType.Raid,
                        Target = raidTarget,
                        TargetPos = raidPos,
                        StartTime = now,
                    };
                    for (int i = 0; i < Cfg.raidPartySize && idleMilitary.Count > 0; i++)
                    {
                        Entity u = idleMilitary[0];
                        idleMilitary.RemoveAt(0);
                        raid.Members.Add(u);
                    }
                    // One formation, not a per-unit stream (AICommon.IssueGroupOrder).
                    AICommon.IssueGroupOrder(em, raid.Members, raidPos, attackMove: true);
                    missions.Add(raid);
                }
            }

            // ── Main attack mission(s) ──
            // The army marches through the FORMATION pipeline (virtual leader,
            // type-ranked slots, slowest-member speed) — the same machinery
            // player group orders use (DispatchArmy).
            //
            // MANY ARMIES, MANY DIRECTIONS (2026-10-05, Game_AI.md 6f). A tier
            // with concurrentArmies above one splits the draft into that many
            // bodies: the main army takes the objective chosen above, each
            // sister army an income objective of the same victim on its own
            // approach bearing (PickSisterTargets), and the group stages,
            // then strikes together (GroupReadyToStrike). The units nearest
            // each objective form its army. Fewer armies launch when the
            // draft cannot give each armyMinUnits, or when no second
            // objective separates enough from the first.
            var sisters = new System.Collections.Generic.List<IncomeTarget>();
            int armies = math.max(1, profile.ConcurrentArmies);
            if (armies > 1 && !rerouted
                && idleMilitary.Count / armies >= math.max(1, Cfg.armyMinUnits))
                PickSisterTargets(em, brainEntity, faction, originPos, target, targetPos, waveVictim, now,
                    idleMilitary, armies, armies - 1,
                    personality != null ? personality.riskMultiplier : 1f, settings, sisters);
            int bodies = 1 + sisters.Count;
            int group = bodies > 1 ? ++_nextArmyGroup : 0;
            int share = idleMilitary.Count / bodies;

            var pool = new System.Collections.Generic.List<Entity>(idleMilitary);
            var launched = new System.Collections.Generic.List<Mission>(bodies);
            for (int k = 0; k < sisters.Count; k++)
            {
                var t = sisters[k];
                float3 tp = t.Pos;
                pool.Sort((a, b) =>
                {
                    float da = em.HasComponent<LocalTransform>(a)
                        ? math.distancesq(em.GetComponentData<LocalTransform>(a).Position, tp) : float.MaxValue;
                    float db = em.HasComponent<LocalTransform>(b)
                        ? math.distancesq(em.GetComponentData<LocalTransform>(b).Position, tp) : float.MaxValue;
                    int c = da.CompareTo(db);
                    return c != 0 ? c : a.Index.CompareTo(b.Index);
                });
                var sister = new Mission
                {
                    Type = MissionType.Attack,
                    Target = t.Ent,
                    TargetPos = t.Pos,
                    StartTime = now,
                    Group = group,
                };
                for (int i = 0; i < share && pool.Count > 0; i++)
                {
                    sister.Members.Add(pool[0]);
                    pool.RemoveAt(0);
                }
                launched.Add(sister);
            }
            var attack = new Mission
            {
                Type = MissionType.Attack,
                Target = target,
                TargetPos = targetPos,
                StartTime = now,
                Group = group,
                Primary = true,
            };
            attack.Members.AddRange(pool);
            launched.Add(attack);
            launchedSize = idleMilitary.Count;
            if (_lastFloor > 0)
                AILogger.Log(faction, "WAVE",
                    $"standing floor {_lastFloor} kept ({standing - launchedSize} standing at home, " +
                    $"{_lastFloorKept} idle held back; wave {launchedSize}, min {minUnits})");
            if (bodies > 1)
                AILogger.Log(faction, "WAVE",
                    $"wave of {bodies} armies against {waveVictim}: " +
                    string.Join(", ", sisters.ConvertAll(s => $"{s.What} at ({s.Pos.x:0},{s.Pos.z:0})")) +
                    $" and the main objective at ({targetPos.x:0},{targetPos.z:0})");

            // The main army is dispatched LAST so it sits after its sisters
            // in the list; columns come later still (TryMergeIntoArmy).
            for (int k = 0; k < launched.Count; k++)
            {
                var a = launched[k];
                string doctrine = a.Primary ? _lastDoctrine
                    : $"income: {sisters[k].Owner}'s {sisters[k].What}, a second direction";
                DispatchArmy(em, faction, a, originPos, now, doctrine, bodies);
            }

            // Remember where this wave went so newly-finished units can be
            // fed into it (ReinforceActiveWave). Without this a wave was a
            // one-shot: everything trained after launch stood in the base
            // until the NEXT wave's larger minimum was met, so armies
            // trickled away at the front while reinforcements idled at home.
            // WHILE STAGING, reinforcements rally to the STAGE POINT — sent
            // at the objective they attack-moved straight past the forming
            // army into the enemy alone. The staging commit repoints this.
            // The MAIN army's objective (6f): sisters never steer the stream.
            aiState.WaveTarget = attack.Phase == MissionPhase.Mustering
                ? attack.StagePos : targetPos;
            aiState.WaveActive = 1;
            aiState.WaveStartTime = now;

            return true;
        }






        /// <summary>
        /// Feed idle military into the wave that is already out.
        ///
        /// A wave used to be a single draft: units finished after it left had
        /// no way to join, so the front thinned while fresh troops stood at
        /// home waiting for a next wave whose minimum kept GROWING
        /// (WaveBaseUnits + WaveNumber * WaveGrowthUnits). That is the shape
        /// of "wave N BLOCKED, posture Rebuild" repeating for 18 minutes.
        ///
        /// The wave stays reinforceable until nothing of ours is still
        /// attack-moving — at which point it is over, won or lost.
        /// </summary>
        /// <summary>The endgame commitment mode: one hostile left standing,
        /// or the match has run past the closeout clock. Shared by the wave
        /// target picker (skip opportunity hijacks, start-hall fallback) and
        /// the wave lifetime leash.</summary>
        private bool CloseoutArmed(EntityManager em, Faction faction, float now)
        {
            if (now > Cfg.closeoutAfterSeconds) return true;
            int hostiles = 0;
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f != faction && Alliances.AreHostile(faction, f)
                    && FindFactionBuilding<HallTag>(em, f) != Entity.Null)
                    hostiles++;
            }
            return hostiles <= 1;
        }

        private void ReinforceActiveWave(EntityManager em, Faction faction,
            ref SimpleAIState aiState, float now)
        {
            if (aiState.WaveActive == 0) return;
            if (now < aiState.NextReinforceTime) return;
            aiState.NextReinforceTime = now + Cfg.reinforceInterval;

            // Age out a wave that has overstayed its welcome, whatever its
            // members are doing. Releasing the army is what lets the NEXT wave
            // draft it — a wave that never retires starves every wave after it.
            //
            // NOT DURING THE CLOSEOUT (2026-09-03): the wall clock retired a
            // WINNING wave mid-siege (Blue's wave 6 went "SPENT (lifetime)" at
            // 43:28 with the enemy base half-razed; the next launch had to
            // re-clear the 11-idle bar, 64 s of zero pressure). A killing push
            // gets double the leash instead of the axe.
            float lifetime = CloseoutArmed(em, faction, now)
                ? Cfg.waveMaxLifetime * 2f : Cfg.waveMaxLifetime;
            if (now - aiState.WaveStartTime > lifetime)
            {
                aiState.WaveActive = 0;
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} SPENT (lifetime) — army released for the next wave");
                return;
            }

            var q = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Collected, then sent as ONE formation. Issuing an attack-move
            // per unit made every reinforcement its own little war: each walked
            // the whole way alone and arrived alone, feeding the enemy army one
            // unit at a time. Reinforcement is the dispatch layer's job, so it
            // dispatches a body.
            var reinforcements = new System.Collections.Generic.List<Entity>();

            // ALREADY SERVING (2026-09-12). A column dispatched to muster
            // moves with IssueFormationMove -- a plain MOVE, which sets no
            // attack tag -- so on the very next pass those units read as idle,
            // were drafted AGAIN, counted again in `sent`, and spawned ANOTHER
            // column to the same place. Green's log is the signature:
            // "reinforced with 16" six times over with 1 committed. That was
            // never 98 reinforcements, it was the same sixteen re-drafted, each
            // draft resetting their march. The mission roster already knows who
            // is serving; ask it.
            var serving = _scratchEnrolled;    // pooled, cleared per use
            serving.Clear();
            foreach (var mission in MissionsFor(faction))
                for (int mi = 0; mi < mission.Members.Count; mi++)
                    serving.Add(mission.Members[mi]);

            int committed = 0, sent = 0, arrived = 0, standing = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                // Free bodies are not the standing army (see CountAliveMilitary).
                if (em.HasComponent<PlundererTag>(e)) continue;
                // Ritualists carry the culture verb — never draft them.
                if (IsVerbUnit(em, e)) continue;
                if (IsClaimSquadMember(e)) continue;   // holding ground for a claim
                // Nor uncommandable raiders: ordering them is a no-op that
                // still counts as "sent", which kept spent waves alive.
                if (em.HasComponent<NotControllableTag>(e)) continue;

                // ARRIVAL IS A PLACE, NOT A MOOD (2026-09-12). This test used
                // to sit BELOW the busy check, so a unit that marched in on an
                // attack-move or was fighting at the objective was counted as
                // "committed" and never as "arrived" -- `arrived` only ever
                // counted bodies standing on the target with nothing left to
                // do. Whole matches reported "0 on the objective" while the
                // battle was happening there. Count the ground first.
                float dx = xfs[i].Position.x - aiState.WaveTarget.x;
                float dz = xfs[i].Position.z - aiState.WaveTarget.z;
                bool atObjective =
                    dx * dx + dz * dz <= Cfg.waveArrivedRadius * Cfg.waveArrivedRadius;
                if (atObjective) arrived++;
                // The standing army (Game_AI.md 6a): not serving, not out at
                // the objective. The floor below is kept out of it.
                if (!atObjective && !serving.Contains(e)
                    && !TransientState.Active<UserMoveOrder>(em, e)) standing++;

                // SAME AVAILABILITY RULE AS THE FRESH-WAVE DRAFT (Game_AI.md
                // 6a): fighting is busy, a roster is busy, the player's order
                // is untouchable, and walking is none of those. An
                // AttackMoveTag with no mission behind it is an orphan -- a
                // survivor of a timed-out mission walking home -- and those
                // are precisely the bodies this reinforcement wants.
                if (TransientState.Active<AttackCommand>(em, e)) { committed++; continue; }
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                if (em.HasComponent<BuildCommand>(e)) continue;

                // Serving in a live mission: marching, mustering or fighting
                // with a body that already has orders. Drafting it again is
                // what created the duplicate columns.
                if (serving.Contains(e)) { committed++; continue; }

                // Idle and already standing on the objective: nothing left
                // here to fight, and re-ordering it would keep the wave alive
                // forever.
                if (atObjective) continue;

                reinforcements.Add(e);
                sent++;
            }

            // THE STANDING FLOOR IS NEVER SENT (2026-10-05, Game_AI.md 6a):
            // reinforcements draw only the surplus above it, like a wave.
            {
                int floor = StandingArmyFloor(faction, ProfileOf(faction), aiState.DesiredMilitary);
                int canSend = math.max(0, standing - floor);
                if (floor > 0 && reinforcements.Count > canSend)
                {
                    int kept = reinforcements.Count - canSend;
                    reinforcements.RemoveRange(canSend, kept);
                    sent = reinforcements.Count;
                    int fk = (int)faction;
                    if (AILogger.Enabled && (!_floorLogAt.TryGetValue(fk, out float at) || now >= at))
                    {
                        _floorLogAt[fk] = now + 60f;   // log cadence, not tuning
                        AILogger.Log(faction, "WAVE",
                            $"standing floor {floor} kept ({kept} held back from reinforcing wave " +
                            $"{aiState.WaveNumber}, {standing} standing)");
                    }
                }
            }

            // GATHER BEFORE MARCHING. A trickle arrives piecemeal and is
            // killed piecemeal; the same bodies arriving together survive the
            // contact. Held groups leave anyway after ReinforceMaxHold so a
            // slow trainer never strands its army at home.
            //
            // THE FLOOR IS A TARGET, NOT A TRIGGER (2026-09-03). Holding only
            // below reinforceMinGroup shipped a packet the instant the Nth
            // body existed — measured across ~90 sends in one match, 6 was the
            // modal packet and each walked the map alone, 10-12 s apart: the
            // config GUARANTEED the trickle the user reported. The hold now
            // scales with the fight it is feeding: half the committed body,
            // never less than the floor, so reinforcement arrives as a
            // company that matters at the front line it joins.
            int want = math.max(Cfg.reinforceMinGroup, committed / 2);
            if (reinforcements.Count > 0 && reinforcements.Count < want)
            {
                if (aiState.ReinforceHoldSince <= 0f) aiState.ReinforceHoldSince = now;
                if (now - aiState.ReinforceHoldSince < Cfg.reinforceMaxHold)
                {
                    AILogger.Log(faction, "WAVE",
                        $"holding {reinforcements.Count}/{want} reinforcement(s) for company " +
                        $"({now - aiState.ReinforceHoldSince:0}s of {Cfg.reinforceMaxHold:0}s)");
                    return;
                }
            }
            aiState.ReinforceHoldSince = 0f;

            if (reinforcements.Count > 0)
            {
                // A COLUMN, NOT A PACKET (2026-09-07). Reinforcements used to
                // be attack-moved from home and appended to the live
                // mission's roster. Two things went wrong: the formation plan
                // only makes members of units near the centroid, so a packet
                // drafted from five rally points walked out in single file;
                // and a column at home averaged into the front-line army's
                // centroid, so the tactical layer's cohesion recall pulled
                // BOTH halves toward the middle of the map. The column is its
                // own mission now — it musters, marches and strikes like the
                // main army — and is merged into the army it was sent to join
                // once it gets there (UpdateMissions).
                Entity myHall = FindFactionBuilding<HallTag>(em, faction);
                float3 from = myHall != Entity.Null && em.HasComponent<LocalTransform>(myHall)
                    ? em.GetComponentData<LocalTransform>(myHall).Position
                    : xfs[0].Position;
                float3 fromTarget = from - aiState.WaveTarget;
                fromTarget.y = 0f;
                float approachDist = math.length(fromTarget);

                var column = new Mission
                {
                    Type = MissionType.Attack,
                    Column = true,
                    Target = Entity.Null,
                    TargetPos = aiState.WaveTarget,
                    StartTime = now,
                    LegStartTime = now,
                    Deadline = DeadlineFor(now, from, aiState.WaveTarget),
                    LastCentroid = from,
                    StalledSince = now,
                };
                column.Members.AddRange(reinforcements);
                if (approachDist > Cfg.stagingDistance * 2f)
                {
                    column.Phase = MissionPhase.Mustering;
                    column.StagePos = aiState.WaveTarget + (fromTarget / approachDist) * Cfg.stagingDistance;
                    column.MusterPos = from - (fromTarget / approachDist) * Cfg.musterDistance;
                    CommandRouter.IssueFormationMove(
                        em, column.Members, column.MusterPos, FormationShape.Box, CommandSource.AI);
                }
                else
                {
                    column.Phase = MissionPhase.Striking;
                    CommandRouter.IssueFormationAttackMove(
                        em, column.Members, aiState.WaveTarget, FormationShape.Box, CommandSource.AI);
                }
                MissionsFor(faction).Add(column);
            }

            // Nothing marching and nothing to march: the wave is over — either
            // it arrived and cleared the objective, or it died on the way.
            // Either way the army is free and the next wave picks a FRESH
            // scored target instead of re-walking a dead one.
            if (committed == 0 && sent == 0)
            {
                aiState.WaveActive = 0;
                if (arrived > 0)
                    AILogger.Log(faction, "WAVE",
                        $"wave {aiState.WaveNumber} SPENT — {arrived} unit(s) hold the objective, " +
                        "army released for the next wave");
                return;
            }
            if (sent > 0)
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} reinforced with {sent} unit(s) " +
                    $"({committed} already committed, {arrived} on the objective)");
        }
        /// <summary>
        /// Per-think-tick mission upkeep (AoE4-style encounter lifecycle):
        ///   * prune dead/missing members; empty missions disband.
        ///   * objective destroyed -> regroup the army home (attack-move, so
        ///     it fights through) and disband.
        ///   * mission locally outmatched -> retreat THAT army only (M6),
        ///     cooldown-gated per faction.
        ///   * stale missions (timeout) disband so members become draftable.
        /// </summary>
        private void UpdateMissions(EntityManager em, Entity brainEntity, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, float now)
        {
            var missions = MissionsFor(faction);
            if (missions.Count == 0) return;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            bool hasHall = hall != Entity.Null && em.HasComponent<LocalTransform>(hall);
            float3 hallPos = hasHall ? em.GetComponentData<LocalTransform>(hall).Position : default;

            for (int m = missions.Count - 1; m >= 0; m--)
            {
                var mission = missions[m];

                // Prune dead members.
                for (int i = mission.Members.Count - 1; i >= 0; i--)
                    if (!em.Exists(mission.Members[i]))
                        mission.Members.RemoveAt(i);
                if (mission.Members.Count == 0) { missions.RemoveAt(m); continue; }

                // A location march (Target == Null) is 'down' only on
                // ARRIVAL — the point of marching at a memory is finding out
                // what is really there (2026-08-31 intel-flow directive).
                bool objectiveDown;
                if (mission.Target != Entity.Null)
                    objectiveDown = !em.Exists(mission.Target);
                else
                {
                    float adx = 0f, adz = 0f;
                    int alive = 0;
                    for (int i = 0; i < mission.Members.Count; i++)
                    {
                        if (!em.HasComponent<LocalTransform>(mission.Members[i])) continue;
                        var mp = em.GetComponentData<LocalTransform>(mission.Members[i]).Position;
                        adx += mp.x; adz += mp.z; alive++;
                    }
                    if (alive > 0) { adx /= alive; adz /= alive; }
                    float ddx = adx - mission.TargetPos.x, ddz = adz - mission.TargetPos.z;
                    objectiveDown = alive > 0 && ddx * ddx + ddz * ddz < 30f * 30f;
                }
                // A mission that never set its deadline (the posture defence)
                // keeps the flat clock.
                bool timedOut = mission.Deadline > 0f
                    ? now > mission.Deadline
                    : now - mission.StartTime > Cfg.missionTimeoutSeconds;

                // GO FOR THE THROAT (2026-08-30). A razed objective used to
                // read as "Success: regroup home" — and that single line is
                // why no batch match ever ENDED: the army killed its one
                // target building and marched home with the enemy base
                // standing open around it. Batch-proven: 1,852 deaths and 94
                // waves in six hour-long matches, zero eliminations. A won
                // assault now CHAINS: while another enemy building stands
                // near the kill, the same army presses on to it. The per-
                // mission retreat check below still pulls it out when the
                // ground turns hostile, and the chain re-arms the timeout so
                // a razing spree is never cut off mid-base.
                if (objectiveDown && !timedOut && mission.Type == MissionType.Attack)
                {
                    // On the ground and in vision now, so the local query is
                    // honest: chain onto whatever hostile building stands
                    // near the site...
                    Entity nextT = FindNearestHostileBuilding(
                        em, faction, mission.TargetPos, Cfg.razeChainRadius, out float3 nextPos);
                    // ...and an EMPTY site sends the army to the next
                    // scouted threat instead of home: march (formation
                    // move), and strike on contact via the staging commit.
                    // THE FINISHER (2026-09-05). The hour-long elimination
                    // probe razed 146 and 180 buildings off the two victims
                    // and still ended 4-alive: the chain only follows Halls
                    // and military buildings, while VictoryConditionSystem
                    // keeps a faction alive on ANY hall OR military building
                    // OR worker — so the rebuild loop (territory income ->
                    // new Hall -> razed -> repeat) never terminated. In the
                    // closeout, the chain therefore also accepts the WEAKEST
                    // victim's ECO BUILDINGS and WORKERS from the sighting
                    // buffer — deny the rebuild, then take the last lifeline.
                    // Sightings only: no omniscient worker hunt.
                    bool chainCloseout = now > Cfg.closeoutAfterSeconds;
                    Faction chainVictim = chainCloseout
                        ? WeakestHostileFaction(em, faction) : faction;
                    if (nextT == Entity.Null && em.HasBuffer<EnemySightingRecord>(brainEntity))
                    {
                        var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
                        float bd2 = float.MaxValue;
                        for (int i = 0; i < buf.Length; i++)
                        {
                            var sg = buf[i];
                            if (!Alliances.AreHostile(faction, sg.OwnerFaction)) continue;
                            if (sg.OwnerFaction == Faction.Border) continue;   // players only (above)
                            bool lifeline = sg.Category == IntelCategory.Hall
                                || sg.Category == IntelCategory.MilitaryBuilding;
                            bool finisher = chainCloseout
                                && sg.OwnerFaction == chainVictim
                                && (sg.Category == IntelCategory.EcoBuilding
                                    || sg.Category == IntelCategory.Worker);
                            if (!lifeline && !finisher) continue;
                            float sdx = sg.Position.x - mission.TargetPos.x;
                            float sdz = sg.Position.z - mission.TargetPos.z;
                            float sd2 = sdx * sdx + sdz * sdz;
                            if (sd2 < 40f * 40f || sd2 >= bd2) continue;  // not where we stand
                            bd2 = sd2;
                            nextPos = sg.Position;
                            nextT = (sg.Enemy != Entity.Null && em.Exists(sg.Enemy))
                                ? sg.Enemy : Entity.Null;
                        }
                        if (bd2 < float.MaxValue)
                        {
                            mission.Deadline = DeadlineFor(now, mission.TargetPos, nextPos);
                            mission.Breaching = false;
                            mission.StalledSince = now;
                            mission.Target = nextT;
                            mission.TargetPos = nextPos;
                            // Repoint the REINFORCEMENT stream too — it steers
                            // by aiState.WaveTarget, and leaving it on the dead
                            // objective sent 17 fresh units to a building razed
                            // 42 s earlier (log-proven), where they idled,
                            // never "arrived", and were re-dispatched forever.
                            // Only the wave's MAIN army steers it (6f).
                            if (OwnsWaveTarget(missions, mission)) aiState.WaveTarget = nextPos;
                            mission.StartTime = now;
                            mission.LegStartTime = now;
                            mission.Phase = MissionPhase.Staging;
                            AILogger.Log(faction, "WAVE",
                                $"site empty — marching to next scouted threat at ({nextPos.x:0},{nextPos.z:0})");
                            CommandRouter.IssueFormationMove(
                                em, mission.Members, nextPos, FormationShape.Box, CommandSource.AI);
                            mission.StagePos = nextPos;
                            continue;
                        }
                    }
                    if (nextT != Entity.Null)
                    {
                        mission.Deadline = DeadlineFor(now, mission.TargetPos, nextPos);
                        mission.Breaching = false;
                        mission.StalledSince = now;
                        mission.Target = nextT;
                        mission.TargetPos = nextPos;
                        // Same reinforcement repoint as the site-empty chain.
                        if (OwnsWaveTarget(missions, mission)) aiState.WaveTarget = nextPos;
                        mission.StartTime = now;
                        mission.LegStartTime = now;
                        mission.Phase = MissionPhase.Striking;
                        CommandRouter.IssueFormationAttackMove(
                            em, mission.Members, nextPos, FormationShape.Box, CommandSource.AI);
                        AILogger.Log(faction, "WAVE",
                            $"objective down — pressing on to the next building at " +
                            $"({nextPos.x:0},{nextPos.z:0})");
                        continue;
                    }
                }

                if (objectiveDown || timedOut)
                {
                    if (objectiveDown && !timedOut && mission.Type == MissionType.Attack)
                        AILogger.Log(faction, "WAVE",
                            $"no player objective — returning ({mission.Members.Count} alive, " +
                            $"last objective at ({mission.TargetPos.x:0},{mission.TargetPos.z:0}))");
                    // A TIMEOUT IS EVIDENCE, NOT JUST AN EXPIRY. Whatever was
                    // wrong with this ground -- unreachable, too well held,
                    // never actually there -- is still wrong in ten seconds,
                    // and the scorer has no other way to learn it.
                    if (timedOut && !objectiveDown)
                    {
                        var (bcx, bcz) = WaveCell(mission.TargetPos);
                        _waveBlocked[((int)faction, bcx, bcz)] = now + WaveBlockSeconds;
                        AILogger.Log(faction, "WAVE",
                            $"mission timed out at ({mission.TargetPos.x:0},{mission.TargetPos.z:0}) " +
                            $"after {now - mission.StartTime:F0}s with {mission.Members.Count} alive — " +
                            $"that ground is off the target list for {WaveBlockSeconds:F0}s");
                    }

                    // Success (or stale): regroup home and free the units for
                    // the next wave. Formation attack-move so the army marches
                    // back in shape and engages stragglers on the way.
                    if (hasHall)
                        CommandRouter.IssueFormationAttackMove(
                            em, mission.Members, hallPos, FormationShape.Box, CommandSource.AI);
                    missions.RemoveAt(m);
                    continue;
                }

                // Army centroid (used by staging commit AND the retreat check).
                float3 sum = float3.zero;
                for (int i = 0; i < mission.Members.Count; i++)
                {
                    if (!em.HasComponent<LocalTransform>(mission.Members[i])) continue;
                    sum += em.GetComponentData<LocalTransform>(mission.Members[i]).Position;
                }
                float3 centroid = sum / mission.Members.Count;

                // A reinforcement column that has reached the army it was
                // sent to join becomes part of it: one roster, one centroid,
                // one set of tactics. (Columns are the later missions in the
                // list; the army they join is an earlier one.)
                // Only a COLUMN is absorbed (6f): a sister army mustering
                // beside the main army is its own body, not a reinforcement.
                if (mission.Type == MissionType.Attack && mission.Column && m > 0
                    && TryMergeIntoArmy(em, missions, m, centroid))
                    continue;

                // MUSTER GATE (2026-09-07): the army leaves once most of it
                // stands at the muster point, or the wait runs out — one
                // straggler must not hold the wave at the gate.
                if (mission.Phase == MissionPhase.Mustering)
                {
                    float fraction = FractionWithin(em, mission, mission.MusterPos, GatherRadius(mission.Members.Count));
                    bool musterTimedOut = now - mission.LegStartTime > Cfg.musterTimeoutSeconds;
                    if (fraction >= Cfg.musterGatherFraction || musterTimedOut)
                    {
                        mission.Phase = MissionPhase.Staging;
                        mission.LegStartTime = now;
                        CommandRouter.IssueFormationMove(
                            em, mission.Members, mission.StagePos, FormationShape.Box, CommandSource.AI);
                        AILogger.Log(faction, "WAVE",
                            $"mustered {(int)(fraction * 100)}% — marching on the stage point " +
                            $"({mission.StagePos.x:0},{mission.StagePos.z:0})" +
                            (musterTimedOut ? " (muster timed out)" : ""));
                    }
                    else RegroupStragglers(em, faction, mission, now, attackMove: false);
                    continue;
                }

                // Forward-staging commit: once the army has gathered at the
                // staging point (or staging times out — stragglers must not
                // stall the push), strike the objective as one formation.
                if (mission.Phase == MissionPhase.Staging)
                {
                    float sx = centroid.x - mission.StagePos.x;
                    float sz = centroid.z - mission.StagePos.z;
                    bool gathered = sx * sx + sz * sz <= Cfg.stagingGatherRadius * Cfg.stagingGatherRadius
                        && FractionWithin(em, mission, centroid, GatherRadius(mission.Members.Count)) >= Cfg.musterGatherFraction;
                    bool stageTimedOut = now - mission.LegStartTime > Cfg.stagingTimeoutSeconds;
                    // STRIKE TOGETHER (2026-10-05, Game_AI.md 6f): a staged
                    // army of a many-army wave holds at its stage point until
                    // every sister has staged, at most armySyncTimeoutSeconds,
                    // so the victim is hit from every direction at once.
                    if (gathered && !stageTimedOut && mission.Group != 0)
                    {
                        if (mission.ReadyAt <= 0f)
                        {
                            mission.ReadyAt = now;
                            AILogger.Log(faction, "WAVE",
                                $"staged at ({mission.StagePos.x:0},{mission.StagePos.z:0}) — " +
                                "waiting for the sister armies");
                        }
                        if (!GroupReadyToStrike(missions, mission, now))
                        {
                            RegroupStragglers(em, faction, mission, now, attackMove: false);
                            continue;
                        }
                    }
                    if (gathered || stageTimedOut)
                    {
                        mission.Phase = MissionPhase.Striking;
                        mission.LegStartTime = now;
                        CommandRouter.IssueFormationAttackMove(
                            em, mission.Members, mission.TargetPos, FormationShape.Box, CommandSource.AI);
                        // Reinforcements now flow to the front, not the
                        // (abandoned) form-up ground — the main army's front (6f).
                        if (OwnsWaveTarget(missions, mission)) aiState.WaveTarget = mission.TargetPos;
                    }
                    else RegroupStragglers(em, faction, mission, now, attackMove: false);
                }
                else if (mission.Phase == MissionPhase.Striking && !mission.Engaged)
                {
                    RegroupStragglers(em, faction, mission, now, attackMove: true);
                }
                if (mission.Phase == MissionPhase.Striking && mission.Type == MissionType.Attack)
                    TryBreachWall(em, faction, ref aiState, mission, centroid, now);

                // Per-mission retreat: compare local strength at the army's
                // centroid. Raids disengage more readily (they harass, they
                // don't trade).
                if (aiState.RetreatCooldown > 0f || !hasHall) continue;

                // Don't retreat from our own base defense.
                float dxh = centroid.x - hallPos.x, dzh = centroid.z - hallPos.z;
                if (dxh * dxh + dzh * dzh < settings.defendRadius * settings.defendRadius) continue;

                int myStr = TacticalQuery.FactionStrengthInRadius(em, faction, centroid, 30f);
                // Defences count. Without the static term this check judged a
                // fight under an enemy Hall as if the Hall were scenery, so a
                // wave dying to a garrison plus tower fire never read as
                // losing and never disengaged (2026-08-18).
                int enemyStr = TacticalQuery.EnemyStrengthInRadius(em, faction, centroid, 30f)
                             + AIEngagement.StaticDefencePower(em, faction, centroid, 30f);
                if (myStr <= 0) continue;
                float ratio = mission.Type == MissionType.Raid
                    ? settings.retreatStrengthRatio * 0.65f
                    : settings.retreatStrengthRatio;
                if (enemyStr <= myStr * ratio) continue;

                // Retreat: plain formation move home (no engaging on the way).
                CommandRouter.IssueFormationMove(
                    em, mission.Members, hallPos, FormationShape.Box, CommandSource.AI);
                missions.RemoveAt(m);
                aiState.RetreatCooldown = settings.retreatCooldownSeconds;
                if (mission.Type == MissionType.Attack)
                    aiState.Posture = AIPosture.Rebuild;
            }
        }

        /// <summary>The gather radius for an army of this size: the configured
        /// radius plus the formation's own footprint, which grows with the
        /// square root of the head count (a 40-unit box stands ~10 m across
        /// by itself, and would never test as "gathered" inside 12 m).</summary>
        private float GatherRadius(int count)
            => Cfg.stagingGatherRadius + 1.5f * math.sqrt(math.max(1, count));

        /// <summary>Share of a mission's members within <paramref name="radius"/>
        /// of a point.</summary>
        private static float FractionWithin(EntityManager em, Mission mission, float3 point, float radius)
        {
            int inside = 0, counted = 0;
            float r2 = radius * radius;
            for (int i = 0; i < mission.Members.Count; i++)
            {
                var u = mission.Members[i];
                if (!em.HasComponent<LocalTransform>(u)) continue;
                var p = em.GetComponentData<LocalTransform>(u).Position;
                float dx = p.x - point.x, dz = p.z - point.z;
                counted++;
                if (dx * dx + dz * dz <= r2) inside++;
            }
            return counted == 0 ? 1f : (float)inside / counted;
        }

        /// <summary>
        /// Fold stragglers back into the formation. A member travelling on
        /// its own — no FormationMemberState, an order still in flight — is
        /// one the plan's cohesion gate left out (too far from the centroid
        /// when the leg was ordered) or one stuck-recovery dropped. Re-issuing
        /// the leg's order to everyone not fighting re-plans around the whole
        /// army: those now close enough become members, the rest keep
        /// converging and get folded in on a later sweep. This is what the
        /// curse shepherd does every few seconds, and it is why a wave reads
        /// as a body.
        /// </summary>
        private void RegroupStragglers(EntityManager em, Faction faction, Mission mission,
            float now, bool attackMove)
        {
            // A falling-back army is being walked AWAY on purpose; folding
            // its "stragglers" back toward the objective would undo that.
            if (mission.FallingBack) return;
            if (now < mission.NextRegroupTime) return;
            mission.NextRegroupTime = now + Cfg.regroupInterval;

            // Re-forming is not free: it re-plans the WHOLE army, and every
            // sweep that fired on a single stray (a unit the stuck recovery
            // had just released, a straggler a few metres out) made the
            // formation stop and re-slot every regroupInterval. Only a real
            // share of the army travelling loose justifies it; one or two
            // strays catch up on their own orders.
            int loose = AICommon.CountLooseMembers(em, mission.Members);
            if (loose == 0) return;
            if (loose < math.max(2f, mission.Members.Count * Cfg.regroupLooseFraction)) return;

            // Pooled: the router copies what it keeps (CommandRouter.Formation).
            var body = _regroupBody;
            body.Clear();
            for (int i = 0; i < mission.Members.Count; i++)
            {
                var u = mission.Members[i];
                if (em.HasComponent<Target>(u) && em.GetComponentData<Target>(u).Value != Entity.Null) continue;
                body.Add(u);
            }
            if (body.Count < 2) return;

            float3 dest = mission.Phase == MissionPhase.Mustering ? mission.MusterPos
                        : mission.Phase == MissionPhase.Staging ? mission.StagePos
                        : mission.TargetPos;
            if (attackMove)
                CommandRouter.IssueFormationAttackMove(em, body, dest, FormationShape.Box, CommandSource.AI);
            else
                CommandRouter.IssueFormationMove(em, body, dest, FormationShape.Box, CommandSource.AI);
        }

        /// <summary>
        /// Merge mission <paramref name="index"/> (a reinforcement column)
        /// into an earlier attack mission whose centroid is within
        /// reinforceMergeRadius of <paramref name="centroid"/>. Returns true
        /// when it merged (the caller drops the column).
        /// </summary>
        private bool TryMergeIntoArmy(EntityManager em,
            System.Collections.Generic.List<Mission> missions, int index, float3 centroid)
        {
            var column = missions[index];
            float r2 = Cfg.reinforceMergeRadius * Cfg.reinforceMergeRadius;
            for (int a = 0; a < index; a++)
            {
                var army = missions[a];
                if (army.Type != MissionType.Attack || army.Members.Count == 0) continue;
                float3 ac = ArmyCentroid(em, army, out int counted);
                if (counted == 0) continue;
                float dx = ac.x - centroid.x, dz = ac.z - centroid.z;
                if (dx * dx + dz * dz > r2) continue;

                for (int i = 0; i < column.Members.Count; i++)
                    if (!army.Members.Contains(column.Members[i]))
                        army.Members.Add(column.Members[i]);
                // The newcomers take the army's current leg.
                army.NextRegroupTime = 0f;
                missions.RemoveAt(index);
                return true;
            }
            return false;
        }

        /// <summary>Disband every mission (Defend entry — all hands home).</summary>
        private void DisbandAllMissions(Faction faction)
        {
            MissionsFor(faction).Clear();
        }

        /// <summary>Why the current wave chose its victim — written by the
        /// doctrine block, printed by the launch motivation line.</summary>
        private string _lastDoctrine = "";

        // Host-only heartbeat throttle (see TickAttackWaves).
        private readonly System.Collections.Generic.Dictionary<int, float> _waveHeartbeat
            = new System.Collections.Generic.Dictionary<int, float>();

        // ── THE STANDING ARMY FLOOR (2026-10-05, Game_AI.md 6a) ──────────
        //
        // Headless33-35: Expert's 120 s wave cadence kept drafting every idle
        // unit, so Normal stood with a LARGER army than Expert at every
        // checkpoint (48 vs 28 at 20 minutes). A harder tier now keeps
        // standingArmyFloorFraction of its desired army at home — never
        // drafted by a wave or its reinforcements — and its waves draw only
        // the surplus above it.

        /// <summary>The last launch's floor and how many idle units it held
        /// back (for the "standing floor n kept" line).</summary>
        private int _lastFloor, _lastFloorKept;

        /// <summary>Per faction: next "standing floor kept" line from the
        /// reinforcement pass (sim time).</summary>
        private readonly System.Collections.Generic.Dictionary<int, float> _floorLogAt
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>Units a wave may never draft below: the tier's
        /// standingArmyFloorFraction of the desired army, capped at a third
        /// of the population capacity (the same ceiling the wave bar uses), so
        /// an impossible desired army cannot freeze every wave. 0 = no floor.</summary>
        private static int StandingArmyFloor(Faction faction, in AIDifficultyProfile profile, int desired)
        {
            if (profile.StandingArmyFloorFraction <= 0f || desired <= 0) return 0;
            PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int cap);
            int ceiling = math.max(4, (cap > 0 ? cap : pop) / 3);
            return math.min(ceiling,
                (int)math.ceil(desired * math.saturate(profile.StandingArmyFloorFraction)));
        }

        /// <summary>Per faction: the curse node the first-RP hunt is on, and
        /// when it launched (units freed later reinforce it until then +
        /// religionHuntReinforceSeconds).</summary>
        private readonly System.Collections.Generic.Dictionary<int, (float3 Node, float LaunchedAt)> _religionHunt
            = new System.Collections.Generic.Dictionary<int, (float3, float)>();
        private readonly System.Collections.Generic.Dictionary<int, float> _nextReligionHuntLog
            = new System.Collections.Generic.Dictionary<int, float>();
        private readonly System.Collections.Generic.List<Entity> _religionHuntArmy
            = new System.Collections.Generic.List<Entity>();
        /// <summary>Per faction: every unit sent on the current first-RP
        /// hunt (launch + reinforcements). The hunt's power is THIS roster,
        /// wherever it stands — not who happens to be at the node yet.</summary>
        private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Entity>> _religionHuntRoster
            = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Entity>>();

        private System.Collections.Generic.List<Entity> ReligionHuntRoster(int key)
        {
            if (!_religionHuntRoster.TryGetValue(key, out var list))
                _religionHuntRoster[key] = list = new System.Collections.Generic.List<Entity>();
            return list;
        }

        /// <summary>Call the hunt off: forget it and walk the roster home
        /// (left alone, the hunters kept their attack-move and died at the
        /// node one by one).</summary>
        private void CallOffReligionHunt(EntityManager em, int key, float3 hallPos)
        {
            _religionHunt.Remove(key);
            var roster = ReligionHuntRoster(key);
            PruneDeadUnits(em, roster);
            if (roster.Count > 0)
                CommandRouter.IssueFormationMove(em, roster, hallPos, FormationShape.Box, CommandSource.AI);
            roster.Clear();
        }

        private static void PruneDeadUnits(EntityManager em, System.Collections.Generic.List<Entity> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (!em.Exists(list[i]) || !em.HasComponent<Health>(list[i])
                    || em.GetComponentData<Health>(list[i]).Value <= 0)
                    list.RemoveAt(i);
        }

        /// <summary>
        /// THE FIRST RELIGION POINT COMES FROM THE CURSE (2026-10-03,
        /// operator: "make sure AI actually chases the curse in order to get
        /// the first religion point"). The Temple costs 1 RP, and before a
        /// Temple exists the only RP source is the curse: kills pay points,
        /// a destroyed node pays a whole RP (docs/Design/Religion.md §1-2).
        /// Nothing used to plan for it — the reclaim squad only looked
        /// within 110 m of the Fortress, where curse nodes are never seeded.
        ///
        /// While the faction has no Temple and no RP: take the nearest curse
        /// node it has SEEN (any distance), judge its free army against what
        /// stands there (AIEngagement.AssessAssault — the curse now guards
        /// its nodes, Territory_Claims.md §6.7, so that is the fight), and
        /// attack with all of it when it wins. Too weak: raise the army
        /// target so the maintenance loop trains toward it.
        /// Returns true while the hunt owns the decision (the reclaim squad
        /// stands down).
        /// </summary>
        private bool TryHuntFirstReligionPoint(EntityManager em, Faction faction,
            ref SimpleAIState aiState, float now)
        {
            int key = (int)faction;
            int templeRp = TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp;
            if (CountFactionBuildings<TempleOfRidanTag>(em, faction) > 0
                || TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction, templeRp))
            {
                _religionHunt.Remove(key);
                ReligionHuntRoster(key).Clear();
                return false;
            }
            if (now < Cfg.religionHuntEarliestSeconds) return false;
            // Economy first on tiers that say so (Game_AI.md § 5h): no hunt,
            // and no army grown for one, until the capital's level.
            if (HuntDeferredForEconomy(em, faction, now)) return false;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return false;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            // Nearest live curse node this faction has seen.
            var nq = QC_SmallNodeTagLocalTransformHealth.Get(em, QT_SmallNodeTagLocalTransformHealth);
            using var nXfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var nHps = nq.ToComponentDataArray<Health>(Allocator.Temp);
            float bestD2 = float.MaxValue; float3 node = default; bool found = false;
            for (int i = 0; i < nXfs.Length; i++)
            {
                if (nHps[i].Value <= 0) continue;
                if (!IsKnownGround(faction, nXfs[i].Position)) continue;
                float d2 = math.distancesq(nXfs[i].Position, hallPos);
                if (d2 < bestD2) { bestD2 = d2; node = nXfs[i].Position; found = true; }
            }
            if (!found)
            {
                _religionHunt.Remove(key);
                LogReligionHunt(faction, now, "no curse node seen yet — the scouts have to find one");
                return false;
            }

            // Free combat units (the reclaim squad's eligibility rules).
            _religionHuntArmy.Clear();
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using (var ents = mq.ToEntityArray(Allocator.Temp))
            using (var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    if (!IsCombatClass(tags[i].Class)) continue;
                    Entity e = ents[i];
                    if (em.HasComponent<UnderConstruction>(e)) continue;
                    if (IsVerbUnit(em, e)) continue;
                    if (IsClaimSquadMember(e)) continue;
                    if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                    if (TransientState.Active<MoveCommand>(em, e)) continue;
                    if (TransientState.Active<AttackCommand>(em, e)) continue;
                    if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                    if (_religionHuntRoster.TryGetValue(key, out var onHunt) && onHunt.Contains(e)) continue;
                    _religionHuntArmy.Add(e);
                }

            // A hunt already under way on this node: reinforce it, no gate.
            // Only the units freed since the last think are in the list (the
            // hunters already marching carry AttackMoveTag), so this sends the
            // newcomers as their own formation and never re-plans the one
            // already on the road; units already standing at the node get a
            // plain per-unit re-poke (AICommon.IssueGroupOrder).
            if (_religionHunt.TryGetValue(key, out var hunt)
                && math.distancesq(hunt.Node, node) < 4f
                && now - hunt.LaunchedAt < Cfg.religionHuntReinforceSeconds)
            {
                // Reinforce only a fight still being won (newcomers plus the
                // hunters already there): a lost hunt is called off, and the
                // next think judges the node afresh instead of streaming
                // recruits into it one by one.
                var r = AssessCurseNode(em, faction, _religionHuntArmy, node,
                    Cfg.religionHuntAssessRadius, now);
                // THE HUNT'S POWER IS ITS ROSTER (2026-10-05): this used to
                // count newcomers plus our units INSIDE the node's radius, so
                // five seconds after launch — the hunters still on the road —
                // it read "losing (power 0+0 vs 72)" and dropped the hunt,
                // while the hunters marched on alone and died.
                var roster = ReligionHuntRoster(key);
                PruneDeadUnits(em, roster);
                int hunting = AIEngagement.PowerOf(em, roster);
                // Called off (and walked home) only when the roster plus the
                // newcomers no longer beat the node at all
                // (religionHuntCallOffMargin) — not at the launch margin,
                // which a garrison top-up would trip mid-march.
                if (r.EnemyPower > 0
                    && r.MyPower + hunting < r.EnemyPower * math.max(0.5f, Cfg.religionHuntCallOffMargin))
                {
                    CallOffReligionHunt(em, key, hallPos);
                    LogReligionHunt(faction, now,
                        $"hunt at ({node.x:0},{node.z:0}) is losing (power {r.MyPower}+{hunting} vs " +
                        $"{r.EnemyPower}) — called off, hunters recalled");
                    return true;
                }
                // No one-unit trickle: newcomers go as a group of at least
                // reclaimMinSquadSize (Game_AI.md § 5i).
                if (_religionHuntArmy.Count >= math.max(1, Cfg.reclaimMinSquadSize))
                {
                    AICommon.IssueGroupOrder(em, _religionHuntArmy, node, attackMove: true, Cfg.waveArrivedRadius);
                    roster.AddRange(_religionHuntArmy);
                }
                return true;
            }
            if (_religionHuntArmy.Count == 0)
            {
                LogReligionHunt(faction, now, "no free army to send");
                return true;
            }

            // A hunt past its reinforce window is over: its roster is free
            // again (the units already left the attack-move behind).
            if (!_religionHunt.ContainsKey(key)) ReligionHuntRoster(key).Clear();
            var a = AssessCurseNode(em, faction, _religionHuntArmy, node,
                Cfg.religionHuntAssessRadius, now);
            // Never at parity (Game_AI.md § 5h): the commit ratio alone let
            // 180-vs-180 hunts launch and lose.
            if (!a.ShouldFight || !HuntHasMargin(a))
            {
                // Too weak: grow the army toward what the node needs.
                int need = (int)math.ceil(_religionHuntArmy.Count * a.Ratio / math.max(0.1f, AIEngagement.DefaultCommitRatio));
                int want = CountAliveMilitary(em, faction) + math.max(1, need - _religionHuntArmy.Count);
                want = math.min(want, math.max(1, ProfileOf(faction).SustainArmyCap));   // the tier's cap holds
                if (aiState.DesiredMilitary < want) aiState.DesiredMilitary = want;
                LogReligionHunt(faction, now,
                    $"saving an army for the curse node at ({node.x:0},{node.z:0}): " +
                    $"power {a.MyPower} vs {a.EnemyPower}, want {want} military");
                return true;
            }

            // The whole hunt marches as one formation (AICommon.IssueGroupOrder).
            AICommon.IssueGroupOrder(em, _religionHuntArmy, node, attackMove: true, Cfg.waveArrivedRadius);
            _religionHunt[key] = (node, now);
            {
                var roster = ReligionHuntRoster(key);
                roster.Clear();
                roster.AddRange(_religionHuntArmy);
            }
            AILogger.Log(faction, "RELIGION",
                $"first Religion Point: {_religionHuntArmy.Count} units attack the curse node at " +
                $"({node.x:0},{node.z:0}) — power {a.MyPower} vs {a.EnemyPower}");
            return true;
        }

        private void LogReligionHunt(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextReligionHuntLog.TryGetValue(key, out float next) && now < next) return;
            _nextReligionHuntLog[key] = now + 60f;
            AILogger.Log(faction, "RELIGION", $"first Religion Point: {why}");
        }

        private readonly System.Collections.Generic.Dictionary<int, float> _nextReclaimHeldLog
            = new System.Collections.Generic.Dictionary<int, float>();

        private void LogReclaimHeld(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextReclaimHeldLog.TryGetValue(key, out float next) && now < next) return;
            _nextReclaimHeldLog[key] = now + Cfg.claimLogInterval;
            AILogger.Log(faction, "RECLAIM", why);
        }

        /// <summary>Host scratch for the reclaim draft (main thread only).</summary>
        private readonly System.Collections.Generic.List<Entity> _reclaimSquad
            = new System.Collections.Generic.List<Entity>();
        /// <summary>Free soldiers past reclaimSquadSize, for the margin.</summary>
        private readonly System.Collections.Generic.List<Entity> _reclaimExtra
            = new System.Collections.Generic.List<Entity>();

        /// <summary>When veilstone-poor, attack-move a small squad onto the
        /// nearest live SmallNode near the base — the military reclaim the
        /// corruption design demands. Drafted units carry AttackMoveTag, so
        /// consecutive ticks never double-draft; killing the SmallNode
        /// collapses the growth and pays the residue field.</summary>
        private void TryReclaimCorruptedPatches(EntityManager em, Faction faction,
            bool savingForAgeUp, float now)
        {
            if (now < Cfg.reclaimEarliestSeconds) return;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            // Nearest live SmallNode threatening the home economy.
            var sporeQuery = QC_SmallNodeTagLocalTransformHealth.Get(em, QT_SmallNodeTagLocalTransformHealth);
            using var sXfs = sporeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sHps = sporeQuery.ToComponentDataArray<Health>(Allocator.Temp);
            float bestD2 = Cfg.reclaimRadius * Cfg.reclaimRadius;
            float3 target = default;
            bool found = false;
            bool targetIsNode = false;
            for (int i = 0; i < sXfs.Length; i++)
            {
                if (sHps[i].Value <= 0) continue;
                if (!IsKnownGround(faction, sXfs[i].Position)) continue;   // only curse it has SEEN
                float dx = sXfs[i].Position.x - hallPos.x;
                float dz = sXfs[i].Position.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = sXfs[i].Position; found = true; targetIsNode = true; }
            }

            // Announced blood contaminations count as threats too (2026-08-04
            // telegraph): send the squad NOW so it is standing on the site
            // when the creatures rise.
            var pendingSpawns = TheWaningBorder.Systems.Border.BloodCurseSpawnSystem.Pending;
            for (int i = 0; i < pendingSpawns.Count; i++)
            {
                float dx = pendingSpawns[i].Pos.x - hallPos.x;
                float dz = pendingSpawns[i].Pos.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = pendingSpawns[i].Pos; found = true; targetIsNode = false; }
            }
            if (!found) return;

            // Engage when the growth THREATENS the base (inside the hall
            // threat ring — 2026-08-04 match 2: Red sat veilstone-RICH while
            // the curse ate its base, because this trigger was poverty-only)
            // or when veilstone-poor anywhere in the reclaim radius.
            bool atDoorstep = bestD2 < Cfg.reclaimHallThreatRadius * Cfg.reclaimHallThreatRadius;
            bool veilstonePoor = FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.GetComponentData<FactionResources>(bank).Veilstone < Cfg.reclaimVeilstonePoorBelow;
            // RELIGION COMES FROM THE CURSE (docs/Design/Religion.md §1): a
            // faction short of Religion Points hunts the nearest curse node's
            // defenders too — curse kills are its main source of RP, and the
            // node's fall opens the ground under it for a claim.
            bool wantsReligion = TheWaningBorder.Economy.FactionReligionPointsHelper
                .GetBalance(em, faction) < Cfg.reclaimReligionBelow;
            if (!atDoorstep && !veilstonePoor && !wantsReligion) return;
            // Saving for the age-up landmark: only curse at the doorstep is
            // worth soldiers (the age-up is what opens the map).
            if (savingForAgeUp && !atDoorstep) return;

            // Draft a small squad of uncommitted military (same eligibility
            // rules as the attack waves).
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var squad = _reclaimSquad;
            squad.Clear();
            _reclaimExtra.Clear();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;   // ritualists are not army
                if (IsClaimSquadMember(e)) continue;   // holding ground for a claim
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<MoveCommand>(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                if (em.HasComponent<NotControllableTag>(e)) continue;
                // The first reclaimSquadSize free soldiers are the squad; the
                // rest are the reserve the margin may call on (below).
                if (squad.Count < Cfg.reclaimSquadSize) squad.Add(e);
                else _reclaimExtra.Add(e);
            }
            if (squad.Count == 0) return;

            // NEVER FEED A LOSING FIGHT (2026-10-04). The squad used to go
            // with whoever was idle — in practice each freshly trained
            // Spearman walked alone onto a guarded node and died: 1,700
            // RECLAIM orders in one 60-minute batch, 65-112 units per
            // straggler lost to the curse before minute 15, and the supplies
            // that should have bought the age-up spent on replacements. The
            // squad goes only when it, plus our units already fighting there,
            // wins against what stands at the node.
            //
            // ...AND NEVER AT PARITY, NEVER PIECEMEAL (2026-10-05, Game_AI.md
            // § 5i). The commit ratio counted our units ALREADY fighting at
            // the node, so a lone Spearman went whenever the sum looked even:
            // Blue logged "held back: 1 free unit power 36 (+107 engaged) vs
            // 171" and then "1 units vs curse node" eleven times in one
            // minute; Red sent 59 such sorties. Now:
            //   * the squad ALONE (fighters already there do not count) must
            //     beat the node by the hunt's margin (religionHuntPowerMargin);
            //     when its first reclaimSquadSize soldiers fall short it takes
            //     more free ones, nearest the node first, up to
            //     claimCurseSquadMax;
            //   * it is at least reclaimMinSquadSize soldiers — no one-unit
            //     sorties, even at an unguarded node;
            //   * after a squad marched on a node, no other marches on it for
            //     reclaimRetrySeconds: a live attempt is not fed piecemeal and
            //     a failed one is not repeated at once.
            var cell = ((int)faction, (int)math.floor(target.x / 20f), (int)math.floor(target.z / 20f));
            if (_reclaimCooldown.TryGetValue(cell, out float retryAt) && now < retryAt) return;

            // A curse NODE is judged by the curse estimate (visible, last
            // seen, garrison baseline — SimpleAISystem.CurseIntel.cs); a
            // pending blood spawn has nothing standing yet.
            var assess = targetIsNode
                ? AssessCurseNode(em, faction, squad, target, Cfg.reclaimAssessRadius, now)
                : AIEngagement.AssessAssault(em, faction, squad, target, Cfg.reclaimAssessRadius);
            float margin = math.max(1.05f, Cfg.religionHuntPowerMargin);
            int need = (int)math.ceil(assess.EnemyPower * margin);
            int mine = assess.MyPower;
            int minSquad = math.max(1, Cfg.reclaimMinSquadSize);
            if ((mine < need || squad.Count < minSquad) && _reclaimExtra.Count > 0)
            {
                _reclaimExtra.Sort((a, b) =>
                {
                    int c = math.distancesq(em.GetComponentData<LocalTransform>(a).Position.xz, target.xz)
                        .CompareTo(math.distancesq(em.GetComponentData<LocalTransform>(b).Position.xz, target.xz));
                    return c != 0 ? c : a.Index.CompareTo(b.Index);
                });
                for (int i = 0; i < _reclaimExtra.Count && (mine < need || squad.Count < minSquad)
                                && squad.Count < Cfg.claimCurseSquadMax; i++)
                {
                    squad.Add(_reclaimExtra[i]);
                    mine += TacticalQuery.UnitStrength(em, _reclaimExtra[i]);
                }
            }
            if (squad.Count < minSquad || (assess.EnemyPower > 0 && mine < need))
            {
                int engaged = TacticalQuery.FactionStrengthInRadius(em, faction, target, Cfg.reclaimAssessRadius);
                LogReclaimHeld(faction, now,
                    $"held back at ({target.x:0},{target.z:0}): {squad.Count} free unit(s) power " +
                    $"{mine} vs {assess.EnemyPower} (needs {need} and {minSquad}+ units; " +
                    $"{engaged} of ours already there do not count)");
                return;
            }

            // One formation, not a per-unit stream; anyone already standing
            // on the node is re-poked on its own (AICommon.IssueGroupOrder).
            AICommon.IssueGroupOrder(em, squad, target, attackMove: true, Cfg.waveArrivedRadius);
            _reclaimCooldown[cell] = now + math.max(0f, Cfg.reclaimRetrySeconds);
            int drafted = squad.Count;
            TWBLog.Log($"[AI {faction}] {drafted} units sent to clear the " +
                       $"curse node at ({target.x:0},{target.z:0}).");
            AILogger.Log(faction, "RECLAIM",
                $"{drafted} units vs curse node at ({target.x:0},{target.z:0}) (power {mine} vs {assess.EnemyPower})");
        }

        /// <summary>(faction, 20 m cell of a curse node) -> sim time before
        /// which no new reclaim squad may march on it (reclaimRetrySeconds).
        /// Reset per match with the economy-defence state.</summary>
        private readonly System.Collections.Generic.Dictionary<(int, int, int), float> _reclaimCooldown
            = new System.Collections.Generic.Dictionary<(int, int, int), float>();

        private void TickAttackWaves(EntityManager em, Entity brainEntity, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
            if (now < profile.FirstAttackEarliestSeconds) return;

            // WAVE HEARTBEAT (2026-08-30): the second-wave freeze was
            // SILENT — one launch, then eleven minutes of nothing, with no
            // BLOCKED line and no exception. Every gate value, once a
            // minute, so "no wave and no reason" cannot happen again.
            if (!_waveHeartbeat.TryGetValue((int)faction, out float hb) || now >= hb)
            {
                _waveHeartbeat[(int)faction] = now + 60f;
                AILogger.Log(faction, "WAVE",
                    $"heartbeat now={(int)now}s next={(int)aiState.NextWaveTime}s " +
                    $"active={aiState.WaveActive} n={aiState.WaveNumber} " +
                    $"posture={aiState.Posture} start={(int)aiState.WaveStartTime}s");
            }

            // Reinforcement is NOT gated on the wave cooldown: the whole
            // point is to feed the live push continuously between waves.
            ReinforceActiveWave(em, faction, ref aiState, now);

            if (now < aiState.NextWaveTime) return;

            // CLAIMS OUTRANK WAVES while open ground waits for soldiers and
            // nothing threatens home (Game_AI.md § 5b) — bounded, so a wave
            // still goes after claimWaveYieldMaxSeconds.
            if (ClaimsYieldWave(faction, aiState.Posture, now)) return;

            // A TARGET, NOT A DOORSTEP (2026-09-12, Game_AI.md 8). WaveBaseUnits
            // is 4 to 6, so the bar was met the moment a couple of bodies came
            // free: Red launched waves of TWO into a defended base, wasted
            // them, and reset its own timer doing it. The bar now scales with
            // the army the faction is actually trying to keep -- half of
            // DesiredMilitary -- with the base value as the floor so an early
            // rush still goes, and the existing `overdue` release still fires
            // it regardless once a wave is late. Big armies wait to be armies.
            int bar = (int)math.round(profile.WaveBaseUnits
                                      * PlanProfileOf(faction).WaveBarScale);
            // With a standing floor (Game_AI.md 6a) the wave is drawn from the
            // surplus above it, so half of THAT is the bar.
            int standFloor = StandingArmyFloor(faction, profile, aiState.DesiredMilitary);
            int minUnits = math.max(2, math.max(bar, (aiState.DesiredMilitary - standFloor) / 2));

            // A SHARE OF THE ARMY IT HAS, NOT OF THE ONE IT WANTS (2026-10-05,
            // Mirror Marches v3). DesiredMilitary jumps to 200 at age-up, so
            // half of it asked a 30-unit army for 85 idle, and the population
            // clamp below made it worse for the tier that houses fastest:
            // Expert's cap passed 200 by minute 15 and its bar read 66-73
            // while Easy's read 30 — Expert attacked at 22-25 minutes against
            // a 180 s earliest. A wave now also goes once waveLiveArmyShare
            // of the LIVE army stands idle above the floor (never below the
            // tier's base bar); the strength gate past strengthWaveAfterSeconds
            // still decides whether that army is enough.
            if (Cfg.waveLiveArmyShare > 0f)
            {
                int alive = CountAliveMilitary(em, faction);
                int liveBar = (int)math.ceil(math.max(0, alive - standFloor) * math.saturate(Cfg.waveLiveArmyShare));
                minUnits = math.max(bar, math.min(minUnits, liveBar));
            }

            // NEVER ASK FOR MORE THAN THE POPULATION CAP CAN HOLD
            // (2026-09-12). DesiredMilitary is SustainArmyCap x the plan's
            // ArmyScale and reaches 320, so half of it is 160 -- and the hard
            // population ceiling is 200, most of which is workers, support and
            // units already committed. Green stood at 200/200 population with
            // an army of 157 and logged "need 160 idle" forever: an army that
            // large is physically unable to field the bar its own target
            // implies, so the late game stopped attacking entirely.
            //
            // Halving an impossible number gives an impossible number. Clamp
            // the bar to a fraction of what the faction can ACTUALLY hold, and
            // the maxed-out army becomes the thing that launches waves instead
            // of the thing that blocks them.
            PopulationHelper.TryGetFactionPopulation(faction, out int barPop, out int barCap);
            int affordable = math.max(4, (barCap > 0 ? barCap : barPop) / 3);
            minUnits = math.min(minUnits, affordable);

            // PAST THE MARK, A WAVE LAUNCHES ON STRENGTH (2026-10-04,
            // Game_AI.md 6a). This replaced "past minute 25, nothing leaves
            // home below FULL population", which held late waves 221 times in
            // one 60-minute batch while banks piled up 34k supplies and 27k
            // iron: a faction that could not fill its cap simply stopped
            // attacking. The late game should still be decided by real pushes
            // rather than half-armies, so the bar is now the fight itself --
            // the army's power against the best known defence at the
            // objective (TryLaunchAttack) -- with a head-count FLOOR so a tiny
            // army never trickles out at an undefended target. Like the rule
            // it replaced, it outranks the overdue release.
            bool strengthGate = now >= Cfg.strengthWaveAfterSeconds;
            if (strengthGate)
                minUnits = math.min(affordable, math.max(2, (int)math.round(
                    Cfg.strengthWaveMinArmy * PlanProfileOf(faction).WaveBarScale)));

            if (TryLaunchAttack(em, brainEntity, faction, minUnits, strengthGate,
                    ref aiState, settings, personality, profile, now, out int waveSize))
            {
                aiState.WaveNumber++;
                // PRESS THE ADVANTAGE (2026-08-04 playtest: "the winning
                // player was very shy to attack, giving the other player
                // breathing space"): Pressure posture means the army is at
                // or above its desired size — a winner should convert that
                // NOW, not politely wait a full interval.
                // Never slower than the five-minute cadence, whatever the tier.
                float interval = math.min(profile.AttackWaveIntervalSeconds, Cfg.waveOverdueSeconds)
                    * (aiState.Posture == AIPosture.Pressure ? 0.5f : 1f);
                aiState.NextWaveTime = now + interval;
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} LAUNCHED at {(int)now}s with {waveSize} unit(s) " +
                    $"(min {minUnits}, posture {aiState.Posture}); " +
                    $"next at {(int)aiState.NextWaveTime}s");
            }
            else
            {
                aiState.NextWaveTime = now + Cfg.waveRetrySeconds;
                // A wave that cannot launch for minutes is the "20 min, zero
                // attacks" bug class — log the why once per ~2 minutes.
                if ((int)(now / 120f) != (int)((now - Cfg.waveRetrySeconds) / 120f))
                {
                    TWBLog.Log($"[AI {faction}] wave {aiState.WaveNumber + 1} blocked at " +
                               $"{(int)now}s (need {minUnits} idle military above a standing floor of {standFloor}, posture " +
                               $"{aiState.Posture}, desired {aiState.DesiredMilitary})");
                    AILogger.Log(faction, "WAVE",
                        $"wave {aiState.WaveNumber + 1} BLOCKED at {(int)now}s " +
                        $"(need {minUnits} idle above standing floor {standFloor}, posture {aiState.Posture}, " +
                        $"desired {aiState.DesiredMilitary})");
                }
            }
        }
    }
}
