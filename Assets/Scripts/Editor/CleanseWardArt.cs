// CleanseWardArt.cs
// Builds the Cleansing Ward's prefab (Territory_Claims.md §6.9.7) from two
// Synty Fantasy Kingdom pieces — a statue plinth with a crystal standing on
// it, the crystal given a holy cyan-white emissive — copies the Lana
// "Burst_rings" effect in as the ward's pulse aura (through PackVfxImport,
// which converts the vendor materials for URP), and binds both to
// CleanseWardSystem.asset. Idempotent: run it again and it rebuilds the same
// two assets in place.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class CleanseWardArt
    {
        const string Folder = "Assets/GameData/TechTree/Age0/Buildings/TempleOfRidan/Units/Litharch/Abilities/Cleanse/";
        const string PrefabPath = Folder + "CleanseWard.prefab";
        const string PulsePath = Folder + "CleanseWard_Pulse.prefab";
        const string ConfigPath = Folder + "CleanseWardSystem.asset";
        const string PlinthFbx = "Assets/Synty/PolygonFantasyKingdom/Models/Bonus/SM_Prop_Statue_Plinth_05.fbx";
        const string CrystalFbx = "Assets/Synty/PolygonFantasyKingdom/Models/SM_Item_Crystal_01.fbx";
        const string PulseSource = "Assets/Tools/Lana Studio/Casual RPG VFX/Prefabs/Burst/Burst_rings.prefab";

        [MenuItem("Waning Border/Art/Build Cleansing Ward Prefab")]
        public static void Build()
        {
            var plinthSrc = AssetDatabase.LoadAssetAtPath<GameObject>(PlinthFbx);
            var crystalSrc = AssetDatabase.LoadAssetAtPath<GameObject>(CrystalFbx);
            if (plinthSrc == null || crystalSrc == null)
            {
                Debug.LogError($"[CleanseWardArt] missing Synty piece(s): plinth={plinthSrc != null} crystal={crystalSrc != null}");
                return;
            }

            var root = new GameObject("CleanseWard");
            try
            {
                var plinth = (GameObject)PrefabUtility.InstantiatePrefab(plinthSrc);
                PrefabUtility.UnpackPrefabInstance(plinth, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                plinth.name = "Plinth";
                plinth.transform.SetParent(root.transform, false);
                plinth.transform.localPosition = Vector3.zero;
                plinth.transform.localScale = Vector3.one * 0.8f;

                float top = 0.6f;
                var pr = plinth.GetComponentInChildren<Renderer>();
                if (pr != null) top = pr.bounds.max.y - root.transform.position.y;

                var crystal = (GameObject)PrefabUtility.InstantiatePrefab(crystalSrc);
                PrefabUtility.UnpackPrefabInstance(crystal, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                crystal.name = "Crystal";
                crystal.transform.SetParent(root.transform, false);
                crystal.transform.localScale = Vector3.one * 2.2f;
                crystal.transform.localPosition = new Vector3(0f, top - 0.05f, 0f);

                // A holy cyan-white glow on the crystal: its own material so the
                // Synty atlas material stays untouched.
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                var glow = new Material(shader) { name = "CleanseWard_Crystal" };
                glow.SetColor("_BaseColor", new Color(0.78f, 0.96f, 1f));
                glow.EnableKeyword("_EMISSION");
                glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                glow.SetColor("_EmissionColor", new Color(0.55f, 0.9f, 1f) * 2.5f);
                string matPath = Folder + "CleanseWard_Crystal.mat";
                var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existingMat != null) { existingMat.CopyPropertiesFromMaterial(glow); glow = existingMat; }
                else AssetDatabase.CreateAsset(glow, matPath);
                foreach (var r in crystal.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = glow;
                    r.sharedMaterials = mats;
                }

                foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            // The pulse aura: the vendor burst copied beside the ability and
            // converted for URP, exactly as every other pack effect.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PulsePath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PulseSource) == null)
                    Debug.LogWarning("[CleanseWardArt] vendor burst not found: " + PulseSource);
                else PackVfxImport.ImportAll();   // the table row for CleanseWard_Pulse
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var cfg = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Systems.Border.CleanseWardSystemConfig>(ConfigPath);
            if (cfg == null) { Debug.LogError("[CleanseWardArt] no config at " + ConfigPath); return; }
            cfg.wardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            cfg.pulsePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PulsePath);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CleanseWardArt] built {PrefabPath}; pulse={(cfg.pulsePrefab != null ? PulsePath : "none")}; bound to {ConfigPath}");
        }
    }
}
