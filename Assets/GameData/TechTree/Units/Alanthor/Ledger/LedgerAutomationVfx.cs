// File: Assets/GameData/TechTree/Units/Alanthor/Ledger/LedgerAutomationVfx.cs
// Presentation manager for the Ledger's Automate Facility ability:
//   - a looping golden "machinery" aura on every building whose AutoYieldBoost
//     is active (rising sparks from a ring around the footprint), and
//   - a larger one-shot burst at the moment the ability lands on a building.
// Attached to RuntimeManagers by GameBootstrap.CreateManagersObject. All
// particles are procedural — the project ships no suitable authored FX.

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Presentation
{
    public class LedgerAutomationVfx : MonoBehaviour
    {
        private const float PollInterval = 0.25f;
        private static readonly Color Gold = new Color(1f, 0.82f, 0.32f, 0.95f);

        private EntityManager _em;
        private bool _emReady;
        private float _nextPoll;
        private readonly Dictionary<Entity, GameObject> _auras = new();
        private readonly List<Entity> _toRemove = new();
        private static Material _particleMat;

        // Cached per the managed-query leak rule (Core/CachedEntityQuery):
        // never CreateEntityQuery per poll in MonoBehaviour code.
        private TheWaningBorder.Core.CachedEntityQuery _boostQuery;
        private static readonly ComponentType[] BoostTypes =
        {
            ComponentType.ReadOnly<AutoYieldBoost>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };

        void Update()
        {
            if (!_emReady)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                _em = world.EntityManager;
                _emReady = true;
            }
            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollInterval;

            var world2 = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world2 == null || !world2.IsCreated) { _emReady = false; return; }

            // Current boosted buildings.
            var q = _boostQuery.Get(_em, BoostTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // New boosts: cast burst + attach aura.
            for (int i = 0; i < ents.Length; i++)
            {
                if (_auras.ContainsKey(ents[i])) continue;
                Vector3 pos = (Vector3)xfs[i].Position;
                SpawnBurst(pos);
                _auras[ents[i]] = SpawnAura(pos);
            }

            // Expired boosts / dead buildings: drop the aura.
            _toRemove.Clear();
            foreach (var kv in _auras)
            {
                bool alive = false;
                for (int i = 0; i < ents.Length; i++)
                    if (ents[i] == kv.Key) { alive = true; break; }
                if (!alive) _toRemove.Add(kv.Key);
            }
            foreach (var e in _toRemove)
            {
                if (_auras.TryGetValue(e, out var go) && go != null) Destroy(go);
                _auras.Remove(e);
            }
        }

        private static Material ParticleMat()
        {
            if (_particleMat == null)
            {
                // Procedural soft disc — GetBuiltinResource("Default-Particle.psd")
                // does not exist in Unity 6 (same pattern as
                // ProceduralBorderParticleGenerator.GetMoteTexture).
                const int res = 32;
                var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                {
                    name = "LedgerSparkDisc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                float c = (res - 1) * 0.5f;
                var px = new Color[res * res];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float dx = (x - c) / c;
                        float dy = (y - c) / c;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        px[y * res + x] = new Color(1f, 1f, 1f, a * a);
                    }
                tex.SetPixels(px);
                tex.Apply();

                _particleMat = new Material(Shader.Find("Sprites/Default"));
                _particleMat.mainTexture = tex;
            }
            return _particleMat;
        }

        /// <summary>Looping aura: golden sparks rising from a ring around the
        /// automated building for the boost's 30 s.</summary>
        private GameObject SpawnAura(Vector3 pos)
        {
            var go = new GameObject("LedgerAutomationAura");
            go.transform.position = pos + Vector3.up * 0.4f;
            go.transform.localEulerAngles = new Vector3(-90f, 0f, 0f); // circle shape flat on ground

            var ps = go.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem auto-plays; duration can only be set on
            // a fully stopped system (Unity logs an error otherwise).
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startColor = Gold;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 16f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 2.0f;
            shape.radiusThickness = 0.15f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = 1.1f; // rise

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Gold, 0f), new GradientColorKey(Gold, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMat();
            ps.Play();
            return go;
        }

        /// <summary>One-shot burst — deliberately LARGER than the aura — played
        /// the moment the Ledger's ability lands on a building.</summary>
        private void SpawnBurst(Vector3 pos)
        {
            var go = new GameObject("LedgerAutomationBurst");
            go.transform.position = pos + Vector3.up * 1.4f;

            var ps = go.AddComponent<ParticleSystem>();
            // Stop the auto-started system before configuring (see SpawnAura).
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 1.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            main.startColor = Gold;
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Gold, 0.25f), new GradientColorKey(Gold, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMat();
            ps.Play();
            Destroy(go, 3f);
        }
    }
}
