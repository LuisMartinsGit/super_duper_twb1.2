// CurseBeaconVfx.cs
// Readability pass (2026-08-04, "the game is hard to read"):
//   * BEACONS — a shining vertical light column over every curse node
//     (Sporeling) and every well (BorderMainNode), so the objectives read
//     at a glance from any camera height.
//   * EMERGENCE PULSES — a pulsating ground ring at every place something
//     is about to rise: telegraphed corruptions (PendingCorruption) and
//     announced blood contaminations (BloodCurseSpawnSystem.Pending).
// Presentation-only: reads sim state, owns pooled primitive GameObjects,
// never writes to the ECS world. Mounted by GameBootstrap.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Presentation
{
    public sealed class CurseBeaconVfx : MonoBehaviour
    {
        private const float PollInterval = 1f;
        private const float BeaconHeight = 26f;
        private const float BeaconRadiusNode = 0.5f;
        private const float BeaconRadiusWell = 1.1f;
        private const float PulseBaseRadius = 4f;

        private static readonly Color BeaconColor = new Color(0.75f, 0.4f, 1f, 0.55f);
        private static readonly Color PulseColor = new Color(0.8f, 0.35f, 1f, 0.6f);

        private float _nextPoll;
        private Material _mat;
        private readonly List<GameObject> _beacons = new();
        private readonly List<GameObject> _pulses = new();
        private int _beaconsUsed, _pulsesUsed;

        private Material Mat()
        {
            if (_mat != null) return _mat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
            _mat = new Material(shader) { name = "CurseBeaconMat" };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _mat.SetInt("_ZWrite", 0);
            _mat.renderQueue = 3200;
            if (_mat.HasProperty("_Surface")) _mat.SetFloat("_Surface", 1);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
            _mat.EnableKeyword("_ALPHABLEND_ON");
            return _mat;
        }

        private GameObject MakePrimitive(List<GameObject> pool, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_Color", color);
            mr.SetPropertyBlock(mpb);
            go.transform.SetParent(transform, false);
            pool.Add(go);
            return go;
        }

        private GameObject Take(List<GameObject> pool, ref int used, Color color)
        {
            GameObject go = used < pool.Count ? pool[used] : MakePrimitive(pool, color);
            used++;
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        private void Update()
        {
            // Pulse animation every frame (cheap — a few transforms).
            float pulse = 0.75f + 0.35f * Mathf.Sin(Time.time * 6f);
            for (int i = 0; i < _pulsesUsed && i < _pulses.Count; i++)
            {
                var t = _pulses[i].transform;
                var s = t.localScale;
                t.localScale = new Vector3(PulseBaseRadius * pulse, s.y, PulseBaseRadius * pulse);
            }

            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollInterval;
            Rebuild();
        }

        private void Rebuild()
        {
            _beaconsUsed = 0;
            _pulsesUsed = 0;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { Deactivate(); return; }
            var em = world.EntityManager;

            // ── Beacons: curse nodes + wells ──
            PlaceBeacons<SporelingTag>(em, BeaconRadiusNode);
            PlaceBeacons<BorderMainNodeTag>(em, BeaconRadiusWell);

            // ── Emergence pulses: telegraphed corruptions ──
            var regQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PendingCorruption>());
            using (var regs = regQuery.ToEntityArray(Allocator.Temp))
            {
                for (int r = 0; r < regs.Length; r++)
                {
                    var buf = em.GetBuffer<PendingCorruption>(regs[r]);
                    for (int i = 0; i < buf.Length; i++)
                        PlacePulse(new Vector3(buf[i].Pos.x, buf[i].Pos.y + 0.3f, buf[i].Pos.z));
                }
            }

            // ── Emergence pulses: announced blood contaminations ──
            var pendingBlood = TheWaningBorder.Systems.Border.BloodCurseSpawnSystem.Pending;
            for (int i = 0; i < pendingBlood.Count; i++)
            {
                var p = pendingBlood[i].Pos;
                PlacePulse(new Vector3(p.x, p.y + 0.3f, p.z));
            }

            Deactivate();
        }

        private void PlaceBeacons<T>(EntityManager em, float radius) where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                var go = Take(_beacons, ref _beaconsUsed, BeaconColor);
                go.transform.position = new Vector3(
                    xfs[i].Position.x, xfs[i].Position.y + BeaconHeight * 0.5f, xfs[i].Position.z);
                go.transform.localScale = new Vector3(radius, BeaconHeight * 0.5f, radius);
            }
        }

        private void PlacePulse(Vector3 pos)
        {
            var go = Take(_pulses, ref _pulsesUsed, PulseColor);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(PulseBaseRadius, 0.12f, PulseBaseRadius);
        }

        private void Deactivate()
        {
            for (int i = _beaconsUsed; i < _beacons.Count; i++)
                if (_beacons[i].activeSelf) _beacons[i].SetActive(false);
            for (int i = _pulsesUsed; i < _pulses.Count; i++)
                if (_pulses[i].activeSelf) _pulses[i].SetActive(false);
        }
    }
}
