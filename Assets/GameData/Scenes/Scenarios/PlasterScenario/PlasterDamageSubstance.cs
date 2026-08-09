using Adobe.Substance.Runtime;
using UnityEngine;

namespace TheWaningBorder.Scenarios
{
    /// <summary>
    /// Drives the Painted Plaster substance inputs from a 0..1 health value and
    /// re-renders the graph at runtime, so the material visually tracks building HP.
    /// Each channel maps a damage window (0 = intact, 1 = destroyed) onto one
    /// substance float input: paint flakes first, then plaster falls exposing the
    /// bricks, then the bricks themselves crack and degrade.
    /// </summary>
    [RequireComponent(typeof(SubstanceRuntimeGraph))]
    public class PlasterDamageSubstance : MonoBehaviour
    {
        [System.Serializable]
        public class Channel
        {
            [Tooltip("Substance input identifier in the SBSAR.")]
            public string input;
            [Tooltip("Input value at full health.")]
            public float intactValue;
            [Tooltip("Input value at zero health.")]
            public float ruinedValue;
            [Tooltip("Damage fraction at which this channel starts changing.")]
            [Range(0f, 1f)] public float damageStart;
            [Tooltip("Damage fraction at which this channel reaches its ruined value.")]
            [Range(0f, 1f)] public float damageEnd = 1f;

            public Channel() { }

            public Channel(string input, float intactValue, float ruinedValue, float damageStart, float damageEnd)
            {
                this.input = input;
                this.intactValue = intactValue;
                this.ruinedValue = ruinedValue;
                this.damageStart = damageStart;
                this.damageEnd = damageEnd;
            }
        }

        [Tooltip("Damage-to-input mappings. Defaults are tuned for PaintedPlasterSubstance002.")]
        public Channel[] channels =
        {
            new Channel("paint_blending",         1f,    -1f,   0.00f, 0.45f),
            new Channel("plaster_coverage",       1f,     0f,   0.10f, 0.85f),
            new Channel("bricks_cracks_intensity", 0f,    1f,   0.30f, 1.00f),
            new Channel("bricks_age",             0.05f,  0.9f, 0.40f, 1.00f),
            new Channel("bricks_degradation",     0f,     1f,   0.55f, 1.00f),
        };

        [Tooltip("Minimum seconds between substance re-renders while the value keeps changing.")]
        [Min(0f)] public float minRenderInterval = 0.15f;

        private SubstanceRuntimeGraph _graph;
        private float _pendingDamage;
        private float _appliedDamage = -1f;
        private bool _renderInFlight;
        private float _lastRenderTime = float.NegativeInfinity;
        private bool _ready;

        private void Awake()
        {
            _graph = GetComponent<SubstanceRuntimeGraph>();
        }

        private void Start()
        {
            // SubstanceRuntimeGraph initializes its native graph in Awake, so the
            // inputs are only safe to touch from Start onward.
            _ready = true;

            if (_graph.HasInput("enable_paint"))
                _graph.SetInputBool("enable_paint", true);

            foreach (var channel in channels)
                if (!_graph.HasInput(channel.input))
                    Debug.LogWarning($"[PlasterDamageSubstance] Substance has no input '{channel.input}' - channel ignored.", this);

            _pendingDamage = 0f;
            TryApply(force: true);
        }

        /// <summary>Push the current health fraction (1 = intact, 0 = destroyed).</summary>
        public void SetHealth01(float health01)
        {
            _pendingDamage = 1f - Mathf.Clamp01(health01);
        }

        private void Update()
        {
            TryApply(force: false);
        }

        private void TryApply(bool force)
        {
            if (!_ready || _renderInFlight)
                return;
            if (!force && Mathf.Approximately(_pendingDamage, _appliedDamage))
                return;
            if (!force && Time.unscaledTime - _lastRenderTime < minRenderInterval)
                return;

            float damage = _pendingDamage;
            foreach (var channel in channels)
            {
                if (!_graph.HasInput(channel.input))
                    continue;

                float t = Mathf.InverseLerp(channel.damageStart, channel.damageEnd, damage);
                _graph.SetInputFloat(channel.input, Mathf.Lerp(channel.intactValue, channel.ruinedValue, t));
            }

            _appliedDamage = damage;
            _lastRenderTime = Time.unscaledTime;
            Render();
        }

        private async void Render()
        {
            _renderInFlight = true;
            try
            {
                await _graph.RenderAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
            finally
            {
                _renderInFlight = false;
            }
        }
    }
}
