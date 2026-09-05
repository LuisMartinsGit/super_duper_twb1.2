// MinimapPanelBinder.Blips.cs
// Entity blips and pings drawn into the overlay, plus the disc
// rasteriser they share.

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
        /// <summary>Flashing event diamonds (MinimapPings) on top of every
        /// other layer — damage red, curse purple, power gold.</summary>
        private void DrawPings()
        {
            var pings = MinimapPings.Live();
            if (pings.Count == 0) return;
            bool flashOn = (Time.time * 4f) % 2f < 1.2f; // fast blink, longer on-phase
            if (!flashOn) return;

            float invBw = 1f / Mathf.Max(0.001f, _boundsMax.x - _boundsMin.x);
            float invBh = 1f / Mathf.Max(0.001f, _boundsMax.y - _boundsMin.y);
            for (int i = 0; i < pings.Count; i++)
            {
                var p = pings[i];
                int cx = Mathf.RoundToInt((p.Pos.x - _boundsMin.x) * invBw * _ovW);
                int cy = Mathf.RoundToInt((p.Pos.z - _boundsMin.y) * invBh * _ovH);
                int r = p.Big ? 7 : 4;
                for (int dy = -r; dy <= r; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= _ovH) continue;
                    int half = r - Mathf.Abs(dy); // diamond
                    for (int dx = -half; dx <= half; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= _ovW) continue;
                        _overlayPixels[py * _ovW + px] = p.Color;
                    }
                }
            }
        }

        private void DrawBlips(EntityManager em, Faction faction)
        {
            // No view faction (and FoW-off matches) = every blip drawn.
            bool unfogged = !GameSettings.FogOfWarEnabled || GameSettings.ViewFaction == null;

            // Units: own always, others only while FoW-visible.
            var unitsQ = _unitsQ.Get(em, UnitQueryTypes);
            using (var facs = unitsQ.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = unitsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < facs.Length; i++)
                {
                    var pos = xfs[i].Position;
                    bool mine = facs[i].Value == faction;
                    if (!mine && !unfogged
                        && !FogOfWarSystem.IsVisibleToFaction(faction, pos)) continue;
                    DrawDisc(WorldToOverlayPixel(pos), UnitRadiusPx, FactionColors.Get(facs[i].Value));
                }
            }

            // Buildings: own always, others visible = solid, revealed = ghost
            // (pre-darkened — the overlay replaces pixels, it doesn't blend).
            var buildingsQ = _buildingsQ.Get(em, BuildingQueryTypes);
            using (var facs = buildingsQ.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = buildingsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < facs.Length; i++)
                {
                    var pos = xfs[i].Position;
                    bool mine = facs[i].Value == faction;
                    bool vis = mine || unfogged || FogOfWarSystem.IsVisibleToFaction(faction, pos);
                    if (!vis && !FogOfWarSystem.IsRevealedToFaction(faction, pos)) continue;

                    Color baseCol = FactionColors.Get(facs[i].Value);
                    Color32 c = vis ? (Color32)baseCol
                                    : (Color32)Color.Lerp(Color.black, baseCol, 0.55f);
                    DrawDisc(WorldToOverlayPixel(pos), BuildingRadiusPx, c);
                }
            }

            // Rocks and resource nodes — static landmarks, but only once the
            // ground is EXPLORED (unexplored minimap is solid black; drawing
            // them fog-ignorant would leak the map layout). Resource nodes
            // also carry ObstacleTag + PresentationId (navmesh carving) and
            // are skipped in the rocks pass so the dedicated passes below
            // keep their colors.
            var obstaclesQ = _obstaclesQ.Get(em, ObstacleQueryTypes);
            using (var ents = obstaclesQ.ToEntityArray(Allocator.Temp))
            using (var xfs = obstaclesQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.HasComponent<IronMineTag>(ents[i])
                        || em.HasComponent<VeilstoneOutcroppingTag>(ents[i])
                        || em.HasComponent<VeilsteelDepositTag>(ents[i])) continue;
                    if (!unfogged
                        && !FogOfWarSystem.IsRevealedToFaction(faction, xfs[i].Position)) continue;
                    DrawDisc(WorldToOverlayPixel(xfs[i].Position), 2, RockBlip);
                }
            }

            Faction? gate = unfogged ? (Faction?)null : faction;
            DrawSimpleBlips(_ironQ.Get(em, IronQueryTypes), 2, IronBlip, gate);
            DrawSimpleBlips(_veilstoneQ.Get(em, VeilstoneQueryTypes), 2, VeilstoneBlip, gate);
            DrawSimpleBlips(_veilsteelQ.Get(em, VeilsteelQueryTypes), 2, VeilsteelBlip, gate);

            // Ritual markers — visible to all players regardless of fog (the
            // spec is explicit that rituals are universally locatable); the
            // colors match RitualBeamSystem's beam tints.
            var ritualsQ = _ritualsQ.Get(em, RitualQueryTypes);
            using (var actives = ritualsQ.ToComponentDataArray<ActiveRitualOnNode>(Allocator.Temp))
            using (var xfs = ritualsQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < actives.Length; i++)
                {
                    Color32 c = actives[i].Kind switch
                    {
                        RitualKind.Conversion        => RitualConversionBlip,
                        RitualKind.ViolentExtraction => RitualExtractionBlip,
                        _                            => RitualDefaultBlip,
                    };
                    DrawDisc(WorldToOverlayPixel(xfs[i].Position), 4, c);
                }
            }

            // Glow pickups — gold, also fog-ignorant by spec.
            DrawSimpleBlips(_shardrootQ.Get(em, ShardrootQueryTypes), 3, ShardrootBlip);
        }

        /// <summary>Draw a disc per query entity. Pass <paramref name="revealGate"/>
        /// to draw only on ground that faction has explored; null = fog-ignorant
        /// (rituals, Glow — universally locatable by spec).</summary>
        private void DrawSimpleBlips(EntityQuery query, int radius, Color32 color,
            Faction? revealGate = null)
        {
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                if (revealGate.HasValue
                    && !FogOfWarSystem.IsRevealedToFaction(revealGate.Value, xfs[i].Position))
                    continue;
                DrawDisc(WorldToOverlayPixel(xfs[i].Position), radius, color);
            }
        }

        private int2 WorldToOverlayPixel(float3 pos)
        {
            float u = Mathf.InverseLerp(_boundsMin.x, _boundsMax.x, pos.x);
            float v = Mathf.InverseLerp(_boundsMin.y, _boundsMax.y, pos.z);
            return new int2(
                Mathf.Clamp(Mathf.FloorToInt(u * _ovW), 0, _ovW - 1),
                Mathf.Clamp(Mathf.FloorToInt(v * _ovH), 0, _ovH - 1));
        }

        private void DrawDisc(int2 center, int r, Color32 col)
        {
            int r2 = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int yy = center.y + dy;
                if (yy < 0 || yy >= _ovH) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = center.x + dx;
                    if (xx < 0 || xx >= _ovW) continue;
                    if (dx * dx + dy * dy <= r2)
                        _overlayPixels[yy * _ovW + xx] = col;
                }
            }
        }
    }
}
