// BlightPocketSystem.cs
// Runs the Age 0 blight pockets (§2.5b) registered by BlightPocketBootstrap:
//
//   * SEED     — once the VeilField exists, stamp each pocket's established
//                haze disc into the saturation grid (falloff like the well
//                seed discs). One-time write; the CA owns the cells after.
//   * SUSTAIN  — nothing to do here: live sporelings are folded into the CA
//                feeder set by VeilFieldSystem, so the patch feeds itself.
//   * STARVE   — a sporeling standing in suppressed ground (Hall hearth or
//                any player influence >= threshold) loses HP each tick;
//                warding a pocket kills it in ~SporelingHealth/StarveDps s.
//   * COLLAPSE — when a pocket's sporeling dies (combat or starvation; the
//                normal death pipeline destroys it), fire exactly once:
//                stamp a field break over the pocket (instant clear + regrow
//                cooldown — the shatter) and pay the residue field of
//                veilstone nodes. This is the Age 0 income beat: secure
//                yourself, get paid.
//
// Determinism: seeded RNG for residue scatter, sim state only, fixed tick.
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Influence;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BlightPocketSystem : SystemBase
    {
        private const float TickInterval = 1f;

        private float _acc;
        private Unity.Mathematics.Random _rng;
        private EntityQuery _hallQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<BlightPocket>();
            _rng = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0x51073) | 1u);
            _hallQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HallTag, LocalTransform>()
                .WithNone<UnderConstruction>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            _acc += SystemAPI.Time.DeltaTime;
            if (_acc < TickInterval) return;
            _acc -= TickInterval;

            var em = EntityManager;
            bool hasField = SystemAPI.HasSingleton<VeilField>();
            double simNow = SystemAPI.Time.ElapsedTime;

            // Raise telegraphed corruptions whose countdown has expired
            // (VeilstoneMiningSystem pings + queues them; we own the rise).
            {
                var regQuery = GetEntityQuery(ComponentType.ReadWrite<BlightPocket>());
                using var regs = regQuery.ToEntityArray(Allocator.Temp);
                if (regs.Length > 0 && em.HasBuffer<PendingCorruption>(regs[0]))
                {
                    // Collect due entries first — spawning is structural.
                    var due = new NativeList<float3>(Allocator.Temp);
                    var pendingBuf = em.GetBuffer<PendingCorruption>(regs[0]);
                    for (int i = pendingBuf.Length - 1; i >= 0; i--)
                    {
                        if (simNow < pendingBuf[i].At) continue;
                        due.Add(pendingBuf[i].Pos);
                        pendingBuf.RemoveAt(i);
                    }
                    for (int i = 0; i < due.Length; i++)
                    {
                        var sporeling = Sporeling.Create(em, due[i]);
                        var pockets = em.GetBuffer<BlightPocket>(regs[0]); // re-fetch post-spawn
                        pockets.Add(new BlightPocket
                        {
                            Sporeling = sporeling,
                            Center = new Unity.Mathematics.float2(due[i].x, due[i].z),
                            Radius = PocketRadius,
                            Seeded = 0,
                            Collapsed = 0,
                        });
                        TheWaningBorder.UI.GameUI.MinimapPings.Post(due[i],
                            TheWaningBorder.UI.GameUI.MinimapPings.Curse, 6f, big: true);
                        TWBLog.Log($"[BlightPocket] telegraphed corruption rose at {due[i]}.");
                    }
                    due.Dispose();
                }
            }

            var buffer = SystemAPI.GetSingletonBuffer<BlightPocket>();

            for (int i = 0; i < buffer.Length; i++)
            {
                var pocket = buffer[i];

                // One-time haze disc, as soon as the field exists.
                if (pocket.Seeded == 0 && hasField)
                {
                    var field = SystemAPI.GetSingleton<VeilField>();
                    if (field.Initialised != 0 && field.Saturation.IsCreated)
                    {
                        SeedPocketDisc(ref field, pocket.Center, pocket.Radius);
                        field.Generation++;
                        SystemAPI.SetSingleton(field);
                        pocket.Seeded = 1;
                        buffer[i] = pocket;
                    }
                }

                if (pocket.Collapsed != 0) continue;

                bool alive = pocket.Sporeling != Entity.Null
                    && em.Exists(pocket.Sporeling)
                    && em.HasComponent<Health>(pocket.Sporeling)
                    && em.GetComponentData<Health>(pocket.Sporeling).Value > 0;

                if (alive)
                {
                    // Starvation: suppressed ground chokes the anchor. Kill
                    // through the normal death pipeline (Health -> 0), never
                    // DestroyEntity here.
                    var pos = em.GetComponentData<LocalTransform>(pocket.Sporeling).Position;
                    if (SuppressedAt(pos))
                    {
                        var hp = em.GetComponentData<Health>(pocket.Sporeling);
                        hp.Value -= (int)math.ceil(SporelingStarveDps * TickInterval);
                        if (hp.Value < 0) hp.Value = 0;
                        em.SetComponentData(pocket.Sporeling, hp);
                    }
                    continue;
                }

                Collapse(em, in pocket, hasField);
                pocket.Collapsed = 1;
                buffer = SystemAPI.GetSingletonBuffer<BlightPocket>(); // re-fetch: Collapse spawned entities
                buffer[i] = pocket;
            }
        }

        /// <summary>Hall hearth or any player influence at/over the threshold
        /// — the same suppression rule the CA reads, sampled point-wise for
        /// the handful of sporelings.</summary>
        private bool SuppressedAt(float3 pos)
        {
            using var halls = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float r2 = HallHearthRadius * HallHearthRadius;
            for (int i = 0; i < halls.Length; i++)
            {
                float dx = halls[i].Position.x - pos.x;
                float dz = halls[i].Position.z - pos.z;
                if (dx * dx + dz * dz <= r2) return true;
            }

            if (PlayerInfluenceMap.Ready)
                for (int f = 0; f < PlayerInfluenceMap.PlayerChannels; f++)
                    if (PlayerInfluenceMap.ChannelStrengthWorld(f, pos.x, pos.z)
                        >= InfluenceThreshold)
                        return true;
            return false;
        }

        /// <summary>Falloff disc like VeilFieldSystem's well seeds: solid at
        /// the core (PocketCoreSaturation), thinning past the crust threshold
        /// toward the rim so the patch reads as haze with a hard heart.</summary>
        private static void SeedPocketDisc(ref VeilField field, float2 center, float radius)
        {
            int r = (int)math.ceil(radius / field.CellSize);
            int cx = (int)math.floor((center.x - field.Origin.x) / field.CellSize);
            int cz = (int)math.floor((center.y - field.Origin.y) / field.CellSize);
            for (int z = math.max(0, cz - r); z <= math.min(field.Height - 1, cz + r); z++)
                for (int x = math.max(0, cx - r); x <= math.min(field.Width - 1, cx + r); x++)
                {
                    float dx = (x - cx) * field.CellSize;
                    float dz = (z - cz) * field.CellSize;
                    float d = math.sqrt(dx * dx + dz * dz);
                    if (d > radius) continue;
                    float t = 1f - d / radius;
                    byte v = (byte)math.min(255f, PocketCoreSaturation * (0.35f + 0.65f * t));
                    int idx = field.Index(x, z);
                    if (v > field.Saturation[idx]) field.Saturation[idx] = v;
                }
        }

        /// <summary>The pocket shatters: break the field over it (instant
        /// clear + regrow cooldown) and scatter the residue payout.</summary>
        private void Collapse(EntityManager em, in BlightPocket pocket, bool hasField)
        {
            if (hasField && SystemAPI.HasSingleton<VeilField>())
            {
                var breaks = SystemAPI.GetSingletonBuffer<VeilBreakRequest>();
                breaks.Add(new VeilBreakRequest
                {
                    Position = pocket.Center,
                    Radius = pocket.Radius + 2f, // clear slightly past the rim
                });
            }

            for (int n = 0; n < PocketResidueNodes; n++)
            {
                float angle = _rng.NextFloat(0f, math.PI * 2f);
                float dist = _rng.NextFloat(1.5f, pocket.Radius * 0.8f);
                float x = pocket.Center.x + math.cos(angle) * dist;
                float z = pocket.Center.y + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                VeilstoneOutcropping.CreateOrMerge(em, new float3(x, y, z), PocketResiduePerNode);
            }
            TWBLog.Log($"[BlightPocket] pocket at {pocket.Center} collapsed — " +
                       $"{PocketResidueNodes}x{PocketResiduePerNode} residue veilstone.");
        }
    }
}
