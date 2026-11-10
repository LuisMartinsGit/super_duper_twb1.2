// CountingHouse.cs
// The COUNTING HOUSE (docs/Design/Sects.md § Sect of Prosperity, decision 47):
// the Sect of Prosperity's ★ power Plenty raises one at the aimed point, snapped
// to the build grid on ground a building could stand on. It trickles income to
// whoever holds it, slowly (rates by power level on CountingHouse.asset), and it
// is captured exactly like a monument (CountingHouseSystem).
//
// Not a building on purpose, like the monument: no BuildingTag and no Health, so
// nothing targets it, and territory, elimination and score never see it.
// FactionTag follows the holder so the selection panel treats a held house as
// its holder's own. The caster's faction holds it at spawn.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

public struct CountingHouseTag : IComponentData { }

public struct CountingHouseState : IComponentData
{
    public Faction Holder;
    /// <summary>Who is taking it right now (Border = nobody), and for how long.</summary>
    public Faction Capturer;
    public int CaptureSeconds;
    /// <summary>The Plenty level that raised it, 1-3: what it pays.</summary>
    public byte Level;
    /// <summary>Per-minute income banked each second, in sixtieths of a unit,
    /// so whole units are paid as they come due (integer, lockstep-safe).</summary>
    public int SuppliesAcc, IronAcc, VeilstoneAcc;
    /// <summary>Seconds banked toward the next religion point.</summary>
    public int ReligionSeconds;
}

namespace TheWaningBorder.Entities
{
    public static class CountingHouse
    {
        /// <summary>Free id (2026-11-10): 388-403 / 412-413 are chapels and
        /// map props, 398 is the monument.</summary>
        public const int PresentationID = 599;

        private static CountingHouseConfig Cfg => CountingHouseConfig.I;

        private static readonly ComponentType[] QT_Houses =
        {
            ComponentType.ReadOnly<CountingHouseTag>(),
            ComponentType.ReadOnly<CountingHouseState>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Houses;

        /// <summary>Every Counting House, in entity order. Callers dispose.</summary>
        public static NativeArray<Entity> All(EntityManager em, Allocator allocator)
            => QC_Houses.Get(em, QT_Houses).ToEntityArray(allocator);

        /// <summary>The footprint, in metres (cells x the 2 m build cell).</summary>
        public static int2 SizeMeters
        {
            get
            {
                int m = (int)math.round(Cfg.footprintCells * BuildGrid.CellSize);
                return new int2(m, m);
            }
        }

        /// <summary>Clamp a power level to the three rungs the asset prices.</summary>
        public static int LevelIndex(int level) => math.clamp(level, 1, 3) - 1;

        /// <summary>
        /// Raise a Counting House for <paramref name="faction"/> at
        /// <paramref name="aim"/>, snapped to the build grid. When the aimed
        /// footprint is not ground a building could stand on, the nearest valid
        /// snapped spot within the search radius is taken; when there is none,
        /// nothing is raised and the call returns false. Executor side: every
        /// peer runs it at the same tick and reaches the same spot.
        /// </summary>
        /// <summary>Counting Houses <paramref name="faction"/> holds right now.</summary>
        public static int HeldBy(EntityManager em, Faction faction)
        {
            using var all = All(em, Allocator.Temp);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                if (em.GetComponentData<CountingHouseState>(all[i]).Holder == faction) n++;
            return n;
        }

        /// <summary>May <paramref name="faction"/> raise another? False at the
        /// cap (maxPerPlayer; 0 = no cap). Captured houses count too.</summary>
        public static bool CanRaise(EntityManager em, Faction faction)
            => Cfg.maxPerPlayer <= 0 || HeldBy(em, faction) < Cfg.maxPerPlayer;

        public static bool Spawn(EntityManager em, Faction faction, float3 aim, int level)
        {
            if ((int)faction < 0 || (int)faction >= 8) return false;
            if (!CanRaise(em, faction)) return false;
            if (!TryFindSpot(em, aim, out float3 at)) return false;

            var e = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(CountingHouseTag),
                typeof(CountingHouseState),
                typeof(Radius));
            em.SetComponentData(e, new PresentationId { Id = PresentationID });
            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(at, quaternion.identity, 1f));
            em.SetComponentData(e, new FactionTag { Value = faction });
            em.SetComponentData(e, new Radius { Value = Cfg.selectionRadius });
            em.SetComponentData(e, new CountingHouseState
            {
                Holder = faction,
                Capturer = Faction.Border,
                Level = (byte)(LevelIndex(level) + 1),
            });
            UnityEngine.Debug.Log($"[CountingHouse] {faction} raised a level {LevelIndex(level) + 1} Counting House at ({at.x:F0},{at.z:F0})");
            return true;
        }

        /// <summary>The aimed spot, or the nearest valid snapped offset within
        /// placementSearchRadius (ties broken by scan order, so deterministic).</summary>
        private static bool TryFindSpot(EntityManager em, float3 aim, out float3 at)
        {
            at = default;
            var size = SizeMeters;
            float step = math.max(BuildGrid.CellSize, Cfg.placementSearchStep);
            int n = (int)math.floor(math.max(0f, Cfg.placementSearchRadius) / step);
            float r2 = Cfg.placementSearchRadius * Cfg.placementSearchRadius;

            using var houses = All(em, Allocator.Temp);
            float best = float.MaxValue;
            bool found = false;
            for (int dz = -n; dz <= n; dz++)
            {
                for (int dx = -n; dx <= n; dx++)
                {
                    float ox = dx * step, oz = dz * step;
                    float d2 = ox * ox + oz * oz;
                    if (d2 > r2 + 0.001f || d2 >= best) continue;
                    var p = BuildGrid.Snap(new float3(aim.x + ox, 0f, aim.z + oz), size);
                    p.y = TerrainUtility.GetHeight(p.x, p.z);
                    if (!BuildCommandHelper.IsValidBuildPosition(em, p, size, null)) continue;
                    if (OverlapsHouse(em, houses, p, size)) continue;
                    best = d2;
                    at = p;
                    found = true;
                }
            }
            return found;
        }

        private static bool OverlapsHouse(EntityManager em, NativeArray<Entity> houses, float3 p, int2 size)
        {
            for (int i = 0; i < houses.Length; i++)
            {
                var q = em.GetComponentData<LocalTransform>(houses[i]).Position;
                if (math.abs(q.x - p.x) < size.x && math.abs(q.z - p.z) < size.y) return true;
            }
            return false;
        }

        /// <summary>What a house of this level pays, per minute (no religion).</summary>
        public static Cost PerMinute(int level)
        {
            int i = LevelIndex(level);
            return Cost.Of(
                supplies: At(Cfg.suppliesPerMinute, i),
                iron: At(Cfg.ironPerMinute, i),
                veilstone: At(Cfg.veilstonePerMinute, i));
        }

        /// <summary>Seconds per religion point at this level; 0 = pays none.</summary>
        public static float SecondsPerReligionPoint(int level)
        {
            var a = Cfg.secondsPerReligionPoint;
            int i = LevelIndex(level);
            return a == null || i >= a.Length ? 0f : a[i];
        }

        private static int At(int[] a, int i) => a == null || i >= a.Length ? 0 : a[i];
    }
}
