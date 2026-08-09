// BuildingLevelUpEffect.cs
// One-shot level-up flourish: warm gold light pulse + upward spark burst +
// faction-tinted ground ring. Spawned by BuildingPrefabSwapSystem the moment
// the new prefab pops in. Self-destructs after ~2 seconds.
// Location: Assets/Scripts/Presentation/Buildings/BuildingLevelUpEffect.cs

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class BuildingLevelUpEffect : MonoBehaviour
    {
        private const float TotalDuration       = 2.0f;
        private const float LightRampTime       = 0.15f;  // 0 → peak
        private const float LightPeakIntensity  = 8.0f;

        /// <summary>
        /// Spawn the effect at the building's position. Sized to the building's
        /// renderer bounds; tinted by the faction colour on the outer flourish.
        /// </summary>
        public static void Spawn(GameObject building, Color factionAccent)
        {
            if (building == null) return;

            // Size to actual visual bounds rather than entity Radius — same
            // approach used elsewhere for sink-depth and collapse effects.
            var renderers = building.GetComponentsInChildren<Renderer>();
            float radius = 2f;
            float height = 3f;
            Vector3 worldPos = building.transform.position;
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                radius = Mathf.Max(b.extents.x, b.extents.z, 1f);
                height = Mathf.Max(b.size.y, 1.5f);
                worldPos = new Vector3(b.center.x, building.transform.position.y, b.center.z);
            }

            // Detached so the FX runs at unit scale even if the building's
            // transform has authored scale, and so it survives if the
            // building is despawned mid-flourish.
            var go = new GameObject("LevelUpFX");
            go.transform.position = worldPos;

            var fx = go.AddComponent<BuildingLevelUpEffect>();
            fx.Build(radius, height, factionAccent);
            Destroy(go, TotalDuration + 0.5f);
        }

        /// <summary>
        /// Tiny one-shot sparkle for unit-trained moments — a quick warm burst
        /// at the given world position, tinted by the faction colour. No light,
        /// no ring, fewer particles than the level-up flourish so it reads as
        /// a small "ding" rather than a major event.
        /// </summary>
        public static void SpawnTrained(Vector3 worldPos, Color factionAccent)
        {
            var go = new GameObject("UnitTrainedFX");
            go.transform.position = worldPos;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.25f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.75f, 1f),
                new Color(1f, 0.75f, 0.40f, 1f));

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.35f;
            shape.position = new Vector3(0f, 0.1f, 0f);
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f)));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(factionAccent, 1f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetSparkMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            ps.Emit(28);
            Destroy(go, 1.2f);
        }

        private Light _light;
        private float _elapsed;
        private float _lightScale = 1f;

        private void Build(float radius, float height, Color factionAccent)
        {
            // Warm gold point light that pulses bright then fades.
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.88f, 0.55f);
            _light.range = Mathf.Max(radius * 5f, 10f);
            _light.intensity = 0f;
            _lightScale = Mathf.Sqrt(radius); // bigger buildings get a bit more punch

            BuildSparks(radius, height, factionAccent);
            BuildBaseRing(radius, factionAccent);
        }

        private void BuildSparks(float radius, float height, Color factionAccent)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone pointing up

            var ps = go.AddComponent<ParticleSystem>();
            // ParticleSystem auto-plays on AddComponent; stop it so we can
            // mutate the main module (duration, loop, etc.) without warnings.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.maxParticles = 250;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.4f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.95f, 0.7f, 1f),
                new Color(1f, 0.7f, 0.3f, 1f));

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = Mathf.Max(radius * 0.5f, 0.3f);
            shape.radiusThickness = 1f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 0.6f),
                new Keyframe(1f, 0f)));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.55f),
                    new GradientColorKey(factionAccent, 1f),
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = grad;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 1.2f;
            noise.damping = true;

            var rotOverLife = ps.rotationOverLifetime;
            rotOverLife.enabled = true;
            rotOverLife.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetSparkMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0.01f;
            renderer.maxParticleSize = 6f;

            ps.Emit(140);
        }

        private void BuildBaseRing(float radius, Color factionAccent)
        {
            var go = new GameObject("BaseRing");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(factionAccent, Color.white, 0.35f),
                Color.Lerp(factionAccent, new Color(1f, 0.85f, 0.45f), 0.45f));
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = Mathf.Max(radius * 1.05f, 1.5f);
            shape.donutRadius = 0.05f;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(factionAccent, 1f),
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(0.4f, 1.4f),
                new Keyframe(1f, 1.9f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetSparkMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            ps.Emit(50);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / TotalDuration);

            // Quick attack, slow decay.
            float intensity = (t < LightRampTime)
                ? t / LightRampTime
                : 1f - (t - LightRampTime) / (1f - LightRampTime);
            intensity = Mathf.Max(0f, intensity);
            intensity *= intensity; // ease for a softer fade

            if (_light != null)
                _light.intensity = intensity * LightPeakIntensity * _lightScale;
        }

        // ── Shared additive material + soft circle texture ──────────────
        private static Material _sparkMaterial;
        private static Texture2D _sparkTexture;

        private static Material GetSparkMaterial()
        {
            if (_sparkMaterial != null) return _sparkMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.mainTexture = GetSparkTexture();
            mat.color = Color.white;

            // Additive blend for that "glowing" look across both URP and built-in.
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3100;
            // URP particle shader: Transparent surface, additive blend mode.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 2);
            mat.EnableKeyword("_ALPHABLEND_ON");

            _sparkMaterial = mat;
            return _sparkMaterial;
        }

        private static Texture2D GetSparkTexture()
        {
            if (_sparkTexture != null) return _sparkTexture;
            const int res = 64;
            _sparkTexture = new Texture2D(res, res, TextureFormat.RGBA32, false);
            _sparkTexture.wrapMode = TextureWrapMode.Clamp;
            _sparkTexture.filterMode = FilterMode.Bilinear;

            float c = (res - 1) * 0.5f;
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a; // sharper falloff → bright core, soft edge
                    px[y * res + x] = new Color(1f, 1f, 1f, a);
                }
            }
            _sparkTexture.SetPixels(px);
            _sparkTexture.Apply();
            return _sparkTexture;
        }
    }
}
