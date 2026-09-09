// LedgerWalker.cs
// Procedural IK gait for the Ledger pentapod: feet are planted in WORLD space
// and only re-placed when the body has walked far enough away from them, so the
// machine adapts to any speed, any direction and any ground instead of playing
// a fixed cycle.
//
// This REPLACES the baked Walk clip — Awake disables the Animator outright,
// because an Animator writing bone rotations after this script would undo every
// solve. Ledger.controller stays in the project as a fallback for anything that
// wants a canned cycle, but nothing drives it while this component is present.
//
// Namespace matches the other entity-visual files in this folder
// (LedgerVisual, LedgerAutomationVfx) per CLAUDE.md's co-location rule: the
// file lives with its entity, and declares TheWaningBorder.Rendering from
// inside GameData/TechTree.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
public class LedgerWalker : MonoBehaviour
{
    [System.Serializable]
    public class Leg
    {
        public Transform hip, knee, ankle, tip;   // Hip{i}.Thigh, .Shin, .Foot bones + tip transform (or Hip{i}.IK bone)
        public Transform restPoint;               // empty under the base, where the foot rests
        public int[] neighbours;                  // indices of adjacent legs

        [HideInInspector] public Vector3 footPos, footNormal = Vector3.up;
        [HideInInspector] public Vector3 stepFrom, stepTo;
        [HideInInspector] public float stepT = 1f;
        [HideInInspector] public float[] len;
        [HideInInspector] public float reach;
        [HideInInspector] public Vector3[] p = new Vector3[4];
        public bool Stepping => stepT < 1f;
    }

    [Header("Rig")]
    [SerializeField] Transform baseBone;       // Hub: translates/tilts, never yaws
    [SerializeField] Transform torsoPivot;     // yaws to face movement
    [SerializeField] Leg[] legs;

    [Header("Gait")]
    [Range(0.15f, 0.5f)] [SerializeField] float stepTriggerFraction = 0.3f;  // of leg reach
    [SerializeField] float minStepDuration = 0.12f, maxStepDuration = 0.35f;
    [SerializeField] float stepHeight = 0.25f;
    [SerializeField] float anticipation = 0.25f;    // seconds of velocity to lead the landing
    [SerializeField] int maxSimultaneous = 2;
    [SerializeField] float idleStepThreshold = 0.05f; // recentre feet slowly when standing

    [Header("Idle")]
    // The Animator is disabled by this component, so an authored idle CLIP
    // could never play — anything it wrote would be overwritten by the solve
    // below on the same frame. The idle is therefore procedural, and blends in
    // as the machine slows rather than switching state: a clockwork thing
    // settling on its legs, breathing slowly, never quite still.
    [SerializeField] float idleBob = 0.012f;        // vertical, metres
    [SerializeField] float idleBobHz = 0.55f;       // slow: this is "ticking over", not "walking"
    [SerializeField] float idleSway = 0.008f;       // lateral drift, metres
    [SerializeField] float idleSwayHz = 0.23f;      // deliberately not a multiple of the bob
    [SerializeField] float idleBlendSpeed = 0.35f;  // m/s at which the idle has fully faded out

    [Header("Body")]
    [SerializeField] float bodyHeight = 0.5f;
    [SerializeField] float bodyFollow = 10f;
    [SerializeField] float bodyBob = 0.02f;
    [SerializeField] float turnSpeed = 540f;
    [SerializeField] LayerMask ground;

    Vector3 lastPos, velocity;

    /// <summary>
    /// The Hub's LOCAL rotation in the bind pose.
    ///
    /// The body used to be assigned an absolute world rotation of
    /// FromToRotation(up, groundNormal) * yaw, which on flat ground is just
    /// identity — fine for a rig whose body bone sits at identity, wrong for
    /// this one. The Ledger's Hub bone points DOWNWARD (head z=0.098, tail
    /// z=0 in Blender), so forcing it to identity turned the machine upside
    /// down. The ground tilt is applied RELATIVE to this instead.
    /// </summary>
    Quaternion baseRestLocalRot = Quaternion.identity;

    /// <summary>
    /// Point the walker at a skeleton from code. LedgerRigBinder calls this in
    /// its own Awake (execution order -200), before the Awake below runs, so a
    /// prefab needs no authored leg references and every spawned or respawned
    /// instance wires itself.
    /// </summary>
    public void Bind(Transform body, Leg[] boundLegs, float measuredBodyHeight, LayerMask groundMask)
    {
        baseBone = body;
        legs = boundLegs;
        bodyHeight = measuredBodyHeight;
        // A walker added through AddComponent starts with a mask of 0
        // (Nothing), so every foot raycast misses and the feet silently fall
        // back to the root's height — flat ground hides it completely.
        ground = groundMask;
    }

