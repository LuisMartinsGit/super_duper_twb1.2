// AITacticsSkill.cs
// One difficulty tier's IN-FIGHT skill set (docs/Design/Game_AI.md § 6e).
//
// Held as a single nested field on AIDifficultyProfileSO (`tactics`), so the
// four tier assets carry it and the inspector draws it with no custom drawer.
// No field initialisers: the values live in the profile assets.

namespace TheWaningBorder.AI
{
    /// <summary>
    /// What an AI army of this tier does once it is in contact. Every switch
    /// here is BEHAVIOUR QUALITY — none of it is a stat or a cheat. Easy runs
    /// with all of it off (the army focus-fires one target and nothing else);
    /// Expert runs all of it.
    /// </summary>
    [System.Serializable]
    public struct AITacticsSkill
    {
        /// <summary>How much an enemy's counter relationship to MY units
        /// (its bonusVsTags against my tags, and mine against its) steers
        /// target choice. 0 = ignored. With this and focusFireWeight both 0
        /// the army keeps the single shared focus target.</summary>
        public float counterTargetWeight;

        /// <summary>How much "nearly dead" and "high value" (heroes, siege,
        /// healers, casters) steer target choice. 0 = ignored.</summary>
        public float focusFireWeight;

        /// <summary>Ranged units step back while reloading when melee closes
        /// in, then fire again.</summary>
        public bool kiting;

        /// <summary>Share of the army's FAST MELEE that swings round to hit
        /// the enemy from the side / rear when a fight opens. 0 = never.</summary>
        public float flankFraction;

        /// <summary>Enemy power over mine above which an engaged army falls
        /// back to a friendly tower / Fortress instead of dying piecemeal.
        /// 0 = never (only the legacy go-home retreat applies).</summary>
        public float retreatRatio;

        /// <summary>Enemy power over mine at or below which a fallen-back army
        /// turns round and re-engages. Below retreatRatio, for hysteresis.</summary>
        public float reengageRatio;

        /// <summary>Ranged units only take targets they can shoot from behind
        /// the melee line, and otherwise hold a firing line behind it.</summary>
        public bool rangedBehindMelee;

        /// <summary>The AI casts its units' active abilities (hero, cavalry
        /// horns, volleys, field hospital, sect-unit actives) at all.</summary>
        public bool castUnitAbilities;

        /// <summary>Fewest enemies an area damage / control ability must catch
        /// before it is spent (sect smites, Shardbound Fury, War Cry, ...).</summary>
        public int aoeMinEnemies;

        /// <summary>Share of the allied HP pool in the circle that must be
        /// MISSING before a heal is spent.</summary>
        public float healMinMissingFraction;

        /// <summary>Enemy power over mine at or above which a fight is
        /// "pivotal" — the point at which ultimates (Honour thy Pledge,
        /// invulnerability, death ward) are worth their long cooldown.</summary>
        public float pivotalRatio;
    }
}
