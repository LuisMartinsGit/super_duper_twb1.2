// SectActivePowerSystem.Targeted.cs
// Single-target sect powers — docs/Design/Spells.md §2 (the per-power target
// table) and §6 (how they are cast).
//
// A power whose SectActivePowerSpec.Target is not None is cast ON an entity
// the player clicked. Fire checks the pick with CheckTarget before the
// cooldown is charged; the strike carries the entity through its wind-up;
// at landing SectActivePowerSystem re-checks it and hands it to LandOnTarget
// here — or, if it is gone or no longer legal, refunds the cooldown.
//
// The radius-pick bodies (ApplyBuildingShutdown, ApplyBounty, ...) stay for
// the one caller that still fires these powers at a bare point: the ability
// showcase (FireUnchecked). A player or AI cast never reaches them.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    public static partial class SectActivePowerHelper
    {
        static readonly ComponentType[] QT_TargetCandidates =
        {
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_TargetCandidates;

        /// <summary>
        /// May a power of <paramref name="kind"/> be cast on
        /// <paramref name="target"/>? The generic kind / side test
        /// (CastTargetRules) plus each power's own rule. Asked by the Religion
        /// panel while the player aims, by Fire before the cooldown, and at
        /// landing.
        /// </summary>
        public static bool CheckTarget(EntityManager em, Faction faction, SectActivePowerKind kind,
            CastTargetKind targetKind, Entity target, out CastTargetRefusal why)
        {
            if (!CastTargetRules.Check(em, targetKind, faction, target, out why)) return false;
            if (targetKind == CastTargetKind.None) return true;

            bool building = em.HasComponent<BuildingTag>(target);
            bool curse = em.GetComponentData<FactionTag>(target).Value == Faction.Border;
            switch (kind)
            {
                // Curse structures are verb objectives, never sect targets
                // (the area bodies skip Faction.Border for the same reason).
                case SectActivePowerKind.BuildingShutdown:
                    if (curse) { why = CastTargetRefusal.Curse; return false; }
                    break;
                // Walls belong to siege alone (Combat_Pacing.md).
                case SectActivePowerKind.UnmakeBuilding:
                case SectActivePowerKind.SmiteCircle:
                    if (building && curse) { why = CastTargetRefusal.Curse; return false; }
                    if (building && CastTargetRules.IsWall(em, target)) { why = CastTargetRefusal.Wall; return false; }
                    break;
                // A second Emergency Repairs would grant a second block of HP
                // and never take it back.
                case SectActivePowerKind.BuildingHpBuff:
                    if (TransientState.Active<SectBulwark>(em, target))
                    { why = CastTargetRefusal.AlreadyAffected; return false; }
                    break;
                case SectActivePowerKind.WardCharge:
                    if (em.HasComponent<UnderConstruction>(target))
                    { why = CastTargetRefusal.Unfinished; return false; }
                    break;
                case SectActivePowerKind.TrainingBoon:
                    if (!em.HasComponent<ProductionState>(target))
                    { why = CastTargetRefusal.NoProduction; return false; }
                    break;
                case SectActivePowerKind.SpyNetwork:
                    if (em.HasComponent<WitnessSpy>(target))
                    { why = CastTargetRefusal.AlreadyAffected; return false; }
                    break;
            }
            return true;
        }

        // ── Line of sight (docs/Design/Spells.md §6.1, 2026-10-19) ──────────

        /// <summary>
        /// Does a cast of <paramref name="kind"/> need its aim point in the
        /// caster's sight? Every aimed power does, except the three whose aim
        /// point is not where they act: Plenty has no target, Nowhere to Hide
        /// (RevealedStrike) is map-wide, and a reveal exists to look INTO the
        /// fog, so gating it on sight would make it pointless.
        /// </summary>
        public static bool NeedsSight(SectActivePowerKind kind) => kind switch
        {
            SectActivePowerKind.Plenty         => false,
            SectActivePowerKind.RevealedStrike => false,
            SectActivePowerKind.RevealCircle   => false,
            SectActivePowerKind.None           => false,
            _                                  => true,
        };

        /// <summary>
        /// An area / ground power aimed at <paramref name="pos"/>: true when
        /// the power needs no sight, or when <paramref name="faction"/> (or an
        /// ally) sees the point right now. The simulation's test
        /// (FactionSight), so Fire, the AI and the aiming ring agree.
        /// </summary>
        public static bool AimInSight(EntityManager em, Faction faction, SectActivePowerKind kind, float3 pos,
            bool fresh = false)
            => !NeedsSight(kind) || TheWaningBorder.World.FogOfWar.FactionSight.CanSee(em, faction, pos, fresh);

        /// <summary>
        /// Is <paramref name="target"/> in <paramref name="faction"/>'s sight?
        /// Its own and its allies' units and buildings always are; anything
        /// else must stand inside a friendly line-of-sight circle now.
        /// </summary>
        public static bool TargetInSight(EntityManager em, Faction faction, Entity target, bool fresh = false)
        {
            if (target == Entity.Null || !em.Exists(target)) return false;
            if (em.HasComponent<FactionTag>(target)
                && Alliances.AreAllied(faction, em.GetComponentData<FactionTag>(target).Value))
                return true;
            if (!em.HasComponent<LocalTransform>(target)) return false;
            return TheWaningBorder.World.FogOfWar.FactionSight.CanSee(
                em, faction, em.GetComponentData<LocalTransform>(target).Position, fresh);
        }

        /// <summary>
        /// <see cref="CheckTarget(EntityManager, Faction, SectActivePowerKind, CastTargetKind, Entity, out CastTargetRefusal)"/>
        /// plus the sight rule — what a CAST must pass (Fire, the aiming UI,
        /// the AI's pick). The landing re-check is plain CheckTarget: a target
        /// that walks into the fog during the wind-up is still struck.
        /// </summary>
        public static bool CheckTargetInSight(EntityManager em, Faction faction, SectActivePowerKind kind,
            CastTargetKind targetKind, Entity target, out CastTargetRefusal why, bool fresh = false)
        {
            if (!CheckTarget(em, faction, kind, targetKind, target, out why)) return false;
            if (targetKind == CastTargetKind.None) return true;
            if (NeedsSight(kind) && !TargetInSight(em, faction, target, fresh))
            { why = CastTargetRefusal.NotInSight; return false; }
            return true;
        }

        /// <summary>The spec overload of <see cref="CheckTarget(EntityManager, Faction, SectActivePowerKind, CastTargetKind, Entity, out CastTargetRefusal)"/>.</summary>
        public static bool CheckTarget(EntityManager em, Faction faction, in SectActivePowerSpec spec,
            Entity target, out CastTargetRefusal why)
            => CheckTarget(em, faction, spec.Kind, spec.Target, target, out why);

        /// <summary>
        /// The legal target nearest <paramref name="pos"/> within
        /// <paramref name="radius"/>, for the AI, which chooses a SPOT and then
        /// casts on the entity standing there. Entity.Null when there is none.
        /// Host-side: the result rides the command, so it need not be
        /// reproducible on other peers.
        /// </summary>
        public static Entity PickTargetNear(EntityManager em, Faction faction, in SectActivePowerSpec spec,
            float3 pos, float radius)
        {
            if (spec.Target == CastTargetKind.None) return Entity.Null;
            var q = QC_TargetCandidates.Get(em, QT_TargetCandidates);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = radius * radius;
            for (int i = 0; i < ents.Length; i++)
            {
                float d = math.distancesq(trs[i].Position.xz, pos.xz);
                if (d > bestD) continue;
                // Only what the caster can see: Fire would refuse the rest.
                if (!CheckTargetInSight(em, faction, spec.Kind, spec.Target, ents[i], out _)) continue;
                bestD = d;
                best = ents[i];
            }
            return best;
        }

        /// <summary>
        /// Land an entity-targeted power on <paramref name="target"/> (already
        /// re-checked). False for a kind that has no entity body, so the caller
        /// falls back to the area dispatch.
        /// </summary>
        internal static bool LandOnTarget(EntityManager em, Faction faction, SectActivePowerKind kind,
            Entity target, float radius, float magnitude, float duration, byte level, float secondary,
            DamageType dmgType)
        {
            switch (kind)
            {
                case SectActivePowerKind.BuildingShutdown:
                    TransientState.Set(em, target, new SectShutdown { TimeRemaining = duration });
                    return true;

                case SectActivePowerKind.BuildingHpBuff:
                    BulwarkOne(em, target, magnitude, duration, level);
                    return true;

                case SectActivePowerKind.Bounty:
                    TransientState.Set(em, target, new BountyMarked { ByFaction = faction, TimeRemaining = duration });
                    SpellDamage.Apply(em, target, (int)math.round(magnitude), dmgType, faction);
                    return true;

                case SectActivePowerKind.Overtime:
                    TransientState.Set(em, target, new OvertimeBoost { Multiplier = magnitude, TimeRemaining = duration });
                    return true;

                case SectActivePowerKind.WardCharge:
                    // Radius is the TRIGGER range of the armed structure.
                    TransientState.Set(em, target, new WardDischargeCharge
                    {
                        Damage = magnitude,
                        TriggerRadius = radius,
                        Interval = math.max(0.25f, duration),
                        PulsesLeft = math.max(1, (int)secondary),
                        Lifetime = SectLeverEffects.WardChargeLifetime,
                        Timer = 0f,
                        Firing = 0,
                    });
                    return true;

                case SectActivePowerKind.SpyNetwork:
                    PlantSpy(em, faction, target, duration > 0f ? duration : SectEffectDuration.Permanent, level);
                    return true;

                case SectActivePowerKind.TrainingBoon:
                {
                    var boon = new SectTrainingBoon
                    {
                        TimeRemaining   = duration,
                        CostMultiplier  = magnitude,
                        SpeedMultiplier = level >= 3 ? SectLeverEffects.CallToArmsSpeedLv3 : 1f,
                    };
                    if (em.HasComponent<SectTrainingBoon>(target)) em.SetComponentData(target, boon);
                    else em.AddComponentData(target, boon);
                    return true;
                }

                case SectActivePowerKind.UnmakeBuilding:
                    UnmakeOne(em, faction, target, magnitude, level, dmgType);
                    return true;

                case SectActivePowerKind.SmiteCircle:
                    SpellDamage.Apply(em, target, (int)magnitude, dmgType, faction);
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Emergency Repairs on one building: a temporary HP grant, recorded so
        /// expiry takes back exactly what it gave; level III also reflects melee
        /// damage. The per-building half of ApplyBulwark (its area twin).
        /// </summary>
        private static void BulwarkOne(EntityManager em, Entity e, float hpFraction, float duration, byte level)
        {
            if (!em.HasComponent<Health>(e)) return;
            var hp = em.GetComponentData<Health>(e);
            if (hp.Value <= 0) return;
            float reflect = level >= 3 ? 0.20f : 0f;
            int granted = (int)(hp.Max * hpFraction);
            hp.Max   += granted;
            hp.Value += granted;
            em.SetComponentData(e, hp);

            TransientState.Set(em, e, new SectBulwark
            {
                TimeRemaining = duration,
                GrantedHp     = granted,
                MeleeReflect  = reflect,
            });

            if (reflect > 0f)
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                var buff = new SpellBuff { DamageReflect = reflect, TimeRemaining = duration };
                CombatDamageHelper.MergeSpellBuff(em, ecb, e, buff);
                ecb.Playback(em);
                ecb.Dispose();
            }
        }

        /// <summary>
        /// Give back a power's cooldown: its target was gone (or no longer
        /// legal) when the wind-up ended, so nothing landed.
        /// </summary>
        internal static void RefundCooldown(EntityManager em, Faction faction, byte sectIndex, byte slot)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;
            if (!em.HasBuffer<SectActivePowerCooldown>(bank)) return;
            var buf = em.GetBuffer<SectActivePowerCooldown>(bank);
            int tier = slot == 0 ? 1 : slot;
            for (int i = buf.Length - 1; i >= 0; i--)
            {
                int entryTier = buf[i].Tier == 0 ? 1 : buf[i].Tier;
                if (buf[i].SectIndex == sectIndex && entryTier == tier) buf.RemoveAtSwapBack(i);
            }
        }
    }
}
