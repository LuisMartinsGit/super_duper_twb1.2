using UnityEngine;

namespace TheWaningBorder.Scenarios
{
    /// <summary>
    /// Scripted demo loop for the plaster-damage building:
    ///   1. Damage the intact building down to 10% HP (90% damage).
    ///   2. A worker walks over and repairs it back to 100%.
    ///   3. The building is damaged again until it is destroyed and collapses.
    /// The Painted Plaster substance reacts to HP in real time throughout.
    /// </summary>
    public class PlasterScenarioDirector : MonoBehaviour
    {
        public enum Phase
        {
            Warmup,
            FirstDamage,
            WorkerApproach,
            Repairing,
            WorkerReturn,
            FinalDamage,
            Destroyed
        }

        public ScenarioBuilding building;
        public Transform worker;

        [Min(1f)] public float damagePerSecond = 80f;
        [Min(1f)] public float repairPerSecond = 140f;
        [Range(0.01f, 0.99f)] public float firstPhaseHealthFloor = 0.10f;
        [Min(0f)] public float pauseBetweenPhases = 1.5f;

        [Min(0.1f)] public float workerSpeed = 3.5f;
        [Min(0.5f)] public float repairDistance = 2.5f;

        public Phase CurrentPhase { get; private set; } = Phase.Warmup;

        private Vector3 _workerHome;
        private float _workerBaseY;
        private float _pauseUntil;

        private void Start()
        {
            if (building == null)
                building = FindFirstObjectByType<ScenarioBuilding>();
            if (building == null)
            {
                Debug.LogError("[PlasterScenarioDirector] No ScenarioBuilding found in scene.", this);
                enabled = false;
                return;
            }

            building.Destroyed += OnBuildingDestroyed;

            if (worker != null)
            {
                _workerHome = worker.position;
                _workerBaseY = worker.position.y;
            }

            _pauseUntil = Time.time + pauseBetweenPhases;
        }

        private void OnDestroy()
        {
            if (building != null)
                building.Destroyed -= OnBuildingDestroyed;
        }

        private void OnBuildingDestroyed()
        {
            CurrentPhase = Phase.Destroyed;
        }

        private void Update()
        {
            if (Time.time < _pauseUntil)
                return;

            switch (CurrentPhase)
            {
                case Phase.Warmup:
                    Advance(Phase.FirstDamage);
                    break;

                case Phase.FirstDamage:
                    building.Damage(damagePerSecond * Time.deltaTime);
                    if (building.Health01 <= firstPhaseHealthFloor)
                        Advance(worker != null ? Phase.WorkerApproach : Phase.Repairing);
                    break;

                case Phase.WorkerApproach:
                    if (MoveWorkerTowards(RepairSpot()))
                        Advance(Phase.Repairing);
                    break;

                case Phase.Repairing:
                    AnimateHammering();
                    building.Repair(repairPerSecond * Time.deltaTime);
                    if (building.Health01 >= 1f)
                        Advance(worker != null ? Phase.WorkerReturn : Phase.FinalDamage);
                    break;

                case Phase.WorkerReturn:
                    if (MoveWorkerTowards(_workerHome))
                        Advance(Phase.FinalDamage);
                    break;

                case Phase.FinalDamage:
                    building.Damage(damagePerSecond * Time.deltaTime);
                    break;

                case Phase.Destroyed:
                    break;
            }
        }

        private void Advance(Phase next)
        {
            CurrentPhase = next;
            _pauseUntil = Time.time + pauseBetweenPhases;
            if (worker != null)
            {
                Vector3 p = worker.position;
                worker.position = new Vector3(p.x, _workerBaseY, p.z);
            }
        }

        private Vector3 RepairSpot()
        {
            Vector3 buildingPos = building.transform.position;
            Vector3 toWorker = _workerHome - buildingPos;
            toWorker.y = 0f;
            if (toWorker.sqrMagnitude < 0.001f)
                toWorker = Vector3.forward;
            return buildingPos + toWorker.normalized * repairDistance;
        }

        /// <summary>Moves the worker on the XZ plane. Returns true once it arrived.</summary>
        private bool MoveWorkerTowards(Vector3 target)
        {
            if (worker == null)
                return true;

            target.y = _workerBaseY;
            worker.position = Vector3.MoveTowards(worker.position, target, workerSpeed * Time.deltaTime);

            Vector3 look = building.transform.position - worker.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
                worker.rotation = Quaternion.LookRotation(look);

            return (worker.position - target).sqrMagnitude < 0.01f;
        }

        private void AnimateHammering()
        {
            if (worker == null)
                return;

            Vector3 p = worker.position;
            p.y = _workerBaseY + Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.25f;
            worker.position = p;
        }

        private void OnGUI()
        {
            const float width = 320f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 90f), GUI.skin.box);
            GUILayout.Label($"Plaster Damage Scenario - {PhaseLabel()}");
            GUILayout.Label($"HP: {Mathf.CeilToInt(building != null ? building.CurrentHealth : 0f)} / {(building != null ? building.maxHealth : 0)}");
            Rect bar = GUILayoutUtility.GetRect(width - 16f, 14f);
            GUI.Box(bar, GUIContent.none);
            if (building != null)
            {
                Rect fill = new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * building.Health01, bar.height - 2f);
                Color previous = GUI.color;
                GUI.color = Color.Lerp(Color.red, Color.green, building.Health01);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
                GUI.color = previous;
            }
            GUILayout.EndArea();
        }

        private string PhaseLabel()
        {
            switch (CurrentPhase)
            {
                case Phase.Warmup: return "Intact";
                case Phase.FirstDamage: return "Taking damage";
                case Phase.WorkerApproach: return "Worker incoming";
                case Phase.Repairing: return "Repairing";
                case Phase.WorkerReturn: return "Worker leaving";
                case Phase.FinalDamage: return "Final assault";
                case Phase.Destroyed: return "Destroyed";
                default: return CurrentPhase.ToString();
            }
        }
    }
}
