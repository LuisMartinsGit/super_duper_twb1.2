// LedgerWanderDriver.cs
// Test harness for LedgerWalker: shove the root in a random direction, pick a
// new one every interval, repeat.
//
// It exists to prove the procedural gait is genuinely direction-agnostic. A
// baked cycle only ever looks right walking the way it was authored; this
// changes heading every second with no warning, which is the case that breaks
// a fixed clip and that the IK walker is supposed to absorb.
//
// The root is moved in Update and LedgerWalker reads the resulting velocity in
// LateUpdate, so the order is already correct without an execution-order
// attribute.
//
// This drives the TRANSFORM directly. It is a scene-test component and has no
// place on a spawned unit, where the movement systems own the transform.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public class LedgerWanderDriver : MonoBehaviour
    {
        [Header("Wander")]
        [Tooltip("Seconds before a new heading is chosen.")]
        public float interval = 1f;

        [Tooltip("Metres per second along the current heading.")]
        public float speed = 1.2f;

        [Tooltip("How fast the heading blends to the new one. Very high = an " +
                 "instant snap, which is the harsher test.")]
        public float headingBlend = 8f;

        [Tooltip("Stay inside this radius of the start point, so a test scene " +
                 "does not wander off its ground plane.")]
        public float wanderRadius = 6f;

        [Header("Facing")]
        [Tooltip("Off by default: a radial pentapod is supposed to walk in any " +
                 "direction WITHOUT turning, and leaving the root unrotated is " +
                 "the cleaner demonstration of that. Turn it on to also " +
                 "exercise rotation.")]
        public bool faceTravelDirection = false;
        public float turnSpeed = 180f;

        private Vector3 _origin;
        private Vector3 _heading = Vector3.forward;
        private Vector3 _wanted = Vector3.forward;
        private float _next;

        private void Start()
        {
            _origin = transform.position;
            _wanted = _heading = RandomHeading();
            _next = Time.time + interval;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (Time.time >= _next)
            {
                _wanted = RandomHeading();
                _next = Time.time + interval;
            }

            // Steer back rather than hard-clamping at the edge: a clamp would
            // stop the root dead, and a walker reading velocity would see a
            // discontinuity that no real movement system would produce.
            Vector3 offset = transform.position - _origin;
            offset.y = 0f;
            if (offset.magnitude > wanderRadius)
                _wanted = (-offset).normalized;

            _heading = Vector3.Slerp(_heading, _wanted, 1f - Mathf.Exp(-headingBlend * dt));
            if (_heading.sqrMagnitude > 1e-6f) _heading.Normalize();

            transform.position += _heading * (speed * dt);

            if (faceTravelDirection && _heading.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(_heading, Vector3.up),
                    turnSpeed * dt);
            }
        }

        private static Vector3 RandomHeading()
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = Application.isPlaying ? _origin : transform.position;
            Gizmos.DrawWireSphere(c, wanderRadius);
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.6f, _heading);
        }
    }
}
