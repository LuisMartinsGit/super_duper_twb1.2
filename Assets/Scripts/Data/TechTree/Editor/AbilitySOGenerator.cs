// AbilitySOGenerator.cs
// EDITOR-ONLY tool: generate one AbilityDefSO asset per ability card (from the
// AbilityCatalog code seed) into Assets/GameData/TechTree/Abilities/<Branch>/
// <Ability>/, plus the aggregating AbilityCatalogSO in Resources — so ability
// numbers become Inspector-editable and each ability folder can also carry its
// icon and VFX prefab. Part of: Data/TechTree/Editor/
//
// Run via menu:  Waning Border > Tech Tree > Generate Ability SOs
//
// Branches: Status = applied states (aftermaths / lockouts); Unit = everything
// else. Sect god powers stay JSON-backed (sects are out of SO-conversion scope,
// same as techs — see TechTreeCatalog.cs); their per-sect code lives under
// Assets/GameData/TechTree/Abilities/Sect/<Sect>/.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef (TheWaningBorder.Runtime) with no separate editor assembly.
//
// Idempotent: re-running overwrites the per-ability asset fields in place
// (preserving each asset's GUID and any icon/vfxPrefab references the
// designer has assigned) and rebuilds the catalog list in seed order.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TheWaningBorder.Abilities;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Data.EditorTools
{
    public static class AbilitySOGenerator
    {
        const string AbilitiesFolder = "Assets/GameData/TechTree/Abilities";
        const string CatalogPath     = "Assets/Resources/AbilityCatalog.asset";

        // Cards that are applied STATES rather than abilities a unit owns.
        static readonly HashSet<string> StatusCards = new HashSet<string>
        {
            "Veilshift Withdrawal",
            "Life Cling",
            "Under Automation",
        };

        [MenuItem("Waning Border/Tech Tree/Generate Ability SOs")]
        public static void GenerateMenu()
        {
            int count = Build(out string error);
            if (error != null)
            {
                EditorUtility.DisplayDialog("Ability SO Generator", error, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Ability SO Generator",
                $"Done.\n\nAbilities: {count}\nCatalog: {CatalogPath}\n\n" +
                "The catalog auto-loads from Resources at runtime; no scene wiring " +
                "needed. Assign each ability's icon / VFX prefab on its asset.", "OK");
        }

        public static int Build(out string error)
        {
            error = null;
            var seed = AbilityCatalog.SeedCards;
            if (seed == null || seed.Length == 0)
            {
                error = "AbilityCatalog has no seed cards.";
                return 0;
            }

            EnsureFolder(AbilitiesFolder);

            var catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalogSO>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AbilityCatalogSO>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.abilities.Clear();

            for (int i = 0; i < seed.Length; i++)
            {
                var card = seed[i];
                string branch = StatusCards.Contains(card.Name) ? "Status" : "Unit";
                string folderName = Sanitize(card.Name);
                string folder = $"{AbilitiesFolder}/{branch}/{folderName}";
                EnsureFolder($"{AbilitiesFolder}/{branch}");
                EnsureFolder(folder);

                string assetPath = $"{folder}/{folderName}.asset";
                var so = AssetDatabase.LoadAssetAtPath<AbilityDefSO>(assetPath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<AbilityDefSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                so.abilityName = card.Name;
                so.activation  = card.Activation;
                so.targeting   = card.Targeting;
                so.affects     = card.Affects;
                so.castTime    = card.CastTime;
                so.duration    = card.Duration;
                so.cooldown    = card.Cooldown;
                so.radius      = card.Radius;
                so.range       = card.Range;

                var fx = new AbilityDefSO.EffectEntry[card.Effects != null ? card.Effects.Length : 0];
                for (int e = 0; e < fx.Length; e++)
                    fx[e] = new AbilityDefSO.EffectEntry
                    { kind = card.Effects[e].Kind, value = card.Effects[e].Value };
                so.effects   = fx;
                so.aftermath = card.Aftermath;
                // icon / vfxPrefab intentionally untouched — designer-owned slots.

                EditorUtility.SetDirty(so);
                catalog.abilities.Add(so);
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return seed.Length;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
#endif
