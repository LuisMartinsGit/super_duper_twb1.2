// TechDefSO.cs
// ScriptableObject authoring asset for one technology and its effects.
//
// One .asset per technology, and it lives in the folder of the BUILDING that
// researches it:
//
//     Buildings/Age 0/ArcheryRange/Research/Fletching.asset
//     Buildings/Alanthor/Smelter/Research/IronPlate.asset
//
// so the folder tree can be walked the same way the tech tree is read.
//
// `researchAt` is the SOURCE OF TRUTH for the host, not the folder path. The
// folder is organisation; the field is data. A building's research list is
// DERIVED from it at load (TechCatalog), which is the point: before this,
// "where is X researched" was answered by two different files that could
// disagree -- the player grid read BuildingDef.research[], the AI read
// TechnologyDef.researchAt, and 69 of 91 technologies were listed by no
// building at all. There is now one field, and both readers project from it.
//
// NOTE: "name" is renamed "displayName" (ScriptableObject already defines `name`).

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    [CreateAssetMenu(fileName = "Tech_", menuName = "Waning Border/Tech Def", order = 3)]
    public class TechDefSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [Tooltip("upgrade | unlock | passive")]
        public string role;

        [Header("Description")]
        [Tooltip("What this technology does (shown on the research button).")]
        [TextArea(2, 4)]
        public string effect;
        [Tooltip("Flavour / lore text.")]
        [TextArea(2, 4)]
        public string desc;

        [Header("Research Host")]
        [Tooltip("Building id that researches this. THE source of truth for the " +
                 "host -- the building's research list is derived from it. Keep it " +
                 "matching the folder this asset sits in.")]
        public string researchAt;
        [Tooltip("Minimum level of the HOST building. 0 or 1 = no level gate.")]
        public int minBuildingLevel;
        [Tooltip("Seconds to research.")]
        public float researchTime;
        [Tooltip("Technology ids that must be researched first.")]
        public string[] prerequisites;
        [Tooltip("Culture this tech is restricted to (Alanthor / Runai / Feraldis). " +
                 "Empty = available to every culture.")]
        public string culture;

        [Header("Economy")]
        public CostBlock cost = new CostBlock();

        [Header("Effects -- fixed stat block")]
        [Tooltip("The original six-stat model. Leave zeroed if this tech uses the " +
                 "generic effect list below, or has no stat effect at all.")]
        public TechEffects effects = new TechEffects();

        [Header("Effects -- generic list (target / stat / op / value)")]
        [Tooltip("The calculator model. Both models coexist; a tech may use either, " +
                 "both, or neither (ability-unlock and age-up techs carry none).")]
        public List<TechEffectEntry> effectsList = new List<TechEffectEntry>();

        /// <summary>Project this asset into the runtime <see cref="TechnologyDef"/>.</summary>
        public TechnologyDef ToDef()
        {
            return new TechnologyDef
            {
                id               = id,
                name             = string.IsNullOrEmpty(displayName) ? id : displayName,
                role             = role ?? "",
                effect           = effect ?? "",
                desc             = desc ?? "",
                researchTime     = researchTime,
                researchAt       = researchAt ?? "",
                prerequisites    = CloneArray(prerequisites),
                culture          = culture ?? "",
                minBuildingLevel = minBuildingLevel,
                cost             = CloneCost(cost),
                // A zeroed block means "no fixed effects" -- hand back null rather
                // than an all-zero object, because TechEffectSystem treats null as
                // "nothing to apply" and would otherwise walk six no-op branches.
                effects          = effects != null && effects.HasAnyEffect ? CloneEffects(effects) : null,
                effectsList      = effectsList != null && effectsList.Count > 0
                                     ? new List<TechEffectEntry>(effectsList)
                                     : null,
            };
        }

        /// <summary>Fill this asset from a parsed def (used by the generator).</summary>
        public void FromDef(TechnologyDef def)
        {
            if (def == null) return;
            id               = def.id;
            displayName      = def.name;
            role             = def.role;
            effect           = def.effect;
            desc             = def.desc;
            researchTime     = def.researchTime;
            researchAt       = def.researchAt;
            prerequisites    = CloneArray(def.prerequisites);
            culture          = def.culture;
            minBuildingLevel = def.minBuildingLevel;
            cost             = CloneCost(def.cost);
            effects          = def.effects != null ? CloneEffects(def.effects) : new TechEffects();
            effectsList      = def.effectsList != null
                                 ? new List<TechEffectEntry>(def.effectsList)
                                 : new List<TechEffectEntry>();
        }

        private static string[] CloneArray(string[] src)
            => src == null ? System.Array.Empty<string>() : (string[])src.Clone();

        private static CostBlock CloneCost(CostBlock c)
            => c == null ? new CostBlock()
                         : CostBlock.Of(c.Supplies, c.Iron, c.Veilstone, c.Veilsteel);

        private static TechEffects CloneEffects(TechEffects e) => new TechEffects
        {
            gatherSpeedMult      = e.gatherSpeedMult,
            meleeAttackSpeedMult = e.meleeAttackSpeedMult,
            meleeDefenseAdd      = e.meleeDefenseAdd,
            meleeDamageAdd       = e.meleeDamageAdd,
            rangedDamageAdd      = e.rangedDamageAdd,
            archerRangeMult      = e.archerRangeMult,
        };
    }
}
