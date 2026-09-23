// MinimapPanelBinder.Fog.cs
// Fog-of-war dimming of the overlay.

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
        // The fog LAYER, kept between refreshes (2026-09-16). The coarse
        // sample grid is re-read every refresh (cheap: a few thousand
        // queries), but the blur + the 65k-pixel bilinear upsample only run
        // when a sample actually changed — with nothing moving, the layer
        // is a memcpy. _pixelRevealed is the per-pixel "explored" answer the
        // territory composite reads, taken from the nearest fog sample: it
        // replaces one FogOfWarSystem.IsRevealedToFaction call per pixel per
        // pass, and it cannot disagree with the fog that is drawn.
        private Color32[] _fogPixels;
        private float[] _fogGridPrev;
        private byte[] _pixelRevealed;
        private bool _fogLayerValid;

        /// <summary>True when fog is off for this view — every pixel counts
        /// as explored and _pixelRevealed is not consulted.</summary>
        private bool _unfogged;

        /// <summary>
        /// Fog pass over the whole overlay: hidden ground solid black,
        /// revealed ground half-dimmed, visible ground clear. The 3-state
        /// fog is sampled on a coarse grid (one query per FogSampleStride
        /// pixels — fewer queries than the old per-2x2-block version), the
        /// grid is 3x3 box-blurred to round the FoW grid's staircase
        /// corners, and the result is bilinearly upsampled to the overlay —
        /// state borders render as smooth ramps instead of square blocks.
        /// </summary>
        private void RefreshFog(Faction faction)
        {
            int n = _ovW * _ovH;
            if (_fogPixels == null || _fogPixels.Length != n)
            {
                _fogPixels = new Color32[n];
                _pixelRevealed = new byte[n];
                _fogLayerValid = false;
            }

            _unfogged = !GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null;
            if (_unfogged)
            {
                for (int i = 0; i < n; i++)
                    _overlayPixels[i] = ClearPixel;
                _fogLayerValid = false;   // fog switched back on later: rebuild
                return;
            }

            int gw = _ovW / FogSampleStride + 2;
            int gh = _ovH / FogSampleStride + 2;
            if (_fogGrid == null || _fogGrid.Length != gw * gh)
            {
                _fogGrid = new float[gw * gh];
                _fogGridSmooth = new float[gw * gh];
                _fogGridPrev = new float[gw * gh];
                _fogLayerValid = false;
            }

            for (int gy = 0; gy < gh; gy++)
            {
                float wz = Mathf.Lerp(_boundsMin.y, _boundsMax.y,
                    gy * FogSampleStride / (float)_ovH);
                for (int gx = 0; gx < gw; gx++)
                {
                    float wx = Mathf.Lerp(_boundsMin.x, _boundsMax.x,
                        gx * FogSampleStride / (float)_ovW);
                    var pos = new float3(wx, 0f, wz);
                    float a;
                    if (FogOfWarSystem.IsVisibleToFaction(faction, pos)) a = 0f;
                    else if (FogOfWarSystem.IsRevealedToFaction(faction, pos)) a = RevealedFogAlpha;
                    else a = HiddenFogAlpha;
                    _fogGrid[gy * gw + gx] = a;
                }
            }

            bool changed = !_fogLayerValid;
            if (!changed)
            {
                for (int i = 0; i < _fogGrid.Length; i++)
                    if (_fogGrid[i] != _fogGridPrev[i]) { changed = true; break; }
            }

            if (changed)
            {
                System.Array.Copy(_fogGrid, _fogGridPrev, _fogGrid.Length);
                _fogLayerValid = true;

                for (int gy = 0; gy < gh; gy++)
                {
                    for (int gx = 0; gx < gw; gx++)
                    {
                        float sum = 0f;
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int sy = Mathf.Clamp(gy + oy, 0, gh - 1);
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int sx = Mathf.Clamp(gx + ox, 0, gw - 1);
                                sum += _fogGrid[sy * gw + sx];
                            }
                        }
                        _fogGridSmooth[gy * gw + gx] = sum / 9f;
                    }
                }

                float inv = 1f / FogSampleStride;
                int half = FogSampleStride / 2;
                for (int y = 0; y < _ovH; y++)
                {
                    float gyF = y * inv;
                    int gy0 = (int)gyF;
                    float fy = gyF - gy0;
                    int rowA = gy0 * gw;
                    int rowB = Mathf.Min(gy0 + 1, gh - 1) * gw;
                    int rowN = Mathf.Min((y + half) / FogSampleStride, gh - 1) * gw;
                    int row = y * _ovW;
                    for (int x = 0; x < _ovW; x++)
                    {
                        float gxF = x * inv;
                        int gx0 = (int)gxF;
                        float fx = gxF - gx0;
                        int gx1 = Mathf.Min(gx0 + 1, gw - 1);
                        float a = Mathf.Lerp(
                            Mathf.Lerp(_fogGridSmooth[rowA + gx0], _fogGridSmooth[rowA + gx1], fx),
                            Mathf.Lerp(_fogGridSmooth[rowB + gx0], _fogGridSmooth[rowB + gx1], fx),
                            fy);
                        _fogPixels[row + x] = a > 0.004f
                            ? new Color32(0, 0, 0, (byte)(a * 255f))
                            : ClearPixel;

                        // Explored = the nearest raw sample is not solid
                        // hidden fog. Nearest, not blurred: exploration is a
                        // fact, only the drawn ramp is cosmetic.
                        int gxN = Mathf.Min((x + half) / FogSampleStride, gw - 1);
                        _pixelRevealed[row + x] = _fogGrid[rowN + gxN] < HiddenFogAlpha ? (byte)1 : (byte)0;
                    }
                }
            }

            System.Array.Copy(_fogPixels, _overlayPixels, n);
        }
    }
}
