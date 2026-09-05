// RitualBeamSystem.cs
// Spawns + tracks vertical light-beam GameObjects at every active ritual
// site (spec §5.1: rituals must broadcast visibly to all players).
//
// Procedural beams — no prefab required. A tall, semi-transparent emissive
// cylinder is built at the node position when ActiveRitualOnNode appears
// and destroyed when it disappears. Color varies by RitualKind so players
// at a glance know what the channeling player is doing:
//   - Purification (Alanthor): cyan-white
//   - Violent Extraction (Feraldis): orange-red  (not yet reachable —
//                                                 §5.3 is HP-driven, not channeled)
//   - Conversion (Runai): emerald-green
//
// Audio cue and minimap marker (also called out in §5.1) are follow-ups —
// they tie into existing systems (audio mixer + MinimapRenderer) that
// need their own integration.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// MonoBehaviour singleton that mirrors ECS ActiveRitualOnNode entities
    /// into managed GameObject beam visuals. Created by GameBootstrap.
    /// </summary>
    public class RitualBeamSystem : MonoBehaviour
    {
        private const float BeamHeight = 28f;
        private const float BeamRadius = 0.35f;
        private const float BeamYOffset = 0f;

        // Pickup-attunement beams are shorter + thinner so they read as a
        // "claim in progress" indicator rather than a ritual broadcast.
        private const float PickupBeamHeight = 12f;
        private const float PickupBeamRadius = 0.22f;

        // Live beams keyed by source entity (node or pickup). NetworkedEntity's
        // NetworkId would be more robust but the source ECS entity is the natural
        // anchor in singleplayer.
        private readonly Dictionary<Entity, GameObject> _beams = new();
        private Unity.Entities.World _world;
        private EntityManager _em;
        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] RitualQueryTypes = {
            ComponentType.ReadOnly<ActiveRitualOnNode>(),
            ComponentType.ReadOnly<LocalTransform>() };
        private static readonly ComponentType[] PickupAttuningQueryTypes = {
            ComponentType.ReadOnly<GlowPickupTag>(),
            ComponentType.ReadOnly<GlowPickupState>(),
            ComponentType.ReadOnly<LocalTransform>() };
        private TheWaningBorder.Core.CachedEntityQuery _ritualQuery;
        private TheWaningBorder.Core.CachedEntityQuery _pickupAttuningQuery;
        private Material _purifyMat;
        private Material _conversionMat;
        private Material _extractionMat;
        private Material _pickupAttuneMat;

        void Awake()
        {
            _purifyMat = BuildBeamMaterial(new Color(0.65f, 0.95f, 1.0f, 0.6f));
            _conversionMat = BuildBeamMaterial(new Color(0.45f, 1.00f, 0.55f, 0.6f));
            _extractionMat = BuildBeamMaterial(new Color(1.00f, 0.45f, 0.20f, 0.6f));
            _pickupAttuneMat = BuildBeamMaterial(new Color(1.00f, 0.85f, 0.30f, 0.7f));
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }

            var ritualQuery = _ritualQuery.Get(_em, RitualQueryTypes);
            var pickupAttuningQuery = _pickupAttuningQuery.Get(_em, PickupAttuningQueryTypes);

            // Reconcile: build a set of live ECS source entities, spawn/update
            // their beams, then prune beams whose source is gone.
            var seen = new HashSet<Entity>();

            // Snapshot ritual sources (nodes).
            using var ritualEnts = ritualQuery.ToEntityArray(Allocator.Temp);
            using var ritualActives = ritualQuery.ToComponentDataArray<ActiveRitualOnNode>(Allocator.Temp);
            using var ritualTransforms = ritualQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < ritualEnts.Length; i++)
            {
                seen.Add(ritualEnts[i]);
                var pos = ritualTransforms[i].Position;
                pos.y += BeamYOffset;
                if (!_beams.TryGetValue(ritualEnts[i], out var go) || go == null)
                {
                    go = BuildBeamGameObject(MaterialFor(ritualActives[i].Kind), BeamHeight, BeamRadius);
                    _beams[ritualEnts[i]] = go;
                }
                if (go != null) go.transform.position = pos;
            }

            // Snapshot pickup attunements. Show beam only while an Attuner is
            // assigned (someone is actively claiming) — idle pickups stay
            // unmarked so the beam reads as "claim in progress" specifically.
            using var pickupEnts = pickupAttuningQuery.ToEntityArray(Allocator.Temp);
            using var pickupStates = pickupAttuningQuery.ToComponentDataArray<GlowPickupState>(Allocator.Temp);
            using var pickupTransforms = pickupAttuningQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < pickupEnts.Length; i++)
            {
                if (pickupStates[i].Attuner == Entity.Null) continue;
                seen.Add(pickupEnts[i]);
                var pos = pickupTransforms[i].Position;
                pos.y += BeamYOffset;
                if (!_beams.TryGetValue(pickupEnts[i], out var go) || go == null)
                {
                    go = BuildBeamGameObject(_pickupAttuneMat, PickupBeamHeight, PickupBeamRadius);
                    _beams[pickupEnts[i]] = go;
                }
                if (go != null) go.transform.position = pos;
            }

            // Prune.
            if (_beams.Count > seen.Count)
            {
                var toRemove = new List<Entity>();
                foreach (var kv in _beams)
                {
                    if (!seen.Contains(kv.Key))
                    {
                        if (kv.Value != null) Destroy(kv.Value);
                        toRemove.Add(kv.Key);
                    }
                }
                foreach (var k in toRemove) _beams.Remove(k);
            }
        }

        void OnDestroy()
        {
            foreach (var kv in _beams)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _beams.Clear();
        }

        private Material MaterialFor(RitualKind kind) => kind switch
        {
            RitualKind.Conversion        => _conversionMat,
            RitualKind.ViolentExtraction => _extractionMat,
            _                            => _purifyMat,
        };

        private static Material BuildBeamMaterial(Color color)
        {
            // URP/Lit-friendly fallback. Project ships with URP; if the shader
            // is missing at runtime, Unity returns the magenta error material
            // which is fine for a stand-in.
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Standard")
                       ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2.0f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);  // transparent
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
            return mat;
        }

        private static GameObject BuildBeamGameObject(Material mat, float height, float radius)
        {
            // Wrapper anchors to the ground; child cylinder is offset up by
            // half its scaled height so the base sits at y = wrapper.y.
            var wrapper = new GameObject("RitualBeam");
            var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inner.name = "Mesh";
            inner.transform.SetParent(wrapper.transform, false);

            var col = inner.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Unity cylinder is 2u tall by default. Scale.y = height*0.5
            // gives height in world. Lift the child by height*0.5 so the
            // bottom face rests on the wrapper origin.
            inner.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            inner.transform.localPosition = new Vector3(0, height * 0.5f, 0);

            var renderer = inner.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = mat;
            return wrapper;
        }
    }
}
