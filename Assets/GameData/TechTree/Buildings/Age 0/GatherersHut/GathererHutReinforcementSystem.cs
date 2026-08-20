// GathererHutReinforcementSystem.cs
// Alanthor Guild "reinforcement" research line for the Gatherer's Hut:
//
//   * Iron reinforcements  → the hut auto-repairs (5 HP/s) once it has been out
//                            of combat for OutOfCombatThreshold seconds.
//   * Veilstone walls      → at 75% HP or lower the hut CASTS a Slow burst
//                            (-50% speed, 7.5 s) on nearby enemies; repair → 10 HP/s.
//   * Veilsteel Pylons     → at 50% HP or lower the hut casts a STOP burst
//                            (-100% speed, 10 s); repair → 20 HP/s.
//
// The two wards are INDEPENDENT one-shot bursts, each on its own 90-second
// cooldown (per hut), NOT continuous auras: a sinking hut fires Slow first
// at 75%, then Stop at 50% (when both trip in the same tick, Stop wins the
// tick and Slow stays armed). Cooldowns live in GathererHutWardState.
//
// All three are faction-wide research flags read live from FactionResearchState
// (same "read live" model as the Shrine heal ladder / Vault banking).
//
// Auto-repair mirrors SectRenewalAutoRepairSystem; the burst mirrors
// UnitAbilitySystem.ApplyAoeSlow (SpellDebuff, ticked down + removed by
// SpellBuffSystem, applied to movement by UnitIntegratorSystem). Debuff targets
// are deduplicated across huts so we never double-add a component in one ECB.
//
// Location: Assets/Scripts/Systems/Economy/GathererHutReinforcementSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Economy
{
    // No [BurstCompile] — reads the managed FactionResearchState singleton and
    // makes structural changes (SpellDebuff add). Runs on a 0.5 s tick.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GathererHutReinforcementSystem : ISystem
    {
        private const float TickInterval = 0.5f;            // half-second tick
        private const float OutOfCombatThreshold = 10.0f;   // seconds without taking damage before auto-repair kicks in
        // Cast radius == the Guild's resource-collection radius, so the
        // defensive burst covers exactly the hut's gather area.
        private const float AuraRadius = GathererHutIncomeSystem.GatherRadius;
        private const float SlowDuration = 7.5f;            // Veilstone walls — slow burst duration
        private const float StopDuration = 10.0f;           // Veilsteel Pylons — stop burst duration
        private const float WardCooldown = 90.0f;           // seconds between defensive casts (per hut, per ward)
        private const float SlowReduction = 0.5f;           // -50% speed
        private const float StopReduction = 1.0f;           // -100% speed (root)
        private const float SlowTriggerFraction = 0.75f;    // Slow ward arms at 75% HP
        private const float StopTriggerFraction = 0.50f;    // Stop ward arms at 50% HP

        private float _tickTimer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GathererHutTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _tickTimer += dt;
            if (_tickTimer < TickInterval) return;
            float effectiveDt = _tickTimer;
            _tickTimer = 0f;

            var research = FactionResearchState.Instance;
            if (research == null) return;

            var em = state.EntityManager;
            double now = SystemAPI.Time.ElapsedTime;

            // Snapshot completed huts.
            var hutQuery = SystemAPI.QueryBuilder()
                .WithAll<GathererHutTag, LocalTransform, FactionTag, Health>()
                .WithNone<UnderConstruction>()
                .Build();

            var hutEntities = hutQuery.ToEntityArray(Allocator.Temp);
            if (hutEntities.Length == 0) { hutEntities.Dispose(); return; }

            var hutTransforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var hutFactions = hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Snapshot units once (potential debuff targets).
            var unitQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, FactionTag, LocalTransform>()
                .Build();
            var unitEntities = unitQuery.ToEntityArray(Allocator.Temp);
            var unitFactions = unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var unitTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Strongest speed-reduction to apply to each enemy unit this tick,
            // accumulated only from huts that actually CAST this tick.
            // Deduplicates across huts so we never record two adds for one entity.
            var reductionByUnit = new NativeHashMap<Entity, float>(unitEntities.Length + 1, Allocator.Temp);

            for (int h = 0; h < hutEntities.Length; h++)
            {
                var faction = hutFactions[h].Value;

                bool ironReinf = research.HasResearched(faction, "IronReinforcements");
                bool walls = research.HasResearched(faction, "VeilstoneWalls");
                bool pylons = research.HasResearched(faction, "VeilsteelPylons");
                if (!ironReinf && !walls && !pylons) continue;

                var hp = em.GetComponentData<Health>(hutEntities[h]);
                if (hp.Value <= 0) continue;

                // ── Auto-repair (Iron reinforcements) ───────────────────────
                if (ironReinf && hp.Value < hp.Max)
                {
                    bool outOfCombat = true;
                    if (em.HasComponent<BuildingDamageState>(hutEntities[h]))
                    {
                        var dmg = em.GetComponentData<BuildingDamageState>(hutEntities[h]);
                        if (now - dmg.LastDamagedAt < OutOfCombatThreshold) outOfCombat = false;
                    }

                    if (outOfCombat)
                    {
                        // 5 / 10 / 20 HP-per-second as the ladder is researched.
                        float rate = 5f + (walls ? 5f : 0f) + (pylons ? 10f : 0f);
                        int delta = (int)math.ceil(rate * effectiveDt);
                        if (delta < 1) delta = 1;
                        hp.Value = math.min(hp.Max, hp.Value + delta);
                        em.SetComponentData(hutEntities[h], hp);
                    }
                }

                // ── Defensive casts: Slow @ 75% (walls), Stop @ 50% (pylons),
                //    each on its OWN 90 s cooldown ─────────────────────────
                if (!walls && !pylons) continue;
                if (hp.Max <= 0) continue;
                float hpFraction = (float)hp.Value / hp.Max;

                var ward = em.HasComponent<GathererHutWardState>(hutEntities[h])
                    ? em.GetComponentData<GathererHutWardState>(hutEntities[h])
                    : default;

                // Stop outranks Slow when both trip in one tick; Slow stays
                // armed and fires on a later tick if the hut survives.
                bool castStop = pylons && hpFraction <= StopTriggerFraction
                    && now >= ward.NextStopCastAt;
                bool castSlow = !castStop && walls && hpFraction <= SlowTriggerFraction
                    && now >= ward.NextSlowCastAt;
                if (!castStop && !castSlow) continue;

                // Fire once at every enemy currently in radius.
                float2 center = new float2(hutTransforms[h].Position.x, hutTransforms[h].Position.z);
                float radiusSq = AuraRadius * AuraRadius;
                float reduction = castStop ? StopReduction : SlowReduction;

                for (int u = 0; u < unitEntities.Length; u++)
                {
                    // enemies only — allies do not trigger reinforcement.
                    // docs/Design/Teams.md
                    if (!Alliances.AreHostile(faction, unitFactions[u].Value)) continue;

                    float2 upos = new float2(unitTransforms[u].Position.x, unitTransforms[u].Position.z);
                    if (math.distancesq(center, upos) > radiusSq) continue;

                    if (reductionByUnit.TryGetValue(unitEntities[u], out float cur))
                    {
                        if (reduction > cur) reductionByUnit[unitEntities[u]] = reduction;
                    }
                    else
                    {
                        reductionByUnit.Add(unitEntities[u], reduction);
                        // Slow ward only: stick the slowdown aura on each newly
                        // slowed enemy for the debuff duration (follows the unit).
                        if (castSlow)
                            TheWaningBorder.Presentation.GuildWardVfx.AttachSlowAura(
                                em, unitEntities[u], unitTransforms[u].Position, SlowDuration);
                    }
                }

                // Start the fired ward's cooldown (add or update the state).
                if (castStop) ward.NextStopCastAt = now + WardCooldown;
                else ward.NextSlowCastAt = now + WardCooldown;
                if (em.HasComponent<GathererHutWardState>(hutEntities[h]))
                    em.SetComponentData(hutEntities[h], ward);
                else
                    em.AddComponentData(hutEntities[h], ward);

                // Presentation only — this system is non-Burst / main-thread, same
                // as SectActivePowerSystem which calls the VFX helpers directly.
                var castPos = hutTransforms[h].Position;
                if (castStop)
                {
                    TheWaningBorder.Presentation.SectPowerVfx.SpawnStopField(
                        castPos, AuraRadius, StopDuration);
                    TheWaningBorder.Presentation.SectPowerVfx.Spawn(
                        "Prefabs/Effects/Sect/NovaStorm", castPos, AuraRadius, StopDuration);
                }
                else
                {
                    // Slow ward: AuraCirclingArcane power-up + looping AuraSimpleArcane
                    // power, both scaled to the gather radius. Per-enemy AuraSlowdown
                    // auras are attached in the enemy loop above.
                    TheWaningBorder.Presentation.GuildWardVfx.SpawnGuildSlow(
                        castPos, AuraRadius, SlowDuration);
                }
            }

            // ── Apply the accumulated debuffs (one op per unique unit) ──────
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < unitEntities.Length; i++)
            {
                var unit = unitEntities[i];
                if (!reductionByUnit.TryGetValue(unit, out float reduction)) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = reduction,
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = reduction >= StopReduction ? StopDuration : SlowDuration
                };

                // Refresh only when at least as strong as an existing debuff, so
                // a slow burst never downgrades a stronger debuff already present.
                if (em.HasComponent<SpellDebuff>(unit))
                {
                    var existing = em.GetComponentData<SpellDebuff>(unit);
                    if (reduction >= existing.SpeedReduction)
                        ecb.SetComponent(unit, debuff);
                }
                else
                {
                    ecb.AddComponent(unit, debuff);
                }
            }
            ecb.Playback(em);
            ecb.Dispose();

            reductionByUnit.Dispose();
            hutEntities.Dispose();
            hutTransforms.Dispose();
            hutFactions.Dispose();
            unitEntities.Dispose();
            unitFactions.Dispose();
            unitTransforms.Dispose();
        }
    }
}
