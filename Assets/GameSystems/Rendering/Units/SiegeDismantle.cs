// SiegeDismantle.cs
// A destroyed siege engine comes apart instead of standing frozen and then
// vanishing (2026-11-23). Catapult, Trebuchet, Battering Ram, Ballista — any
// unit carrying SiegeTag gets this at spawn (PresentationSpawnSystem).
//
// The engines are rigid-part models (Synty part hierarchies or procedural
// primitives), so the parts are simply the renderers: each is lifted out of
// the hierarchy, sags outward and drops under gravity, and the settled
// wreckage is handed to CorpseDissolver, which clears it the way every corpse
// is cleared. The same idea as LedgerDisassembly, without a skeleton.
//
// It starts the moment the engine dies (DeathAnimationState), not when the
// entity is finally removed two seconds later. Driven from Update rather than
// a coroutine, so a view the fog switches off and on again carries on.
//
// Presentation only: nothing here touches simulation state.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Rendering
{
    public sealed class SiegeDismantle : MonoBehaviour
    {
        // How it falls apart. A collapse, not an explosion: parts lean out a
        // little and drop. Presentation timing, not game data.
        private const float OutwardSpeed = 0.9f;
        private const float UpSpeed = 0.8f;
        private const float Gravity = -9f;
        private const float SpinDegreesPerSecond = 160f;
        private const float Bounce = 0.2f;
        /// <summary>Parts start falling over this long, lowest last, so the
        /// engine sags from the top instead of dropping as one.</summary>
        private const float StaggerSeconds = 0.5f;
        private const float MaxSettleSeconds = 4f;
        private const float LingerSeconds = 8f;

        private EntityReference _entityRef;
        private EntityManager _em;
        private bool _emReady;

        private bool _started;
        private bool _entityGone;
        private bool _handedOff;
        private float _elapsed;
        private float _floorY;

        private readonly List<Transform> _parts = new();
        private Vector3[] _vel;
        private Vector3[] _spinAxis;
        private float[] _spin;
        private float[] _startAt;

        void Start()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) { _em = world.EntityManager; _emReady = true; }
            _entityRef = GetComponent<EntityReference>();
        }

        void Update()
        {
            if (!_started)
            {
                // Dead but not yet removed: start coming apart now.
                if (!_emReady) return;
                if (_entityRef == null) { _entityRef = GetComponent<EntityReference>(); if (_entityRef == null) return; }
                var e = _entityRef.Entity;
                if (e == Entity.Null || !_em.Exists(e)) return;
                if (_em.HasComponent<DeathAnimationState>(e) && _em.IsComponentEnabled<DeathAnimationState>(e))
                    Begin();
                return;
            }
            if (_handedOff) return;

            if (Fall(Time.deltaTime) || !_entityGone) return;

            // Settled, and the entity is gone: the wreckage is ours to clear.
            _handedOff = true;
            var dissolver = GetComponent<CorpseDissolver>() ?? gameObject.AddComponent<CorpseDissolver>();
            dissolver.LingerSeconds = LingerSeconds;
            dissolver.BeginDeath();
        }

        /// <summary>
        /// The entity has been removed (PresentationSpawnSystem). From here
        /// this component owns the GameObject. Idempotent.
        /// </summary>
        public void BeginDeath()
        {
            _entityGone = true;
            // Out of sight (fog): nobody can watch it fall, and nothing will
            // ever switch the view back on, so clear it now.
            if (!gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }
            if (!_started) Begin();
        }

        private void Begin()
        {
            _started = true;

            // Stop whatever poses the engine (arm drivers, wheel rollers) and
            // make the wreck unselectable.
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>())
                if (mb != null && mb != this && !(mb is CorpseDissolver) && !(mb is EntityReference))
                    mb.enabled = false;
            var anim = GetComponentInChildren<Animator>();
            if (anim) anim.enabled = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;

            _floorY = transform.position.y;

            var renderers = GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 centre = bounds.center;
            float height = Mathf.Max(0.1f, bounds.max.y - _floorY);

            var scrap = new GameObject("Wreckage").transform;
            scrap.SetParent(transform, true);

            // Lift every part out of the hierarchy FIRST, then give impulses:
            // detaching as we go would drag children that are parts themselves.
            foreach (var r in renderers) _parts.Add(r.transform);
            foreach (var p in _parts) p.SetParent(scrap, true);

            int n = _parts.Count;
            _vel = new Vector3[n];
            _spinAxis = new Vector3[n];
            _spin = new float[n];
            _startAt = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 pos = renderers[i].bounds.center;
                Vector3 outward = pos - centre;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-5f
                    ? outward.normalized
                    : new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f).normalized;

                _vel[i] = outward * (OutwardSpeed * Random.Range(0.4f, 1.2f))
                        + Vector3.up * (UpSpeed * Random.Range(0.2f, 1f));
                _spinAxis[i] = Random.onUnitSphere;
                _spin[i] = SpinDegreesPerSecond * Random.Range(-1f, 1f);
                // The top goes first.
                float h = Mathf.Clamp01((pos.y - _floorY) / height);
                _startAt[i] = (1f - h) * StaggerSeconds;
            }
        }

        /// <summary>One step of the fall. True while anything still moves.</summary>
        private bool Fall(float dt)
        {
            _elapsed += dt;
            if (_elapsed > MaxSettleSeconds + StaggerSeconds) return false;

            bool moving = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                var p = _parts[i];
                if (p == null) continue;
                if (_elapsed < _startAt[i]) { moving = true; continue; }

                _vel[i] += Vector3.up * (Gravity * dt);
                Vector3 next = p.position + _vel[i] * dt;
                if (next.y <= _floorY)
                {
                    next.y = _floorY;
                    if (Mathf.Abs(_vel[i].y) > 0.35f)
                    {
                        _vel[i] = new Vector3(_vel[i].x * 0.4f, -_vel[i].y * Bounce, _vel[i].z * 0.4f);
                        _spin[i] *= 0.35f;
                    }
                    else
                    {
                        _vel[i] = Vector3.zero;
                        _spin[i] = 0f;
                    }
                }
                p.position = next;
                if (Mathf.Abs(_spin[i]) > 0.01f)
                    p.rotation = Quaternion.AngleAxis(_spin[i] * dt, _spinAxis[i]) * p.rotation;
                if (_vel[i].sqrMagnitude > 0.0004f) moving = true;
            }
            return moving;
        }
    }
}
