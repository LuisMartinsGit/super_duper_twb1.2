// WarTotemRites.cs
// Totem tiers and rites (docs/Design/Age_1_Feraldis.md §6.2, 2026-10-12).
//
// A totem drinks blood into two counters: LIFETIME (never goes down, evolves
// the totem I → II → III) and STORED (TotemFervor.Value, spent on rites).
// Spending never slows evolution.
//
//   tier  passive (radius)                          rites
//   I     frenzy lingers longer                     Bloodlust (attack speed) · Hunger (healing)
//   II    longer still                              + Red Mist (allies cannot drop below 1 HP)
//   III   longer still; kills heal the killer       + Blood Moon (the radius counts as blood)

using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Systems.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public enum TotemRite : byte { Bloodlust = 0, Hunger = 1, RedMist = 2, BloodMoon = 3 }

/// <summary>A totem's lifetime blood, its tier, and the rite it is running.</summary>
public struct TotemLifetime : IComponentData
{
    public float Lifetime;
    public byte Tier;
    public TotemRite ActiveRite;
    public float RiteRemaining;
    public float RiteCooldown;
}

namespace TheWaningBorder.Feraldis
{
    public static class WarTotemRites
    {
        private static WarTotemRitesConfig _cfg;
        public static WarTotemRitesConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<WarTotemRitesConfig>());

        public static byte TierFor(float lifetime)
        {
            var c = Cfg;
            return lifetime >= c.tier3Lifetime ? (byte)3 : lifetime >= c.tier2Lifetime ? (byte)2 : (byte)1;
        }

        public static int MinTier(TotemRite r) => r switch
        {
            TotemRite.RedMist => 2,
            TotemRite.BloodMoon => 3,
            _ => 1,
        };

        public static float Cost(TotemRite r) => At(Cfg.riteCost, (int)r);
        public static float Seconds(TotemRite r) => At(Cfg.riteSeconds, (int)r);

        public static bool CanBegin(EntityManager em, Entity totem, TotemRite rite)
        {
            if (!em.HasComponent<TotemFervor>(totem) || !em.HasComponent<TotemLifetime>(totem)) return false;
            if (em.HasComponent<UnderConstruction>(totem)) return false;
            var life = em.GetComponentData<TotemLifetime>(totem);
            if (life.RiteRemaining > 0f || life.RiteCooldown > 0f) return false;
            if (life.Tier < MinTier(rite)) return false;
            return em.GetComponentData<TotemFervor>(totem).Value >= Cost(rite);
        }

        /// <summary>Start a rite (an order, via FeraldisOrders — runs on every peer).</summary>
        public static void Begin(EntityManager em, Entity totem, TotemRite rite)
        {
            if (!CanBegin(em, totem, rite)) return;
            var f = em.GetComponentData<TotemFervor>(totem);
            f.Value -= Cost(rite);
            em.SetComponentData(totem, f);
            var life = em.GetComponentData<TotemLifetime>(totem);
            life.ActiveRite = rite;
            life.RiteRemaining = Seconds(rite);
            life.RiteCooldown = Cfg.riteCooldown;
            em.SetComponentData(totem, life);
        }

        public static string RiteName(TotemRite r) => r switch
        {
            TotemRite.Bloodlust => "Bloodlust",
            TotemRite.Hunger => "Hunger",
            TotemRite.RedMist => "Red Mist",
            _ => "Blood Moon",
        };

        public static string TierName(byte tier) => tier switch
        {
            3 => "Skull Furnace",
            2 => "Gore Engine",
            _ => "Blood Pylon",
        };

