// File: Assets/Scripts/Systems/Buildings/BuildingUpgradeSystem.cs
// Ticks BuildingUpgrading on every upgradeable building. When Progress
// >= Total, bump BuildingUpgradeState.Level and recompute scaled stats
// from BASE values (NOT current — that way any reapply is idempotent).
//
// Per-building specials:
//   - Hall: BuildingRangedAttack.MaxTargets follows BuildingUpgradeConfig.HallMaxTargets[level].
//   - Barracks: gains BuildingRangedAttack at level 3 (component is added
//       fresh — base cooldown captured here, not in command helper, since
//       Barracks has no attack at level 0).
//   - Hut: PopulationProvider.Amount = base + HutBonusPop[level].
//
// BuildingUpgrading is removed at completion. Training systems +
// BuildingCombatSystem are gated WithNone<BuildingUpgrading> so the
// building goes briefly inert during the upgrade.

using Unity.Entities;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BuildingUpgradeSystem : ISystem
    {
        // Barracks — when it gains its first attack at level 3, use these.
        // Mirrors Hall's stats from Hall.cs (Range 20, Damage 12, Cooldown 2.5s).
        // Barracks is closer to a watchtower than a keep — same range/cooldown,
        // slightly lower damage so it's not a strict Hall upgrade.
        private const float BarracksAttackRange    = 18f;
        private const int   BarracksAttackDamage   = 8;
        private const float BarracksAttackCooldown = 2.5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingUpgrading>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot — applying the level is a structural change for
            // Barracks (adds BuildingRangedAttack), so we mustn't iterate
            // SystemAPI.Query while doing it.
            var query = em.CreateEntityQuery(ComponentType.ReadWrite<BuildingUpgrading>());
            using var ents = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (!em.Exists(e)) continue;

                var up = em.GetComponentData<BuildingUpgrading>(e);
                up.Progress += dt;

                if (up.Progress < up.Total)
                {
                    em.SetComponentData(e, up);
                    continue;
                }

                // ── Apply level ────────────────────────────────────────
                ApplyLevel(em, e, up.TargetLevel);
                em.RemoveComponent<BuildingUpgrading>(e);

                // Debug log so the upgrade pipeline is visible during
                // playtesting. Identifies the building type by tag — same
                // resolution the command helper uses.
                if (em.HasComponent<FactionTag>(e))
                {
                    string id = em.HasComponent<HallTag>(e)     ? "Hall"
                             :  em.HasComponent<BarracksTag>(e) ? "Barracks"
                             :  em.HasComponent<HutTag>(e)      ? "Hut"
                             :                                    "Building";
                    var fac = em.GetComponentData<FactionTag>(e).Value;
                    UnityEngine.Debug.Log(
                        $"[Upgrade] {fac} {id} → L{up.TargetLevel}");
                }
            }
        }

        /// <summary>
        /// Set BuildingUpgradeState.Level = <paramref name="level"/> and
        /// recompute scaled stats from BASE values. Always idempotent —
        /// calling with the same level twice produces the same result.
        /// </summary>
        public static void ApplyLevel(EntityManager em, Entity building, byte level)
        {
            if (!em.HasComponent<BuildingUpgradeState>(building)) return;

            var ups = em.GetComponentData<BuildingUpgradeState>(building);
            ups.Level = level;
            em.SetComponentData(building, ups);

            // Health: scale Max from base, scale current proportionally so
            // the visual HP bar stays at the same percentage. (Mid-combat
            // upgrades don't suddenly heal or kill the building.)
            if (em.HasComponent<Health>(building) && ups.BaseHpMax > 0)
            {
                int newMax = (int)(ups.BaseHpMax * BuildingUpgradeConfig.HpMultiplier[level]);
                var hp = em.GetComponentData<Health>(building);
                float pct = hp.Max > 0 ? (float)hp.Value / hp.Max : 1f;
                hp.Max = newMax;
                hp.Value = Unity.Mathematics.math.clamp((int)(newMax * pct), 0, newMax);
                em.SetComponentData(building, hp);
            }

            // Attack: cooldown scales by AttackCooldownMultiplier. Hall +
            // Barracks-at-lvl-3 also pull from HallMaxTargets where applicable.
            ApplyAttackChanges(em, building, level, ups.BaseAttackCooldown);

            // Hut: +5 pop per level.
            if (em.HasComponent<HutTag>(building) && em.HasComponent<PopulationProvider>(building))
            {
                var pp = em.GetComponentData<PopulationProvider>(building);
                pp.Amount = ups.BasePopulationProvider + BuildingUpgradeConfig.HutBonusPop[level];
                em.SetComponentData(building, pp);
            }
        }

        private static void ApplyAttackChanges(EntityManager em, Entity building,
            byte level, float baseAttackCooldown)
        {
            bool isHall     = em.HasComponent<HallTag>(building);
            bool isBarracks = em.HasComponent<BarracksTag>(building);

            if (isHall && em.HasComponent<BuildingRangedAttack>(building))
            {
                // Already attacks — scale cooldown, set MaxTargets per level.
                var atk = em.GetComponentData<BuildingRangedAttack>(building);
                atk.Cooldown = baseAttackCooldown * BuildingUpgradeConfig.AttackCooldownMultiplier[level];
                atk.MaxTargets = BuildingUpgradeConfig.HallMaxTargets[level];
                em.SetComponentData(building, atk);
            }
            else if (isBarracks && level >= 3)
            {
                // L3 Barracks gains a ranged attack. Apply the L3 attack-rate
                // multiplier to its cooldown for symmetry with Hall — a fully
                // upgraded Barracks fires at the same cadence as a fully
                // upgraded Hall.
                float scaledCooldown = BarracksAttackCooldown
                    * BuildingUpgradeConfig.AttackCooldownMultiplier[level];

                // Gain ranged attack on first arrival at lvl 3. Idempotent —
                // we set the same fields whether the component is new or old.
                if (!em.HasComponent<BuildingRangedAttack>(building))
                {
                    em.AddComponentData(building, new BuildingRangedAttack
                    {
                        Range      = BarracksAttackRange,
                        Damage     = BarracksAttackDamage,
                        Cooldown   = scaledCooldown,
                        Timer      = 0f,
                        MaxTargets = 1,
                    });
                    // Barracks is normally not a damager — give it the standard
                    // ranged damage type so the projectile path treats arrows correctly.
                    if (!em.HasComponent<DamageTypeData>(building))
                        em.AddComponentData(building, new DamageTypeData { Value = DamageType.Ranged });
                }
                else
                {
                    var atk = em.GetComponentData<BuildingRangedAttack>(building);
                    atk.Range      = BarracksAttackRange;
                    atk.Damage     = BarracksAttackDamage;
                    atk.Cooldown   = scaledCooldown;
                    atk.MaxTargets = 1;
                    em.SetComponentData(building, atk);
                }
            }
            // Hut + Barracks below lvl 3 — no attack changes.
        }
    }
}
