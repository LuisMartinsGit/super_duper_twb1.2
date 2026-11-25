// BuildingAnchors.cs
// THE EMPTY MARKERS A HAND-AUTHORED BUILDING PREFAB CARRIES (2026-11-12).
//
// Developer, on the new Garrison L1: "it has no floor. It has some empty
// gameobjects in it: corners (NSEW) — these should anchor the building on
// inclines; the building height should be such that all the corners are above
// ground. It now has a Unit spawn point and a building exit point: on spawn,
// the unit should appear on the unit spawn and walk out of the building by
// moving to the exit point."
//
// The markers are plain empties found by name (the same kind of convention
// as the "Footprint" group): direct children of the prefab root, or children
// of a group under it (Garrison_2 keeps them in "0 [snap] - Logic"). Their
// positions are read in the ROOT's frame either way.
//
//   *_corner      (N_corner, S_corner, E_corner, W_corner) — the ground
//                 contact points. The view is lifted until every one of them
//                 is on or above the terrain (GroundY). A prefab that carries
//                 them is authored at unit scale in the entity's own frame:
//                 it takes NO footprint fit and NO pivot offset
//                 (PresentationSpawnSystem.ComputeBuildingViewScale), so a
//                 marker's local position is its position in the world.
//   Footprint_center  the middle of the plot, on the root; one more ground
//                 contact point, so a hump inside the plot cannot come
//                 through the floor. Edge midpoints are corners too
//                 (NE_corner, SE_corner, SW_corner, NW_corner).
//   Unit_spawn    where a trained unit appears (inside the building).
//   Exit_point    where it walks to before it takes its rally order
//                 (TrainingSystem.SpawnUnit, BuildingExitSystem).
//
// Read off the prefab ASSET the building's SO names, once per prefab — the
// simulation reads the same asset on every peer, so the points are the same
// on every peer. A prefab without a marker simply has none: the building
// keeps the behaviour it had.

using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Buildings
{
    public sealed class BuildingAnchors
    {
        public const string CornerSuffix = "_corner";
        /// <summary>The middle of the plot: a ground point as the corners are.</summary>
        public const string FootprintCenterName = "Footprint_center";
        public const string UnitSpawnName = "Unit_spawn";
        public const string ExitPointName = "Exit_point";

        /// <summary>Corner markers in root-local units (root scale not applied).</summary>
        public Vector3[] Corners = System.Array.Empty<Vector3>();
        /// <summary>Unit_spawn / Exit_point in root-local units.</summary>
        public Vector3 UnitSpawn, ExitPoint;
        /// <summary>Both Unit_spawn and Exit_point are authored.</summary>
        public bool HasExitPath;
        /// <summary>The prefab root's authored (uniform) scale.</summary>
        public float AuthoredScale = 1f;

        public bool HasCorners => Corners.Length > 0;

        private static readonly BuildingAnchors None = new BuildingAnchors();
        private static readonly Dictionary<int, BuildingAnchors> _cache = new Dictionary<int, BuildingAnchors>();

        // A prefab edited between two Play sessions must be read again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _cache.Clear();

        /// <summary>The markers of <paramref name="prefab"/> (never null).</summary>
        public static BuildingAnchors For(GameObject prefab)
        {
            if (prefab == null) return None;
            int key = prefab.GetInstanceID();
            if (_cache.TryGetValue(key, out var found)) return found;

            var root = prefab.transform;
            var corners = new List<Vector3>(4);
            bool hasSpawn = false, hasExit = false;
            Vector3 spawn = default, exit = default;
            void Read(Transform child)
            {
                string n = child.name;
                // In the root's own units (its scale is applied later, as AuthoredScale).
                Vector3 local = child.parent == root
                    ? child.localPosition
                    : root.InverseTransformPoint(child.position);
                if (n.EndsWith(CornerSuffix, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, FootprintCenterName, System.StringComparison.OrdinalIgnoreCase))
                    corners.Add(local);
                else if (string.Equals(n, UnitSpawnName, System.StringComparison.OrdinalIgnoreCase))
                { spawn = local; hasSpawn = true; }
                else if (string.Equals(n, ExitPointName, System.StringComparison.OrdinalIgnoreCase))
                { exit = local; hasExit = true; }
            }
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                Read(child);
                // One level down: a logic group holding the markers.
                for (int j = 0; j < child.childCount; j++) Read(child.GetChild(j));
            }

            BuildingAnchors result = None;
            if (corners.Count > 0 || (hasSpawn && hasExit))
            {
                var s = root.localScale;
                result = new BuildingAnchors
                {
                    Corners = corners.ToArray(),
                    UnitSpawn = spawn,
                    ExitPoint = exit,
                    HasExitPath = hasSpawn && hasExit,
                    AuthoredScale = (s.x + s.y + s.z) / 3f,
                };
            }
            _cache[key] = result;
            return result;
        }

        /// <summary>
        /// A marker's simulated world position for <paramref name="building"/>.
        /// A building's view is its entity turned half a turn about Y
        /// (PresentationSpawnSystem.BuildingVisualOffsetY180), which is the
        /// X / Z sign flip here — exact, no trigonometry. Y is the building's.
        /// </summary>
        public float3 ToWorld(in LocalTransform building, Vector3 local)
        {
            float k = AuthoredScale * building.Scale;
            float3 turned = new float3(-local.x, 0f, -local.z) * k;
            float3 p = building.Position + math.mul(building.Rotation, turned);
            p.y = building.Position.y;
            return p;
        }

        /// <summary>
        /// The height the view's root takes so that every corner marker is on
        /// or above the terrain: the highest ground under any of them (less
        /// the marker's own height above the root). <paramref name="viewRot"/>
        /// and <paramref name="viewScale"/> are the view's — the half turn and
        /// the authored scale included. <paramref name="fallbackY"/> when the
        /// prefab has no corners.
        /// </summary>
        public float GroundY(Vector3 rootPos, Quaternion viewRot, float viewScale, float fallbackY)
        {
            if (Corners.Length == 0) return fallbackY;
            float y = float.NegativeInfinity;
            for (int i = 0; i < Corners.Length; i++)
            {
                Vector3 c = Corners[i] * viewScale;
                Vector3 w = rootPos + viewRot * new Vector3(c.x, 0f, c.z);
                y = Mathf.Max(y, TerrainUtility.GetHeight(w.x, w.z) - c.y);
            }
            return y;
        }
    }
}
