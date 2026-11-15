// Effect bodies for the Alanthor power kinds added 2026-10-15
// (docs/Design/Sects.md §4): Trespass, Revive, Ward Discharge, Bounty,
// Overtime and Plenty. The dispatch switch lives in SectActivePowerSystem.cs;
// the timed state these leave behind is ticked by AlanthorSectPowerSystem.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Sect
{
    public static partial class SectActivePowerHelper
    {
        static readonly ComponentType[] QT_LiveUnits2026 =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_LiveUnits2026;

        static readonly ComponentType[] QT_DeathLog =
        {
            ComponentType.ReadOnly<DeathLogTag>(),
            ComponentType.ReadWrite<RecentDeath>(),
        };
        static CachedEntityQuery QC_DeathLog;

        // ── Antiquity: Trespass ─────────────────────────────────────────────

        /// <summary>Full <paramref name="damage"/> to hostile units standing
        /// in the caster's territory, half to those outside it.</summary>
        private static void ApplyTrespass(EntityManager em, Faction faction, float3 center,
            float radius, float damage, DamageType type)
        {
            float r2 = radius * radius;
            var q = QC_LiveUnits2026.Get(em, QT_LiveUnits2026);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(faction, facs[i].Value)) continue;
                var p = trs[i].Position;
                if (math.distancesq(p.xz, center.xz) > r2) continue;
                bool inside = TerritoryOwnership.OwnerAt(p.x, p.z) == (int)faction;
                SpellDamage.Apply(em, ents[i], (int)math.round(inside ? damage : damage * 0.5f), type, faction);
            }
        }

        // ── Renewal: Revive ─────────────────────────────────────────────────

        /// <summary>Bring back up to <paramref name="most"/> of the caster's
        /// units that died in the circle in the last <paramref name="window"/>
        /// seconds, at <paramref name="hpFraction"/> of max HP.</summary>
        private static void ApplyRevive(EntityManager em, Faction faction, float3 center,
            float radius, float hpFraction, float window, float most)
        {
            var q = QC_DeathLog.Get(em, QT_DeathLog);
            if (q.IsEmpty) return;
            var logEntity = q.GetSingletonEntity();
            double now = AlanthorSectPowerSystem.Now;
            float r2 = radius * radius;

            // Copy the matches out first: creating units is a structural change.
            var picks = new NativeList<RecentDeath>(8, Allocator.Temp);
            var log = em.GetBuffer<RecentDeath>(logEntity);
            for (int i = log.Length - 1; i >= 0 && picks.Length < (int)most; i--)
            {
                var d = log[i];
                if (d.Faction != faction || now - d.Time > window) continue;
                if (math.distancesq(d.Position.xz, center.xz) > r2) continue;
                picks.Add(d);
                log.RemoveAt(i);
            }

            for (int i = 0; i < picks.Length; i++)
            {
                var unit = UnitFactory.Create(em, picks[i].UnitId.ToString(), picks[i].Position, faction);
                if (unit == Entity.Null || !em.HasComponent<Health>(unit)) continue;
                var hp = em.GetComponentData<Health>(unit);
                hp.Value = math.max(1, (int)math.round(hp.Max * hpFraction));
                em.SetComponentData(unit, hp);
            }
            picks.Dispose();
        }

        /// <summary>The Great Rewind's door to the same log (Heroes.md §3).</summary>
        public static void ReviveFallen(EntityManager em, Faction faction, float3 center,
            float radius, float hpFraction, float window, float most)
            => ApplyRevive(em, faction, center, radius, hpFraction, window, most);

        // ── Fortitude: Ward Discharge ───────────────────────────────────────

        /// <summary>Arm the caster's structure nearest the cast point.</summary>
        private static void ApplyWardCharge(EntityManager em, Faction faction, float3 center,
            float triggerRadius, float damagePerPulse, float interval, float pulses)
        {
            Entity best = Entity.Null;
            float bestD = SectLeverEffects.WardPickRadius * SectLeverEffects.WardPickRadius;
            var q = QC_BuildingTagHealthLocalTransformFactionTag.Get(em, QT_BuildingTagHealthLocalTransformFactionTag);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction || hps[i].Value <= 0) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                float d = math.distancesq(trs[i].Position.xz, center.xz);
                if (d < bestD) { bestD = d; best = ents[i]; }
            }
            if (best == Entity.Null) return;
            TransientState.Set(em, best, new WardDischargeCharge
            {
                Damage = damagePerPulse,
                TriggerRadius = triggerRadius,
                Interval = math.max(0.25f, interval),
                PulsesLeft = math.max(1, (int)pulses),
                Lifetime = SectLeverEffects.WardChargeLifetime,
                Timer = 0f,
                Firing = 0,
            });
        }

        // ── Prosperity: Bounty / Overtime / Plenty ──────────────────────────

        /// <summary>Strike the hostile unit nearest the cast point and mark it:
        /// if it dies inside <paramref name="window"/>, its cost is paid.</summary>
        /// <summary>Bounty (decision 59): every hostile unit in the circle takes
        /// 40% of its maximum health and is marked; a marked unit that dies
        /// within 10 s pays the caster half its cost.</summary>
        private static void ApplyBounty(EntityManager em, Faction faction, float3 center, float radius)
            => ProsperityBounty.StrikeInRadius(em, faction, center, math.max(radius, 1f),
                ProsperityBounty.StrikeShare);

        /// <summary>Overtime (decision 47): every one of the caster's buildings in
        /// the circle works faster.</summary>
        private static void ApplyOvertime(EntityManager em, Faction faction, float3 center,
            float radius, float multiplier, float seconds)
        {
            float r = math.max(radius, SectRadii.Small);
            float r2 = r * r;
            var q = QC_BuildingTagHealthLocalTransformFactionTag.Get(em, QT_BuildingTagHealthLocalTransformFactionTag);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (math.distancesq(trs[i].Position.xz, center.xz) > r2) continue;
                TransientState.Set(em, ents[i], new OvertimeBoost { Multiplier = multiplier, TimeRemaining = seconds });
            }
        }

        /// <summary>Plenty (decision 47): raise a Counting House of the cast's
        /// level at the aimed point (CountingHouse snaps and validates it).</summary>
        private static void ApplyPlenty(EntityManager em, Faction faction, float3 center, int level)
            => TheWaningBorder.Entities.CountingHouse.Spawn(em, faction, center, math.clamp(level, 1, 3));

        /// <summary>The live unit nearest <paramref name="center"/> within
        /// <paramref name="radius"/>, hostile to (or owned by) the caster.</summary>
        private static Entity NearestUnit(EntityManager em, float3 center, float radius,
            Faction faction, bool hostile)
        {
            Entity best = Entity.Null;
            float bestD = radius * radius;
            var q = QC_LiveUnits2026.Get(em, QT_LiveUnits2026);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var trs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                bool match = hostile ? Alliances.AreHostile(faction, facs[i].Value) : facs[i].Value == faction;
                if (!match) continue;
                float d = math.distancesq(trs[i].Position.xz, center.xz);
                if (d < bestD) { bestD = d; best = ents[i]; }
            }
            return best;
        }

        /// <summary>Powers that take no target point (cast at once). None since
        /// Plenty raises a Counting House at an aimed point (decision 47).</summary>
        public static bool IsUntargeted(SectActivePowerKind kind) => false;
    }
}
