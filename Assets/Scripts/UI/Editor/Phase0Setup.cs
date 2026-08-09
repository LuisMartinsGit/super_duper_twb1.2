// Editor-only tool inside the single runtime asmdef: the Editor/ folder
// convention does NOT apply within an .asmdef, so without this guard the
// file is compiled into PLAYER builds and fails them (UnityEditor missing).
#if UNITY_EDITOR
// Phase 0 setup helper.
// One-click action that creates Assets/UI/Settings/HudPanelSettings.asset with a
// ThemeStyleSheet wired up. Solves the "no theme → no font → labels invisible"
// problem at runtime: PanelSettings created via ScriptableObject.CreateInstance
// does not auto-assign a theme, but Unity emits a working theme as soon as you
// touch UI Toolkit via the Editor menu, so we scan the project for it.

using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheWaningBorder.UI.EditorTools
{
    public static class Phase0Setup
    {
        private const string TargetPath = "Assets/UI/Settings/HudPanelSettings.asset";

        [MenuItem("Tools/Waning Border/UI/Create Phase 0 PanelSettings")]
        public static void CreatePanelSettings()
        {
            EnsureFolder("Assets/UI/Settings");

            // 1) Try Unity's internal PanelSettings creator via reflection. This
            //    is what the "Create > UI Toolkit > Panel Settings Asset" menu
            //    calls — it auto-creates the default runtime theme if missing
            //    and wires it up. Public API hasn't been stable across versions,
            //    so we look for the type by name.
            if (TryCreateViaInternalCreator())
            {
                return;
            }

            // 2) Fallback: create the asset ourselves and scan for any existing
            //    ThemeStyleSheet in the project to attach.
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;

            var theme = FindAnyThemeStyleSheet();
            if (theme != null)
            {
                ps.themeStyleSheet = theme;
            }

            AssetDatabase.CreateAsset(ps, TargetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var created = AssetDatabase.LoadAssetAtPath<PanelSettings>(TargetPath);
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);

            if (theme == null)
            {
                Debug.LogWarning(
                    "[Phase0Setup] PanelSettings created at " + TargetPath +
                    " but no ThemeStyleSheet was found in the project. " +
                    "Right-click in Project → Create → UI Toolkit → Panel Settings Asset " +
                    "(any folder) to make Unity emit UnityDefaultRuntimeTheme.tss, " +
                    "then assign it to the Theme Style Sheet field of " + TargetPath + ".");
            }
            else
            {
                Debug.Log("[Phase0Setup] Created " + TargetPath +
                          " with theme: " + theme.name);
            }
        }

        private static bool TryCreateViaInternalCreator()
        {
            // Unity ships an internal `PanelSettingsCreator` in the
            // UnityEditor.UIElementsModule assembly that the project-window
            // Create menu invokes. Signature: CreatePanelSettingsWithPath(string).
            var asm = typeof(EditorWindow).Assembly;
            var t = asm.GetType("UnityEditor.UIElements.PanelSettingsCreator");
            if (t == null) return false;

            var method = t.GetMethod(
                "CreatePanelSettingsWithPath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                method = t.GetMethod(
                    "CreatePanelSettings",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (method == null) return false;

            try
            {
                var pars = method.GetParameters();
                object result;
                if (pars.Length == 0)
                {
                    result = method.Invoke(null, null);
                }
                else if (pars.Length == 1 && pars[0].ParameterType == typeof(string))
                {
                    result = method.Invoke(null, new object[] { TargetPath });
                }
                else
                {
                    return false;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(TargetPath);
                if (ps == null && result is PanelSettings rps)
                {
                    ps = rps;
                }
                if (ps != null)
                {
                    ps.referenceResolution = new Vector2Int(1920, 1080);
                    ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    EditorUtility.SetDirty(ps);
                    AssetDatabase.SaveAssets();

                    Selection.activeObject = ps;
                    EditorGUIUtility.PingObject(ps);
                    Debug.Log("[Phase0Setup] Created PanelSettings via Unity's internal creator: " +
                              AssetDatabase.GetAssetPath(ps));
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Phase0Setup] Internal PanelSettings creator failed: " +
                                 e.Message + ". Falling back to manual creation.");
                return false;
            }
        }

        private static ThemeStyleSheet FindAnyThemeStyleSheet()
        {
            // Search the project's AssetDatabase first — UnityDefaultRuntimeTheme.tss
            // appears here once Unity has emitted it (e.g., after the first
            // PanelSettings is created via menu).
            foreach (var guid in AssetDatabase.FindAssets("t:ThemeStyleSheet"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var t = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(p);
                if (t != null) return t;
            }

            // Last resort: any ThemeStyleSheet that's already loaded in memory.
            var loaded = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
            return loaded.Length > 0 ? loaded[0] : null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var leaf = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

#endif // UNITY_EDITOR
