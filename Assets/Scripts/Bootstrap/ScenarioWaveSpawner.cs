// ScenarioWaveSpawner.cs
// Periodically spawns passive walker soldiers from a ring outside a target
// area; each walker heads toward a random point near the centre and never
// fights back. Used by the PatrolDefense scenario to feed the patrolling
// Veilstingers a stream of targets without bidirectional combat.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public class ScenarioWaveSpawner : MonoBehaviour
    {
        public Vector3 Center;
        /// <summary>Distance from Center where soldiers spawn (outside the patrol).</summary>
        public float SpawnRadius = 35f;
        /// <summary>Soldiers walk toward a random point inside this radius of Center.</summary>
        public float InnerTargetRadius = 5f;
        /// <summary>Seconds between successive spawns.</summary>
        public float Interval = 3f;
        /// <summary>Unit ID passed to UnitFactory.Create.</summary>
        public string UnitId = "Swordsman";
        /// <summary>Spawned soldiers' faction — must differ from the Veilstingers'.</summary>
        public Faction SoldierFaction = Faction.Blue;

        private float _timer = 0f;

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < Interval) return;
            _timer = 0f;
            SpawnOne();
        }

        private void SpawnOne()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Spawn somewhere on the outer ring.
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector3 spawnPos = Center + new Vector3(
                Mathf.Cos(angle) * SpawnRadius, 0f, Mathf.Sin(angle) * SpawnRadius);
            spawnPos.y = TerrainUtility.GetHeight(spawnPos.x, spawnPos.z);

            // Walk toward a random point near the centre.
            Vector2 inner = UnityEngine.Random.insideUnitCircle * InnerTargetRadius;
            Vector3 targetPos = Center + new Vector3(inner.x, 0f, inner.y);
            targetPos.y = TerrainUtility.GetHeight(targetPos.x, targetPos.z);

            var soldier = UnitFactory.Create(em,
                UnitId,
                new float3(spawnPos.x, spawnPos.y, spawnPos.z),
                SoldierFaction);
            if (soldier == Entity.Null) return;

            // Strip combat capability — soldiers are passive walkers. Without
            // Damage and Target the combat systems' queries can't match the
            // soldier (they all WithAll<Target, Damage> on attackers), so the
            // soldier never seeks or engages.
            if (em.HasComponent<Damage>(soldier))
                em.RemoveComponent<Damage>(soldier);
            if (em.HasComponent<Target>(soldier))
                em.RemoveComponent<Target>(soldier);

            // Push toward the inner waypoint. Swordsman.Create doesn't
            // pre-allocate DesiredDestination, so we have to add the
            // component if it's missing — otherwise the soldier spawns and
            // stands still at the outer ring.
            var dest = new DesiredDestination
            {
                Position = new float3(targetPos.x, targetPos.y, targetPos.z),
                Has = 1
            };
            if (em.HasComponent<DesiredDestination>(soldier))
                em.SetComponentData(soldier, dest);
            else
                em.AddComponentData(soldier, dest);
        }
    }
}
