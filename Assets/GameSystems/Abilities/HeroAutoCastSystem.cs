// HeroAutoCastSystem.cs
// EVERY HERO ABILITY BELOW LEVEL 10 CASTS ITSELF (docs/Design/Heroes.md §3,
// 2026-11-07), for the player's heroes and the AI's alike, within the hero's own
// line of sight. A deterministic consequence of the tick on every lockstep peer:
// no command travels, it sets AbilityActivated directly as the Ledger's old
// auto-cast did, and AbilityLifecycleSystem then checks and charges it as usual.
//
// Sim state only, in query order, ties to the lower entity index.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;

namespace TheWaningBorder.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AbilityLifecycleSystem))]
    public partial class HeroAutoCastSystem : SystemBase
    {
        private const float Interval = 0.5f;
        /// <summary>Self-defence: at or under this HP share, with an enemy this close.</summary>
        private const float HurtFraction = 0.6f;
        private const float CloseMetres = 8f;
        /// <summary>Recall: at or under this HP share.</summary>
        private const float BadlyHurtFraction = 0.35f;
        /// <summary>Liquidate: an own building at or under this HP share is about to fall.</summary>
        private const float FallingFraction = 0.15f;
        /// <summary>Honour thy Pledge: at least this many enemies in sight.</summary>
        private const int PledgeEnemies = 3;
        /// <summary>Challenge: at least this many enemies inside its radius.</summary>
        private const int ChallengeEnemies = 2;
        /// <summary>Leap: only at an enemy further than this; closer, it simply fights.</summary>
        private const float LeapMinMetres = 6f;

        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Heroes =
        {
            ComponentType.ReadOnly<HeroLevel>(), ComponentType.ReadOnly<UnitAbilities>(),
            ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LineOfSight>(),
        };
        static CachedEntityQuery QC_Heroes;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Buildings;

        private readonly List<(Entity E, float3 P, int Hp)> _enemies = new List<(Entity, float3, int)>(32);

        protected override void OnUpdate()
        {
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Interval)) return;
            var em = EntityManager;
            var heroQ = QC_Heroes.Get(em, QT_Heroes);
            if (heroQ.IsEmpty) return;

            using var heroes = heroQ.ToEntityArray(Allocator.Temp);
            var unitQ = QC_Units.Get(em, QT_Units);
            using var units = unitQ.ToEntityArray(Allocator.Temp);
            using var unitXf = unitQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var unitFac = unitQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitHp = unitQ.ToComponentDataArray<Health>(Allocator.Temp);

            int[] slots = new int[UnitAbilities.Count];
            for (int h = 0; h < heroes.Length; h++)
            {
                var hero = heroes[h];
                var hp = em.GetComponentData<Health>(hero);
                if (hp.Value <= 0 || TransientState.Active<DeathAnimationState>(em, hero)) continue;
                if (em.HasComponent<TheWaningBorder.Entities.Launched>(hero)) continue;
                if (TransientState.Active<AbilityCastState>(em, hero) || TransientState.Active<AbilityActivated>(em, hero)) continue;
                if (TransientState.Active<SectDisordered>(em, hero)) continue;

                int n = AbilityQuery.ActiveSlots(em, hero, slots);
                if (n == 0) continue;

                var pos = em.GetComponentData<LocalTransform>(hero).Position;
                var fac = em.GetComponentData<FactionTag>(hero).Value;
                float los = em.GetComponentData<LineOfSight>(hero).Radius;
                float los2 = los * los;
                _enemies.Clear();
                for (int i = 0; i < units.Length; i++)
                {
                    if (unitHp[i].Value <= 0 || !Alliances.AreHostile(fac, unitFac[i].Value)) continue;
                    if (math.distancesq(unitXf[i].Position.xz, pos.xz) > los2) continue;
                    _enemies.Add((units[i], unitXf[i].Position, unitHp[i].Value));
                }

                var abilities = em.GetComponentData<UnitAbilities>(hero);
                float frac = hp.Max > 0 ? hp.Value / (float)hp.Max : 1f;
                for (int k = 0; k < n; k++)
                {
                    int slot = slots[k];
                    if (AbilityQuery.CooldownRemaining(em, hero, slot) > 0f) continue;
                    var card = AbilityCatalog.Get(abilities.Get(slot));
                    if (card == null || card.IsUltimate) continue;
                    if (!Decide(em, hero, card, fac, pos, los, frac, out Entity target, out float3? aim)) continue;
                    CommandRouter.StampAbilityAim(em, hero, aim);
                    TransientState.Set(em, hero, new AbilityActivated { Target = target, Slot = slot });
                    break;   // one cast per hero per pass
                }
            }
        }

        private bool Decide(EntityManager em, Entity hero, AbilityCard card, Faction fac, float3 pos, float los,
            float frac, out Entity target, out float3? aim)
        {
            target = Entity.Null; aim = null;

            if (card.HasEffect(AbilityEffectKind.RunaiTheCall))
                return _enemies.Count > 0 && TheWaningBorder.Runai.RunaiTradeSystem.CallableEscorts(em, fac) > 0;
            if (card.HasEffect(AbilityEffectKind.SectStampOfRecord)) return PickEnemy(em, pos, requireAbilities: true, out target);
            if (card.HasEffect(AbilityEffectKind.SectLancet)) return PickStrongest(em, out target);
            if (card.HasEffect(AbilityEffectKind.FeraldisRaze)) return PickBuilding(em, fac, pos, los, hostile: true, out target);
            if (card.HasEffect(AbilityEffectKind.ResourceYieldPct)) return PickEconomy(em, fac, pos, los, out target);
            if (card.HasEffect(AbilityEffectKind.SectLiquidate)) return PickFalling(em, fac, pos, los, out target);
            if (card.HasEffect(AbilityEffectKind.SectStake))
                return _enemies.Count == 0 && PickEconomy(em, fac, pos,
                    TheWaningBorder.Systems.Sect.SectHeroAbilities.StakeRange, out _, anyState: true);
            if (card.HasEffect(AbilityEffectKind.FeraldisLeap))
            {
                float best = float.MaxValue;
                for (int i = 0; i < _enemies.Count; i++)
                {
                    float d = math.distance(_enemies[i].P.xz, pos.xz);
                    if (d > LeapMinMetres && d < best) { best = d; aim = _enemies[i].P; }
                }
                return aim.HasValue;
            }
            if (card.HasEffect(AbilityEffectKind.SectRecall)) return frac <= BadlyHurtFraction && _enemies.Count > 0;
            if (card.HasEffect(AbilityEffectKind.SectChallenge)) return EnemiesWithin(pos, card.Radius) >= ChallengeEnemies;
            if (card.HasEffect(AbilityEffectKind.SummonPledgeArmy)) return _enemies.Count >= PledgeEnemies;
            // Self-defence: Liquid Courage, Iron Frame.
            if (card.Targeting == AbilityTargeting.SelfCast && card.Affects == AbilityAffects.Self
                && card.HasEffect(AbilityEffectKind.DamageTakenPct))
                return frac <= HurtFraction && EnemiesWithin(pos, CloseMetres) > 0;
            // Everything else helps the fight in front of the hero.
            return _enemies.Count > 0;
        }

        private int EnemiesWithin(float3 pos, float radius)
        {
            float r2 = radius * radius; int n = 0;
            for (int i = 0; i < _enemies.Count; i++)
                if (math.distancesq(_enemies[i].P.xz, pos.xz) <= r2) n++;
            return n;
        }

        private bool PickEnemy(EntityManager em, float3 pos, bool requireAbilities, out Entity target)
        {
            target = Entity.Null;
            float best = float.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (requireAbilities && !em.HasComponent<UnitAbilities>(e.E)) continue;
                float d = math.distancesq(e.P.xz, pos.xz);
                if (d < best || (d == best && e.E.Index < target.Index)) { best = d; target = e.E; }
            }
            return target != Entity.Null;
        }

        /// <summary>A hero first, then whoever has the most HP left.</summary>
        private bool PickStrongest(EntityManager em, out Entity target)
        {
            target = Entity.Null;
            long best = -1;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                long score = e.Hp + (em.HasComponent<HeroLevel>(e.E) ? 1000000L : 0L);
                if (score > best || (score == best && e.E.Index < target.Index)) { best = score; target = e.E; }
            }
            return target != Entity.Null;
        }

        private bool PickBuilding(EntityManager em, Faction fac, float3 pos, float range, bool hostile, out Entity target)
        {
            target = Entity.Null;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            float best = range * range;
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                if (hostile ? !Alliances.AreHostile(fac, facs[i].Value) : facs[i].Value != fac) continue;
                float d = math.distancesq(xfs[i].Position.xz, pos.xz);
                if (d <= best) { best = d; target = ents[i]; }
            }
            return target != Entity.Null;
        }

        /// <summary>The nearest own economy building in range that can take the
        /// effect (Automate Facility's lockout), or any at all for Stake.</summary>
        private bool PickEconomy(EntityManager em, Faction fac, float3 pos, float range, out Entity target, bool anyState = false)
        {
            target = Entity.Null;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            float best = range * range;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != fac) continue;
                float d = math.distancesq(xfs[i].Position.xz, pos.xz);
                if (d > best || !AbilityAuraSystem.IsEconomyBuilding(em, ents[i])) continue;
                if (!anyState && (TransientState.Active<UnderAutomation>(em, ents[i])
                                  || TransientState.Active<AutoYieldBoost>(em, ents[i]))) continue;
                best = d; target = ents[i];
            }
            return target != Entity.Null;
        }

        private bool PickFalling(EntityManager em, Faction fac, float3 pos, float range, out Entity target)
        {
            target = Entity.Null;
            var q = QC_Buildings.Get(em, QT_Buildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            float best = range * range;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != fac || hps[i].Value <= 0 || hps[i].Max <= 0) continue;
                if (hps[i].Value / (float)hps[i].Max > FallingFraction) continue;
                if (em.HasComponent<HallTag>(ents[i]) || em.HasComponent<FortressTag>(ents[i])) continue;
                if (em.HasComponent<UnderConstruction>(ents[i]) || em.HasComponent<ShardrootTag>(ents[i])) continue;
                float d = math.distancesq(xfs[i].Position.xz, pos.xz);
                if (d <= best) { best = d; target = ents[i]; }
            }
            return target != Entity.Null;
        }
    }
}
