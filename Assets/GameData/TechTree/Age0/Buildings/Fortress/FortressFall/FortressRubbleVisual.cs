// FortressRubbleVisual.cs
// Draws the rubble of a fallen keystone — Fortress, Temple or landmark
// (decision 69): a heap of the models on FortressRubbleVisual.asset, scattered
// over the footprint, where each KeystoneRubble entity stands. Removed when
// the entity goes (the building is rebuilt, or its owner is out). Presentation only; mounted on RuntimeManagers
// by GameBootstrap. The scatter is derived from the position, so a heap looks
// the same on every machine and every visit.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public class FortressRubbleVisual : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;   // presentation poll, not tuning
        private float _timer;
        private EntityQuery _query;
        private bool _ready;
        private readonly Dictionary<Entity, GameObject> _heaps = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();

        void Update()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!_ready)
            {
                _query = em.CreateEntityQuery(ComponentType.ReadOnly<KeystoneRubble>(),
                                              ComponentType.ReadOnly<LocalTransform>());
                _ready = true;
            }
            _timer += Time.deltaTime;
            if (_timer < ScanInterval) return;
            _timer = 0f;

            _seen.Clear();
            using (var ents = _query.ToEntityArray(Allocator.Temp))
            using (var xfs = _query.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    _seen.Add(ents[i]);
                    if (_heaps.TryGetValue(ents[i], out var h) && h != null) continue;
                    _heaps[ents[i]] = Build(xfs[i].Position, xfs[i].Rotation);
                }
            }
            _gone.Clear();
            foreach (var kv in _heaps) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var e in _gone)
            {
                if (_heaps[e] != null) Destroy(_heaps[e]);
                _heaps.Remove(e);
            }
        }

        private GameObject Build(Vector3 at, Quaternion rot)
        {
            var root = new GameObject("FortressRubble");
            root.transform.SetPositionAndRotation(at, rot);
            var cfg = FortressRubbleVisualConfig.I;
            if (cfg == null || cfg.pieces == null || cfg.pieces.Length == 0) return root;
            int seed = Mathf.RoundToInt(at.x * 7f) * 73856093 ^ Mathf.RoundToInt(at.z * 7f) * 19349663;
            var rng = new System.Random(seed);
            for (int i = 0; i < cfg.pieceCount; i++)
            {
                var src = cfg.pieces[rng.Next(cfg.pieces.Length)];
                if (src == null) continue;
                var p = Instantiate(src, root.transform);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * cfg.spread;
                p.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                p.transform.localScale = Vector3.one * cfg.pieceScale;
                foreach (var c in p.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            return root;
        }

        void OnDestroy()
        {
            foreach (var kv in _heaps) if (kv.Value != null) Destroy(kv.Value);
            _heaps.Clear();
        }
    }
}
