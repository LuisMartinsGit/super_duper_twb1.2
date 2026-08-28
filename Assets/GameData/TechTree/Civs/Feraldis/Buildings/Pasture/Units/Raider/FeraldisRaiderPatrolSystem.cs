// Drives UNCONTROLLABLE Feraldis raiders (House-spawned Raiders and Raider
// Camp Plunderers) toward the nearest enemy every RetargetInterval seconds.
//
// The NotControllableTag half of the filter is load-bearing: since the
// 2026-08-05 rev.2 pass the Pasture trains a PLAYER-CONTROLLED Raider that
// also carries FeraldisRaiderTag. Without this gate the system would
// overwrite that unit's DesiredDestination and Target every 1.5 s and yank
// it off whatever the player ordered.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisRaiderPatrolSystem : SystemBase
    {
        private const float RetargetInterval = 1.5f;
        private const float MaxSearchRadiusSq = 200f * 200f;

        private double _lastRetargetTime;

        protected override void OnCreate()
        {
            RequireForUpdate<FeraldisRaiderTag>();
            _lastRetargetTime = 0;
        }

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (now - _lastRetargetTime < RetargetInterval) return;
            _lastRetargetTime = now;

            var em = EntityManager;

            // Snapshot all faction-tagged entities with health — Raiders consider
            // both units and buildings as valid targets per design §5.3.
            var enemyQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());

            using var enemyEnts = enemyQuery.ToEntityArray(Allocator.Temp);
            using var enemyFactions = enemyQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var enemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var enemyHealth = enemyQuery.ToComponentDataArray<Health>(Allocator.Temp);

            // Writes are staged and applied AFTER the loop. Adding
            // DesiredDestination to a raider that lacks one is a structural
            // change, and doing that inside a SystemAPI.Query foreach
            // invalidates the enumerator's chunks — it throws on the next
            // access with collections checks on. Raider Camps make this a
            // certainty rather than a rarity: a fresh Plunderer arrives every
            // few seconds and every one of them hit the add path.
            var writeEnts = new NativeList<Entity>(Allocator.Temp);
            var writeDest = new NativeList<float3>(Allocator.Temp);
            var writeTarget = new NativeList<Entity>(Allocator.Temp);

            foreach (var (transform, factionTag, raiderEntity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<FeraldisRaiderTag, NotControllableTag>()
                .WithEntityAccess())
            {
                Faction self = factionTag.ValueRO.Value;
                float3 myPos = transform.ValueRO.Position;

                Entity bestTarget = Entity.Null;
                float bestDistSq = MaxSearchRadiusSq;
                float3 bestPos = float3.zero;

                for (int i = 0; i < enemyEnts.Length; i++)
                {
                    if (enemyFactions[i].Value == self) continue;
                    if (enemyHealth[i].Value <= 0) continue;

                    // RAID PLAYERS, NEVER THE CURSE (2026-08-05 match post-
                    // mortem). Faction.Border owns the wells, the crust and
                    // every curse creature, and it was almost always the
                    // NEAREST thing — so raiders marched into the curse,
                    // died there, and their blood fed BloodCurseSpawnSystem,
                    // which then killed their owner. The entire Feraldis
                    // economy was feeding the thing that was killing it.
                    if (enemyFactions[i].Value == Faction.Border) continue;

                    // Don't chase a target standing on cursed ground either —
                    // exposure would kill the raider before it ever landed a
                    // hit, and the corpse would feed the spawner again.
                    if (OnCursedGround(enemyTransforms[i].Position)) continue;

                    float3 d = enemyTransforms[i].Position - myPos;
                    float distSq = math.lengthsq(d);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestTarget = enemyEnts[i];
                        bestPos = enemyTransforms[i].Position;
                    }
                }

                if (bestTarget == Entity.Null) continue;

                writeEnts.Add(raiderEntity);
                writeDest.Add(bestPos);
                writeTarget.Add(bestTarget);
            }

            for (int i = 0; i < writeEnts.Length; i++)
            {
                var e = writeEnts[i];
                var dest = new DesiredDestination { Position = writeDest[i], Has = 1 };
                if (em.HasComponent<DesiredDestination>(e)) em.SetComponentData(e, dest);
                else em.AddComponentData(e, dest);

                if (em.HasComponent<Target>(e))
                    em.SetComponentData(e, new Target { Value = writeTarget[i] });
            }

            writeEnts.Dispose();
            writeDest.Dispose();
            writeTarget.Dispose();
            enemyQuery.Dispose();
        }

        /// <summary>
        /// True where the curse crust is thick enough to hurt. Raiders are
        /// 45 HP tax collectors — walking into exposure is certain death and
        /// a free meal for the blood-curse spawner. The nav layer already
        /// makes crust expensive to path THROUGH; this stops them choosing a
        /// destination inside it in the first place.
        /// </summary>
        private bool OnCursedGround(float3 pos)
        {
            if (!SystemAPI.HasSingleton<VeilField>()) return false;
            var field = SystemAPI.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return false;
            return field.SaturationAt(pos) >= VeilField.CrustThreshold;
        }
    }
}
