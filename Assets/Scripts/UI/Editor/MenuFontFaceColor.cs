// Editor-only tool inside the single runtime asmdef: the Editor/ folder
// convention does NOT apply within an .asmdef, so without this guard the
// file is compiled into PLAYER builds and fails them (UnityEditor missing).
#if UNITY_EDITOR
// MenuFontFaceColor.cs (editor-only)
// Neutralises the _FaceColor baked into the Synty menu fonts' DEFAULT material.
//
// Run: Tools > Waning Border > Menu > Fix Menu Font Face Colour.
//
// -- What this fixes ------------------------------------------------------
// TMP renders a glyph as `vertex colour * material _FaceColor`, and Synty ships
// AlegreyaSans-Medium SDF with its default material's _FaceColor set to
// (0.620, 0.482, 0) - a dark olive yellow. Every TMP label in MainMenu.unity
// and SkirmishMenu.unity uses that font AND that default material, so the whole
// menu renders dark yellow no matter what colour the label is authored:
//
//     authored white  (1.000, 1.000, 1.000)  ->  (0.620, 0.482, 0)
//     authored gold   (0.910, 0.722, 0.290)  ->  (0.564, 0.348, 0)
//
// It also made SkirmishPanelChrome's "Whiten Skirmish Text" pass a no-op on
// screen: it does set every label to white, and white times dark yellow is
// still dark yellow. With the face colour neutral, an authored colour finally
// means what it says, which is the assumption every colour constant in
// MenuPanelsBuilder / SkirmishPanelChrome / MapOptionsChrome is written under.
//
// -- Why this is a tool and not just an edit ------------------------------
// `Assets/Synty/` is gitignored (.gitignore:155), so the fixed asset is NOT in
// the repo. A fresh clone, or a reimport of the Interface Fantasy Menus
// package, brings the dark yellow straight back with no diff to explain it.
// This pass is the tracked copy of the fix - re-run it after either.
//
// Idempotent, registers Undo, and touches nothing but _FaceColor.

using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    internal static class MenuFontFaceColor
    {
        private const string MenuPath = "Tools/Waning Border/Menu/Fix Menu Font Face Colour";

        /// <summary>
        /// TMP font assets the game's own menu scenes actually render with.
        /// Pinned by GUID rather than path for the same reason MenuPlayModeStart
        /// pins its scene: the Synty folder gets moved and reimported.
        /// AlegreyaSans-Medium SDF is every label in MainMenu.unity and
        /// SkirmishMenu.unity - all 95 of them, at the time of writing.
        /// </summary>
        private static readonly string[] MenuFontGuids =
        {
            "a22d951180719b04d8a44cece769d37a", // AlegreyaSans-Medium SDF
            "74498b731f62b0b4dbfdfc107b481edd", // LTMuseum-Bold SDF (menu title)
        };

        /// <summary>Other Synty fonts are reported, never rewritten - a font the
        /// menus do not use is not this pass's business to restyle.</summary>
        private const string SyntyFontFolder = "Assets/Synty/InterfaceFantasyMenus/Fonts";

        [MenuItem(MenuPath)]
        private static void Fix()
        {
            var fixedUp = new List<string>();
            var alreadyNeutral = new List<string>();
            var missing = new List<string>();

            foreach (var guid in MenuFontGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var font = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null) { missing.Add(guid); continue; }

                var mat = font.material;
                if (mat == null || !mat.HasProperty(ShaderUtilities.ID_FaceColor)) continue;

                var face = mat.GetColor(ShaderUtilities.ID_FaceColor);
                if (IsNeutral(face)) { alreadyNeutral.Add(font.name); continue; }

                Undo.RecordObject(mat, "Fix Menu Font Face Colour");
                // Alpha is preserved: it is a legitimate way to dim a whole
                // font, and nothing here has any business overriding it.
                mat.SetColor(ShaderUtilities.ID_FaceColor,
                             new Color(1f, 1f, 1f, face.a));
                EditorUtility.SetDirty(mat);
                fixedUp.Add($"{font.name} ({face.r:F3}, {face.g:F3}, {face.b:F3}) -> white");
            }

            if (fixedUp.Count > 0) AssetDatabase.SaveAssets();

            var report = new List<string>();
            report.Add(fixedUp.Count > 0
                ? "Neutralised: " + string.Join("; ", fixedUp)
                : "Nothing to fix - every menu font already renders its authored colour.");
            if (alreadyNeutral.Count > 0)
                report.Add("Already neutral: " + string.Join(", ", alreadyNeutral));
            if (missing.Count > 0)
                report.Add("NOT FOUND by GUID (has the Synty package been reimported to a " +
                           "different GUID?): " + string.Join(", ", missing));

            foreach (var other in TintedFontsNotOwnedHere())
                report.Add("FYI, not touched: " + other + " also carries a tinted _FaceColor. " +
                           "The menus do not use it; add its GUID above if that changes.");

            Debug.Log("[MenuFontFaceColor] " + string.Join("\n  ", report));
        }

        private static bool IsNeutral(Color c) =>
            c.r >= 0.999f && c.g >= 0.999f && c.b >= 0.999f;

        private static IEnumerable<string> TintedFontsNotOwnedHere()
        {
            var owned = new HashSet<string>(MenuFontGuids);
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { SyntyFontFolder }))
            {
                if (owned.Contains(guid)) continue;
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                var mat = font != null ? font.material : null;
                if (mat == null || !mat.HasProperty(ShaderUtilities.ID_FaceColor)) continue;

                var face = mat.GetColor(ShaderUtilities.ID_FaceColor);
                if (!IsNeutral(face))
                    yield return $"{font.name} ({face.r:F3}, {face.g:F3}, {face.b:F3})";
            }
        }
    }
}

#endif // UNITY_EDITOR
