// MinimapPanelBinder.Elevation.cs
// The one-time terrain -> minimap image bake: hillshaded hypsometric
// ramp, cliffs at the walkability limit, timesliced so map load
// never hitches.

using System.Collections;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class MinimapPanelBinder
    {
        private IEnumerator BuildElevationSprite(Vector2 worldMin, Vector2 worldMax)
        {
            _boundsMin = worldMin;
            _boundsMax = worldMax;
            float sizeX = worldMax.x - worldMin.x;
            float sizeZ = worldMax.y - worldMin.y;
            int texW, texH;
            if (sizeX >= sizeZ)
            {
                texW = MaxTextureSize;
                texH = Mathf.Max(8, Mathf.RoundToInt(MaxTextureSize * sizeZ / sizeX));
            }
            else
            {
                texH = MaxTextureSize;
                texW = Mathf.Max(8, Mathf.RoundToInt(MaxTextureSize * sizeX / sizeZ));
            }

            // Pass 1: world-space height per pixel (NaN = outside every
            // tile), one GetHeights grab per terrain tile.
            var heights = new float[texW * texH];
            for (int i = 0; i < heights.Length; i++) heights[i] = float.NaN;

            float minH = float.MaxValue, maxH = float.MinValue;
            var tiles = UnityEngine.Terrain.activeTerrains;
            foreach (var tile in tiles)
            {
                if (tile == null || tile.terrainData == null) continue;
                var data = tile.terrainData;
                Vector3 tPos = tile.transform.position;
                Vector3 tSize = data.size;
                int res = data.heightmapResolution;
                float[,] hm = data.GetHeights(0, 0, res, res); // [z, x], normalized 0..1

                int px0 = Mathf.Clamp(Mathf.CeilToInt((tPos.x - worldMin.x) / sizeX * texW - 0.5f), 0, texW - 1);
                int px1 = Mathf.Clamp(Mathf.FloorToInt((tPos.x + tSize.x - worldMin.x) / sizeX * texW - 0.5f), 0, texW - 1);
                int py0 = Mathf.Clamp(Mathf.CeilToInt((tPos.z - worldMin.y) / sizeZ * texH - 0.5f), 0, texH - 1);
                int py1 = Mathf.Clamp(Mathf.FloorToInt((tPos.z + tSize.z - worldMin.y) / sizeZ * texH - 0.5f), 0, texH - 1);

                for (int py = py0; py <= py1; py++)
                {
                    float wz = worldMin.y + (py + 0.5f) / texH * sizeZ;
                    float v = Mathf.Clamp01((wz - tPos.z) / tSize.z) * (res - 1);
                    int z0 = Mathf.Min((int)v, res - 2);
                    float fz = v - z0;

                    for (int px = px0; px <= px1; px++)
                    {
                        float wx = worldMin.x + (px + 0.5f) / texW * sizeX;
                        float u = Mathf.Clamp01((wx - tPos.x) / tSize.x) * (res - 1);
                        int x0 = Mathf.Min((int)u, res - 2);
                        float fx = u - x0;

                        float h = Mathf.Lerp(
                            Mathf.Lerp(hm[z0, x0], hm[z0, x0 + 1], fx),
                            Mathf.Lerp(hm[z0 + 1, x0], hm[z0 + 1, x0 + 1], fx),
                            fz) * tSize.y + tPos.y;

                        heights[py * texW + px] = h;
                        if (h < minH) minH = h;
                        if (h > maxH) maxH = h;
                    }

                    if ((py & (RowsPerFrame - 1)) == RowsPerFrame - 1) yield return null;
                }
            }

            if (minH >= maxH)
            {
                // Perfectly flat map — still show it, mid-ramp.
                minH = maxH - 1f;
            }

            // Pass 2: colorize. Height picks the ramp stop and the base
            // alpha; the finite-difference gradient drives both the NW
            // hillshade and the incline mark. The slope formula matches
            // PassabilityGrid's 4-point gradient, so ground drawn as cliff
            // is ground units cannot climb.
            float stepX = sizeX / texW;
            float stepZ = sizeZ / texH;
            var pixels = new Color32[texW * texH];
            var clear = new Color32(0, 0, 0, 0);

            for (int py = 0; py < texH; py++)
            {
                for (int px = 0; px < texW; px++)
                {
                    int i = py * texW + px;
                    float h = heights[i];
                    if (float.IsNaN(h)) { pixels[i] = clear; continue; }

                    float hl = SampleOr(heights, texW, texH, px - 1, py, h);
                    float hr = SampleOr(heights, texW, texH, px + 1, py, h);
                    float hd = SampleOr(heights, texW, texH, px, py - 1, h);
                    float hu = SampleOr(heights, texW, texH, px, py + 1, h);
                    float dx = (hr - hl) / (2f * stepX);
                    float dz = (hu - hd) / (2f * stepZ);
                    var normal = new Vector3(-dx, 1f, -dz).normalized;
                    float shade = Mathf.Lerp(0.6f, 1.05f,
                        Mathf.Clamp01(Vector3.Dot(normal, LightDir)));

                    float t = (h - minH) / (maxH - minH);
                    Color c = (t < 0.5f
                        ? Color.Lerp(LowColor, MidColor, t * 2f)
                        : Color.Lerp(MidColor, HighColor, (t - 0.5f) * 2f)) * shade;
                    float a = Mathf.Lerp(LowAlpha, HighAlpha, t);

                    // Steepness: darken toward rock as the incline grows,
                    // then blend hard to the cliff color across the last 15%
                    // below the walkability limit (soft edge, no aliasing).
                    float slope = Mathf.Sqrt(dx * dx + dz * dz);
                    float steep = Mathf.Clamp01(slope / PassabilityGrid.MaxWalkableSlope);
                    c = Color.Lerp(c, CliffColor, steep * 0.35f);
                    float cliff = Mathf.InverseLerp(
                        0.85f * PassabilityGrid.MaxWalkableSlope,
                        PassabilityGrid.MaxWalkableSlope, slope);
                    c = Color.Lerp(c, CliffColor, cliff);
                    a = Mathf.Lerp(a, CliffAlpha, cliff);

                    pixels[i] = new Color32(
                        (byte)(Mathf.Clamp01(c.r) * 255f),
                        (byte)(Mathf.Clamp01(c.g) * 255f),
                        (byte)(Mathf.Clamp01(c.b) * 255f),
                        (byte)(a * 255f));
                }
                if ((py & (RowsPerFrame - 1)) == RowsPerFrame - 1) yield return null;
            }

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                name = "MinimapElevation",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            _mapImage.sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                new Vector2(0.5f, 0.5f), 100f);
            _mapImage.color = Color.white;

            // Layout must have settled before the overlay can be aspect-fit
            // to the Map rect (it has, after the terrain wait, but guard).
            while (_mapImage.rectTransform.rect.width < 1f)
                yield return null;
            CreateOverlayLayers(texW, texH);
        }

        private static float SampleOr(float[] heights, int w, int h, int x, int y, float fallback)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return fallback;
            float v = heights[y * w + x];
            return float.IsNaN(v) ? fallback : v;
        }
    }
}
