// LedgerUnitBuilder.cs
// Wires the exported Ledger pentapod FBX into a unit the game can animate,
// and reports what it found on the way so an import problem is visible rather
// than silent.
//
// WHY THIS EXISTS. Assigning the model to UnitDefSO.prefab is only half the
// wiring. PresentationSpawnSystem hands a spawned unit an Animator Controller
// from TechCatalog.TryGetController(presentationId) — and ONLY when the
// prefab's own Animator has none. The Ledger had neither: no .controller
// asset existed and the SO's animatorController was null, so the Animator sat
// there with nothing to play and the mesh held its bind pose. The legs were
// not broken; nothing was ever asked to move them.
//
// The model ships ONE clip, Walk. Idle therefore plays that same clip at
// speed 0, which holds frame 0 as a standing pose rather than needing a
// second clip that does not exist yet. Add a real Idle later and only the
// Idle state's motion changes.

using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class LedgerUnitBuilder
    {
        private const string Dir = "Assets/GameData/TechTree/Civs/Alanthor/Units/Ledger/";
        private const string FbxPath = Dir + "Ledger.fbx";
        private const string ControllerPath = Dir + "Ledger.controller";
        private const string SoPath = Dir + "Ledger.asset";

        private const string WalkClip = "Walk";
        private const string IsMoving = "IsMoving";

        /// <summary>
        /// Does the Walk clip actually MOVE this prefab's bones?
        ///
        /// "Animator has a controller" and "the animation plays" are not the
        /// same thing. A clip binds to bones by TRANSFORM PATH, so if the
        /// prefab the SO points at nests the model under an extra root, every
        /// path misses, the state machine runs happily and the mesh sits in
        /// its bind pose — which looks exactly like legs that do not move.
        /// This samples the clip at two times and measures a foot.
        /// </summary>
        [MenuItem("Waning Border/Units/Verify Ledger Walk Binds")]
        public static void Verify()
        {
            var so = AssetDatabase.LoadAssetAtPath<Object>(SoPath);
            var sobj = new SerializedObject(so);
            var prefab = sobj.FindProperty("prefab").objectReferenceValue as GameObject;
            var ctrl = sobj.FindProperty("animatorController").objectReferenceValue
                as RuntimeAnimatorController;

            if (prefab == null) { Debug.LogError("[Ledger] SO has no prefab"); return; }
            if (ctrl == null) { Debug.LogError("[Ledger] SO has no animatorController"); return; }
            Debug.Log($"[Ledger] SO prefab='{prefab.name}' controller='{ctrl.name}'");

            var clip = ctrl.animationClips.FirstOrDefault(c => c.name == WalkClip)
                       ?? ctrl.animationClips.FirstOrDefault();
            if (clip == null) { Debug.LogError("[Ledger] controller has no clips"); return; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = inst.GetComponentInChildren<Animator>();
                Debug.Log($"[Ledger] prefab hierarchy root='{inst.name}' " +
                          $"animator={(animator == null ? "MISSING" : animator.gameObject.name)} " +
                          $"animatorIsRoot={(animator != null && animator.gameObject == inst)}");

                // A foot as far down the chain as possible: if IT moves, the
                // whole leg chain resolved.
                var foot = inst.GetComponentsInChildren<Transform>()
                               .FirstOrDefault(t => t.name == "Hip1.Foot");
                if (foot == null)
                {
                    Debug.LogError("[Ledger] no bone named 'Hip1.Foot' under the prefab — " +
                                   "the skeleton is not there, so nothing can bind.");
                    return;
                }

                // Sample the WHOLE cycle, not two arbitrary instants: an ankle
                // near a pivot at both sample times reads as almost no motion
                // even when the leg is swinging its full stroke.
                const int Samples = 12;
                var pts = new Vector3[Samples];
                for (int i = 0; i < Samples; i++)
                {
                    clip.SampleAnimation(inst, clip.length * i / (float)Samples);
                    pts[i] = foot.position;
                }

                float moved = 0f;
                for (int i = 0; i < Samples; i++)
                    for (int j = i + 1; j < Samples; j++)
                        moved = Mathf.Max(moved, Vector3.Distance(pts[i], pts[j]));

                float lift = 0f;
                for (int i = 0; i < Samples; i++)
                    lift = Mathf.Max(lift, pts[i].y - pts[0].y);

                Debug.Log($"[Ledger] clip '{clip.name}' len={clip.length:0.00}s loop={clip.isLooping}; " +
                          $"Hip1.Foot travels {moved:0.0000} m across the cycle, " +
                          $"rises {lift:0.0000} m");

                // Every one of the five legs should move, not just the one.
                foreach (var legName in new[] { "Hip1.Foot", "Hip2.Foot", "Hip3.Foot",
                                                "Hip4.Foot", "Hip5.Foot" })
                {
                    var t = inst.GetComponentsInChildren<Transform>()
                                .FirstOrDefault(x => x.name == legName);
                    if (t == null) { Debug.LogError($"[Ledger]   {legName}: MISSING"); continue; }
                    Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                    for (int i = 0; i < Samples; i++)
                    {
                        clip.SampleAnimation(inst, clip.length * i / (float)Samples);
                        lo = Vector3.Min(lo, t.position);
                        hi = Vector3.Max(hi, t.position);
                    }
                    Debug.Log($"[Ledger]   {legName}: travel span {(hi - lo).magnitude:0.0000} m");
                }

                if (moved < 0.001f)
                    Debug.LogError("[Ledger] FAIL — the foot does not move. The clip is not binding " +
                                   "to this prefab's transforms (usually an extra root above the " +
                                   "model, so the clip's paths miss).");
                else
                    Debug.Log("[Ledger] PASS — the clip drives the skeleton. Legs will move.");
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>What does the Ledger skin ACTUALLY contain? The toe
        /// measurement depends on reading bone weights correctly, and two
        /// different readings of the legacy API gave two different wrong
        /// answers, so this prints the raw data instead of inferring it.</summary>
        [MenuItem("Waning Border/Units/Diagnose Ledger Skin")]
        public static void DiagnoseSkin()
        {
            var prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var smr = prefabObj != null ? prefabObj.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (smr == null) { Debug.LogError("[LedgerSkin] no SkinnedMeshRenderer on the FBX."); return; }
            var mesh = smr.sharedMesh;

            Debug.Log($"[LedgerSkin] mesh '{mesh.name}' verts={mesh.vertexCount} " +
                      $"bindposes={mesh.bindposes.Length} smr.bones={smr.bones.Length}");

            var legacy = mesh.boneWeights;
            Debug.Log($"[LedgerSkin] legacy boneWeights.Length={legacy.Length}");
            if (legacy.Length > 0)
            {
                int ge05 = 0;
                var idx = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < legacy.Length; i++)
                {
                    if (legacy[i].weight0 >= 0.5f) ge05++;
                    idx.Add(legacy[i].boneIndex0);
                }
                var list = new System.Collections.Generic.List<int>(idx);
                list.Sort();
                Debug.Log($"[LedgerSkin] legacy: weight0>=0.5 on {ge05}/{legacy.Length}; " +
                          $"distinct boneIndex0 = [{string.Join(",", list)}]");
                Debug.Log($"[LedgerSkin] legacy[0] = idx({legacy[0].boneIndex0},{legacy[0].boneIndex1}) " +
                          $"w({legacy[0].weight0:0.###},{legacy[0].weight1:0.###})");
            }

            var bpv = mesh.GetBonesPerVertex();
            var all = mesh.GetAllBoneWeights();
            Debug.Log($"[LedgerSkin] modern: bonesPerVertex.Length={bpv.Length} allBoneWeights.Length={all.Length}");
            if (bpv.Length > 0)
            {
                var counts = new System.Collections.Generic.Dictionary<int,int>();
                for (int i = 0; i < bpv.Length; i++)
                {
                    int k = bpv[i];
                    counts.TryGetValue(k, out int c); counts[k] = c + 1;
                }
                foreach (var kv in counts) Debug.Log($"[LedgerSkin]   {kv.Value} verts have {kv.Key} influence(s)");
            }
            if (all.Length > 0)
                Debug.Log($"[LedgerSkin] allBoneWeights[0] = bone {all[0].boneIndex} weight {all[0].weight:0.###}");

            for (int b = 0; b < smr.bones.Length; b++)
                Debug.Log($"[LedgerSkin]   bone[{b}] = {(smr.bones[b] ? smr.bones[b].name : "<null>")}");
            bpv.Dispose(); all.Dispose();
        }

        [MenuItem("Waning Border/Units/Build Ledger Walk Controller")]
        public static void Build()
        {
            // ── 1. What did the FBX actually import as? ─────────────────
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[Ledger] no ModelImporter at {FbxPath} — is the FBX in the project?");
                return;
            }

            Debug.Log($"[Ledger] importer: animationType={importer.animationType} " +
                      $"(Generic expected), importAnimation={importer.importAnimation}, " +
                      $"avatarSetup={importer.avatarSetup}");

            var subAssets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            var clips = subAssets.OfType<AnimationClip>()
                                 .Where(c => !c.name.StartsWith("__preview__"))
                                 .ToArray();
            var avatar = subAssets.OfType<Avatar>().FirstOrDefault();

            Debug.Log($"[Ledger] sub-assets: {clips.Length} clip(s) [" +
                      string.Join(", ", clips.Select(c => $"{c.name} {c.length:0.00}s loop={c.isLooping}")) +
                      $"], avatar={(avatar == null ? "NONE" : avatar.name)} " +
                      $"valid={(avatar != null && avatar.isValid)}");

            if (clips.Length == 0)
            {
                Debug.LogError("[Ledger] the FBX imported NO animation clips — the controller " +
                               "would have nothing to play. Check Rig/Animation on the importer.");
                return;
            }
            if (avatar == null || !avatar.isValid)
            {
                Debug.LogError("[Ledger] the FBX has no VALID avatar. A Generic rig needs one or " +
                               "the clip cannot bind to the skeleton and the mesh holds its bind pose.");
                return;
            }

            var walk = clips.FirstOrDefault(c => c.name == WalkClip) ?? clips[0];
            if (walk.name != WalkClip)
                Debug.LogWarning($"[Ledger] no clip named '{WalkClip}'; using '{walk.name}'.");
            if (!walk.isLooping)
                Debug.LogWarning($"[Ledger] clip '{walk.name}' is NOT set to loop — the walk will " +
                                 "play once and stop. Tick Loop Time on the FBX's Animation tab.");

            // ── 2. Build the controller ─────────────────────────────────
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter(IsMoving, AnimatorControllerParameterType.Bool);

            var sm = ac.layers[0].stateMachine;

            var idle = sm.AddState("Idle");
            idle.motion = walk;
            idle.speed = 0f;          // hold frame 0 — the model has no separate idle clip yet

            var walkState = sm.AddState("Walk");
            walkState.motion = walk;
            walkState.speed = 1f;

            sm.defaultState = idle;

            var toWalk = idle.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.12f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, IsMoving);

            var toIdle = walkState.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.12f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, IsMoving);

            EditorUtility.SetDirty(ac);
            Debug.Log($"[Ledger] built {ControllerPath}: param {IsMoving}, " +
                      $"states Idle(speed 0) <-> Walk, motion '{walk.name}'");

            // ── 3. Point the unit at it ─────────────────────────────────
            var so = AssetDatabase.LoadAssetAtPath<Object>(SoPath);
            if (so == null)
            {
                Debug.LogError($"[Ledger] could not load {SoPath}");
                return;
            }

            var sobj = new SerializedObject(so);
            var ctrlProp = sobj.FindProperty("animatorController");
            var prefabProp = sobj.FindProperty("prefab");
            var pidProp = sobj.FindProperty("presentationId");

            if (ctrlProp == null)
            {
                Debug.LogError("[Ledger] UnitDefSO has no 'animatorController' field?");
                return;
            }
            ctrlProp.objectReferenceValue = ac;

            // The SO's prefab wins over the PrefabPaths Resources map, so if it
            // is still the placeholder the pentapod never spawns at all.
            var currentPrefab = prefabProp != null ? prefabProp.objectReferenceValue : null;
            var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (currentPrefab == null || currentPrefab.name.StartsWith("PLACEHOLDER"))
            {
                if (prefabProp != null && fbxRoot != null)
                {
                    prefabProp.objectReferenceValue = fbxRoot;
                    Debug.Log($"[Ledger] prefab was '{(currentPrefab == null ? "none" : currentPrefab.name)}' " +
                              $"— pointed it at the FBX root instead.");
                }
            }
            else
            {
                Debug.Log($"[Ledger] leaving prefab as '{currentPrefab.name}' (already set).");
            }

            sobj.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Ledger] DONE. presentationId={(pidProp != null ? pidProp.intValue : -1)}, " +
                      $"animatorController={ac.name}. Spawn a Ledger and it should walk when it moves.");
        }
    }
}
