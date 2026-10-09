// MusterMapOverlay.cs
// The moving layer of the Muster Rolls map: resource nodes, buildings (real
// footprints, level pips, sites translucent), units as class symbols
// interpolated between recorded samples, and death rings fading out. One uGUI
// mesh in content-local METRES (the map content is laid out 1 unit = 1 m,
// centred on the world bounds), rebuilt only when the playhead or the zoom
// moves.
//
// Unit symbols follow the Muster Rolls page (tools/twb-report-page.html):
// melee circle, ranged triangle, cavalry diamond, siege square, worker ring,
// hero star, curse star. A fighting unit is backed in red.

using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Diagnostics.MatchRecording;

namespace TheWaningBorder.UI.Ingame
{
    internal sealed class MusterMapOverlay : MaskableGraphic
    {
        private const int VertexBudget = 60000;

        private MatchRecord _rec;
        private MusterRollsPanelConfig _cfg;
        private float _t;
        private float _pxPerMetre = 1f;
        private float _zoom = 1f;
        private Vector2 _centre;

        /// <summary>Units / buildings alive per faction at the last rebuild
        /// (index 8 = the curse), for the sidebar.</summary>
        public readonly int[] UnitCounts = new int[MatchRecord.Factions + 1];
        public readonly int[] BuildingCounts = new int[MatchRecord.Factions + 1];

        public void Bind(MatchRecord rec, MusterRollsPanelConfig cfg)
        {
            _rec = rec;
            _cfg = cfg;
            _centre = rec != null ? (rec.WorldMin + rec.WorldMax) * 0.5f : Vector2.zero;
            SetVerticesDirty();
        }

        /// <summary>Playhead and on-screen scale (canvas units per metre at
        /// the current zoom); dirties the mesh only when one moved.</summary>
        public void Show(float t, float pxPerMetre, float zoom)
        {
            if (Mathf.Approximately(t, _t) && Mathf.Approximately(pxPerMetre, _pxPerMetre)
                && Mathf.Approximately(zoom, _zoom)) return;
            _t = t; _pxPerMetre = Mathf.Max(1e-4f, pxPerMetre); _zoom = Mathf.Max(1f, zoom);
            SetVerticesDirty();
        }

        private Vector2 L(float x, float z) => new Vector2(x - _centre.x, z - _centre.y);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            System.Array.Clear(UnitCounts, 0, UnitCounts.Length);
            System.Array.Clear(BuildingCounts, 0, BuildingCounts.Length);
            if (_rec == null || _cfg == null) return;
            float t = _t;
            // Metres for a given on-screen size (canvas units).
            float m = 1f / _pxPerMetre;
            Color curse = _cfg.curseColor;

            // ── resource nodes ──
            var nodes = _rec.Nodes;
            float nodeR = Mathf.Max(2f, 3f * m);
            for (int i = 0; i < nodes.Count && vh.currentVertCount < VertexBudget; i++)
            {
                var n = nodes[i];
                Color c = n.Kind == "iron" ? new Color(0.62f, 0.62f, 0.68f, 0.9f)
                        : n.Kind == "veilstone" ? new Color(0.30f, 0.90f, 1f, 0.95f)
                        : new Color(0.80f, 0.68f, 0.38f, 0.85f);
                Poly(vh, L(n.X, n.Z), nodeR, 4, 45f, c);
            }

            // ── buildings ──
            var bs = _rec.Buildings;
            float minHalf = 2.5f * m;
            for (int i = 0; i < bs.Count && vh.currentVertCount < VertexBudget; i++)
            {
                var b = bs[i];
                if (!b.AliveAt(t)) continue;
                bool isCurse = b.Faction < 0 || b.Faction >= MatchRecord.Factions;
                int slot = isCurse ? MatchRecord.Factions : b.Faction;
                BuildingCounts[slot]++;
                Color c = isCurse ? curse : _rec.FactionColor[b.Faction];
                bool site = b.IsSiteAt(t);
                c.a = site ? 0.35f : 0.85f;
                var p = L(b.X, b.Z);
                float hw = Mathf.Max(minHalf, b.W * 0.5f), hh = Mathf.Max(minHalf, b.H * 0.5f);
                Rect(vh, p, hw + m, hh + m, b.Yaw, new Color(0f, 0f, 0f, site ? 0.25f : 0.6f));
                Rect(vh, p, hw, hh, b.Yaw, c);
                int lv = b.LevelAt(t);
                if (lv > 0 && !site)
                {
                    float pip = 2.2f * m, gap = 5.5f * m;
                    float startX = p.x - (lv - 1) * gap * 0.5f;
                    float y = p.y + Mathf.Max(hw, hh) + 5f * m;
                    for (int k = 0; k < Mathf.Min(lv, 6); k++)
                        Poly(vh, new Vector2(startX + k * gap, y), pip, 4, 45f, GameUIKit.Gold);
                }
            }

