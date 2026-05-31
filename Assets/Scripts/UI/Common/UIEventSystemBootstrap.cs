// File: Assets/Scripts/UI/Common/UIEventSystemBootstrap.cs
// Single source of truth for the scene's UnityEngine UI EventSystem.
//
// History: MinimapRenderer and MinimapUI each ran their own
//   `if (FindFirstObjectByType<EventSystem>() == null) ... create ...`
// dance during Awake. FindFirstObjectByType returns null inside the same
// Awake-frame for OTHER freshly-instantiated EventSystems, so when both
// renderers spun up together each one created its own copy. Scene reloads
// (returning to menu + new game) compounded the problem until Unity warned
// about 17 EventSystems at once.
//
// Centralised policy: a STATIC cache holds the singleton across concurrent
// Awake calls so two scripts racing don't each create their own. Every call
// also sweeps for stragglers and destroys them.

using UnityEngine;
using UnityEngine.EventSystems;

namespace TheWaningBorder.UI.Common
{
    public static class UIEventSystemBootstrap
    {
        // Static cache survives across scene loads. Unity's Object==null
        // operator returns true once the underlying GameObject is destroyed,
        // so we naturally re-create when the previous scene's EventSystem
        // was torn down with it.
        private static EventSystem _shared;

        // Reset the static cache when the editor enters play mode. Without
        // this, a stale Unity managed reference can survive a domain reload
        // and trip the next session's idempotency check. Cheap and only
        // runs at editor play-start (no cost in built players).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _shared = null;
        }

        /// <summary>
        /// Ensure exactly one EventSystem (+ StandaloneInputModule) is alive.
        /// Returns the surviving instance. Idempotent — concurrent Awake
        /// callers all get the same EventSystem; any duplicates that already
        /// exist (from race conditions or scene-load merges) are destroyed.
        /// </summary>
        public static EventSystem EnsureSingle()
        {
            // Fast path: we already created or adopted one this run. Sweep
            // for any duplicates that have appeared since (e.g. a prefab
            // instantiated an EventSystem on Awake) and destroy them.
            if (_shared != null)
            {
                SweepDuplicates(_shared);
                return _shared;
            }

            // Cold path: find any EventSystem the scene file or another
            // script created before us, then either adopt it or make our own.
            var existing = Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (existing.Length > 0)
            {
                _shared = existing[0];
                for (int i = 1; i < existing.Length; i++)
                    if (existing[i] != null && existing[i].gameObject != null)
                        Object.Destroy(existing[i].gameObject);
                return _shared;
            }

            var go = new GameObject("EventSystem",
                typeof(EventSystem), typeof(StandaloneInputModule));
            go.hideFlags = HideFlags.DontSave;
            _shared = go.GetComponent<EventSystem>();
            return _shared;
        }

        static void SweepDuplicates(EventSystem keep)
        {
            var all = Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i] == keep) continue;
                if (all[i].gameObject != null)
                    Object.Destroy(all[i].gameObject);
            }
        }
    }
}