        internal static float At(float[] a, int i) => a != null && a.Length > 0 ? a[math.clamp(i, 0, a.Length - 1)] : 0f;
    }

    /// <summary>
    /// Runs the tier passives and the active rites on the units in each
    /// totem's radius, and keeps every totem's lifetime / tier current
    /// (WarTotemFervorSystem feeds TotemFervor; the lifetime counter is fed
    /// from the same drink — see WarTotemFervorSystem).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class WarTotemRitesSystem : SystemBase
    {
        private const float Tick = 0.5f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<FeraldisUnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Tick)) return;
            var em = EntityManager;
            var c = WarTotemRites.Cfg;

            // Totems without the lifetime counter yet (built before this system ran).
            var fresh = new NativeList<Entity>(Allocator.Temp);
            foreach (var (t, e) in SystemAPI.Query<RefRO<WarTotemTag>>().WithNone<TotemLifetime>().WithEntityAccess())
                fresh.Add(e);
            for (int i = 0; i < fresh.Length; i++)
                em.AddComponentData(fresh[i], new TotemLifetime { Tier = 1 });
            fresh.Dispose();

            var totems = new NativeList<Entity>(Allocator.Temp);
            foreach (var (t, e) in SystemAPI.Query<RefRO<TotemLifetime>>().WithAll<WarTotemTag>()
                         .WithNone<UnderConstruction>().WithEntityAccess())
                totems.Add(e);
            if (totems.Length == 0) { totems.Dispose(); return; }

            var uq = QC_Units.Get(em, QT_Units);
            using var units = uq.ToEntityArray(Allocator.Temp);
            using var uxf = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ufac = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var uhp = uq.ToComponentDataArray<Health>(Allocator.Temp);
            float r2 = c.radius * c.radius;

            for (int t = 0; t < totems.Length; t++)
            {
                var totem = totems[t];
                var life = em.GetComponentData<TotemLifetime>(totem);
                life.Tier = WarTotemRites.TierFor(life.Lifetime);
                life.RiteCooldown = math.max(0f, life.RiteCooldown - Tick);
                bool riteOn = life.RiteRemaining > 0f;
                if (riteOn) life.RiteRemaining = math.max(0f, life.RiteRemaining - Tick);
                em.SetComponentData(totem, life);

                var fac = em.GetComponentData<FactionTag>(totem).Value;
                float3 tp = em.GetComponentData<LocalTransform>(totem).Position;
                float linger = WarTotemRites.At(c.lingerByTier, life.Tier - 1);
                int tierIdx = life.Tier - 1;

                for (int i = 0; i < units.Length; i++)
                {
                    if (ufac[i].Value != fac || uhp[i].Value <= 0) continue;
                    if (math.distancesq(uxf[i].Position.xz, tp.xz) > r2) continue;
                    var u = units[i];

                    // Passive: frenzy lingers. Blood Moon: everyone in range frenzies.
                    bool moon = riteOn && life.ActiveRite == TotemRite.BloodMoon;
                    if (em.HasComponent<BloodFrenzy>(u))
                    {
                        var bf = em.GetComponentData<BloodFrenzy>(u);
                        bf.Remaining = math.max(bf.Remaining, moon ? 1.5f : linger);
                        em.SetComponentData(u, bf);
                    }
                    else if (moon) em.AddComponentData(u, new BloodFrenzy { Remaining = 1.5f });

                    if (!riteOn) continue;
                    switch (life.ActiveRite)
                    {
                        case TotemRite.Bloodlust:
                            var haste = new SectHaste { Multiplier = WarTotemRites.At(c.bloodlustHaste, tierIdx), TimeRemaining = Tick * 2f };
                            if (em.HasComponent<SectHaste>(u)) { em.SetComponentData(u, haste); em.SetComponentEnabled<SectHaste>(u, true); }
                            else em.AddComponentData(u, haste);
                            break;
                        case TotemRite.Hunger:
                            var h = em.GetComponentData<Health>(u);
                            h.Value = math.min(h.Max, h.Value + Mathf.CeilToInt(WarTotemRites.At(c.hungerHealPerSecond, tierIdx) * Tick));
                            em.SetComponentData(u, h);
                            break;
                        case TotemRite.RedMist:
                            var cling = new LifeCling { Floor = 1, TimeRemaining = Tick * 2f };
                            if (em.HasComponent<LifeCling>(u)) { em.SetComponentData(u, cling); em.SetComponentEnabled<LifeCling>(u, true); }
                            else em.AddComponentData(u, cling);
                            break;
                    }
                }
            }

            totems.Dispose();
        }
    }

    /// <summary>
    /// Tier III passive: an enemy that dies inside a Skull Furnace's radius
    /// heals its killer. Every frame (not on the rites cadence): a corpse is
    /// seen exactly once, on the frame it dies, before DeathSystem marks it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class TotemKillHealSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            var em = EntityManager;
            var c = WarTotemRites.Cfg;
            float r2 = c.radius * c.radius;
            var totems = new NativeList<Entity>(Allocator.Temp);
            foreach (var (life, e) in SystemAPI.Query<RefRO<TotemLifetime>>().WithAll<WarTotemTag>()
                         .WithNone<UnderConstruction>().WithEntityAccess())
                if (life.ValueRO.Tier >= 3) totems.Add(e);
            if (totems.Length == 0) { totems.Dispose(); return; }

            // Tier III passive: an enemy that dies inside the radius heals its killer.
            foreach (var (hp, last, xf, e) in SystemAPI.Query<RefRO<Health>, RefRO<LastAttackerEntity>, RefRO<LocalTransform>>()
                         .WithNone<DeathAnimationState, BuildingCollapseState>().WithEntityAccess())
            {
                if (hp.ValueRO.Value > 0) continue;
                var killer = last.ValueRO.Value;
                if (killer == Entity.Null || !em.Exists(killer) || !em.HasComponent<Health>(killer)) continue;
                if (!em.HasComponent<FactionTag>(killer)) continue;
                var kf = em.GetComponentData<FactionTag>(killer).Value;
                for (int t = 0; t < totems.Length; t++)
                {
                    if (em.GetComponentData<FactionTag>(totems[t]).Value != kf) continue;
                    float3 tp = em.GetComponentData<LocalTransform>(totems[t]).Position;
                    if (math.distancesq(tp.xz, xf.ValueRO.Position.xz) > r2) continue;
                    var kh = em.GetComponentData<Health>(killer);
                    kh.Value = math.min(kh.Max, kh.Value + c.killHeal);
                    em.SetComponentData(killer, kh);
                    break;
                }
            }
            totems.Dispose();
        }
    }
}
