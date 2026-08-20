// AbilityCatalogSO.cs
// Single ScriptableObject that aggregates every AbilityDefSO, mirroring the
// TechTreeCatalog pattern. When the asset exists at
// Assets/Resources/AbilityCatalog.asset with entries, AbilityCatalog builds
// its card table from these SOs instead of the code seed.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Abilities
{
    [CreateAssetMenu(fileName = "AbilityCatalog", menuName = "Waning Border/Ability Catalog", order = 4)]
    public class AbilityCatalogSO : ScriptableObject
    {
        [Tooltip("Order IS the stable catalog index contract (UnitAbilities references " +
                 "abilities by index). Append only; never reorder.")]
        public List<AbilityDefSO> abilities = new List<AbilityDefSO>();

        public bool HasEntries => abilities != null && abilities.Count > 0;
    }
}
