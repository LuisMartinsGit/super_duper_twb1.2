// AbilityBuildingGhost.cs
// The ghost GroundPlacement shows for a building an ABILITY raises
// (docs/Design/Build_Grid.md § Buildings raised by abilities, 2026-11-11): the
// same finished-building preview WorkerCommandPanel puts under the cursor — the
// SO's art, fitted to the footprint and stood on the ground the way the real
// spawn does — or, with no art, a block the size of the footprint. Kept out of
// the worker panel so that file stays the worker's palette.

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.UI.World
{
    public static class AbilityBuildingGhost
    {
        /// <summary>A fresh ghost of <paramref name="buildingId"/> (a BuildingDefSO
        /// id) for the local player; never null.</summary>
        public static GameObject ForBuilding(string buildingId)
        {
            var size = BuildCommandHelper.GetBuildingSize(buildingId);
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            GameObject ghost = null;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                ghost = PresentationSpawnSystem.CreateFinishedBuildingPreview(
                    em, buildingId, GameSettings.LocalPlayerFaction, out _);
                if (ghost != null)
                {
                    // Scale and centre exactly like the worker's ghost
                    // (WorkerCommandPanel.StartPlacement): the footprint fit,
                    // and the bounds centre / lowest point on the placement point.
                    float rootScale = PresentationSpawnSystem.ComputeFinishedViewScale(
                        em, buildingId, GameSettings.LocalPlayerFaction,
                        ghost, size.x, size.y, out Vector3 fitOffset);
                    ghost.transform.localScale = Vector3.one * rootScale;
                    if (fitOffset != Vector3.zero)
                        for (int c = 0; c < ghost.transform.childCount; c++)
                            ghost.transform.GetChild(c).localPosition -= fitOffset;
                }
            }
            return ghost != null ? Finish(ghost) : Block(size);
        }

        /// <summary>A ghost from a model prefab at a fixed scale (the Counting
        /// House: CountingHouse.asset modelPrefab / modelScale, the scale its
        /// spawn uses), or a block of <paramref name="sizeMeters"/> with none.</summary>
        public static GameObject ForModel(GameObject prefab, float scale, int2 sizeMeters)
        {
            if (prefab == null) return Block(sizeMeters);
            var root = new GameObject("AbilityPlacementGhost");
            var model = Object.Instantiate(prefab, root.transform);
            model.SetActive(true);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * scale;
            return Finish(root);
        }

        private static GameObject Block(int2 sizeMeters)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.localScale = new Vector3(sizeMeters.x, math.max(sizeMeters.x, sizeMeters.y) * 0.5f, sizeMeters.y);
            return Finish(cube);
        }

        private static GameObject Finish(GameObject ghost)
        {
            ghost.name = "AbilityPlacementGhost";
            foreach (var l in ghost.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            return ghost;
        }
    }
}
