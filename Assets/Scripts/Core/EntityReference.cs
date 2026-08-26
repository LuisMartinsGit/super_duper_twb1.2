// EntityReference.cs
// The link from a view GameObject back to its ECS entity.
// Location: Assets/Scripts/Core/EntityReference.cs
//
// Four lines that used to sit at the bottom of RTSInputManager.cs, 1,390
// lines into a file about mouse clicks, because that is where someone
// happened to need it first. It is not an input concept: roughly 25
// presentation files -- every unit and building visual, the spawn pipeline,
// the prefab swapper -- ask a GameObject which entity it belongs to.
//
// Living in Input meant the content layer's visuals depended on the input
// layer for it, which is backwards and blocks Input from moving up beside
// the rest of the presentation code.
//
// Safe to move: nothing serialises it. It is only ever AddComponent'd or
// GetComponent'd at runtime (18 and 26 call sites), and no scene or prefab
// references the file it came from.

using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Core
{
    /// <summary>
    /// Links a GameObject to an ECS Entity. Attach to visual representations
    /// of entities.
    /// </summary>
    public class EntityReference : MonoBehaviour
    {
        public Entity Entity;
    }
}
