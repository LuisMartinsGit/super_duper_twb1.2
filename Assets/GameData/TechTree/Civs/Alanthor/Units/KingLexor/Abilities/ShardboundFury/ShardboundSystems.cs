// ShardboundSystems.cs
// The two sim systems behind the Shardbound King (ShardboundFury.cs):
//
//   ShardboundKingSystem  stamps ShardboundKing + grants Shardbound Fury the
//                         tick King Lexor carries the artifact, and takes both
//                         away the tick he stops (dropped on death, handed to
//                         the Temple). Nothing else may add or remove the tag.
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
    public partial struct ShardboundKingSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var grant = new NativeList<Entity>(Allocator.Temp);
            var revoke = new NativeList<Entity>(Allocator.Temp);

            foreach (var (unique, entity) in SystemAPI
                .Query<RefRO<UniqueUnitTag>>()
                .WithAll<ShardrootTag>()
                .WithNone<ShardboundKing>()
                .WithEntityAccess())
            {
                if (unique.ValueRO.Kind == UniqueUnitKind.KingLexor) grant.Add(entity);
            }
            foreach (var (_, entity) in SystemAPI
                .Query<RefRO<ShardboundKing>>()
                .WithNone<ShardrootTag>()
                .WithEntityAccess())
            {
                revoke.Add(entity);
            }

            int fury = AbilityCatalog.IndexOf(ShardboundFury.AbilityName);
            for (int i = 0; i < grant.Length; i++)
            {
                var e = grant[i];
                em.AddComponent<ShardboundKing>(e);
                AbilityAssignment.AddAbility(em, e, fury);
                var f = em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value : Faction.Blue;
                SimSignals.Notify(string.Format(Loc.T("{0}'s King Lexor is SHARDBOUND."), f));
                TWBLog.Log($"[Shardbound] {f}: King Lexor bears the Shardroot -- cleave + Fury granted");
            }
            for (int i = 0; i < revoke.Length; i++)
            {
                var e = revoke[i];
                em.RemoveComponent<ShardboundKing>(e);
                AbilityAssignment.RemoveAbility(em, e, fury);
                TWBLog.Log("[Shardbound] the king no longer bears the Shardroot -- empowerment withdrawn");
            }
            grant.Dispose();
            revoke.Dispose();
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
            var landDamage = new NativeList<int>(Allocator.Temp);

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
                    landDamage.Add(l.LandDamage);
                }
            }

            var em = state.EntityManager;
            for (int i = 0; i < landed.Length; i++)
            {
                var e = landed[i];
                em.RemoveComponent<Launched>(e);
                if (!em.HasComponent<Health>(e)) continue;
                var h = em.GetComponentData<Health>(e);
                h.Value = math.max(0, h.Value - landDamage[i]);
                em.SetComponentData(e, h);
            }
            landed.Dispose();
            landDamage.Dispose();
        }
    }
}
