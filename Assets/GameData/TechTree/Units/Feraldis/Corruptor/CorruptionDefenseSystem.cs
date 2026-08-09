// File: Assets/GameData/TechTree/Units/Feraldis/Corruptor/CorruptionDefenseSystem.cs
// The curse fights back while a well is cracked open.
// Canon: docs/Design/Age_1_Feraldis.md § Corruptor.
//
// This is a deliberate sibling of the retired RitualDefenseSystem rather
// than a revival of it: that system is switched off behind
// BorderConstants.CurseFieldsArmies (false), together with the whole
// BorderWaveOrder marching stack. Flipping that flag would wake several
// other dormant systems at once. So this spawns its own defenders and lets
// them behave as ordinary Faction.Border hostiles under normal target
// acquisition — exactly how BloodCurseSpawnSystem's creatures already work.
//
// COMPOSITION: Crystallings, with Veilstingers mixed in once the window is
// past halfway. NO GODSPLINTERS, ever. A Godsplinter is magic-siege-tank
// class even after its nerf (420 HP / 34 dmg / 26 range); adding them to a
// wave that must be survived WHILE killing a 4000 HP well made the whole
// objective unwinnable, which is the note the design calls out explicitly.
//
// Determinism: no RNG object — the roll is arithmetic on the node index and
// the defender counter, so lockstep clients spawn identical waves.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CorruptionRitualSystem))]
    public partial class CorruptionDefenseSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<WellCorrupted>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            var spawnAt = new NativeList<float3>(Allocator.Temp);
            var spawnRanged = new NativeList<byte>(Allocator.Temp);

            foreach (var (corrupt, xf) in SystemAPI
                .Query<RefRW<WellCorrupted>, RefRO<LocalTransform>>())
            {
                ref var wc = ref corrupt.ValueRW;
                if (wc.DefendersSpawned >= CorruptionMaxDefenders) continue;

                wc.WaveTimer -= dt;
                if (wc.WaveTimer > 0f) continue;

                // Pressure ramps as the window runs down.
                float elapsed01 = wc.TotalSeconds > 0f
                    ? math.saturate(1f - wc.Remaining / wc.TotalSeconds)
                    : 0f;
                wc.WaveTimer = math.lerp(
                    CorruptionWaveMaxInterval, CorruptionWaveMinInterval, elapsed01);

                var c = xf.ValueRO.Position;
                for (int i = 0; i < CorruptionWaveBurst; i++)
                {
                    if (wc.DefendersSpawned >= CorruptionMaxDefenders) break;
                    int n = wc.DefendersSpawned;
                    wc.DefendersSpawned = n + 1;

                    // Deterministic ring placement + ranged roll.
                    float ang = ((n * 47 + i * 113) % 360) * math.PI / 180f;
                    float rad = CorruptionSpawnRadius * (0.7f + ((n * 31) % 30) / 100f);
                    float x = c.x + math.cos(ang) * rad;
                    float z = c.z + math.sin(ang) * rad;

                    bool ranged = elapsed01 > 0.5f
                        && ((n * 17) % 100) < (int)(CorruptionVeilstingerChance * 100f);

                    spawnAt.Add(new float3(x, TerrainUtility.GetHeight(x, z), z));
                    spawnRanged.Add(ranged ? (byte)1 : (byte)0);
                }
            }

            // Post-loop: entity creation is a structural change.
            for (int i = 0; i < spawnAt.Length; i++)
            {
                if (spawnRanged[i] != 0) Veilstinger.Create(em, spawnAt[i], Faction.Border);
                else Crystalling.Create(em, spawnAt[i], Faction.Border);
            }

            spawnAt.Dispose();
            spawnRanged.Dispose();
        }
    }
}