            // ── units ──
            var us = _rec.Units;
            float r0 = _cfg.unitSymbolSize * 0.5f * Mathf.Pow(_zoom, 0.35f) * m;
            var shade = new Color(0f, 0f, 0f, 0.65f);
            var fight = new Color(1f, 0.32f, 0.26f, 0.95f);
            for (int i = 0; i < us.Count; i++)
            {
                var u = us[i];
                if (!u.AliveAt(t)) continue;
                if (!u.TryGetAt(t, out var pos, out byte state)) continue;
                bool isCurse = (u.Kind & UnitTrack.KindCurse) != 0
                               || u.Faction < 0 || u.Faction >= MatchRecord.Factions;
                UnitCounts[isCurse ? MatchRecord.Factions : u.Faction]++;
                if (vh.currentVertCount >= VertexBudget) continue;

                Color c = isCurse ? curse : _rec.FactionColor[u.Faction];
                c.a = 1f;
                var p = L(pos.x, pos.y);
                var back = (state & UnitTrack.StateFighting) != 0 ? fight : shade;
                bool hero = (u.Kind & UnitTrack.KindHero) != 0;
                if (isCurse || hero)
                {
                    float r = r0 * (hero ? 1.5f : 1.15f);
                    Star(vh, p, r * 1.3f, back);
                    Star(vh, p, r, c);
                    continue;
                }
                var cls = (UnitClass)Mathf.Clamp(u.Class, 0, 7);
                if (u.Class < 0) cls = UnitClass.Melee;
                if (cls == UnitClass.Worker || cls == UnitClass.Economy)
                {
                    Ring(vh, p, r0 * 0.55f, r0 * 0.95f, back, r0 * 0.25f);
                    Ring(vh, p, r0 * 0.55f, r0 * 0.85f, c, 0f);
                }
                else if (cls == UnitClass.Siege)
                {
                    Rect(vh, p, r0 * 1.0f, r0 * 1.0f, 0f, back);
                    Rect(vh, p, r0 * 0.72f, r0 * 0.72f, 0f, c);
                }
                else if ((u.Kind & UnitTrack.KindCavalry) != 0)
                {
                    Poly(vh, p, r0 * 1.3f, 4, 0f, back);
                    Poly(vh, p, r0 * 0.95f, 4, 0f, c);
                }
                else if (cls == UnitClass.Ranged || cls == UnitClass.Magic)
                {
                    Poly(vh, p, r0 * 1.3f, 3, 0f, back);
                    Poly(vh, p, r0 * 0.95f, 3, 0f, c);
                }
                else
                {
                    float s = cls == UnitClass.Scout ? 0.75f : 1f;
                    Poly(vh, p, r0 * 1.0f * s, 10, 0f, back);
                    Poly(vh, p, r0 * 0.72f * s, 10, 0f, c);
                }
            }

            // ── deaths: rings that grow and fade ──
            float fade = Mathf.Max(0.5f, _cfg.deathFadeSeconds);
            var ds = _rec.Deaths;
            int first = _rec.FirstDeathFrom(t - fade);
            for (int i = first; i < ds.Count && vh.currentVertCount < VertexBudget; i++)
            {
                var d = ds[i];
                if (d.T > t) break;
                float age = (t - d.T) / fade;
                Color c = d.Faction >= 0 && d.Faction < MatchRecord.Factions ? _rec.FactionColor[d.Faction] : curse;
                c.a = 0.85f * (1f - age);
                float r = (4f + 14f * age) * m;
                Ring(vh, L(d.X, d.Z), r, r + 2f * m, c, 0f);
            }
        }

        // ── primitives ────────────────────────────────────────────────

        private static void Poly(VertexHelper vh, Vector2 c, float r, int sides, float rotDeg, Color32 col)
        {
            int i0 = vh.currentVertCount;
            vh.AddVert(c, col, Vector4.zero);
            float rot = (rotDeg + 90f) * Mathf.Deg2Rad;
            for (int k = 0; k < sides; k++)
            {
                float a = rot + k * Mathf.PI * 2f / sides;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col, Vector4.zero);
            }
            for (int k = 0; k < sides; k++)
                vh.AddTriangle(i0, i0 + 1 + k, i0 + 1 + (k + 1) % sides);
        }

        private static void Star(VertexHelper vh, Vector2 c, float r, Color32 col)
        {
            int i0 = vh.currentVertCount;
            vh.AddVert(c, col, Vector4.zero);
            for (int k = 0; k < 10; k++)
            {
                float a = Mathf.PI * 0.5f + k * Mathf.PI / 5f;
                float rr = (k & 1) == 0 ? r : r * 0.45f;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, col, Vector4.zero);
            }
            for (int k = 0; k < 10; k++)
                vh.AddTriangle(i0, i0 + 1 + k, i0 + 1 + (k + 1) % 10);
        }

        private static void Ring(VertexHelper vh, Vector2 c, float r0, float r1, Color32 col, float grow)
        {
            const int seg = 14;
            r0 = Mathf.Max(0f, r0 - grow);
            r1 += grow;
            int i0 = vh.currentVertCount;
            for (int k = 0; k < seg; k++)
            {
                float a = k * Mathf.PI * 2f / seg;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(c + d * r0, col, Vector4.zero);
                vh.AddVert(c + d * r1, col, Vector4.zero);
            }
            for (int k = 0; k < seg; k++)
            {
                int a = i0 + k * 2, b = i0 + ((k + 1) % seg) * 2;
                vh.AddTriangle(a, a + 1, b + 1);
                vh.AddTriangle(a, b + 1, b);
            }
        }

        /// <summary>A rectangle of half-size (hw, hh) rotated by yaw degrees
        /// about +Y (0 = facing +Z, clockwise seen from above).</summary>
        private static void Rect(VertexHelper vh, Vector2 c, float hw, float hh, float yawDeg, Color32 col)
        {
            float a = -yawDeg * Mathf.Deg2Rad;
            var ax = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * hw;
            var az = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * hh;
            int i0 = vh.currentVertCount;
            vh.AddVert(c - ax - az, col, Vector4.zero);
            vh.AddVert(c - ax + az, col, Vector4.zero);
            vh.AddVert(c + ax + az, col, Vector4.zero);
            vh.AddVert(c + ax - az, col, Vector4.zero);
            vh.AddTriangle(i0, i0 + 1, i0 + 2);
            vh.AddTriangle(i0, i0 + 2, i0 + 3);
        }
    }
}
