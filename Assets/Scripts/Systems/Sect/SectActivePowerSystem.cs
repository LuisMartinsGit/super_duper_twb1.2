// SectActivePowerSystem.cs
// Active-Power lever dispatch (task-063 phase 5). Each adopted sect
// exposes one triggered ability per the SectLeverEffects.ActiveOf table;
// players (and AI) request a cast via SectActivePowerHelper.Fire which
// validates cooldown + lever level, deducts the cooldown, and then
// dispatches to the per-kind handler in SwitchOnKind.
//
// The cooldown lives in a DynamicBuffer<SectActivePowerCooldown> on the
// faction bank entity so all 12 sects' timers per faction live in one
// place. SectActivePowerSystem ticks all cooldowns and prunes entries
// at zero.
//
// Magnitudes scale with the Active-Power lever level via
// SectLeverEffects.LevelScalar; cooldowns scale inversely.
//
// task-063 phase 5.
//
// Location: Assets/Scripts/Systems/Sect/SectActivePowerSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectActivePowerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // No specific RequireForUpdate — runs every tick to bleed cooldowns.
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Snapshot entities first — DynamicBuffer iteration variables are
            // read-only inside a SystemAPI foreach, so we re-fetch the buffer
            // via EntityManager.GetBuffer for the mutating pass.
            var bankEntities = new Unity.Collections.NativeList<Entity>(
                Unity.Collections.Allocator.Temp);
            foreach (var (_, entity) in SystemAPI
                .Query<DynamicBuffer<SectActivePowerCooldown>>()
                .WithEntityAccess())
            {
                bankEntities.Add(entity);
            }

            for (int b = 0; b < bankEntities.Length; b++)
            {
                var bank = bankEntities[b];
                if (!em.Exists(bank)) continue;
                if (!em.HasBuffer<SectActivePowerCooldown>(bank)) continue;
                var cooldowns = em.GetBuffer<SectActivePowerCooldown>(bank);
                for (int i = cooldowns.Length - 1; i >= 0; i--)
                {
                    var cd = cooldowns[i];
                    cd.Remaining -= dt;
                    if (cd.Remaining <= 0f)
                        cooldowns.RemoveAtSwapBack(i);
                    else
                        cooldowns[i] = cd;
                }
            }
            bankEntities.Dispose();
        }
    }

    /// <summary>
    /// Static helper for firing a sect's Active Power. UI buttons /
    /// hotkeys / AI all funnel through Fire so the cooldown + spec
    /// lookup live in one place.
    /// </summary>
    public static class SectActivePowerHelper
    {
        /// <summary>
        /// Returns the remaining cooldown for the faction's sect Active
        /// Power, or 0 if ready (or if the lever isn't bought).
        /// </summary>
        public static float CooldownRemaining(EntityManager em, Faction faction, string sectId)
        {
            int sectIdx = SectConfig.IndexOf(sectId);
            if (sectIdx < 0) return float.MaxValue;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return float.MaxValue;
            if (!em.HasBuffer<SectActivePowerCooldown>(bank)) return 0f;
            var buf = em.GetBuffer<SectActivePowerCooldown>(bank);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].SectIndex == sectIdx) return buf[i].Remaining;
            return 0f;
        }

        public static bool CanFire(EntityManager em, Faction faction, string sectId)
        {
            byte level = SectQuery.LevelOf(em, faction, sectId, SectLeverKind.ActivePower);
            if (level == 0) return false;
            return CooldownRemaining(em, faction, sectId) <= 0f;
        }

        /// <summary>
        /// Attempt to fire <paramref name="sectId"/>'s Active Power for
        /// <paramref name="faction"/> at <paramref name="targetPos"/>.
        /// Returns true if the cast succeeded (cooldown is set and the
        /// effect was dispatched), false otherwise.
        /// </summary>
        public static bool Fire(EntityManager em, Faction faction, string sectId, float3 targetPos)
        {
            byte level = SectQuery.LevelOf(em, faction, sectId, SectLeverKind.ActivePower);
            if (level == 0) return false;
            if (CooldownRemaining(em, faction, sectId) > 0f) return false;

            var spec = SectLeverEffects.ActiveOf(sectId);
            if (spec.Kind == SectActivePowerKind.None) return false;

            // Magnitude scales with level; cooldown scales inversely.
            float scalar = SectLeverEffects.LevelScalar(level);
            float radius = spec.Radius;
            float magnitude = spec.Magnitude * scalar;
            float duration = spec.Duration;
            float cooldown = spec.Cooldown / scalar;

            DispatchEffect(em, faction, spec.Kind, targetPos, radius, magnitude, duration);

            // Stamp cooldown.
            int sectIdx = SectConfig.IndexOf(sectId);
            if (FactionEconomy.TryGetBank(em, faction, out var bank))
            {
                DynamicBuffer<SectActivePowerCooldown> buf;
                if (!em.HasBuffer<SectActivePowerCooldown>(bank))
                    buf = em.AddBuffer<SectActivePowerCooldown>(bank);
                else
                    buf = em.GetBuffer<SectActivePowerCooldown>(bank);

                bool found = false;
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i].SectIndex == sectIdx)
                    {
                        buf[i] = new SectActivePowerCooldown { SectIndex = (byte)sectIdx, Remaining = cooldown };
                        found = true; break;
                    }
                }
                if (!found)
                    buf.Add(new SectActivePowerCooldown { SectIndex = (byte)sectIdx, Remaining = cooldown });
            }
            return true;
        }

        private static void DispatchEffect(EntityManager em, Faction faction,
            SectActivePowerKind kind, float3 pos, float radius, float magnitude, float duration)
        {
            switch (kind)
            {
                case SectActivePowerKind.SmiteCircle:
                    ApplyCircleDamage(em, faction, pos, radius, (int)magnitude);
                    break;
                case SectActivePowerKind.HealCircle:
                    ApplyCircleHeal(em, faction, pos, radius, (int)magnitude);
                    break;
                case SectActivePowerKind.ArmorCircle:
                    ApplyCircleBuff(em, faction, pos, radius,
                        new SpellBuff { ArmorBonus = magnitude, TimeRemaining = duration });
                    break;
                case SectActivePowerKind.DamageCircle:
                    ApplyCircleBuff(em, faction, pos, radius,
                        new SpellBuff { DamageMultiplier = magnitude, TimeRemaining = duration });
                    break;
                case SectActivePowerKind.SpeedCircle:
                    ApplyCircleBuff(em, faction, pos, radius,
                        new SpellBuff { SpeedMultiplier = magnitude, TimeRemaining = duration });
                    break;
                case SectActivePowerKind.BurningCircle:
                case SectActivePowerKind.SpawnPyre:
                    SpawnBurning(em, faction, pos, radius, magnitude, duration);
                    break;
                case SectActivePowerKind.RevealCircle:
                    SpawnReveal(em, faction, pos, radius, duration);
                    break;
            }
        }

        private static void ApplyCircleDamage(EntityManager em, Faction faction,
            float3 center, float radius, int dmg)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value == faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                var hp = em.GetComponentData<Health>(e);
                hp.Value = math.max(0, hp.Value - dmg);
                em.SetComponentData(e, hp);
            }
        }

        private static void ApplyCircleHeal(EntityManager em, Faction faction,
            float3 center, float radius, int amount)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                hp.Value = math.min(hp.Max, hp.Value + amount);
                em.SetComponentData(e, hp);
            }
        }

        private static void ApplyCircleBuff(EntityManager em, Faction faction,
            float3 center, float radius, SpellBuff buff)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                TheWaningBorder.Systems.Combat.CombatDamageHelper.MergeSpellBuff(em, ecb, e, buff);
            }
            ecb.Playback(em);
            ecb.Dispose();
        }

        private static void SpawnBurning(EntityManager em, Faction faction,
            float3 center, float radius, float dps, float duration)
        {
            var pyre = em.CreateEntity(
                typeof(BurningGround), typeof(LocalTransform), typeof(FactionTag));
            em.SetComponentData(pyre, new BurningGround
            {
                DPS = dps,
                TimeRemaining = duration,
                Radius = radius,
            });
            em.SetComponentData(pyre, LocalTransform.FromPositionRotationScale(
                center, quaternion.identity, 1f));
            em.SetComponentData(pyre, new FactionTag { Value = faction });
        }

        private static void SpawnReveal(EntityManager em, Faction faction,
            float3 center, float radius, float duration)
        {
            // Stub: a fog-of-war reveal needs FogOfWarSystem support that's
            // out of scope here. Future Phase 5 polish will add a SectReveal
            // entity that FogOfWarSystem honors. For now, scout-LOS bumps
            // implemented elsewhere cover the core intent.
        }
    }
}