    void Awake()
    {
        lastPos = transform.position;
        if (legs == null) legs = new Leg[0];
        foreach (var L in legs)
        {
            // FIELD INITIALISERS ON A [Serializable] CLASS DO NOT SURVIVE.
            // Unity rebuilds each Leg from serialized data rather than running
            // the constructor, so `p = new Vector3[4]` came back LENGTH ZERO
            // (IndexOutOfRange on the first solve) and `stepT = 1f` came back
            // 0, which reads as "every leg is mid-step" and deadlocks the gait
            // behind maxSimultaneous. Both have to be established here.
            if (L.p == null || L.p.Length < 4) L.p = new Vector3[4];
            L.stepT = 1f;
            L.footNormal = Vector3.up;

            L.len = new[] { (L.knee.position - L.hip.position).magnitude,
                            (L.ankle.position - L.knee.position).magnitude,
                            (L.tip.position - L.ankle.position).magnitude };
            L.reach = L.len[0] + L.len[1] + L.len[2];
            L.footPos = Ground(L.restPoint.position, out L.footNormal);
        }
        if (baseBone) baseRestLocalRot = baseBone.localRotation;

        var anim = GetComponentInChildren<Animator>();
        if (anim) anim.enabled = false;           // never let an Animator overwrite the bones
    }

    void LateUpdate()
    {
        // An unbound walker used to throw from two places every frame —
        // legs[0] on an empty array and baseBone.position on a null — which
        // buried the ONE line that said why (the binder's own error). Standing
        // still is the honest failure here.
        if (baseBone == null || legs == null || legs.Length == 0) return;

        float dt = Mathf.Max(Time.deltaTime, 1e-5f);

        // 1. velocity of whatever moves the root (agent, flow field, script)
        Vector3 v = (transform.position - lastPos) / dt;
        velocity = Vector3.Lerp(velocity, v, 1f - Mathf.Exp(-12f * dt));
        lastPos = transform.position;
        float speed = new Vector2(velocity.x, velocity.z).magnitude;

        // 2. decide steps: most-displaced first, adjacency + concurrency gating
        int stepping = 0; foreach (var L in legs) if (L.Stepping) stepping++;
        var order = new int[legs.Length]; var score = new float[legs.Length];
        for (int i = 0; i < legs.Length; i++)
        {
            var L = legs[i]; order[i] = i;
            Vector3 lead = L.restPoint.position + velocity * anticipation;
            score[i] = Vector3.ProjectOnPlane(lead - L.footPos, Vector3.up).magnitude / L.reach;
        }
        System.Array.Sort(score, order); System.Array.Reverse(order); System.Array.Reverse(score);

        for (int k = 0; k < order.Length; k++)
        {
            int i = order[k]; var L = legs[i];
            if (L.Stepping || stepping >= maxSimultaneous) continue;
            float trigger = speed > 0.05f ? stepTriggerFraction : idleStepThreshold;
            if (score[k] < trigger) continue;
            bool clear = true;
            // Renamed from 'n': the body-normal 'Vector3 n' further down this
            // same method makes the short name illegal here (CS0136).
            foreach (int nb in L.neighbours) if (legs[nb].Stepping) { clear = false; break; }
            if (!clear) continue;

            Vector3 target = L.restPoint.position + velocity * anticipation;
            // overshoot a little in the direction of travel so the foot spends time "behind" the body
            target += Vector3.ProjectOnPlane(velocity, Vector3.up).normalized * (0.15f * L.reach * Mathf.Clamp01(speed));
            L.stepTo = Ground(target, out _);
            L.stepFrom = L.footPos;
            L.stepT = 0f;
            stepping++;
        }

        // 3. animate steps; step time shrinks with speed so legs never fall behind the body
        float travelTime = speed > 0.05f ? (stepTriggerFraction * legs[0].reach) / speed : maxStepDuration;
        float duration = Mathf.Clamp(travelTime * 0.6f, minStepDuration, maxStepDuration);
        foreach (var L in legs)
        {
            if (!L.Stepping) continue;
            L.stepT = Mathf.Min(1f, L.stepT + dt / duration);
            float e = EaseInOut(L.stepT);
            Vector3 flat = Vector3.Lerp(L.stepFrom, L.stepTo, e);
            float arc = Mathf.Sin(e * Mathf.PI);
            L.footPos = flat + Vector3.up * (arc * stepHeight);
            if (L.stepT >= 1f) { L.footPos = Ground(L.stepTo, out L.footNormal); }
        }

        // 4. body: height and tilt from the planted feet, torso yaw from velocity
        Vector3 avg = Vector3.zero, n = Vector3.zero;
        for (int i = 0; i < legs.Length; i++) avg += legs[i].footPos;
        avg /= legs.Length;
        for (int i = 0; i < legs.Length; i++)
        {
            Vector3 a = legs[i].footPos - avg, b = legs[(i + 1) % legs.Length].footPos - avg;
            n += Vector3.Cross(a, b);
        }
        n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
        if (Vector3.Dot(n, Vector3.up) < 0) n = -n;
        // Walk bob and idle breathing are the same channel, cross-faded on
        // speed: at a stand the slow idle owns it, under way the faster
        // walk bob does, and in between they blend instead of popping.
        float moving = Mathf.Clamp01(speed / Mathf.Max(0.001f, idleBlendSpeed));
        float bob = bodyBob * Mathf.Sin(Time.time * 6f) * moving
                  + idleBob * Mathf.Sin(Time.time * idleBobHz * Mathf.PI * 2f) * (1f - moving);

        // A little lateral drift while idle, on a different period from the
        // bob so the two never line up into an obvious cycle.
        float sway = idleSway * Mathf.Sin(Time.time * idleSwayHz * Mathf.PI * 2f) * (1f - moving);

        Vector3 wantPos = new Vector3(transform.position.x + sway,
                                      avg.y + bodyHeight + bob,
                                      transform.position.z);
        float f = 1f - Mathf.Exp(-bodyFollow * dt);
        baseBone.position = Vector3.Lerp(baseBone.position, wantPos, f);
        // Rest orientation resolved through the PARENT each frame, so the body
        // still follows the root if anything yaws it, then the ground tilt on
        // top. Reading baseBone's own euler yaw (as this line used to) fed the
        // bone's current rotation back into its own target, which pinned the
        // body's facing to whatever it happened to be on frame one.
        Quaternion parentRot = baseBone.parent ? baseBone.parent.rotation : Quaternion.identity;
        Quaternion restWorld = parentRot * baseRestLocalRot;
        baseBone.rotation = Quaternion.Slerp(baseBone.rotation,
            Quaternion.FromToRotation(Vector3.up, n) * restWorld, f);

        Vector3 flatV = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (flatV.sqrMagnitude > 0.01f && torsoPivot)
            torsoPivot.rotation = Quaternion.RotateTowards(torsoPivot.rotation, Quaternion.LookRotation(flatV, Vector3.up), turnSpeed * dt);

        // 5. IK last, after the body moved, so feet stay exactly where they were planted
        foreach (var L in legs) Solve(L);
    }

