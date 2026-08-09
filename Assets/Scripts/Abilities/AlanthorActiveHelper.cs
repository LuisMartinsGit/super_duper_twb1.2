// AlanthorActiveHelper.cs
// The two Alanthor actives that are fired from a BUILDING rather than carried
// by a unit: Choreographed Volleys (Archery Range, faction-wide archer fire
// rate) and Ranging Shot (Siege Yard, the aimed shot). Buildings have no
// UnitAbilities slots, so these do not go through the unit ability engine —
// they are one-shot faction sweeps with a shared per-faction cooldown.
//
// Also the single place that decides which units a freshly trained unit's
// combat passives come from, so unit factories and the research grants agree.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Abilities
{
    public static class AlanthorActiveHelper
    {
        public const float VolleysDuration = 5f;
        public const float VolleysCooldown = 40f;
        public const float VolleysMult = 2f;      // double fire rate
        public const float RangingShotPct = 100f; // +100% on the next shot
        public const float RangingShotWindow = 10f;
        public const float RangingShotCooldown = 45f;

        // Per-faction cooldown clocks. Managed static state, mirroring how
        // FactionResearchState holds researched techs; ticked by
        // AlanthorActiveCooldownSystem.
        private static readonly Dictionary<int, float> _volleysCd = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _rangingCd = new Dictionary<int, float>();

        public static float VolleysCooldownRemaining(Faction f)
            => _volleysCd.TryGetValue((int)f, out var v) ? v : 0f;

        public static float RangingShotCooldownRemaining(Faction f)
            => _rangingCd.TryGetValue((int)f, out var v) ? v : 0f;

        /// <summary>Decrement both clocks. Called once per frame by the cooldown system.</summary>
        public static void Tick(float dt)
        {
            TickMap(_volleysCd, dt);
            TickMap(_rangingCd, dt);
        }

        private static void TickMap(Dictionary<int, float> map, float dt)
        {
            if (map.Count == 0) return;
            var keys = new List<int>(map.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                float v = map[keys[i]] - dt;
                map[keys[i]] = v > 0f ? v : 0f;
            }
        }

        public static void ResetAll() { _volleysCd.Clear(); _rangingCd.Clear(); }

        /// <summary>
        /// Fire Choreographed Volleys: every archer of the faction doubles its fire
        /// rate for 5 s. Returns false if not researched or still cooling.
        /// </summary>
        public static bool TriggerChoreographedVolleys(EntityManager em, Faction faction)
        {
            if (FactionResearchState.Instance == null
                || !FactionResearchState.Instance.HasResearched(faction, "ChoreographedVolleys")) return false;
            if (VolleysCooldownRemaining(faction) > 0f) return false;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<UnitTypeId>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                string id = em.GetComponentData<UnitTypeId>(entities[i]).Value.ToString();
                if (!IsArcher(id)) continue;
                AddOrSet(em, entities[i], new VolleyBuff { Mult = VolleysMult, TimeRemaining = VolleysDuration });
            }

            _volleysCd[(int)faction] = VolleysCooldown;
            return true;
        }

        /// <summary>
        /// Fire Ranging Shot: every PLANTED siege engine of the faction loads an
        /// aimed shot worth +100%. Engines that are still moving are skipped —
        /// the shot is the reward for having stood still, per the design.
        /// </summary>
        public static bool TriggerRangingShot(EntityManager em, Faction faction)
        {
            if (FactionResearchState.Instance == null
                || !FactionResearchState.Instance.HasResearched(faction, "RangingShot")) return false;
            if (RangingShotCooldownRemaining(faction) > 0f) return false;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<UnitTypeId>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int armed = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                string id = em.GetComponentData<UnitTypeId>(entities[i]).Value.ToString();
                if (!IsSiege(id)) continue;

                // Only engines that have actually been standing get the shot. The
                // Siege Screens stillness clock doubles as the "planted" signal
                // when the faction owns it; without it, arm unconditionally.
                if (em.HasComponent<SiegeScreens>(entities[i])
                    && em.GetComponentData<SiegeScreens>(entities[i]).Ready == 0) continue;

                AddOrSet(em, entities[i], new NextShotBonus { Pct = RangingShotPct, TimeRemaining = RangingShotWindow });
                armed++;
            }

            if (armed == 0) return false;
            _rangingCd[(int)faction] = RangingShotCooldown;
            return true;
        }

        public static bool IsGarrisonInfantry(string id)
            => id == "Spearman" || id == "Alanthor_Swordsman"
            || id == "Alanthor_Nobleman" || id == "Alanthor_Sentinel";

        public static bool IsArcher(string id)
            => id == "Archer" || id == "Alanthor_Crossbowman" || id == "Alanthor_Longbowman";

        public static bool IsSiege(string id)
            => id == "Alanthor_Ballista" || id == "Alanthor_BatteringRam" || id == "Alanthor_Trebuchet";

        /// <summary>
        /// Attach the researched combat passives to a unit at spawn. Called by the
        /// unit factories so newly trained units match the ones the research
        /// sweep already stamped.
        /// </summary>
        public static void ApplySpawnPassives(EntityManager em, Entity e, Faction faction, string unitId)
        {
            var rs = FactionResearchState.Instance;
            if (rs == null) return;

            if (IsGarrisonInfantry(unitId))
            {
                if (rs.HasResearched(faction, "Charge"))
                    AddOrSet(em, e, new FirstStrike { Pct = 30f, Ready = 1 });
                if (rs.HasResearched(faction, "ShieldWall"))
                    AddOrSet(em, e, new ShieldWallState { Pct = 30f });
            }
            else if (IsArcher(unitId))
            {
                if (rs.HasResearched(faction, "DeployStakes"))
                    AddOrSet(em, e, new StakesState { Pct = 50f });
            }
            else if (IsSiege(unitId))
            {
                if (rs.HasResearched(faction, "SiegeScreens"))
                    AddOrSet(em, e, new SiegeScreens { Pct = 50f });
            }
            else if (unitId == "Litharch" && rs.HasResearched(faction, "FieldHospital"))
            {
                AbilityAssignment.AddAbility(em, e, AbilityCatalog.IndexOf("Deploy Field Hospital"));
            }
        }

        private static void AddOrSet<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (!em.Exists(e)) return;
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }
    }
}
