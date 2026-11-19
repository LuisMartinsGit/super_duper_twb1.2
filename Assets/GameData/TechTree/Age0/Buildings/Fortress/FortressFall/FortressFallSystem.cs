// FortressFallSystem.cs
// THE ONE FORTRESS IS HARD TO BRING DOWN (decision 69, 2026-11-17; developer:
// "a fortress must be nearly unkillable unless there is a considerable force
// attacking it (and it must have siege)").
//
// ScaleHit, called from CombatDamageHelper.ScaleVsBuilding and the projectile
// splash, turns a hit on a finished Fortress into what lands: a SIEGE hit
// lands siegeShare of its damage (every engine's hit, so the damage scales
// with the siege brought); an arrow or bolt lands rangedShare; any other hit
// (melee, magic, the curse's blows) lands meleeShare — the Age of Empires IV
// model: arrows barely scratch stone, torches burn it slowly, siege razes it. The leftover fraction of a point is carried on
// the building (BuildingDamageState.ChipCarry), as for every chipped building.
//
// It also clears keystone rubble (KeystoneRubble.cs: Fortress, Temple,
// landmark) once that building stands again, or once the owner is out.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FortressFallSystem : SystemBase
    {
        private SimCadence.Periodic _acc;

        static readonly ComponentType[] QT_Rubble = { ComponentType.ReadOnly<KeystoneRubble>() };
        static CachedEntityQuery QC_Rubble;

        static readonly ComponentType[] QT_Verdict = { ComponentType.ReadOnly<MatchVerdictState>() };
        static CachedEntityQuery QC_Verdict;

        protected override void OnUpdate()
        {
            var cfg = FortressFallSystemConfig.I;
            if (cfg == null) return;   // Require already logged the data bug
            var lockstep = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            if (lockstep != null && !lockstep.IsSimulationRunning) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, math.max(0.1f, cfg.scanIntervalSeconds))) return;

            var em = EntityManager;

            // ── Rubble: gone once its keystone stands again (finished) or the
            // owner is out ──
            var rq = QC_Rubble.Get(em, QT_Rubble);
            if (rq.IsEmptyIgnoreFilter) return;
            using var rubble = rq.ToEntityArray(Allocator.Temp);
            using var rData = rq.ToComponentDataArray<KeystoneRubble>(Allocator.Temp);
            var vq = QC_Verdict.Get(em, QT_Verdict);
            Entity verdict = Entity.Null;
            if (!vq.IsEmptyIgnoreFilter)
            {
                using var ve = vq.ToEntityArray(Allocator.Temp);
                verdict = ve[0];
            }
            for (int i = 0; i < rubble.Length; i++)
            {
                var owner = rData[i].Owner;
                bool standing = TheWaningBorder.Entities.KeystoneRubbles.Has(
                    em, owner, rData[i].BuildingId.ToString(), finishedOnly: true);
                bool out_ = false;
                if (!standing && verdict != Entity.Null && em.HasBuffer<EliminatedFactionRecord>(verdict))
                {
                    var buf = em.GetBuffer<EliminatedFactionRecord>(verdict);
                    for (int b = 0; b < buf.Length; b++)
                        if (buf[b].Value == owner) { out_ = true; break; }
                }
                if (standing || out_) em.DestroyEntity(rubble[i]);
            }
        }

        /// <summary>
        /// The damage that lands when <paramref name="damage"/> of
        /// <paramref name="type"/> hits a finished Fortress (see the file
        /// header). Anything that is not a finished Fortress returns
        /// <paramref name="damage"/> unchanged; <paramref name="handled"/> says
        /// which.
        /// </summary>
        public static int ScaleHit(EntityManager em, Entity target, int damage, DamageType type, out bool handled)
        {
            handled = false;
            if (damage <= 0 || target == Entity.Null || !em.Exists(target)) return damage;
            if (!em.HasComponent<HallTag>(target) || em.HasComponent<UnderConstruction>(target)) return damage;
            var cfg = FortressFallSystemConfig.I;
            if (cfg == null) return damage;
            handled = true;

            float share = type == DamageType.Siege ? cfg.siegeShare
                        : type == DamageType.Ranged ? cfg.rangedShare
                        : cfg.meleeShare;

            bool has = em.HasComponent<BuildingDamageState>(target);
            var st = has ? em.GetComponentData<BuildingDamageState>(target) : default;
            float total = damage * share + st.ChipCarry;
            int whole = (int)total;
            if (has)
            {
                st.ChipCarry = total - whole;
                em.SetComponentData(target, st);
            }
            return whole;
        }
    }
}
