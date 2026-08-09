// RitualBacklashSystem.cs
// THE BACKLASH — the price of a failed rite. Canon §2.9.
//
// A ritual that STARTS and does not FINISH wakes the well's fury: five
// escalating waves of crystal creatures erupt from it, 10 units growing to
// 50. The verbs decide the match and sit behind Temple L3/L4, so reaching for
// one is a commitment — this is what makes it a commitment rather than a free
// retry.
//
// Design notes that are load-bearing:
//   * Only a broken CHANNEL arms it. An approach cancelled before channelling
//     (well already claimed / cracked / destroyed) costs nothing — you are
//     punished for a rite you began, not for finding the door taken.
//   * Godsplinters are rationed: none before wave 3, 3 at the very most. One
//     is a magic siege tank (420 HP / 34 dmg / 26 range / 5 m AoE); a wave of
//     them is a table-flip, not a fight. CorruptionDefenseSystem bans them
//     outright for the same reason — the Backlash is a punishment, so it gets
//     them, sparingly.
//   * The creatures are ordinary Faction.Border hostiles under normal target
//     acquisition, exactly like blight-pocket eruptions and corruption
//     defenders. No curse brain, no CurseFieldsArmies dependency.
//   * Determinism: no RNG object. Placement and composition are arithmetic on
//     the wave index and the spawn counter, so lockstep peers erupt
//     identically.
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;      // Crystalling / Veilstinger / Godsplinter factories
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// On a well that is erupting after a failed rite. Removed when the fifth
    /// wave has been spawned.
    /// </summary>
    public struct RitualBacklash : IComponentData
    {
        /// <summary>Waves already spawned, 0..RitualBacklashTuning.WaveCount.</summary>
        public int WavesDone;

        /// <summary>Seconds until the next wave erupts.</summary>
        public float NextWaveTimer;

        /// <summary>Faction whose rite failed here. Logging / attribution only —
        /// the creatures are hostile to everyone, including them.</summary>
        public Faction Provoker;
    }

    public static class RitualBacklashTuning
    {
        public const int WaveCount = 5;

        /// <summary>Units in wave N (1-based): 10, 20, 30, 40, 50.</summary>
        public static int UnitsInWave(int wave) => 10 * math.clamp(wave, 1, WaveCount);

        /// <summary>
        /// Godsplinters in wave N. None until wave 3, then 1 / 2 / 3 — the
        /// "go easy on the Godsplinters" cap from the design.
        /// </summary>
        public static int GodsplintersInWave(int wave) =>
            wave <= 2 ? 0 : math.min(wave - 2, MaxGodsplintersPerWave);

        public const int MaxGodsplintersPerWave = 3;

        /// <summary>
        /// Fraction of the non-Godsplinter body that is ranged (Veilstingers).
        /// Climbs with the wave so later waves out-range the defence instead of
        /// just out-massing it.
        /// </summary>
        public static float RangedFraction(int wave) =>
            wave <= 1 ? 0f : math.min(0.25f + (wave - 2) * 0.09f, 0.40f);

        /// <summary>Seconds before the first wave — a beat of warning between
        /// the rite collapsing and the ground opening.</summary>
        public const float FirstWaveDelay = 6f;

        /// <summary>Seconds between waves. Long enough to fight one wave and
        /// short enough that the five arrive as one escalating assault rather
        /// than five unrelated raids.</summary>
        public const float WaveInterval = 26f;

        /// <summary>Ring radius the creatures erupt on, around the well.</summary>
        public const float SpawnRadius = 11f;
    }

    /// <summary>
    /// Ticks every erupting well and spawns its waves. Managed SystemBase
    /// because entity creation through the creature factories is a structural
    /// change.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RitualBacklashSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<RitualBacklash>();

        /// <summary>
        /// Arm (or re-arm) the Backlash on a well whose channel just broke.
        /// Called by all three verb systems. A well already erupting restarts
        /// the sequence at wave 1 rather than stacking a second series.
        /// </summary>
        public static void Arm(EntityManager em, Entity node, Faction provoker)
        {
            if (node == Entity.Null || !em.Exists(node)) return;

            var backlash = new RitualBacklash
            {
                WavesDone = 0,
                NextWaveTimer = RitualBacklashTuning.FirstWaveDelay,
                Provoker = provoker,
            };
            if (em.HasComponent<RitualBacklash>(node))
                em.SetComponentData(node, backlash);
            else
                em.AddComponentData(node, backlash);

            TheWaningBorder.AI.AILogger.Log(provoker, "RITUAL",
                $"BACKLASH armed — the rite failed and the well answers with " +
                $"{RitualBacklashTuning.WaveCount} waves");
            TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                "The rite collapses — the well erupts!");
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            var spawnPos = new NativeList<float3>(Allocator.Temp);
            var spawnKind = new NativeList<byte>(Allocator.Temp);   // 0 melee 1 ranged 2 siege
            var finished = new NativeList<Entity>(Allocator.Temp);

            foreach (var (backlashRW, xf, entity) in SystemAPI
                .Query<RefRW<RitualBacklash>, RefRO<LocalTransform>>()
                .WithEntityAccess())
            {
                ref var b = ref backlashRW.ValueRW;

                b.NextWaveTimer -= dt;
                if (b.NextWaveTimer > 0f) continue;

                int wave = b.WavesDone + 1;
                b.WavesDone = wave;
                b.NextWaveTimer = RitualBacklashTuning.WaveInterval;

                int total = RitualBacklashTuning.UnitsInWave(wave);
                int siege = RitualBacklashTuning.GodsplintersInWave(wave);
                int body = math.max(0, total - siege);
                int ranged = (int)math.round(body * RitualBacklashTuning.RangedFraction(wave));
                int melee = math.max(0, body - ranged);

                var c = xf.ValueRO.Position;
                for (int i = 0; i < total; i++)
                {
                    // Deterministic ring placement: golden-angle stride so a
                    // 50-unit wave spreads evenly instead of stacking on one
                    // bearing, with the radius breathing per index so they do
                    // not erupt in a perfect circle.
                    float ang = ((i * 137 + wave * 53) % 360) * math.PI / 180f;
                    float rad = RitualBacklashTuning.SpawnRadius
                              * (0.75f + ((i * 29 + wave * 11) % 50) / 100f);
                    float x = c.x + math.cos(ang) * rad;
                    float z = c.z + math.sin(ang) * rad;

                    byte kind = i < siege ? (byte)2 : (i < siege + ranged ? (byte)1 : (byte)0);
                    spawnPos.Add(new float3(x, TerrainUtility.GetHeight(x, z), z));
                    spawnKind.Add(kind);
                }

                TheWaningBorder.AI.AILogger.Log(b.Provoker, "RITUAL",
                    $"BACKLASH wave {wave}/{RitualBacklashTuning.WaveCount}: {total} erupt " +
                    $"({melee} Crystalling, {ranged} Veilstinger, {siege} Godsplinter)");
                TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                    $"Backlash — wave {wave} of {RitualBacklashTuning.WaveCount}!");

                if (wave >= RitualBacklashTuning.WaveCount) finished.Add(entity);
            }

            // Structural changes after iteration.
            for (int i = 0; i < spawnPos.Length; i++)
            {
                switch (spawnKind[i])
                {
                    case 2: Godsplinter.Create(em, spawnPos[i], Faction.Border); break;
                    case 1: Veilstinger.Create(em, spawnPos[i], Faction.Border); break;
                    default: Crystalling.Create(em, spawnPos[i], Faction.Border); break;
                }
            }
            for (int i = 0; i < finished.Length; i++)
                if (em.Exists(finished[i])) em.RemoveComponent<RitualBacklash>(finished[i]);

            spawnPos.Dispose();
            spawnKind.Dispose();
            finished.Dispose();
        }
    }
}
