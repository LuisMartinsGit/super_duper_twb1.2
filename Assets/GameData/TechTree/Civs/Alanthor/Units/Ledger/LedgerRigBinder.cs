// LedgerRigBinder.cs
// Wires LedgerWalker to the Ledger skeleton, from bone names, on every
// instance — spawned in a match, or respawned by the test loop.
//
// WHY A BINDER AT ALL. The walker needs four transforms per leg, and the
// fourth — the toe — does not exist: the FBX was exported without leaf bones,
// so the end of each claw is a bone TAIL in Blender and nothing at all in
// Unity. Something has to create those five transforms, and if that something
// is an editor script then only the scene it built can walk.
//
// WHY THE TOE OFFSETS ARE BAKED. Measuring the toe means reading mesh
// vertices, and the model imports with Read/Write DISABLED — so at runtime
// Mesh.vertices comes back EMPTY and every measurement silently finds nothing.
// The editor never sees this, because the editor can read any mesh: the first
// version measured perfectly in the scene builder and then failed on every
// instance in play mode, with the binder disabling itself and the walker
// reporting an unassigned baseBone sixty times a second.
//
// Marking the mesh readable would fix it and cost a second CPU copy of the
// geometry on every Ledger alive. The offsets are constants of the rig, so
// they are measured ONCE at edit time (see LedgerWalkTestBuilder) and stored
// here. Runtime does no mesh access at all.
//
// Runs at -200 so it lands before LedgerWalker.Awake, which reads what it writes.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(LedgerWalker))]
    public class LedgerRigBinder : MonoBehaviour
    {
        public const int LegCount = 5;

        [Tooltip("Toe position for each leg, in that leg's FOOT BONE local space. " +
                 "Baked at edit time — see the class docs for why this is not " +
                 "measured at runtime. Five entries, Hip1..Hip5.")]
        public Vector3[] toeLocalOffsets = new Vector3[0];

        [Tooltip("Layers the feet are raycast against. Must include the terrain " +
                 "or every foot falls back to the root's height.")]
        public LayerMask ground = 0;

        [Tooltip("Log bone/toe measurements on bind. Off for normal play.")]
        public bool verbose = false;

        private void Reset() { ground = LayerMask.GetMask("Ground"); }

        private void Awake()
        {
            var walker = GetComponent<LedgerWalker>();
            if (walker == null) return;

            // The walker owns the bones; an Animator would fight it for them.
            var animator = GetComponentInChildren<Animator>();
            if (animator) animator.enabled = false;

            var all = GetComponentsInChildren<Transform>(true);
            Transform Find(string n)
            {
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name == n) return all[i];
                return null;
            }

            var hub = Find("Hub");
            if (hub == null)
            {
                Debug.LogError("[LedgerRig] no 'Hub' bone; this Ledger cannot walk.");
                enabled = false;
                return;
            }

            var ankles = new Transform[LegCount];
            for (int i = 0; i < LegCount; i++)
            {
                ankles[i] = Find($"Hip{i + 1}.Foot");
                if (ankles[i] == null)
                {
                    Debug.LogError($"[LedgerRig] missing bone 'Hip{i + 1}.Foot'.");
                    enabled = false;
                    return;
                }
            }

            Vector3[] toes = ResolveToes(ankles);
            if (toes == null) { enabled = false; return; }

            var restRoot = new GameObject("FootRestPoints").transform;
            restRoot.SetParent(transform, false);

            var legs = new LedgerWalker.Leg[LegCount];
            float footY = 0f;

            for (int i = 0; i < LegCount; i++)
            {
                int n = i + 1;
                var hip = Find($"Hip{n}.Thigh");
                var knee = Find($"Hip{n}.Shin");
                if (hip == null || knee == null)
                {
                    Debug.LogError($"[LedgerRig] leg {n}: missing Thigh or Shin bone.");
                    enabled = false;
                    return;
                }

                Vector3 toe = toes[i];

                var tip = new GameObject($"Hip{n}.Toe").transform;
                tip.SetParent(ankles[i], false);
                tip.position = toe;

                var rest = new GameObject($"Hip{n}.Rest").transform;
                rest.SetParent(restRoot, false);
                // On the ground the feet were authored on, in the body's own
                // frame: rest points translate and yaw with the unit but must
                // not inherit the body's tilt.
                rest.position = new Vector3(toe.x, transform.position.y, toe.z);
                footY += toe.y;

                legs[i] = new LedgerWalker.Leg
                {
                    hip = hip,
                    knee = knee,
                    ankle = ankles[i],
                    tip = tip,
                    restPoint = rest,
                    // The legs sit on a regular pentagon and Hip1..Hip5 run
                    // round it in order, so a leg's neighbours are its index
                    // either side. Adjacent legs never step together.
                    neighbours = new[] { (i + LegCount - 1) % LegCount, (i + 1) % LegCount },
                };

                if (verbose)
                    Debug.Log($"[LedgerRig] leg{n} toe=({toe.x:0.000},{toe.y:0.000},{toe.z:0.000}) " +
                              $"segments {(knee.position - hip.position).magnitude:0.000}/" +
                              $"{(ankles[i].position - knee.position).magnitude:0.000}/" +
                              $"{(toe - ankles[i].position).magnitude:0.000}");
            }

            float bodyHeight = hub.position.y - (footY / LegCount);
            LayerMask mask = ground.value != 0 ? ground : LayerMask.GetMask("Ground");
            walker.Bind(hub, legs, bodyHeight, mask);

            if (verbose)
                Debug.Log($"[LedgerRig] bound {LegCount} legs, bodyHeight={bodyHeight:0.000} m");
        }

        /// <summary>Baked offsets if they are there, a live measurement if the
        /// mesh happens to be readable, and a clear failure otherwise.</summary>
        private Vector3[] ResolveToes(Transform[] ankles)
        {
            if (toeLocalOffsets != null && toeLocalOffsets.Length == LegCount)
            {
                var baked = new Vector3[LegCount];
                for (int i = 0; i < LegCount; i++)
                    baked[i] = ankles[i].TransformPoint(toeLocalOffsets[i]);
                return baked;
            }

            var smr = GetComponentInChildren<SkinnedMeshRenderer>();
            var measured = smr != null ? MeasureToeOffsets(smr) : null;
            if (measured != null)
            {
                var live = new Vector3[LegCount];
                for (int i = 0; i < LegCount; i++)
                    live[i] = ankles[i].TransformPoint(measured[i]);
                return live;
            }

            Debug.LogError("[LedgerRig] no baked toe offsets and the mesh is not readable, " +
                           "so the toes cannot be located. Run " +
                           "'Waning Border > Units > Build Ledger Walk Test Scene' to bake them " +
                           "onto the prefab.");
            return null;
        }

        /// <summary>
        /// Toe per leg in that foot bone's LOCAL space: of the vertices weighted
        /// to the bone, the one farthest from the joint. Null when the mesh
        /// cannot be read (the normal runtime case — see the class docs).
        ///
        /// Public so the editor can bake the result onto the prefab.
        /// </summary>
        public static Vector3[] MeasureToeOffsets(SkinnedMeshRenderer smr)
        {
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null) return null;

            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            var binds = mesh.bindposes;
            // Read/Write disabled strips the CPU copy: the arrays come back
            // EMPTY rather than throwing, which is what made this fail silently.
            if (verts.Length == 0 || weights.Length == 0 || binds.Length == 0) return null;

            var bones = smr.bones;
            var result = new Vector3[LegCount];

            for (int i = 0; i < LegCount; i++)
            {
                string footName = $"Hip{i + 1}.Foot";
                int bi = -1;
                for (int b = 0; b < bones.Length; b++)
                    if (bones[b] != null && bones[b].name == footName) { bi = b; break; }
                if (bi < 0 || bi >= binds.Length) return null;

                // bindpose maps renderer-root space -> bone space, which is
                // exactly the local space we want to store the offset in.
                Matrix4x4 toBone = binds[bi];
                Vector3 best = Vector3.zero;
                float bestD = -1f;

                int count = Mathf.Min(verts.Length, weights.Length);
                for (int v = 0; v < count; v++)
                {
                    var w = weights[v];
                    float weight = 0f;
                    if (w.boneIndex0 == bi) weight = w.weight0;
                    else if (w.boneIndex1 == bi) weight = w.weight1;
                    else if (w.boneIndex2 == bi) weight = w.weight2;
                    else if (w.boneIndex3 == bi) weight = w.weight3;
                    if (weight < 0.5f) continue;

                    Vector3 local = toBone.MultiplyPoint3x4(verts[v]);
                    float d = local.sqrMagnitude;     // distance from the joint itself
                    if (d > bestD) { bestD = d; best = local; }
                }

                if (bestD < 0f) return null;
                result[i] = best;
            }
            return result;
        }
    }
}
