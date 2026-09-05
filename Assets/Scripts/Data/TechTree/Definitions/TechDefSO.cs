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
        public string id;
        public string displayName;
        public string role;

        [TextArea(2, 4)]
        public string effect;
        [TextArea(2, 4)]
        public string desc;

        public string researchAt;
        public int minBuildingLevel;
        public float researchTime;
        public string[] prerequisites;
        public string culture;

        public CostBlock cost = new CostBlock();

        public TechEffects effects = new TechEffects();

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
