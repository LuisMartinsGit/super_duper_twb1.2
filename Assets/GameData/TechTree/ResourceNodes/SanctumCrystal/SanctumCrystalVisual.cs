// SanctumCrystalVisual.cs
// The slow life of the Sanctum landmark: the shard above the spires turns and
// bobs, and the gold light breathes with it. Presentation only — it moves
// transforms and a light, never a material (Art_Direction.md §10: emissive
// crystals keep a static HDR material), and the sim never reads it.
//
// Children it looks for by name, authored by SanctumCrystalAuthor:
//   "FloatingShard"  turned and bobbed
//   "SanctumLight"   the point light that breathes
// The phase comes from the landmark's position, so two Sanctums never pulse in
// step and every client draws the same one.

using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    public sealed class SanctumCrystalVisual : MonoBehaviour
    {
        private static SanctumCrystalVisualConfig _cfg;
        public static SanctumCrystalVisualConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<SanctumCrystalVisualConfig>());

        private Transform _shard;
        private Vector3 _shardRest;
        private Light _light;
        private float _lightRest;
        private float _phase;

        private void Start()
        {
            _shard = FindDeep(transform, "FloatingShard");
            if (_shard != null) _shardRest = _shard.localPosition;
            var lt = FindDeep(transform, "SanctumLight");
            if (lt != null) _light = lt.GetComponent<Light>();
            if (_light != null) _lightRest = _light.intensity;
            var p = transform.position;
            _phase = Mathf.Repeat(p.x * 0.137f + p.z * 0.071f, 1f);
        }

        private void Update()
        {
            var cfg = Cfg;
            if (cfg == null) return;
            float cycle = Mathf.Max(0.1f, cfg.cycleSeconds);
            float wave = Mathf.Sin((Time.time / cycle + _phase) * Mathf.PI * 2f);
            if (_shard != null)
            {
                _shard.localPosition = _shardRest + Vector3.up * (wave * cfg.shardBobMetres);
                _shard.Rotate(0f, cfg.shardSpinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            }
            if (_light != null)
                _light.intensity = _lightRest * (1f + wave * cfg.lightPulseShare);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
