using UnityEngine;
using Unity.Entities;

public class DebugEntityCounter : MonoBehaviour
{
    // Fix #237: removed 'bool active = true' field that was never set to false.
    void Update()
    {
        if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null) { Debug.Log("No ECS World!"); return; }
            
            var em = world.EntityManager;
            
            var units = em.CreateEntityQuery(typeof(UnitTag)).CalculateEntityCount();
            var buildings = em.CreateEntityQuery(typeof(BuildingTag)).CalculateEntityCount();
            var halls = em.CreateEntityQuery(typeof(HallTag)).CalculateEntityCount();
            
            Debug.Log($"[DEBUG] Units: {units}, Buildings: {buildings}, Halls: {halls}");
        }
    }
}