// FactionSight.cs
// The SIMULATION's line-of-sight test: can a faction see a point right now?
//
// FogOfWarManager is the presentation grid: a MonoBehaviour, absent headless,
// stamped on a wall-clock cadence and only meaningful for the local player.
// Nothing the simulation decides may read it. This helper answers the same
// question from simulation state alone — every sight source's LineOfSight
// radius around its LocalTransform, owned by the faction or an ally (teams
// share line of sight, docs/Design/Teams.md) — so every lockstep peer gets
// the same answer on the same tick.
//
// Sight sources are what FogOfWarSystem stamps: any entity with LineOfSight +
// LocalTransform + FactionTag that is not the curse's (BorderTag / Faction
// .Border). Disabled entities (garrisoned units) give no sight of their own —
// the wall or tower they stand in does.
//
// The source list is snapshotted once and reused until the world moves on:
// the key is the world, its GlobalSystemVersion (bumped before every system
// update) and its EntityOrderVersion (bumped by every structural change), so
// the many tests one AI think or one command batch makes cost one gather.
//
// Cartography (2026-10-19): a territory held by the faction or an ally with
// the research is seen whole as well (SeesHeldTerritory) — the fog grid's
// per-territory stamp in FogOfWarManager is the presentation of the same rule.
//
// First reader: sect powers (docs/Design/Spells.md §6.1 — a power may only be
// aimed where the caster can see).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.World.FogOfWar
{
    public static class FactionSight
    {
        static readonly ComponentType[] QT_SightSources =
        {
            ComponentType.ReadOnly<LineOfSight>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<BorderTag>(),
        };
        static CachedEntityQuery QC_SightSources;

        // Snapshot of every sight source (host scratch, rebuilt on demand).
        static float[] _x = new float[256];
        static float[] _z = new float[256];
        static float[] _r2 = new float[256];
        static byte[] _fac = new byte[256];
        static int _count;

        static Unity.Entities.World _world;
        static uint _systemVersion;
        static int _orderVersion = -1;

        /// <summary>
        /// True when <paramref name="faction"/> — through its own or an
        /// allied faction's units and buildings — has <paramref name="pos"/>
        /// inside a line-of-sight circle right now. Measured on the ground
        /// plane (x/z). Deterministic: reads simulation components only.
        /// <paramref name="fresh"/> forces a new gather: the AUTHORITATIVE
        /// caller (a cast executing on every lockstep peer) passes it, so its
        /// answer never depends on whether a cache was warm on this peer —
        /// the version key cannot see a component write made outside a
        /// system update (a command applied from a MonoBehaviour).
        /// </summary>
        public static bool CanSee(EntityManager em, Faction faction, float3 pos, bool fresh = false)
        {
            if (fresh) _world = null;
            Refresh(em);

            // Allied-faction mask once per call; Alliances is a table lookup
            // but this loop can run over a thousand sources.
            int mask = 0;
            for (int f = 0; f < Alliances.MaxFactions; f++)
                if (f != (int)Faction.Border && Alliances.AreAllied(faction, (Faction)f))
                    mask |= 1 << f;
            if (mask == 0) return false;

            for (int i = 0; i < _count; i++)
            {
                if ((mask & (1 << _fac[i])) == 0) continue;
                float dx = _x[i] - pos.x, dz = _z[i] - pos.z;
                if (dx * dx + dz * dz <= _r2[i]) return true;
            }
            return SeesHeldTerritory(mask, pos);
        }

        /// <summary>
        /// CARTOGRAPHY (docs/Design/Sects.md §4, 2026-10-19): a territory held
        /// by the faction or an ally that has researched it is seen whole —
        /// the same rule the fog grid draws. Deterministic: territory
        /// ownership, the region partition and the research flag are all
        /// lockstep state.
        /// </summary>
        private static bool SeesHeldTerritory(int allyMask, float3 pos)
        {
            if (!TheWaningBorder.World.Regions.RegionMap.Ready
                || !TheWaningBorder.World.Regions.TerritoryOwnership.Ready) return false;
            int t = TheWaningBorder.World.Regions.RegionMap.RegionAt(pos.x, pos.z);
            if (t == TheWaningBorder.World.Regions.RegionMap.None) return false;
            int owner = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t);
            if (owner < 0 || owner >= Alliances.MaxFactions || (allyMask & (1 << owner)) == 0) return false;
            return TheWaningBorder.Economy.SectResearchEffects.RevealsHeldTerritory((Faction)owner);
        }

        private static void Refresh(EntityManager em)
        {
            var world = em.World;
            uint sysV = em.GlobalSystemVersion;
            int ordV = em.EntityOrderVersion;
            if (ReferenceEquals(world, _world) && sysV == _systemVersion && ordV == _orderVersion)
                return;
            _world = world;
            _systemVersion = sysV;
            _orderVersion = ordV;

            var q = QC_SightSources.Get(em, QT_SightSources);
            using var los = q.ToComponentDataArray<LineOfSight>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = los.Length;
            if (_x.Length < n)
            {
                int cap = math.ceilpow2(n);
                _x = new float[cap]; _z = new float[cap]; _r2 = new float[cap]; _fac = new byte[cap];
            }
            _count = 0;
            for (int i = 0; i < n; i++)
            {
                byte f = (byte)facs[i].Value;
                if (f >= Alliances.MaxFactions || f == (byte)Faction.Border) continue;
                float r = los[i].Radius;
                if (r <= 0f) continue;
                var p = xfs[i].Position;
                _x[_count] = p.x;
                _z[_count] = p.z;
                _r2[_count] = r * r;
                _fac[_count] = f;
                _count++;
            }
        }
    }
}
