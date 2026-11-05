// PresentationSpawnSystem.Monument.cs
// The monument's visual: an interim Synty statue (MonumentSystem.asset), the ring
// that marks where a unit conquers it, a brighter arc filling as it is taken, and
// a light. Ring and light wear the holder's colour.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;
using TheWaningBorder.World.Terrain;

public partial class PresentationSpawnSystem
{
    private GameObject CreateMonumentVisual(Vector3 center, Entity entity)
    {
        var cfg = MonumentSystemConfig.I;
        var root = new GameObject($"Monument_{entity.Index}");
        root.transform.position = center;

        if (cfg.statuePrefab != null)
        {
            var statue = Object.Instantiate(cfg.statuePrefab, root.transform);
            statue.transform.localPosition = Vector3.zero;
            statue.transform.localRotation = Quaternion.identity;
            statue.transform.localScale = Vector3.one * cfg.statueScale;
        }

        var lightGo = new GameObject("HolderLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * cfg.lightHeight;
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = cfg.lightRange;
        light.intensity = cfg.lightIntensity;
        light.color = cfg.neutralColor;

        var ring = MonumentRing.Make(root.transform, "Ring", cfg.ringWidth);
        var arc = MonumentRing.Make(root.transform, "CaptureArc", cfg.ringWidth * cfg.arcWidthScale);
        MonumentRing.Trace(ring, center, cfg.captureRadius, cfg.ringSegments, 1f);
        arc.positionCount = 0;

        root.AddComponent<MonumentHolderLight>().Bind(entity, light, ring, arc, center);
        return root;
    }
}

namespace TheWaningBorder.Rendering
{
    /// <summary>Draws a circle that follows the ground.</summary>
    internal static class MonumentRing
    {
        public static LineRenderer Make(Transform parent, string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.material = new Material(ProceduralMaterialHelper.BaseLitEmissive) { name = "MonumentRing" };
            return lr;
        }

        public static void Trace(LineRenderer lr, Vector3 center, float radius, int segments, float fill)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Clamp01(fill)) + 1);
            lr.positionCount = count;
            float lift = MonumentSystemConfig.I.ringLift;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                float x = center.x + Mathf.Sin(a) * radius;
                float z = center.z + Mathf.Cos(a) * radius;
                lr.SetPosition(i, new Vector3(x, TerrainUtility.GetHeight(x, z) + lift, z));
            }
        }

        public static void Paint(LineRenderer lr, Color color, float glow)
        {
            var m = lr.material;
            m.SetColor("_BaseColor", color);
            m.SetColor("_EmissionColor", color * glow);
            m.EnableKeyword("_EMISSION");
        }
    }

    /// <summary>Keeps the monument's ring and light in its holder's colour, and
    /// fills the capture arc.</summary>
    public sealed class MonumentHolderLight : MonoBehaviour
    {
        private const float Interval = 0.25f;
        private Entity _entity;
        private Light _light;
        private LineRenderer _ring, _arc;
        private Vector3 _center;
        private float _next;
        private int _shownHolder = -1, _shownCapturer = -1, _shownSeconds = -1;

        internal void Bind(Entity entity, Light light, LineRenderer ring, LineRenderer arc, Vector3 center)
        {
            _entity = entity;
            _light = light;
            _ring = ring;
            _arc = arc;
            _center = center;
        }

        private static Color ColorOf(Faction f)
            => (int)f >= 8 ? MonumentSystemConfig.I.neutralColor : FactionColors.Get(f);

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _ring == null) return;
            var em = world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<MonumentState>(_entity)) return;
            var s = em.GetComponentData<MonumentState>(_entity);
            var cfg = MonumentSystemConfig.I;

            if ((int)s.Holder != _shownHolder)
            {
                _shownHolder = (int)s.Holder;
                var c = ColorOf(s.Holder);
                _light.color = c;
                MonumentRing.Paint(_ring, c, cfg.ringGlow);
            }
            if ((int)s.Capturer != _shownCapturer || s.CaptureSeconds != _shownSeconds)
            {
                _shownCapturer = (int)s.Capturer;
                _shownSeconds = s.CaptureSeconds;
                if (s.CaptureSeconds <= 0 || (int)s.Capturer >= 8)
                {
                    _arc.positionCount = 0;
                }
                else
                {
                    MonumentRing.Paint(_arc, ColorOf(s.Capturer), cfg.ringGlow * cfg.arcGlowScale);
                    MonumentRing.Trace(_arc, _center, cfg.captureRadius, cfg.ringSegments,
                        (float)s.CaptureSeconds / cfg.captureSeconds);
                }
            }
        }
    }
}
