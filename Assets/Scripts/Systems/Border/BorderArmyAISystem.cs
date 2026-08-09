// BorderArmyAISystem.cs
// Per-node army brain for the The Border. Each main node acts as its own
// border "faction": a PRIVATE veilstone bank (BorderNodeBank) plus two army slots
// (BorderNodeArmies) — one DEFEND, one ATTACK — sized from BorderSettings tiers.
//
//   * Income — every node earns veilstone from its own territory + its green-
//     veilstone (Resource) sub-nodes into its own bank.
//   * Defend slot — held at the node; dead members are replenished for FREE
//     while the node owns at least one Resource sub-node. Upgraded to a higher
//     tier for the full upgrade cost (kept units count toward the new size).
//   * Attack slot — fielded for the tier's full train cost, mustered at the
//     node, then marches (BorderHordeSystem) to seek a RANDOM player until the
//     army or all enemies die. Upgrading recalls it home and costs the full
//     upgrade cost. No replenishment — when it dies the slot frees up and the
//     node fields a fresh (often larger) one.
//
// Units are stamped with OwnerNode (their node) + BorderArmyRole (their slot);
// BorderHordeSystem reads both to move each node's two armies independently.
//
// Determinism (lockstep): fixed-step World.Time only; random target pick uses
// the same lockstep-tick seed pattern as BorderAISystem; stable chunk order.
//
// SystemBase (not ISystem) because the unit factories do structural changes.
//
// Location: Assets/Scripts/Systems/Border/BorderArmyAISystem.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.AI;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Data.Border;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BorderArmyAISystem : SystemBase
    {
        // Income + training advance on this cadence; higher-level fielding /
        // upgrade decisions fire on the SO's decisionInterval.
        private const float ControlInterval = 1f;

        private float _acc;
        private float _decisionAcc;
        private uint _tick;

        private EntityQuery _mainNodeQuery;
        private EntityQuery _subNodeQuery;
        private EntityQuery _borderUnitQuery;
        private EntityQuery _hallQuery;

        // Per-node tally of current army members, rebuilt each control tick.
        private struct Tally
        {
            public int dC, dV, dG;          // defenders by type
            public int aC, aV, aG;          // attackers by type
            public float3 aSum; public int aCount; // attacker centroid accumulator
        }

        protected override void OnCreate()
        {
            // §2.5: the curse is a force, not a faction — it fields no army.
            Enabled = TheWaningBorder.Core.Config.BorderConstants.CurseFieldsArmies;
            RequireForUpdate<BorderMainNodeTag>();

            _mainNodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<BorderMainNodeTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<BorderNode>(),
                },
                None = new[] { ComponentType.ReadOnly<NodeDormant>() },
            });

            _subNodeQuery = GetEntityQuery(
                ComponentType.ReadOnly<BorderSubNodeTag>(),
                ComponentType.ReadOnly<OwnerNode>());

            _borderUnitQuery = GetEntityQuery(
                ComponentType.ReadOnly<BorderUnitTag>(),
                ComponentType.ReadOnly<BorderArmyRole>(),
                ComponentType.ReadOnly<OwnerNode>(),
                ComponentType.ReadOnly<LocalTransform>());

            _hallQuery = GetEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        // Faction currently holding the Shardroot (Faction.Border = none).
        // Refreshed once per control tick; PickTarget biases toward it
        // ("the curse wants it back", canon §3.1).
        private Faction _shardrootHolder = Faction.Border;

        protected override void OnUpdate()
        {
            float dt = World.Time.DeltaTime;
            _acc += dt;
            if (_acc < ControlInterval) return;
            float step = _acc;
            _acc = 0f;

            var s = BorderSettings.Get();
            if (s == null || s.TierCount == 0) return;
            int maxTier = s.TierCount - 1;

            _shardrootHolder = Faction.Border;
            var shardQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ShardrootState>());
            if (!shardQuery.IsEmptyIgnoreFilter)
            {
                using var shardEnts = shardQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                _shardrootHolder = EntityManager
                    .GetComponentData<ShardrootState>(shardEnts[0]).HolderFaction;
            }

            // Higher-level decisions (field / upgrade) fire on the slower cadence.
            _decisionAcc += step;
            bool decide = _decisionAcc >= s.decisionInterval;
            if (decide) _decisionAcc = 0f;

            var em = EntityManager;

            // Deterministic RNG seed (mirror BorderAISystem).
            if (GameSettings.IsMultiplayer && LockstepServiceLocator.IsActive)
                _tick = (uint)LockstepServiceLocator.Instance.CurrentTick;
            else
                _tick++;
            var rng = new Unity.Mathematics.Random(_tick * 7919u + (uint)GameSettings.SpawnSeed + 13u);

            // ── Living player factions (have a standing Hall) ──────────────
            var players = new NativeList<Faction>(8, Allocator.Temp);
            var playerHallPos = new NativeList<float3>(8, Allocator.Temp);
            {
                using var hf = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var hh = _hallQuery.ToComponentDataArray<Health>(Allocator.Temp);
                using var hx = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < hf.Length; i++)
                {
                    if (hf[i].Value == Faction.Border) continue;
                    if (hh[i].Value <= 0) continue;
                    bool seen = false;
                    for (int j = 0; j < players.Length; j++) if (players[j] == hf[i].Value) { seen = true; break; }
                    if (!seen) { players.Add(hf[i].Value); playerHallPos.Add(hx[i].Position); }
                }
            }

            // M5 escalation, smoothed: a continuous ramp (SO: escalationStart/
            // FullMinute) instead of the old discrete 0/1/2 steps at 5/15 min,
            // so border power grows gradually rather than jumping. BorderAISystem
            // keeps its discrete phases for structural sub-node unlocks.
            double elapsedTime = World.Time.ElapsedTime;
            float phase = s.EscalationPhase(elapsedTime);
            float incomeScale = 1f + phase * math.max(0f, s.phaseIncomeBonus);
            float trainScale = 1f + phase * math.max(0f, s.phaseTrainSpeedBonus);

            // ── Resource sub-nodes per owning main node ────────────────────
            // DETERMINISM: these Entity-keyed managed dictionaries are used for
            // O(1) LOOKUP ONLY (TryGetValue). Never enumerate .Keys/.Values/
            // GetEnumerator on them to drive a sim decision — managed-dict
            // iteration order is non-deterministic and would desync lockstep.
            // All ordered work walks the query arrays (deterministic chunk order).
            var resCount = new Dictionary<Entity, int>();
            {
                using var sTags = _subNodeQuery.ToComponentDataArray<BorderSubNodeTag>(Allocator.Temp);
                using var sOwn = _subNodeQuery.ToComponentDataArray<OwnerNode>(Allocator.Temp);
                for (int i = 0; i < sTags.Length; i++)
                {
                    if (sTags[i].Type != BorderSubNodeType.Resource) continue;
                    var o = sOwn[i].Value;
                    resCount.TryGetValue(o, out int c);
                    resCount[o] = c + 1;
                }
            }

            // ── Current army tallies per node ──────────────────────────────
            var tally = new Dictionary<Entity, Tally>();
            {
                using var uOwn = _borderUnitQuery.ToComponentDataArray<OwnerNode>(Allocator.Temp);
                using var uRole = _borderUnitQuery.ToComponentDataArray<BorderArmyRole>(Allocator.Temp);
                using var uXf = _borderUnitQuery.ToEntityArray(Allocator.Temp);
                using var uPos = _borderUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < uOwn.Length; i++)
                {
                    var node = uOwn[i].Value;
                    var e = uXf[i];
                    byte type = UnitType(em, e);
                    tally.TryGetValue(node, out var t);
                    if (uRole[i].Role == BorderArmyRoleType.Attack)
                    {
                        if (type == 3) t.aG++; else if (type == 2) t.aV++; else t.aC++;
                        t.aSum += uPos[i].Position; t.aCount++;
                    }
                    else
                    {
                        if (type == 3) t.dG++; else if (type == 2) t.dV++; else t.dC++;
                    }
                    tally[node] = t;
                }
            }

            // ── Per main node ──────────────────────────────────────────────
            using var nodes = _mainNodeQuery.ToEntityArray(Allocator.Temp);
            using var nodeXf = _mainNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int n = 0; n < nodes.Length; n++)
            {
                Entity node = nodes[n];
                float3 nodePos = nodeXf[n].Position;

                // Only Active nodes run armies (Converted/Cleansed/Destroyed skip).
                if (em.HasComponent<BorderNodeState>(node)
                    && em.GetComponentData<BorderNodeState>(node).State != NodeState.Active)
                    continue;

                // Lazy-add the per-node bank + army slots.
                if (!em.HasComponent<BorderNodeBank>(node))
                    em.AddComponentData(node, new BorderNodeBank { Veilstone = s.startingCrystal, IncomeAccum = 0f });
                if (!em.HasComponent<BorderNodeArmies>(node))
                    em.AddComponentData(node, new BorderNodeArmies
                    {
                        DefendTier = 0,
                        AttackTier = -1,
                        AttackState = BorderAttackState.Mustering,
                        HasAttackTarget = 0,
                        TrainingUnitType = 0,
                        Initialised = 1,
                        // Early-game grace: the first attack wave cannot field
                        // before firstWaveDelaySeconds of game time.
                        RefieldCooldown = math.max(0f,
                            s.firstWaveDelaySeconds - (float)elapsedTime),
                    });

                var bank = em.GetComponentData<BorderNodeBank>(node);
                var army = em.GetComponentData<BorderNodeArmies>(node);
                int resN = resCount.TryGetValue(node, out int rc) ? rc : 0;
                tally.TryGetValue(node, out var t);

                // ── Income (phase-scaled, M5) ──────────────────────────────
                bank.IncomeAccum += (s.baseIncomePerSecond + resN * s.incomePerResourceNode) * incomeScale * step;
                if (bank.IncomeAccum >= 1f)
                {
                    int whole = (int)bank.IncomeAccum;
                    bank.IncomeAccum -= whole;
                    bank.Veilstone += whole;
                }

                // ── Attack-slot state machine ──────────────────────────────
                HandleAttack(ref army, ref bank, s, t, players, ref rng, maxTier, decide,
                    nodePos, step, elapsedTime);

                // ── Defend-slot upgrades (offense first) ───────────────────
                if (decide && army.AttackTier >= 0
                    && army.DefendTier < maxTier
                    && bank.Veilstone >= s.Tier(army.DefendTier + 1).upgradeCost)
                {
                    bank.Veilstone -= s.Tier(army.DefendTier + 1).upgradeCost;
                    army.DefendTier++;
                }

                // ── Training (one unit at a time, phase-scaled) ────────────
                AdvanceTraining(ref army, node, nodePos, t, s, resN, step, trainScale, ref rng);

                em.SetComponentData(node, bank);
                em.SetComponentData(node, army);
            }

            players.Dispose();
            playerHallPos.Dispose();
        }

        // ── Attack slot: field / muster / retarget / recall ────────────────
        private void HandleAttack(ref BorderNodeArmies army, ref BorderNodeBank bank,
            BorderSettingsSO s, Tally t, NativeList<Faction> players,
            ref Unity.Mathematics.Random rng, int maxTier, bool decide, float3 nodePos,
            float step, double elapsedTime)
        {
            // Wave schedule (SO): caps the fielded tier + sets the per-window
            // breather so wave power ramps on the authored curve instead of
            // "biggest tier the bank happens to afford".
            bool scheduled = s.TryGetWave(elapsedTime, out int schedTier, out float breather);
            int tierCap = scheduled ? math.min(schedTier, maxTier) : maxTier;

            // No army fielded → field up to the scheduled tier (decision tick).
            if (army.AttackTier < 0)
            {
                // Breathing space between waves: after an attack army dies,
                // the node waits out RefieldCooldown before fielding the next
                // one (on top of the muster/training time the fresh army
                // needs anyway).
                if (army.RefieldCooldown > 0f)
                {
                    army.RefieldCooldown = math.max(0f, army.RefieldCooldown - step);
                    return;
                }
                if (!decide || players.Length == 0) return;
                int best = -1;
                for (int ti = tierCap; ti >= 0; ti--)
                    if (bank.Veilstone >= s.Tier(ti).trainCost) { best = ti; break; }
                if (best < 0) return;
                bank.Veilstone -= s.Tier(best).trainCost;
                army.AttackTier = best;
                army.AttackState = BorderAttackState.Mustering;
                army.HasAttackTarget = 0;
                return;
            }

            var tier = s.Tier(army.AttackTier);
            bool full = t.aC >= tier.crystallings && t.aV >= tier.veilstingers && t.aG >= tier.godsplinters;

            switch (army.AttackState)
            {
                case BorderAttackState.Mustering:
                    if (full)
                    {
                        army.AttackState = BorderAttackState.Attacking;
                        PickTarget(ref army, players, ref rng, _shardrootHolder);
                    }
                    break;

                case BorderAttackState.Attacking:
                    if (t.aCount == 0)
                    {
                        // Army wiped — free the slot and start the breather so
                        // the next wave is fielded no sooner than the current
                        // schedule window's breather (fallback: waveBreatherSeconds).
                        army.AttackTier = -1;
                        army.HasAttackTarget = 0;
                        army.RefieldCooldown = math.max(0f, breather);
                        break;
                    }
                    // Retarget if the current player is gone.
                    if (army.HasAttackTarget == 0 || !Contains(players, army.AttackTarget))
                        PickTarget(ref army, players, ref rng, _shardrootHolder);

                    // Optional escalation: upgrade to the next tier (recall home).
                    // Capped by the wave schedule so a rich node can't outrun
                    // the authored power curve mid-attack.
                    if (decide && army.AttackTier < tierCap
                        && bank.Veilstone >= s.Tier(army.AttackTier + 1).upgradeCost)
                    {
                        bank.Veilstone -= s.Tier(army.AttackTier + 1).upgradeCost;
                        army.AttackTier++;
                        army.AttackState = BorderAttackState.Recalling;
                    }
                    break;

                case BorderAttackState.Recalling:
                    if (t.aCount == 0)
                    {
                        army.AttackState = BorderAttackState.Mustering; // rebuild at new tier
                        break;
                    }
                    // BorderHordeSystem marches recalling attackers to the node;
                    // once they've regrouped there, resume mustering (now against
                    // the upgraded tier — kept units count, the rest train up).
                    float3 c = t.aSum / math.max(1, t.aCount);
                    if (math.distance(c, nodePos) <= s.recallArriveRadius)
                        army.AttackState = BorderAttackState.Mustering;
                    break;
            }
        }

        // ── Training: one unit at a time per node ──────────────────────────
        private void AdvanceTraining(ref BorderNodeArmies army, Entity node, float3 nodePos,
            Tally t, BorderSettingsSO s, int resN, float step, float trainScale, ref Unity.Mathematics.Random rng)
        {
            var em = EntityManager;

            // Finish the in-progress unit.
            if (army.TrainingUnitType != 0)
            {
                army.TrainTimer -= step;
                if (army.TrainTimer > 0f) return;

                float3 pos = SpawnPos(nodePos, ref rng);
                Entity u = army.TrainingUnitType switch
                {
                    2 => Veilstinger.Create(em, pos, Faction.Border),
                    3 => Godsplinter.Create(em, pos, Faction.Border),
                    _ => Crystalling.Create(em, pos, Faction.Border),
                };
                if (u != Entity.Null)
                {
                    var role = army.TrainingForAttack != 0 ? BorderArmyRoleType.Attack : BorderArmyRoleType.Defend;
                    if (em.HasComponent<OwnerNode>(u)) em.SetComponentData(u, new OwnerNode { Value = node });
                    else em.AddComponentData(u, new OwnerNode { Value = node });
                    if (em.HasComponent<BorderArmyRole>(u)) em.SetComponentData(u, new BorderArmyRole { Role = role });
                    else em.AddComponentData(u, new BorderArmyRole { Role = role });
                }
                army.TrainingUnitType = 0;
                return;
            }

            // Decide what to train next. Attack mustering takes priority.
            byte type = 0;
            byte forAttack = 0;

            if (army.AttackTier >= 0 && army.AttackState == BorderAttackState.Mustering)
            {
                var tier = s.Tier(army.AttackTier);
                type = MissingType(t.aC, t.aV, t.aG, tier);
                if (type != 0) forAttack = 1;
            }

            if (type == 0)
            {
                // Defend replenishment: free, but needs a green-veilstone node.
                bool gateOk = !s.replenishNeedsResourceNode || resN > 0;
                if (gateOk && army.DefendTier >= 0)
                {
                    var dt = s.Tier(army.DefendTier);
                    type = MissingType(t.dC, t.dV, t.dG, dt);
                    forAttack = 0;
                }
            }

            if (type == 0) return;

            army.TrainingUnitType = type;
            army.TrainingForAttack = forAttack;
            // M5: higher escalation phases train faster.
            army.TrainTimer = s.TrainTime(type) / math.max(0.01f, trainScale);
        }

        // Train order within a tier: Crystallings → Veilstingers → Godsplinters.
        private static byte MissingType(int c, int v, int g, BorderSettingsSO.ArmyTier tier)
        {
            if (c < tier.crystallings) return 1;
            if (v < tier.veilstingers) return 2;
            if (g < tier.godsplinters) return 3;
            return 0;
        }

        /// <summary>
        /// Per-node-independent target pick — uniform random, EXCEPT when a
        /// player holds the SHARDROOT (Curse & Shardroot canon §3.1: "the
        /// curse wants it back"): the Border prioritizes the holder with a
        /// 60% draw, spreading the rest uniformly. The anti-snowball valve —
        /// holding the strongest object paints the map's target on you.
        /// The old M5 weakest-defense bias stays removed by design.
        /// </summary>
        private static void PickTarget(ref BorderNodeArmies army, NativeList<Faction> players,
            ref Unity.Mathematics.Random rng, Faction shardrootHolder)
        {
            if (players.Length == 0) { army.HasAttackTarget = 0; return; }

            if (shardrootHolder != Faction.Border
                && Contains(players, shardrootHolder)
                && rng.NextFloat() < 0.6f)
            {
                army.AttackTarget = shardrootHolder;
                army.HasAttackTarget = 1;
                return;
            }

            int pick = rng.NextInt(0, players.Length);
            army.AttackTarget = players[pick];
            army.HasAttackTarget = 1;
        }

        private static bool Contains(NativeList<Faction> players, Faction f)
        {
            for (int i = 0; i < players.Length; i++) if (players[i] == f) return true;
            return false;
        }

        private static byte UnitType(EntityManager em, Entity e)
        {
            if (em.HasComponent<GodsplinterState>(e)) return 3;
            if (em.HasComponent<VeilstingerState>(e)) return 2;
            return 1; // Crystalling
        }

        private static float3 SpawnPos(float3 nodePos, ref Unity.Mathematics.Random rng)
        {
            float a = rng.NextFloat(0f, math.PI * 2f);
            float r = rng.NextFloat(3f, 8f);
            float x = nodePos.x + math.cos(a) * r;
            float z = nodePos.z + math.sin(a) * r;
            return new float3(x, TerrainUtility.GetHeight(x, z), z);
        }
    }
}
