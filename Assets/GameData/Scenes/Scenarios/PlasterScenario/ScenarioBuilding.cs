using System;
using System.Collections;
using UnityEngine;

namespace TheWaningBorder.Scenarios
{
    /// <summary>
    /// Standalone building health for the plaster-damage scenario. Mirrors the
    /// game's ECS Health (Value/Max) semantics but lives on a plain GameObject so
    /// the test scene runs without the ECS world. Pushes every HP change into the
    /// PlasterDamageSubstance on the same object, then collapses when destroyed.
    /// </summary>
    public class ScenarioBuilding : MonoBehaviour
    {
        [Min(1)] public int maxHealth = 1000;
        [Tooltip("Seconds the collapse (sink + shrink) takes once HP hits zero.")]
        [Min(0.1f)] public float collapseSeconds = 2.5f;
        [Tooltip("How far the building sinks into the ground while collapsing.")]
        [Min(0f)] public float collapseSinkDepth = 3f;

        public float CurrentHealth { get; private set; }
        public float Health01 => Mathf.Clamp01(CurrentHealth / maxHealth);
        public bool IsDestroyed { get; private set; }

        public event Action<float> HealthChanged;
        public event Action Destroyed;

        private PlasterDamageSubstance _plaster;

        private void Awake()
        {
            CurrentHealth = maxHealth;
            _plaster = GetComponent<PlasterDamageSubstance>();
        }

        public void Damage(float amount)
        {
            if (IsDestroyed)
                return;

            SetHealth(CurrentHealth - Mathf.Max(0f, amount));

            if (CurrentHealth <= 0f)
            {
                IsDestroyed = true;
                Destroyed?.Invoke();
                StartCoroutine(Collapse());
            }
        }

        public void Repair(float amount)
        {
            if (IsDestroyed)
                return;

            SetHealth(CurrentHealth + Mathf.Max(0f, amount));
        }

        private void SetHealth(float value)
        {
            CurrentHealth = Mathf.Clamp(value, 0f, maxHealth);
            if (_plaster != null)
                _plaster.SetHealth01(Health01);
            HealthChanged?.Invoke(Health01);
        }

        private IEnumerator Collapse()
        {
            Vector3 startPosition = transform.position;
            Vector3 startScale = transform.localScale;
            Vector3 endPosition = startPosition + Vector3.down * collapseSinkDepth;
            Vector3 endScale = new Vector3(startScale.x * 0.85f, startScale.y * 0.1f, startScale.z * 0.85f);

            for (float t = 0f; t < 1f; t += Time.deltaTime / collapseSeconds)
            {
                float eased = t * t;
                transform.position = Vector3.Lerp(startPosition, endPosition, eased);
                transform.localScale = Vector3.Lerp(startScale, endScale, eased);
                yield return null;
            }

            gameObject.SetActive(false);
        }
    }
}
