// SetPassives.cs
// The four SET PASSIVES (docs/Design/Religion.md §3.3, Sects.md §6b,
// 2026-10-15). A faction that holds all four sects of one cluster, or two
// sects of each cluster, earns that set's passive. Six slots make the two
// kinds of set exclusive, so a faction has at most one.
//
// Derived from SectAdoptionState alone: adoption is permanent and replicated,
// so every lockstep peer computes the same set with no new state. Destruction
// (Temple, sect buildings) never switches a set off.
//
//   Sovereign Hour     (Alanthor)  SetPassiveSystem: own territory haste / slow
//   The Breaking Hour  (Feraldis)  SetPassiveSystem: haste after a kill
//   Foreknown          (Runai)     SectActivePowerHelper.Fire: windup x2
//   Concord            (2/2/2)     EffectivePowerLevel: +1 level, max III

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Economy
{
    public enum SectSet : byte
    {
        None = 0,
        Alanthor = 1,   // Sovereign Hour
        Feraldis = 2,   // The Breaking Hour
        Runai = 3,      // Foreknown
        Concord = 4,    // two of each cluster
    }

    public static class SetPassives
    {
        public const int SetSize = 4;
        public const int ConcordPerCluster = 2;

        static readonly ComponentType[] QT_Targets =
        {
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Targets;

        /// <summary>Adopted sects per cluster (Alanthor, Runai, Feraldis).</summary>
        public static int3 ClusterCounts(in SectAdoptionState state)
        {
            int3 c = default;
            for (int i = 0; i < SectConfig.SectCount; i++)
            {
                if (!state.Get(i).IsAdopted) continue;
                switch (SectConfig.ClusterByIndex[i])
                {
                    case SectCluster.Alanthor: c.x++; break;
                    case SectCluster.Runaii:   c.y++; break;
                    case SectCluster.Feraldis: c.z++; break;
                }
            }
            return c;
        }

        /// <summary>The set a cluster count completes, if any.</summary>
        public static SectSet SetOf(int3 counts)
        {
            if (counts.x >= SetSize) return SectSet.Alanthor;
            if (counts.z >= SetSize) return SectSet.Feraldis;
            if (counts.y >= SetSize) return SectSet.Runai;
            if (counts.x >= ConcordPerCluster && counts.y >= ConcordPerCluster
                && counts.z >= ConcordPerCluster) return SectSet.Concord;
            return SectSet.None;
        }

        /// <summary>The set passive <paramref name="faction"/> holds right now.</summary>
        public static SectSet ActiveSet(EntityManager em, Faction faction)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)
                || !em.HasComponent<SectAdoptionState>(bank)) return SectSet.None;
            return SetOf(ClusterCounts(em.GetComponentData<SectAdoptionState>(bank)));
        }

        /// <summary>Can <paramref name="set"/> still be completed with the slots
        /// left? Drives the religion menu's struck-through set rings.</summary>
        public static bool StillReachable(int3 counts, SectSet set)
        {
            int used = counts.x + counts.y + counts.z;
            int free = SectConfig.MaxAdoptedSects - used;
            switch (set)
            {
                case SectSet.Alanthor: return SetSize - counts.x <= free;
                case SectSet.Runai:    return SetSize - counts.y <= free;
                case SectSet.Feraldis: return SetSize - counts.z <= free;
                case SectSet.Concord:
                    int need = math.max(0, ConcordPerCluster - counts.x)
                             + math.max(0, ConcordPerCluster - counts.y)
                             + math.max(0, ConcordPerCluster - counts.z);
                    return need <= free;
            }
            return false;
        }

        /// <summary>
        /// The level a sect's powers are cast at: its chapel level, one higher
        /// under Concord, never above III. Fire and the tooltips both read it.
        /// </summary>
        public static int EffectivePowerLevel(EntityManager em, Faction faction, string sectId)
        {
            int level = SectQuery.PowerLevelOf(em, faction, sectId);
            if (level < 1) level = 1;
            if (ActiveSet(em, faction) == SectSet.Concord) level++;
            return level > 3 ? 3 : level;
        }

        /// <summary>
        /// Foreknown: does a hostile power cast by <paramref name="caster"/> at
        /// this circle reach a unit or building of a faction holding the Runai
        /// set? Then its windup is doubled (Sects.md §6b). Reads only
        /// replicated state, so every peer agrees.
        /// </summary>
        public static bool ForeknownTargeted(EntityManager em, Faction caster, float3 center, float radius)
        {
            uint foreknown = 0;
            for (int f = 0; f < 8; f++)
            {
                var fac = (Faction)f;
                if (fac == caster || !Alliances.AreHostile(caster, fac)) continue;
                if (ActiveSet(em, fac) == SectSet.Runai) foreknown |= 1u << f;
            }
            if (foreknown == 0) return false;

            float r2 = math.max(radius, 1.5f);
            r2 *= r2;
            var q = QC_Targets.Get(em, QT_Targets);
            using var tr = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var fa = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < tr.Length; i++)
            {
                int f = (int)fa[i].Value;
                if (f < 0 || f > 31 || (foreknown & (1u << f)) == 0) continue;
                if (math.distancesq(tr[i].Position.xz, center.xz) <= r2) return true;
            }
            return false;
        }

        /// <summary>True when this edition offers every sect the set needs.</summary>
        public static bool InEdition(SectSet set) => set switch
        {
            SectSet.Alanthor => SectConfig.ClusterInEdition(SectCluster.Alanthor),
            SectSet.Runai    => SectConfig.ClusterInEdition(SectCluster.Runaii),
            SectSet.Feraldis => SectConfig.ClusterInEdition(SectCluster.Feraldis),
            SectSet.Concord  => SectConfig.ClusterInEdition(SectCluster.Runaii)
                                && SectConfig.ClusterInEdition(SectCluster.Feraldis),
            _                => false,
        };

        public static string Name(SectSet set) => set switch
        {
            SectSet.Alanthor => "Sovereign Hour",
            SectSet.Feraldis => "The Breaking Hour",
            SectSet.Runai    => "Foreknown",
            SectSet.Concord  => "Concord",
            _                => "",
        };

        public static string Description(SectSet set) => set switch
        {
            SectSet.Alanthor => "Inside your territory your units attack 15% faster and enemy units move 10% slower.",
            SectSet.Feraldis => "When an enemy building or a curse node falls to you, your units nearby attack and move 20% faster for 15 s.",
            SectSet.Runai    => "Hostile sect powers aimed at your units or buildings take twice as long to wind up.",
            SectSet.Concord  => "Every power of your six sects is cast one level higher, up to III.",
            _                => "",
        };
    }
}
