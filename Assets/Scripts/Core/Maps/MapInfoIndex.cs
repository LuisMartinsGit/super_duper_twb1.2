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
        private static bool _searched;

        /// <summary>MapInfo for the given scene name, or null when the map
        /// has no authored info asset (callers should fall back gracefully).</summary>
        public static MapInfo For(string sceneName)
        {
            if (!_searched)
            {
                _cached = Resources.Load<MapInfoIndex>("MapInfoIndex");
                _searched = true;
            }
            if (_cached == null || _cached.Maps == null) return null;

            foreach (var info in _cached.Maps)
                if (info != null && info.SceneName == sceneName)
                    return info;
            return null;
        }
    }
}
