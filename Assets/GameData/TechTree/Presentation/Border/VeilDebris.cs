// VeilDebris.cs
// PHASE 5 polish — a small pooled shower of crystal shards when the veil is
// broken. Purely decorative: it's ONE ParticleSystem (its own internal pool),
// Emit()ed at the break point. It never simulates per-interior-crystal — the
// interior is merged mesh, the shards are a fixed particle burst.
//
// Self-mounts once; call VeilDebris.Burst(worldPos, radius) from anywhere.
//
// Location: Assets/GameData/TechTree/Presentation/Border/VeilDebris.cs

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class VeilDebris : MonoBehaviour
    {
        private static VeilDebris _instance;
        private ParticleSystem _ps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("VeilDebris");
            _instance = go.AddComponent<VeilDebris>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            _ps = gameObject.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.85f;
            main.startSpeed = 3.2f;
            main.startSize = 0.22f;
            main.gravityModifier = 2.6f;
            main.startColor = new Color(0.55f, 0.28f, 0.85f);
            main.maxParticles = 512;

            var emission = _ps.emission;
            emission.enabled = false; // manual Emit only

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1f;

            var renderer = GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                renderer.material = new Material(shader) { color = new Color(0.6f, 0.3f, 0.9f) };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Spray a shard burst at a world point. Count scales with the
        /// break radius but stays capped — this is decoration, not simulation.</summary>
        public static void Burst(Vector3 worldPos, float radius)
        {
            if (_instance == null || _instance._ps == null) return;
            _instance.transform.position = worldPos;
            var shape = _instance._ps.shape;
            shape.radius = Mathf.Max(0.5f, radius * 0.6f);
            int count = Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 12, 96);
            _instance._ps.Emit(count);
        }
    }
}
