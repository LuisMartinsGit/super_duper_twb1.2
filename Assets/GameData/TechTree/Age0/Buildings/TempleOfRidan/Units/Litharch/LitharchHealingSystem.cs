// LitharchHealingSystem.cs
// Processes healing for Litharch support units

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles Litharch healing behavior:
    /// 1. Processes explicit HealCommand (player right-clicks friendly unit)
    /// 2. Auto-searches for injured friendlies when idle — preferring patients
    ///    that are NOT in melee contact
    /// 3. Walks to a STAND-OFF point inside heal range (never onto the
    ///    patient) and heals over time
    /// 4. Steps away from an armed enemy that gets too close
    ///
    /// A support unit stays out of melee (docs/Design/Stances.md § Support
    /// units). A player / AI move order always wins: while UserMoveOrder is
    /// live nothing here touches the Litharch. The Hold stance keeps it
    /// planted: it heals only what is already in range and never steps away,
    /// unless the heal was ORDERED (orders win on every stance).
    /// Tunables: LitharchHealingSystem.asset; heal range / rate: Litharch.asset.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct LitharchHealingSystem : ISystem
    {
        private const float SearchInterval = 1.0f;
        private const float HealTickInterval = 1.0f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LitharchState>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Managed config read (this system is not Burst-compiled). A
            // missing asset is logged loudly by Require; no code-side default.
            var cfg = ComponentConfig.Require<LitharchHealingSystemConfig>();
            if (cfg == null) return;

            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            // Fix #225: Singleton ECB.
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Phase 1: Process explicit HealCommands
            foreach (var (healCmd, lithState, transform, entity) in SystemAPI
                         .Query<RefRO<HealCommand>, RefRW<LitharchState>, RefRO<LocalTransform>>()
                         .WithAll<LitharchTag>()
                         .WithEntityAccess())
            {
                var target = healCmd.ValueRO.Target;
                if (target != Entity.Null && em.Exists(target) && em.HasComponent<Health>(target)
                    && !em.HasComponent<UnhealableTag>(target))
                {
                    lithState.ValueRW.HealTarget = target;
                    lithState.ValueRW.IsHealing = 1;
                    lithState.ValueRW.Ordered = 1;
                }
                ecb.RemoveComponent<HealCommand>(entity);
            }

            // Armed, living units, gathered ONCE per frame on first need and
            // shared by every Litharch's threat / stand-point checks (it used
            // to be a full unit query per Litharch per check).
            var armed = new NativeList<ArmedUnit>(Allocator.Temp);
            bool armedBuilt = false;

            // Phase 2: Auto-search for injured friendlies when idle
            foreach (var (lithState, canHeal, factionTag, transform, entity) in SystemAPI
                         .Query<RefRW<LitharchState>, RefRO<CanHeal>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                         .WithAll<LitharchTag>()
                         .WithNone<HealCommand>()
                         .WithEntityAccess())
            {
                // UserMoveOrder = player/AI issued a manual command — cancel healing, obey command
                if (TransientState.Active<UserMoveOrder>(em, entity))
                {
                    if (lithState.ValueRO.IsHealing != 0)
                    {
                        lithState.ValueRW.HealTarget = Entity.Null;
                        lithState.ValueRW.IsHealing = 0;
                        lithState.ValueRW.Ordered = 0;
                    }
                    lithState.ValueRW.SteppingAway = 0;
                    continue;
                }
                // An ordered attack (Warrior Priests Litharch) is also an order
                // healing must not fight over the destination with.
                if (TransientState.Active<AttackCommand>(em, entity)) continue;

                var myPos = transform.ValueRO.Position;
                var myFaction = factionTag.ValueRO.Value;
                bool hold = StanceCommandHelper.Effective(em, entity) == UnitStanceMode.Hold;

                // ── Step away from an armed enemy that got too close. ──
                // Checked on its own cadence; a running step-away blocks the
                // heal movement below until the step is FINISHED — it reached
                // its destination, or ran out its time bound. Ending it on the
                // threat-check cadence (0.5 s) cut a 6 m step short at ~3 m
                // and the heal branch walked the healer straight back in: the
                // yo-yo.
                lithState.ValueRW.ThreatTimer -= dt;
                if (lithState.ValueRO.SteppingAway != 0)
                {
                    lithState.ValueRW.StepTimer -= dt;
                    bool arrived = DistXZ(myPos, lithState.ValueRO.StepTarget) <= cfg.stepArriveTolerance;
                    if (!arrived && lithState.ValueRO.StepTimer > 0f) continue;
                    lithState.ValueRW.SteppingAway = 0;
                    // Re-check at once: an enemy that followed gets another step.
                    lithState.ValueRW.ThreatTimer = 0f;
                }
                if (!hold && lithState.ValueRO.ThreatTimer <= 0f)
                {
                    lithState.ValueRW.ThreatTimer = cfg.threatCheckInterval;
                    EnsureArmed(ref state, em, armed, ref armedBuilt);
                    if (NearestArmedEnemy(armed, myPos, myFaction, cfg.threatRadius, out float3 threatPos))
                    {
                        float3 away = myPos - threatPos;
                        away.y = 0f;
                        float len = math.length(away);
                        away = len > 1e-3f ? away / len : new float3(0f, 0f, -1f);
                        float3 to = myPos + away * cfg.stepAwayDistance;
                        SetDestination(em, ref ecb, entity, to);
                        // The spot it stepped to is its post, so return-to-guard
                        // does not walk it straight back into the fight.
                        if (em.HasComponent<GuardPoint>(entity))
                            ecb.SetComponent(entity, new GuardPoint { Position = to, Has = 1 });
                        // Time bound derived from the unit's own speed (a
                        // floor of 1 m/s so a slowed healer still ends it).
                        float speed = em.HasComponent<MoveSpeed>(entity)
                            ? em.GetComponentData<MoveSpeed>(entity).Value : 0f;
                        lithState.ValueRW.StepTarget = to;
                        lithState.ValueRW.StepTimer =
                            cfg.stepAwayDistance / math.max(speed, 1f) + cfg.threatCheckInterval;
                        lithState.ValueRW.SteppingAway = 1;
                        continue;
                    }
                }

                // Skip if already has a valid heal target
                if (lithState.ValueRO.HealTarget != Entity.Null &&
                    em.Exists(lithState.ValueRO.HealTarget))
                {
                    var tgtHealth = em.GetComponentData<Health>(lithState.ValueRO.HealTarget);
                    if (tgtHealth.Value < tgtHealth.Max && tgtHealth.Value > 0)
                        goto ProcessHealing;
                    // Target is full HP or dead, clear it
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                    lithState.ValueRW.Ordered = 0;
                }

                // Search for injured nearby friendlies periodically
                lithState.ValueRW.SearchTimer += dt;
                if (lithState.ValueRO.SearchTimer < SearchInterval)
                    continue;
                lithState.ValueRW.SearchTimer = 0f;

                {
                    // On Hold it heals only what it can reach from where it stands.
                    float searchRange = hold ? canHeal.ValueRO.HealRange : cfg.autoSearchRange;
                    float healRange = canHeal.ValueRO.HealRange;
                    float standDist = math.max(1f, healRange - cfg.standOffMargin);
                    float bestFree = float.MaxValue, bestContact = float.MaxValue;
                    uint bestFreeKey = uint.MaxValue, bestContactKey = uint.MaxValue;
                    Entity freeTarget = Entity.Null, contactTarget = Entity.Null;
                    EnsureArmed(ref state, em, armed, ref armedBuilt);

                    foreach (var (tHealth, tFaction, tTransform, tEntity) in SystemAPI
                                 .Query<RefRO<Health>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                                 .WithAll<UnitTag>()
                                 .WithEntityAccess())
                    {
                        if (tEntity == entity) continue;
                        // Litharchs heal allies too. docs/Design/Teams.md
                        if (!Alliances.AreAllied(myFaction, tFaction.ValueRO.Value)) continue;
                        if (em.HasComponent<UnhealableTag>(tEntity)) continue;
                        if (tHealth.ValueRO.Value >= tHealth.ValueRO.Max) continue;
                        if (tHealth.ValueRO.Value <= 0) continue;

                        var tPos = tTransform.ValueRO.Position;
                        float dist = DistXZ(myPos, tPos);
                        if (dist >= searchRange) continue;

                        // A patient it would have to WALK to is only a
                        // candidate if the stand point it would walk to is
                        // clear of armed enemies; otherwise it waits for
                        // another patient rather than walking into the fight.
                        if (dist > healRange)
                        {
                            float3 stand = StandPoint(armed, tPos, myPos, myFaction, standDist, cfg.threatRadius);
                            if (ThreatNear(armed, stand, myFaction, cfg.threatRadius + cfg.standSafetyMargin))
                                continue;
                        }

                        // Exact-distance ties broken on a peer-stable key, not
                        // chunk order (lockstep).
                        uint key = StableKey(em, tEntity, tPos);

                        // Prefer a patient who is NOT trading blows: healing
                        // one in melee contact drags the healer into the fight.
                        if (InMeleeContact(em, tEntity, tPos, cfg.meleeContactRadius))
                        {
                            if (Better(dist, key, bestContact, bestContactKey))
                            { bestContact = dist; bestContactKey = key; contactTarget = tEntity; }
                        }
                        else if (Better(dist, key, bestFree, bestFreeKey))
                        { bestFree = dist; bestFreeKey = key; freeTarget = tEntity; }
                    }

                    Entity bestTarget = freeTarget != Entity.Null ? freeTarget : contactTarget;
                    if (bestTarget != Entity.Null)
                    {
                        lithState.ValueRW.HealTarget = bestTarget;
                        lithState.ValueRW.IsHealing = 1;
                        lithState.ValueRW.Ordered = 0;
                    }
                }

                ProcessHealing:

                // Phase 3: Move to target and heal
                if (lithState.ValueRO.HealTarget == Entity.Null) continue;
                if (!em.Exists(lithState.ValueRO.HealTarget))
                {
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                    lithState.ValueRW.Ordered = 0;
                    continue;
                }

                {
                    var healTarget = lithState.ValueRO.HealTarget;
                    var targetPos = em.GetComponentData<LocalTransform>(healTarget).Position;
                    float dist = DistXZ(myPos, targetPos);
                    float range = canHeal.ValueRO.HealRange;

                    if (dist > range)
                    {
                        // Hold: never moves on its own — an AUTO patient that
                        // walked out of reach is let go. An ordered heal is
                        // followed on every stance.
                        if (hold && lithState.ValueRO.Ordered == 0)
                        {
                            lithState.ValueRW.HealTarget = Entity.Null;
                            lithState.ValueRW.IsHealing = 0;
                            continue;
                        }

                        // Walk to a STAND-OFF point, not to the patient,
                        // (healRange − margin) out from the patient — on the
                        // side AWAY from the nearest enemy when one is near it,
                        // else on the patient→Litharch line (the RitualApproach
                        // pattern). Pathing to the patient's exact spot walked
                        // the healer into the melee it was healing.
                        EnsureArmed(ref state, em, armed, ref armedBuilt);
                        float3 stand = StandPoint(armed, targetPos, myPos, myFaction,
                            math.max(1f, range - cfg.standOffMargin), cfg.threatRadius);

                        // Never walk in to an unsafe stand point. Hold here
                        // (and make here the post, so return-to-guard does not
                        // walk it in either); an AUTO patient is let go so the
                        // next search picks a safer one, an ORDERED one is
                        // waited on until the point clears.
                        if (ThreatNear(armed, stand, myFaction, cfg.threatRadius + cfg.standSafetyMargin))
                        {
                            TheWaningBorder.Core.TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);
                            if (em.HasComponent<GuardPoint>(entity))
                                ecb.SetComponent(entity, new GuardPoint { Position = myPos, Has = 1 });
                            if (lithState.ValueRO.Ordered == 0)
                            {
                                lithState.ValueRW.HealTarget = Entity.Null;
                                lithState.ValueRW.IsHealing = 0;
                            }
                            continue;
                        }

                        bool rewrite = true;
                        if (em.HasComponent<DesiredDestination>(entity))
                        {
                            var dd = em.GetComponentData<DesiredDestination>(entity);
                            // Same point as already set — do not churn the
                            // path request every frame.
                            rewrite = dd.Has == 0 || DistXZ(dd.Position, stand) > 1f;
                        }
                        if (rewrite) SetDestination(em, ref ecb, entity, stand);

                        // Guard from the stand point (NOT the patient), so
                        // return-to-guard neither snaps the healer home nor
                        // parks it on top of the fight when the heal ends.
                        if (em.HasComponent<GuardPoint>(entity))
                            ecb.SetComponent(entity, new GuardPoint { Position = stand, Has = 1 });
                    }
                    else
                    {
                        // In range - plant, face the patient, and heal
                        TheWaningBorder.Core.TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);

                        lithState.ValueRW.HealTimer += dt;
                        if (lithState.ValueRO.HealTimer >= HealTickInterval)
                        {
                            lithState.ValueRW.HealTimer = 0f;

                            var targetHealth = em.GetComponentData<Health>(healTarget);
                            float healRate = canHeal.ValueRO.HealRate;

                            int healAmount = (int)(healRate * HealTickInterval);
                            if (healAmount < 1) healAmount = 1;

                            targetHealth.Value = math.min(targetHealth.Value + healAmount, targetHealth.Max);
                            // Fix #228: write health immediately via EntityManager
                            // to match how combat systems apply damage. ECB playback
                            // happens AFTER Melee/RangedCombatSystem in the same
                            // frame, so an ECB heal write would overwrite damage
                            // dealt this frame — effectively nullifying it.
                            em.SetComponentData(healTarget, targetHealth);

                            // If fully healed, clear target
                            if (targetHealth.Value >= targetHealth.Max)
                            {
                                lithState.ValueRW.HealTarget = Entity.Null;
                                lithState.ValueRW.IsHealing = 0;
                                lithState.ValueRW.Ordered = 0;
                            }
                        }
                    }
                }
            }

            armed.Dispose();
            // ECB plays back automatically at EndSimulation.
        }

        /// <summary>An armed, living unit, as the threat checks see it.</summary>
        private struct ArmedUnit
        {
            public float3 Pos;
            public Faction Faction;
            public uint Key;
        }

        /// <summary>Fill <paramref name="armed"/> once per frame, on first need.
        /// NativeList is a handle, so the by-value parameter fills the caller's list.</summary>
        private void EnsureArmed(ref SystemState state, EntityManager em,
            NativeList<ArmedUnit> armed, ref bool built)
        {
            if (built) return;
            built = true;
            foreach (var (eHealth, eFaction, eXf, eDmg, e) in SystemAPI
                         .Query<RefRO<Health>, RefRO<FactionTag>, RefRO<LocalTransform>, RefRO<Damage>>()
                         .WithAll<UnitTag>()
                         .WithNone<DeathAnimationState>()
                         .WithEntityAccess())
            {
                if (eDmg.ValueRO.Value <= 0 || eHealth.ValueRO.Value <= 0) continue;
                armed.Add(new ArmedUnit
                {
                    Pos = eXf.ValueRO.Position,
                    Faction = eFaction.ValueRO.Value,
                    Key = StableKey(em, e, eXf.ValueRO.Position),
                });
            }
        }

        /// <summary>A peer-stable identity for tie-breaks: the lockstep
        /// NetworkId, else the position bits. Never chunk order, which differs
        /// between peers.</summary>
        private static uint StableKey(EntityManager em, Entity e, float3 pos)
        {
            if (em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(e))
                return (uint)em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(e).NetworkId;
            return math.hash(pos) | 0x80000000u;
        }

        private static bool Better(float d, uint key, float bestD, uint bestKey)
            => d < bestD || (d == bestD && key < bestKey);

        /// <summary>Where to stand to heal the patient: <paramref name="standDist"/>
        /// out from it, on the side AWAY from the nearest armed enemy within
        /// standDist + threatRadius of the patient (so the healer is not placed
        /// in, or pathed through, the melee); with no such enemy, on the
        /// patient→healer line. Degenerate directions fall back to a fixed
        /// bearing, so the result is the same on every peer.</summary>
        private static float3 StandPoint(NativeList<ArmedUnit> armed, float3 patient, float3 healer,
            Faction myFaction, float standDist, float threatRadius)
        {
            float3 d;
            if (NearestArmedEnemy(armed, patient, myFaction, standDist + threatRadius, out float3 enemy))
                d = patient - enemy;
            else
                d = healer - patient;
            d.y = 0f;
            float len = math.length(d);
            d = len > 1e-3f ? d / len : new float3(0f, 0f, -1f);
            return patient + d * standDist;
        }

        /// <summary>Is any armed hostile within <paramref name="radius"/>?</summary>
        private static bool ThreatNear(NativeList<ArmedUnit> armed, float3 pos, Faction myFaction, float radius)
        {
            for (int i = 0; i < armed.Length; i++)
            {
                var a = armed[i];
                if (!Alliances.AreHostile(myFaction, a.Faction)) continue;
                if (DistXZ(pos, a.Pos) <= radius) return true;
            }
            return false;
        }

        /// <summary>Is this patient trading blows — its own target, or the unit
        /// that last hit it, standing within <paramref name="radius"/>?</summary>
        private static bool InMeleeContact(EntityManager em, Entity patient, float3 pos, float radius)
        {
            if (em.HasComponent<Target>(patient))
            {
                var t = em.GetComponentData<Target>(patient).Value;
                if (t != Entity.Null && em.Exists(t) && em.HasComponent<LocalTransform>(t)
                    && DistXZ(pos, em.GetComponentData<LocalTransform>(t).Position) <= radius)
                    return true;
            }
            if (TransientState.Active<LastAttackerEntity>(em, patient))
            {
                var a = em.GetComponentData<LastAttackerEntity>(patient).Value;
                if (a != Entity.Null && em.Exists(a) && em.HasComponent<LocalTransform>(a)
                    && DistXZ(pos, em.GetComponentData<LocalTransform>(a).Position) <= radius)
                    return true;
            }
            return false;
        }

        /// <summary>The nearest hostile, living, ARMED unit within
        /// <paramref name="radius"/> of <paramref name="pos"/>.</summary>
        private static bool NearestArmedEnemy(NativeList<ArmedUnit> armed, float3 pos,
            Faction myFaction, float radius, out float3 threatPos)
        {
            threatPos = default;
            float best = float.MaxValue;
            uint bestKey = uint.MaxValue;
            bool found = false;
            for (int i = 0; i < armed.Length; i++)
            {
                var a = armed[i];
                if (!Alliances.AreHostile(myFaction, a.Faction)) continue;
                float d = DistXZ(pos, a.Pos);
                if (d > radius) continue;
                // Peer-stable tie-break (the old '<=' kept the LAST in chunk
                // order, which can differ between lockstep peers).
                if (Better(d, a.Key, best, bestKey)) { best = d; bestKey = a.Key; threatPos = a.Pos; found = true; }
            }
            return found;
        }

        private static void SetDestination(EntityManager em, ref EntityCommandBuffer ecb, Entity e, float3 pos)
        {
            if (em.HasComponent<DesiredDestination>(e))
                ecb.SetComponent(e, new DesiredDestination { Position = pos, Has = 1 });
            else
                ecb.AddComponent(e, new DesiredDestination { Position = pos, Has = 1 });
        }
    }
}
