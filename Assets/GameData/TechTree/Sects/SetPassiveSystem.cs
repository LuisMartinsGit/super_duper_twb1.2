// SetPassiveSystem.cs
// Applies the two set passives that act on the field every tick
// (docs/Design/Sects.md §6b):
//
//   Sovereign Hour     a faction holding all four Alanthor sects: inside its
//                      own territory its units attack 15% faster (SectHaste)
//                      and hostile units move 10% slower (SpellDebuff).
//   The Breaking Hour  a faction holding all four Feraldis sects: when an
//                      enemy building or a curse node falls to it, its units
//                      within L (25 m) attack and move 20% faster for 15 s.
//
// Both refresh short-lived transients (TransientState) rather than holding a
// permanent stat change, so leaving the territory or the passive ending simply
// lets them lapse. Runs on sim time from replicated state only.

using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using TheWaningBorder.World.Regions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class SetPassiveSystem : SystemBase
    {
        public const float SovereignHaste = 1.15f;
        public const float SovereignSlow = 0.10f;
        public const float BreakingBoost = 1.20f;
        public const float BreakingSeconds = 15f;
        public const float BreakingRadius = 25f;   // SectRadius.Large
        private const float PulseSeconds = 0.5f;
        private const float PulseHold = 0.75f;    // a little over one pulse, so the effect never flickers

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_DeadBuildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LastDamagedByFaction>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<DeathAnimationState>(),
            ComponentType.Exclude<BuildingCollapseState>(),
        };
        static CachedEntityQuery QC_DeadBuildings;

        private float _pulse;

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // Which factions hold which set — at most 8, read once per tick.
            uint sovereign = 0, breaking = 0;
            for (int f = 0; f < 8; f++)
            {
                var set = SetPassives.ActiveSet(em, (Faction)f);
                if (set == SectSet.Alanthor) sovereign |= 1u << f;
                else if (set == SectSet.Feraldis) breaking |= 1u << f;
            }

            if (breaking != 0) BreakingHour(em, breaking);

            _pulse += SystemAPI.Time.DeltaTime;
            if (_pulse < PulseSeconds) return;
            _pulse = 0f;
            if (sovereign != 0) SovereignHour(em, sovereign);
        }

        private static void SovereignHour(EntityManager em, uint sovereign)
        {
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tr = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var fa = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hp = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hp[i].Value <= 0) continue;
                int owner = TerritoryOwnership.OwnerAt(tr[i].Position.x, tr[i].Position.z);
                if (owner < 0 || owner > 31 || (sovereign & (1u << owner)) == 0) continue;
                var unitFaction = fa[i].Value;
                if ((int)unitFaction == owner)
                    Haste(em, ents[i], SovereignHaste, PulseHold);
                else if (Alliances.AreHostile((Faction)owner, unitFaction))
                    Slow(em, ents[i], SovereignSlow, PulseHold);
            }
        }

        private static void BreakingHour(EntityManager em, uint breaking)
        {
            var q = QC_DeadBuildings.Get(em, QT_DeadBuildings);
            if (q.IsEmpty) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hp = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var fa = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var tr = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hp[i].Value > 0) continue;
                if (!TransientState.Active<LastDamagedByFaction>(em, ents[i])) continue;
                var killer = em.GetComponentData<LastDamagedByFaction>(ents[i]).Value;
                int k = (int)killer;
                if (k < 0 || k > 31 || (breaking & (1u << k)) == 0) continue;
                var victim = fa[i].Value;
                // An enemy building, or a curse node (the curse is Border).
                if (victim == killer) continue;
                if (victim != Faction.Border && !Alliances.AreHostile(killer, victim)) continue;
                BoostAround(em, killer, tr[i].Position);
            }
        }

        private static void BoostAround(EntityManager em, Faction faction, float3 at)
        {
            float r2 = BreakingRadius * BreakingRadius;
            var q = QC_Units.Get(em, QT_Units);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tr = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var fa = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (fa[i].Value != faction) continue;
                if (math.distancesq(tr[i].Position.xz, at.xz) > r2) continue;
                Haste(em, ents[i], BreakingBoost, BreakingSeconds);
                var buff = TransientState.Active<SpellBuff>(em, ents[i])
                    ? em.GetComponentData<SpellBuff>(ents[i]) : default;
                buff.SpeedMultiplier = math.max(buff.SpeedMultiplier, BreakingBoost);
                buff.TimeRemaining = math.max(buff.TimeRemaining, BreakingSeconds);
                if (buff.DamageMultiplier <= 0f) buff.DamageMultiplier = 1f;
                TransientState.Set(em, ents[i], buff);
            }
        }

        private static void Haste(EntityManager em, Entity e, float multiplier, float seconds)
        {
            var h = TransientState.Active<SectHaste>(em, e) ? em.GetComponentData<SectHaste>(e) : default;
            h.Multiplier = math.max(h.Multiplier, multiplier);
            h.TimeRemaining = math.max(h.TimeRemaining, seconds);
            TransientState.Set(em, e, h);
        }

        private static void Slow(EntityManager em, Entity e, float reduction, float seconds)
        {
            var d = TransientState.Active<SpellDebuff>(em, e) ? em.GetComponentData<SpellDebuff>(e) : default;
            d.SpeedReduction = math.max(d.SpeedReduction, reduction);
            d.TimeRemaining = math.max(d.TimeRemaining, seconds);
            TransientState.Set(em, e, d);
        }
    }
}
