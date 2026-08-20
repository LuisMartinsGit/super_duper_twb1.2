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
    public static partial class SectActivePowerHelper
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
            // Canon sects hand over all THREE actives the moment the sect is
            // adopted (docs/Design/Sects.md section 1); what Temple upgrades
            // buy is the power LEVEL, a separate axis read through
            // SectQuery.PowerLevelOf (section 3). Gating the SLOT on the
            // Active-Power lever as well is the legacy tier model, and it left
            // two of every canon sect's three powers uncastable until the
            // Temple was upgraded - Heavy Bureaucracy and Sew Disorder were
            // simply unreachable for an Antiquity faction at a Lv-1 Temple.
            if (SectLeverEffects.IsCanonSect(sectId)) return SectLeverEffects.ActiveSlots;

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
            // Blood Rain silences the whole map, both sides, the caster
            // included (docs/Design/Sects.md section 6). Checked before the
            // cooldown is charged so a blocked cast costs nothing.
            if (IsGloballySilenced(em)) return false;
            if (tier < 1) tier = 1;
            if (tier > UnlockedTier(em, faction, sectId)) return false;
            if (CooldownRemaining(em, faction, sectId, tier) > 0f) return false;

            // `tier` is the SLOT — which of the sect's three actives was
            // clicked. The LEVEL is a separate axis, earned by adopting before
            // a Temple upgrade rather than bought (docs/Design/Sects.md
            // section 3), so it is read here rather than inferred from the slot.
            byte powerLevel = SectQuery.PowerLevelOf(em, faction, sectId);
            if (powerLevel < 1) powerLevel = 1;

            var spec = SectLeverEffects.ActiveOf(sectId, tier, powerLevel);
            if (spec.Kind == SectActivePowerKind.None) return false;

            // Curse wells are not castable targets for offensive powers.
            // Rejected BEFORE the cooldown is charged so a misclick costs
            // nothing and the player can simply re-aim.
            if (IsOffensive(spec.Kind) && IsOnCurseWell(em, targetPos)) return false;

            // The effect level the strike lands at is the sect's power level,
            // not the slot index. Before the canon pass these two were the same
            // number, which is why a sect's third button used to behave like a
            // level-III cast regardless of when the sect was adopted.
            byte effLevel = powerLevel;
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

        /// <summary>Radius around a curse well inside which an offensive cast is
        /// refused. Generous — the intent is "you cannot aim at the well", not
        /// "you must miss it by a hair".</summary>
        private const float WellNoCastRadius = 8f;

        /// <summary>
        /// True when the aim point sits on a curse well. Wells are VERB
        /// objectives — they are claimed with purify/pacify/destroy rituals,
        /// never damaged — and ApplyCircleDamage already exempts
        /// Faction.Border, so a smite aimed at one did nothing but burn the
        /// cooldown. Refuse the cast instead, before anything is spent.
        /// </summary>
        private static bool IsOnCurseWell(EntityManager em, float3 pos)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xf = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xf.Length; i++)
            {
                float dx = xf[i].Position.x - pos.x;
                float dz = xf[i].Position.z - pos.z;
                if (dx * dx + dz * dz <= WellNoCastRadius * WellNoCastRadius) return true;
            }
            return false;
        }

        private static bool IsOffensive(SectActivePowerKind kind) => kind switch
        {
            SectActivePowerKind.SmiteCircle       => true,
            SectActivePowerKind.BurningCircle     => true,
            SectActivePowerKind.SpawnPyre         => true,
            SectActivePowerKind.FreezeCooldowns   => true,
            // Canon kinds that target the enemy. Everything else in the canon
            // set is self-buff, terrain or economy and may be cast anywhere,
            // including on top of a curse well.
            SectActivePowerKind.BuildingShutdown  => true,
            SectActivePowerKind.HostileConversion => true,
            SectActivePowerKind.UnmakeBuilding    => true,
            SectActivePowerKind.SpitePool         => true,
            _                                     => false,
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
                case SectActivePowerKind.UnmakeBuilding:
                    ApplyUnmake(em, faction, pos, radius, magnitude, level);
                    break;
                case SectActivePowerKind.SpitePool:
                    ApplySpite(em, faction, pos, radius);
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

                // ── Canon kinds (docs/Design/Sects.md) ──────────────────────
                // Bodies live in SectActivePowerSystem.Alanthor.cs.
                case SectActivePowerKind.BuildingShutdown:
                    ApplyBuildingShutdown(em, faction, pos, radius, duration);
                    break;
                case SectActivePowerKind.HostileConversion:
                    ApplyHostileConversion(em, faction, pos, radius, duration);
                    break;
                case SectActivePowerKind.HealCirclePercent:
                    ApplyCircleHealPercent(em, faction, pos, radius, magnitude, duration);
                    break;
                case SectActivePowerKind.RaiseTower:
                    RaiseWatchTowers(em, faction, pos, radius, (byte)magnitude, duration);
                    break;
                case SectActivePowerKind.DeathWard:
                    ApplyDeathWard(em, faction, pos, radius, magnitude, duration);
                    break;
                case SectActivePowerKind.Veil:
                    ApplyVeil(em, faction, pos, radius, magnitude, duration);
                    break;
                case SectActivePowerKind.BuildingHpBuff:
                    ApplyBulwark(em, faction, pos, radius, magnitude, duration, level);
                    break;
                case SectActivePowerKind.Invulnerable:
                    ApplyInvulnerable(em, faction, pos, radius, duration);
                    break;
                case SectActivePowerKind.NodeOverYield:
                    ApplyNodeOverYield(em, faction, pos, radius, duration, level);
                    break;
                case SectActivePowerKind.InfluenceBurst:
                    SpawnInfluenceBurst(em, faction, pos, radius, magnitude, duration, level);
                    break;
                case SectActivePowerKind.CurseWard:
                    ApplyCurseWard(em, faction, pos, radius, magnitude, duration);
                    break;

                // -- War canon kinds. Bodies in SectActivePowerSystem.War.cs --
                case SectActivePowerKind.BloodRain:
                    ApplyBloodRain(em, faction, pos, radius, magnitude, duration);
                    break;
                case SectActivePowerKind.TrainingBoon:
                    ApplyCallToArms(em, faction, pos, radius, magnitude, duration, level);
                    break;
                case SectActivePowerKind.DamageArmorCircle:
                    ApplyDamageArmorCircle(em, faction, pos, radius, magnitude, duration);
                    break;
            }
        }

        /// <summary>
        /// Ruin "Unmake" (docs/Design/Sects.md): exactly ONE enemy building —
        /// the nearest to the cast point inside <paramref name="radius"/> —
        /// loses <paramref name="hpFraction"/> of its CURRENT hp. The radius is
        /// a search range, never a blast: however many buildings stand in it,
        /// only one is ever unmade.
        ///
        /// At level III the design adds a 25% splash to OTHER buildings in a
        /// small area around the one that was unmade.
        /// </summary>
        private static void ApplyUnmake(EntityManager em, Faction faction,
            float3 center, float radius, float hpFraction, byte level)
        {
            const float SplashRadius = 6f;      // "small area" (Sects.md)
            const float SplashFraction = 0.25f;

            float r2 = radius * radius;
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var buildings = bq.ToEntityArray(Allocator.Temp);

            // Nearest hostile building wins. Same two exemptions as the smite:
            // wall pieces belong to siege alone, and Border structures are verb
            // objectives rather than targets.
            Entity target = Entity.Null;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < buildings.Length; i++)
            {
                var e = buildings[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == Faction.Border) continue;
                if (!Alliances.AreHostile(faction, fac)) continue;
                if (em.HasComponent<WallTag>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2 || d2 >= bestD2) continue;
                bestD2 = d2;
                target = e;
            }
            if (target == Entity.Null) return;

            var hp = em.GetComponentData<Health>(target);
            hp.Value = math.max(0, hp.Value - (int)(hp.Value * hpFraction));
            em.SetComponentData(target, hp);

            if (level < 3) return;

            float3 epicentre = em.GetComponentData<LocalTransform>(target).Position;
            float s2 = SplashRadius * SplashRadius;
            for (int i = 0; i < buildings.Length; i++)
            {
                var e = buildings[i];
                if (e == target) continue;
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == Faction.Border) continue;
                if (!Alliances.AreHostile(faction, fac)) continue;
                if (em.HasComponent<WallTag>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - epicentre.x, dz = p.z - epicentre.z;
                if (dx * dx + dz * dz > s2) continue;

                var shp = em.GetComponentData<Health>(e);
                shp.Value = math.max(0, shp.Value - (int)(shp.Value * SplashFraction));
                em.SetComponentData(e, shp);
            }
        }

        /// <summary>
        /// Wrath "Spite" (docs/Design/Sects.md): every enemy unit in the area
        /// has the damage it has dealt this match added to one pool; the pool
        /// is then split equally over those same units and each takes its
        /// share. Canon example: five units that have dealt 200 between them
        /// take 40 each.
        ///
        /// Levels scale the AREA only — the arithmetic here is identical at
        /// every level, so a bigger Spite catches more of the army rather than
        /// hitting harder per head.
        /// </summary>
        private static void ApplySpite(EntityManager em, Faction faction,
            float3 center, float radius)
        {
            float r2 = radius * radius;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            var caught = new NativeList<Entity>(Allocator.Temp);
            long pool = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                caught.Add(e);
                if (em.HasComponent<DamageDealtTotal>(e))
                    pool += em.GetComponentData<DamageDealtTotal>(e).Value;
            }

            // A crowd that has never landed a blow owes nothing.
            if (caught.Length > 0 && pool > 0)
            {
                // Integer division, so the split is bit-identical on every
                // lockstep peer regardless of float order.
                int share = (int)(pool / caught.Length);
                if (share > 0)
                {
                    for (int i = 0; i < caught.Length; i++)
                    {
                        var hp = em.GetComponentData<Health>(caught[i]);
                        hp.Value = math.max(0, hp.Value - share);
                        em.SetComponentData(caught[i], hp);
                    }
                }
            }
            caught.Dispose();
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
                // Enemy-only power: allies are spared. docs/Design/Teams.md
                if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
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
                // Friendly-only power: reaches allies too. docs/Design/Teams.md
                if (!Alliances.AreAllied(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
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
                // Friendly-only power: reaches allies too. docs/Design/Teams.md
                if (!Alliances.AreAllied(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
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
                // Enemy-only power: allies are spared. docs/Design/Teams.md
                if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
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
