// NavMeshStaticObstacle.cs
// Marker component. Any GameObject carrying this is fed into the runtime
// NavMeshManager's initial bake as a Mesh source — the navmesh bakes around
// the actual mesh geometry instead of ignoring the GameObject.
//
// Used by ProceduralCliffGenerator (auto-added on bake) and any other
// non-ECS static map prop that should block pathing.
//
// Location: Assets/Scripts/Systems/Movement/NavMeshStaticObstacle.cs

using UnityEngine;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Tag MonoBehaviour. The mesh on this GameObject's MeshFilter is used as
    /// a NavMeshBuildSource (shape = Mesh) during NavMeshManager's initial
    /// bake. Cliffs, rocks, and other static scene props that aren't ECS
    /// entities should carry this so the navmesh routes around them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class NavMeshStaticObstacle : MonoBehaviour { }
}
