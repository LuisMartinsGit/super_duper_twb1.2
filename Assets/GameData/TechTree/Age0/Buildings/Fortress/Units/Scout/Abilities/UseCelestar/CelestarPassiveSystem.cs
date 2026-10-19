// CelestarPassiveSystem.cs
// The Celestar passive (docs/Design/Age_1_Alanthor.md § Scouting Celestarii,
// 2026-10-19). Scouting Celestarii — Alanthor-gated on its tech SO — puts the
// "Use Celestar" card on every Scout of the faction (TechEffectSystem for the
// Scouts alive when it lands, Scout.Create for the rest). Since 2026-10-19 the
// card is a PASSIVE: each Scout carrying it, independently and only while it
// is IDLE, reveals a random patch anywhere on the map for the card's Duration,
// then waits the card's Cooldown before the next one. Radius / Duration /
// Cooldown are the UseCelestar ability SO's own fields — nothing here is a
// balance number.
//
// Determinism: everything this reads is replicated sim state (orders, target,
// position, the fixed-step delta), and the patch is drawn from a hash of the
// match seed, the faction, the Scout's own fire count and its position — no
// shared RNG stream, so iteration order cannot fork the peers. The reveal is
// the same timed LineOfSight entity every scrying power uses
// (SectActivePowerHelper.SpawnReveal), so the fog lights it for the faction and
// its team with no special support.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Abilities
{
    /// <summary>Per-Scout state of the Celestar passive. Added by
    /// <see cref="CelestarPassiveSystem"/> to any unit carrying the card.</summary>
    public struct CelestarPassiveState : IComponentData
    {
        /// <summary>Seconds until the next reveal may fire (reveal + cooldown).</summary>
        public float Cooldown;
        /// <summary>Position at the last tick, for the "not moving" test.</summary>
        public float LastX, LastZ;
        /// <summary>Reveals fired so far — varies the hashed patch.</summary>
        public uint Fires;
        /// <summary>0 until the first position sample (a fresh unit is not "still").</summary>
        public byte Sampled;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CelestarPassiveSystem : SystemBase
    {
        private const string CardName = "Use Celestar";

        /// <summary>Below this a step is "standing still" (m). Engine-side
        /// tolerance for float drift, not a tunable.</summary>
        private const float StillEpsilonSq = 0.0004f;

        /// <summary>How often units newly granted the card are looked for (s).
        /// Discovery cadence only — the passive itself ticks every step.</summary>
        private const float DiscoverySeconds = 1f;

        private SimCadence.Periodic _discover;

        static readonly ComponentType[] QT_Armed =
        {
            ComponentType.ReadOnly<UnitAbilities>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadWrite<CelestarPassiveState>(),
        };
        static CachedEntityQuery QC_Armed;

        static readonly ComponentType[] QT_Unarmed =
        {
            ComponentType.ReadOnly<UnitAbilities>(),
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.Exclude<CelestarPassiveState>(),
        };
        static CachedEntityQuery QC_Unarmed;

        protected override void OnUpdate()
        {
            int idx = AbilityCatalog.IndexOf(CardName);
            if (idx < 0) return;
            var card = AbilityCatalog.Get(idx);
            if (card == null || !card.IsPassive) return;

            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            if (_discover.Due(dt, DiscoverySeconds)) Discover(em, idx);

            var q = QC_Armed.Get(em, QT_Armed);
            if (q.IsEmpty) return;

            float radius = card.EffectValue(AbilityEffectKind.RevealFog);
            if (radius <= 0f) radius = card.Radius;
            float duration = card.Duration;
            float cooldown = card.Cooldown;
            if (radius <= 0f || duration <= 0f) return;

            TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);

            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var ua = em.GetComponentData<UnitAbilities>(e);
                if (ua.S0 != idx && ua.S1 != idx && ua.S2 != idx && ua.S3 != idx) continue;

                var st = em.GetComponentData<CelestarPassiveState>(e);
                float3 pos = em.GetComponentData<LocalTransform>(e).Position;

                float dx = pos.x - st.LastX, dz = pos.z - st.LastZ;
                bool still = st.Sampled != 0 && dx * dx + dz * dz <= StillEpsilonSq;
                st.LastX = pos.x; st.LastZ = pos.z; st.Sampled = 1;

                // The cooldown runs whether or not the Scout is idle; only
                // the FIRING waits for idleness.
                if (st.Cooldown > 0f) st.Cooldown = math.max(0f, st.Cooldown - dt);

                if (st.Cooldown <= 0f && still && IsIdle(em, e))
                {
                    var faction = em.GetComponentData<FactionTag>(e).Value;
                    float3 patch = RandomPatch(faction, st.Fires, pos, bMin.x, bMin.y, bMax.x, bMax.y);
                    TheWaningBorder.Systems.Sect.SectActivePowerHelper.SpawnReveal(em, faction, patch, radius, duration);
                    st.Fires++;
                    // "Stays revealed for Duration, THEN the cooldown":
                    // the next reveal is Duration + Cooldown away.
                    st.Cooldown = duration + math.max(0f, cooldown);
                }

                em.SetComponentData(e, st);
            }
        }

        /// <summary>Give the state to every unit that carries the card and
        /// has none yet (granted by research, spawned, or loaded from a save).</summary>
        private static void Discover(EntityManager em, int idx)
        {
            var q = QC_Unarmed.Get(em, QT_Unarmed);
            if (q.IsEmpty) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var uas = q.ToComponentDataArray<UnitAbilities>(Allocator.Temp);
            var add = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var ua = uas[i];
                if (ua.S0 == idx || ua.S1 == idx || ua.S2 == idx || ua.S3 == idx) add.Add(ents[i]);
            }
            for (int i = 0; i < add.Length; i++)
                em.AddComponentData(add[i], new CelestarPassiveState());
            add.Dispose();
        }

        /// <summary>Idle = alive, no move / attack / attack-move / patrol /
        /// queued order, not walking anywhere and not fighting. (Stillness is
        /// tested separately, from the position samples.)</summary>
        private static bool IsIdle(EntityManager em, Entity e)
        {
            if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) return false;
            if (TransientState.Active<MoveCommand>(em, e)
                || TransientState.Active<AttackCommand>(em, e)
                || TransientState.Active<UserMoveOrder>(em, e)
                || TransientState.Active<AttackMoveTag>(em, e)) return false;
            if (em.HasComponent<PatrolTag>(e) || em.HasComponent<PatrolCommand>(e)) return false;
            if (em.HasBuffer<QueuedCommand>(e) && em.GetBuffer<QueuedCommand>(e).Length > 0) return false;
            if (em.HasComponent<DesiredDestination>(e)
                && em.GetComponentData<DesiredDestination>(e).Has != 0) return false;
            if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) return false;
            return true;
        }

        /// <summary>A uniform point in the playable rectangle, hashed from
        /// replicated state only (match seed, faction, fire count, position).</summary>
        private static float3 RandomPatch(Faction faction, uint fires, float3 pos,
            float minX, float minZ, float maxX, float maxZ)
        {
            uint h = math.hash(new uint4(
                (uint)GameSettings.SpawnSeed,
                (uint)faction + 1u,
                fires * 0x9E3779B1u + 0x51A7u,
                math.asuint(pos.x) ^ math.rol(math.asuint(pos.z), 16)));
            var rng = Unity.Mathematics.Random.CreateFromIndex(h);
            float x = rng.NextFloat(minX, math.max(minX + 0.01f, maxX));
            float z = rng.NextFloat(minZ, math.max(minZ + 0.01f, maxZ));
            return new float3(x, 0f, z);
        }
    }
}
