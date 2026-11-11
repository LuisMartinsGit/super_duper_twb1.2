// CountingHouse.cs
// The COUNTING HOUSE (docs/Design/Sects.md § Sect of Prosperity, decision 47):
// the Sect of Prosperity's ★ power Plenty raises one at the aimed point, snapped
// to the build grid on ground a building could stand on — placed like a
// building, ghost and all (Build_Grid.md § Buildings raised by abilities). It trickles income to
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

        /// <summary>
        /// Raise a Counting House for <paramref name="faction"/> at
        /// <paramref name="aim"/>, snapped to the build grid. When the snapped
        /// site fails <see cref="SiteRefusal"/> nothing is raised and the call
        /// returns false (the strike refunds the cooldown). Executor side: every
        /// peer runs it at the same tick and reaches the same verdict.
        /// </summary>
        public static bool Spawn(EntityManager em, Faction faction, float3 aim, int level)
        {
            if ((int)faction < 0 || (int)faction >= 8) return false;
            if (!CanRaise(em, faction)) return false;
            // Placed like a building (Build_Grid.md § Buildings raised by
            // abilities): exactly on the snapped aim, the ghost's own spot, or
            // not at all — never wandered to a nearby one.
            float3 at = SiteFor(aim);
            if (SiteRefusal(em, faction, at) != null) return false;

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

        /// <summary>Where a house aimed at <paramref name="aim"/> stands: on the
        /// build grid by its footprint, at the ground's height.</summary>
        public static float3 SiteFor(float3 aim) => AbilityBuildingSite.Snap(aim, SizeMeters);

        /// <summary>
        /// The one test of a house's SNAPPED site (null = it may stand there):
        /// ground a building could stand on (Sects.md, Plenty) — the crust and
        /// geometry rules and the owner's plans, no territory gate — and never
        /// on another Counting House. The ghost, the AI, Fire and the landing
        /// strike all ask this.
        /// </summary>
        public static string SiteRefusal(EntityManager em, Faction faction, float3 site)
        {
            var size = SizeMeters;
            string why = AbilityBuildingSite.Refusal(em, faction, site, size, null,
                AbilityBuildingSite.Ground.Buildable);
            if (why != null) return why;
            using var houses = All(em, Allocator.Temp);
            return OverlapsHouse(em, houses, site, size)
                ? TheWaningBorder.World.Regions.PlacementRefusalText.Of(
                    TheWaningBorder.World.Regions.PlacementRefusal.Overlap)
                : null;
        }

        /// <summary>For the AI: the legal snapped site nearest <paramref name="aim"/>
        /// within placementSearchRadius (deterministic). The cast is then aimed
        /// there, and lands there.</summary>
        public static bool TryNearestSite(EntityManager em, Faction faction, float3 aim, out float3 at)
            => AbilityBuildingSite.TryNearest(aim, SizeMeters, Cfg.placementSearchRadius,
                p => SiteRefusal(em, faction, p), out at);

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
