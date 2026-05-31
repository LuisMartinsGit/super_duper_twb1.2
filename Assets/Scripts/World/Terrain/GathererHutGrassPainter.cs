// File: Assets/Scripts/World/Terrain/GathererHutGrassPainter.cs
//
// Spawns flat yellow grass patches inside a disc around each completed
// GathererHut — stylised "field" look: a handful of rectangular ground
// quads of varying sizes and rotations, rejection-sampled so they don't
// overlap, with terrain-height sampled at each corner so patches conform
// to slopes.
//
// We bypass Unity's Terrain Detail/Grass system entirely — in URP (and
// our drawInstanced=true setup) the terrain detail pipeline silently
// swallows custom-material details (verified: SetDetailLayer writes
// survive readback, but nothing renders). Each hut gets a single child
// GameObject holding a combined mesh of all its patches on a URP/Unlit
// solid-yellow material, which goes through the regular MeshRenderer
// path and Just Works™.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.World.Terrain
{
    [DefaultExecutionOrder(920)]
    public class GathererHutGrassPainter : MonoBehaviour
    {
        // Disc radius matches the income gather radius — visual = mechanic.
        private const float GrassRadius = GathererHutIncomeSystem.GatherRadius;

        // Don't drop patches over the hut footprint.
        private const float InnerExclusionRadius = 2.5f;

        // Patch tuning. Aim for a handful of distinct rectangles.
        private const int   TargetPatchesPerDisc = 7;
        private const float PatchMinLong  = 4.0f;
        private const float PatchMaxLong  = 11.0f;
        private const float PatchMinShort = 2.5f;
        private const float PatchMaxShort = 6.0f;
        private const float PatchEdgeMargin = 0.5f; // gap from disc edge
        private const float PatchInterGap   = 0.5f; // gap between patches
        private const float GroundLift      = 0.05f; // above terrain to avoid z-fight
        private const float CornerInsetFromBounds = 1.0f; // bounds pad for safety

        // World metres per texture tile. Smaller = more visible blade detail
        // but more obvious repetition. 2m reads as a dense "grass field" at
        // RTS camera height without obvious tiling.
        private const float GrassTileSize = 2.0f;

        // Shared resources — one material + one texture reused across all hut discs.
        private static Material _grassMaterial;
        private static Texture2D _grassTex;

        // Per-hut grass GameObject (child of this painter — keeps the scene
        // hierarchy tidy and ensures cleanup on painter destruction).
        private readonly Dictionary<Entity, GameObject> _discs = new();

        private EntityWorld _world;
        private EntityManager _em;

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            _em = _world.EntityManager;

            EnsureSharedResources();

            var hutQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            var entities = hutQuery.ToEntityArray(Allocator.Temp);
            var transforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Pass 1: spawn grass on newly-completed huts
            var liveCompleted = new HashSet<Entity>();
            for (int i = 0; i < entities.Length; i++)
            {
                if (_em.HasComponent<UnderConstruction>(entities[i])) continue;
                liveCompleted.Add(entities[i]);

                if (!_discs.ContainsKey(entities[i]))
                    SpawnDisc(entities[i], transforms[i].Position);
            }

            entities.Dispose();
            transforms.Dispose();

            // Pass 2: remove discs whose hut died or reverted to construction
            List<Entity> toRemove = null;
            foreach (var key in _discs.Keys)
            {
                if (liveCompleted.Contains(key)) continue;
                if (_em.Exists(key) && !_em.HasComponent<UnderConstruction>(key))
                    continue;
                toRemove ??= new List<Entity>();
                toRemove.Add(key);
            }
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                    DespawnDisc(toRemove[i]);
            }
        }

        // ── Per-hut disc spawn ─────────────────────────────────────────────
        private void SpawnDisc(Entity entity, float3 worldPos)
        {
            var go = new GameObject($"GrassDisc_{entity.Index}");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = new Vector3(worldPos.x, 0f, worldPos.z);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _grassMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            mf.sharedMesh = BuildDiscMesh(worldPos, entity.Index);

            _discs[entity] = go;
        }

        private void DespawnDisc(Entity entity)
        {
            if (_discs.TryGetValue(entity, out var go))
            {
                if (go != null)
                {
                    if (go.GetComponent<MeshFilter>() is var mf && mf != null && mf.sharedMesh != null)
                        Destroy(mf.sharedMesh);
                    Destroy(go);
                }
                _discs.Remove(entity);
            }
        }

        // ── Mesh builder ───────────────────────────────────────────────────
        // Builds a single combined mesh of flat rectangular grass patches
        // inside the disc. Patches are placed by rejection sampling so they
        // don't overlap each other; each corner samples terrain height so
        // patches conform to slopes.
        private static Mesh BuildDiscMesh(float3 hutPos, int seed)
        {
            var rng = new Unity.Mathematics.Random((uint)(seed * 73856093 ^ 0xA110CA7) | 1u);

            float innerR = InnerExclusionRadius;
            float outerR = GrassRadius - PatchEdgeMargin;

            // Each placed patch tracked by center + bounding-circle radius
            // (max half-dimension) for cheap rejection.
            var placedCenters = new List<float2>(TargetPatchesPerDisc);
            var placedRadii   = new List<float>(TargetPatchesPerDisc);

            // Output corners (mesh-local: parented at hut.xz, y in world)
            var quadCorners = new List<Vector3>(TargetPatchesPerDisc * 4);

            int safety = TargetPatchesPerDisc * 12;
            while (placedCenters.Count < TargetPatchesPerDisc && safety-- > 0)
            {
                // Uniform-area sample inside (innerR, outerR)
                float u = rng.NextFloat();
                float v = rng.NextFloat();
                float r = math.sqrt(u) * outerR;
                if (r < innerR) continue;
                float a = v * math.PI * 2f;
                float lx = math.cos(a) * r;
                float lz = math.sin(a) * r;

                // Random rectangle dimensions
                float lng = rng.NextFloat(PatchMinLong,  PatchMaxLong);
                float wid = rng.NextFloat(PatchMinShort, PatchMaxShort);
                float halfBound = math.max(lng, wid) * 0.5f;

                // Reject if the patch's bounding circle overlaps an existing one
                bool reject = false;
                for (int p = 0; p < placedCenters.Count; p++)
                {
                    float dx = lx - placedCenters[p].x;
                    float dz = lz - placedCenters[p].y;
                    float minDist = halfBound + placedRadii[p] + PatchInterGap;
                    if (dx * dx + dz * dz < minDist * minDist) { reject = true; break; }
                }
                if (reject) continue;

                // Build the rectangle's four corners in world XZ, sample terrain Y
                float yaw = rng.NextFloat(0f, math.PI * 2f);
                float cs = math.cos(yaw), sn = math.sin(yaw);
                Vector3 axL = new Vector3( cs, 0f,  sn) * (lng * 0.5f); // long axis
                Vector3 axW = new Vector3(-sn, 0f,  cs) * (wid * 0.5f); // short axis
                Vector3 cWorldXZ = new Vector3(hutPos.x + lx, 0f, hutPos.z + lz);

                Vector3 c0 = cWorldXZ - axL - axW;
                Vector3 c1 = cWorldXZ + axL - axW;
                Vector3 c2 = cWorldXZ + axL + axW;
                Vector3 c3 = cWorldXZ - axL + axW;

                c0.y = TerrainUtility.GetHeight(c0.x, c0.z) + GroundLift;
                c1.y = TerrainUtility.GetHeight(c1.x, c1.z) + GroundLift;
                c2.y = TerrainUtility.GetHeight(c2.x, c2.z) + GroundLift;
                c3.y = TerrainUtility.GetHeight(c3.x, c3.z) + GroundLift;

                // Express in mesh-local (parented at hut.xz with y=0)
                quadCorners.Add(new Vector3(c0.x - hutPos.x, c0.y, c0.z - hutPos.z));
                quadCorners.Add(new Vector3(c1.x - hutPos.x, c1.y, c1.z - hutPos.z));
                quadCorners.Add(new Vector3(c2.x - hutPos.x, c2.y, c2.z - hutPos.z));
                quadCorners.Add(new Vector3(c3.x - hutPos.x, c3.y, c3.z - hutPos.z));

                placedCenters.Add(new float2(lx, lz));
                placedRadii.Add(halfBound);
            }

            int n = placedCenters.Count;
            var verts   = new Vector3[n * 4];
            var uvs     = new Vector2[n * 4];
            var normals = new Vector3[n * 4];
            var tris    = new int[n * 6];

            for (int i = 0; i < n; i++)
            {
                int v0 = i * 4;
                verts[v0 + 0] = quadCorners[i * 4 + 0];
                verts[v0 + 1] = quadCorners[i * 4 + 1];
                verts[v0 + 2] = quadCorners[i * 4 + 2];
                verts[v0 + 3] = quadCorners[i * 4 + 3];
                // UVs scaled to world dimensions / tile size — texture wraps in
                // Repeat mode so each patch shows multiple tile reps. Fetch
                // patch dimensions back from the corner geometry rather than
                // tracking lng/wid separately.
                float uPatchLng = (quadCorners[i * 4 + 1] - quadCorners[i * 4 + 0]).magnitude / GrassTileSize;
                float vPatchWid = (quadCorners[i * 4 + 3] - quadCorners[i * 4 + 0]).magnitude / GrassTileSize;
                uvs[v0 + 0] = new Vector2(0,        0);
                uvs[v0 + 1] = new Vector2(uPatchLng, 0);
                uvs[v0 + 2] = new Vector2(uPatchLng, vPatchWid);
                uvs[v0 + 3] = new Vector2(0,        vPatchWid);
                normals[v0 + 0] = normals[v0 + 1] = normals[v0 + 2] = normals[v0 + 3] = Vector3.up;

                int t0 = i * 6;
                tris[t0 + 0] = v0 + 0; tris[t0 + 1] = v0 + 2; tris[t0 + 2] = v0 + 1;
                tris[t0 + 3] = v0 + 0; tris[t0 + 4] = v0 + 3; tris[t0 + 5] = v0 + 2;
            }

            var mesh = new Mesh { name = $"GrassDiscMesh_{seed}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            // Small bounds pad — slope conformity may push the highest corner
            // a little above the local terrain plane.
            var b = mesh.bounds;
            b.Expand(new Vector3(CornerInsetFromBounds, 0.5f, CornerInsetFromBounds));
            mesh.bounds = b;
            return mesh;
        }

        // ── Shared material + texture ──────────────────────────────────────
        private static void EnsureSharedResources()
        {
            if (_grassMaterial != null) return;

            _grassTex = GenerateTopDownGrassTile(256);

            // URP/Unlit so the patches stay flat-shaded regardless of sun
            // position. Falls back to URP/Lit then Built-in if Unlit isn't
            // available.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Texture")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            _grassMaterial = new Material(shader) { name = "YellowGrass_Mat" };

            if (_grassMaterial.HasProperty("_BaseMap"))  _grassMaterial.SetTexture("_BaseMap", _grassTex);
            if (_grassMaterial.HasProperty("_MainTex"))  _grassMaterial.SetTexture("_MainTex", _grassTex);
            // _BaseColor as multiplier — keep white so texture colour comes through.
            if (_grassMaterial.HasProperty("_BaseColor")) _grassMaterial.SetColor("_BaseColor", Color.white);
            if (_grassMaterial.HasProperty("_Color"))     _grassMaterial.SetColor("_Color",     Color.white);
            // Surface=Opaque, no alpha clip — patches are solid rectangles.
            if (_grassMaterial.HasProperty("_Surface"))   _grassMaterial.SetFloat("_Surface", 0f);
            if (_grassMaterial.HasProperty("_AlphaClip")) _grassMaterial.SetFloat("_AlphaClip", 0f);
            if (_grassMaterial.HasProperty("_Cull"))      _grassMaterial.SetFloat("_Cull", 2f); // back-face cull
            _grassMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            _grassMaterial.enableInstancing = true;
        }

        // ── Procedural top-down grass tile ─────────────────────────────────
        // Bright yellow base + ~250 darker yellow blade strokes scattered with
        // random positions / rotations / lengths. Strokes that cross the tile
        // edge are wrapped to the opposite edge so the tile is seamless when
        // repeated. Deterministic seed → identical tile every run.
        private static Texture2D GenerateTopDownGrassTile(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: false);
            tex.name = "YellowGrass_TileTex";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Repeat;

            var pixels = new Color32[size * size];

            // Base fill: bright golden yellow with a tiny per-pixel dither so
            // it doesn't read as a flat slab even before blades are drawn.
            var rng = new System.Random(0xF1E1D);
            for (int i = 0; i < pixels.Length; i++)
            {
                int jitter = rng.Next(-8, 9);
                pixels[i] = new Color32(
                    (byte)Mathf.Clamp(245 + jitter, 0, 255),
                    (byte)Mathf.Clamp(220 + jitter, 0, 255),
                    (byte)Mathf.Clamp( 60 + jitter / 2, 0, 255),
                    255);
            }

            // Blade strokes
            const int strokes = 320;
            for (int s = 0; s < strokes; s++)
            {
                float cx = (float)rng.NextDouble() * size;
                float cy = (float)rng.NextDouble() * size;
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float len = 3f + (float)rng.NextDouble() * 6f;
                float dx = Mathf.Cos(angle);
                float dy = Mathf.Sin(angle);

                // Darker olive-yellow tones for the blades, with mild variation
                int variant = rng.Next(0, 3);
                byte br, bg, bb;
                switch (variant)
                {
                    case 0:  br = 170; bg = 145; bb = 30; break; // dark olive
                    case 1:  br = 200; bg = 175; bb = 45; break; // mid yellow
                    default: br = 145; bg = 120; bb = 25; break; // very dark
                }
                int rJ = rng.Next(-15, 16);
                br = (byte)Mathf.Clamp(br + rJ, 0, 255);
                bg = (byte)Mathf.Clamp(bg + rJ, 0, 255);
                bb = (byte)Mathf.Clamp(bb + rJ / 2, 0, 255);

                // Walk the stroke pixel-by-pixel; alpha tapers from base (1.0)
                // to tip (0.3) so blades feel pointed.
                int steps = Mathf.CeilToInt(len);
                for (int t = 0; t < steps; t++)
                {
                    float fr = t / (float)Mathf.Max(1, steps - 1);
                    float px = cx + dx * t;
                    float py = cy + dy * t;
                    float alpha = Mathf.Lerp(1.0f, 0.30f, fr);

                    // 2-px wide stroke — write the centre pixel and a soft neighbour
                    // pair perpendicular to the stroke direction.
                    float nx = -dy;
                    float ny =  dx;
                    PutPixelWrapped(pixels, size, px,            py,            br, bg, bb, alpha);
                    PutPixelWrapped(pixels, size, px + nx * 0.5f, py + ny * 0.5f, br, bg, bb, alpha * 0.6f);
                    PutPixelWrapped(pixels, size, px - nx * 0.5f, py - ny * 0.5f, br, bg, bb, alpha * 0.6f);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return tex;
        }

        // Modular wrap-around pixel write. `coverage` is 0..1 — we lerp the
        // existing pixel toward the new colour by that amount, so overlapping
        // strokes deepen organically rather than overwriting.
        private static void PutPixelWrapped(Color32[] pixels, int size,
            float fx, float fy, byte r, byte g, byte b, float coverage)
        {
            // Wrap into [0, size)
            int ix = ((Mathf.FloorToInt(fx) % size) + size) % size;
            int iy = ((Mathf.FloorToInt(fy) % size) + size) % size;
            int idx = iy * size + ix;
            var p = pixels[idx];
            float t = Mathf.Clamp01(coverage);
            pixels[idx] = new Color32(
                (byte)(p.r + (r - p.r) * t),
                (byte)(p.g + (g - p.g) * t),
                (byte)(p.b + (b - p.b) * t),
                255);
        }

        void OnDestroy()
        {
            foreach (var kv in _discs)
            {
                if (kv.Value == null) continue;
                if (kv.Value.GetComponent<MeshFilter>() is var mf && mf != null && mf.sharedMesh != null)
                    Destroy(mf.sharedMesh);
                Destroy(kv.Value);
            }
            _discs.Clear();
        }
    }
}
