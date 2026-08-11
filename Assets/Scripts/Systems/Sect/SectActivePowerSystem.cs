// SectActivePowerSystem.cs
// Active-Power lever dispatch (task-063 phase 5). Each adopted sect
// exposes one triggered ability per the SectLeverEffects.ActiveOf table;
// players (and AI) request a cast via SectActivePowerHelper.Fire which
// validates cooldown + lever level, deducts the cooldown, and then
// dispatches to the per-kind handler in SwitchOnKind.
//
// The cooldown lives in a DynamicBuffer<SectActivePowerCooldown> on the
// faction bank entity so all 12 sects' timers per faction live in one
// place. SectActivePowerSystem ticks all cooldowns and prunes entries
// at zero.
//
// Magnitudes scale with the Active-Power lever level via
// SectLeverEffects.LevelScalar; cooldowns scale inversely.
//
// task-063 phase 5.
//
// Location: Assets/Scripts/Systems/Sect/SectActivePowerSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectActivePowerSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // No specific RequireForUpdate — runs every tick to bleed cooldowns.
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Snapshot entities first — DynamicBuffer iteration variables are
            // read-only inside a SystemAPI foreach, so we re-fetch the buffer
            // via EntityManager.GetBuffer for the mutating pass.
            var bankEntities = new Unity.Collections.NativeList<Entity>(
                Unity.Collections.Allocator.Temp);
            foreach (var (_, entity) in SystemAPI
                .Query<DynamicBuffer<SectActivePowerCooldown>>()
                .WithEntityAccess())
            {
                bankEntities.Add(entity);
            }

            for (int b = 0; b < bankEntities.Length; b++)
            {
                var bank = bankEntities[b];
                if (!em.Exists(bank)) continue;
                if (!em.HasBuffer<SectActivePowerCooldown>(bank)) continue;
                var cooldowns = em.GetBuffer<SectActivePowerCooldown>(bank);
                for (int i = cooldowns.Length - 1; i >= 0; i--)
                {
                    var cd = cooldowns[i];
                    cd.Remaining -= dt;
                    if (cd.Remaining <= 0f)
                        cooldowns.RemoveAtSwapBack(i);
                    else
                        cooldowns[i] = cd;
                }
            }
            bankEntities.Dispose();

            // Wind-up tick for offensive strikes: apply the effect (and its
            // impact VFX) when the telegraph runs out, then drop the entity.
            var landed = new Unity.Collections.NativeList<Entity>(
                Unity.Collections.Allocator.Temp);
            foreach (var (strike, entity) in SystemAPI
                .Query<RefRW<PendingSectStrike>>().WithEntityAccess())
            {
                strike.ValueRW.Windup -= dt;
                if (strike.ValueRO.Windup <= 0f)
                    landed.Add(entity);
            }
            for (int i = 0; i < landed.Length; i++)
            {
                var e = landed[i];
                if (!em.Exists(e)) continue;
                var s = em.GetComponentData<PendingSectStrike>(e);
                em.DestroyEntity(e);

                SectActivePowerHelper.DispatchEffect(em, s.Caster,
                    (SectActivePowerKind)s.Kind, s.Position, s.Radius,
                    s.Magnitude, s.Duration, s.Level);
                TheWaningBorder.Presentation.SectPowerVfx.SpawnForSect(
                    SectConfig.IdAt(s.SectIndex), s.Position, s.Radius);
            }
            landed.Dispose();
        }
    }

    /// <summary>
    /// Static helper for firing a sect's Active Power. UI buttons /
    /// hotkeys / AI all funnel through Fire so the cooldown + spec
    /// lookup live in one place.
    /// </summary>
    public static class SectActivePowerHelper
    {
        /// <summary>
        /// Returns the remaining cooldown for the faction's sect Active
        /// Power, or 0 if ready (or if the lever isn't bought).
        /// </summary>
        /// <summary>True if the faction's Temple has GlowAllocated == 1 on the slot whose SectId matches.</summary>
        public static bool HasGlowAllocated(EntityManager em, Faction faction, string sectId)
        {
            if (!TryGetFactionTemple(em, faction, out var temple)) return false;
            if (!em.HasBuffer<TempleChapelSlot>(temple)) return false;
            var buf = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SectId.ToString() == sectId) return buf[i].GlowAllocated == 1;
            }
            return false;
        }

        /// <summary>True if this faction has adopted the sect (any AdoptedAtAge != 0).</summary>
        public static bool IsAdopted(EntityManager em, Faction faction, string sectId)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<SectAdoptionState>(bank)) return false;
            var sect = em.GetComponentData<SectAdoptionState>(bank).Get(sectId);
            return sect.IsAdopted;
        }

        /// <summary>
        /// Allocate 1 Glow from the Temple's GlowStored to the matching sect's
        /// shrine slot. No-op if Glow is already allocated, the sect isn't
        /// adopted, or the Temple has no Glow to spend.
        /// </summary>
        public static bool AllocateGlow(EntityManager em, Faction faction, string sectId)
        {
            if (!IsAdopted(em, faction, sectId)) return false;
            if (!TryGetFactionTemple(em, faction, out var temple)) return false;
            if (!em.HasComponent<GlowStored>(temple)) return false;
            if (!em.HasBuffer<TempleChapelSlot>(temple)) return false;

            var stored = em.GetComponentData<GlowStored>(temple);
            if (stored.Amount <= 0) return false;

            var buf = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SectId.ToString() != sectId) continue;
                if (buf[i].GlowAllocated == 1) return false;  // no stacking
                var slot = buf[i];
                slot.GlowAllocated = 1;
                buf[i] = slot;
                stored.Amount -= 1;
                em.SetComponentData(temple, stored);
                return true;
            }
            return false;
        }

        /// <summary>Deallocate 1 Glow from this sect's shrine (refunded to the Temple's GlowStored).</summary>
        public static bool DeallocateGlow(EntityManager em, Faction faction, string sectId)
        {
            if (!TryGetFactionTemple(em, faction, out var temple)) return false;
            if (!em.HasComponent<GlowStored>(temple)) return false;
            if (!em.HasBuffer<TempleChapelSlot>(temple)) return false;

            var buf = em.GetBuffer<TempleChapelSlot>(temple);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SectId.ToString() != sectId) continue;
                if (buf[i].GlowAllocated == 0) return false;
                var slot = buf[i];
                slot.GlowAllocated = 0;
                buf[i] = slot;
                var stored = em.GetComponentData<GlowStored>(temple);
                stored.Amount += 1;
                em.SetComponentData(temple, stored);
                return true;
            }
            return false;
        }

        /// <summary>Look up the faction's TempleOfRidan entity, if any.</summary>
        private static bool TryGetFactionTemple(EntityManager em, Faction faction, out Entity temple)
        {
            temple = Entity.Null;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value == faction) { temple = ents[i]; return true; }
            }
            return false;
        }

        public static float CooldownRemaining(EntityManager em, Faction faction, string sectId, int tier = 1)
        {
            int sectIdx = SectConfig.IndexOf(sectId);
            if (sectIdx < 0) return float.MaxValue;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return float.MaxValue;
            if (!em.HasBuffer<SectActivePowerCooldown>(bank)) return 0f;
            var buf = em.GetBuffer<SectActivePowerCooldown>(bank);
            // Tier 0 entries are pre-tier legacy stamps; treat them as tier 1.
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SectIndex != sectIdx) continue;
                int entryTier = buf[i].Tier == 0 ? 1 : buf[i].Tier;
                if (entryTier == tier) return buf[i].Remaining;
            }
            return 0f;
        }

        public static bool CanFire(EntityManager em, Faction faction, string sectId, int tier = 1)
        {
            if (!IsAdopted(em, faction, sectId)) return false;
            return CooldownRemaining(em, faction, sectId, tier) <= 0f;
        }

        /// <summary>Highest active-skill tier this faction has unlocked on
        /// the sect (levers auto-track the temple level, so this is the
        /// sect's ActivePowerLevel clamped to the defined tiers).</summary>
        public static int UnlockedTier(EntityManager em, Faction faction, string sectId)
        {
            byte level = SectQuery.LevelOf(em, faction, sectId, SectLeverKind.ActivePower);
            return level == 0 ? 1 : (level > 3 ? 3 : level);
        }

        /// <summary>
        /// Attempt to fire <paramref name="sectId"/>'s Active Power for
        /// <paramref name="faction"/> at <paramref name="targetPos"/>.
        /// Returns true if the cast succeeded (cooldown is set and the
        /// effect was dispatched), false otherwise.
        /// </summary>
        public static bool Fire(EntityManager em, Faction faction, string sectId, float3 targetPos)
            => Fire(em, faction, sectId, 1, targetPos);

        public static bool Fire(EntityManager em, Faction faction, string sectId, int tier, float3 targetPos)
        {
            // Every ADOPTED sect has its tier-1 power; tiers 2/3 unlock with
            // the temple level (levers auto-track it). Tiered specs are
            // hand-tuned per tier, so the old per-level magnitude scalar is
            // gone — the tier IS the scaling.
            if (!IsAdopted(em, faction, sectId)) return false;
            if (tier < 1) tier = 1;
            if (tier > UnlockedTier(em, faction, sectId)) return false;
            if (CooldownRemaining(em, faction, sectId, tier) > 0f) return false;

            var spec = SectLeverEffects.ActiveOf(sectId, tier);
            if (spec.Kind == SectActivePowerKind.None) return false;

            byte effLevel = (byte)tier;
            float radius = spec.Radius;
            float magnitude = spec.Magnitude;
            float duration = spec.Duration;
            float cooldown = spec.Cooldown;
            // Global rebalance (2026-08-04): every power now WINDS UP before
            // landing, so cooldowns halve to keep powers present in play —
            // telegraphed-but-frequent beats instant-but-rare.
            cooldown *= 0.5f;
            if (HasGlowAllocated(em, faction, sectId)) cooldown *= 0.5f;

            // Shrine of Ridan simple upgrade (design 2026-07-04): reduces
            // sect power cooldowns — -10% at L2, -20% at L3.
            int shrineLv = ChoiceUpgradeQuery.MaxShrineLevel(em, faction);
            if (shrineLv >= 2) cooldown *= 0.8f;
            else if (shrineLv == 1) cooldown *= 0.9f;

            // EVERY power winds up now (design 2026-08-04 — was offensive
            // only): a telegraph ring marks the circle for the windup so
            // everyone can react, then SectActivePowerSystem applies the
            // effect. Offensive strikes keep their longer tell; buffs and
            // utility land after a short charge. The cooldown still starts
            // at cast.
            float windup = IsOffensive(spec.Kind)
                ? OffensiveWindupSeconds
                : UtilityWindupSeconds;
            var strike = em.CreateEntity(typeof(PendingSectStrike));
            em.SetComponentData(strike, new PendingSectStrike
            {
                Kind      = (byte)spec.Kind,
                SectIndex = (byte)SectConfig.IndexOf(sectId),
                Level     = effLevel,
                Caster    = faction,
                Position  = targetPos,
                Radius    = radius,
                Magnitude = magnitude,
                Duration  = duration,
                Windup    = windup,
            });
            TheWaningBorder.Presentation.SectPowerVfx.SpawnTelegraph(
                targetPos, radius, windup);

            // Presentation: golden minimap ping at the cast site.
            TheWaningBorder.UI.GameUI.MinimapPings.Post(targetPos,
                TheWaningBorder.UI.GameUI.MinimapPings.Power, 4f, big: true);

            // Stamp cooldown — per (sect, tier); each tier cools independently.
            int sectIdx = SectConfig.IndexOf(sectId);
            if (FactionEconomy.TryGetBank(em, faction, out var bank))
            {
                DynamicBuffer<SectActivePowerCooldown> buf;
                if (!em.HasBuffer<SectActivePowerCooldown>(bank))
                    buf = em.AddBuffer<SectActivePowerCooldown>(bank);
                else
                    buf = em.GetBuffer<SectActivePowerCooldown>(bank);

                bool found = false;
                for (int i = 0; i < buf.Length; i++)
                {
                    int entryTier = buf[i].Tier == 0 ? 1 : buf[i].Tier;
                    if (buf[i].SectIndex == sectIdx && entryTier == tier)
                    {
                        buf[i] = new SectActivePowerCooldown { SectIndex = (byte)sectIdx, Tier = (byte)tier, Remaining = cooldown };
                        found = true; break;
                    }
                }
                if (!found)
                    buf.Add(new SectActivePowerCooldown { SectIndex = (byte)sectIdx, Tier = (byte)tier, Remaining = cooldown });
            }
            return true;
        }

        /// <summary>Wind-up applied to hostile-target powers so they can be
        /// dodged (damage bursts, burning ground, pyres, the Codex freeze).</summary>
        public const float OffensiveWindupSeconds = 1.5f;
        /// <summary>Windup for non-offensive powers (2026-08-04: every power
        /// telegraphs now) — a short visible charge, not a combat tell.</summary>
        public const float UtilityWindupSeconds = 1.0f;

        private static bool IsOffensive(SectActivePowerKind kind) => kind switch
        {
            SectActivePowerKind.SmiteCircle     => true,
            SectActivePowerKind.BurningCircle   => true,
            SectActivePowerKind.SpawnPyre       => true,
            SectActivePowerKind.FreezeCooldowns => true,
            _                                   => false,
        };

        // Internal so SectActivePowerSystem can apply a wound-up strike.
        internal static void DispatchEffect(EntityManager em, Faction faction,
            SectActivePowerKind kind, float3 pos, float radius, float magnitude, float duration,
            byte level = 1)
        {
            switch (kind)
            {
                case SectActivePowerKind.FreezeCooldowns:
                    // Recall the Codex — magnitude carries the freeze
                    // duration (level-scaled by the caller); Lv III also
                    // surges CURRENT cooldowns +50%.
                    ApplyCooldownFreeze(em, faction, pos, radius, magnitude, surge: level >= 3);
                    break;
                case SectActivePowerKind.SmiteCircle:
                    ApplyCircleDamage(em, faction, pos, radius, (int)magnitude);
                    break;
                case SectActivePowerKind.HealCircle:
                    ApplyCircleHeal(em, faction, pos, radius, (int)magnitude);
                    break;
                case SectActivePowerKind.ArmorCircle:
                    ApplyCircleBuff(em, faction, pos, radius,
                        new SpellBuff { ArmorBonus = magnitude, TimeRemaining = duration });
                    break;
                case SectActivePowerKind.DamageCircle:
                    ApplyCircleBuff(em, faction, pos, radius,
                        new SpellBuff { DamageMultiplier = magnitude, TimeRemaining = duration });
                    break;
                case SectActivePowerKind.SpeedCircle:
                    ApplyCircleBuff(em, faction, pos, radius,
                        new SpellBuff { SpeedMultiplier = magnitude, TimeRemaining = duration });
                    break;
                case SectActivePowerKind.BurningCircle:
                case SectActivePowerKind.SpawnPyre:
                    SpawnBurning(em, faction, pos, radius, magnitude, duration);
                    break;
                case SectActivePowerKind.RevealCircle:
                    SpawnReveal(em, faction, pos, radius, duration);
                    break;
            }
        }

        private static void ApplyCircleDamage(EntityManager em, Faction faction,
            float3 center, float radius, int dmg)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value == faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                var hp = em.GetComponentData<Health>(e);
                hp.Value = math.max(0, hp.Value - dmg);
                em.SetComponentData(e, hp);
            }

            // Buildings burn under the god's hand too (2026-08-11: smite
            // ignored structures entirely — "Sect powers should damage
            // buildings but they don't"). Two exemptions: WALL pieces (only
            // siege touches the fortification line — Combat_Pacing.md) and
            // Border-owned structures (wells are verb objectives, never
            // splash targets).
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var buildings = bq.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < buildings.Length; i++)
            {
                var e = buildings[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == faction || fac == Faction.Border) continue;
                if (em.HasComponent<WallTag>(e)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                var hp = em.GetComponentData<Health>(e);
                hp.Value = math.max(0, hp.Value - dmg);
                em.SetComponentData(e, hp);
            }
        }

        private static void ApplyCircleHeal(EntityManager em, Faction faction,
            float3 center, float radius, int amount)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                hp.Value = math.min(hp.Max, hp.Value + amount);
                em.SetComponentData(e, hp);
            }
        }

        private static void ApplyCircleBuff(EntityManager em, Faction faction,
            float3 center, float radius, SpellBuff buff)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                TheWaningBorder.Systems.Combat.CombatDamageHelper.MergeSpellBuff(em, ecb, e, buff);
            }
            ecb.Playback(em);
            ecb.Dispose();
        }

        private static void SpawnBurning(EntityManager em, Faction faction,
            float3 center, float radius, float dps, float duration)
        {
            var pyre = em.CreateEntity(
                typeof(BurningGround), typeof(LocalTransform), typeof(FactionTag));
            em.SetComponentData(pyre, new BurningGround
            {
                DPS = dps,
                TimeRemaining = duration,
                Radius = radius,
            });
            em.SetComponentData(pyre, LocalTransform.FromPositionRotationScale(
                center, quaternion.identity, 1f));
            em.SetComponentData(pyre, new FactionTag { Value = faction });
        }

        /// <summary>
        /// Spawn a timed fog-reveal entity: FactionTag + LocalTransform +
        /// LineOfSight is exactly the trio FogOfWarSystem stamps vision from,
        /// so the circle lights up on the next fog frame with zero special
        /// support. SectRevealTickSystem destroys it when the timer ends.
        /// Public — the Reliquary's Scry/Vision abilities reuse it.
        /// </summary>
        public static void SpawnReveal(EntityManager em, Faction faction,
            float3 center, float radius, float duration)
        {
            var reveal = em.CreateEntity(
                typeof(SectRevealMarker), typeof(LocalTransform),
                typeof(FactionTag), typeof(LineOfSight));
            em.SetComponentData(reveal, new SectRevealMarker { TimeRemaining = duration });
            em.SetComponentData(reveal, LocalTransform.FromPositionRotationScale(
                center, quaternion.identity, 1f));
            em.SetComponentData(reveal, new FactionTag { Value = faction });
            em.SetComponentData(reveal, new LineOfSight { Radius = radius });
        }

        /// <summary>
        /// Recall the Codex / Reliquary lockout: stamp CodexFrozen on every
        /// ENEMY unit in the circle — their attack/ability cooldowns stop
        /// recovering for the duration. With <paramref name="surge"/> (Lv III)
        /// their CURRENT cooldowns are also inflated +50% on application.
        /// Public — the Reliquary's Lockout ability reuses it.
        /// </summary>
        public static void ApplyCooldownFreeze(EntityManager em, Faction faction,
            float3 center, float radius, float duration, bool surge)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            var toFreeze = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value == faction) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;
                toFreeze.Add(e);
            }

            for (int i = 0; i < toFreeze.Length; i++)
            {
                var e = toFreeze[i];

                if (em.HasComponent<CodexFrozen>(e))
                {
                    var f = em.GetComponentData<CodexFrozen>(e);
                    if (duration > f.TimeRemaining) f.TimeRemaining = duration;
                    em.SetComponentData(e, f);
                }
                else
                {
                    em.AddComponentData(e, new CodexFrozen { TimeRemaining = duration });
                }

                if (surge)
                {
                    if (em.HasComponent<AttackCooldown>(e))
                    {
                        var cd = em.GetComponentData<AttackCooldown>(e);
                        cd.Timer *= 1.5f;
                        em.SetComponentData(e, cd);
                    }
                    if (em.HasComponent<ArcherState>(e))
                    {
                        var ast = em.GetComponentData<ArcherState>(e);
                        ast.CooldownTimer *= 1.5f;
                        em.SetComponentData(e, ast);
                    }
                }
            }
            toFreeze.Dispose();
        }
    }
}
