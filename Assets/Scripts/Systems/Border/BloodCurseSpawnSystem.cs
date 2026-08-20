// BloodCurseSpawnSystem.cs
// §2.5b rev.3 — "blood + curse = spawner". Blood outside influence is
// eternal (BloodMap.DecayInsideInfluence); where such a pool soaks CURSED
// ground (veil saturation >= CrustThreshold), the curse quickens: the site
// births crystal creatures and CONSUMES the pool.
//
//   * One site per scan (every BloodSpawnInterval s): the strongest
//     blood-on-crust pool on the map. Size and tier scale with the pool —
//     small -> Crystalling, large -> Veilstinger, massive -> Godsplinter;
//     count ramps to BloodSpawnMaxCount.
//   * BloodSpawnCap on live Border creatures is the anti-snowball lid.
//   * The birth drains the pool (BloodMap.Drain) — each battlefield feeds
//     the curse once, then the stain is spent.
//
// This is the creatures' return, but as an emergent, player-caused, fully
// attributable source: only battles fought (and dead left) on cursed
// ground arm it. No aggro faction, no target selection — spawned creatures
// use the ordinary TargetingSystem like any hostile.
//
// Determinism: BloodMap + VeilField are sim-fed; scan order is row-major;
// RNG is seeded. Lockstep-safe.
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Influence;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BloodCurseSpawnSystem : SystemBase
    {
        /// <summary>A contaminating pool: announced + pinged now, creatures
        /// rise at <see cref="At"/>. Public so the AI can pre-position a
        /// squad on the site (user directive 2026-08-04: "AI must be aware").</summary>
        public struct PendingBloodSpawn
        {
            public float3 Pos;
            public int Crystallings, Veilstingers, Godsplinters;
            public double At;
        }

        public static readonly System.Collections.Generic.List<PendingBloodSpawn> Pending = new();

        private float _acc;
        private Unity.Mathematics.Random _rng;
        private EntityQuery _borderUnitQuery;

        protected override void OnCreate()
        {
            Enabled = BloodCurseSpawnsEnabled;
            Pending.Clear();
            if (!Enabled) return;
            RequireForUpdate<VeilField>();
            _rng = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xB100D) | 1u);
            _borderUnitQuery = GetEntityQuery(ComponentType.ReadOnly<BorderUnitTag>());
        }

        /// <summary>Spawn every pending contamination whose countdown has
        /// expired. Runs each tick (independent of the scan cadence).</summary>
        private void ProcessPending(double simNow)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var p = Pending[i];
                if (simNow < p.At) continue;
                Pending.RemoveAt(i);

                int total = p.Crystallings + p.Veilstingers + p.Godsplinters;
                for (int u = 0; u < total; u++)
                {
                    float angle = _rng.NextFloat(0f, math.PI * 2f);
                    float dist = _rng.NextFloat(0f, 3f);
                    float sx = p.Pos.x + math.cos(angle) * dist;
                    float sz = p.Pos.z + math.sin(angle) * dist;
                    var pos = new float3(sx, TerrainUtility.GetHeight(sx, sz), sz);
                    if (u < p.Godsplinters) Godsplinter.Create(EntityManager, pos, Faction.Border);
                    else if (u < p.Godsplinters + p.Veilstingers) Veilstinger.Create(EntityManager, pos, Faction.Border);
                    else Crystalling.Create(EntityManager, pos, Faction.Border);
                }
                BloodMap.Drain(p.Pos.x, p.Pos.z, BloodPoolRadius);
                TheWaningBorder.UI.GameUI.MinimapPings.Post(p.Pos,
                    TheWaningBorder.UI.GameUI.MinimapPings.Curse, 6f, big: true);
                TWBLog.Log($"[BloodCurse] telegraphed pool at ({p.Pos.x:0},{p.Pos.z:0}) " +
                           $"birthed {total} creature(s).");
            }
        }

        protected override void OnUpdate()
        {
            ProcessPending(SystemAPI.Time.ElapsedTime);

            _acc += SystemAPI.Time.DeltaTime;
            if (_acc < BloodSpawnInterval) return;
            _acc -= BloodSpawnInterval;

            // One announced contamination at a time — its countdown must
            // resolve before the next pool can quicken.
            if (Pending.Count > 0) return;

            // Opening-minutes grace: no births while players are still
            // securing themselves (§2.5b loop damping). Blood accumulated
            // during the grace stays on the map — the debt comes due.
            if (SystemAPI.Time.ElapsedTime < BloodSpawnGraceSeconds) return;

            if (!BloodMap.Ready || !PlayerInfluenceMap.Ready) return;
            if (!BloodMap.HasPresence(BloodSpawnThreshold)) return;

            var field = SystemAPI.GetSingleton<VeilField>();
            if (field.Initialised == 0 || !field.Saturation.IsCreated) return;

            // Anti-snowball lid: never grow the curse's live population past
            // the cap, whatever the map looks like.
            if (_borderUnitQuery.CalculateEntityCount() >= BloodSpawnCap) return;

            // Grid geometry (BloodMap shares PlayerInfluenceMap's bounds).
            UnityEngine.Vector2 wMin = PlayerInfluenceMap.WorldMin;
            UnityEngine.Vector2 wSize = PlayerInfluenceMap.WorldSize;
            float cellW = wSize.x / BloodMap.Resolution;
            float cellH = wSize.y / BloodMap.Resolution;
            int poolRx = (int)math.ceil(BloodPoolRadius / cellW);
            int poolRy = (int)math.ceil(BloodPoolRadius / cellH);

            // Hall keep-out (2026-08-04, log-proven: a mid-game battle's
            // eternal pools next to both Halls turned bases into permanent
            // spawner nests — armies were re-killed at home four times over
            // and the AI locked into Defend, blocking every wave). The dead
            // stay eternal; the curse just never QUICKENS at a doorstep.
            var hallQuery = GetEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
            using var hallXfs = hallQuery
                .ToComponentDataArray<Unity.Transforms.LocalTransform>(Allocator.Temp);

            // Strongest blood-on-crust site this scan (row-major, deterministic).
            float bestPool = 0f;
            float bestX = 0f, bestZ = 0f;
            float hallKeepOutSq = BloodSpawnHallKeepOut * BloodSpawnHallKeepOut;
            for (int y = 0; y < BloodMap.Resolution; y++)
            {
                for (int x = 0; x < BloodMap.Resolution; x++)
                {
                    if (BloodMap.CellValue(x, y) < BloodSpawnThreshold) continue;

                    float wx = wMin.x + (x + 0.5f) * cellW;
                    float wz = wMin.y + (y + 0.5f) * cellH;
                    if (field.SaturationAt(new float3(wx, 0f, wz)) < VeilField.CrustThreshold)
                        continue;

                    bool nearHall = false;
                    for (int h = 0; h < hallXfs.Length && !nearHall; h++)
                    {
                        float hx = hallXfs[h].Position.x - wx;
                        float hz = hallXfs[h].Position.z - wz;
                        nearHall = hx * hx + hz * hz < hallKeepOutSq;
                    }
                    if (nearHall) continue;

                    // Pool size: summed normalized blood around the site.
                    float pool = 0f;
                    for (int py = math.max(0, y - poolRy);
                         py <= math.min(BloodMap.Resolution - 1, y + poolRy); py++)
                        for (int px = math.max(0, x - poolRx);
                             px <= math.min(BloodMap.Resolution - 1, x + poolRx); px++)
                            pool += BloodMap.CellValue(px, py);

                    if (pool > bestPool)
                    {
                        bestPool = pool;
                        bestX = wx;
                        bestZ = wz;
                    }
                }
            }
            if (bestPool <= 0f) return;

            // Wave COMPOSITION — a mix, never a monotier burst: mostly
            // Crystallings, Veilstingers only from sizeable pools, and at
            // most ONE Godsplinter, only where a LARGE battle occurred.
            int crystallings = (int)math.clamp(
                1f + bestPool / BloodPoolPerCrystalling, 1f, 3f);
            int veilstingers = bestPool >= BloodPoolVeilstingerMin
                ? math.min(2, (int)(bestPool / BloodPoolPerVeilstinger)) : 0;
            int godsplinters = bestPool >= BloodPoolGodsplinterMin ? 1 : 0;
            int count = crystallings + veilstingers + godsplinters;

            // TELEGRAPHED (2026-08-04): announce + ping now, creatures rise
            // after the countdown. The pending entry is public so the AI can
            // pre-position on the site.
            double at = SystemAPI.Time.ElapsedTime + BloodSpawnTelegraphSeconds;
            float siteY = TerrainUtility.GetHeight(bestX, bestZ);
            Pending.Add(new PendingBloodSpawn
            {
                Pos = new float3(bestX, siteY, bestZ),
                Crystallings = crystallings,
                Veilstingers = veilstingers,
                Godsplinters = godsplinters,
                At = at,
            });
            TheWaningBorder.UI.GameUI.MinimapPings.Post(new float3(bestX, siteY, bestZ),
                TheWaningBorder.UI.GameUI.MinimapPings.Curse,
                BloodSpawnTelegraphSeconds, big: true);
            TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                string.Format(
                    Loc.T("Blood pool contaminating — {0} curse unit(s) will rise at ({1:0},{2:0}) in {3}s!"),
                    count, bestX, bestZ, (int)BloodSpawnTelegraphSeconds));
            TWBLog.Log($"[BloodCurse] pool {bestPool:0.0} at ({bestX:0},{bestZ:0}) " +
                       $"contaminating: {crystallings}c/{veilstingers}v/{godsplinters}g in " +
                       $"{(int)BloodSpawnTelegraphSeconds}s.");
        }
    }
}
