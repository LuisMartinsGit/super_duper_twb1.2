// VfxSpawn.cs
// Shared helpers for spawning authored particle prefabs (Lana Studio, Hovl)
// on units, buildings and ground points: world-size scaling regardless of the
// parent's scale, one-shot vs looping, and a fade-out that lets live particles
// finish instead of popping. Presentation only.

using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class VfxSpawn
    {
        /// <summary>True when the prefab actually draws something — the
        /// PLACEHOLDER_* effect prefabs in the ability folders carry no
        /// particle system and are skipped.</summary>
        public static bool HasParticles(GameObject prefab)
            => prefab != null && prefab.GetComponentInChildren<ParticleSystem>(true) != null;

        /// <summary>
        /// Instantiate <paramref name="prefab"/> at <paramref name="position"/>,
        /// parented to <paramref name="parent"/> (so fog of war hides it with
        /// the unit or building), at <paramref name="worldScale"/> × its
        /// authored size. The prefab's own root rotation is kept — flat ground
        /// effects are authored lying down. <paramref name="loop"/> forces
        /// every particle system to loop (true) or play once (false).
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position, float worldScale, bool loop)
        {
            if (prefab == null) return null;
            var fx = Object.Instantiate(prefab, position, prefab.transform.rotation, parent);
            fx.name = prefab.name;
            // Follows its parent's position, never its turning.
            if (parent != null) fx.AddComponent<VfxKeepWorldRotation>();
            SetWorldScale(fx.transform, prefab.transform.localScale * worldScale);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = loop;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.stopAction = ParticleSystemStopAction.None;
            }
            return fx;
        }

        /// <summary>Longest a one-shot instance can still be drawing:
        /// duration + max start lifetime over its systems.</summary>
        public static float OneShotLength(GameObject fx)
        {
            float t = 0.5f;
            if (fx == null) return t;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main;
                t = Mathf.Max(t, m.startDelay.constantMax + m.duration + m.startLifetime.constantMax);
            }
            return t;
        }

        /// <summary>Spawn once and destroy when it has finished drawing.</summary>
        public static GameObject OneShot(GameObject prefab, Transform parent, Vector3 position, float worldScale)
        {
            var fx = Spawn(prefab, parent, position, worldScale, loop: false);
            if (fx != null) Object.Destroy(fx, OneShotLength(fx));
            return fx;
        }

        /// <summary>Stop emitting, let live particles finish, then destroy.
        /// Unparented first, so a unit that dies does not take it along.</summary>
        public static void FadeOut(GameObject fx, float seconds)
        {
            if (fx == null) return;
            fx.transform.SetParent(null, true);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(fx, seconds);
        }

        /// <summary>Scale a child so its WORLD scale is <paramref name="world"/>,
        /// whatever scale its parent carries.</summary>
        public static void SetWorldScale(Transform t, Vector3 world)
        {
            var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(world.x / Mathf.Max(1e-4f, Mathf.Abs(p.x)),
                                       world.y / Mathf.Max(1e-4f, Mathf.Abs(p.y)),
                                       world.z / Mathf.Max(1e-4f, Mathf.Abs(p.z)));
        }

        /// <summary>Mesh bounds of a view (particle / trail / line renderers
        /// excluded — a world-space particle child can be map-sized).</summary>
        public static bool MeshBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            if (go == null) return false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>The view of an entity, if it has one and it is shown
        /// (fog of war deactivates hidden views).</summary>
        public static bool TryVisibleView(Entity e, out GameObject view)
        {
            view = null;
            return EntityViewManager.Instance != null
                && EntityViewManager.Instance.TryGetView(e, out view)
                && view != null && view.activeInHierarchy;
        }

        /// <summary>Foot of a view: its pivot (views stand on the ground).</summary>
        public static Vector3 Foot(GameObject view) => view.transform.position;

        /// <summary>Height of a view's meshes, metres (2 when it has none).</summary>
        public static float Height(GameObject view)
            => MeshBounds(view, out var b) ? Mathf.Max(0.5f, b.size.y) : 2f;

        /// <summary>Half the longer horizontal side of a view, metres.</summary>
        public static float HalfWidth(GameObject view)
            => MeshBounds(view, out var b) ? Mathf.Max(0.3f, Mathf.Max(b.extents.x, b.extents.z)) : 1f;
    }
}
