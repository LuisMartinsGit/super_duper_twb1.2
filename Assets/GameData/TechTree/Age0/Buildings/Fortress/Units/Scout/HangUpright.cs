// HangUpright.cs
// A thing carried by its handle (the Scout's lantern): it hangs straight down
// from the hand whatever the arm does, with a little swing from the motion.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class HangUpright : MonoBehaviour
    {
        const float SwingPerSpeed = 5f, MaxSwing = 24f, Settle = 5f;

        Vector3 _last;
        Vector3 _swing;
        bool _has;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0001f || transform.parent == null) return;
            Vector3 p = transform.position;
            Vector3 speed = _has ? (p - _last) / dt : Vector3.zero;
            _last = p;
            _has = true;
            _swing = Vector3.Lerp(_swing, Vector3.ClampMagnitude(speed * SwingPerSpeed, MaxSwing), 1f - Mathf.Exp(-Settle * dt));
            float yaw = transform.root.eulerAngles.y;
            transform.rotation = Quaternion.Euler(_swing.z, 0f, -_swing.x) * Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
