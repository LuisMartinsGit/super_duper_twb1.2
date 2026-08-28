// MapInfoIndex.cs
// Resources-hosted registry of every MapInfo asset. Exists so the per-map
// MapInfo assets (which live in the map folders, outside Resources) are
// referenced by something that ships in player builds and can be found at
// runtime. The bake tool (MapInfoBaker) keeps it up to date; it can also
// be edited by hand at Assets/UI/Resources/MapInfoIndex.asset.

using UnityEngine;

namespace TheWaningBorder.Core.Maps
{
    [CreateAssetMenu(fileName = "MapInfoIndex", menuName = "Waning Border/Map Info Index")]
    public sealed class MapInfoIndex : ScriptableObject
    {
        public MapInfo[] Maps = new MapInfo[0];

        private static MapInfoIndex _cached;
        private static bool _warned;

        /// <summary>MapInfo for the given scene name, or null when the map
        /// has no authored info asset (callers should fall back gracefully).</summary>
        public static MapInfo For(string sceneName)
        {
            // Retry the load until it succeeds rather than latching the first
            // result. The old code set a _searched flag on the FIRST call and
            // never looked again, so one lookup that happened before the asset
            // was importable (a fresh domain reload, a bake in flight) poisoned
            // every map's info for the rest of the session — maps then rendered
            // with fallback "2-8P" text and a wrong player cap. Resources.Load
            // caches internally, so the repeat cost on the success path is a
            // dictionary hit.
            if (_cached == null)
            {
                _cached = Resources.Load<MapInfoIndex>("MapInfoIndex");
                if (_cached == null && !_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[MapInfoIndex] Resources/MapInfoIndex not found — map " +
                                     "descriptions and player caps fall back to defaults. " +
                                     "Bake a map to create it.");
                }
            }
            if (_cached == null || _cached.Maps == null) return null;

            foreach (var info in _cached.Maps)
                if (info != null && info.SceneName == sceneName)
                    return info;
            return null;
        }
    }
}
