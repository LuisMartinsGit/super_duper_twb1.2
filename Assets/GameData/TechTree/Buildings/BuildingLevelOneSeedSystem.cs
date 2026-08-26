// BuildingLevelOneSeedSystem.cs
// SIM-side stamping of culture level 1 on completed buildings.
//
// A completed building of a faction that has chosen a culture is level 1 —
// design canon ("construction always Lv0, completes into culture Lv1").
// This used to be stamped by PresentationSpawnSystem when the building's
// VIEW spawned, which is render-paced: on two lockstep peers the stamp (and
// the Health.Max / attack-cooldown / population writes ApplyLevel performs)
// landed on different ticks, and Health is part of the desync checksum
// (found in the 2026-08-16 determinism sweep). Here the stamp happens inside
// the tick-stepped SimulationSystemGroup, on the first tick the building is
// both finished and cultured — the same tick on every peer.
//
// The presentation now only READS BuildingUpgradeState for its variant
// choice; a view that spawns a tick early simply shows Lv0 until the next
// variant scan.

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BuildingLevelOneSeedSystem : SystemBase
    {
        private EntityQuery _unstamped;

        protected override void OnCreate()
        {
            // Temples are excluded: their ladder lives in TempleLevel (set by
            // TempleUpgradeSystem). Stamping BuildingUpgradeState on a temple
            // gave BuildingPrefabSwapSystem two disagreeing level sources and
            // made the level-up flourish replay every scan, forever.
            _unstamped = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, FactionTag>()
                .WithNone<UnderConstruction, BuildingUpgradeState, TempleLevel>()
                .Build(this);
            RequireForUpdate(_unstamped);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // COMPLETED cultures only. FactionColors flips at click time so
            // unit tints preview instantly, but level-1 stamping (and the
            // building visuals that follow it) must wait for the 60s age-up
            // research to finish — CultureConfig reads Hall FactionProgress.
            System.Span<byte> completed = stackalloc byte[8];
            bool anyCulture = false;
            for (int f = 0; f < 8; f++)
            {
                completed[f] = CultureConfig.GetCompletedCulture(em, (Faction)f);
                if (completed[f] != Cultures.None) anyCulture = true;
            }
            if (!anyCulture) return;

            using var ents = _unstamped.ToEntityArray(Allocator.Temp);
            using var facs = _unstamped.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= 8 || completed[fi] == Cultures.None) continue;

                var e = ents[i];

                // Capture base stats once, exactly as the manual upgrade
                // command does, so a later L1->L2/L3 upgrade recomputes
                // idempotently from the original values.
                int baseHp = em.HasComponent<Health>(e)
                    ? em.GetComponentData<Health>(e).Max : 0;
                float baseAtkCd = em.HasComponent<BuildingRangedAttack>(e)
                    ? em.GetComponentData<BuildingRangedAttack>(e).Cooldown : 0f;
                int basePop = em.HasComponent<TheWaningBorder.Economy.PopulationProvider>(e)
                    ? em.GetComponentData<TheWaningBorder.Economy.PopulationProvider>(e).Amount : 0;

                em.AddComponentData(e, new BuildingUpgradeState
                {
                    Level                  = 0,
                    BaseHpMax              = baseHp,
                    BaseAttackCooldown     = baseAtkCd,
                    BasePopulationProvider = basePop,
                });

                BuildingUpgradeSystem.ApplyLevel(em, e, 1);
            }
        }
    }
}
