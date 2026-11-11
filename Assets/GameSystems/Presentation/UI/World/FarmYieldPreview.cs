// FarmYieldPreview.cs
// THE FARM'S FIELD WHILE PLACING (docs/Design/Age_1_Alanthor.md § The Farm,
// Unification decision 57). While a building ghost is being placed:
//
//   * placing a FARM: "Yield NN%" by the cursor — the free share of the crop
//     ring it would have there, and what it would pay full — and its ring
//     drawn on the ground;
//   * placing ANYTHING whose footprint (or, for a Farm, whose ring) would cut
//     into one of the player's own Farm rings: one line per such Farm,
//     "Farm 84% → 71%", and that Farm's ring drawn on the ground.
//
// Every percentage is FarmRing.FreePermilleAt — the function the simulation
// pays with — asked with the ghost as a would-be building, so the preview
// shows exactly what the Farm will be paid. Recomputed only when the snapped
// position or the building changes (and twice a second while it stands
// still, for a world that moves under it), never per frame.
//
// Driven by WorkerCommandPanel (BuildCommandPannel.cs): Show every placement
// frame, Hide when placement ends. Presentation only: reads the world,
// writes nothing.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Common;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.World
{
    public sealed class FarmYieldPreview : MonoBehaviour
    {
        // Presentation constants (as BuildFootprintOutline's): refresh pace,
        // ring drawing and the hint's place by the cursor.
        private const float RefreshSeconds = 0.5f;
        private const int RingSegments = 72;
        private const float RingGroundOffset = 0.15f;
        private const float RingWidth = 0.14f;
        private const int MaxRings = 8;

        private static readonly Color GoodTint = new Color(0.45f, 0.95f, 0.45f, 0.95f);
        private static readonly Color FairTint = new Color(1f, 0.80f, 0.25f, 0.95f);
        private static readonly Color PoorTint = new Color(1f, 0.38f, 0.30f, 0.95f);

        private static readonly ComponentType[] QT_Farms =
        {
            ComponentType.ReadOnly<FarmTag>(),
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Farms;

        private static FarmYieldPreview _instance;

        private struct Affected
        {
            public float2 Centre;
            public int Now;
            public int After;
        }

        private bool _active;
        private string _buildId;
        private float2 _pos;
        private float _nextRefresh;
        private bool _placingFarm;
        private int _ghostPermille = -1;
        private int _ghostFullPay;
        private readonly List<Affected> _affected = new List<Affected>(6);
        private readonly List<string> _lines = new List<string>(8);
        private readonly List<Color> _lineColors = new List<Color>(8);

        private LineRenderer[] _rings = System.Array.Empty<LineRenderer>();
        private Material _ringMat;
        private GUIStyle _hint;

        // ── API ──────────────────────────────────────────────────────

        /// <summary>
        /// Show the field preview for <paramref name="buildingId"/> placed at
        /// <paramref name="snappedPos"/> by <paramref name="faction"/>. Call
        /// every placement frame; cheap when nothing changed.
        /// </summary>
        public static void Show(EntityManager em, float3 snappedPos, string buildingId, Faction faction)
        {
            if (string.IsNullOrEmpty(buildingId)) { Hide(); return; }
            Ensure();
            _instance.Refresh(em, snappedPos.xz, buildingId, faction);
        }

        /// <summary>Hide the preview. Cheap to call every frame.</summary>
        public static void Hide()
        {
            if (_instance == null || !_instance._active) return;
            _instance._active = false;
            _instance._buildId = null;
            _instance._lines.Clear();
            _instance.HideRings(0);
        }

        // ── Computing ────────────────────────────────────────────────

        private void Refresh(EntityManager em, float2 pos, string buildingId, Faction faction)
        {
            bool moved = !_active || buildingId != _buildId || math.any(pos != _pos);
            if (!moved && Time.unscaledTime < _nextRefresh) { DrawRings(); return; }
            _active = true;
            _buildId = buildingId;
            _pos = pos;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            _placingFarm = buildingId == Farm.BuildingId;
            var ghost = FarmRing.Ghost.Of(buildingId, pos);

            _ghostPermille = -1;
            _ghostFullPay = 0;
            if (_placingFarm)
            {
                _ghostPermille = FarmRing.FreePermilleAt(em, pos, Entity.Null, FarmRing.Ghost.None);
                int slots = TechCatalog.Building(Farm.BuildingId).garrisonSlots;
                _ghostFullPay = Farm.SuppliesPerMinute(slots, Farm.PerWorkerPerMinute(em, faction), _ghostPermille);
            }

            // The player's own Farms this ghost would cut into.
            _affected.Clear();
            var q = QC_Farms.Get(em, QT_Farms);
            if (!q.IsEmptyIgnoreFilter)
            {
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length && _affected.Count < MaxRings - 1; i++)
                {
                    if (facs[i].Value != faction) continue;
                    float2 c = xfs[i].Position.xz;
                    if (!Reaches(ghost, c)) continue;
                    int now = FarmRing.FreePermilleAt(em, c, ents[i], FarmRing.Ghost.None);
                    int after = FarmRing.FreePermilleAt(em, c, ents[i], ghost);
                    if (after == now) continue;
                    _affected.Add(new Affected { Centre = c, Now = now, After = after });
                }
            }

            _lines.Clear();
            _lineColors.Clear();
            if (_placingFarm)
            {
                _lines.Add(string.Format(Loc.T("Yield {0}% ({1} supplies/min with every Worker)"),
                    _ghostPermille / 10, _ghostFullPay));
                _lineColors.Add(TintFor(_ghostPermille));
            }
            for (int i = 0; i < _affected.Count; i++)
            {
                _lines.Add(string.Format(Loc.T("Farm {0}% → {1}%"), _affected[i].Now / 10, _affected[i].After / 10));
                _lineColors.Add(TintFor(_affected[i].After));
            }
            DrawRings();
        }

        /// <summary>Can this ghost change the ring of the Farm at
        /// <paramref name="farm"/>? A cheap test before the real count.</summary>
        private static bool Reaches(in FarmRing.Ghost ghost, float2 farm)
        {
            float r = FarmRing.OuterRadius;
            if (ghost.IsFarm) return math.distancesq(ghost.Centre, farm) < (2f * r) * (2f * r);
            float2 half = new float2(ghost.SizeMeters.x, ghost.SizeMeters.y) * 0.5f;
            float2 mn = ghost.Centre - half, mx = ghost.Centre + half;
            float2 d = farm - math.clamp(farm, mn, mx);
            return math.lengthsq(d) < r * r;
        }

        private static Color TintFor(int permille)
            => permille >= 750 ? GoodTint : permille >= 500 ? FairTint : PoorTint;

        // ── Drawing ──────────────────────────────────────────────────

        private void DrawRings()
        {
            int used = 0;
            float r = FarmRing.OuterRadius;
            if (_placingFarm && _ghostPermille >= 0)
                DrawRing(used++, _pos, r, TintFor(_ghostPermille));
            for (int i = 0; i < _affected.Count && used < MaxRings; i++)
                DrawRing(used++, _affected[i].Centre, r, TintFor(_affected[i].After));
            HideRings(used);
        }

        private void DrawRing(int index, float2 centre, float radius, Color c)
        {
            EnsureRings(index + 1);
            var lr = _rings[index];
            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            lr.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * (math.PI * 2f / RingSegments);
                float x = centre.x + math.cos(a) * radius;
                float z = centre.y + math.sin(a) * radius;
                lr.SetPosition(i, new Vector3(x, TerrainUtility.GetHeight(x, z) + RingGroundOffset, z));
            }
            lr.startColor = c;
            lr.endColor = c;
            var m = lr.material;
            if (m != null)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            }
        }

        private void HideRings(int from)
        {
            for (int i = from; i < _rings.Length; i++)
                if (_rings[i] != null && _rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(false);
        }

        private void EnsureRings(int needed)
        {
            if (_rings.Length >= needed) return;
            if (_ringMat == null)
            {
                _ringMat = new Material(ProceduralMaterialHelper.LitShader) { name = "FarmYieldPreview_Ring" };
                PlacementOverlayMaterial.MakeTransparent(_ringMat);
            }
            var grown = new LineRenderer[needed];
            System.Array.Copy(_rings, grown, _rings.Length);
            for (int i = _rings.Length; i < needed; i++)
            {
                var go = new GameObject("FarmRing" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = new Material(_ringMat);
                lr.loop = true;
                lr.startWidth = RingWidth;
                lr.endWidth = RingWidth;
                lr.useWorldSpace = true;
                lr.numCornerVertices = 0;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                go.SetActive(false);
                grown[i] = lr;
            }
            _rings = grown;
        }

        private void OnGUI()
        {
            if (!_active || _lines.Count == 0) return;
            if (_hint == null)
                _hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };

            Vector2 m = UnityEngine.Input.mousePosition;
            // Below the cursor, clear of the refusal / targeting hints that sit beside it.
            float x = m.x + 18f, y = Screen.height - m.y + 30f;
            for (int i = 0; i < _lines.Count; i++)
            {
                var r = new Rect(x, y + i * 20f, 420f, 22f);
                _hint.normal.textColor = WorldOverlayPalette.InlayShadow;
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), _lines[i], _hint);
                _hint.normal.textColor = _lineColors[i];
                GUI.Label(r, _lines[i], _hint);
            }
        }

        // ── Plumbing ─────────────────────────────────────────────────

        private static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("FarmYieldPreview");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FarmYieldPreview>();
        }
    }
}
