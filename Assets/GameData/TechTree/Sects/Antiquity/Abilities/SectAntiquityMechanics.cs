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
using TheWaningBorder.Core;
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

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every call
        // and these were never disposed, so this per-frame detection pass
        // leaked one per invocation. See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] QT_StealthTagLocalTransformFactionTag =
        {
            ComponentType.ReadOnly<StealthTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_StealthTagLocalTransformFactionTag;

        #endregion

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
                if (em.Exists(expired[i]))
                    TransientState.Clear<StealthRevealed>(em, expired[i]);
            }
            expired.Dispose();

            if (!_cadence.Due(dt, TickInterval)) return;

            // Snapshot stealthed units once.
            var stealthQuery = QC_StealthTagLocalTransformFactionTag.Get(em, QT_StealthTagLocalTransformFactionTag);
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
                // Enableable, pre-added on units (TransientState.cs).
                TransientState.Set(em, e, new StealthRevealed { TimeRemaining = RevealHold });
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

        #region Cached queries

        // Leaked one query per frame before being cached.
        // See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] QT_LorekeeperTagLocalTransformFactionTag =
        {
            ComponentType.ReadOnly<LorekeeperTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_LorekeeperTagLocalTransformFactionTag;

        #endregion

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
            var loreQuery = QC_LorekeeperTagLocalTransformFactionTag.Get(em, QT_LorekeeperTagLocalTransformFactionTag);
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
}
