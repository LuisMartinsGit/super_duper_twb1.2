// A totem must pay for itself. Canon: docs/Design/Age_1_Feraldis.md
// "Feraldis rebalance — raiders, plunder, totems (2026-08-07)".
//
// Planting on blood is no longer enough on its own. A War Totem now projects
// a HEALING + ATTACK aura over friendly units in TotemAuraRadius — and
// SUSTAINING that aura drains the pool it stands on, on top of the Fervor
// drink in WarTotemFervorSystem. A totem whose pool runs dry has
// TotemDryLifetime (60 s) to find more blood before it collapses.
//
// The point is to make a totem a DECISION rather than free furniture: plant it
// where a real battle happened, get a fortified position that heals your army,
// and watch it eat the very thing keeping it alive. Spamming totems on thin
// blood now costs more than it returns — which is the behaviour the 2026-08-07
// logs showed (the same coordinates re-totemed over and over).
//
// The attack buff is applied as a component stamped on units in range and
// stripped when they leave, mirroring how the Border aura nodes do it, so
// nothing has to diff auras per-frame.
//
// BloodMap is managed main-thread state, so this is a SystemBase.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.FeraldisConstants;

using TheWaningBorder.Core;
namespace TheWaningBorder.Systems.World
{

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WarTotemAuraSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<WarTotemTag>();

        protected override void OnUpdate()
        {
            if (!BloodMap.Ready) return;
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            // Snapshot live totems first: healing and the starvation timer
            // both need positions, and collapsing one is a structural change.
            var totemPos = new NativeList<float3>(Allocator.Temp);
            var totemFaction = new NativeList<Faction>(Allocator.Temp);
            var collapsing = new NativeList<Entity>(Allocator.Temp);
            var startedStarving = new NativeList<Entity>(Allocator.Temp);
            var stoppedStarving = new NativeList<Entity>(Allocator.Temp);

            foreach (var (xf, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<WarTotemTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                float3 p = xf.ValueRO.Position;

                // Sustaining the aura costs blood, drawn from the whole aura
                // disc.
                BloodMap.Consume(p.x, p.z, TotemAuraRadius,
                                 TotemAuraBloodPerSecond * dt);

                // Dryness is measured THE WAY PLACEMENT MEASURES IT — a point
                // sample under the totem — because that is what
                // TotemDryBloodThreshold was tuned against (see the constant's
                // own note: deliberately under the 0.15 placement threshold so
                // a legally-sited totem does not instantly starve).
                //
                // It used to test Consume's disc MEAN over the 18 m aura. A
                // real pool covers a handful of grid cells, so that mean sits
                // far below 0.03 even standing in fresh blood: every totem
                // read dry on its first frame and collapsed 60 s later, which
                // is the exact opposite of the documented intent.
                float atTotem = BloodMap.SampleWorld(p.x, p.z);
                bool dry = atTotem <= TotemDryBloodThreshold;
                bool wasStarving = em.HasComponent<TotemStarving>(entity);

                if (dry)
                {
                    if (!wasStarving)
                    {
                        startedStarving.Add(entity);
                    }
                    else
                    {
                        var st = em.GetComponentData<TotemStarving>(entity);
                        st.DrySeconds += dt;
                        if (st.DrySeconds >= TotemDryLifetime) collapsing.Add(entity);
                        else em.SetComponentData(entity, st);
                    }
                }
                else if (wasStarving)
                {
                    // Fresh blood — the clock resets rather than pausing.
                    stoppedStarving.Add(entity);
                }

                // A starving totem still projects its aura: it is dying, not
                // switched off. That keeps the last minute of a totem's life
                // useful and makes the collapse read as a loss, not a fizzle.
                totemPos.Add(p);
                totemFaction.Add(faction.ValueRO.Value);
            }

            // ── Heal + buff every friendly unit inside any totem's radius ──
            if (totemPos.Length > 0)
            {
                float r2 = TotemAuraRadius * TotemAuraRadius;
                var gainBuff = new NativeList<Entity>(Allocator.Temp);
                var loseBuff = new NativeList<Entity>(Allocator.Temp);

                foreach (var (health, xf, faction, entity) in SystemAPI
                    .Query<RefRW<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                    .WithAll<UnitTag>()
                    .WithNone<DeathAnimationState>()
                    .WithEntityAccess())
                {
                    float3 up = xf.ValueRO.Position;
                    Faction uf = faction.ValueRO.Value;

                    bool inAura = false;
                    for (int i = 0; i < totemPos.Length; i++)
                    {
                        // Own army and allies. docs/Design/Teams.md
                        if (!Alliances.AreAllied(totemFaction[i], uf)) continue;
                        float dx = up.x - totemPos[i].x, dz = up.z - totemPos[i].z;
                        if (dx * dx + dz * dz > r2) continue;
                        inAura = true;
                        break;
                    }

                    bool hasBuff = em.HasComponent<TotemAuraBuff>(entity);
                    if (inAura && !hasBuff) gainBuff.Add(entity);
                    else if (!inAura && hasBuff) loseBuff.Add(entity);

                    if (!inAura) continue;

                    ref var h = ref health.ValueRW;
                    if (h.Value > 0 && h.Value < h.Max)
                        h.Value = (int)math.min(h.Max, h.Value + TotemAuraHealPerSecond * dt);
                }

                for (int i = 0; i < gainBuff.Length; i++)
                    em.AddComponentData(gainBuff[i],
                        new TotemAuraBuff { AttackBonus = TotemAuraAttackBonus });
                for (int i = 0; i < loseBuff.Length; i++)
                    em.RemoveComponent<TotemAuraBuff>(loseBuff[i]);

                gainBuff.Dispose();
                loseBuff.Dispose();
            }

            // ── Structural changes last ──────────────────────────────────
            for (int i = 0; i < startedStarving.Length; i++)
                em.AddComponentData(startedStarving[i], new TotemStarving { DrySeconds = 0f });
            for (int i = 0; i < stoppedStarving.Length; i++)
                em.RemoveComponent<TotemStarving>(stoppedStarving[i]);

            for (int i = 0; i < collapsing.Length; i++)
            {
                var t = collapsing[i];
                if (!em.Exists(t)) continue;
                // Kill through Health so the normal death path runs (visuals,
                // rubble, build-space release) rather than vanishing the
                // entity from under every system holding a reference.
                if (em.HasComponent<Health>(t))
                {
                    var h = em.GetComponentData<Health>(t);
                    h.Value = 0;
                    em.SetComponentData(t, h);
                }
                SimSignals.Notify(
                    Loc.T("A War Totem crumbles — its blood is spent."));
            }

            totemPos.Dispose();
            totemFaction.Dispose();
            collapsing.Dispose();
            startedStarving.Dispose();
            stoppedStarving.Dispose();
        }
    }
}
