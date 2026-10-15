// FeraldisCombatSystems.cs
// The per-tick Feraldis combat rules (docs/Design/Age_1_Feraldis.md §7):
//   Berserk                 a Berserker at a quarter HP hits much harder and moves faster (tech)
//   Altered Self-Perception the window opened at 1 HP: invulnerable, then dies unless healed (tech)
//   Warpath Sprint          infantry out of combat move faster (tech) — the clock lives here
//   War Chariot trample     bladed wheels hurt enemy infantry it drives through (Spiked Wheels: more)
//   Overcharge              the War Rig's cannon active — a damage burst that costs it HP (tech)

using TheWaningBorder.Core;
using TheWaningBorder.Systems.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    public static class FeraldisCombatSystems
    {
        /// <summary>The War Rig's Overcharge (an order, via FeraldisOrders).</summary>
        public static void Overcharge(EntityManager em, Entity rig)
        {
            var c = FeraldisCombat.Cfg;
            var st = em.HasComponent<OverchargeState>(rig) ? em.GetComponentData<OverchargeState>(rig) : default;
            if (st.Cooldown > 0f || st.Remaining > 0f) return;
            st.Remaining = c.overchargeSeconds;
            st.Cooldown = c.overchargeCooldown;
            if (em.HasComponent<OverchargeState>(rig)) em.SetComponentData(rig, st);
            else em.AddComponentData(rig, st);
            if (em.HasComponent<Health>(rig))
            {
                var h = em.GetComponentData<Health>(rig);
                h.Value = math.max(1, h.Value - Mathf.RoundToInt(h.Max * c.overchargeHpCost));
                em.SetComponentData(rig, h);
            }
        }

        /// <summary>
        /// Death interception for the Berserker (called from
        /// FeraldisDeathInterceptor). TRUE = held at 1 HP this frame.
        /// </summary>
        public static bool InterceptBerserker(EntityManager em, Entity e)
        {
            if (em.HasComponent<AlteredSelfState>(e)) { ClampToOne(em, e); return true; }
            if (em.HasComponent<AlteredSelfSpent>(e)) return false;
            if (!FeraldisCombat.Researched(em, e, "Feraldis_AlteredSelfPerception")) return false;

            ClampToOne(em, e);
            float secs = FeraldisCombat.Cfg.aspInvulnerableSeconds;
            em.AddComponentData(e, new AlteredSelfState { Remaining = secs });
            em.AddComponent<AlteredSelfSpent>(e);
            var inv = new Invulnerable { TimeRemaining = secs };
            if (em.HasComponent<Invulnerable>(e))
            {
                em.SetComponentData(e, inv);
                em.SetComponentEnabled<Invulnerable>(e, true);
            }
            else em.AddComponentData(e, inv);
            return true;
        }

        public static bool WantsInterceptBerserker(EntityManager em, Entity e)
            => em.HasComponent<AlteredSelfState>(e)
               || (!em.HasComponent<AlteredSelfSpent>(e) && FeraldisCombat.Researched(em, e, "Feraldis_AlteredSelfPerception"));

        private static void ClampToOne(EntityManager em, Entity e)
        {
            var h = em.GetComponentData<Health>(e);
            if (h.Value < 1) { h.Value = 1; em.SetComponentData(e, h); }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class BerserkerRulesSystem : SystemBase
    {
        private const float Tick = 0.25f;
        private SimCadence.Periodic _cadence;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Tick)) return;
            var em = EntityManager;
            var c = FeraldisCombat.Cfg;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Altered Self-Perception: when the window closes, a Berserker not
            // healed above the threshold dies (no second window — Spent).
            foreach (var (st, hp, e) in SystemAPI.Query<RefRW<AlteredSelfState>, RefRW<Health>>().WithEntityAccess())
            {
                st.ValueRW.Remaining -= Tick;
                if (st.ValueRO.Remaining > 0f) continue;
                ecb.RemoveComponent<AlteredSelfState>(e);
                float frac = hp.ValueRO.Max > 0 ? hp.ValueRO.Value / (float)hp.ValueRO.Max : 0f;
                if (frac <= c.aspSurviveHpFraction) hp.ValueRW.Value = 0;
            }

            // Berserk: on at or below the threshold (with the tech), off above it.
            foreach (var (hp, e) in SystemAPI.Query<RefRO<Health>>().WithAll<BerserkerTag>().WithEntityAccess())
            {
                bool low = hp.ValueRO.Max > 0 && hp.ValueRO.Value > 0
                           && hp.ValueRO.Value <= hp.ValueRO.Max * c.berserkHpFraction
                           && FeraldisCombat.Researched(em, e, "Feraldis_Berserk");
                bool has = em.HasComponent<BerserkBuff>(e);
                if (low && !has) ecb.AddComponent<BerserkBuff>(e);
                else if (!low && has) ecb.RemoveComponent<BerserkBuff>(e);
            }
            ecb.Playback(em);
            ecb.Dispose();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisMotionSystem : SystemBase
    {
        private const float Tick = 0.5f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Infantry =
        {
            ComponentType.ReadOnly<FeraldisUnitTag>(),
            ComponentType.ReadOnly<Target>(),
            ComponentType.Exclude<SiegeTag>(),
            ComponentType.Exclude<CanBuild>(),
        };
        static CachedEntityQuery QC_Infantry;

        static readonly ComponentType[] QT_Victims =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Victims;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Tick)) return;
            var em = EntityManager;
            var c = FeraldisCombat.Cfg;

            // Warpath Sprint clock: seconds without a target.
            {
                var q = QC_Infantry.Get(em, QT_Infantry);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var tgts = q.ToComponentDataArray<Target>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    bool fighting = tgts[i].Value != Entity.Null;
                    var clock = em.HasComponent<SprintClock>(ents[i]) ? em.GetComponentData<SprintClock>(ents[i]) : default;
                    clock.OutOfCombat = fighting ? 0f : clock.OutOfCombat + Tick;
                    if (em.HasComponent<SprintClock>(ents[i])) em.SetComponentData(ents[i], clock);
                    else em.AddComponentData(ents[i], clock);
                }
            }

            // Overcharge timers.
            foreach (var st in SystemAPI.Query<RefRW<OverchargeState>>())
            {
                st.ValueRW.Remaining = math.max(0f, st.ValueRO.Remaining - Tick);
                st.ValueRW.Cooldown = math.max(0f, st.ValueRO.Cooldown - Tick);
            }

            // War Chariot trample (the chariot carries BloodTrail).
            var chariots = new NativeList<Entity>(Allocator.Temp);
            foreach (var (bt, e) in SystemAPI.Query<RefRO<BloodTrail>>().WithAll<FeraldisUnitTag>().WithEntityAccess())
                chariots.Add(e);
            if (chariots.Length > 0)
            {
                var vq = QC_Victims.Get(em, QT_Victims);
                using var vEnts = vq.ToEntityArray(Allocator.Temp);
                using var vXfs = vq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var vFacs = vq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                uint infantry = UnitTagParse.Tag("infantry");
                for (int k = 0; k < chariots.Length; k++)
                {
                    var ch = chariots[k];
                    float3 pos = em.GetComponentData<LocalTransform>(ch).Position;
                    var trail = em.HasComponent<TrampleState>(ch) ? em.GetComponentData<TrampleState>(ch) : new TrampleState { Last = pos };
                    float moved = math.distance(trail.Last.xz, pos.xz);
                    trail.Last = pos;
                    if (em.HasComponent<TrampleState>(ch)) em.SetComponentData(ch, trail); else em.AddComponentData(ch, trail);
                    if (moved < c.trampleMinSpeed * Tick) continue;

                    var fac = em.GetComponentData<FactionTag>(ch).Value;
                    int dmg = Mathf.RoundToInt(c.trampleDamage
                        * (FeraldisCombat.Researched(fac, "Feraldis_SpikedWheels") ? c.spikedWheelsMultiplier : 1f));
                    float r2 = c.trampleRadius * c.trampleRadius;
                    for (int i = 0; i < vEnts.Length; i++)
                    {
                        if (!Alliances.AreHostile(fac, vFacs[i].Value)) continue;
                        if (math.distancesq(vXfs[i].Position.xz, pos.xz) > r2) continue;
                        if (!em.HasComponent<UnitTagsData>(vEnts[i])
                            || (em.GetComponentData<UnitTagsData>(vEnts[i]).Mask & infantry) == 0) continue;
                        SpellDamage.Apply(em, vEnts[i], dmg, DamageType.Melee, fac);
                    }
                }
            }
            chariots.Dispose();
        }
    }

    /// <summary>A War Chariot's position at the last trample check.</summary>
    public struct TrampleState : IComponentData { public float3 Last; }
}
