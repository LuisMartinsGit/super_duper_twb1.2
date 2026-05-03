// File: Assets/Scripts/Presentation/BuildingPartAnimator.cs
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Lightweight animator for procedural building decorations: spins cogwheels,
    /// sways canopies/banners, and bobs tethered objects (balloons). Attached to
    /// individual sub-transforms by the building generators.
    /// </summary>
    public class BuildingPartAnimator : MonoBehaviour
    {
        public enum Mode { Rotate, Sway, Bob }

        public Mode Animation = Mode.Rotate;

        // Rotate: degrees per second around <see cref="Axis"/>.
        public Vector3 Axis = Vector3.up;
        public float Speed = 30f;

        // Sway: peak rotation angle (degrees) around <see cref="Axis"/>, oscillating.
        public float Amplitude = 8f;
        public float Frequency = 0.4f;
        public float PhaseOffset;

        // Bob: vertical displacement (units) on top of the start local position.
        public float BobAmplitude = 0.15f;
        public float BobFrequency = 0.35f;

        private Vector3 _startLocalPos;
        private Quaternion _startLocalRot;

        void Awake()
        {
            _startLocalPos = transform.localPosition;
            _startLocalRot = transform.localRotation;
            // Stagger phase by name hash so neighbours don't sway in lockstep.
            PhaseOffset += (gameObject.name.GetHashCode() & 0xFF) / 255f * Mathf.PI * 2f;
        }

        void Update()
        {
            float t = Time.time;
            switch (Animation)
            {
                case Mode.Rotate:
                    transform.Rotate(Axis, Speed * Time.deltaTime, Space.Self);
                    break;
                case Mode.Sway:
                {
                    float angle = Mathf.Sin(t * Frequency * Mathf.PI * 2f + PhaseOffset) * Amplitude;
                    transform.localRotation = _startLocalRot * Quaternion.AngleAxis(angle, Axis);
                    break;
                }
                case Mode.Bob:
                {
                    float dy = Mathf.Sin(t * BobFrequency * Mathf.PI * 2f + PhaseOffset) * BobAmplitude;
                    transform.localPosition = _startLocalPos + new Vector3(0f, dy, 0f);
                    break;
                }
            }
        }
    }
}
