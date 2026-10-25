// EndgameMarkers.cs
// THE END OF THE HUNT (docs/Design/Territory_Claims.md § 7.4, 2026-10-20).
// A match ends only when every hostile faction is eliminated, and the last
// stragglers — a worker behind a hill, a wall stub, a squad in a large
// territory — made the endgame a search. Once the local player has
// researched Field Intelligence (Fortress L3, every culture), every
// territory one of its soldiers stands in marks what still stands against
// it there:
//
//   ▼ soldiers   hostile military units — they hold the territory's meter
//   ◆ structures hostile buildings and construction sites (walls included)
//                and workers — what keeps a faction alive
//
// Only on ground the player has EXPLORED (FogOfWarSystem.IsRevealedToFaction),
// in the owner's colour. A marker on screen sits over its target; one off
// screen is an arrow on the screen edge pointing at it.
//
// A BLOCKED CLAIM SHOWS WHAT BLOCKS IT (Territory_Claims.md § 3.1,
// 2026-10-25): every structure the simulation has revealed to the local
// player (or a team-mate) because it locks a territory that player is taking
// — FogReveal's lock timer, granted by TerritoryClaimSystem — is marked with
// a diamond as well, with no research needed and in the blocker's colour
// (the curse's included).
//
// Presentation only: reads the world, writes nothing. The target list is
// rebuilt twice a second; positions are read every frame, so moving units
// are tracked.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.World.Regions;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    public sealed class EndgameMarkers : MonoBehaviour
    {
        /// <summary>The research that turns the markers on.</summary>
        public const string TechId = "FieldIntelligence";

        private const float ScanInterval = 0.5f;
        /// <summary>Marker size at 1080p; scaled with the screen height.</summary>
        private const float MarkerSize = 26f;
        private const float ReferenceHeight = 1080f;
        /// <summary>Distance an off-screen arrow keeps from the screen edge.</summary>
        private const float EdgeMargin = 36f;
        /// <summary>Metres above the target's feet a marker floats.</summary>
        private const float UnitLift = 3.2f;
        private const float BuildingLift = 6f;
        /// <summary>Hard cap on markers drawn — a deathball needs no 200 arrows.</summary>
        private const int MaxMarkers = 96;
        /// <summary>Over the claim bars (45), under floating health bars (50).</summary>
        private const int CanvasSortingOrder = 46;

        private struct Target
        {
            public Entity Entity;
            public bool Structure;
            public Color Color;
        }

        private readonly List<Target> _targets = new List<Target>();
        private readonly List<Image> _pool = new List<Image>();
        private readonly HashSet<int> _myTerritories = new HashSet<int>();
        private readonly HashSet<Entity> _blockers = new HashSet<Entity>();
        private RectTransform _canvasRoot;
        private Sprite _triangle, _diamond;
        private float _next;

        private static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Units;

        private static readonly ComponentType[] QT_Buildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Buildings;

        private static readonly ComponentType[] QT_Revealed =
        {
            ComponentType.ReadOnly<FogReveal>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Revealed;

        private void Awake()
        {
            var go = new GameObject("EndgameMarkers-Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            _canvasRoot = (RectTransform)go.transform;
            _triangle = MakeSprite(diamond: false);
            _diamond = MakeSprite(diamond: true);
        }

        private void LateUpdate()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _canvasRoot == null) { HideFrom(0); return; }
            var em = world.EntityManager;

            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + ScanInterval;
                Scan(em);
            }
            Place(em);
        }

        // ── What to mark ────────────────────────────────────────────────

        private void Scan(EntityManager em)
        {
            _targets.Clear();
            _myTerritories.Clear();
            _blockers.Clear();
            if (GameSettings.IsSpectating || !RegionMap.Ready) return;
            var me = GameSettings.LocalPlayerFaction;
            AddClaimBlockers(em, me);
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research == null || !research.HasResearched(me, TechId)) return;

            var units = QC_Units.Get(em, QT_Units);
            using var unitEnts = units.ToEntityArray(Allocator.Temp);
            using var unitFacs = units.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitXfs = units.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Where my soldiers stand.
            for (int i = 0; i < unitEnts.Length; i++)
            {
                if (unitFacs[i].Value != me) continue;
                if (!TheWaningBorder.Systems.World.TerritoryClaimSystem.CountsForClaim(em, unitEnts[i])) continue;
                int t = RegionMap.RegionAt(unitXfs[i].Position.x, unitXfs[i].Position.z);
                if (t != RegionMap.None) _myTerritories.Add(t);
            }
            if (_myTerritories.Count == 0) return;

            // Hostile soldiers (claim blockers) and workers (lifelines) there.
            for (int i = 0; i < unitEnts.Length && _targets.Count < MaxMarkers; i++)
            {
                var f = unitFacs[i].Value;
                if (!IsEnemy(me, f)) continue;
                var e = unitEnts[i];
                bool worker = em.HasComponent<CanBuild>(e);
                if (!worker && !TheWaningBorder.Systems.World.TerritoryClaimSystem.CountsForClaim(em, e)) continue;
                if (worker && em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                if (!Inside(me, unitXfs[i].Position)) continue;
                _targets.Add(new Target { Entity = e, Structure = worker, Color = FactionColors.Get(f) });
            }

            // Hostile buildings and construction sites, walls included.
            var blds = QC_Buildings.Get(em, QT_Buildings);
            using var bEnts = blds.ToEntityArray(Allocator.Temp);
            using var bFacs = blds.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bXfs = blds.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < bEnts.Length && _targets.Count < MaxMarkers; i++)
            {
                var f = bFacs[i].Value;
                if (!IsEnemy(me, f)) continue;
                var e = bEnts[i];
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                // A wall segment is the graph edge, not a piece — its cells
                // and hubs are marked instead.
                if (em.HasComponent<WallSegmentTag>(e)) continue;
                if (_blockers.Contains(e)) continue;   // already marked as a claim blocker
                if (!Inside(me, bXfs[i].Position)) continue;
                _targets.Add(new Target { Entity = e, Structure = true, Color = FactionColors.Get(f) });
            }
        }

        /// <summary>Territory_Claims.md § 3.1: the structures blocking a claim of
        /// mine or a team-mate's, as the simulation revealed them.</summary>
        private void AddClaimBlockers(EntityManager em, Faction me)
        {
            var q = QC_Revealed.Get(em, QT_Revealed);
            if (q.IsEmpty) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var reveals = q.ToComponentDataArray<FogReveal>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length && _targets.Count < MaxMarkers; i++)
            {
                bool forUs = false;
                for (int f = 0; f < FogReveal.Players && !forUs; f++)
                    forUs = reveals[i].LockRevealedTo(f) && ((Faction)f == me || Alliances.AreAllied(me, (Faction)f));
                if (!forUs) continue;
                var e = ents[i];
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                _blockers.Add(e);
                _targets.Add(new Target { Entity = e, Structure = true, Color = FactionColors.Get(facs[i].Value) });
            }
        }

        /// <summary>A hostile PLAYER — the curse keeps no faction alive and
        /// weighs on no meter.</summary>
        private static bool IsEnemy(Faction me, Faction f)
            => f != me && f != Faction.Border && Alliances.AreHostile(me, f);

        /// <summary>In a territory my soldiers stand in, on ground I have explored.</summary>
        private bool Inside(Faction me, float3 p)
        {
            int t = RegionMap.RegionAt(p.x, p.z);
            if (t == RegionMap.None || !_myTerritories.Contains(t)) return false;
            return TheWaningBorder.Systems.Visibility.FogOfWarSystem.IsRevealedToFaction(me, p);
        }

        // ── Drawing ─────────────────────────────────────────────────────

        private void Place(EntityManager em)
        {
            var cam = PresentationState.GameplayCamera != null ? PresentationState.GameplayCamera : Camera.main;
            if (cam == null || _targets.Count == 0) { HideFrom(0); return; }

            float scale = Screen.height / ReferenceHeight;
            float size = MarkerSize * scale;
            float margin = EdgeMargin * scale;
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            int used = 0;

            for (int i = 0; i < _targets.Count; i++)
            {
                var tg = _targets[i];
                if (!em.Exists(tg.Entity) || !em.HasComponent<LocalTransform>(tg.Entity)) continue;
                Vector3 pos = em.GetComponentData<LocalTransform>(tg.Entity).Position;
                pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z)
                        + (tg.Structure ? BuildingLift : UnitLift);

                Vector3 sp = cam.WorldToScreenPoint(pos);
                bool behind = sp.z < 0f;
                if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
                bool onScreen = !behind && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;

                var img = Marker(used++);
                var rt = img.rectTransform;
                rt.sizeDelta = new Vector2(size, size);
                img.color = tg.Color;
                if (onScreen)
                {
                    img.sprite = tg.Structure ? _diamond : _triangle;
                    rt.localEulerAngles = Vector3.zero;
                    rt.position = new Vector3(sp.x, sp.y, 0f);
                }
                else
                {
                    // Off screen: an arrow on the edge, pointing at the target.
                    Vector2 dir = new Vector2(sp.x, sp.y) - centre;
                    if (dir.sqrMagnitude < 1f) dir = Vector2.down;
                    float halfW = centre.x - margin, halfH = centre.y - margin;
                    float k = Mathf.Min(halfW / Mathf.Max(Mathf.Abs(dir.x), 0.001f),
                                        halfH / Mathf.Max(Mathf.Abs(dir.y), 0.001f));
                    Vector2 edge = centre + dir * k;
                    img.sprite = _triangle;
                    // The triangle points DOWN; rotate it to point along dir.
                    rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);
                    rt.position = new Vector3(edge.x, edge.y, 0f);
                }
            }
            HideFrom(used);
        }

        private Image Marker(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("EndgameMarker", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_canvasRoot, false);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                _pool.Add(img);
            }
            var m = _pool[index];
            if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
            return m;
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        /// <summary>A white, dark-outlined marker shape drawn once: a downward
        /// triangle (soldiers, edge arrows) or a diamond (structures, workers).
        /// Tinted per target with Image.color.</summary>
        private static Sprite MakeSprite(bool diamond)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    // Signed distance to the shape (negative inside), in uv units.
                    float d = diamond
                        ? Mathf.Abs(u) + Mathf.Abs(v) - 0.92f
                        : Mathf.Max(v - 0.85f, Mathf.Abs(u) * 0.95f - (v + 0.85f) * 0.55f);
                    byte a = (byte)(Mathf.Clamp01(0.5f - d * N * 0.5f) * 255f);
                    byte c = d < -0.16f ? (byte)255 : (byte)40;   // white body, dark rim
                    px[y * N + x] = new Color32(c, c, c, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
