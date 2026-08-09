// OverpassBridgeAuthoring.cs
// Scene-authoring component for overpass bridges (dual-level: walk OVER the
// deck on nav layer 1, walk UNDER it on the ground layer).
//
// Place on the root of an overpass object, positioned at the span center,
// with the local +Z axis pointing along the span. Optionally attach a
// BridgeSurface to the same object (with the deck meshes as children) so
// height sampling follows the actual deck geometry; without one the deck
// rides at the rampart deck height (LayerTransitionSystem.DeckY = 4 m) —
// keep authored deck meshes at that height so clicks on the deck plane
// resolve correctly (RTSInputManager.TryGetRampartClick projects onto it).
//
// On Start this creates the ECS entities the nav stack consumes:
//   * one OverpassBridge (span data, stamped by CostFieldStampSystem), and
//   * two OverpassRampTag ramp entities (ungated layer access points for
//     LayeredMoveSystem) at the deck ends.
//
// Overpasses are STATIC scene furniture, like BridgeSurface bridges:
// spawn/move at runtime is not supported.
//
// Location: Assets/Scripts/World/Terrain/OverpassBridgeAuthoring.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.World.Terrain
{
    [DisallowMultipleComponent]
    public class OverpassBridgeAuthoring : MonoBehaviour
    {
        [Header("Span (local +Z axis, centered on this transform)")]
        [Tooltip("Total deck length in meters, along the object's local +Z.")]
        [SerializeField] private float length = 16f;
        [Tooltip("Full deck width in meters.")]
        [SerializeField] private float width = 4f;

        void Start()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogWarning($"[OverpassBridge] '{name}': no ECS world — bridge not registered.");
                return;
            }
            var em = world.EntityManager;

            Vector3 axis = transform.forward;
            axis.y = 0f;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
            axis.Normalize();

            Vector3 c = transform.position;
            float3 start = new float3(c.x - axis.x * length * 0.5f, 0f, c.z - axis.z * length * 0.5f);
            float3 end = new float3(c.x + axis.x * length * 0.5f, 0f, c.z + axis.z * length * 0.5f);

            var bridge = em.CreateEntity();
            em.AddComponentData(bridge, new OverpassBridge
            {
                Start = start,
                End = end,
                Width = math.max(1f, width),
            });

            CreateRamp(em, start);
            CreateRamp(em, end);
        }

        private static void CreateRamp(EntityManager em, float3 at)
        {
            float y = TerrainUtility.GetHeight(at.x, at.z);
            var ramp = em.CreateEntity();
            em.AddComponent<OverpassRampTag>(ramp);
            em.AddComponentData(ramp, LocalTransform.FromPosition(new float3(at.x, y, at.z)));
        }
    }
}
