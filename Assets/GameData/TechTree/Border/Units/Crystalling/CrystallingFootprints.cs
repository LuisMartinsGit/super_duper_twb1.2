// CrystallingFootprints.cs
// The crystals a Crystalling leaves where its paws land: each juts out of the
// ground in a few hundredths of a second, stands, and dissolves, gone one
// second after the paw came down. Pooled; presentation only.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class CrystallingFootprints : MonoBehaviour
    {
        const float Life = 1f;
        const float Rise = 0.07f;
        const float DissolveFrom = 0.45f;
        const float Height = 0.5f;       // metres at unit scale 1
        const int MaxLive = 700;

        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        struct Print
        {
            public Transform T;
            public MeshRenderer R;
            public float Age;
            public float Size;
        }

        static CrystallingFootprints _runner;
        readonly List<Print> _live = new List<Print>(256);
        readonly Stack<Transform> _free = new Stack<Transform>(256);
        MaterialPropertyBlock _block;
        float _meshHeight = 1f;

        public static void Spawn(Mesh mesh, Material material, Vector3 at, float yaw, float unitScale)
        {
            if (mesh == null || material == null) return;
            if (_runner == null)
            {
                var go = new GameObject("CrystallingFootprints");
                DontDestroyOnLoad(go);
                _runner = go.AddComponent<CrystallingFootprints>();
                _runner._block = new MaterialPropertyBlock();
                _runner._meshHeight = Mathf.Max(0.01f, mesh.bounds.size.y);
            }
            _runner.Add(mesh, material, at, yaw, unitScale);
        }

        void Add(Mesh mesh, Material material, Vector3 at, float yaw, float unitScale)
        {
            if (_live.Count >= MaxLive) return;
            Transform t;
            MeshRenderer r;
            if (_free.Count > 0)
            {
                t = _free.Pop();
                r = t.GetComponent<MeshRenderer>();
                t.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("Print");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                t = go.transform;
            }
            // Deterministic-looking variety without a random stream: off the position.
            float jitter = Mathf.Repeat(at.x * 12.9898f + at.z * 78.233f, 1f);
            float size = Height * unitScale * (0.8f + 0.45f * jitter) / _meshHeight;
            t.SetPositionAndRotation(at, Quaternion.Euler((jitter - 0.5f) * 26f, yaw + jitter * 360f, (0.5f - jitter) * 22f));
            t.localScale = new Vector3(size, 0f, size);
            _block.SetFloat(DissolveId, 0f);
            r.SetPropertyBlock(_block);
            _live.Add(new Print { T = t, R = r, Age = 0f, Size = size });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                p.Age += dt;
                if (p.Age >= Life)
                {
                    p.T.gameObject.SetActive(false);
                    _free.Push(p.T);
                    _live[i] = _live[_live.Count - 1];
                    _live.RemoveAt(_live.Count - 1);
                    continue;
                }
                // Jut out fast, with a little overshoot.
                float rise = Mathf.Clamp01(p.Age / Rise);
                float y = rise < 1f ? rise * 1.15f : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((p.Age - Rise) / 0.08f));
                p.T.localScale = new Vector3(p.Size, p.Size * y, p.Size);
                if (p.Age > DissolveFrom)
                {
                    _block.SetFloat(DissolveId, (p.Age - DissolveFrom) / (Life - DissolveFrom));
                    p.R.SetPropertyBlock(_block);
                }
                _live[i] = p;
            }
        }

        void OnDestroy() { if (_runner == this) _runner = null; }
    }
}
