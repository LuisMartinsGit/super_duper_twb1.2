// PresentationSpawnSystem.Monument.cs
// The monument's visual: an interim Synty statue (MonumentSystem.asset), a light,
// and the claim circle (docs/Design/Monuments_And_Petriarchy.md §1).
//
// The circle is a ground decal, the same workflow as the selection decals
// (GroundDecals), so it follows every slope. Its outer edge is always visible.
// Inside, alpha climbs linearly from fully transparent at the centre to fully
// visible at the edge, in the holder's colour; a capture fills in from the edge
// toward the centre, in the capturer's colour.
//
// The decal's driver is NOT a child of the statue: fog hides the statue's view,
// and every player must be able to watch a circle fill up.

using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;

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

        // Clicks find entities through a collider (ScreenPick).
        FitSelectionCollider(root, entity, _em, minWorldXZ: 1.5f, minWorldY: 1.5f);
        root.AddComponent<EntityReference>().Entity = entity;

        var driver = new GameObject($"MonumentCircle_{entity.Index}");
        driver.AddComponent<MonumentCircle>().Bind(entity, light, center);
        return root;
    }
}

namespace TheWaningBorder.Rendering
{
    /// <summary>Paints and follows one monument's claim circle.</summary>
    public sealed class MonumentCircle : MonoBehaviour
    {
        private const float Interval = 0.1f;

        private Entity _entity;
        private Light _light;
        private Vector3 _center;
        private DecalProjector _decal;
        private Texture2D _texture;
        private Color32[] _pixels;
        private float _next;
        private float _shownFill = -1f;
        private int _shownHolder = -1, _shownCapturer = -1;

        public void Bind(Entity entity, Light light, Vector3 center)
        {
            _entity = entity;
            _light = light;
            _center = center;
        }

        private void Start()
        {
            var cfg = MonumentSystemConfig.I;
            int size = cfg.circleResolution;
            _texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"MonumentCircle_{_entity.Index}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _pixels = new Color32[size * size];
            _decal = GroundDecals.Rent(GroundDecals.Ring(), Color.white);
            GroundDecals.SetPretinted(_decal, _texture);
            GroundDecals.Place(_decal, _center, cfg.captureRadius * 2f);
        }

        private void OnDestroy()
        {
            if (_decal != null) GroundDecals.Return(_decal);
            if (_texture != null) Destroy(_texture);
        }

        private static Color ColorOf(Faction f)
            => (int)f >= 8 ? MonumentSystemConfig.I.neutralColor : FactionColors.Get(f);

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<MonumentState>(_entity))
            {
                Destroy(gameObject);
                return;
            }

            var s = em.GetComponentData<MonumentState>(_entity);
            var cfg = MonumentSystemConfig.I;
            bool capturing = s.CaptureSeconds > 0 && (int)s.Capturer < 8;
            float target = capturing ? (float)s.CaptureSeconds / cfg.captureSeconds : 0f;

            // The sim counts whole seconds; the circle eases toward them.
            float fill = _shownFill < 0f || target < _shownFill
                ? target
                : Mathf.MoveTowards(_shownFill, target, Interval / cfg.captureSeconds * 2f);

            bool changed = (int)s.Holder != _shownHolder
                || (int)s.Capturer != _shownCapturer
                || Mathf.Abs(fill - _shownFill) > 0.002f;
            if (!changed) return;

            _shownHolder = (int)s.Holder;
            _shownCapturer = (int)s.Capturer;
            _shownFill = fill;
            if (_light != null) _light.color = ColorOf(s.Holder);
            Paint((int)s.Holder < 8, ColorOf(s.Holder), ColorOf(s.Capturer), fill);
        }

        /// <summary>d runs 0 at the centre to 1 at the rim. Alpha inside is d, so
        /// the centre is fully transparent and the rim fully visible.</summary>
        private void Paint(bool held, Color holder, Color capturer, float fill)
        {
            var cfg = MonumentSystemConfig.I;
            int size = cfg.circleResolution;
            float half = (size - 1) * 0.5f;
            float inner = 1f - cfg.circleEdge;
            float reach = 1f - fill * inner;   // the capture's inner limit
            float aa = 1.5f / half;
            for (int y = 0; y < size; y++)
            {
                float dy = (y - half) / half;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    float a;
                    if (d >= inner)
                    {
                        // The outer edge: always visible, in the holder's colour.
                        c = holder;
                        a = Mathf.Clamp01((1f - d) / aa);
                    }
                    else if (fill > 0f && d >= reach)
                    {
                        c = capturer;
                        a = d;
                    }
                    else if (held)
                    {
                        c = holder;
                        a = d;
                    }
                    else
                    {
                        c = holder;
                        a = 0f;
                    }
                    c.a = a;
                    _pixels[y * size + x] = c;
                }
            }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }
    }
}
