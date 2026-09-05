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
            // No view faction = unfogged (mirrors FogVisibilitySyncSystem).
            if (!GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null)
            {
                for (int i = 0; i < _overlayPixels.Length; i++)
                    _overlayPixels[i] = ClearPixel;
                return;
            }

            // Sample points sit every FogSampleStride pixels, plus one
            // column/row past the far edge so upsampling never extrapolates.
            int gw = _ovW / FogSampleStride + 2;
            int gh = _ovH / FogSampleStride + 2;
            if (_fogGrid == null || _fogGrid.Length != gw * gh)
            {
                _fogGrid = new float[gw * gh];
                _fogGridSmooth = new float[gw * gh];
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

            // 3x3 box blur, edge-clamped.
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
            for (int y = 0; y < _ovH; y++)
            {
                float gyF = y * inv;
                int gy0 = (int)gyF;
                float fy = gyF - gy0;
                int rowA = gy0 * gw;
                int rowB = Mathf.Min(gy0 + 1, gh - 1) * gw;
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
                    _overlayPixels[y * _ovW + x] = a > 0.004f
                        ? new Color32(0, 0, 0, (byte)(a * 255f))
                        : ClearPixel;
                }
            }
        }
    }
}
