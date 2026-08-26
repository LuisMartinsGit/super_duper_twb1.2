// SectAntiquityMechanics.cs
// Runtime systems for the Sect of Antiquity's full mechanic set (task-063
// spec — implemented 2026-07-05):
//   * CodexFreezeTickSystem     — ticks/removes CodexFrozen (Recall the Codex).
//   * SectRevealTickSystem      — expires timed fog-reveal entities.
//   * LorekeeperDetectionSystem — stealth reveal aura + Lv III far-sight +
//                                 Reliquary garrison presence.
//   * ReliquarySystem           — ability cooldown recovery, scaled by
//                                 building level and Lorekeeper garrison.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    /// <summary>Ticks CodexFrozen (cooldown-recovery freeze) and removes it
    /// when expired. The freeze itself is enforced at the cooldown tick
    /// sites (Melee/Ranged/UnitAbility systems).</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CodexFreezeTickSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CodexFrozen>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var expired = new NativeList<Entity>(8, Allocator.Temp);
            foreach (var (frozen, entity) in SystemAPI
                .Query<RefRW<CodexFrozen>>().WithEntityAccess())
            {
                frozen.ValueRW.TimeRemaining -= dt;
                if (frozen.ValueRO.TimeRemaining <= 0f)
                    expired.Add(entity);
            }
            for (int i = 0; i < expired.Length; i++)
            {
                if (em.Exists(expired[i]) && em.HasComponent<CodexFrozen>(expired[i]))
                    em.RemoveComponent<CodexFrozen>(expired[i]);
            }
            expired.Dispose();
        }
    }

    /// <summary>Destroys timed fog-reveal entities when their timer ends
    /// (FogOfWarSystem stamps their vision while they live).</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectRevealTickSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SectRevealMarker>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var expired = new NativeList<Entity>(4, Allocator.Temp);
            foreach (var (marker, entity) in SystemAPI
                .Query<RefRW<SectRevealMarker>>().WithEntityAccess())
            {
                marker.ValueRW.TimeRemaining -= dt;
                if (marker.ValueRO.TimeRemaining <= 0f)
                    expired.Add(entity);
            }
            for (int i = 0; i < expired.Length; i++)
            {
                if (em.Exists(expired[i]))
                    em.DestroyEntity(expired[i]);
            }
            expired.Dispose();
        }
    }

    /// <summary>
    /// Lorekeeper behaviour (Antiquity unit lever), 0.5s cadence:
    ///   * Stamps StealthRevealed on stealthed enemies within detection
    ///     range — Lv I 6m, Lv II+ 12m (TargetingSystem honors the stamp).
    ///   * Lv III: far-sight — the Lorekeeper's LineOfSight is raised to 24m
    ///     ("aura grants sight through fog").
    ///   * Ticks StealthRevealed timers down and removes expired stamps.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class LorekeeperDetectionSystem : SystemBase
    {
        private const float TickInterval = 0.5f;
        private const float RevealHold = 1.0f;   // seconds a stamp outlives the tick
        private const float Lv3LineOfSight = 24f;

        // SimCadence, not a bare float — see SimCadence.cs. The detection
        // sweep stamps StealthRevealed, so an out-of-phase peer reveals a
        // moving unit a tick or two later and every downstream decision
        // that reads visibility diverges from there.
        private SimCadence.Periodic _cadence;

        protected override void OnCreate()
        {
            RequireForUpdate<LorekeeperTag>();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Tick existing stamps every frame (cheap; few entities).
            var expired = new NativeList<Entity>(4, Allocator.Temp);
            foreach (var (revealed, entity) in SystemAPI
                .Query<RefRW<StealthRevealed>>().WithEntityAccess())
            {
                revealed.ValueRW.TimeRemaining -= dt;
                if (revealed.ValueRO.TimeRemaining <= 0f)
                    expired.Add(entity);
            }
            for (int i = 0; i < expired.Length; i++)
            {
                if (em.Exists(expired[i]) && em.HasComponent<StealthRevealed>(expired[i]))
                    em.RemoveComponent<StealthRevealed>(expired[i]);
            }
            expired.Dispose();

            if (!_cadence.Due(dt, TickInterval)) return;

            // Snapshot stealthed units once.
            var stealthQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<StealthTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var stealthed = stealthQuery.ToEntityArray(Allocator.Temp);
            using var stealthedXf = stealthQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var stealthedFac = stealthQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var toStamp = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var (xf, faction, los, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>, RefRW<LineOfSight>>()
                .WithAll<LorekeeperTag>()
                .WithEntityAccess())
            {
                byte level = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    SectConfig.Antiquity, SectLeverKind.Unit);
                if (level == 0) level = 1;

                // Lv III far-sight.
                if (level >= 3 && los.ValueRO.Radius < Lv3LineOfSight)
                    los.ValueRW.Radius = Lv3LineOfSight;

                float detect = level >= 2 ? 12f : 6f;
                float d2 = detect * detect;
                float3 myPos = xf.ValueRO.Position;

                for (int i = 0; i < stealthed.Length; i++)
                {
                    if (stealthedFac[i].Value == faction.ValueRO.Value) continue;
                    float dx = stealthedXf[i].Position.x - myPos.x;
                    float dz = stealthedXf[i].Position.z - myPos.z;
                    if (dx * dx + dz * dz > d2) continue;
                    toStamp.Add(stealthed[i]);
                }
            }

            for (int i = 0; i < toStamp.Length; i++)
            {
                var e = toStamp[i];
                if (!em.Exists(e)) continue;
                if (em.HasComponent<StealthRevealed>(e))
                    em.SetComponentData(e, new StealthRevealed { TimeRemaining = RevealHold });
                else
                    em.AddComponentData(e, new StealthRevealed { TimeRemaining = RevealHold });
            }
            toStamp.Dispose();
        }
    }

    /// <summary>
    /// Reliquary ability cooldown recovery. Base recovery is realtime;
    /// building Lv III bakes a -30% base-cooldown discount at FIRE time
    /// (ReliquaryHelper), while a garrisoned Lorekeeper (within
    /// GarrisonRange) accelerates RECOVERY here: -15%/-30%/-50% cooldown by
    /// the Lorekeeper's unit-lever level, doubled by building Lv III
    /// ("garrison effects double"), capped at 80%.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ReliquarySystem : SystemBase
    {
        public const float GarrisonRange = 6f;

        protected override void OnCreate()
        {
            RequireForUpdate<ReliquaryTag>();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Snapshot Lorekeepers once for the garrison scan.
            var loreQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<LorekeeperTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var lores = loreQuery.ToEntityArray(Allocator.Temp);
            using var loreXf = loreQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var loreFac = loreQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            foreach (var (relState, xf, faction) in SystemAPI
                .Query<RefRW<ReliquaryState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<ReliquaryTag>()
                .WithNone<UnderConstruction>())
            {
                float speed = 1f;

                // Garrison: any own Lorekeeper within range accelerates
                // cooldown recovery by the unit-lever schedule.
                float g2 = GarrisonRange * GarrisonRange;
                for (int i = 0; i < lores.Length; i++)
                {
                    if (loreFac[i].Value != faction.ValueRO.Value) continue;
                    float dx = loreXf[i].Position.x - xf.ValueRO.Position.x;
                    float dz = loreXf[i].Position.z - xf.ValueRO.Position.z;
                    if (dx * dx + dz * dz > g2) continue;

                    byte unitLv = SectQuery.LevelOf(em, faction.ValueRO.Value,
                        SectConfig.Antiquity, SectLeverKind.Unit);
                    float reduction = unitLv switch { 3 => 0.50f, 2 => 0.30f, _ => 0.15f };
                    byte bldLv = SectQuery.LevelOf(em, faction.ValueRO.Value,
                        SectConfig.Antiquity, SectLeverKind.Building);
                    if (bldLv >= 3) reduction *= 2f;               // garrison effects double
                    reduction = math.min(0.8f, reduction);
                    speed = 1f / (1f - reduction);
                    break;
                }

                float step = dt * speed;
                ref var s = ref relState.ValueRW;
                if (s.ScryCooldown > 0f) s.ScryCooldown = math.max(0f, s.ScryCooldown - step);
                if (s.LockoutCooldown > 0f) s.LockoutCooldown = math.max(0f, s.LockoutCooldown - step);
                if (s.VisionCooldown > 0f) s.VisionCooldown = math.max(0f, s.VisionCooldown - step);
            }
        }
    }

    /// <summary>
    /// Fire-side helper for the Reliquary's three abilities. Level gating
    /// per the spec: Lv I = Scry only; Lv II = all three; Lv III = base
    /// cooldowns -30%.
    /// </summary>
    public static class ReliquaryHelper
    {
        public const float ScryBaseCooldown = 90f;
        public const float LockoutBaseCooldown = 120f;
        public const float VisionBaseCooldown = 75f;

        public const float ScryRadius = 10f;
        public const float ScryDuration = 10f;
        public const float LockoutRadius = 8f;
        public const float LockoutDuration = 6f;
        public const float VisionRadius = 18f;
        public const float VisionDuration = 15f;

        public static bool AbilityUnlocked(EntityManager em, Faction faction, int ability)
        {
            byte lv = SectQuery.LevelOf(em, faction, SectConfig.Antiquity, SectLeverKind.Building);
            if (lv == 0) lv = 1;                 // owning a Reliquary implies Lv I
            return ability == 0 || lv >= 2;      // 0 = Scry always; Lockout/Vision need Lv II
        }

        private static float CooldownFor(EntityManager em, Faction faction, float baseCd)
        {
            byte lv = SectQuery.LevelOf(em, faction, SectConfig.Antiquity, SectLeverKind.Building);
            return lv >= 3 ? baseCd * 0.7f : baseCd;
        }

        /// <summary>Fire ability 0=Scry (ground target), 1=Lockout (ground
        /// target), 2=Vision (self). Returns false when on cooldown/locked.</summary>
        public static bool Fire(EntityManager em, Entity reliquary, int ability, float3 target)
        {
            if (!em.Exists(reliquary) || !em.HasComponent<ReliquaryState>(reliquary)) return false;
            if (!em.HasComponent<FactionTag>(reliquary)) return false;
            var faction = em.GetComponentData<FactionTag>(reliquary).Value;
            if (!AbilityUnlocked(em, faction, ability)) return false;

            var s = em.GetComponentData<ReliquaryState>(reliquary);
            switch (ability)
            {
                case 0:
                    if (s.ScryCooldown > 0f) return false;
                    SectActivePowerHelper.SpawnReveal(em, faction, target, ScryRadius, ScryDuration);
                    s.ScryCooldown = CooldownFor(em, faction, ScryBaseCooldown);
                    break;
                case 1:
                    if (s.LockoutCooldown > 0f) return false;
                    SectActivePowerHelper.ApplyCooldownFreeze(em, faction, target,
                        LockoutRadius, LockoutDuration, surge: false);
                    s.LockoutCooldown = CooldownFor(em, faction, LockoutBaseCooldown);
                    break;
                case 2:
                    if (s.VisionCooldown > 0f) return false;
                    float3 selfPos = em.GetComponentData<LocalTransform>(reliquary).Position;
                    SectActivePowerHelper.SpawnReveal(em, faction, selfPos, VisionRadius, VisionDuration);
                    s.VisionCooldown = CooldownFor(em, faction, VisionBaseCooldown);
                    break;
                default:
                    return false;
            }
            em.SetComponentData(reliquary, s);
            return true;
        }
    }
}
