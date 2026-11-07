// ShardboundSystems.cs
// The two sim systems behind the Shardbound King (ShardboundFury.cs):
//
//   AscendedHeroSystem  stamps AscendedHero the tick ANY hero carries the
//                         artifact and takes it away the tick it stops (dropped,
//                         dead, stowed in the Temple). Nothing else may add or
//                         remove the tag. It also fires Shardbound Fury by itself
//                         when enemies crowd the hero (docs/Design/Heroes.md §4).
//   LaunchSystem          flies every Launched unit on a parabola the SIM
//                         owns -- LocalTransform.y is written from sim time,
//                         so the arc is identical on every peer and the view
//                         (which follows the sim position) shows it -- then
//                         grounds it, applies LandDamage and removes Launched.
//                         Runs before the integrator, which excludes Launched
//                         units, so nothing else moves them while airborne.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct AscendedHeroSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var grant = new NativeList<Entity>(Allocator.Temp);
            var revoke = new NativeList<Entity>(Allocator.Temp);

            foreach (var (_, entity) in SystemAPI
                .Query<RefRO<UniqueUnitTag>>()
                .WithAll<ShardrootTag>()
                .WithNone<AscendedHero>()
                .WithEntityAccess())
            {
                grant.Add(entity);
            }
            foreach (var (_, entity) in SystemAPI
                .Query<RefRO<AscendedHero>>()
                .WithNone<ShardrootTag>()
                .WithEntityAccess())
            {
                revoke.Add(entity);
            }

            for (int i = 0; i < grant.Length; i++)
            {
                var e = grant[i];
                em.AddComponent<AscendedHero>(e);
                // He is bound with a full second of the swarm cap in hand.
                if (em.HasComponent<Health>(e))
                    em.SetComponentData(e, new AscendedHero
                    { DamageBudget = AscendedHeroRules.BudgetMax(em.GetComponentData<Health>(e)) });
                var f = em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value : Faction.Blue;
                SimSignals.Notify(string.Format(Loc.T("{0}'s hero is ASCENDED — nearly invincible!"), f));
                TWBLog.Log($"[Shardbound] {f}: a hero bears the Shardroot -- Ascended");
            }
            for (int i = 0; i < revoke.Length; i++)
            {
                var e = revoke[i];
                em.RemoveComponent<AscendedHero>(e);
                TWBLog.Log("[Shardbound] a hero no longer bears the Shardroot -- Ascended state withdrawn");
            }
            grant.Dispose();
            revoke.Dispose();
            TickFury(ref state, em);
        }
    }

    public partial struct AscendedHeroSystem
    {
        /// <summary>Every Ascended hero's Fury: its own clock, no ability slot,
        /// and it fires when enough enemies stand inside the card's radius.</summary>
        private void TickFury(ref SystemState state, EntityManager em)
        {
            var kings = new NativeList<Entity>(Allocator.Temp);
            float dt = SystemAPI.Time.DeltaTime;
            foreach (var (k, entity) in SystemAPI.Query<RefRW<AscendedHero>>().WithEntityAccess())
            {
                if (k.ValueRO.FuryCooldown > 0f) k.ValueRW.FuryCooldown -= dt;
                if (k.ValueRO.FuryCooldown <= 0f) kings.Add(entity);
            }
            if (kings.Length > 0)
            {
                var card = AbilityCatalog.Get(ShardboundFury.AbilityName);
                int need = math.max(1, AscendedHeroConfig.I.furyMinEnemies);
                for (int i = 0; card != null && i < kings.Length; i++)
                {
                    var e = kings[i];
                    if (em.GetComponentData<Health>(e).Value <= 0) continue;
                    using var near = TheWaningBorder.Systems.Sect.SectHeroAbilities.UnitsNear(em,
                        em.GetComponentData<LocalTransform>(e).Position, card.Radius,
                        em.GetComponentData<FactionTag>(e).Value, hostile: true);
                    if (near.Length < need) continue;
                    var k = em.GetComponentData<AscendedHero>(e);
                    k.FuryCooldown = card.Cooldown;
                    em.SetComponentData(e, k);
                    AbilityEffectExecutor.Apply(em, e, card, Entity.Null);
                }
            }
            kings.Dispose();
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TheWaningBorder.Systems.Navigation.UnitIntegratorSystem))]
    public partial struct LaunchSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var landed = new NativeList<Entity>(Allocator.Temp);
            var landHit = new NativeList<Launched>(Allocator.Temp);

            foreach (var (launched, xf, entity) in SystemAPI
                .Query<RefRW<Launched>, RefRW<LocalTransform>>()
                .WithEntityAccess())
            {
                ref var l = ref launched.ValueRW;
                l.Elapsed += dt;
                float u = math.saturate(l.Elapsed / math.max(0.01f, l.Duration));
                float3 ground = math.lerp(l.From, l.To, u);
                float surface = TerrainUtility.GetHeight(ground.x, ground.z);
                // 4u(1-u): 0 at take-off, Height at the apex, 0 at landing.
                float lift = l.Height * 4f * u * (1f - u);
                var t = xf.ValueRO;
                t.Position = new float3(ground.x, surface + lift, ground.z);
                xf.ValueRW = t;
                if (u >= 1f)
                {
                    landed.Add(entity);
                    landHit.Add(l);
                }
            }

            var em = state.EntityManager;
            for (int i = 0; i < landed.Length; i++)
            {
                var e = landed[i];
                em.RemoveComponent<Launched>(e);
                if (!em.HasComponent<Health>(e)) continue;
                var hit = landHit[i];
                if (hit.SpellRouted != 0)
                {
                    // Shardbound Fury's slam is a spell hit (docs/Design/Spells.md).
                    // The victim was hostile when thrown; friendlyFire keeps a
                    // unit converted mid-flight from landing unharmed.
                    TheWaningBorder.Systems.Combat.SpellDamage.Apply(
                        em, e, hit.LandDamage, hit.DamageKind, hit.Source, friendlyFire: true);
                    continue;
                }
                // Shield points are hit points (Combat_Pacing.md): spend them first.
                int left = TheWaningBorder.Systems.Combat.ShieldDamage.Absorb(em, e, hit.LandDamage);
                var h = em.GetComponentData<Health>(e);
                h.Value = math.max(0, h.Value - left);
                em.SetComponentData(e, h);
            }
            landed.Dispose();
            landHit.Dispose();
        }
    }
}
