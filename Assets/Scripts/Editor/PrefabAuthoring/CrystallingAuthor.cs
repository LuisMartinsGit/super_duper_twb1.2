// CrystallingAuthor.cs
// Builds the curse's Crystalling as a fast quadruped crystal monster out of
// Synty POLYGON Fantasy Kingdom rocks and the NV3D veilstone gems: a dark stone torso,
// crystal spines, a crystal snout and horns, a tail shard, and four
// two-segment crystal legs walked by CrystallingGait. The crystal is the
// gem material of the veilstone outcrops and curse nodes, with the curse glow.
//
//   Waning Border > Art > Units > Author Crystalling
//       writes Crystalling.prefab and three materials in the unit folder and
//       points Crystalling.asset at the prefab; renders Temp/crystalling/*.png.

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools
{
    public static class CrystallingAuthor
    {
        const string UnitDir = "Assets/GameData/TechTree/Border/Units/Crystalling/";
        const string PrefabPath = UnitDir + "Crystalling.prefab";
        const string SoPath = UnitDir + "Crystalling.asset";
        const string FK = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string Out = "Temp/crystalling/";

        const float BodyHeight = 0.56f;
        const float UpperLength = 0.40f;
        const float LowerLength = 0.50f;

        // The single gems of the same NV3D set the veilstone outcrops and
        // curse nodes are clusters of, so the shared gem material maps onto
        // them exactly as it does there.
        const string Gems = "Assets/MISC/NV3D/Shatter Stone/Stylized Gemstones/Prefabs/Modular Pieces/";
        static readonly string[] Crystals =
        {
            Gems + "SM_Gemstone4_A.prefab", Gems + "SM_Gemstone4_B.prefab", Gems + "SM_Gemstone4_C.prefab",
            Gems + "SM_Gemstone4_D.prefab", Gems + "SM_Gemstone4_E.prefab",
        };
        static readonly string[] Rocks =
        {
            FK + "Environments/K_SM_Env_Rock_Chunk_01.prefab", FK + "Environments/K_SM_Env_Rock_Chunk_02.prefab",
            FK + "Environments/K_SM_Env_Rock_Chunk_03.prefab",
        };

        [MenuItem("Waning Border/Art/Units/Author Crystalling")]
        public static void Author()
        {
            Directory.CreateDirectory(Out);
            var log = new StringBuilder();
            // The crystal is the SAME gem material the veilstone outcrops and
            // the curse nodes wear, with the curse's glow and hue shift baked
            // into one shared copy (EmissiveLadder does the same per node).
            var gem = Resources.Load<GameObject>("Prefabs/Veilstone/P_VeilstoneOutcropping_GemA");
            Material gemMat = null;
            var gemMeshes = new System.Collections.Generic.List<string>();
            if (gem != null)
                foreach (var r in gem.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (gemMat == null) gemMat = r.sharedMaterial;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        log.AppendLine($"gem mesh {mf.sharedMesh.name} size {mf.sharedMesh.bounds.size.ToString("0.00")} mat {r.sharedMaterial.name} shader {r.sharedMaterial.shader.name}");
                }
            if (gemMat == null) { log.AppendLine("MISSING gem material"); File.WriteAllText(Out + "author.txt", log.ToString()); return; }
            string crystalPath = UnitDir + "CrystallingCrystal.mat";
            AssetDatabase.DeleteAsset(crystalPath);
            var crystal = new Material(gemMat) { name = "CrystallingCrystal" };
            if (crystal.HasProperty("_EmissionColor"))
            {
                crystal.EnableKeyword("_EMISSION");
                crystal.SetColor("_EmissionColor", EmissiveLadder.Curse);
            }
            if (crystal.HasProperty("_AlbedoHueShift")) crystal.SetFloat("_AlbedoHueShift", EmissiveLadder.CurseHueShift);
            AssetDatabase.CreateAsset(crystal, crystalPath);
            Texture gemTex = null;
            foreach (var prop in new[] { "_BaseMap", "_MainTex", "_Albedo", "_BaseColorMap" })
                if (gemMat.HasProperty(prop) && gemMat.GetTexture(prop) != null) { gemTex = gemMat.GetTexture(prop); break; }
            foreach (var prop in gemMat.GetTexturePropertyNames())
                log.AppendLine("gem tex prop " + prop + " = " + (gemMat.GetTexture(prop) != null ? gemMat.GetTexture(prop).name : "-"));
            var stone = Mat("CrystallingStone", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.12f, 0.11f, 0.15f));
                m.SetFloat("_Smoothness", 0.28f);
                m.SetFloat("_Metallic", 0f);
            });
            var print = Mat("CrystallingFootprint", "TheWaningBorder/CorpseDissolve", m =>
            {
                m.SetColor("_BaseColor", gemTex != null ? Color.white : new Color(0.25f, 0.62f, 0.70f));
                m.SetTexture("_BaseMap", gemTex);
                m.SetColor("_EdgeColor", EmissiveLadder.Curse);
                m.SetFloat("_EdgeWidth", 0.12f);
                m.SetFloat("_NoiseScale", 26f);
                m.SetFloat("_Dissolve", 0f);
            });

            var go = Build(crystal, stone, print, log);
            try
            {
                var saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                var so = AssetDatabase.LoadMainAssetAtPath(SoPath);
                var ser = new SerializedObject(so);
                ser.FindProperty("prefab").objectReferenceValue = saved;
                ser.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                AssetDatabase.SaveAssets();

                var b = LegionaryShot.BoundsOf(go);
                log.AppendLine($"saved {PrefabPath}; size {b.size.ToString("0.00")} top {b.max.y:0.00}");
                b.Expand(0.3f);
                LegionaryShot.Shoot(b, 215f, 18f, 1400, 1000, Out + "front.png");
                LegionaryShot.Shoot(b, 270f, 6f, 1400, 1000, Out + "side.png");
                LegionaryShot.Shoot(b, 180f, 60f, 1400, 1000, Out + "top.png");
            }
            finally
            {
                Object.DestroyImmediate(go);
                File.WriteAllText(Out + "author.txt", log.ToString());
                Debug.Log("[CrystallingAuthor] " + log);
            }
        }

        static GameObject Build(Material crystal, Material stone, Material print, StringBuilder log)
        {
            var root = new GameObject("Crystalling");
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0f, BodyHeight, 0f);

            // Torso: two stone chunks, chest forward and haunch behind.
            Blob(body, Rocks[0], "Chest", new Vector3(0f, 0.02f, 0.22f), new Vector3(0.46f, 0.36f, 0.56f), new Vector3(0f, 20f, 0f), stone, log);
            Blob(body, Rocks[1], "Haunch", new Vector3(0f, 0f, -0.24f), new Vector3(0.40f, 0.32f, 0.54f), new Vector3(0f, -35f, 0f), stone, log);

            // Spines along the back, raked backwards.
            Shard(body, Crystals[0], "Spine_A", new Vector3(0f, 0.12f, 0.30f), new Vector3(0f, 0.58f, 0.10f), 0.15f, crystal, log);
            Shard(body, Crystals[2], "Spine_B", new Vector3(0.05f, 0.12f, 0.06f), new Vector3(0.08f, 0.66f, -0.20f), 0.17f, crystal, log);
            Shard(body, Crystals[1], "Spine_C", new Vector3(-0.06f, 0.10f, -0.14f), new Vector3(-0.10f, 0.52f, -0.42f), 0.14f, crystal, log);
            Shard(body, Crystals[3], "Spine_D", new Vector3(0.03f, 0.08f, -0.34f), new Vector3(0.04f, 0.36f, -0.60f), 0.11f, crystal, log);
            Shard(body, Crystals[4], "Flank_L", new Vector3(-0.18f, 0.06f, 0.10f), new Vector3(-0.42f, 0.30f, -0.10f), 0.10f, crystal, log);
            Shard(body, Crystals[4], "Flank_R", new Vector3(0.18f, 0.06f, 0.10f), new Vector3(0.42f, 0.30f, -0.10f), 0.10f, crystal, log);

            // Head: a crystal snout thrust forward and low, two horns swept back.
            Blob(body, Rocks[2], "Skull", new Vector3(0f, 0.04f, 0.52f), new Vector3(0.28f, 0.24f, 0.30f), new Vector3(10f, 0f, 0f), stone, log);
            Shard(body, Crystals[0], "Snout", new Vector3(0f, 0.02f, 0.56f), new Vector3(0f, -0.08f, 1.04f), 0.20f, crystal, log);
            Shard(body, Crystals[1], "Horn_L", new Vector3(-0.10f, 0.12f, 0.54f), new Vector3(-0.24f, 0.46f, 0.30f), 0.09f, crystal, log);
            Shard(body, Crystals[1], "Horn_R", new Vector3(0.10f, 0.12f, 0.54f), new Vector3(0.24f, 0.46f, 0.30f), 0.09f, crystal, log);
            Shard(body, Crystals[3], "Fang_L", new Vector3(-0.07f, -0.04f, 0.62f), new Vector3(-0.09f, -0.24f, 0.74f), 0.06f, crystal, log);
            Shard(body, Crystals[3], "Fang_R", new Vector3(0.07f, -0.04f, 0.62f), new Vector3(0.09f, -0.24f, 0.74f), 0.06f, crystal, log);

            // Tail.
            Shard(body, Crystals[2], "Tail", new Vector3(0f, 0.04f, -0.46f), new Vector3(0f, 0.26f, -1.08f), 0.13f, crystal, log);

            // Legs: front-left, front-right, back-left, back-right.
            var gait = root.AddComponent<CrystallingGait>();
            gait.body = body;
            gait.hips = new Transform[4];
            gait.uppers = new Transform[4];
            gait.lowers = new Transform[4];
            gait.pawHome = new Vector3[4];
            gait.upperLength = UpperLength;
            gait.lowerLength = LowerLength;
            string[] names = { "FL", "FR", "BL", "BR" };
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = i < 2 ? 1f : -1f;
                var hip = new GameObject("Hip_" + names[i]).transform;
                hip.SetParent(body, false);
                hip.localPosition = new Vector3(sx * 0.20f, -0.04f, sz * 0.30f);
                gait.hips[i] = hip;
                gait.pawHome[i] = new Vector3(sx * 0.36f, 0f, sz * 0.34f + 0.04f);

                var upper = new GameObject("Upper_" + names[i]).transform;
                upper.SetParent(root.transform, false);
                Shard(upper, Rocks[i % 3], "Thigh", new Vector3(0f, -0.04f, 0f), new Vector3(0f, UpperLength + 0.03f, 0f), 0.17f, stone, log);
                Shard(upper, Crystals[(i + 1) % 5], "Knee", new Vector3(0f, UpperLength - 0.06f, 0f), new Vector3(0f, UpperLength + 0.20f, 0f), 0.08f, crystal, log);
                gait.uppers[i] = upper;

                var lower = new GameObject("Lower_" + names[i]).transform;
                lower.SetParent(root.transform, false);
                // The shin is one long crystal, point down: the paw is its tip.
                Shard(lower, Crystals[i % 5], "Shin", new Vector3(0f, -0.03f, 0f), new Vector3(0f, LowerLength, 0f), 0.12f, crystal, log);
                gait.lowers[i] = lower;

                // Rest pose for the prefab and the previews: paws on the ground.
                Vector3 hipPos = body.localPosition + hip.localPosition;
                Vector3 paw = gait.pawHome[i];
                Vector3 mid = (hipPos + paw) * 0.5f + new Vector3(sx * 0.16f, 0.22f, -sz * 0.06f);
                Vector3 knee = hipPos + (mid - hipPos).normalized * UpperLength;
                upper.localPosition = hipPos;
                upper.localRotation = Quaternion.FromToRotation(Vector3.up, knee - hipPos);
                lower.localPosition = knee;
                lower.localRotation = Quaternion.FromToRotation(Vector3.up, paw - knee);
            }

            // The crystal a paw leaves behind.
            gait.footprintMesh = MeshOf(Crystals[0]);
            gait.footprintMaterial = print;
            return root;
        }

        static Mesh MeshOf(string prefabPath)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var mf = p != null ? p.GetComponentInChildren<MeshFilter>(true) : null;
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>A mesh stretched from <paramref name="from"/> (its base) to
        /// <paramref name="to"/> (its point), <paramref name="thickness"/> across.</summary>
        static void Shard(Transform parent, string prefabPath, string name, Vector3 from, Vector3 to, float thickness, Material mat, StringBuilder log)
        {
            var mesh = MeshOf(prefabPath);
            if (mesh == null) { log.AppendLine("MISSING MESH " + prefabPath); return; }
            var b = mesh.bounds;
            int ax = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            Vector3 axis = Vector3.zero; axis[ax] = 1f;
            float cross = 0f;
            for (int k = 0; k < 3; k++) if (k != ax) cross = Mathf.Max(cross, b.size[k]);

            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.localPosition = from;
            holder.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);

            var m = new GameObject("Mesh");
            m.transform.SetParent(holder, false);
            m.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = m.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            Vector3 scale = Vector3.one * (thickness / Mathf.Max(0.001f, cross));
            scale[ax] = (to - from).magnitude / Mathf.Max(0.001f, b.size[ax]);
            var align = Quaternion.FromToRotation(axis, Vector3.up);
            Vector3 basePoint = b.center; basePoint[ax] = b.min[ax];
            m.transform.localRotation = align;
            m.transform.localScale = scale;
            m.transform.localPosition = -(align * Vector3.Scale(basePoint, scale));
        }

        /// <summary>A mesh fitted into a box of <paramref name="size"/> centred on <paramref name="centre"/>.</summary>
        static void Blob(Transform parent, string prefabPath, string name, Vector3 centre, Vector3 size, Vector3 euler, Material mat, StringBuilder log)
        {
            var mesh = MeshOf(prefabPath);
            if (mesh == null) { log.AppendLine("MISSING MESH " + prefabPath); return; }
            var b = mesh.bounds;
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.localPosition = centre;
            holder.localRotation = Quaternion.Euler(euler);
            var m = new GameObject("Mesh");
            m.transform.SetParent(holder, false);
            m.AddComponent<MeshFilter>().sharedMesh = mesh;
            m.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var scale = new Vector3(size.x / Mathf.Max(0.001f, b.size.x), size.y / Mathf.Max(0.001f, b.size.y), size.z / Mathf.Max(0.001f, b.size.z));
            m.transform.localScale = scale;
            m.transform.localPosition = -Vector3.Scale(b.center, scale);
        }

        static Material Mat(string name, string shader, System.Action<Material> set)
        {
            string path = UnitDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader)) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = Shader.Find(shader);
            set(m);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
