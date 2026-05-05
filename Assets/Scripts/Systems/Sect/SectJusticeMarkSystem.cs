// SectJusticeMarkSystem.cs
// Implements Justice's Lv I "Marked for Sentence" passive: an enemy that
// kills one of your units is marked for 30s — visible through fog and
// taking +10% damage from units of your faction.
//
// Hook: scans freshly-dead entities (same WithNone-marker pattern as
// PillageSystem / SectVenerationFervorSystem). Reads LastAttackerEntity
// to find the killer, gates on the VICTIM's faction having Justice
// adopted (the avenger is the dead unit's faction), then applies the
// MarkedForSentence component to the killer.
//
// The damage bonus flows through CombatDamageHelper (a small read site
// added in this same task) when the marker-faction's units attack.
// Visibility-through-fog is enforced by FogOfWarSystem reading
// MarkedForSentence on visible enemies and revealing them to the
// marker faction.
//
// task-063 phase 2c.
//
// Location: Assets/Scripts/Systems/Sect/SectJusticeMarkSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectJusticeMarkSystem : ISystem
    {
        // Mark duration is shared across levels — Phase 4 only scales the
        // damage bonus per the Lv I/II/III spec (+10% / +20% / +30%).
        private const float MarkDuration = 30f;

        private static float DamageBonusFor(byte level) => level switch
        {
            2 => 0.20f,
            3 => 0.30f,
            _ => 0.10f,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Phase 1: tick existing marks. Collect expired into a list and
            // remove after the foreach (structural change).
            var expired = new NativeList<Entity>(8, Allocator.Temp);
            foreach (var (mark, entity) in SystemAPI
                .Query<RefRW<MarkedForSentence>>()
                .WithEntityAccess())
            {
                mark.ValueRW.TimeRemaining -= dt;
                if (mark.ValueRO.TimeRemaining <= 0f)
                    expired.Add(entity);
            }
            for (int i = 0; i < expired.Length; i++)
            {
                if (em.Exists(expired[i]) && em.HasComponent<MarkedForSentence>(expired[i]))
                    em.RemoveComponent<MarkedForSentence>(expired[i]);
            }
            expired.Dispose();

            // Phase 2: detect kills and stamp the killer.
            foreach (var (health, lastAttacker, victimFaction, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastAttackerEntity>, RefRO<FactionTag>>()
                .WithNone<DeathAnimationState, BuildingCollapseState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Entity killer = lastAttacker.ValueRO.Value;
                if (killer == Entity.Null || !em.Exists(killer)) continue;
                if (!em.HasComponent<FactionTag>(killer)) continue;

                Faction avengerFaction = victimFaction.ValueRO.Value;
                Faction killerFaction = em.GetComponentData<FactionTag>(killer).Value;
                if (avengerFaction == killerFaction) continue; // friendly fire — no mark

                // Gate on the VICTIM's faction having Justice adopted.
                byte level = SectQuery.LevelOf(em, avengerFaction,
                    SectConfig.Justice, SectLeverKind.Passive);
                if (level == 0) continue;

                var mark = new MarkedForSentence
                {
                    MarkerFaction = avengerFaction,
                    DamageBonus   = DamageBonusFor(level),
                    TimeRemaining = MarkDuration,
                };

                if (em.HasComponent<MarkedForSentence>(killer))
                {
                    // Refresh: take the harsher mark (longer duration / higher
                    // bonus) if multiple Justice factions both lost a unit to
                    // this killer recently.
                    var existing = em.GetComponentData<MarkedForSentence>(killer);
                    if (existing.MarkerFaction == avengerFaction)
                    {
                        // Same avenger — just refresh.
                        existing.TimeRemaining = MarkDuration;
                        em.SetComponentData(killer, existing);
                    }
                    else if (mark.DamageBonus > existing.DamageBonus
                          || mark.TimeRemaining > existing.TimeRemaining)
                    {
                        em.SetComponentData(killer, mark);
                    }
                }
                else
                {
                    em.AddComponentData(killer, mark);
                }
            }
        }
    }
}
