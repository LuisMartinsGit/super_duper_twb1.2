// File: Assets/GameData/TechTree/Abilities/Sect/SectActivePowerSystem.Alanthor.cs
// Effect bodies for the ten canon power kinds the Alanthor rewrite introduced
// (docs/Design/Sects.md section 4). The dispatch switch lives in
// SectActivePowerSystem.cs; this file is only the bodies, split out so the
// original file stays readable.
//
// Every body follows the same shape as the pre-existing ApplyCircle* helpers:
// query, filter by faction and radius, stamp a component through an
// EntityCommandBuffer, play back. Structural changes never happen mid-query.
//
// Single Target is expressed as a radius (SectRadii.Single) rather than as a
// separate code path: the caller aims at an entity, and a 1.5 m circle around
// the aim point picks exactly it. The one place that matters — Bulwark I and
// Heavy Bureaucracy I — additionally stops after the first match so a tight
// cluster of buildings cannot be caught by a "single target" cast.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Sect
{
    public static partial class SectActivePowerHelper
    {
        /// <summary>A cast at this radius or tighter hits one entity only.</summary>
        private static bool IsSingleTarget(float radius) => radius <= SectRadii.Single + 0.01f;

        // ── Antiquity ───────────────────────────────────────────────────────

        /// <summary>
        /// Heavy Bureaucracy. Enemy buildings in the circle produce nothing at
        /// all for the duration. Re-casting refreshes rather than stacking.
        /// </summary>
        private static void ApplyBuildingShutdown(EntityManager em, Faction faction,
            float3 center, float radius, float duration)
        {
            bool single = IsSingleTarget(radius);
            float r2 = radius * radius;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var owner = em.GetComponentData<FactionTag>(e).Value;
                if (owner == faction || owner == Faction.Border) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                var stamp = new SectShutdown { TimeRemaining = duration };
                if (em.HasComponent<SectShutdown>(e)) ecb.SetComponent(e, stamp);
                else                                  ecb.AddComponent(e, stamp);
                if (single) break;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        /// <summary>
        /// Sew Disorder. Enemy units in the circle turn hostile to everything,
        /// their own side included. The original faction is recorded so a
        /// non-permanent cast can hand them back. Duration 0 means Lv III —
        /// hostile until killed.
        /// </summary>
        private static void ApplyHostileConversion(EntityManager em, Faction faction,
            float3 center, float radius, float duration)
        {
            float r2 = radius * radius;
            float stored = duration > 0f ? duration : SectEffectDuration.Permanent;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var owner = em.GetComponentData<FactionTag>(e).Value;
                if (owner == faction || owner == Faction.Border) continue;
                // Do not re-stamp an already-disordered unit: that would
                // overwrite OriginalFaction with the disordered faction and
                // strand the unit permanently.
                if (em.HasComponent<SectDisordered>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                ecb.AddComponent(e, new SectDisordered
                {
                    TimeRemaining   = stored,
                    OriginalFaction = owner,
                });

                // "Hostile to all other units" is exactly what Faction.Border
                // already means to every combat system, so the flip reuses that
                // plumbing rather than teaching the targeting stack a second
                // notion of hostility. The tick hands the unit back when the
                // timer ends; at Lv III it never does.
                //
                // Known trade-off: ApplyCircleDamage exempts Faction.Border, so
                // a disordered unit cannot be finished off by a sect smite while
                // the effect lasts. Ordinary weapons still kill it.
                ecb.SetComponent(e, new FactionTag { Value = Faction.Border });
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        // ── Renewal ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hands of Plenty. Heals a FRACTION of max HP — units and buildings
        /// alike, which is what makes it Renewal's signature rather than a
        /// second Heal Circle. A non-zero duration adds the Lv III regen tail.
        /// </summary>
        private static void ApplyCircleHealPercent(EntityManager em, Faction faction,
            float3 center, float radius, float fraction, float duration)
        {
            float r2 = radius * radius;

            var query = em.CreateEntityQuery(
                ComponentType.ReadWrite<Health>(),
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

                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;            // dead, and DeathSystem owns it
                hp.Value = math.min(hp.Max, hp.Value + (int)(hp.Max * fraction));
                em.SetComponentData(e, hp);

                if (duration <= 0f) continue;
                // The tail restores the same fraction again, spread over the
                // duration, so Lv III reads as "80% now, 80% more over 10s".
                var tail = new SectRegenTail
                {
                    TimeRemaining     = duration,
                    FractionPerSecond = fraction / duration,
                };
                if (em.HasComponent<SectRegenTail>(e)) ecb.SetComponent(e, tail);
                else                                   ecb.AddComponent(e, tail);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        /// <summary>
        /// Raise Anew. Conjures Watch Towers outright — it never touches a
        /// construction queue. Single Target raises one at the aim point; a
        /// wider cast rings the point so the towers do not overlap.
        /// </summary>
        private static void RaiseWatchTowers(EntityManager em, Faction faction,
            float3 center, float radius, byte towerLevel, float duration)
        {
            if (towerLevel < 1) towerLevel = 1;
            float life = duration > 0f ? duration : SectEffectDuration.Permanent;

            if (IsSingleTarget(radius))
            {
                RaiseOneTower(em, faction, center, towerLevel, life);
                return;
            }

            // An 8 m circle fits three towers comfortably without them clipping.
            const int Count = 3;
            float ring = radius * 0.55f;
            for (int i = 0; i < Count; i++)
            {
                float a = (math.PI * 2f / Count) * i;
                var p = new float3(center.x + math.cos(a) * ring, center.y,
                                   center.z + math.sin(a) * ring);
                RaiseOneTower(em, faction, p, towerLevel, life);
            }
        }

        private static void RaiseOneTower(EntityManager em, Faction faction,
            float3 position, byte towerLevel, float life)
        {
            var tower = WatchTower.Create(em, position, faction);

            if (towerLevel > 1)
            {
                // Ride the normal Lv1-3 ladder rather than inventing conjured-
                // tower stats, so a raised Lv 3 tower is exactly a Lv 3 tower.
                var hp = em.GetComponentData<Health>(tower);
                int baseMax = hp.Max;
                int scaled = (int)(baseMax * BuildingUpgradeConfig.HpMultiplier[towerLevel]);
                hp.Max   = scaled;
                hp.Value = scaled;
                em.SetComponentData(tower, hp);

                em.AddComponentData(tower, new BuildingUpgradeState
                {
                    Level     = towerLevel,
                    BaseHpMax = baseMax,
                });
            }

            em.AddComponentData(tower, new SectConjuredTower
            {
                TimeRemaining = life,
                TowerLevel    = towerLevel,
            });
        }

        /// <summary>Second Wind. Allied units in the circle cannot drop below 1 HP.</summary>
        private static void ApplyDeathWard(EntityManager em, Faction faction,
            float3 center, float radius, float healOnExpiry, float duration)
        {
            StampOnAlliedUnits(em, faction, center, radius, (ecb, e) =>
            {
                var ward = new SectDeathWard
                {
                    TimeRemaining = duration,
                    HealOnExpiry  = healOnExpiry,
                };
                if (em.HasComponent<SectDeathWard>(e)) ecb.SetComponent(e, ward);
                else                                   ecb.AddComponent(e, ward);
            });
        }

        // ── Fortitude ───────────────────────────────────────────────────────

        /// <summary>
        /// Stoneveil. Veiled units keep moving — faster — but are invisible,
        /// untargetable and unable to interact with anything. StealthTag does
        /// the invisibility; SectVeiled carries the rest.
        /// </summary>
        private static void ApplyVeil(EntityManager em, Faction faction,
            float3 center, float radius, float damageOnExpiry, float duration)
        {
            // The move bonus is not in the design text as a number; 25% is the
            // same step the game's other speed buffs use, and it is what makes
            // "you may reposition, you may not fight" a real choice.
            const float VeilSpeedBonus = 0.25f;

            StampOnAlliedUnits(em, faction, center, radius, (ecb, e) =>
            {
                var veil = new SectVeiled
                {
                    TimeRemaining  = duration,
                    SpeedBonus     = VeilSpeedBonus,
                    DamageOnExpiry = damageOnExpiry,
                };
                if (em.HasComponent<SectVeiled>(e)) ecb.SetComponent(e, veil);
                else                                ecb.AddComponent(e, veil);

                var stealth = new StealthTag { TimeRemaining = duration };
                if (em.HasComponent<StealthTag>(e)) ecb.SetComponent(e, stealth);
                else                                ecb.AddComponent(e, stealth);

                // The move bonus rides the ordinary speed buff rather than a
                // bespoke path, so the movement stack needs no knowledge of
                // veiling at all.
                var speed = new SpellBuff
                {
                    SpeedMultiplier = 1f + VeilSpeedBonus,
                    TimeRemaining   = duration,
                };
                TheWaningBorder.Systems.Combat.CombatDamageHelper.MergeSpellBuff(em, ecb, e, speed);
            });
        }

        /// <summary>
        /// Bulwark. Allied buildings gain a temporary HP grant; Lv III also
        /// reflects melee damage. The grant is recorded so expiry can take back
        /// exactly what it gave.
        /// </summary>
        private static void ApplyBulwark(EntityManager em, Faction faction,
            float3 center, float radius, float hpFraction, float duration, byte level)
        {
            bool single = IsSingleTarget(radius);
            float reflect = level >= 3 ? 0.20f : 0f;
            float r2 = radius * radius;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                // Friendly-only power: reaches allies too. docs/Design/Teams.md
                if (!Alliances.AreAllied(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
                // Already bulwarked: refreshing would grant a second block of HP
                // on top of the first and never give it back.
                if (em.HasComponent<SectBulwark>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                int granted = (int)(hp.Max * hpFraction);
                hp.Max   += granted;
                hp.Value += granted;
                em.SetComponentData(e, hp);

                ecb.AddComponent(e, new SectBulwark
                {
                    TimeRemaining = duration,
                    GrantedHp     = granted,
                    MeleeReflect  = reflect,
                });

                if (reflect > 0f)
                {
                    var buff = new SpellBuff { DamageReflect = reflect, TimeRemaining = duration };
                    TheWaningBorder.Systems.Combat.CombatDamageHelper.MergeSpellBuff(em, ecb, e, buff);
                }

                if (single) break;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        /// <summary>Immovable III. Allied units take no damage for the duration.</summary>
        private static void ApplyInvulnerable(EntityManager em, Faction faction,
            float3 center, float radius, float duration)
        {
            StampOnAlliedUnits(em, faction, center, radius, (ecb, e) =>
            {
                var inv = new Invulnerable { TimeRemaining = duration };
                if (em.HasComponent<Invulnerable>(e)) ecb.SetComponent(e, inv);
                else                                  ecb.AddComponent(e, inv);
            });
        }

        // ── Reclamation ─────────────────────────────────────────────────────

        /// <summary>
        /// Harvest the Veil. The nearest resource node to the aim point
        /// over-yields on a 5 s tick for the duration. Always single-target —
        /// the escalation is in the yield, not the reach.
        /// </summary>
        private static void ApplyNodeOverYield(EntityManager em, Faction faction,
            float3 center, float radius, float duration, byte level)
        {
            // Generous pick radius: the player aims at a node, and asking them
            // to click within 1.5 m of its centre would be a precision test,
            // not a decision.
            const float PickRadius = 6f;
            float r2 = PickRadius * PickRadius;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            Entity best = Entity.Null;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2 || d2 >= bestD2) continue;
                bestD2 = d2;
                best = e;
            }

            if (best == Entity.Null) return;

            var stamp = new SectNodeOverYield
            {
                TimeRemaining = duration,
                // Pay the first tick immediately rather than after 5 s of
                // nothing — a 30 s power that looks dead for its first sixth
                // reads as broken.
                TickTimer     = 0f,
                Level         = level,
                Beneficiary   = faction,
            };
            if (em.HasComponent<SectNodeOverYield>(best)) em.SetComponentData(best, stamp);
            else                                          em.AddComponentData(best, stamp);
        }

        /// <summary>
        /// Cleanse. Spawns an effect entity that pumps player influence into
        /// the circle for its duration — the curse recedes wherever player
        /// influence wins, so this pushes the border back and claims ground in
        /// one motion. Lv III also heals allies standing in it.
        /// </summary>
        private static void SpawnInfluenceBurst(EntityManager em, Faction faction,
            float3 center, float radius, float perSecond, float duration, byte level)
        {
            var e = em.CreateEntity(
                typeof(SectInfluenceBurst), typeof(LocalTransform), typeof(FactionTag));

            em.SetComponentData(e, new SectInfluenceBurst
            {
                TimeRemaining = duration,
                Radius        = radius,
                PerSecond     = perSecond,
                HealsAllies   = level >= 3,
                Owner         = faction,
            });
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(
                center, quaternion.identity, 1f));
            em.SetComponentData(e, new FactionTag { Value = faction });
        }

        /// <summary>Veil-Touched. Allied units take no curse damage.</summary>
        private static void ApplyCurseWard(EntityManager em, Faction faction,
            float3 center, float radius, float speedBonus, float duration)
        {
            StampOnAlliedUnits(em, faction, center, radius, (ecb, e) =>
            {
                var ward = new SectCurseWard
                {
                    TimeRemaining          = duration,
                    CursedGroundSpeedBonus = speedBonus,
                };
                if (em.HasComponent<SectCurseWard>(e)) ecb.SetComponent(e, ward);
                else                                   ecb.AddComponent(e, ward);
            });
        }

        // ── Shared ──────────────────────────────────────────────────────────

        /// <summary>
        /// Walk allied units inside the circle and let the caller stamp each
        /// one through an ECB. Six of the ten canon effects differ only in
        /// what they stamp; this is that loop, written once.
        /// </summary>
        private static void StampOnAlliedUnits(EntityManager em, Faction faction,
            float3 center, float radius, System.Action<EntityCommandBuffer, Entity> stamp)
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

                stamp(ecb, e);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
