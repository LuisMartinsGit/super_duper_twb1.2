// MountedRider.cs
// A unit that is a horse and the rider on it, each with an Animator of its own.
// UnitAnimationSync drives the horse's; this hands the same booleans to the
// rider (whose upper body strikes while the horse stands), and tips the horse
// over when it dies: the pack has no death clip. The pack's gaits are root-motion
// clips that carry the horse forward on its Root_Motion bone; the unit moves on
// its own, so the bone is held on its spot (a canter crept ahead and snapped
// back every loop). Presentation only.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class MountedRider : MonoBehaviour
    {
        public Animator horse;
        public Animator rider;

        static readonly int Moving = Animator.StringToHash("IsMoving");
        static readonly int Attacking = Animator.StringToHash("IsAttacking");
        static readonly int Death = Animator.StringToHash("Death");
        const float FallSeconds = 0.7f, FallDegrees = 84f, FallRise = 0.28f;

        bool _moving, _attacking, _primed;
        Transform _root;
        Vector3 _rootRest;
        float _fall;

        void Awake()
        {
            // Read before the first animated frame: the prefab's own spot.
            if (horse == null) return;
            foreach (var t in horse.GetComponentsInChildren<Transform>(true))
                if (t.name == "Root_Motion") { _root = t; _rootRest = t.localPosition; break; }
        }

        void OnEnable() => _primed = false;

        void LateUpdate()
        {
            // Not isActiveAndEnabled: the distance LOD disables the Animator and steps it.
            if (horse == null || rider == null || !horse.gameObject.activeInHierarchy) return;
            if (_root != null)
            {
                var p = _root.localPosition;
                _root.localPosition = new Vector3(_rootRest.x, p.y, _rootRest.z);
            }
            bool m = horse.GetBool(Moving), a = horse.GetBool(Attacking);
            if (!_primed || m != _moving) rider.SetBool(Moving, m);
            if (!_primed || a != _attacking) rider.SetBool(Attacking, a);
            _moving = m;
            _attacking = a;
            _primed = true;

            if (_fall <= 0f && horse.GetCurrentAnimatorStateInfo(0).shortNameHash != Death) return;
            if (_fall >= 1f) return;
            _fall = Mathf.Min(1f, _fall + Time.deltaTime / FallSeconds);
            float k = _fall * _fall;
            horse.transform.localRotation = Quaternion.Euler(0f, 0f, FallDegrees * k);
            horse.transform.localPosition = Vector3.up * (FallRise * k);
        }
    }
}
