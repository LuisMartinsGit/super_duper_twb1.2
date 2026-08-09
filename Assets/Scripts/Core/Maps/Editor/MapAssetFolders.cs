// MapAssetFolders.cs
// EDITOR-ONLY: one correct way to make sure an asset folder exists before
// writing into it.
//
// The trap this exists to close: Directory.CreateDirectory puts a folder on
// disk but leaves it unknown to the AssetDatabase, and
// AssetDatabase.CreateAsset into an unimported folder fails with
//   UnityException: Creating asset at path <path> failed.
// It cost two runs of the map generator — once on the map folder itself,
// once on Assets/UI/Resources when MapInfoBaker went to write the index.
// Both were the same mistake in different files, so the fix lives in one
// place now.
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef with no separate editor assembly; the Editor/ folder name alone
// does not exclude it from player builds.

#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapAssetFolders
    {
        /// <summary>
        /// Guarantee <paramref name="assetPath"/> ("Assets/Foo/Bar") exists
        /// AND is known to the AssetDatabase, creating missing parents on the
        /// way down. Safe on a folder that already exists on disk but was
        /// never imported. Throws if the folder genuinely cannot be made —
        /// callers writing assets should not proceed past that.
        /// </summary>
        public static void Ensure(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            assetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            // Present on disk but unimported (e.g. left by a failed run):
            // a refresh adopts it and writes the .meta.
            if (Directory.Exists(assetPath))
            {
                AssetDatabase.Refresh();
                if (AssetDatabase.IsValidFolder(assetPath)) return;
            }

            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            if (!AssetDatabase.IsValidFolder(parent)) Ensure(parent);

            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, leaf)))
                throw new IOException($"Could not create asset folder '{assetPath}'.");
        }
    }
}
#endif
