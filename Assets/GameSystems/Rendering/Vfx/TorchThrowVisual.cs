// TorchThrowVisual.cs
// The torch a melee unit throws at a building (decision 82, 2026-11-19;
// docs/Design/Combat_Pacing.md § Buildings): a burning torch tumbling along a
// short arc with a red-orange streak and a point light, and a flash where it
// lands.
//
// PRESENTATION ONLY. The hit is the simulation's and has already landed when
// the torch is drawn (MeleeCombatSystem.Swing posts it to TorchThrows). This
// draws only throws the viewer can see (FogOfWar, as the monuments do).
// Everything is pooled: a raid of forty throws a torch every second or two
// each. Tunables on TorchThrowVisual.asset.

using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Rendering
{
    public sealed class TorchThrowVisual : MonoBehaviour
    {
        private sealed class Piece
        {
            public GameObject Go;
            public Transform Spin;      // the tumbling torch (head + haft)
            public TrailRenderer Trail;
            public Light Light;
            public Renderer Flash;      // flash only
        }

        private struct Flight
        {
            public Piece P;
            public Vector3 From, To;
            public float T0;
            public bool Landed;
            public float ReturnAt;
        }

        private struct Burst
        {
            public Piece P;
            public float T0;
        }

        private TorchThrowVisualConfig _cfg;
        private Material _headMat, _haftMat, _streakMat, _flashMat;
        private readonly Stack<Piece> _torchPool = new Stack<Piece>();
        private readonly Stack<Piece> _flashPool = new Stack<Piece>();
        private readonly List<Flight> _flights = new List<Flight>();
        private readonly List<Burst> _bursts = new List<Burst>();
        private readonly List<TorchThrows.Throw> _drain = new List<TorchThrows.Throw>(64);
        private int _lit;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            _cfg = TorchThrowVisualConfig.I;
            if (_cfg == null) { enabled = false; return; }   // reported loudly by Require

            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            _headMat = Tinted(unlit, _cfg.headColor);
            _haftMat = Tinted(lit, _cfg.haftColor);
            _flashMat = Tinted(unlit, _cfg.flashColor);
            _streakMat = new Material(particles);
            // Additive glow; the gradient's alpha does the fading.
            if (_streakMat.HasProperty("_Blend")) _streakMat.SetFloat("_Blend", 2f);
            if (_streakMat.HasProperty("_Surface")) _streakMat.SetFloat("_Surface", 1f);
            _streakMat.SetOverrideTag("RenderType", "Transparent");
            _streakMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _streakMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _streakMat.SetInt("_ZWrite", 0);
            _streakMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _streakMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetColor(_streakMat, Color.white);
        }

        void OnEnable() => TorchThrows.Listening = true;

        void OnDisable()
        {
            TorchThrows.Listening = false;
            TorchThrows.Clear();
        }

        private static Material Tinted(Shader s, Color c)
        {
            var m = new Material(s);
            SetColor(m, c);
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        // ── pieces ──────────────────────────────────────────────────────

        private static GameObject Primitive(PrimitiveType type, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private static Light AddLight(GameObject go)
        {
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        private Piece NewTorch()
        {
            var root = new GameObject("ThrownTorch");
            root.transform.SetParent(transform, false);
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);

            float h = _cfg.haftLength;
            var haft = Primitive(PrimitiveType.Cylinder, spin, _haftMat);
            haft.transform.localScale = new Vector3(h * 0.12f, h * 0.5f, h * 0.12f);
            var head = Primitive(PrimitiveType.Sphere, spin, _headMat);
            head.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            head.transform.localScale = Vector3.one * _cfg.headSize;

            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _streakMat;
            trail.time = _cfg.streakSeconds;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = _cfg.streakWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(_cfg.streakColor, 0f), new GradientColorKey(_cfg.streakTailColor, 1f) },
                new[] { new GradientAlphaKey(_cfg.streakColor.a, 0f), new GradientAlphaKey(_cfg.streakTailColor.a, 1f) });
            trail.colorGradient = g;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;

            var light = AddLight(root);
            light.color = _cfg.lightColor;
            light.intensity = _cfg.lightIntensity;
            light.range = _cfg.lightRange;

            root.SetActive(false);
            return new Piece { Go = root, Spin = spin, Trail = trail, Light = light };
        }

        private Piece NewFlash()
        {
            var root = new GameObject("TorchFlash");
            root.transform.SetParent(transform, false);
            var ball = Primitive(PrimitiveType.Sphere, root.transform, _flashMat);
            var light = AddLight(root);
            light.color = _cfg.lightColor;
            light.range = _cfg.flashLightRange;
            root.SetActive(false);
            return new Piece { Go = root, Light = light, Flash = ball.GetComponent<Renderer>() };
        }

        private bool TakeLight()
        {
            if (_lit >= _cfg.maxLights) return false;
            _lit++;
            return true;
        }

        private void DropLight(Piece p)
        {
            if (p.Light != null && p.Light.enabled) { p.Light.enabled = false; _lit--; }
        }

        // ── frame ───────────────────────────────────────────────────────

        void LateUpdate()
        {
            float now = Time.time;
            _drain.Clear();
            TorchThrows.Drain(_drain);
            for (int i = 0; i < _drain.Count; i++) Launch(_drain[i], now);

            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                if (f.Landed)
                {
                    if (now < f.ReturnAt) continue;
                    f.P.Go.SetActive(false);
                    _torchPool.Push(f.P);
                    _flights.RemoveAt(i);
                    continue;
                }

                float k = Mathf.Clamp01((now - f.T0) / Mathf.Max(0.01f, _cfg.flightSeconds));
                Vector3 pos = Vector3.Lerp(f.From, f.To, k);
                pos.y += 4f * _cfg.arcHeight * k * (1f - k);
                var t = f.P.Go.transform;
                Vector3 dir = pos - t.position;
                t.position = pos;
                if (dir.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(dir);
                f.P.Spin.localRotation = Quaternion.Euler((now - f.T0) * _cfg.spinTurnsPerSecond * 360f, 0f, 0f);

                if (k >= 1f)
                {
                    // Landed: the torch goes out, its streak fades where it stopped.
                    f.Landed = true;
                    f.ReturnAt = now + _cfg.streakSeconds;
                    f.P.Spin.gameObject.SetActive(false);
                    DropLight(f.P);
                    Flash(f.To, now);
                }
                _flights[i] = f;
            }

            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                var b = _bursts[i];
                float k = (now - b.T0) / Mathf.Max(0.01f, _cfg.flashSeconds);
                if (k >= 1f)
                {
                    DropLight(b.P);
                    b.P.Go.SetActive(false);
                    _flashPool.Push(b.P);
                    _bursts.RemoveAt(i);
                    continue;
                }
                float fade = 1f - k;
                b.P.Flash.transform.localScale = Vector3.one * (_cfg.flashSize * (0.4f + 0.6f * Mathf.Sqrt(k)));
                b.P.Flash.enabled = fade > 0.05f;
                if (b.P.Light.enabled) b.P.Light.intensity = _cfg.flashLightIntensity * fade * fade;
            }
        }

        private void Launch(TorchThrows.Throw th, float now)
        {
            Vector3 from = (Vector3)th.From + Vector3.up * _cfg.releaseHeight;
            Vector3 to = (Vector3)th.To + Vector3.up * _cfg.impactHeight;
            if (!Seen(from) && !Seen(to)) return;

            var p = _torchPool.Count > 0 ? _torchPool.Pop() : NewTorch();
            p.Go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(
                (to - from).sqrMagnitude > 1e-6f ? to - from : Vector3.forward));
            p.Spin.gameObject.SetActive(true);
            p.Go.SetActive(true);
            p.Trail.Clear();   // a recycled streak never draws from its last landing
            p.Light.enabled = TakeLight();
            _flights.Add(new Flight { P = p, From = from, To = to, T0 = now });
        }

        private void Flash(Vector3 at, float now)
        {
            var p = _flashPool.Count > 0 ? _flashPool.Pop() : NewFlash();
            p.Go.transform.position = at;
            p.Flash.enabled = true;
            p.Flash.transform.localScale = Vector3.one * (_cfg.flashSize * 0.4f);
            p.Light.enabled = TakeLight();
            p.Light.intensity = _cfg.flashLightIntensity;
            p.Go.SetActive(true);
            _bursts.Add(new Burst { P = p, T0 = now });
        }

        private static bool Seen(Vector3 position)
        {
            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            var viewer = GameSettings.ViewFaction;
            if (fog == null || viewer == null) return true;
            return fog.IsVisible(viewer.Value, position);
        }
    }
}
