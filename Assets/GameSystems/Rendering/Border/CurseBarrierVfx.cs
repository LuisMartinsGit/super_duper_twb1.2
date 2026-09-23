// CurseBarrierVfx.cs
// The veil at the edge of cursed ground (docs/Design/Art_Direction.md §6.4).
// For every territory the curse holds, the boundary curve TerritoryBorderCurves
// already traces gets, instead of its purple decal line:
//   * a standing RIBBON mesh along the curve (3 m tall, terrain-following,
//     UV.x = arc length in metres) drawn by TWB/CurseBarrier — a translucent
//     body with scrolling emissive wisps of dark cyan and purple;
//   * a ParticleSystem of WISPS, emitted by hand along the curve every
//     frame (no shape module gymnastics), rising and swirling.
// Player / neutral borders keep their decals. Built once per territory when
// the curse takes it, torn down when the curse loses it — driven by the
// same TerritoryOwnership.Version compare the decal renderer uses, so the
// only per-frame work is that compare plus a handful of particle emits.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    public sealed class CurseBarrierVfx : MonoBehaviour
    {
        private CurseBarrierVfxConfig _cfg;
        private CurseBarrierVfxConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<CurseBarrierVfxConfig>());

        const string RibbonMaterialResource = "CurseBarrier/CurseBarrierRibbon";
        const string WispMaterialResource   = "CurseBarrier/CurseBarrierWisp";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!MapRegistry.IsGameplayScene(scene.name)) return;
            if (Object.FindFirstObjectByType<CurseBarrierVfx>() != null) return;
            new GameObject("[Curse Barrier]").AddComponent<CurseBarrierVfx>();
        }

        /// <summary>One held territory's veil.</summary>
        sealed class Barrier
        {
            public GameObject Root;
            public Mesh Mesh;
            public ParticleSystem Wisps;
            public List<Vector3> Base = new List<Vector3>();   // ground points along every loop
            public float Perimeter;
            public float EmitAccumulator;
        }

        readonly Dictionary<int, Barrier> _barriers = new Dictionary<int, Barrier>();
        readonly List<List<Vector2>> _loops = new List<List<Vector2>>();
        readonly List<int> _toDrop = new List<int>();
        Material _ribbonMat, _wispMat;
        int _ownershipVersion = -1;
        int _regionVersion = -1;
        bool _materialsMissing;

        void LateUpdate()
        {
            if (Cfg == null || _materialsMissing) return;
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            if (!TerritoryBorderCurves.CurvesReady) return;

            // Re-derive when ownership OR the partition changed (a new map in
            // the same process re-traces the curves).
            if (_ownershipVersion != TerritoryOwnership.Version || _regionVersion != RegionMap.Version)
            {
                _ownershipVersion = TerritoryOwnership.Version;
                _regionVersion = RegionMap.Version;
                double t0 = Time.realtimeSinceStartupAsDouble;
                Sync();
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("CurseBarrier.Sync",
                    (Time.realtimeSinceStartupAsDouble - t0) * 1000.0, $"veils={_barriers.Count}");
            }

            EmitWisps(Time.deltaTime);
        }

        void Sync()
        {
            if (!LoadMaterials()) return;

            int count = RegionMap.Count;
            _toDrop.Clear();
            foreach (var kv in _barriers)
                if (kv.Key >= count || TerritoryOwnership.OwnerOf(kv.Key) != TerritoryOwnership.Curse)
                    _toDrop.Add(kv.Key);
            for (int i = 0; i < _toDrop.Count; i++) { Destroy(_barriers[_toDrop[i]]); _barriers.Remove(_toDrop[i]); }

            for (int r = 0; r < count; r++)
            {
                if (TerritoryOwnership.OwnerOf(r) != TerritoryOwnership.Curse) continue;
                if (_barriers.ContainsKey(r)) continue;
                if (!TerritoryBorderCurves.TryGetLoops(r, _loops) || _loops.Count == 0) continue;
                _barriers[r] = Build(r, _loops);
            }
        }

        bool LoadMaterials()
        {
            if (_ribbonMat != null && _wispMat != null) return true;
            _ribbonMat = Resources.Load<Material>(RibbonMaterialResource);
            _wispMat = Resources.Load<Material>(WispMaterialResource);
            if (_ribbonMat == null || _wispMat == null)
            {
                // Same trap as the corpse dissolve: an unreferenced shader is
                // stripped from a player build, so the materials are assets in
                // Resources/, and their absence is loud, once.
                Debug.LogError("[CurseBarrierVfx] Missing Resources/CurseBarrier/*.mat — the curse boundary has no veil.");
                _materialsMissing = true;
                return false;
            }
            return true;
        }

        Barrier Build(int territory, List<List<Vector2>> loops)
        {
            var b = new Barrier();
            b.Root = new GameObject($"CurseBarrier_{territory}");
            b.Root.transform.SetParent(transform, false);

            // The veil is an ARCH in cross-section, not a vertical sheet: feet
            // on both sides of the boundary, peak above it. A single sheet is
            // edge-on — invisible — wherever the boundary runs toward the RTS
            // camera; an arch always has a face turned toward it.
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float h = Cfg.ribbonHeight, off = Cfg.groundOffset, halfW = Cfg.archHalfWidth;
            const int Arc = 4;                       // quads across the arch

            foreach (var loop in loops)
            {
                if (loop.Count < 2) continue;
                // Resample to the ribbon spacing; the source curve is already
                // smooth, so this is only for vertex count.
                var pts = new List<Vector2>();
                Vector2 prev = loop[0];
                pts.Add(prev);
                for (int i = 1; i <= loop.Count; i++)
                {
                    Vector2 p = loop[i % loop.Count];
                    if (i < loop.Count && (p - prev).magnitude < Cfg.ribbonSpacing * 0.5f) continue;
                    pts.Add(p); prev = p;
                }
                if (pts.Count < 2) continue;

                // Two nested arches give the veil thickness: an outer one and
                // a narrower, lower inner one on its own noise phase (UV.x
                // offset), so the layers do not pulse in lockstep.
                for (int layer = 0; layer < 2; layer++)
                {
                    float lw = layer == 0 ? halfW : halfW * 0.35f;
                    float lh = layer == 0 ? h : h * 0.7f;
                    float uOffset = layer * 37f;
                    float arc = 0f;
                    int first = verts.Count;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector2 p = pts[i];
                        if (i > 0) arc += (p - pts[i - 1]).magnitude;
                        Vector2 tan = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                        Vector2 across = new Vector2(-tan.y, tan.x);
                        float y0 = TerrainUtility.GetHeight(p.x, p.y) + off;
                        for (int k = 0; k <= Arc; k++)
                        {
                            float theta = Mathf.PI * k / Arc;
                            Vector2 xz = p + across * (lw * Mathf.Cos(theta));
                            float rise = Mathf.Sin(theta);
                            // Feet follow the terrain on their own side.
                            float y = (k == 0 || k == Arc ? TerrainUtility.GetHeight(xz.x, xz.y) + off : y0) + lh * rise;
                            verts.Add(new Vector3(xz.x, y, xz.y));
                            uvs.Add(new Vector2(arc + uOffset, rise));
                        }
                        if (layer == 0) b.Base.Add(new Vector3(p.x, y0, p.y));
                    }
                    if (layer == 0) b.Perimeter += arc;
                    int stride = Arc + 1;
                    int columns = (verts.Count - first) / stride;
                    for (int c = 0; c < columns - 1; c++)
                    {
                        for (int k = 0; k < Arc; k++)
                        {
                            int a0 = first + c * stride + k, a1 = a0 + 1;
                            int b0 = a0 + stride, b1 = b0 + 1;
                            tris.Add(a0); tris.Add(a1); tris.Add(b0);
                            tris.Add(a1); tris.Add(b1); tris.Add(b0);
                        }
                    }
                }
            }

            b.Mesh = new Mesh { name = $"CurseBarrier_{territory}" };
            b.Mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            b.Mesh.SetVertices(verts);
            b.Mesh.SetUVs(0, uvs);
            b.Mesh.SetTriangles(tris, 0);
            b.Mesh.RecalculateBounds();

            var ribbon = new GameObject("Ribbon");
            ribbon.transform.SetParent(b.Root.transform, false);
            ribbon.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var mr = ribbon.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ribbonMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            b.Wisps = BuildWisps(b.Root.transform, Mathf.Min(Cfg.maxWispsPerTerritory,
                Mathf.CeilToInt(b.Perimeter * Cfg.wispsPerMetrePerSecond * Cfg.wispLifetimeMax) + 16));
            return b;
        }

        ParticleSystem BuildWisps(Transform parent, int maxParticles)
        {
            var go = new GameObject("Wisps");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;          // emitted by hand along the curve

            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(Cfg.wispPeakAlpha, 0.25f),
                        new GradientAlphaKey(Cfg.wispPeakAlpha * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.5f, 1f), new Keyframe(1f, 1.25f)));

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = Cfg.wispNoiseStrength;
            noise.frequency = Cfg.wispNoiseFrequency;
            noise.scrollSpeed = 0.3f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = _wispMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;

            ps.Play(true);
            return ps;
        }

        void EmitWisps(float dt)
        {
            if (_barriers.Count == 0) return;
            var ep = new ParticleSystem.EmitParams();
            foreach (var kv in _barriers)
            {
                var b = kv.Value;
                if (b.Base.Count == 0 || b.Wisps == null) continue;
                b.EmitAccumulator += b.Perimeter * Cfg.wispsPerMetrePerSecond * dt;
                int n = Mathf.FloorToInt(b.EmitAccumulator);
                if (n <= 0) continue;
                b.EmitAccumulator -= n;
                n = Mathf.Min(n, 32);
                for (int i = 0; i < n; i++)
                {
                    // A random point along the boundary, jittered across it.
                    int k = Random.Range(0, b.Base.Count);
                    var p = b.Base[k];
                    p.x += Random.Range(-0.6f, 0.6f);
                    p.z += Random.Range(-0.6f, 0.6f);
                    p.y += Random.Range(0.1f, 0.8f);
                    ep.position = p;
                    ep.velocity = new Vector3(0f, Cfg.wispRiseSpeed * Random.Range(0.6f, 1.4f), 0f);
                    ep.startLifetime = Random.Range(Cfg.wispLifetimeMin, Cfg.wispLifetimeMax);
                    ep.startSize = Random.Range(Cfg.wispSizeMin, Cfg.wispSizeMax);
                    ep.startColor = Color.Lerp(Cfg.wispCyan, Cfg.wispPurple, Random.value);
                    ep.rotation = Random.Range(0f, 360f);
                    b.Wisps.Emit(ep, 1);
                }
            }
        }

        void Destroy(Barrier b)
        {
            if (b == null) return;
            if (b.Mesh != null) Object.Destroy(b.Mesh);
            if (b.Root != null) Object.Destroy(b.Root);
        }

        void OnDestroy()
        {
            foreach (var kv in _barriers) Destroy(kv.Value);
            _barriers.Clear();
        }
    }
}
