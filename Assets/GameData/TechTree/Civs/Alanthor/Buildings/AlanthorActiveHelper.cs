// AlanthorActiveHelper.cs
// Ranging Shot (Siege Yard, the aimed shot) — an Alanthor active fired from a
// BUILDING rather than carried by a unit. Buildings have no UnitAbilities
// slots, so it does not go through the unit ability engine: it is a one-shot
// faction sweep with a per-faction cooldown.
//
// Choreographed Volleys used to live here too. It is a UNIT active now — a
// ranged unit calls the cadence — so it went to the ability engine where the
// rest of the unit actives are.
//
// Also the single place that decides which units a freshly trained unit's
// combat passives come from, so unit factories and the research grants agree.

using System.Collections.Generic;
using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Abilities
{
    public static class AlanthorActiveHelper
    {

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagUnitTypeIdFactionTag =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_UnitTagUnitTypeIdFactionTag;

        #endregion
        public const float RangingShotPct = 100f; // +100% on the next shot
        public const float RangingShotWindow = 10f;
        public const float RangingShotCooldown = 45f;

        // Per-faction cooldown clocks. Managed static state, mirroring how
        // FactionResearchState holds researched techs; ticked by
        // AlanthorActiveCooldownSystem.
        private static readonly Dictionary<int, float> _rangingCd = new Dictionary<int, float>();

        public static float RangingShotCooldownRemaining(Faction f)
            => _rangingCd.TryGetValue((int)f, out var v) ? v : 0f;

        /// <summary>Decrement both clocks. Called once per frame by the cooldown system.</summary>
        public static void Tick(float dt)
        {
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

        public static void ResetAll() { _rangingCd.Clear(); }

        // Choreographed Volleys no longer has a faction-wide building
        // trigger: it is a unit active now (AbilityCatalog
        // "Choreographed Volleys"), cast by the ranged line that fires it.

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

            var query = QC_UnitTagUnitTypeIdFactionTag.Get(em, QT_UnitTagUnitTypeIdFactionTag);
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
            => id == "Alanthor_Archer" || id == "Archer" || id == "Alanthor_Crossbowman" || id == "Alanthor_Longbowman";

        public static bool IsSiege(string id)
            => id == "Alanthor_Ballista" || id == "Alanthor_BatteringRam" || id == "Alanthor_Trebuchet";

        public static bool IsCavalry(string id)
            => id == "Alanthor_Outrider" || id == "Alanthor_Cataphract"
            || id == "Outrider" || id == "Cataphract";

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
                    AddOrSet(em, e, new StakesState
                    {
                        Pct = 50f,
                        Ready = 1,
                        ReflectPct = AlanthorPassiveTuning.StakesReflectPct,
                    });
                // Choreographed Volleys is cast BY a ranged unit, so a unit
                // trained after the research has to spawn holding it.
                if (rs.HasResearched(faction, "ChoreographedVolleys"))
                    AbilityAssignment.AddAbility(em, e,
                        AbilityCatalog.IndexOf("Choreographed Volleys"));
            }
            else if (IsSiege(unitId))
            {
                if (rs.HasResearched(faction, "SiegeScreens"))
                    AddOrSet(em, e, new SiegeScreens { Pct = 50f });
            }
            else if (IsCavalry(unitId))
            {
                // The Royal Stable's charge — same passive, cavalry roster.
                if (rs.HasResearched(faction, "CavalryCharge"))
                    AddOrSet(em, e, new FirstStrike { Pct = 30f, Ready = 1 });
            }
            // Not an Alanthor passive: Field Hospital is the Sect of Renewal's
            // research, so any culture that adopts Renewal arms its Litharchs.
            // It rides in this dispatcher because this is the one hook every
            // trained unit passes through - see TrainingSystem.
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
