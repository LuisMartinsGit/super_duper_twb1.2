// SimpleAISystem.Posture.cs
// Posture evaluation and base defence.
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
        // POSTURE + DEFENSE RESPONSE + RETREAT (M4 / M6)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluate and act on the AI's posture each think tick:
        ///   Defend  — ThreatMap spike near the Hall: recall the fielded army,
        ///             dispatch an idle builder to repair damaged buildings.
        ///   Rebuild — army below half its desired size: hold attacks.
        ///   Pressure— army at/above desired size: attack with smaller waves.
        ///   Develop — everything else.
        /// Also runs the M6 retreat check while an attack wave is out.
        /// </summary>
        private void EvaluatePosture(EntityManager em, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality)
        {
            var prevPosture = aiState.Posture;
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall))
            {
                aiState.Posture = AIPosture.Rebuild; // no base: just rebuild
                if (prevPosture != AIPosture.Rebuild)
                    AILogger.Log(faction, "POSTURE",
                        $"{prevPosture} -> Rebuild (no Hall standing)");
                return;
            }
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            int aliveMil = CountAliveMilitary(em, faction);

            // Defend triggers on enemies PHYSICALLY inside the base (tight
            // 30 u probe around the Hall), not on the ambient threat map —
            // a static border garrison parked 40 u away was keeping factions
            // in Defend permanently, freezing their build orders at every
            // LaunchAttack step (total stagnation). The threat map still
            // drives scout routing and target risk.
            int enemyInBase = TacticalQuery.EnemyStrengthInRadius(em, faction, hallPos, 30f);
            if (enemyInBase > settings.defendThreatThreshold / 2)
            {
                // COMMAND FOLLOW-THROUGH: the recall + repair dispatch fires
                // once on ENTERING Defend, not every think tick — re-issuing
                // attack-moves each tick was resetting units mid-order.
                bool entering = aiState.Posture != AIPosture.Defend;
                aiState.Posture = AIPosture.Defend;
                if (entering)
                {
                    AILogger.Log(faction, "POSTURE",
                        $"{prevPosture} -> Defend (enemy strength {enemyInBase} inside the base ring)");
                    // All hands home: standing missions are void when the
                    // base itself is under attack (the imperative exception
                    // to command follow-through).
                    DisbandAllMissions(faction);
                    DefendBase(em, faction, hallPos, settings);
                }
                return;
            }

            // OUTLYING BUILDING UNDER ATTACK (2026-07-12: "AI is not
            // assigning defenders — they rely on auto-acquire while the base
            // burns"). The Hall probe above only covers 30 u; a GathererHut
            // or Barracks being razed across the base triggered nothing.
            // A building counts as under attack when it is damaged, its
            // last attacker still exists, and enemy strength is STILL
            // present around it (so long-healed scars don't lock Defend).
            if (TryFindAttackedBuilding(em, faction, out float3 attackedPos))
            {
                bool entering = aiState.Posture != AIPosture.Defend;
                aiState.Posture = AIPosture.Defend;
                if (entering)
                {
                    AILogger.Log(faction, "POSTURE",
                        $"{prevPosture} -> Defend (building under attack at ({attackedPos.x:0},{attackedPos.z:0}))");
                    DisbandAllMissions(faction);
                    // Rally at the ATTACKED building, not the Hall — the
                    // defenders converge on the actual fight.
                    DefendBase(em, faction, attackedPos, settings);
                }
                return;
            }

            // (Per-mission retreat lives in UpdateMissions — the old global
            // CheckRetreat pulled EVERY army home when one was outmatched.)

            if (aiState.DesiredMilitary > 0 && aliveMil < aiState.DesiredMilitary / 2)
            {
                aiState.Posture = AIPosture.Rebuild;
                if (prevPosture != AIPosture.Rebuild)
                    AILogger.Log(faction, "POSTURE",
                        $"{prevPosture} -> Rebuild (army {aliveMil} below half of desired {aiState.DesiredMilitary})");
                return;
            }

            aiState.Posture = aliveMil >= math.max(personality.attackThreshold, aiState.DesiredMilitary)
                ? AIPosture.Pressure
                : AIPosture.Develop;
            if (aiState.Posture != prevPosture)
                AILogger.Log(faction, "POSTURE",
                    $"{prevPosture} -> {aiState.Posture} (army {aliveMil}, desired {aiState.DesiredMilitary})");
        }

        /// <summary>Find a damaged own building whose attacker still exists
        /// AND still has enemy presence nearby — the "outpost under attack"
        /// signal for the defense posture. Deterministic entity-order scan;
        /// returns the first match.</summary>
        private static bool TryFindAttackedBuilding(EntityManager em, Faction faction, out float3 pos)
        {
            pos = default;
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LastAttackerEntity>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = bq.ToEntityArray(Allocator.Temp);
            using var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = bq.ToComponentDataArray<Health>(Allocator.Temp);
            using var atk = bq.ToComponentDataArray<LastAttackerEntity>(Allocator.Temp);
            using var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (hps[i].Value <= 0 || hps[i].Value >= hps[i].Max) continue;
                if (atk[i].Value == Entity.Null || !em.Exists(atk[i].Value)) continue;
                // Live fight check: enemies still near the building (a scar
                // from minutes ago must not pin the AI in Defend).
                if (TacticalQuery.EnemyStrengthInRadius(em, faction, xfs[i].Position, 25f) <= 0)
                    continue;
                pos = xfs[i].Position;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Defend response (M4): recall every fielded military unit that is
        /// outside the defend radius back to the Hall (attack-move, so it
        /// fights through), and put an idle builder on the most-damaged
        /// completed building.
        /// </summary>
        // Instance, not static: registering the defence as a mission needs
        // the per-faction mission list, which is instance state.
        private void DefendBase(EntityManager em, Faction faction, float3 hallPos, AISettingsSO settings)
        {
            float defendRadiusSq = settings.defendRadius * settings.defendRadius;

            // The actual threat: nearest ENEMY unit inside the defend ring.
            // (2026-08-04 playtest: "AI was under attack and had defenders,
            // but it didn't use them" — home units got no order and stood on
            // auto-aggro while archers picked the base apart from range.)
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            bool threatFound = false;
            float threatD2 = defendRadiusSq;
            float3 threatPos = hallPos;
            // The intruder itself, not just where it stands: UpdateMissions
            // reads a null Target as "objective destroyed" and disbands the
            // mission on its next tick, so a defence with no Target would be
            // dissolved before it fought anything.
            Entity threatEnt = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
            {
                // Allies do not count as a threat near the Hall; the curse is
                // excluded here as before. docs/Design/Teams.md
                if (!Alliances.AreHostile(faction, facs[i].Value)
                    || facs[i].Value == Faction.Border) continue;
                float dx = xfs[i].Position.x - hallPos.x;
                float dz = xfs[i].Position.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < threatD2)
                {
                    threatD2 = d2; threatPos = xfs[i].Position;
                    threatEnt = ents[i]; threatFound = true;
                }
            }

            // HOME DEFENCE IS AN ARMY TOO.
            //
            // Both loops below used to hand out one attack-move PER UNIT, and
            // Defend disbands every mission on entry — so the moment the AI was
            // attacked at home it stopped having armies at all and fought the
            // defence as N independent units, each walking to its own nearest
            // enemy. That is the same defect the tactical layer exists to fix,
            // on the one occasion the AI can least afford it.
            //
            // Collect instead, then dispatch each group as one body, and
            // register the defenders as a mission so SimpleAISystem.Tactics
            // concentrates them like any other army.
            var recalled = new System.Collections.Generic.List<Entity>();
            var defenders = new System.Collections.Generic.List<Entity>();

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                // Ritualists are never recalled. This recall is unconditional
                // — no busy/committed check at all — so it was the harshest of
                // the draft sites: a Corruptor or Scholar part-way through the
                // walk to a well got dragged home the instant the brain
                // flipped to Defend, and the endgame system re-dispatched it
                // 5 s later. Defending the base must not cost you the verb.
                if (IsVerbUnit(em, ents[i])) continue;
                float dx = xfs[i].Position.x - hallPos.x;
                float dz = xfs[i].Position.z - hallPos.z;
                bool home = dx * dx + dz * dz <= defendRadiusSq;

                if (!home)
                {
                    // Fielded army: recall toward the base, as a body.
                    recalled.Add(ents[i]);
                    continue;
                }

                // HOME defenders ENGAGE the intruder — but never yank a unit
                // already fighting (Target set) or already ordered.
                if (!threatFound) continue;
                Entity e = ents[i];
                if (em.HasComponent<Target>(e)
                    && em.GetComponentData<Target>(e).Value != Entity.Null) continue;
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                defenders.Add(e);
            }

            if (recalled.Count > 0)
                CommandRouter.IssueFormationAttackMove(
                    em, recalled, hallPos, FormationShape.Box, CommandSource.AI);

            if (threatFound && defenders.Count > 0)
            {
                CommandRouter.IssueFormationAttackMove(
                    em, defenders, threatPos, FormationShape.Box, CommandSource.AI);

                // Register the defence as a mission so the tactical layer picks
                // ONE priority target for it and keeps it together, instead of
                // every defender chasing whatever is nearest to itself.
                // Everything already home counts: the recalled units join on
                // arrival, through the same reinforcement path a wave uses.
                var defence = new Mission
                {
                    Type = MissionType.Attack,
                    Phase = MissionPhase.Direct,
                    Target = threatEnt,
                    TargetPos = threatPos,
                    StagePos = hallPos,
                    StartTime = (float)SystemAPI.Time.ElapsedTime,
                };
                defence.Members.AddRange(defenders);
                MissionsFor(faction).Add(defence);
            }

            // Repair: most-damaged completed building gets one idle builder.
            Entity worst = Entity.Null;
            float worstFrac = 0.85f; // only bother below 85% HP
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var bEnts = bq.ToEntityArray(Allocator.Temp);
            using var bFacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bHps = bq.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < bEnts.Length; i++)
            {
                if (bFacs[i].Value != faction) continue;
                if (bHps[i].Max <= 0 || bHps[i].Value <= 0) continue;
                if (em.HasComponent<UnderConstruction>(bEnts[i])) continue;
                float frac = bHps[i].Value / (float)bHps[i].Max;
                if (frac < worstFrac) { worstFrac = frac; worst = bEnts[i]; }
            }
            if (worst == Entity.Null) return;

            var cq = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var cEnts = cq.ToEntityArray(Allocator.Temp);
            using var cFacs = cq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < cEnts.Length; i++)
            {
                if (cFacs[i].Value != faction) continue;
                if (IsCommittedWorker(em, cEnts[i])) continue; // building/repairing already
                CommandRouter.IssueRepair(em, cEnts[i], worst, CommandSource.AI);
                break;
            }
        }
    }
}
