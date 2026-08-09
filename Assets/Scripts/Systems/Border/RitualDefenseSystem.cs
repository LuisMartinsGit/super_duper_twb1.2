// RitualDefenseSystem.cs
// While a ritual is being channeled on a veilstone main node, the node spawns
// defensive border units at increasing intensity (spec §5.1 + §5.5). They
// converge on the ritualist with BorderWaveOrder so they actually march
// to the channel site instead of dawdling at the node.
//
// Intensity ramp:
//   - Spawn interval shrinks linearly from RitualDefenseMaxInterval at
//     progress=0 to RitualDefenseMinInterval at progress=1.
//   - Runai conversion rituals get a 1.6x intensity multiplier (the node
//     "fights enslavement harder than destruction" per spec §5.5). Not yet
//     reachable — only Purification is implemented — but the gate is wired.
//   - Mid-ritual swap to Veilstinger for ~1-in-4 spawns (more dangerous
//     defenders as the channel matures).
//   - Per-ritual hard cap on defender count (RitualDefenseMaxDefenders).
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Spawns defensive border units at every ritual site. Runs every frame
    /// since spawn cadence is per-second; the timer state on
    /// ActiveRitualOnNode gates work, not the system update rate.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PurificationRitualSystem))]
    public partial class RitualDefenseSystem : SystemBase
    {
        private uint _spawnSeed = 1u;

        protected override void OnCreate()
        {
            // §2.5: the curse fields no defenders — ritual-defence waves retired.
            Enabled = TheWaningBorder.Core.Config.BorderConstants.CurseFieldsArmies;
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            // Collect spawn requests during iteration — structural changes
            // (new Crystalling entities, ActiveRitualOnNode mutations) happen
            // after the loop.
            var spawnNodes = new NativeList<Entity>(2, Allocator.Temp);
            var spawnPositions = new NativeList<float3>(2, Allocator.Temp);
            var spawnTargets = new NativeList<float3>(2, Allocator.Temp);
            var spawnIsRanged = new NativeList<byte>(2, Allocator.Temp);

            foreach (var (activeRW, nodeTransform, entity) in SystemAPI
                .Query<RefRW<ActiveRitualOnNode>, RefRO<LocalTransform>>()
                .WithEntityAccess())
            {
                ref var active = ref activeRW.ValueRW;
                if (active.DefendersSpawned >= RitualDefenseMaxDefenders) continue;

                Entity ritualist = active.Ritualist;
                if (!em.Exists(ritualist) || !em.HasComponent<RitualState>(ritualist)) continue;
                var ritual = em.GetComponentData<RitualState>(ritualist);
                if (ritual.TotalDuration <= 0f) continue;

                active.DefenseSpawnTimer -= dt;
                if (active.DefenseSpawnTimer > 0f) continue;

                float progress01 = math.saturate(ritual.Progress / ritual.TotalDuration);
                float interval = math.lerp(RitualDefenseMaxInterval,
                                           RitualDefenseMinInterval,
                                           progress01);
                if (active.Kind == RitualKind.Conversion)
                    interval /= RitualDefenseRunaiIntensity;

                active.DefenseSpawnTimer = interval;
                active.DefendersSpawned += 1;

                var nodePos = nodeTransform.ValueRO.Position;
                var ritualistPos = em.HasComponent<LocalTransform>(ritualist)
                    ? em.GetComponentData<LocalTransform>(ritualist).Position
                    : nodePos;

                // Deterministic per-spawn jitter — seed mixes node entity
                // index, defender count, and a system-local counter so
                // single-player feels organic while multi-step replays stay
                // identical given the same starting tick.
                uint seed = (uint)(entity.Index * 1009 + active.DefendersSpawned * 7919 + _spawnSeed);
                var rng = new Random(math.max(1u, seed));
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float r = RitualDefenseSpawnRadius * rng.NextFloat(0.6f, 1.0f);
                var spawnPos = new float3(
                    nodePos.x + math.cos(angle) * r,
                    nodePos.y,
                    nodePos.z + math.sin(angle) * r);

                // Late-ritual swap-in for Veilstinger (heavier defender).
                bool ranged = progress01 > 0.5f && (rng.NextFloat() < 0.25f);

                spawnNodes.Add(entity);
                spawnPositions.Add(spawnPos);
                spawnTargets.Add(ritualistPos);
                spawnIsRanged.Add(ranged ? (byte)1 : (byte)0);
            }

            _spawnSeed++;

            for (int i = 0; i < spawnNodes.Length; i++)
            {
                Entity unit = spawnIsRanged[i] == 1
                    ? Veilstinger.Create(em, spawnPositions[i], Faction.Border)
                    : Crystalling.Create(em, spawnPositions[i], Faction.Border);

                // Group these defenders with their own node + the DEFEND slot so
                // BorderHordeSystem holds them at the node (and they auto-engage).
                if (em.HasComponent<OwnerNode>(unit))
                    em.SetComponentData(unit, new OwnerNode { Value = spawnNodes[i] });
                else
                    em.AddComponentData(unit, new OwnerNode { Value = spawnNodes[i] });
                if (em.HasComponent<BorderArmyRole>(unit))
                    em.SetComponentData(unit, new BorderArmyRole { Role = BorderArmyRoleType.Defend });
                else
                    em.AddComponentData(unit, new BorderArmyRole { Role = BorderArmyRoleType.Defend });

                // Stamp with a wave order pointed at the ritualist so the
                // AI's marching path applies — units charge the channel site
                // instead of standing around the spawn node.
                if (em.HasComponent<BorderWaveOrder>(unit))
                    em.SetComponentData(unit, new BorderWaveOrder
                    {
                        Target = spawnTargets[i],
                        WaveNumber = -1,  // negative = ritual-defense (not a normal wave)
                    });
                else
                    em.AddComponentData(unit, new BorderWaveOrder
                    {
                        Target = spawnTargets[i],
                        WaveNumber = -1,
                    });
            }

            spawnNodes.Dispose();
            spawnPositions.Dispose();
            spawnTargets.Dispose();
            spawnIsRanged.Dispose();
        }
    }
}
