// FactionSectState.cs
// Managed singleton tracking adopted sects and their multiplier effects per faction
// Part of: Economy/

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Tracks which sects each faction has adopted and stores computed multiplier
    /// values from sect passives and synergy bonuses.
    /// MonoBehaviour singleton - lives on the RuntimeManagers GameObject.
    ///
    /// Pattern: FactionResearchState (same lifecycle, same Dictionary storage).
    ///
    /// Used by:
    /// - SectEffectSystem: to apply passives on adoption
    /// - UI (SectAdoptionPanel): to display adopted sects
    /// - Game systems: to query multiplier values (research speed, income, etc.)
    /// </summary>
    public class FactionSectState : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ═══════════════════════════════════════════════════════════════════════

        public static FactionSectState Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DATA
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adopted sects per faction. Key = (int)Faction, Value = set of sect IDs.
        /// </summary>
        private readonly Dictionary<int, HashSet<string>> _adoptedByFaction = new();

        /// <summary>
        /// Multiplier values per faction. Key = (int)Faction.
        /// Recomputed whenever a sect is adopted or temple level changes.
        /// </summary>
        private readonly Dictionary<int, SectMultipliers> _multipliersByFaction = new();

        /// <summary>
        /// Fired when a sect is successfully adopted. Parameters: (faction, sectId).
        /// Subscribed to by SectEffectSystem to apply entity-level effects.
        /// </summary>
        public event Action<Faction, string> OnSectAdopted;

        // ═══════════════════════════════════════════════════════════════════════
        // MULTIPLIER STRUCT
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// All computed multiplier values from sect passives + synergy bonuses.
        /// Systems query these to modify their behavior.
        /// </summary>
        public struct SectMultipliers
        {
            /// <summary>Multiplier for research speed (default 1.0)</summary>
            public float ResearchSpeed;

            /// <summary>Multiplier for build speed (default 1.0)</summary>
            public float BuildSpeed;

            /// <summary>Multiplier for all income sources (default 1.0)</summary>
            public float AllIncome;

            /// <summary>Multiplier for wall enclosure income (default 1.0)</summary>
            public float WallIncome;

            /// <summary>Multiplier for trade income (default 1.0)</summary>
            public float TradeIncome;

            /// <summary>Multiplier for damage vs Crystal entities (default 1.0)</summary>
            public float DamageVsCrystal;

            /// <summary>Multiplier for ranged damage (default 1.0)</summary>
            public float RangedDamage;

            /// <summary>Multiplier for attack speed (default 1.0)</summary>
            public float AttackSpeed;

            /// <summary>Multiplier for building HP (default 1.0)</summary>
            public float BuildingHP;

            /// <summary>Multiplier for vault interest rate (default 1.0)</summary>
            public float VaultInterest;

            /// <summary>Bonus to fog vision range (additive, default 0.0)</summary>
            public float FogVisionBonus;

            /// <summary>Bonus melee damage multiplier (default 1.0)</summary>
            public float MeleeDamage;

            /// <summary>Bonus ranged accuracy (additive, default 0.0)</summary>
            public float RangedAccuracy;

            /// <summary>Panic chance on hit (default 0.0)</summary>
            public float PanicChance;

            /// <summary>Control chance on hit (default 0.0)</summary>
            public float ControlChance;

            /// <summary>Whether Renewal conditional income is active (walls full HP)</summary>
            public bool HasRenewal;

            /// <summary>Renewal income bonus value (scaled)</summary>
            public float RenewalIncomeBonus;

            /// <summary>Create default multipliers (all 1.0 or 0.0)</summary>
            public static SectMultipliers Default => new SectMultipliers
            {
                ResearchSpeed = 1.0f,
                BuildSpeed = 1.0f,
                AllIncome = 1.0f,
                WallIncome = 1.0f,
                TradeIncome = 1.0f,
                DamageVsCrystal = 1.0f,
                RangedDamage = 1.0f,
                AttackSpeed = 1.0f,
                BuildingHP = 1.0f,
                VaultInterest = 1.0f,
                FogVisionBonus = 0.0f,
                MeleeDamage = 1.0f,
                RangedAccuracy = 0.0f,
                PanicChance = 0.0f,
                ControlChance = 0.0f,
                HasRenewal = false,
                RenewalIncomeBonus = 0.0f
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC API - ADOPTION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check if a faction has adopted a specific sect.
        /// </summary>
        public bool HasAdopted(Faction faction, string sectId)
        {
            int key = (int)faction;
            return _adoptedByFaction.TryGetValue(key, out var set) && set.Contains(sectId);
        }

        /// <summary>
        /// Get the RP cost to adopt a sect for a given faction.
        /// Returns AffinityCost (1) if the sect's affinity matches the faction's culture,
        /// otherwise NonAffinityCost (3).
        /// </summary>
        public int GetAdoptionCost(Faction faction, string sectId)
        {
            byte factionCulture = FactionColors.GetFactionCulture(faction);
            byte sectCulture = SectConfig.GetAffinityCulture(sectId);

            if (factionCulture == Cultures.None || sectCulture == Cultures.None)
                return SectConfig.NonAffinityCost;

            return factionCulture == sectCulture
                ? SectConfig.AffinityCost
                : SectConfig.NonAffinityCost;
        }

        /// <summary>
        /// Attempt to adopt a sect for a faction.
        /// Checks RP availability, deducts cost, adds to adopted set, fires event.
        /// </summary>
        /// <returns>True if adoption succeeded.</returns>
        public bool TryAdopt(Faction faction, string sectId)
        {
            // Already adopted?
            if (HasAdopted(faction, sectId))
            {
                Debug.LogWarning($"[FactionSectState] {faction} already adopted {sectId}");
                return false;
            }

            // Check RP
            int cost = GetAdoptionCost(faction, sectId);
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            var em = world.EntityManager;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<ReligionPoints>(bank)) return false;

            var rp = em.GetComponentData<ReligionPoints>(bank);
            if (rp.Value < cost)
            {
                Debug.Log($"[FactionSectState] {faction} cannot afford {sectId}: need {cost} RP, have {rp.Value}");
                return false;
            }

            // Deduct RP
            rp.Value -= cost;
            em.SetComponentData(bank, rp);

            // Add to adopted set
            int key = (int)faction;
            if (!_adoptedByFaction.TryGetValue(key, out var set))
            {
                set = new HashSet<string>();
                _adoptedByFaction[key] = set;
            }
            set.Add(sectId);

            Debug.Log($"[FactionSectState] {faction} adopted {SectConfig.GetDisplayName(sectId)} " +
                      $"(cost: {cost} RP, remaining: {rp.Value} RP)");

            // Recompute multipliers
            RecomputeMultipliers(faction);

            // Fire event
            OnSectAdopted?.Invoke(faction, sectId);

            return true;
        }

        /// <summary>
        /// Get all adopted sect IDs for a faction.
        /// </summary>
        public IReadOnlyCollection<string> GetAdoptedSects(Faction faction)
        {
            int key = (int)faction;
            if (_adoptedByFaction.TryGetValue(key, out var set))
                return set;
            return Array.Empty<string>();
        }

        /// <summary>
        /// Get the count of adopted sects for a faction.
        /// </summary>
        public int GetAdoptedCount(Faction faction)
        {
            int key = (int)faction;
            return _adoptedByFaction.TryGetValue(key, out var set) ? set.Count : 0;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC API - MULTIPLIER QUERIES
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Get all multipliers for a faction (returns default if none set).</summary>
        public SectMultipliers GetMultipliers(Faction faction)
        {
            int key = (int)faction;
            return _multipliersByFaction.TryGetValue(key, out var mults)
                ? mults
                : SectMultipliers.Default;
        }

        public float GetResearchSpeedMult(Faction f) => GetMultipliers(f).ResearchSpeed;
        public float GetBuildSpeedMult(Faction f) => GetMultipliers(f).BuildSpeed;
        public float GetIncomeMultiplier(Faction f) => GetMultipliers(f).AllIncome;
        public float GetWallIncomeMult(Faction f) => GetMultipliers(f).WallIncome;
        public float GetTradeIncomeMult(Faction f) => GetMultipliers(f).TradeIncome;
        public float GetDamageVsCrystalMult(Faction f) => GetMultipliers(f).DamageVsCrystal;
        public float GetRangedDamageMult(Faction f) => GetMultipliers(f).RangedDamage;
        public float GetAttackSpeedMult(Faction f) => GetMultipliers(f).AttackSpeed;
        public float GetBuildingHPMult(Faction f) => GetMultipliers(f).BuildingHP;
        public float GetVaultInterestMult(Faction f) => GetMultipliers(f).VaultInterest;
        public float GetFogVisionBonus(Faction f) => GetMultipliers(f).FogVisionBonus;
        public float GetMeleeDamageMult(Faction f) => GetMultipliers(f).MeleeDamage;
        public float GetRangedAccuracyBonus(Faction f) => GetMultipliers(f).RangedAccuracy;
        public float GetPanicChance(Faction f) => GetMultipliers(f).PanicChance;
        public float GetControlChance(Faction f) => GetMultipliers(f).ControlChance;

        // ═══════════════════════════════════════════════════════════════════════
        // MULTIPLIER RECOMPUTATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recompute all multiplier values for a faction based on adopted sects,
        /// temple level, and synergy bonuses. Called on sect adoption and temple level-up.
        /// </summary>
        public void RecomputeMultipliers(Faction faction)
        {
            int key = (int)faction;
            var mults = SectMultipliers.Default;

            if (!_adoptedByFaction.TryGetValue(key, out var adopted) || adopted.Count == 0)
            {
                _multipliersByFaction[key] = mults;
                return;
            }

            // Get temple level for scaling
            float scaling = GetTempleScaling(faction);

            // Apply each adopted sect's passive
            foreach (var sectId in adopted)
            {
                ApplySectPassiveToMultipliers(ref mults, sectId, scaling);
            }

            // Check synergy pairs
            foreach (var pair in SectConfig.SynergyPairs)
            {
                if (adopted.Contains(pair.SectA) && adopted.Contains(pair.SectB))
                {
                    ApplySynergyToMultipliers(ref mults, pair, scaling);
                }
            }

            _multipliersByFaction[key] = mults;

            Debug.Log($"[FactionSectState] Recomputed multipliers for {faction}: " +
                      $"ResearchSpeed={mults.ResearchSpeed:F2}, BuildSpeed={mults.BuildSpeed:F2}, " +
                      $"Income={mults.AllIncome:F2}, MeleeDmg={mults.MeleeDamage:F2}, " +
                      $"DmgVsCrystal={mults.DamageVsCrystal:F2}");
        }

        /// <summary>
        /// Recompute multipliers for all factions. Called when temple levels change.
        /// </summary>
        public void RecomputeAllMultipliers()
        {
            foreach (var kvp in _adoptedByFaction)
            {
                RecomputeMultipliers((Faction)kvp.Key);
            }
        }

        private float GetTempleScaling(Faction faction)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return 1.0f;

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleTag>(),
                ComponentType.ReadOnly<TempleLevel>(),
                ComponentType.ReadOnly<FactionTag>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var levels = query.ToComponentDataArray<TempleLevel>(Allocator.Temp);

            int maxLevel = 1;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction && levels[i].Level > maxLevel)
                    maxLevel = levels[i].Level;
            }

            return SectConfig.GetTempleScaling(maxLevel);
        }

        private static void ApplySectPassiveToMultipliers(ref SectMultipliers m, string sectId, float scaling)
        {
            switch (sectId)
            {
                case SectConfig.Renewal:
                    m.HasRenewal = true;
                    m.RenewalIncomeBonus = SectConfig.RenewalIncomeBonus * scaling;
                    break;

                case SectConfig.Antiquity:
                    m.ResearchSpeed += SectConfig.AntiquityResearchSpeed * scaling;
                    break;

                case SectConfig.LivingStone:
                    m.WallIncome += SectConfig.LivingStoneWallIncome * scaling;
                    m.BuildSpeed += SectConfig.LivingStoneBuildSpeed * scaling;
                    break;

                case SectConfig.VeiledMemory:
                    m.FogVisionBonus += SectConfig.VeiledMemoryFogVision * scaling;
                    break;

                case SectConfig.StillFlame:
                    m.TradeIncome += SectConfig.StillFlameTradeIncome * scaling;
                    break;

                case SectConfig.QuietVault:
                    m.VaultInterest += SectConfig.QuietVaultInterest * scaling;
                    break;

                case SectConfig.MirrorRite:
                    m.RangedAccuracy += SectConfig.MirrorRiteAccuracy * scaling;
                    break;

                case SectConfig.ShardJudgment:
                    m.AllIncome += SectConfig.ShardJudgmentIncome * scaling;
                    break;

                case SectConfig.EmberAsh:
                    m.MeleeDamage += SectConfig.EmberAshMeleeDamage * scaling;
                    break;

                case SectConfig.HollowBrand:
                    m.PanicChance += SectConfig.HollowBrandPanic * scaling;
                    break;

                case SectConfig.FlamewroughtChains:
                    m.ControlChance += SectConfig.FlamewroughtChainsControl * scaling;
                    break;

                case SectConfig.UnmakersGrasp:
                    m.DamageVsCrystal += SectConfig.UnmakersGraspVsCrystal * scaling;
                    break;
            }
        }

        private static void ApplySynergyToMultipliers(ref SectMultipliers m, SectConfig.SynergyPair pair, float scaling)
        {
            float bonus = pair.BonusValue * scaling;

            switch (pair.BonusType)
            {
                case "BuildingHP":
                    m.BuildingHP += bonus;
                    break;
                case "ResearchSpeed":
                    m.ResearchSpeed += bonus;
                    break;
                case "AllIncome":
                    m.AllIncome += bonus;
                    break;
                case "RangedDamage":
                    m.RangedDamage += bonus;
                    break;
                case "AttackSpeed":
                    m.AttackSpeed += bonus;
                    break;
                case "DamageVsCrystal":
                    m.DamageVsCrystal += bonus;
                    break;
            }

            Debug.Log($"[FactionSectState] Synergy '{pair.Name}' active: {pair.Description} (x{scaling} scaling)");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RESET
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reset all sect state (for new game).
        /// </summary>
        public void ResetAll()
        {
            _adoptedByFaction.Clear();
            _multipliersByFaction.Clear();
            Debug.Log("[FactionSectState] Reset all sect state");
        }
    }
}
