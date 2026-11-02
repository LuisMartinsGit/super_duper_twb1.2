// UnitTrainedSparkle.cs
// The small warm "ding" at a trainer's door when a unit is trained. Lived in
// BuildingLevelUpEffect until 2026-10-30, when the level-up nova (gold light
// pulse, spark fountain, ground ring) was removed from the game outright; this
// is the one piece of it that was never a level-up effect.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class UnitTrainedSparkle
    {
        /// <summary>
        /// A quick warm burst at <paramref name="worldPos"/>, tinted by the
        /// faction colour as it fades. No light, no ring: a small "ding", not
        /// a major event.
        /// </summary>
        public static void Spawn(Vector3 worldPos, Color factionAccent)
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
            Object.Destroy(go, 1.2f);
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
