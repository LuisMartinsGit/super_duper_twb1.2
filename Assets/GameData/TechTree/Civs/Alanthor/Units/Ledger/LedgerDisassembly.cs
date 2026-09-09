// LedgerDisassembly.cs
// The Ledger's death: the automaton comes apart, the pieces fall, and the
// wreckage is then cleared by the same CorpseDissolver the Longbowman uses.
//
// WHY THIS IS CHEAP. The export binds every vertex 1.0 to exactly ONE bone —
// each rigid part is its own bone — so scattering the BONES is literally
// scattering the parts. No debris prefab, no second mesh, no shattering: the
// skin follows whatever the bones do, and the bones are free to stop being a
// skeleton.
//
// The bones are reparented to a flat scrap root first. They are a HIERARCHY
// while the thing is alive (a shin follows its thigh), which is exactly what
// must stop when it falls apart. Reparenting keeps the SkinnedMeshRenderer's
// references intact — skinning is bone.localToWorldMatrix * bindpose, so as
// long as each bone's WORLD transform is preserved across the reparent, the
// mesh does not flinch.
//
// This is presentation only. It runs after the ECS entity is already gone, so
// nothing here touches simulation state or needs to be deterministic.

using System.Collections;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class LedgerDisassembly : MonoBehaviour
    {
        [Header("Break-up")]
        [Tooltip("Outward shove given to each part, metres/second.")]
        public float outwardSpeed = 1.35f;

        [Tooltip("Upward kick, metres/second. The machine comes apart slightly " +
                 "upward before it drops, which reads as failure rather than a " +
                 "puppet's strings being cut.")]
        public float upSpeed = 1.5f;

        [Tooltip("The body drops rather than flying: it is the heavy part.")]
        public float hubOutwardScale = 0.25f;

        public float gravity = -9.0f;
        public float spinSpeed = 420f;
        [Range(0f, 0.8f)] public float bounce = 0.28f;

        [Tooltip("Give up settling after this long and hand over regardless.")]
        public float maxSettleSeconds = 3.0f;

        [Header("Clearing")]
        [Tooltip("Seconds the wreckage lies there before it dissolves out. " +
                 "Passed to CorpseDissolver.")]
        public float lingerSeconds = 8f;

        private bool _started;
        private Transform[] _parts;
        private Vector3[] _vel;
        private Vector3[] _spinAxis;
        private float[] _spin;
        private float _floorY;

        /// <summary>
        /// Take the corpse over. Called by PresentationSpawnSystem when the
        /// entity is destroyed; idempotent, mirroring CorpseDissolver.
        /// </summary>
        public void BeginDeath()
        {
            if (_started) return;
            _started = true;

            // Stop anything that would keep posing the rig. The walker writes
            // bone rotations every LateUpdate and would undo the fall.
            var walker = GetComponent<LedgerWalker>();
            if (walker) walker.enabled = false;
            var binder = GetComponent<LedgerRigBinder>();
            if (binder) binder.enabled = false;
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb != null && mb != this && mb is LedgerWanderDriver) mb.enabled = false;
            var anim = GetComponentInChildren<Animator>();
            if (anim) anim.enabled = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;

            // Same fog guard CorpseDissolver carries: a coroutine cannot start
            // on an inactive GameObject, and the entity is already gone so
            // nothing will ever reactivate it.
            if (!gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }

            if (!Scatter())
            {
                HandOff();
                return;
            }
            StartCoroutine(Fall());
        }

        /// <summary>Detach the bones and give each part its impulse. False if
        /// there is nothing to scatter.</summary>
        private bool Scatter()
        {
            var smr = GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.bones == null || smr.bones.Length == 0) return false;

            // Parts fly well outside the skin's authored bounds; without this
            // the renderer is culled the moment the pieces leave the box and
            // the corpse vanishes mid-air.
            smr.updateWhenOffscreen = true;

            var scrap = new GameObject("Wreckage").transform;
            scrap.SetParent(transform, true);

            _floorY = transform.position.y;
            Vector3 centre = smr.bounds.center;

            var bones = smr.bones;
            _parts = new Transform[bones.Length];
            _vel = new Vector3[bones.Length];
            _spinAxis = new Vector3[bones.Length];
            _spin = new float[bones.Length];

            // Reparent every bone to the flat scrap root FIRST, all of them,
            // before moving any. Doing it as we go would have a parent's
            // detach yank children that had not been detached yet.
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].SetParent(scrap, true);   // true = keep world transform
                _parts[i] = bones[i];
            }

            for (int i = 0; i < bones.Length; i++)
            {
                var t = _parts[i];
                if (t == null) continue;

                Vector3 outward = t.position - centre;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-5f
                    ? outward.normalized
                    : new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f).normalized;

                // The Hub carries the body: it should sag and drop, not be
                // flung with the limbs.
                float outScale = t.name == "Hub" ? hubOutwardScale : 1f;

                _vel[i] = outward * (outwardSpeed * outScale * Random.Range(0.6f, 1.25f))
                        + Vector3.up * (upSpeed * Random.Range(0.5f, 1.1f));
                _spinAxis[i] = Random.onUnitSphere;
                _spin[i] = spinSpeed * Random.Range(-1f, 1f);
            }
            return true;
        }

        private IEnumerator Fall()
        {
            float t = 0f;
            bool moving = true;

            while (moving && t < maxSettleSeconds)
            {
                float dt = Time.deltaTime;
                t += dt;
                moving = false;

                for (int i = 0; i < _parts.Length; i++)
                {
                    var p = _parts[i];
                    if (p == null) continue;

                    _vel[i] += Vector3.up * (gravity * dt);
                    Vector3 next = p.position + _vel[i] * dt;

                    if (next.y <= _floorY)
                    {
                        next.y = _floorY;
                        if (Mathf.Abs(_vel[i].y) > 0.35f)
                        {
                            // Bounce, shedding most of the energy and the spin
                            // with it, so a part settles rather than skating.
                            _vel[i] = new Vector3(_vel[i].x * 0.4f, -_vel[i].y * bounce, _vel[i].z * 0.4f);
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
                yield return null;
            }

            HandOff();
        }

        /// <summary>Give the settled wreckage to the shared corpse clearer, so
        /// the Ledger fades out the same way every other unit does instead of
        /// inventing a second dissolve.</summary>
        private void HandOff()
        {
            var dissolver = GetComponent<CorpseDissolver>() ?? gameObject.AddComponent<CorpseDissolver>();
            dissolver.LingerSeconds = lingerSeconds;
            dissolver.BeginDeath();
        }
    }
}
