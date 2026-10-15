// AlanthorSectEconomy.cs
// The economic hooks of the Alanthor sects (docs/Design/Sects.md §4):
//
//   Embargo (Antiquity B)       an embargoed building (SectShutdown) earns nothing
//   Overtime (Prosperity B)     a boosted building works and earns faster
//   Treasury (Prosperity bldg)  while one stands, the faction's extractors earn more
//   Reliquary (Antiquity bldg)  while one stands, the faction's sect powers recharge faster
//   Plenty (Prosperity ★)       reads the territory rate recorded here
//
// TerritoryIncomeSystem multiplies every extractor by ExtractorMultiplier and
// records each faction's territory rate; ProductionQueueSystem scales its
// clock by ProductionSpeed; SectActivePowerHelper.EffectiveCooldown applies
// the Reliquary. Replicated state only, so every peer pays the same.

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public static class AlanthorSectEconomy
    {
        /// <summary>Treasury: extractor income bonus while one stands.</summary>
        public const float TreasuryIncomeBonus = 0.25f;

        /// <summary>Reliquary: sect power cooldown scale while one stands.</summary>
        public const float ReliquaryCooldownScale = 0.85f;

        /// <summary>
        /// Per-building multiplier on what an extractor earns: 0 while
        /// embargoed, Overtime's boost while it runs, the Treasury's bonus
        /// while its owner holds one standing.
        /// </summary>
        public static float ExtractorMultiplier(EntityManager em, Entity building)
        {
            if (TransientState.Active<SectShutdown>(em, building)) return 0f;
            float m = 1f;
            if (TransientState.Active<OvertimeBoost>(em, building))
                m *= em.GetComponentData<OvertimeBoost>(building).Multiplier;
            if (em.HasComponent<FactionTag>(building)
                && SectQuery.HasStandingSectBuilding(em, em.GetComponentData<FactionTag>(building).Value,
                       SectConfig.BuildingIdFor(SectConfig.Prosperity)))
                m *= 1f + TreasuryIncomeBonus;
            return m;
        }

        /// <summary>Arrow Slits (Stonehold): range and damage multipliers.</summary>
        public const float ArrowSlitsRange = 1.2f;
        public const float ArrowSlitsDamage = 1.25f;

        /// <summary>Does <paramref name="building"/> — a wall piece or a tower —
        /// shoot through Arrow Slits? True while its owner holds a standing
        /// Stonehold.</summary>
        public static bool ArrowSlits(EntityManager em, Entity building, Faction owner)
        {
            if (!em.HasComponent<WallTag>(building) && !em.HasComponent<WatchTowerTag>(building)
                && !em.HasComponent<WallTowerTag>(building)) return false;
            return SectQuery.HasStandingSectBuilding(em, owner, SectConfig.BuildingIdFor(SectConfig.Fortitude));
        }

        /// <summary>How fast a building's production queue runs (Overtime).</summary>
        public static float ProductionSpeed(EntityManager em, Entity building)
            => TransientState.Active<OvertimeBoost>(em, building)
                ? em.GetComponentData<OvertimeBoost>(building).Multiplier : 1f;

        /// <summary>Sect power cooldown scale for a faction (the Reliquary).</summary>
        public static float CooldownScale(EntityManager em, Faction faction)
            => SectQuery.HasStandingSectBuilding(em, faction, SectConfig.BuildingIdFor(SectConfig.Antiquity))
                ? ReliquaryCooldownScale : 1f;

        // ── The territory rate, recorded by TerritoryIncomeSystem ─────────

        private static readonly float[] _rate = new float[4 * 32];
        private static readonly float[] _next = new float[4 * 32];

        /// <summary>TerritoryIncomeSystem, start of a paying tick.</summary>
        public static void BeginRate() => System.Array.Clear(_next, 0, _next.Length);

        /// <summary>TerritoryIncomeSystem, per territory paid (per minute).</summary>
        public static void AddRate(int faction, float supplies, float iron, float veilstone, float veilsteel)
        {
            if (faction < 0 || faction >= 32) return;
            _next[faction * 4 + 0] += supplies;
            _next[faction * 4 + 1] += iron;
            _next[faction * 4 + 2] += veilstone;
            _next[faction * 4 + 3] += veilsteel;
        }

        /// <summary>TerritoryIncomeSystem, end of a paying tick.</summary>
        public static void EndRate() => System.Array.Copy(_next, _rate, _rate.Length);

        /// <summary>What <paramref name="seconds"/> of this faction's territory
        /// income is worth right now (Plenty).</summary>
        public static Cost IncomeOver(Faction faction, float seconds)
        {
            int f = (int)faction;
            if (f < 0 || f >= 32) return default;
            float k = seconds / 60f;
            return Cost.Of(
                supplies: UnityEngine.Mathf.FloorToInt(UnityEngine.Mathf.Max(0f, _rate[f * 4 + 0]) * k),
                iron: UnityEngine.Mathf.FloorToInt(UnityEngine.Mathf.Max(0f, _rate[f * 4 + 1]) * k),
                veilstone: UnityEngine.Mathf.FloorToInt(UnityEngine.Mathf.Max(0f, _rate[f * 4 + 2]) * k),
                veilsteel: UnityEngine.Mathf.FloorToInt(UnityEngine.Mathf.Max(0f, _rate[f * 4 + 3]) * k));
        }
    }
}
