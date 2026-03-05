// SectConfig.cs
// Static configuration for sect adoption costs, passive effects, synergy pairs, and temple scaling
// Location: Assets/Scripts/Economy/SectConfig.cs

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Static configuration for the sect adoption system.
    /// Contains affinity costs, passive effect values, synergy pair definitions,
    /// and temple scaling multipliers.
    ///
    /// Sect adoption costs:
    ///   Affinity (culture matches): 1 RP
    ///   Non-affinity (different culture): 3 RP
    ///
    /// Temple scaling amplifies passives:
    ///   Level 1: 1.0x
    ///   Level 2: 1.5x
    ///   Level 3+: 2.0x
    /// </summary>
    public static class SectConfig
    {
        // ═══════════════════════════════════════════════════════════════════
        // ADOPTION COSTS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>RP cost to adopt a sect whose affinity matches the player's culture</summary>
        public const int AffinityCost = 1;

        /// <summary>RP cost to adopt a sect whose affinity does NOT match the player's culture</summary>
        public const int NonAffinityCost = 3;

        // ═══════════════════════════════════════════════════════════════════
        // TEMPLE SCALING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the passive multiplier for a given temple level.
        /// Level 1 = 1.0x, Level 2 = 1.5x, Level 3+ = 2.0x.
        /// </summary>
        public static float GetTempleScaling(int templeLevel) => templeLevel switch
        {
            1 => 1.0f,
            2 => 1.5f,
            _ => templeLevel >= 3 ? 2.0f : 1.0f
        };

        // ═══════════════════════════════════════════════════════════════════
        // SECT IDS
        // ═══════════════════════════════════════════════════════════════════

        // Alanthor sects
        public const string Renewal = "Sect_Renewal";
        public const string Antiquity = "Sect_Antiquity";
        public const string LivingStone = "Sect_LivingStone";
        public const string VeiledMemory = "Sect_VeiledMemory";

        // Runai sects
        public const string StillFlame = "Sect_StillFlame";
        public const string QuietVault = "Sect_QuietVault";
        public const string MirrorRite = "Sect_MirrorRite";
        public const string ShardJudgment = "Sect_ShardJudgment";

        // Feraldis sects
        public const string EmberAsh = "Sect_EmberAsh";
        public const string HollowBrand = "Sect_HollowBrand";
        public const string FlamewroughtChains = "Sect_FlamewroughtChains";
        public const string UnmakersGrasp = "Sect_UnmakersGrasp";

        /// <summary>All 12 sect IDs in display order (Alanthor, Runai, Feraldis).</summary>
        public static readonly string[] AllSectIds =
        {
            Renewal, Antiquity, LivingStone, VeiledMemory,
            StillFlame, QuietVault, MirrorRite, ShardJudgment,
            EmberAsh, HollowBrand, FlamewroughtChains, UnmakersGrasp
        };

        // ═══════════════════════════════════════════════════════════════════
        // DISPLAY NAMES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get a human-readable display name for a sect ID.
        /// </summary>
        public static string GetDisplayName(string sectId) => sectId switch
        {
            Renewal => "Renewal",
            Antiquity => "Antiquity",
            LivingStone => "Living Stone",
            VeiledMemory => "Veiled Memory",
            StillFlame => "Still Flame",
            QuietVault => "Quiet Vault",
            MirrorRite => "Mirror Rite",
            ShardJudgment => "Shard Judgment",
            EmberAsh => "Ember Ash",
            HollowBrand => "Hollow Brand",
            FlamewroughtChains => "Flamewrought Chains",
            UnmakersGrasp => "Unmaker's Grasp",
            _ => sectId
        };

        /// <summary>
        /// Get a short description of the sect's passive effect.
        /// </summary>
        public static string GetPassiveDescription(string sectId) => sectId switch
        {
            Renewal => "+20% income if all walls full HP",
            Antiquity => "+20% research speed",
            LivingStone => "+20% wall income, +10% build speed",
            VeiledMemory => "+15% fog vision range",
            StillFlame => "+15% trade income",
            QuietVault => "+30% banking interest",
            MirrorRite => "+10% ranged accuracy",
            ShardJudgment => "+10% income",
            EmberAsh => "+12% melee damage",
            HollowBrand => "5% panic chance on hit",
            FlamewroughtChains => "3% control chance on hit",
            UnmakersGrasp => "+20% damage vs Crystal",
            _ => ""
        };

        // ═══════════════════════════════════════════════════════════════════
        // PASSIVE BASE VALUES (before temple scaling)
        // ═══════════════════════════════════════════════════════════════════

        // Alanthor passives
        public const float RenewalIncomeBonus = 0.20f;         // +20% income if walls full HP
        public const float AntiquityResearchSpeed = 0.20f;     // +20% research speed
        public const float LivingStoneWallIncome = 0.20f;      // +20% wall income
        public const float LivingStoneBuildSpeed = 0.10f;      // +10% build speed
        public const float VeiledMemoryFogVision = 0.15f;      // +15% LOS range

        // Runai passives
        public const float StillFlameTradeIncome = 0.15f;      // +15% trade income
        public const float QuietVaultInterest = 0.30f;         // +30% vault interest
        public const float MirrorRiteAccuracy = 0.10f;         // +10% ranged accuracy
        public const float ShardJudgmentIncome = 0.10f;        // +10% all income

        // Feraldis passives
        public const float EmberAshMeleeDamage = 0.12f;        // +12% melee damage
        public const float HollowBrandPanic = 0.05f;           // 5% panic chance
        public const float FlamewroughtChainsControl = 0.03f;  // 3% control chance
        public const float UnmakersGraspVsCrystal = 0.20f;     // +20% vs Crystal

        // ═══════════════════════════════════════════════════════════════════
        // SYNERGY PAIRS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Synergy pair definition: two sects that grant a bonus when both are adopted.
        /// </summary>
        public struct SynergyPair
        {
            public string SectA;
            public string SectB;
            public string Name;
            public string BonusType;
            public float BonusValue;
            public string Description;
        }

        /// <summary>6 synergy pairs. Checked when a sect is adopted.</summary>
        public static readonly SynergyPair[] SynergyPairs =
        {
            new SynergyPair
            {
                SectA = Renewal, SectB = LivingStone,
                Name = "The Fortress",
                BonusType = "BuildingHP",
                BonusValue = 0.10f,
                Description = "+10% building HP"
            },
            new SynergyPair
            {
                SectA = Antiquity, SectB = VeiledMemory,
                Name = "The Archive",
                BonusType = "ResearchSpeed",
                BonusValue = 0.15f,
                Description = "+15% research speed"
            },
            new SynergyPair
            {
                SectA = StillFlame, SectB = QuietVault,
                Name = "The Merchant",
                BonusType = "AllIncome",
                BonusValue = 0.10f,
                Description = "+10% all income"
            },
            new SynergyPair
            {
                SectA = MirrorRite, SectB = ShardJudgment,
                Name = "The Inquisitor",
                BonusType = "RangedDamage",
                BonusValue = 0.15f,
                Description = "+15% ranged damage"
            },
            new SynergyPair
            {
                SectA = EmberAsh, SectB = HollowBrand,
                Name = "The Warband",
                BonusType = "AttackSpeed",
                BonusValue = 0.10f,
                Description = "+10% attack speed"
            },
            new SynergyPair
            {
                SectA = FlamewroughtChains, SectB = UnmakersGrasp,
                Name = "The Purifier",
                BonusType = "DamageVsCrystal",
                BonusValue = 0.30f,
                Description = "+30% damage vs Crystal"
            }
        };

        /// <summary>
        /// Get the culture affinity string for a sect ID.
        /// </summary>
        public static string GetAffinity(string sectId) => sectId switch
        {
            Renewal or Antiquity or LivingStone or VeiledMemory => "Alanthor",
            StillFlame or QuietVault or MirrorRite or ShardJudgment => "Runai",
            EmberAsh or HollowBrand or FlamewroughtChains or UnmakersGrasp => "Feraldis",
            _ => ""
        };

        /// <summary>
        /// Get the culture byte constant for a sect's affinity.
        /// </summary>
        public static byte GetAffinityCulture(string sectId) => sectId switch
        {
            Renewal or Antiquity or LivingStone or VeiledMemory => Cultures.Alanthor,
            StillFlame or QuietVault or MirrorRite or ShardJudgment => Cultures.Runai,
            EmberAsh or HollowBrand or FlamewroughtChains or UnmakersGrasp => Cultures.Feraldis,
            _ => Cultures.None
        };
    }
}
