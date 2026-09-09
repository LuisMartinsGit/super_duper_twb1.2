// PledgeArmy.cs
// "Honour thy Pledge" — the temporary army King Lexor calls in.
// Canon: docs/Design/Heroes.md §3.
//
//   The oath runs both ways. Lexor calls it in.
//
// Everything about the summon scales with the king's level, and the scaling is
// three separate axes rather than one: HOW MANY men answer, HOW GOOD they are
// (they arrive already veteran), and HOW LONG they stay. That is what makes a
// level-10 call qualitatively different from a level-4 one instead of merely
// larger.
//
// Filed under the caster's own Abilities/ folder per the co-location rule in
// CLAUDE.md: an ability lives with whatever owns it, and the thing it conjures
// files with the ability (the Field Hospital sets the precedent).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static class PledgeArmy
    {
        /// <summary>The sworn. Alanthor's line infantry and its bows — the men
        /// an Alanthor king would actually have a pledge from.</summary>
        private const string SwordsmanId = "Alanthor_Swordsman";
        private const string ArcherId    = "Alanthor_Archer";

        /// <summary>Radius of the ring the pledged form up on. Wide enough not
        /// to spawn inside the king, tight enough that they arrive as a body
        /// rather than a scatter.</summary>
        private const float SpawnRing = 6f;

        /// <summary>One in this many of the answering men carries a bow, so
        /// the line always outnumbers the archers.</summary>
        private const int ArcherEvery = 3;

        // ── The three scaling axes (Heroes.md §3) ───────────────────────

        /// <summary>Men who answer: one per level of the king.</summary>
        public static int SoldiersFor(int heroLevel) => math.max(0, heroLevel);

        /// <summary>Archers among them; the rest carry swords.</summary>
        public static int ArchersFor(int heroLevel) => SoldiersFor(heroLevel) / ArcherEvery;

        /// <summary>Veteran rank they arrive at — one step every two levels
        /// above the unlock, capped at 4. This is the "upgrades" half of the
        /// ability.</summary>
        public static byte RankFor(int heroLevel)
        {
            int r = 1 + (heroLevel - HonourThyPledge.UnlockLevel) / 2;
            return (byte)math.clamp(r, 1, 4);
        }

        /// <summary>Seconds they stay: 30 at the unlock, +5 per level after.</summary>
        public static float DurationFor(int heroLevel)
            => 30f + 5f * math.max(0, heroLevel - HonourThyPledge.UnlockLevel);

        /// <summary>
        /// Call in the pledge around <paramref name="caster"/>. Returns how
        /// many men answered.
        ///
        /// The king's level is read off the caster, so a hero revived at a
        /// lower level (Heroes.md §4) immediately calls a smaller army — the
        /// ability has no memory of what he used to be.
        /// </summary>
        public static int Summon(EntityManager em, Entity caster)
        {
            if (caster == Entity.Null || !em.Exists(caster)) return 0;
            if (!em.HasComponent<LocalTransform>(caster)) return 0;

            int level = em.HasComponent<HeroLevel>(caster)
                ? em.GetComponentData<HeroLevel>(caster).Value
                : HeroProgressionConfig.MinLevel;

            int total = SoldiersFor(level);
            if (total <= 0) return 0;

            int archers = ArchersFor(level);
            byte rank = RankFor(level);
            float ttl = DurationFor(level);

            float3 origin = em.GetComponentData<LocalTransform>(caster).Position;
            Faction faction = em.HasComponent<FactionTag>(caster)
                ? em.GetComponentData<FactionTag>(caster).Value : Faction.Blue;

            int made = 0;
            for (int i = 0; i < total; i++)
            {
                // Even spacing round the ring, derived from the index alone —
                // no RNG draw, so every lockstep peer forms the same shape in
                // the same order.
                float angle = (math.PI * 2f) * i / total;
                float sx = origin.x + math.cos(angle) * SpawnRing;
                float sz = origin.z + math.sin(angle) * SpawnRing;
                var pos = new float3(sx, TerrainUtility.GetHeight(sx, sz), sz);

                // Archers last, so the melee take the outward-facing slots
                // first and the bows land behind them on a partial ring.
                string id = i >= total - archers ? ArcherId : SwordsmanId;

                Entity e = UnitFactory.Create(em, id, pos, faction);
                if (e == Entity.Null) continue;
                MakeTemporary(em, e, rank, ttl);
                made++;
            }

            if (made > 0)
            {
                SimSignals.Ping(origin, SimPingKind.Combat, SpawnRing);
                TWBLog.Log($"[HonourThyPledge] {faction} King Lexor (lv {level}) called " +
                           $"{made} sworn — rank {rank}, {ttl:0}s.");
            }
            return made;
        }

        /// <summary>
        /// Turn a freshly trained unit into a pledged one.
        ///
        /// Two things have to come OFF as well as on. The population cost goes,
        /// because the ability is a burst of tempo and not a way past the
        /// population ceiling; and the unit is marked TemporarySummon, which is
        /// what stops it earning or granting hero experience — without that,
        /// Lexor levels off men he paid nothing for, and his opponent farms
        /// them right back.
        /// </summary>
        private static void MakeTemporary(EntityManager em, Entity e, byte rank, float ttl)
        {
            if (em.HasComponent<PopulationCost>(e))
                em.RemoveComponent<PopulationCost>(e);

            if (em.HasComponent<UnitRank>(e))
                em.SetComponentData(e, new UnitRank { Value = rank });
            else
                em.AddComponentData(e, new UnitRank { Value = rank });

            em.AddComponentData(e, new TemporarySummon { TimeToLive = ttl });
        }
    }
}
