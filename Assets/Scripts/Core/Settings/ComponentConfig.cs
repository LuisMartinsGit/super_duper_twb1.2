using UnityEngine;

namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// Runtime accessor for the per-component config assets, mirroring
    /// TechCatalog: load one catalog out of Resources/ on first use, then serve
    /// everything it references.
    ///
    /// <see cref="Require{T}"/> deliberately has NO fallback. A component whose
    /// config is missing is a data bug, and it is caught loudly here rather than
    /// papered over by C# initialisers that a designer can neither see nor edit.
    /// </summary>
    public static class ComponentConfig
    {
        const string CatalogResourceName = "ComponentConfigCatalog";

        static ComponentConfigCatalog _catalog;
        static bool _loadAttempted;

        /// <summary>The config of type T, or null if it is not in the catalog.</summary>
        public static T Find<T>() where T : ScriptableObject
        {
            if (!_loadAttempted)
            {
                _catalog = Resources.Load<ComponentConfigCatalog>(CatalogResourceName);
                _loadAttempted = true;

                if (_catalog == null)
                    Debug.LogError($"[ComponentConfig] Resources/{CatalogResourceName}.asset is missing. "
                                 + "Every component that reads its settings from an asset will fail.");
            }

            if (_catalog?.configs == null) return null;

            foreach (var so in _catalog.configs)
                if (so is T typed) return typed;

            return null;
        }

        /// <summary>
        /// The config of type T. Logs an error naming the missing asset if it is
        /// absent — there is no code-side default to fall back to, by design.
        /// </summary>
        public static T Require<T>() where T : ScriptableObject
        {
            var config = Find<T>();

            if (config == null)
                Debug.LogError($"[ComponentConfig] No {typeof(T).Name} in the catalog. "
                             + "Add its asset beside the class it configures and run "
                             + "Waning Border > Component Config > Rebuild Catalog.");

            return config;
        }

        /// <summary>Drop the cache — the editor tooling calls this after a rebuild.</summary>
        public static void Invalidate()
        {
            _catalog = null;
            _loadAttempted = false;
        }
    }
}
