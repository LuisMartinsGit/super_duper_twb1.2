// GroundDecals.cs
// Shared pool for the small ground decals the game draws under things:
// selection rings, building contours, spell aim previews.
//
// Before this, each of those drew itself as world geometry - a LineRenderer
// ring or a flat Quad - which sits at ONE height and so floats or sinks the
// moment the ground is not level. A decal is projected onto the terrain, so it
// follows every slope for free. That is the whole reason to prefer them.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Rents pooled <see cref="DecalProjector"/>s that paint the terrain only.
    /// </summary>
    public static class GroundDecals
    {
        /// <summary>
        /// TERRAIN ONLY, and it must stay that way. Everything inside a
        /// projector's volume receives the decal otherwise — a selection ring
        /// would be painted across the unit standing in it, and across anything
        /// walking through. Matches InfluenceOverlayRenderer's bit: the renderer
        /// feature has decal layers enabled, every renderer ships on bit 0, and
        /// only the terrain is opted into bit 1 below.
        /// </summary>
        public const uint TerrainDecalLayerBit = 1u << 1;

        /// <summary>
        /// Height of the projection volume, centred on the ground point. Deep
        /// enough to cover the slope a decal spans, shallow enough not to reach
        /// terrain on the far side of a ridge.
        /// </summary>
        const float Depth = 24f;

        /// <summary>
        /// The decal material lives in Resources so its URP Decal shader ships
        /// in player builds — Shader.Find on an unreferenced shader is stripped
        /// (the fog shader lesson). Instances are made from it, never the asset.
        /// </summary>
        const string SourceMaterial = "TWBTerritoryBorderDecal";

        static Material _source;
        static Transform _root;
        static int _terrainVersion = -1;

        // One material per (texture, colour). Bounded in practice: a handful of
        // shapes across eight faction colours.
        static readonly Dictionary<(Texture2D, uint), Material> _materials = new();
        static readonly Dictionary<int, Texture2D> _shapes = new();
        static readonly Stack<DecalProjector> _pool = new();

        #region Rent / Return

        /// <summary>A projector painting <paramref name="shape"/> in <paramref name="tint"/>.</summary>
        public static DecalProjector Rent(Texture2D shape, Color tint)
        {
            EnsureRoot();

            var p = _pool.Count > 0 ? _pool.Pop() : Create();
            p.material = MaterialFor(shape, tint);
            p.gameObject.SetActive(true);
            return p;
        }

        /// <summary>Hand a projector back. Safe with null.</summary>
        public static void Return(DecalProjector p)
        {
            if (p == null) return;
            p.gameObject.SetActive(false);
            _pool.Push(p);
        }

        /// <summary>Repaint without renting again.</summary>
        public static void SetShape(DecalProjector p, Texture2D shape, Color tint)
        {
            if (p != null) p.material = MaterialFor(shape, tint);
        }

        // Caller-owned pretinted textures -> their material. The texture
        // object is the key: repainting its CONTENT (SetPixels + Apply)
        // needs no new material, so a caller that owns a long-lived texture
        // pays one material for its whole lifetime.
        static readonly Dictionary<Texture2D, Material> _pretinted = new();

        /// <summary>
        /// Paint a caller-owned texture that already carries its colour —
        /// for shapes too big or too dynamic for the shared tint cache
        /// (territory borders repaint their own texture per owner change).
        /// </summary>
        public static void SetPretinted(DecalProjector p, Texture2D tinted)
        {
            if (p == null || tinted == null) return;

            if (!_pretinted.TryGetValue(tinted, out var m) || m == null)
            {
                if (!EnsureSource()) return;

                // Bounded sweep: entries whose texture died (end of match)
                // would otherwise pin dead materials forever.
                if (_pretinted.Count > 128)
                {
                    var dead = new List<Texture2D>();
                    foreach (var kv in _pretinted)
                        if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
                    foreach (var k in dead) _pretinted.Remove(k);
                }

                m = new Material(_source) { name = $"GroundDecalPretinted_{tinted.name}" };
                m.SetTexture("Base_Map", tinted);
                m.SetTexture("_BaseMap", tinted);
                _pretinted[tinted] = m;
            }
            p.material = m;
        }

        /// <summary>
        /// Sit the decal on the ground at <paramref name="worldPos"/>, sized
        /// <paramref name="width"/> x <paramref name="depth"/> metres.
        ///
        /// The volume is CENTRED on the point rather than hung below it, so a
        /// decal straddling a slope still finds terrain on both sides.
        /// </summary>
        public static void Place(DecalProjector p, Vector3 worldPos,
                                 float width, float depth, float yawDegrees = 0f)
        {
            if (p == null) return;

            OptInTerrain();

            var t = p.transform;
            t.SetPositionAndRotation(worldPos, Quaternion.Euler(90f, yawDegrees, 0f));
            p.size = new Vector3(width, depth, Depth);
            p.pivot = Vector3.zero;
        }

        /// <summary>Square helper for rings and other round shapes.</summary>
        public static void Place(DecalProjector p, Vector3 worldPos, float diameter)
            => Place(p, worldPos, diameter, diameter);

        #endregion

        #region Shapes

        /// <summary>
        /// A ring, cached by thickness. <paramref name="thickness"/> is the band
        /// width as a fraction of the radius.
        /// </summary>
        public static Texture2D Ring(float thickness = 0.14f, int resolution = 128)
        {
            int key = Mathf.RoundToInt(thickness * 1000f) * 31 + resolution;
            if (_shapes.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewShapeTexture(resolution, "GroundDecalRing");
            var px = new Color32[resolution * resolution];

            float outer = 0.5f;
            float inner = Mathf.Max(0.02f, outer - thickness * 0.5f);
            float feather = 1.5f / resolution;

            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float dx = (x + 0.5f) / resolution - 0.5f;
                float dy = (y + 0.5f) / resolution - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                // Fade at BOTH edges of the band, or the ring aliases badly at
                // the small on-screen sizes a selection ring is drawn at.
                float a = Mathf.Min(Mathf.InverseLerp(outer, outer - feather, d),
                                    Mathf.InverseLerp(inner, inner + feather, d));
                px[y * resolution + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            _shapes[key] = tex;
            return tex;
        }

        /// <summary>
        /// A hollow rectangle, for a building footprint contour. Cached by
        /// thickness (fraction of the shorter side).
        /// </summary>
        public static Texture2D RectContour(float thickness = 0.10f, int resolution = 128)
        {
            int key = 1_000_000 + Mathf.RoundToInt(thickness * 1000f) * 31 + resolution;
            if (_shapes.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewShapeTexture(resolution, "GroundDecalRectContour");
            var px = new Color32[resolution * resolution];

            int band = Mathf.Max(1, Mathf.RoundToInt(resolution * thickness * 0.5f));

            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, resolution - 1 - x),
                                     Mathf.Min(y, resolution - 1 - y));
                byte a = (byte)(edge < band ? 255 : 0);
                px[y * resolution + x] = new Color32(255, 255, 255, a);
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            _shapes[key] = tex;
            return tex;
        }

        // shape + colour -> a texture carrying that colour. Cached: bounded by
        // shapes times the handful of faction colours.
        static readonly Dictionary<(Texture2D, uint), Texture2D> _tinted = new();

        /// <summary>A copy of a white shape mask painted in <paramref name="tint"/>.</summary>
        static Texture2D Tinted(Texture2D shape, Color tint)
        {
            uint key = Key(tint);
            if (_tinted.TryGetValue((shape, key), out var cached) && cached != null) return cached;

            var src = shape.GetPixels32();
            var dst = new Color32[src.Length];
            var c = (Color32)tint;

            for (int i = 0; i < src.Length; i++)
            {
                // The shape carries the silhouette in ALPHA; RGB is flat white.
                byte a = (byte)(src[i].a * c.a / 255);
                dst[i] = new Color32(c.r, c.g, c.b, a);
            }

            var tex = NewShapeTexture(shape.width, shape.name + "_Tinted");
            tex.Reinitialize(shape.width, shape.height);
            tex.SetPixels32(dst);
            tex.Apply(false, false);

            _tinted[(shape, key)] = tex;
            return tex;
        }

        static uint Key(Color c)
        {
            var b = (Color32)c;
            return (uint)b.r << 24 | (uint)b.g << 16 | (uint)b.b << 8 | b.a;
        }

        static Texture2D NewShapeTexture(int resolution, string name) =>
            new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

        #endregion

        #region Plumbing

        static void EnsureRoot()
        {
            if (_root != null) return;

            var go = new GameObject("GroundDecals");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        static DecalProjector Create()
        {
            var go = new GameObject("GroundDecal");
            go.transform.SetParent(_root, false);

            var p = go.AddComponent<DecalProjector>();
            p.renderingLayerMask = TerrainDecalLayerBit;
            p.pivot = Vector3.zero;
            return p;
        }

        static bool EnsureSource()
        {
            if (_source != null) return true;
            _source = Resources.Load<Material>(SourceMaterial);
            if (_source == null)
            {
                Debug.LogError($"[GroundDecals] Resources/{SourceMaterial}.mat is missing — "
                             + "ground decals will not render.");
                return false;
            }
            return true;
        }

        static Material MaterialFor(Texture2D shape, Color tint)
        {
            uint key = Key(tint);

            if (_materials.TryGetValue((shape, key), out var m) && m != null) return m;

            if (!EnsureSource()) return null;

            // THE COLOUR GOES IN THE TEXTURE, not in a material tint. The decal
            // Shader Graph exposes Base_Map and Normal_Map and NO colour input —
            // the _BaseColor/_Color rows on the material are inert leftovers, so
            // setting them tinted nothing and every decal drew white. (The old
            // InfluenceOverlayRenderer painted its colours straight into the
            // texture, which is why it looked right.)
            var tinted = Tinted(shape, tint);

            m = new Material(_source) { name = $"GroundDecal_{shape.name}" };
            m.SetTexture("Base_Map", tinted);
            m.SetTexture("_BaseMap", tinted);

            _materials[(shape, key)] = m;
            return m;
        }

        /// <summary>
        /// Opt the terrain into the decal layer. Idempotent, and repeated when
        /// the terrain is swapped — nothing receives these decals otherwise.
        /// </summary>
        static void OptInTerrain()
        {
            if (_terrainVersion == TerrainUtility.TerrainVersion) return;

            var terrain = TerrainUtility.GetActiveTerrain();
            if (terrain == null) return;

            terrain.renderingLayerMask |= TerrainDecalLayerBit;
            _terrainVersion = TerrainUtility.TerrainVersion;
        }

        #endregion
    }
}
