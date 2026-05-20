// ProceduralTextures.cs
//
// Runtime-generated diffuse textures for the splat layers. Each layer
// stacks four noise channels (macro / meso / micro / speckle) plus a
// per-LayerKind palette of 4-5 colours so the result reads as a real
// surface rather than a uniform tinted Perlin blur.
//
// Pipeline per pixel:
//   1. Sample 4 Fbm channels at different frequencies + seeds. The
//      per-octave rotation inside NoiseUtils.Fbm keeps the lattice from
//      reading as a grid at any zoom.
//   2. Run the layer-specific recipe (e.g. Grass picks dry/lush macro
//      patches, adds tuft highlights, darkens with micro grain, sparse
//      twig specks).
//   3. Optional anisotropic ripples (Sand) or Worley cracks (Rock).
//
// Cost: 7 layers × 512² = ~1.8 M pixels, ~15-25 ms at scene load. Done
// once during ProceduralMapGen.

using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public enum LayerKind
    {
        SeaFloor,
        Sand,        // beach
        Grass,
        Forest,
        Dirt,        // hill / talus
        Rock,        // cliff face
        Snow,        // mountain cap
    }

    public static class ProceduralTextures
    {
        // Default resolution per layer. 512 reads well at default camera
        // height; bump to 1024 if the camera lingers in close-up.
        public const int DefaultSize = 512;

        public static Texture2D BuildLayer(LayerKind kind, int seed, int size = DefaultSize)
        {
            // RGB24 (no alpha) — URP terrain reads the diffuse alpha as a
            // smoothness source, and a full-alpha procedural fill made every
            // layer read as wet glass. Without alpha the shader falls back
            // to the layer's explicit `smoothness` field.
            var tex = new Texture2D(size, size, TextureFormat.RGB24, mipChain: true, linear: false)
            {
                name = $"Proc_{kind}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };

            var pixels = new Color32[size * size];
            float inv = 1f / size;

            for (int y = 0; y < size; y++)
            {
                float v = y * inv;
                for (int x = 0; x < size; x++)
                {
                    float u = x * inv;
                    Color c = SampleLayer(kind, u, v, seed);
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true);
            return tex;
        }

        // ── Per-pixel layer dispatcher ──────────────────────────────────────

        static Color SampleLayer(LayerKind kind, float u, float v, int seed)
        {
            // Four noise channels at coarse → fine frequencies. All in
            // [0, 1]. The seed XOR ensures each channel reads a different
            // lattice so they don't correlate.
            float ma = Sample01(u, v, 4f,   4, seed ^ 0x01);
            float me = Sample01(u, v, 18f,  4, seed ^ 0x02);
            float mi = Sample01(u, v, 70f,  3, seed ^ 0x03);
            float sp = Sample01(u, v, 220f, 2, seed ^ 0x04);

            return kind switch
            {
                LayerKind.Grass    => GrassColor   (u, v, ma, me, mi, sp, seed),
                LayerKind.Dirt     => DirtColor    (u, v, ma, me, mi, sp, seed),
                LayerKind.Sand     => SandColor    (u, v, ma, me, mi, sp, seed),
                LayerKind.Rock     => RockColor    (u, v, ma, me, mi, sp, seed),
                LayerKind.Snow     => SnowColor    (u, v, ma, me, mi, sp, seed),
                LayerKind.Forest   => ForestColor  (u, v, ma, me, mi, sp, seed),
                LayerKind.SeaFloor => SeaFloorColor(u, v, ma, me, mi, sp, seed),
                _                  => new Color(0.5f, 0.5f, 0.5f),
            };
        }

        static float Sample01(float u, float v, float freq, int octaves, int seed)
        {
            float n = NoiseUtils.Fbm(u * freq, v * freq, octaves, 2f, 0.5f, 1f, seed);
            return 0.5f + 0.5f * n;
        }

        // ── Grass ───────────────────────────────────────────────────────────
        // Lush meadow with dry tuft patches and rare bare-earth showings.
        // Macro picks lush/dry, meso adds sunlit blades, micro adds grain,
        // sparse speckles read as scattered seeds/twigs.
        static Color GrassColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var shadow = new Color(0.16f, 0.24f, 0.10f); // deep shaded grass
            var bladeM = new Color(0.27f, 0.40f, 0.16f); // average blade
            var bladeL = new Color(0.46f, 0.58f, 0.22f); // sunlit tip
            var dryTuf = new Color(0.55f, 0.48f, 0.18f); // sun-bleached tuft
            var bareDt = new Color(0.36f, 0.28f, 0.16f); // bare earth patch

            // Macro: dry-to-lush gradient.
            Color c = Color.Lerp(bladeM, shadow, Mathf.Pow(ma, 1.4f) * 0.6f);

            // Meso: tuft highlights — squared so most of the surface stays
            // mid-tone and only a few bright spots pop.
            c = Color.Lerp(c, bladeL, Mathf.Pow(me, 2.2f) * 0.55f);

            // Macro: dry tuft patches where macro is low.
            if (ma < 0.40f)
            {
                float t = (0.40f - ma) * 2.5f * Mathf.Pow(me, 1.5f);
                c = Color.Lerp(c, dryTuf, Mathf.Clamp01(t * 0.35f));
            }

            // Macro: rare bare-earth patches where both macro AND meso peak.
            if (ma > 0.78f && me > 0.62f)
            {
                float t = (ma - 0.78f) * 4.5f * (me - 0.62f) * 2.6f;
                c = Color.Lerp(c, bareDt, Mathf.Clamp01(t));
            }

            // Micro grain — small luminance jitter.
            float g = (mi - 0.5f) * 0.10f;
            c.r += g; c.g += g; c.b += g;

            // Speckles — sparse darker dots (twigs, fallen leaves).
            if (sp > 0.78f)
            {
                float s = (sp - 0.78f) * 4.5f;
                c.r *= 1f - 0.32f * s;
                c.g *= 1f - 0.30f * s;
                c.b *= 1f - 0.36f * s;
            }
            return c;
        }

        // ── Dirt ────────────────────────────────────────────────────────────
        // Loamy brown with damp-streak patches and small embedded pebbles.
        static Color DirtColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var damp = new Color(0.24f, 0.18f, 0.12f); // wet dark loam
            var midL = new Color(0.42f, 0.32f, 0.20f); // average loam
            var dryL = new Color(0.58f, 0.46f, 0.30f); // sun-dried ochre
            var clay = new Color(0.48f, 0.35f, 0.22f); // clay highlight
            var pbbl = new Color(0.55f, 0.52f, 0.48f); // pebble grey

            // Macro: damp vs dry distribution.
            Color c = Color.Lerp(midL, damp, Mathf.Pow(ma, 1.6f) * 0.7f);
            c = Color.Lerp(c, dryL, Mathf.Pow(1f - ma, 1.4f) * 0.45f);

            // Meso: clay seams.
            c = Color.Lerp(c, clay, Mathf.Pow(me, 1.8f) * 0.30f);

            // Micro grain.
            float g = (mi - 0.5f) * 0.14f;
            c.r += g; c.g += g; c.b += g * 0.7f;

            // Pebbles — high-freq speckle thresholded so we get sparse dots.
            if (sp > 0.82f)
            {
                float s = (sp - 0.82f) * 5.5f;
                c = Color.Lerp(c, pbbl, Mathf.Clamp01(s));
            }
            // Tiny dark organic specks at a different threshold.
            if (sp < 0.18f)
            {
                float s = (0.18f - sp) * 5.5f;
                c.r *= 1f - 0.35f * s; c.g *= 1f - 0.32f * s; c.b *= 1f - 0.38f * s;
            }
            return c;
        }

        // ── Sand ────────────────────────────────────────────────────────────
        // Pale beach sand with wind-ripple stripes, darker wet streaks near
        // the surf line, and scattered dark grains.
        static Color SandColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var deep   = new Color(0.62f, 0.54f, 0.36f); // wet sand
            var midS   = new Color(0.84f, 0.76f, 0.54f); // average dry sand
            var hi     = new Color(0.96f, 0.91f, 0.74f); // sun-bleached
            var grain  = new Color(0.28f, 0.22f, 0.16f); // dark mineral grain

            Color c = Color.Lerp(midS, deep, Mathf.Pow(ma, 1.7f) * 0.55f);
            c = Color.Lerp(c, hi, Mathf.Pow(me, 1.6f) * 0.45f);

            // Wind ripples — sine perpendicular to a low-freq noise direction.
            float rippleAngle = (NoiseUtils.Fbm(u * 1.5f, v * 1.5f, 2, 2f, 0.5f, 1f, seed ^ 0x05) + 1f) * Mathf.PI;
            float rx = u * Mathf.Cos(rippleAngle) - v * Mathf.Sin(rippleAngle);
            float ripple = 0.5f + 0.5f * Mathf.Sin(rx * 120f + (mi - 0.5f) * 6f);
            ripple = Mathf.Pow(ripple, 3f); // sharpen
            c.r *= 1f - 0.10f * ripple;
            c.g *= 1f - 0.10f * ripple;
            c.b *= 1f - 0.12f * ripple;

            // Dark grains — sparse pepper.
            if (sp > 0.88f)
            {
                float s = (sp - 0.88f) * 8f;
                c = Color.Lerp(c, grain, Mathf.Clamp01(s));
            }

            // Subtle warm tint variation.
            float warm = (mi - 0.5f) * 0.06f;
            c.r += warm; c.g += warm * 0.6f;
            return c;
        }

        // ── Rock ────────────────────────────────────────────────────────────
        // Layered grey-brown with crevice veins, lichen tints, and crack
        // lines via Worley.
        static Color RockColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var deepCr = new Color(0.20f, 0.18f, 0.17f); // crevice shadow
            var midGr  = new Color(0.40f, 0.38f, 0.35f); // average rock
            var hiGr   = new Color(0.62f, 0.58f, 0.52f); // hit-highlight
            var lichen = new Color(0.35f, 0.42f, 0.20f); // moss/lichen
            var rust   = new Color(0.45f, 0.30f, 0.20f); // iron oxide streak

            Color c = Color.Lerp(midGr, deepCr, Mathf.Pow(ma, 1.3f) * 0.55f);
            c = Color.Lerp(c, hiGr, Mathf.Pow(me, 2.0f) * 0.40f);

            // Iron-oxide streaks (low-freq band).
            if (ma > 0.60f && me < 0.45f)
            {
                float r = (ma - 0.60f) * 2.5f * (0.45f - me) * 2.2f;
                c = Color.Lerp(c, rust, Mathf.Clamp01(r * 0.55f));
            }

            // Lichen patches (low-freq, biased toward bright meso).
            if (ma < 0.35f && me > 0.55f)
            {
                float l = (0.35f - ma) * 2.8f * (me - 0.55f) * 2.2f;
                c = Color.Lerp(c, lichen, Mathf.Clamp01(l * 0.40f));
            }

            // Worley cracks.
            float edge = NoiseUtils.WorleyEdge(u, v, 0.10f, seed ^ 0x33);
            float crackMask = 1f - NoiseUtils.Smoothstep(0f, 0.035f, edge);
            c.r *= 1f - 0.45f * crackMask;
            c.g *= 1f - 0.45f * crackMask;
            c.b *= 1f - 0.45f * crackMask;

            // Micro grain.
            float gr = (mi - 0.5f) * 0.14f;
            c.r += gr; c.g += gr; c.b += gr;
            return c;
        }

        // ── Snow ────────────────────────────────────────────────────────────
        // White with subtle blue shadow in dips, faint pink tint where lit.
        static Color SnowColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var deepBl = new Color(0.78f, 0.84f, 0.94f); // shaded snow
            var midSn  = new Color(0.92f, 0.94f, 0.97f); // average snow
            var hiSn   = new Color(0.99f, 0.99f, 1.00f); // sunlit
            var dust   = new Color(0.62f, 0.66f, 0.70f); // wind-blown dust

            Color c = Color.Lerp(midSn, deepBl, Mathf.Pow(ma, 1.8f) * 0.6f);
            c = Color.Lerp(c, hiSn, Mathf.Pow(me, 2.4f) * 0.55f);

            // Micro sparkle — slightly raises the highlight on tiny spots.
            if (mi > 0.78f)
            {
                float s = (mi - 0.78f) * 4f;
                c.r += 0.04f * s; c.g += 0.04f * s; c.b += 0.05f * s;
            }

            // Dust grains — rare.
            if (sp > 0.94f)
            {
                float s = (sp - 0.94f) * 16f;
                c = Color.Lerp(c, dust, Mathf.Clamp01(s));
            }
            return c;
        }

        // ── Forest floor ────────────────────────────────────────────────────
        // Wet dark soil with mixed leaf litter, dry needles, moss, and tiny
        // twig specks. No Worley cracks (those bled visibly into adjacent
        // grass when Forest blends on top).
        static Color ForestColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var damp = new Color(0.15f, 0.10f, 0.05f); // wet soil
            var leaf = new Color(0.34f, 0.22f, 0.10f); // brown leaf
            var dry  = new Color(0.55f, 0.40f, 0.18f); // dry needle
            var moss = new Color(0.20f, 0.32f, 0.12f); // moss
            var twig = new Color(0.12f, 0.07f, 0.03f); // dark twig

            Color c = Color.Lerp(leaf, damp, Mathf.Pow(ma, 1.5f) * 0.6f);
            c = Color.Lerp(c, dry, Mathf.Pow(me, 2.0f) * 0.55f);

            // Moss patches where macro is low + meso mid.
            if (ma < 0.40f && me > 0.40f && me < 0.70f)
            {
                float t = (0.40f - ma) * 2.5f * 0.6f;
                c = Color.Lerp(c, moss, Mathf.Clamp01(t));
            }

            // Micro grain.
            float g = (mi - 0.5f) * 0.12f;
            c.r += g; c.g += g; c.b += g * 0.6f;

            // Twig specks.
            if (sp > 0.85f)
            {
                float s = (sp - 0.85f) * 6f;
                c = Color.Lerp(c, twig, Mathf.Clamp01(s));
            }
            return c;
        }

        // ── Sea floor ───────────────────────────────────────────────────────
        // Mottled silty bed with darker debris and lighter pebble patches.
        static Color SeaFloorColor(float u, float v, float ma, float me, float mi, float sp, int seed)
        {
            var deep = new Color(0.10f, 0.16f, 0.20f); // deep silt
            var mid  = new Color(0.22f, 0.28f, 0.30f); // average bed
            var sand = new Color(0.40f, 0.42f, 0.38f); // pale silt
            var alg  = new Color(0.08f, 0.18f, 0.12f); // algae patch

            Color c = Color.Lerp(mid, deep, Mathf.Pow(ma, 1.6f) * 0.7f);
            c = Color.Lerp(c, sand, Mathf.Pow(me, 2f) * 0.45f);

            // Algae patches.
            if (ma > 0.65f && me < 0.40f)
            {
                float t = (ma - 0.65f) * 2.8f * (0.40f - me) * 2.5f;
                c = Color.Lerp(c, alg, Mathf.Clamp01(t * 0.6f));
            }

            // Micro grain.
            float g = (mi - 0.5f) * 0.10f;
            c.r += g; c.g += g; c.b += g;

            // Sparse dark debris.
            if (sp > 0.85f)
            {
                float s = (sp - 0.85f) * 6f;
                c.r *= 1f - 0.35f * s; c.g *= 1f - 0.30f * s; c.b *= 1f - 0.30f * s;
            }
            return c;
        }

        // ── Normal map generation ───────────────────────────────────────────

        /// <summary>
        /// Build a normal map for the given layer. The normal is derived
        /// from a per-layer height field via central differences, then packed
        /// into RGB24 with the standard convention (R=Nx*0.5+0.5, G=Ny*0.5+0.5,
        /// B=Nz*0.5+0.5). URP terrain unpacks this automatically.
        /// </summary>
        public static Texture2D BuildNormalMap(LayerKind kind, int seed, int size = DefaultSize)
        {
            // linear:true marks the texture as non-sRGB so the shader doesn't
            // gamma-decode normal components when sampling.
            var tex = new Texture2D(size, size, TextureFormat.RGB24, mipChain: true, linear: true)
            {
                name = $"ProcN_{kind}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };

            var pixels = new Color32[size * size];
            float inv = 1f / size;
            // Sample step for central differences. One pixel in UV space.
            float dUV = inv;

            for (int y = 0; y < size; y++)
            {
                float v = y * inv;
                for (int x = 0; x < size; x++)
                {
                    float u = x * inv;
                    // Two-tap central differences (cheaper than 4-tap Sobel
                    // and visually indistinguishable on these noise fields).
                    float h0 = LayerHeight(kind, u, v, seed);
                    float hX = LayerHeight(kind, u + dUV, v, seed);
                    float hY = LayerHeight(kind, u, v + dUV, seed);
                    // Slope strength — bigger pulls normals further off the
                    // up axis, so the bump reads more strongly.
                    float strength = LayerHeightStrength(kind);
                    float dX = (hX - h0) * strength;
                    float dY = (hY - h0) * strength;
                    // Normal = normalize(-dX, -dY, 1). Standard tangent-space
                    // convention with Z pointing out of the surface.
                    float nx = -dX, ny = -dY, nz = 1f;
                    float inv2 = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx *= inv2; ny *= inv2; nz *= inv2;
                    byte r = (byte)Mathf.Clamp(Mathf.RoundToInt((nx * 0.5f + 0.5f) * 255f), 0, 255);
                    byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((ny * 0.5f + 0.5f) * 255f), 0, 255);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt((nz * 0.5f + 0.5f) * 255f), 0, 255);
                    pixels[y * size + x] = new Color32(r, g, b, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true);
            return tex;
        }

        // Per-layer height field, in [0, 1]. The recipes deliberately differ
        // so each surface bumps in a distinct way — pebbled rock, rippled
        // sand, soft snow, etc. The diffuse pass uses its own noise channels,
        // but height is intentionally correlated where it makes sense (rock
        // cracks darken AND indent, sand ripples darken AND ridge).
        static float LayerHeight(LayerKind kind, float u, float v, int seed)
        {
            switch (kind)
            {
                case LayerKind.Grass:
                {
                    float h = NoiseUtils.Fbm(u * 50f, v * 50f, 3, 2f, 0.5f, 1f, seed ^ 0x11);
                    return 0.5f + 0.5f * h;
                }
                case LayerKind.Dirt:
                {
                    float h = NoiseUtils.Fbm(u * 35f, v * 35f, 4, 2f, 0.5f, 1f, seed ^ 0x12);
                    // Pebble bumps via thresholded high-freq speckle.
                    float sp = NoiseUtils.Fbm(u * 220f, v * 220f, 2, 2f, 0.5f, 1f, seed ^ 0x04);
                    float pebble = sp > 0.6f ? (sp - 0.6f) * 1.2f : 0f;
                    return Mathf.Clamp01(0.5f + 0.5f * h + pebble * 0.4f);
                }
                case LayerKind.Sand:
                {
                    // Anisotropic ripples — same recipe as the diffuse so
                    // ridge highlights match the normal-map ripple direction.
                    float rippleAngle = (NoiseUtils.Fbm(u * 1.5f, v * 1.5f, 2, 2f, 0.5f, 1f, seed ^ 0x05) + 1f) * Mathf.PI;
                    float rx = u * Mathf.Cos(rippleAngle) - v * Mathf.Sin(rippleAngle);
                    float ripple = 0.5f + 0.5f * Mathf.Sin(rx * 120f);
                    float drift = NoiseUtils.Fbm(u * 8f, v * 8f, 3, 2f, 0.5f, 1f, seed ^ 0x13);
                    return Mathf.Clamp01(ripple * 0.7f + (0.5f + 0.5f * drift) * 0.3f);
                }
                case LayerKind.Rock:
                {
                    // Coarse bumps + Worley crack indentations. Cracks
                    // become deep V-grooves in the normal so directional
                    // light reads them as real seams.
                    float h = NoiseUtils.Fbm(u * 25f, v * 25f, 4, 2f, 0.5f, 1f, seed ^ 0x14);
                    float edge = NoiseUtils.WorleyEdge(u, v, 0.10f, seed ^ 0x33);
                    float crack = NoiseUtils.Smoothstep(0f, 0.04f, edge); // 0 in crack, 1 elsewhere
                    return Mathf.Clamp01((0.5f + 0.5f * h) * crack);
                }
                case LayerKind.Snow:
                {
                    // Very smooth, low-freq drifts only — snow is almost flat.
                    float h = NoiseUtils.Fbm(u * 8f, v * 8f, 3, 2f, 0.5f, 1f, seed ^ 0x15);
                    return 0.5f + 0.5f * h;
                }
                case LayerKind.Forest:
                {
                    // Leaf-litter clumps — irregular medium-freq mounds plus
                    // sparse "log/twig" bumps.
                    float h = NoiseUtils.Fbm(u * 40f, v * 40f, 4, 2f, 0.5f, 1f, seed ^ 0x16);
                    float sp = NoiseUtils.Fbm(u * 180f, v * 180f, 2, 2f, 0.5f, 1f, seed ^ 0x17);
                    float twig = sp > 0.7f ? (sp - 0.7f) * 1.5f : 0f;
                    return Mathf.Clamp01(0.5f + 0.5f * h + twig * 0.3f);
                }
                case LayerKind.SeaFloor:
                {
                    float h = NoiseUtils.Fbm(u * 12f, v * 12f, 3, 2f, 0.5f, 1f, seed ^ 0x18);
                    return 0.5f + 0.5f * h;
                }
                default:
                    return 0.5f;
            }
        }

        // Per-layer slope strength multiplier. Higher = more pronounced bumps
        // in directional light. Rock and Dirt get the strongest treatment;
        // Snow and Sea Floor stay near-flat.
        static float LayerHeightStrength(LayerKind kind) => kind switch
        {
            LayerKind.Grass    => 25f,
            LayerKind.Dirt     => 35f,
            LayerKind.Sand     => 20f,
            LayerKind.Rock     => 60f,
            LayerKind.Snow     => 12f,
            LayerKind.Forest   => 30f,
            LayerKind.SeaFloor => 14f,
            _                  => 20f,
        };

        /// <summary>
        /// Build a TerrainLayer for the given kind. Diffuse from BuildLayer,
        /// normal from BuildNormalMap. Tile size is 8 m so the texture
        /// repeats every 8 m of world space — large enough that the eye
        /// stops seeing the tile, small enough that per-pixel detail is
        /// visible at default camera height.
        /// </summary>
        public static TerrainLayer BuildTerrainLayer(LayerKind kind, int seed)
        {
            // Per-kind smoothness so the map reads as ground, not glass.
            // Snow stays soft (matte snow), Sand is dry, Rock has a touch of
            // sheen so the cliff cracks read in directional light.
            float smoothness, metallic, normalScale;
            switch (kind)
            {
                case LayerKind.SeaFloor: smoothness = 0.18f; metallic = 0f; normalScale = 0.5f; break;
                case LayerKind.Sand:     smoothness = 0.06f; metallic = 0f; normalScale = 0.7f; break;
                case LayerKind.Grass:    smoothness = 0.04f; metallic = 0f; normalScale = 0.8f; break;
                case LayerKind.Forest:   smoothness = 0.05f; metallic = 0f; normalScale = 1.0f; break;
                case LayerKind.Dirt:     smoothness = 0.04f; metallic = 0f; normalScale = 1.0f; break;
                case LayerKind.Rock:     smoothness = 0.12f; metallic = 0f; normalScale = 1.3f; break;
                case LayerKind.Snow:     smoothness = 0.20f; metallic = 0f; normalScale = 0.4f; break;
                default:                 smoothness = 0.05f; metallic = 0f; normalScale = 0.8f; break;
            }

            var layer = new TerrainLayer
            {
                name = $"Proc_{kind}",
                diffuseTexture = BuildLayer(kind, seed),
                normalMapTexture = BuildNormalMap(kind, seed),
                tileSize = new Vector2(8f, 8f),
                tileOffset = Vector2.zero,
                specular = Color.black,
                smoothness = smoothness,
                metallic = metallic,
                normalScale = normalScale,
            };
            return layer;
        }
    }
}
