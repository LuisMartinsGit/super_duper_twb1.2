// FeraldisPalisadeSystem.cs
// THE FERALDIS WOODEN WALL (docs/Design/Age_1_Feraldis.md § Walls,
// docs/Design/Age_0.md § Palisade; 2026-10-13).
//
// Feraldis never leave timber: at age-up every palisade they own is raised
// to their ONE cultured wall level — the Wooden Wall, the level SO
// Civs/Feraldis/Buildings/Palisade/Palisade_Lvl1 — automatically, with no
// research and no upgrade, and there is no level above it. Its numbers are
// that SO's (hpMultiplier); nothing here holds a balance value.
//
// Two halves, one per kind of piece:
//   * the palisade HUB carries BuildingUpgradeable, so the age-up auto-L1
//     (BuildingCultureAutoLevelSystem -> BuildingUpgradeSystem.ApplyLevel)
//     applies the level to it like any building — its ladder id is
//     "Palisade" (UpgradeBuildingCommandHelper.ResolveBuildingId);
//   * the curtain modules, gates and wall towers have no upgrade state, so
//     THIS system scales them: every Feraldis-owned palisade piece with
//     Health is brought to (its own Max) x hpMultiplier once, and stamped
//     with the Max it was given. A piece whose Max was reset since — a module
//     converted into a gate or a tower takes that structure's own SO HP — no
//     longer matches its stamp and is scaled again, so conversions need no
//     hook of their own. Pieces raised after age-up are scaled the same way
//     within one scan (a site under construction keeps building toward the
//     scaled Max).
//
// Deterministic: lockstep cadence, integer HP, replicated state only.

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>The Max HP the Feraldis Wooden Wall level last gave this palisade
/// piece (FeraldisPalisadeSystem). A Max that no longer matches means the
/// piece was rebuilt as something else and is scaled again.</summary>
public struct PalisadeLevelApplied : IComponentData
{
    public int AppliedMax;
}

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisPalisadeSystem : SystemBase
    {
        /// <summary>Seconds between scans — engine cadence, not balance.</summary>
        private const float ScanInterval = 1f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Pieces =
        {
            ComponentType.ReadOnly<PalisadeTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadWrite<Health>(),
            ComponentType.Exclude<WallHubTag>(),
        };
        static CachedEntityQuery QC_Pieces;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4).
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, ScanInterval)) return;
            var em = EntityManager;
            var q = QC_Pieces.Get(em, QT_Pieces);
            if (q.IsEmpty) return;

            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var stamp = new NativeList<Entity>(Allocator.Temp);
            var stampMax = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                float mult = TheWaningBorder.Entities.WallTiers.PalisadeHpMultiplier(em, facs[i].Value);
                if (mult == 1f) continue;
                var e = ents[i];
                var hp = em.GetComponentData<Health>(e);
                if (hp.Max <= 0) continue;
                if (em.HasComponent<PalisadeLevelApplied>(e)
                    && em.GetComponentData<PalisadeLevelApplied>(e).AppliedMax == hp.Max) continue;
                // A piece already dead or collapsing is DeathSystem's.
                if (hp.Value <= 0 || em.HasComponent<BuildingCollapseState>(e)) continue;

                int newMax = math.max(1, (int)math.round(hp.Max * mult));
                int newValue;
                if (em.HasComponent<UnderConstruction>(e))
                    newValue = hp.Value;   // the site keeps building toward the new Max
                else
                {
                    // Keep the damage taken proportional: a raised wall does
                    // not heal, and a breached one stays breached.
                    float frac = math.saturate((float)hp.Value / hp.Max);
                    newValue = math.max(1, (int)math.round(newMax * frac));
                }
                em.SetComponentData(e, new Health { Max = newMax, Value = math.min(newValue, newMax) });
                stamp.Add(e);
                stampMax.Add(newMax);
            }

            // Structural adds after the reads: one pass, no iteration open.
            for (int i = 0; i < stamp.Length; i++)
            {
                var stampData = new PalisadeLevelApplied { AppliedMax = stampMax[i] };
                if (em.HasComponent<PalisadeLevelApplied>(stamp[i])) em.SetComponentData(stamp[i], stampData);
                else em.AddComponentData(stamp[i], stampData);
            }
            // Re-clad: the raised piece draws its culture's wall set
            // (WallTiers.ArtLevelOf / WallModuleArt.asset cultureSets) — the
            // instanced pieces (cells, gates, towers) only read that when
            // their view is built, exactly as the stone wall's level applier
            // respawns what it promotes. The curved curtain re-clads itself.
            // A standing piece raised by the age-up re-clads with the
            // level-up wave; a site still rising (a palisade placed after the
            // age-up, stamped on its first scan) just respawns.
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null)
                for (int i = 0; i < stamp.Length; i++)
                {
                    var s = stamp[i];
                    if (!em.Exists(s)) continue;
                    if (em.HasComponent<UnderConstruction>(s)) spawn.ForceRespawn(s);
                    else spawn.ForceRespawnWithTransition(s,
                        flourish: em.HasComponent<WallTowerTag>(s) || em.HasComponent<WallGateTag>(s));
                }
            stamp.Dispose();
            stampMax.Dispose();
        }
    }
}