    // ---------- helpers ----------
    Vector3 Ground(Vector3 p, out Vector3 normal)
    {
        if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ground, QueryTriggerInteraction.Ignore))
        { normal = hit.normal; return hit.point; }
        normal = Vector3.up; return new Vector3(p.x, transform.position.y, p.z);
    }

    static float EaseInOut(float t) => t * t * (3f - 2f * t);

    void Solve(Leg L)
    {
        Vector3 root = L.hip.position;
        Vector3 target = L.footPos;
        // keep the target inside 98% of reach: at the limit the chain goes straight and looks robotic
        Vector3 toT = target - root;
        if (toT.magnitude > L.reach * 0.98f) target = root + toT.normalized * L.reach * 0.98f;

        // constrain the solve to the leg's vertical plane (hinge joints)
        Vector3 radial = Vector3.ProjectOnPlane(target - root, Vector3.up).normalized;
        if (radial.sqrMagnitude < 1e-4f) radial = Vector3.ProjectOnPlane(L.knee.position - root, Vector3.up).normalized;
        Vector3 planeN = Vector3.Cross(radial, Vector3.up);

        L.p[0] = root; L.p[1] = L.knee.position; L.p[2] = L.ankle.position; L.p[3] = target;
        // seed the knee outward + a bit up so the bend direction is stable
        L.p[1] = root + radial * (L.len[0] * 0.8f) - Vector3.up * (L.len[0] * 0.4f);
        L.p[2] = target + Vector3.up * (L.len[2] * 0.9f) - radial * (L.len[2] * 0.3f);

        for (int it = 0; it < 8; it++)
        {
            L.p[3] = target;
            for (int i = 2; i >= 0; i--) L.p[i] = L.p[i + 1] + (L.p[i] - L.p[i + 1]).normalized * L.len[i];
            L.p[0] = root;
            for (int i = 1; i <= 3; i++) L.p[i] = L.p[i - 1] + (L.p[i] - L.p[i - 1]).normalized * L.len[i - 1];
            for (int i = 1; i <= 2; i++) L.p[i] -= planeN * Vector3.Dot(L.p[i] - root, planeN);   // project onto plane
        }

        Aim(L.hip, L.p[1], planeN);
        Aim(L.knee, L.p[2], planeN);
        Aim(L.ankle, L.p[3], planeN);
    }

    // Blender bones point along local +Y; the hinge is local +Z (set in the rig). Keep Z on the plane normal.
    static void Aim(Transform bone, Vector3 to, Vector3 hinge)
    {
        Vector3 dir = to - bone.position;
        if (dir.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.LookRotation(hinge, dir);   // forward = Z = hinge, up = Y = bone axis
    }

    void OnDrawGizmosSelected()
    {
        if (legs == null) return;
        foreach (var L in legs)
        {
            if (L.restPoint) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(L.restPoint.position, 0.03f); }
            Gizmos.color = L.Stepping ? Color.red : Color.green; Gizmos.DrawSphere(L.footPos, 0.03f);
        }
    }
}
}
