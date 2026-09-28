// UnitStanceSystem.cs
// Unit stances (docs/Design/Stances.md): stamps every new unit with its
// faction's default stance and the engagement record TargetingSystem leashes
// by, and publishes the stance tunables (UnitStanceSystem.asset) as the
// StanceSettings singleton the Bursted TargetingSystem reads.
//
// Managed on purpose: the default depends on whether the faction is
// human-controlled (GameSettings.IsFactionHumanControlled), which is filled
// from the lobby identically on every peer before the match starts, so the
// stamp is the same everywhere.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetingSystem))]
    public partial class UnitStanceSystem : SystemBase
    {
        private EntityQuery _needQuery;
        private UnitStanceSystemConfig _cfg;

        protected override void OnCreate()
        {
            _needQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag>()
                .WithNone<UnitEngagement>()
                .Build();
        }

        protected override void OnUpdate()
        {
            if (_cfg == null) _cfg = ComponentConfig.Require<UnitStanceSystemConfig>();
            if (_cfg == null) return;   // logged loudly by Require; TargetingSystem waits for the singleton

            var em = EntityManager;
            PublishSettings(em);

            if (_needQuery.IsEmpty) return;

            using var ents = _needQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!em.Exists(e)) continue;

                // Default stance — only if nothing set one already (a scenario
                // that adds HoldPositionTag directly is on Hold).
                if (!em.HasComponent<UnitStance>(e))
                {
                    UnitStanceMode mode = DefaultFor(em, e);
                    if (em.HasComponent<HoldPositionTag>(e)) mode = UnitStanceMode.Hold;
                    em.AddComponentData(e, new UnitStance { Value = mode });
                }

                // Stagger phase from the NETWORK id — the one identity every
                // lockstep peer agrees on. Unnetworked units only exist
                // outside lockstep, where the index is as good as anything.
                int key = em.HasComponent<NetworkedEntity>(e)
                    ? em.GetComponentData<NetworkedEntity>(e).NetworkId
                    : e.Index;
                int hp = em.HasComponent<Health>(e) ? em.GetComponentData<Health>(e).Value : 0;

                em.AddComponentData(e, new UnitEngagement
                {
                    AutoTarget = Entity.Null,
                    LastHealth = hp,
                    // Far in the past: a fresh unit is not "under fire".
                    HitAt = -1e6f,
                    NextAcquireAt = 0f,
                    ScanPhase = (byte)(key & (TargetingSystem.AcquireStagger - 1)),
                });
            }
        }

        /// <summary>Humans start Defensive, AI factions (and the curse)
        /// Aggressive (Stances.md §4).</summary>
        private static UnitStanceMode DefaultFor(EntityManager em, Entity e)
        {
            if (!em.HasComponent<FactionTag>(e)) return UnitStanceMode.Defensive;
            var f = em.GetComponentData<FactionTag>(e).Value;
            if (f == Faction.Border) return UnitStanceMode.Aggressive;
            return GameSettings.IsFactionHumanControlled(f)
                ? UnitStanceMode.Defensive
                : UnitStanceMode.Aggressive;
        }

        private void PublishSettings(EntityManager em)
        {
            var s = new StanceSettings
            {
                AggressiveLeash = _cfg.aggressiveLeash,
                RetaliationWindow = _cfg.retaliationWindow,
                LeashReacquireCooldown = _cfg.leashReacquireCooldown,
                UnderFireWindow = _cfg.underFireWindow,
            };
            if (SystemAPI.TryGetSingletonEntity<StanceSettings>(out var single))
                em.SetComponentData(single, s);
            else
                em.AddComponentData(em.CreateEntity(), s);
        }
    }
}
