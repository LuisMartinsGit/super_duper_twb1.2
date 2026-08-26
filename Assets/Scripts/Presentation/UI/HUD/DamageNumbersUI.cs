// Floating combat numbers above entities:
//   * damage taken by ENEMY units  -> white number
//   * damage taken by OWN units    -> red number
//   * healing (any faction)        -> green number with a leading "+"
//
// Presentation-only: watches Health deltas each frame (no combat-system
// hooks, so every damage/heal source is covered — melee, projectiles, AoE,
// spells, regen). Deltas are accumulated per entity over a short window and
// emitted as ONE popup, so DoT ticks and per-frame regen read as a single
// number instead of confetti. Renderer-only state — nothing is written to
// the ECS world, so multiplayer determinism is unaffected.
//
// Same UGUI-pool-on-private-canvas pattern as FloatingHealthBars (sorting
// order 50: above the 3D scene, below the CEF web HUD at 100).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Systems.Visibility;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    [DefaultExecutionOrder(911)]
    public class DamageNumbersUI : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Deltas accumulate per entity for this long, then emit one popup.")]
        [SerializeField] private float accumulateWindow = 0.1f;
        [SerializeField] private float popupLifetime = 0.9f;

        [Header("Appearance")]
        [SerializeField] private int fontSize = 14;
        [SerializeField] private float riseWorldUnits = 1.4f;
        [SerializeField] private float yOffsetAboveEntity = 2.1f;
        [SerializeField] private float buildingYOffset = 3.6f;
        // FloatingHealthBars' canvas sits at 50 and the CEF web HUD at 100 —
        // 60 draws damage numbers ON TOP of the health bars, under the HUD.
        [SerializeField] private int canvasSortingOrder = 60;

        private static readonly Color DamageOwn   = new Color(1f, 0.25f, 0.2f, 1f);   // red
        private static readonly Color DamageEnemy = Color.white;
        private static readonly Color HealColor   = new Color(0.35f, 1f, 0.4f, 1f);   // green

        private const int MaxActivePopups = 64;

        private EntityWorld _world;
        private EntityManager _em;
        private Camera _cachedCamera;

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] HealthQueryTypes =
        {
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _healthQuery;

        // Last observed health per entity. New entities get a silent baseline.
        private readonly Dictionary<Entity, (int value, int max)> _lastHealth = new();

        // Per-entity accumulation window.
        private struct Pending
        {
            public int Damage;          // sum of health lost in the window
            public int Heal;            // sum of health gained in the window
            public float WindowEnd;
            public Vector3 LastPos;     // survives the entity dying before emit
            public float YOffset;
            public bool IsOwn;          // local player's unit?
        }
        private readonly Dictionary<Entity, Pending> _pending = new();
        private readonly List<Entity> _scratchKeys = new();

        private struct Popup
        {
            public Entity Follow;       // keep tracking the entity while it lives
            public Vector3 WorldPos;
            public float YOffset;
            public string Text;
            public Color Color;
            public float SpawnTime;
        }
        private readonly List<Popup> _popups = new();

        // UGUI text pool.
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private readonly List<Text> _pool = new();
        private Font _font;

        private int _pruneFrameCounter;
        private const int PruneEveryNFrames = 300; // ~5s at 60fps

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;
            EnsureCanvas();
        }

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("DamageNumbers-Canvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = canvasSortingOrder;
            _canvasRect = (RectTransform)go.transform;
            go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world != null && _world.IsCreated) _em = _world.EntityManager;
            }
            if (_em.Equals(default(EntityManager))) { HideAll(); return; }

            var cam = _cachedCamera != null ? _cachedCamera : (_cachedCamera = Camera.main);

            SampleHealthDeltas();
            EmitExpiredWindows();
            if (cam != null) DrawPopups(cam); else HideAll();

            if (++_pruneFrameCounter >= PruneEveryNFrames)
            {
                _pruneFrameCounter = 0;
                PruneStale();
            }
        }

        /// <summary>Diff every Health component against the last frame.</summary>
        private void SampleHealthDeltas()
        {
            float now = Time.time;
            Faction local = GameSettings.LocalPlayerFaction;

            var healthQuery = _healthQuery.Get(_em, HealthQueryTypes);
            using var entities = healthQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var healths = healthQuery.ToComponentDataArray<Health>(Unity.Collections.Allocator.Temp);
            using var xforms = healthQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                int value = healths[i].Value;
                int max = healths[i].Max;

                if (!_lastHealth.TryGetValue(e, out var last))
                {
                    // First sighting — baseline silently (spawning isn't damage).
                    _lastHealth[e] = (value, max);
                    continue;
                }
                _lastHealth[e] = (value, max);

                // Max changed (construction ramp, upgrades, tier research):
                // re-baseline without a popup — that's not combat healing.
                if (max != last.max) continue;

                int delta = value - last.value;
                if (delta == 0) continue;

                // Buildings under construction gain HP every tick as they
                // build — not healing.
                if (delta > 0 && _em.HasComponent<UnderConstruction>(e)) continue;

                var pos = xforms[i].Position;

                // Only surface numbers the local player can actually see
                // (observers see everything).
                if (!GameSettings.IsObserver
                    && !FogOfWarSystem.IsVisibleToFaction(local, pos)) continue;

                bool isOwn = _em.HasComponent<FactionTag>(e)
                    && _em.GetComponentData<FactionTag>(e).Value == local;
                float yOff = _em.HasComponent<BuildingTag>(e) ? buildingYOffset : yOffsetAboveEntity;

                if (!_pending.TryGetValue(e, out var p))
                {
                    p = new Pending { WindowEnd = now + accumulateWindow };
                }
                if (delta < 0) p.Damage += -delta;
                else p.Heal += delta;
                p.LastPos = new Vector3(pos.x, pos.y, pos.z);
                p.YOffset = yOff;
                p.IsOwn = isOwn;
                _pending[e] = p;
            }
        }

        /// <summary>Turn finished accumulation windows into popups.</summary>
        private void EmitExpiredWindows()
        {
            if (_pending.Count == 0) return;
            float now = Time.time;

            _scratchKeys.Clear();
            foreach (var kvp in _pending)
                if (now >= kvp.Value.WindowEnd) _scratchKeys.Add(kvp.Key);

            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                var e = _scratchKeys[i];
                var p = _pending[e];
                _pending.Remove(e);

                if (p.Damage > 0)
                {
                    SpawnPopup(e, p.LastPos, p.YOffset,
                        p.Damage.ToString(),
                        p.IsOwn ? DamageOwn : DamageEnemy);
                    // Red minimap ping wherever the LOCAL player is taking
                    // damage (2026-08-04). Near-duplicates merge in the
                    // registry, so a battle reads as one hot spot.
                    if (p.IsOwn)
                        TheWaningBorder.UI.GameUI.MinimapPings.Post(
                            p.LastPos, TheWaningBorder.UI.GameUI.MinimapPings.Damage, 2.5f);
                }
                if (p.Heal > 0)
                {
                    // Healing stacks slightly above the damage number when both
                    // land in the same window.
                    SpawnPopup(e, p.LastPos, p.YOffset + (p.Damage > 0 ? 0.5f : 0f),
                        "+" + p.Heal,
                        HealColor);
                }
            }
        }

        private void SpawnPopup(Entity follow, Vector3 worldPos, float yOff, string text, Color color)
        {
            if (_popups.Count >= MaxActivePopups) _popups.RemoveAt(0);
            _popups.Add(new Popup
            {
                Follow = follow,
                WorldPos = worldPos,
                YOffset = yOff,
                Text = text,
                Color = color,
                SpawnTime = Time.time,
            });
        }

        private void DrawPopups(Camera cam)
        {
            float now = Time.time;
            int used = 0;

            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                float age = now - p.SpawnTime;
                if (age >= popupLifetime) { _popups.RemoveAt(i); continue; }

                // Follow the entity while it lives; keep the last position after death.
                if (p.Follow != Entity.Null && _em.Exists(p.Follow)
                    && _em.HasComponent<LocalTransform>(p.Follow))
                {
                    var lp = _em.GetComponentData<LocalTransform>(p.Follow).Position;
                    p.WorldPos = new Vector3(lp.x, lp.y, lp.z);
                    _popups[i] = p;
                }

                float t = age / popupLifetime;
                Vector3 world = p.WorldPos + new Vector3(0f, p.YOffset + riseWorldUnits * t, 0f);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z < 0f) continue;

                var label = GetOrAllocate(used++);
                label.text = p.Text;
                // Fade out over the last 40% of the lifetime.
                float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                var c = p.Color; c.a = alpha;
                label.color = c;
                var rect = (RectTransform)label.transform;
                rect.anchoredPosition = new Vector2(screen.x, screen.y);
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }

            for (int i = used; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        private Text GetOrAllocate(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("DmgNum", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
                go.transform.SetParent(_canvasRect, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(120f, 30f);

                var text = go.GetComponent<Text>();
                text.font = _font;
                text.fontSize = fontSize;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;

                var outline = go.GetComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
                outline.effectDistance = new Vector2(1f, -1f);

                _pool.Add(text);
            }
            return _pool[index];
        }

        private void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        /// <summary>Drop baseline entries for entities that no longer exist.</summary>
        private void PruneStale()
        {
            if (_lastHealth.Count == 0) return;
            List<Entity> stale = null;
            foreach (var kvp in _lastHealth)
                if (!_em.Exists(kvp.Key)) (stale ??= new List<Entity>()).Add(kvp.Key);
            if (stale != null)
                for (int i = 0; i < stale.Count; i++)
                {
                    _lastHealth.Remove(stale[i]);
                    _pending.Remove(stale[i]);
                }
        }
    }
}
