// CaravanVisualSystem.cs
// Procedural visual for Runai caravans (spec refinement #3: "desert traveler
// with a backpack and lances"). Mirrors RitualBeamSystem's pattern: each
// CaravanTag entity gets a procedural GameObject built from primitive shapes
// — body, head, backpack, two lances. No prefab needed.
//
// The existing PresentationSpawnSystem may still create the base PresentationId
// 401 visual; this system adds the traveler kit as a child of a sibling
// wrapper anchored at the caravan's world position. If the project later
// ships a proper desert-traveler prefab, replace this system with that.
//
// Location: Assets/Scripts/Presentation/

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class CaravanVisualSystem : MonoBehaviour
    {
        private readonly Dictionary<Entity, GameObject> _visuals = new();
        private Unity.Entities.World _world;
        private EntityManager _em;
        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] CaravanQueryTypes = {
            ComponentType.ReadOnly<CaravanTag>(),
            ComponentType.ReadOnly<LocalTransform>() };
        private TheWaningBorder.Core.CachedEntityQuery _caravanQuery;
        private Material _clothMat;
        private Material _woodMat;
        private Material _metalMat;

        void Awake()
        {
            _clothMat = BuildMat(new Color(0.78f, 0.62f, 0.40f, 1f));   // sand cloth
            _woodMat  = BuildMat(new Color(0.42f, 0.27f, 0.16f, 1f));   // dark wood backpack frame
            _metalMat = BuildMat(new Color(0.65f, 0.65f, 0.70f, 1f));   // lance tips
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }

            var caravanQuery = _caravanQuery.Get(_em, CaravanQueryTypes);
            using var ents = caravanQuery.ToEntityArray(Allocator.Temp);
            using var transforms = caravanQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var seen = new HashSet<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                seen.Add(ents[i]);
                if (!_visuals.TryGetValue(ents[i], out var go) || go == null)
                {
                    go = BuildDesertTraveler();
                    _visuals[ents[i]] = go;
                }
                if (go != null)
                {
                    var t = transforms[i];
                    go.transform.position = t.Position;
                    go.transform.rotation = t.Rotation;
                }
            }

            if (_visuals.Count > seen.Count)
            {
                var toRemove = new List<Entity>();
                foreach (var kv in _visuals)
                    if (!seen.Contains(kv.Key))
                    {
                        if (kv.Value != null) Destroy(kv.Value);
                        toRemove.Add(kv.Key);
                    }
                foreach (var k in toRemove) _visuals.Remove(k);
            }
        }

        void OnDestroy()
        {
            foreach (var kv in _visuals)
                if (kv.Value != null) Destroy(kv.Value);
            _visuals.Clear();
        }

        private GameObject BuildDesertTraveler()
        {
            var root = new GameObject("Caravan_Traveler");

            // Body — tall capsule (default Y-up). Scale to humanoid.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.45f, 0.85f, 0.45f);
            body.transform.localPosition = new Vector3(0, 0.85f, 0);
            StripCollider(body);
            SetMat(body, _clothMat);

            // Head — small sphere atop the body.
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
            head.transform.localPosition = new Vector3(0, 1.82f, 0);
            StripCollider(head);
            SetMat(head, _clothMat);

            // Backpack — cube behind the body.
            var pack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pack.name = "Backpack";
            pack.transform.SetParent(root.transform, false);
            pack.transform.localScale = new Vector3(0.5f, 0.55f, 0.30f);
            pack.transform.localPosition = new Vector3(0, 1.0f, -0.32f);
            StripCollider(pack);
            SetMat(pack, _woodMat);

            // Two lances — thin cylinders crossed behind/over the shoulder.
            // Default cylinder is Y-up and 2m tall; scale Y for length.
            BuildLance(root.transform, new Vector3(-0.22f, 1.6f, -0.20f), 20f);
            BuildLance(root.transform, new Vector3( 0.22f, 1.6f, -0.20f), -20f);

            return root;
        }

        private void BuildLance(Transform parent, Vector3 mountPos, float yawDegrees)
        {
            var lance = new GameObject("Lance");
            lance.transform.SetParent(parent, false);
            lance.transform.localPosition = mountPos;
            lance.transform.localRotation = Quaternion.Euler(15f, yawDegrees, 0f);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(lance.transform, false);
            shaft.transform.localScale = new Vector3(0.06f, 0.95f, 0.06f);
            // Cylinder pivots at center; lift so the bottom is at lance origin.
            shaft.transform.localPosition = new Vector3(0, 0.95f, 0);
            StripCollider(shaft);
            SetMat(shaft, _woodMat);

            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tip.name = "Tip";
            tip.transform.SetParent(lance.transform, false);
            tip.transform.localScale = new Vector3(0.08f, 0.16f, 0.08f);
            tip.transform.localPosition = new Vector3(0, 1.95f, 0);
            StripCollider(tip);
            SetMat(tip, _metalMat);
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static void SetMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        private static Material BuildMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Standard")
                       ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }
    }
}
