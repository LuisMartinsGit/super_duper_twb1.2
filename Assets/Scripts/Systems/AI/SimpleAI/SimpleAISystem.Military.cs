// SimpleAISystem.Military.cs
// Army missions, attack waves, reinforcement and corrupted-patch reclaim.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
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
        private enum MissionPhase : byte { Direct = 0, Staging = 1, Striking = 2 }

        private sealed class Mission
        {
            public MissionType Type;
            public MissionPhase Phase;
            public Entity Target;
            public float3 TargetPos;
            public float3 StagePos;
            public float StartTime;

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

            public readonly System.Collections.Generic.List<Entity> Members
                = new System.Collections.Generic.List<Entity>();
        }

        private const float MissionTimeoutSeconds = 180f;
        // Forward staging: form up this far from the target (home side), and
        // commit once the army centroid is within the gather radius (or the
        // staging phase times out — stragglers must not stall the push).
        private const float StagingDistance = 30f;
        private const float StagingGatherRadius = 12f;
        private const float StagingTimeoutSeconds = 60f;
        private const int RaidPartySize = 3;
        // Launch a raid alongside the attack only when this many EXTRA idle
        // units exist beyond the attack threshold.
        private const int RaidSurplus = 2;

        private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Mission>> _missions
            = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Mission>>();

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
        private bool TryLaunchAttack(EntityManager em, Entity brainEntity, Faction faction, int minUnits,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
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

            // Difficulty knob: no offensive missions before the tier's first-
            // attack time (AoE4: first Hardest attack ≈ 8 min, later on lower
            // tiers). Defense (posture engine) is unaffected.
            if (now < profile.FirstAttackEarliestSeconds) return false;

            // Defend / Rebuild postures hold the army home (M4).
            if (aiState.Posture == AIPosture.Defend || aiState.Posture == AIPosture.Rebuild)
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
            var militaryQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = militaryQuery.ToEntityArray(Allocator.Temp);
            using var tags = militaryQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = militaryQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Units already enrolled in a living mission are never re-drafted.
            var enrolled = new System.Collections.Generic.HashSet<Entity>();
            foreach (var m in MissionsFor(faction))
                foreach (var member in m.Members)
                    enrolled.Add(member);

            var idleMilitary = new System.Collections.Generic.List<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;   // ritualists are not army
                // Uncommandable bodies (Feraldis House Raiders) cannot be sent
                // anywhere — FeraldisRaiderPatrolSystem owns them and
                // overrides any order the same frame. Drafting them inflates
                // the wave's apparent strength with units that never march.
                if (em.HasComponent<NotControllableTag>(e)) continue;
                if (enrolled.Contains(e)) continue;
                // Already on a mission or carrying out another order — leave alone.
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<MoveCommand>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                idleMilitary.Add(e);
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
            if (target == Entity.Null) return false; // no enemy reachable
            if (!em.HasComponent<LocalTransform>(target)) return false;
            float3 targetPos = em.GetComponentData<LocalTransform>(target).Position;

            // ── CAN WE ACTUALLY WIN THERE? ──────────────────────────────
            // The wave gate above is a COUNT ("do I have minUnits idle"),
            // which says nothing about what is waiting. Assess the objective:
            // hostile army AND hostile buildings, because a Hall is a
            // multi-target gun on 2400 HP and used to score zero
            // (AIEngagement). Refusing here keeps the army home to grow
            // instead of feeding it in piecemeal — the "attacks next to the
            // enemy Hall and is always outnumbered" report, 2026-08-18.
            var assault = AIEngagement.AssessAssault(em, faction, idleMilitary, targetPos);
            if (!assault.ShouldFight)
            {
                AILogger.Log(faction, "WAVE",
                    $"hold — assault at ({targetPos.x:0},{targetPos.z:0}) unfavourable: " +
                    $"mine {assault.MyPower} vs {assault.EnemyPower} " +
                    $"(army {assault.EnemyMobilePower} + defences {assault.EnemyStaticPower}), " +
                    $"ratio {assault.Ratio:0.00}");
                return false;
            }

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
                    anchor = FindNearestSmallNode(em, mid, out anchorPos);
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
            if (scored
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
            // (miners / eco buildings) while the main army takes the scored
            // objective. Two simultaneous pressure points instead of one blob.
            var missions = MissionsFor(faction);
            if (!rerouted && profile.RaidingEnabled
                && idleMilitary.Count >= minUnits + RaidPartySize + RaidSurplus)
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
                    for (int i = 0; i < RaidPartySize && idleMilitary.Count > 0; i++)
                    {
                        Entity u = idleMilitary[0];
                        idleMilitary.RemoveAt(0);
                        raid.Members.Add(u);
                        CommandRouter.IssueAttackMove(em, u, raidPos, CommandSource.AI);
                    }
                    missions.Add(raid);
                }
            }

            // ── Main attack mission ──
            // The army marches through the FORMATION pipeline (virtual leader,
            // type-ranked slots, slowest-member speed) — the same machinery
            // player group orders use. Hard+ tiers stage first: form up near
            // the target on the home side, then commit at full strength.
            var attack = new Mission
            {
                Type = MissionType.Attack,
                Target = target,
                TargetPos = targetPos,
                StartTime = now,
            };
            for (int i = 0; i < idleMilitary.Count; i++)
                attack.Members.Add(idleMilitary[i]);

            float3 fromTarget = originPos - targetPos;
            fromTarget.y = 0f;
            float approachDist = math.length(fromTarget);
            if (profile.ForwardStaging && approachDist > StagingDistance * 2f)
            {
                attack.Phase = MissionPhase.Staging;
                attack.StagePos = targetPos + (fromTarget / approachDist) * StagingDistance;
                CommandRouter.IssueFormationAttackMove(
                    em, attack.Members, attack.StagePos, FormationShape.Box, CommandSource.AI);
            }
            else
            {
                attack.Phase = MissionPhase.Striking;
                CommandRouter.IssueFormationAttackMove(
                    em, attack.Members, targetPos, FormationShape.Box, CommandSource.AI);
            }
            missions.Add(attack);

            // Remember where this wave went so newly-finished units can be
            // fed into it (ReinforceActiveWave). Without this a wave was a
            // one-shot: everything trained after launch stood in the base
            // until the NEXT wave's larger minimum was met, so armies
            // trickled away at the front while reinforcements idled at home.
            aiState.WaveTarget = targetPos;
            aiState.WaveActive = 1;
            aiState.WaveStartTime = now;

            return true;
        }

        /// <summary>Seconds between reinforcement sweeps for a live wave.</summary>
        private const float ReinforceInterval = 10f;

        /// <summary>
        /// A unit standing within this many metres of the wave target has
        /// ARRIVED. Arrivals are not reinforcement candidates: re-issuing
        /// AttackMove to a position a unit is already standing on completes
        /// instantly, which is what made a spent wave look permanently "alive"
        /// (2026-08-07 match: `wave 4 reinforced with 47 unit(s) (0 already
        /// committed)` every 10 s for twenty minutes, army parked on a razed
        /// objective, `wave 5 BLOCKED` because everyone was nominally busy).
        /// </summary>
        private const float WaveArrivedRadius = 30f;

        /// <summary>
        /// Hard cap on a single wave's life. Backstop for the case the arrival
        /// test cannot see — units that keep an AttackMoveTag forever because
        /// they are stuck on terrain read as "committed" and would hold the
        /// wave open indefinitely. At the cap the wave is retired and its army
        /// is released to the next draft, so the cadence keeps running.
        /// </summary>
        private const float WaveMaxLifetime = 150f;

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
        private void ReinforceActiveWave(EntityManager em, Faction faction,
            ref SimpleAIState aiState, float now)
        {
            if (aiState.WaveActive == 0) return;
            if (now < aiState.NextReinforceTime) return;
            aiState.NextReinforceTime = now + ReinforceInterval;

            // Age out a wave that has overstayed its welcome, whatever its
            // members are doing. Releasing the army is what lets the NEXT wave
            // draft it — a wave that never retires starves every wave after it.
            if (now - aiState.WaveStartTime > WaveMaxLifetime)
            {
                aiState.WaveActive = 0;
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} SPENT (lifetime) — army released for the next wave");
                return;
            }

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
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

            int committed = 0, sent = 0, arrived = 0;
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
                // Nor uncommandable raiders: ordering them is a no-op that
                // still counts as "sent", which kept spent waves alive.
                if (em.HasComponent<NotControllableTag>(e)) continue;

                bool busy = em.HasComponent<AttackMoveTag>(e)
                         || em.HasComponent<AttackCommand>(e);
                if (busy) { committed++; continue; }
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                if (em.HasComponent<BuildCommand>(e)) continue;

                // Idle AND already standing on the objective: this unit has
                // arrived and there is nothing left here to fight. Re-ordering
                // it is a no-op that would keep the wave alive forever.
                float dx = xfs[i].Position.x - aiState.WaveTarget.x;
                float dz = xfs[i].Position.z - aiState.WaveTarget.z;
                if (dx * dx + dz * dz <= WaveArrivedRadius * WaveArrivedRadius)
                {
                    arrived++;
                    continue;
                }

                reinforcements.Add(e);
                sent++;
            }

            if (reinforcements.Count > 0)
                CommandRouter.IssueFormationAttackMove(
                    em, reinforcements, aiState.WaveTarget,
                    FormationShape.Box, CommandSource.AI);

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
        private void UpdateMissions(EntityManager em, Faction faction,
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

                bool objectiveDown = mission.Target == Entity.Null || !em.Exists(mission.Target);
                bool timedOut = now - mission.StartTime > MissionTimeoutSeconds;
                if (objectiveDown || timedOut)
                {
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

                // Forward-staging commit: once the army has gathered at the
                // staging point (or staging times out — stragglers must not
                // stall the push), strike the objective as one formation.
                if (mission.Phase == MissionPhase.Staging)
                {
                    float sx = centroid.x - mission.StagePos.x;
                    float sz = centroid.z - mission.StagePos.z;
                    bool gathered = sx * sx + sz * sz <= StagingGatherRadius * StagingGatherRadius;
                    bool stageTimedOut = now - mission.StartTime > StagingTimeoutSeconds;
                    if (gathered || stageTimedOut)
                    {
                        mission.Phase = MissionPhase.Striking;
                        CommandRouter.IssueFormationAttackMove(
                            em, mission.Members, mission.TargetPos, FormationShape.Box, CommandSource.AI);
                    }
                }

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

        /// <summary>Disband every mission (Defend entry — all hands home).</summary>
        private void DisbandAllMissions(Faction faction)
        {
            MissionsFor(faction).Clear();
        }
        /// <summary>
        /// Recurring attack waves (2026-08-04): once the difficulty's
        /// first-attack gate passes, launch a wave every
        /// AttackWaveIntervalSeconds. The idle-army minimum starts at
        /// WaveBaseUnits and grows by WaveGrowthUnits per successful wave
        /// (capped near SustainArmyCap) — and since TryLaunchAttack drafts
        /// ALL idle military, real waves scale up with the economy beyond the
        /// minimum. A wave that cannot launch (army short, posture holds, no
        /// target) retries on a short fuse instead of waiting a full
        /// interval.
        /// </summary>
        private const float WaveRetrySeconds = 20f;
        // §2.5b corruption counterplay knobs.
        private const float ReclaimEarliestSeconds = 240f;   // opening stays scripted
        private const int ReclaimVeilstonePoorBelow = 150;   // bank level that counts as starving
        private const float ReclaimRadius = 110f;            // "threatening the home economy"
        /// <summary>Inside this ring of the Hall, a curse growth is attacked
        /// regardless of the veilstone bank — threat-based, not poverty-based.</summary>
        private const float ReclaimHallThreatRadius = 65f;
        private const int ReclaimSquadSize = 6;

        /// <summary>When veilstone-poor, attack-move a small squad onto the
        /// nearest live SmallNode near the base — the military reclaim the
        /// corruption design demands. Drafted units carry AttackMoveTag, so
        /// consecutive ticks never double-draft; killing the SmallNode
        /// collapses the growth and pays the residue field.</summary>
        private void TryReclaimCorruptedPatches(EntityManager em, Faction faction, float now)
        {
            if (now < ReclaimEarliestSeconds) return;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            // Nearest live SmallNode threatening the home economy.
            var sporeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<SmallNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var sXfs = sporeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sHps = sporeQuery.ToComponentDataArray<Health>(Allocator.Temp);
            float bestD2 = ReclaimRadius * ReclaimRadius;
            float3 target = default;
            bool found = false;
            for (int i = 0; i < sXfs.Length; i++)
            {
                if (sHps[i].Value <= 0) continue;
                float dx = sXfs[i].Position.x - hallPos.x;
                float dz = sXfs[i].Position.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = sXfs[i].Position; found = true; }
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
                if (d2 < bestD2) { bestD2 = d2; target = pendingSpawns[i].Pos; found = true; }
            }
            if (!found) return;

            // Engage when the growth THREATENS the base (inside the hall
            // threat ring — 2026-08-04 match 2: Red sat veilstone-RICH while
            // the curse ate its base, because this trigger was poverty-only)
            // or when veilstone-poor anywhere in the reclaim radius.
            bool atDoorstep = bestD2 < ReclaimHallThreatRadius * ReclaimHallThreatRadius;
            bool veilstonePoor = FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.GetComponentData<FactionResources>(bank).Veilstone < ReclaimVeilstonePoorBelow;
            if (!atDoorstep && !veilstonePoor) return;

            // Draft a small squad of uncommitted military (same eligibility
            // rules as the attack waves).
            var mq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int drafted = 0;
            for (int i = 0; i < ents.Length && drafted < ReclaimSquadSize; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;   // ritualists are not army
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<MoveCommand>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                CommandRouter.IssueAttackMove(em, e, target, CommandSource.AI);
                drafted++;
            }
            if (drafted > 0)
            {
                TWBLog.Log($"[AI {faction}] veilstone-poor — {drafted} units sent to clear the " +
                           $"curse node at ({target.x:0},{target.z:0}).");
                AILogger.Log(faction, "RECLAIM",
                    $"{drafted} units vs curse node at ({target.x:0},{target.z:0})");
            }
        }

        private void TickAttackWaves(EntityManager em, Entity brainEntity, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
            if (now < profile.FirstAttackEarliestSeconds) return;

            // Reinforcement is NOT gated on the wave cooldown: the whole
            // point is to feed the live push continuously between waves.
            ReinforceActiveWave(em, faction, ref aiState, now);

            if (now < aiState.NextWaveTime) return;

            int minUnits = math.min(
                profile.WaveBaseUnits + aiState.WaveNumber * profile.WaveGrowthUnits,
                math.max(profile.WaveBaseUnits, profile.SustainArmyCap - 2));
            // Never demand a larger wave than the army director intends to
            // FIELD. Waves escalate by count (5, 7, 9, ...) while
            // DesiredMilitary can sit low or at 0 mid-build-order; the old
            // unclamped bar produced the permanent "need 7 idle, desired 0"
            // block — no wave ever launched again after wave 1 died.
            minUnits = math.min(minUnits,
                math.max(profile.WaveBaseUnits, aiState.DesiredMilitary));

            // THE PLAN SETS THE BAR. This is the difference between an army
            // you watch gather and one that dribbles out in threes: Rush
            // attacks at half the normal count, Mass waits until it has half
            // again as many, and Fortress effectively never leaves home.
            minUnits = math.max(1,
                (int)math.round(minUnits * PlanProfileOf(faction).WaveBarScale));

            if (TryLaunchAttack(em, brainEntity, faction, minUnits,
                    ref aiState, settings, personality, profile, now))
            {
                aiState.WaveNumber++;
                // PRESS THE ADVANTAGE (2026-08-04 playtest: "the winning
                // player was very shy to attack, giving the other player
                // breathing space"): Pressure posture means the army is at
                // or above its desired size — a winner should convert that
                // NOW, not politely wait a full interval.
                float interval = profile.AttackWaveIntervalSeconds
                    * (aiState.Posture == AIPosture.Pressure ? 0.5f : 1f);
                aiState.NextWaveTime = now + interval;
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} LAUNCHED at {(int)now}s (min {minUnits}, " +
                    $"posture {aiState.Posture}); next at {(int)aiState.NextWaveTime}s");
            }
            else
            {
                aiState.NextWaveTime = now + WaveRetrySeconds;
                // A wave that cannot launch for minutes is the "20 min, zero
                // attacks" bug class — log the why once per ~2 minutes.
                if ((int)(now / 120f) != (int)((now - WaveRetrySeconds) / 120f))
                {
                    TWBLog.Log($"[AI {faction}] wave {aiState.WaveNumber + 1} blocked at " +
                               $"{(int)now}s (need {minUnits} idle military, posture " +
                               $"{aiState.Posture}, desired {aiState.DesiredMilitary})");
                    AILogger.Log(faction, "WAVE",
                        $"wave {aiState.WaveNumber + 1} BLOCKED at {(int)now}s " +
                        $"(need {minUnits} idle, posture {aiState.Posture}, " +
                        $"desired {aiState.DesiredMilitary})");
                }
            }
        }
    }
}
