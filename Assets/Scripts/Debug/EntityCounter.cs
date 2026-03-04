using UnityEngine;
using Unity.Entities;

public class DebugEntityCounter : MonoBehaviour
{   
    bool active = true;
    void Update()
    {
        if (UnityEngine.Input.GetKeyDown(KeyCode.F1) && active)
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