// File: Assets/GameData/TechTree/Units/Alanthor/Ledger/LedgerVisual.cs
// Animates the Ledger automaton visual (built by GameDataMaintenanceTool.
// WireLedgerVisuals): hover bob for the legless body, spinning cogwheels,
// the central crystal tinted and pulsing in the OWNING PLAYER's color, and
// the forcefield disc underneath — spinning, breathing, and humming (the
// hum is synthesized at runtime; the project ships no audio assets).

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Input; // EntityReference

namespace TheWaningBorder.Presentation
{
    public class LedgerVisual : MonoBehaviour
    {
        [Tooltip("Hover bob amplitude in meters.")]
        public float BobAmplitude = 0.12f;

        [Tooltip("Hover bob frequency in Hz.")]
        public float BobFrequency = 0.7f;

        [Tooltip("Base cogwheel spin speed in degrees per second (each cog varies around this, alternating direction).")]
        public float CogSpinSpeed = 60f;

        [Tooltip("Forcefield yaw spin in degrees per second.")]
        public float ForcefieldSpin = 25f;

        [Tooltip("Forcefield hum volume.")]
        public float HumVolume = 0.35f;

        private Transform _body;
        private float _bodyBaseY;
        private Transform[] _cogs = System.Array.Empty<Transform>();
        private Transform _forcefield;
        private Material _crystalMat;
        private Material _fieldMat;
        private Color _factionColor = Color.cyan;
        private bool _tinted;
        private EntityManager _em;
        private bool _emReady;
        private EntityReference _entityRef;

        private static AudioClip _humClip; // shared by all Ledgers

        void Start()
        {
            _body = FindDeep(transform, "Body");
            if (_body != null) _bodyBaseY = _body.localPosition.y;

            var cogList = new System.Collections.Generic.List<Transform>();
            CollectByPrefix(transform, "Cog", cogList);
            _cogs = cogList.ToArray();

            _forcefield = FindDeep(transform, "Forcefield");

            var crystal = FindDeep(transform, "Crystal");
            if (crystal != null && crystal.TryGetComponent<MeshRenderer>(out var cr))
                _crystalMat = cr.material; // instance — tinted per faction
            if (_forcefield != null && _forcefield.TryGetComponent<MeshRenderer>(out var fr))
                _fieldMat = fr.material;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
            _entityRef = GetComponent<EntityReference>();

            // Forcefield hum — a seamless synthesized loop (55 + 110 Hz with a
            // slow beat), 3D-positioned at the disc.
            if (_forcefield != null)
            {
                if (_humClip == null) _humClip = BuildHumClip();
                var src = _forcefield.gameObject.AddComponent<AudioSource>();
                src.clip = _humClip;
                src.loop = true;
                src.volume = HumVolume;
                src.spatialBlend = 1f;
                src.minDistance = 3f;
                src.maxDistance = 22f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.playOnAwake = false;
                src.Play();
            }
        }

        void LateUpdate()
        {
            float t = Time.time;

            // Player color arrives once the ECS entity link is live.
            if (!_tinted) TryTint();

            // Hover bob — the whole body floats; there are no legs to plant.
            if (_body != null)
            {
                var lp = _body.localPosition;
                lp.y = _bodyBaseY + Mathf.Sin(t * BobFrequency * 2f * Mathf.PI) * BobAmplitude;
                _body.localPosition = lp;
            }

            // Cogwheels — each spins around its own axis, alternating
            // direction and varying speed so the machinery reads as alive.
            for (int i = 0; i < _cogs.Length; i++)
            {
                if (_cogs[i] == null) continue;
                float dir = (i % 2 == 0) ? 1f : -1f;
                float speed = CogSpinSpeed * (0.7f + 0.2f * i);
                _cogs[i].Rotate(0f, 0f, dir * speed * Time.deltaTime, Space.Self);
            }

            // Crystal pulse — emission breathes around the faction color.
            if (_crystalMat != null && _tinted)
            {
                float pulse = 1.6f + Mathf.Sin(t * 2.2f) * 0.7f;
                _crystalMat.SetColor("_EmissionColor", _factionColor * pulse);
            }

            // Forcefield — slow spin plus an alpha/scale breath.
            if (_forcefield != null)
            {
                _forcefield.Rotate(0f, ForcefieldSpin * Time.deltaTime, 0f, Space.Self);
                float breath = 1f + Mathf.Sin(t * 1.4f) * 0.04f;
                _forcefield.localScale = new Vector3(breath, 1f, breath);
                if (_fieldMat != null && _tinted)
                {
                    var c = _factionColor;
                    c.a = 0.30f + Mathf.Sin(t * 1.4f) * 0.08f;
                    _fieldMat.SetColor("_BaseColor", c);
                }
            }
        }

        private void TryTint()
        {
            if (_entityRef == null || !_emReady) return;
            var e = _entityRef.Entity;
            if (e == Entity.Null || !_em.Exists(e) || !_em.HasComponent<FactionTag>(e)) return;

            _factionColor = FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value);
            if (_crystalMat != null)
            {
                _crystalMat.SetColor("_BaseColor", Color.Lerp(_factionColor, Color.white, 0.35f));
                _crystalMat.EnableKeyword("_EMISSION");
                _crystalMat.SetColor("_EmissionColor", _factionColor * 1.6f);
            }
            _tinted = true;
        }

        /// <summary>Seamless 1-second hum: 55 + 110 Hz partials (integer cycle
        /// counts, so the loop point is click-free) with a soft 2.5 Hz beat.</summary>
        private static AudioClip BuildHumClip()
        {
            const int rate = 44100;
            var samples = new float[rate];
            for (int i = 0; i < rate; i++)
            {
                float time = i / (float)rate;
                float beat = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 2.5f * time);
                samples[i] = beat * (0.55f * Mathf.Sin(2f * Mathf.PI * 55f * time)
                                   + 0.30f * Mathf.Sin(2f * Mathf.PI * 110f * time));
            }
            var clip = AudioClip.Create("LedgerHum", rate, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void CollectByPrefix(Transform root, string prefix,
            System.Collections.Generic.List<Transform> into)
        {
            if (root.name.StartsWith(prefix)) into.Add(root);
            for (int i = 0; i < root.childCount; i++)
                CollectByPrefix(root.GetChild(i), prefix, into);
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            if (root.name == childName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
