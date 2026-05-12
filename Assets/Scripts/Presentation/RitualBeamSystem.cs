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
    /// <summary>
    /// MonoBehaviour singleton that mirrors ECS ActiveRitualOnNode entities
    /// into managed GameObject beam visuals. Created by GameBootstrap.
    /// </summary>
    public class RitualBeamSystem : MonoBehaviour
    {
        private const float BeamHeight = 28f;
        private const float BeamRadius = 0.35f;
        private const float BeamYOffset = 0f;

        // Live beams keyed by node entity. NetworkedEntity's NetworkId would
        // be more robust but the node ECS entity is the natural anchor in
        // singleplayer.
        private readonly Dictionary<Entity, GameObject> _beams = new();
        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _ritualQuery;
        private Material _purifyMat;
        private Material _conversionMat;
        private Material _extractionMat;

        void Awake()
        {
            _purifyMat = BuildBeamMaterial(new Color(0.65f, 0.95f, 1.0f, 0.6f));
            _conversionMat = BuildBeamMaterial(new Color(0.45f, 1.00f, 0.55f, 0.6f));
            _extractionMat = BuildBeamMaterial(new Color(1.00f, 0.45f, 0.20f, 0.6f));
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
                _ritualQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<ActiveRitualOnNode>(),
                    ComponentType.ReadOnly<LocalTransform>());
            }

            // Snapshot current rituals.
            using var ents = _ritualQuery.ToEntityArray(Allocator.Temp);
            using var actives = _ritualQuery.ToComponentDataArray<ActiveRitualOnNode>(Allocator.Temp);
            using var transforms = _ritualQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Reconcile: build a set of live ECS rituals, spawn/update beams,
            // then prune beams whose ECS source is gone.
            var seen = new HashSet<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                seen.Add(ents[i]);
                var pos = transforms[i].Position;
                pos.y += BeamYOffset;
                if (!_beams.TryGetValue(ents[i], out var go) || go == null)
                {
                    go = BuildBeamGameObject(MaterialFor(actives[i].Kind));
                    _beams[ents[i]] = go;
                }
                if (go != null)
                {
                    go.transform.position = pos;
                }
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

        private static GameObject BuildBeamGameObject(Material mat)
        {
            // Wrapper anchors to the ground; child cylinder is offset up by
            // half its scaled height so the base sits at y = wrapper.y.
            var wrapper = new GameObject("RitualBeam");
            var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inner.name = "Mesh";
            inner.transform.SetParent(wrapper.transform, false);

            var col = inner.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Unity cylinder is 2u tall by default. Scale.y = BeamHeight*0.5
            // gives BeamHeight in world. Lift the child by BeamHeight*0.5 so
            // the bottom face of the cylinder rests on the wrapper origin.
            inner.transform.localScale = new Vector3(BeamRadius * 2f, BeamHeight * 0.5f, BeamRadius * 2f);
            inner.transform.localPosition = new Vector3(0, BeamHeight * 0.5f, 0);

            var renderer = inner.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = mat;
            return wrapper;
        }
    }
}
