using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// References every per-component config asset so they load without a magic
    /// Resources/ folder — the same trick TechTreeCatalog plays for the tech
    /// tree, and for the same reason: the config assets have to live BESIDE the
    /// code they configure, and Resources.Load cannot reach them there.
    ///
    /// Only this catalog sits in Resources/. Unity pulls the assets it
    /// references into the build automatically.
    ///
    /// Rebuilt by Waning Border > Component Config > Rebuild Catalog, which
    /// pairs each config asset with the class it is named after.
    /// </summary>
    public sealed class ComponentConfigCatalog : ScriptableObject
    {
        /// <summary>
        /// One entry per configured component, looked up by config type. Held
        /// as ScriptableObject because the configs live in assemblies this one
        /// does not reference — TheWaningBorder.Presentation among them.
        /// </summary>
        public List<ScriptableObject> configs = new List<ScriptableObject>();
    }
}
