// HeroLevelSystem.cs
// Awards hero experience for kills and derives the level from it.
// Canon: docs/Design/Heroes.md §1.
//
// Death detection mirrors UnitRankDeathEffectsSystem: poll for units at zero
// HP before DeathSystem clears them, snapshot what is needed, then act. The
// difference is that a kill must pay out EXACTLY ONCE, and a corpse sits at
// zero HP for several frames — so each body is stamped HeroXpAwarded as it is
// counted.
//
// Managed SystemBase, not ISystem: the payout reads the victim's cost out of
// TechCatalog, which is managed data.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class HeroLevelSystem : SystemBase
    {
        #region Cached queries

        // Never CreateEntityQuery on a repeating path — see
        // Core/CachedEntityQuery.cs and the rule in CLAUDE.md.

        static readonly ComponentType[] QT_Heroes =
        {
            ComponentType.ReadOnly<TheWaningBorder.Abilities.UniqueUnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Heroes;

        #endregion

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // ── 1. Collect this frame's fresh corpses ───────────────────
            //
            // A body is only interesting if something killed it and it has not
            // paid out yet. Temporary summons pay nothing at all (Heroes.md
            // §3) — otherwise an opponent farms Lexor's own conjured army.
            var victimPos = new NativeList<float3>(Allocator.Temp);
            var victimFaction = new NativeList<Faction>(Allocator.Temp);
            var victimXp = new NativeList<int>(Allocator.Temp);
            var victimKiller = new NativeList<Entity>(Allocator.Temp);
            var stamp = new NativeList<Entity>(Allocator.Temp);

            foreach (var (health, xform, faction, e) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithNone<HeroXpAwarded, TemporarySummon>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                stamp.Add(e);

                // A HERO falling is the input to the revival price
                // (Heroes.md §4). Recorded here rather than in a death system
                // of its own because this is already the one place that sees
                // every fresh corpse exactly once.
                if (em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(e)
                    && em.HasComponent<HeroLevel>(e))
                {
                    TheWaningBorder.Abilities.HeroRevival.RecordDeath(
                        faction.ValueRO.Value, em.GetComponentData<HeroLevel>(e).Value);
                }

                int xp = XpValueOf(em, e);
                if (xp <= 0) continue;

                victimPos.Add(xform.ValueRO.Position);
                victimFaction.Add(faction.ValueRO.Value);
                victimXp.Add(xp);
                victimKiller.Add(KillerOf(em, e));
            }

            // Stamp every corpse we looked at, paying or not, so it is never
            // reconsidered. Done outside the query — this is a structural
            // change and cannot run during iteration.
            for (int i = 0; i < stamp.Length; i++)
                em.AddComponent<HeroXpAwarded>(stamp[i]);

            // ── 2. Pay it out ───────────────────────────────────────────
            if (victimXp.Length > 0) Award(em, victimPos, victimFaction, victimXp, victimKiller);

            victimPos.Dispose();
            victimFaction.Dispose();
            victimXp.Dispose();
            victimKiller.Dispose();
            stamp.Dispose();

            // ── 3. Derive the level from banked XP ──────────────────────
            //
            // Deriving rather than incrementing on award is what keeps level
            // and XP from ever disagreeing, including after a revival writes
            // a level floor directly.
            foreach (var (lvl, xp) in SystemAPI.Query<RefRW<HeroLevel>, RefRO<HeroExperience>>())
            {
                byte earned = HeroProgressionConfig.LevelForXp(xp.ValueRO.Xp);
                if (earned > lvl.ValueRO.Value) lvl.ValueRW = new HeroLevel { Value = earned };
            }
        }

        /// <summary>
        /// Hand this kill to the hero that landed it, and half of it to every
        /// other allied hero standing close enough to have been part of the
        /// fight (Heroes.md §1.1).
        /// </summary>
        private void Award(EntityManager em,
                           NativeList<float3> pos, NativeList<Faction> victimFaction,
                           NativeList<int> xp, NativeList<Entity> killer)
        {
            var heroQ = QC_Heroes.Get(em, QT_Heroes);
            using var heroes = heroQ.ToEntityArray(Allocator.Temp);
            using var heroXf = heroQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var heroFac = heroQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            if (heroes.Length == 0) return;

            float assistR2 = HeroProgressionConfig.AssistRadius * HeroProgressionConfig.AssistRadius;

            for (int k = 0; k < xp.Length; k++)
            {
                for (int h = 0; h < heroes.Length; h++)
                {
                    // A hero never earns from a friendly death, however it
                    // happened.
                    if (!Alliances.AreHostile(heroFac[h].Value, victimFaction[k])) continue;

                    bool landedIt = killer[k] != Entity.Null && killer[k] == heroes[h];

                    int gain;
                    if (landedIt) gain = xp[k];
                    else
                    {
                        var hp = heroXf[h].Position;
                        float dx = hp.x - pos[k].x, dz = hp.z - pos[k].z;
                        if (dx * dx + dz * dz > assistR2) continue;
                        gain = (int)(xp[k] * HeroProgressionConfig.AssistShare);
                    }
                    if (gain <= 0) continue;

                    AddXp(em, heroes[h], gain);
                }
            }
        }

        private static void AddXp(EntityManager em, Entity hero, int gain)
        {
            int banked = em.HasComponent<HeroExperience>(hero)
                ? em.GetComponentData<HeroExperience>(hero).Xp : 0;
            var next = new HeroExperience { Xp = banked + gain };

            if (em.HasComponent<HeroExperience>(hero)) em.SetComponentData(hero, next);
            else em.AddComponentData(hero, next);

            if (!em.HasComponent<HeroLevel>(hero))
                em.AddComponentData(hero, new HeroLevel { Value = HeroProgressionConfig.MinLevel });
        }

        /// <summary>What this unit's death is worth, from its authored cost.
        /// Zero for anything with no unit id or no entry in the catalog.</summary>
        private static int XpValueOf(EntityManager em, Entity victim)
        {
            if (!em.HasComponent<UnitTypeId>(victim)) return 0;
            string id = em.GetComponentData<UnitTypeId>(victim).Value.ToString();
            if (string.IsNullOrEmpty(id)) return 0;
            UnitDef def = TechCatalog.Unit(id);
            return def == null ? 0 : HeroProgressionConfig.XpForKill(def.cost);
        }

        /// <summary>The entity credited with the kill, or Null. LastAttackerEntity
        /// is already maintained by the combat path for the sect tallies.</summary>
        private static Entity KillerOf(EntityManager em, Entity victim)
        {
            if (!em.HasComponent<LastAttackerEntity>(victim)) return Entity.Null;
            var a = em.GetComponentData<LastAttackerEntity>(victim).Value;
            return a != Entity.Null && em.Exists(a) ? a : Entity.Null;
        }
    }
}
