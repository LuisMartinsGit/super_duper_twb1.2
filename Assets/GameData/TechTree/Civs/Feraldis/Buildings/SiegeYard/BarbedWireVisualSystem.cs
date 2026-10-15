// BarbedWireVisualSystem.cs
// Draws every live Harpoon Rig barbed-wire line (BarbedWire entity) as a
// dark, rusty line just above the ground, so the hazard the simulation runs is
// visible. Presentation only: it reads the sim state and never writes it.
// docs/Design/Age_1_Feraldis.md §7.5.

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BarbedWireVisualSystem : SystemBase
    {
        private readonly Dictionary<Entity, LineRenderer> _lines = new Dictionary<Entity, LineRenderer>();
        private readonly HashSet<Entity> _seen = new HashSet<Entity>();
        private readonly List<Entity> _gone = new List<Entity>();
        private Material _material;

        protected override void OnUpdate()
        {
            _seen.Clear();
            foreach (var (wire, e) in SystemAPI.Query<RefRO<BarbedWire>>().WithEntityAccess())
            {
                _seen.Add(e);
                if (!_lines.TryGetValue(e, out var line) || line == null)
                {
                    line = Create();
                    _lines[e] = line;
                }
                Vector3 a = wire.ValueRO.From, b = wire.ValueRO.To;
                a.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(a.x, a.z) + 0.35f;
                b.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(b.x, b.z) + 0.35f;
                line.SetPosition(0, a);
                line.SetPosition(1, b);
            }

            _gone.Clear();
            foreach (var kv in _lines) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var e in _gone)
            {
                if (_lines[e] != null) Object.Destroy(_lines[e].gameObject);
                _lines.Remove(e);
            }
        }

        private LineRenderer Create()
        {
            if (_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                var rust = new Color(0.32f, 0.07f, 0.05f);
                _material = new Material(shader) { color = rust };
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", rust);
            }
            var go = new GameObject("BarbedWire");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = 0.18f;
            line.sharedMaterial = _material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        protected override void OnDestroy()
        {
            foreach (var kv in _lines) if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
            _lines.Clear();
        }
    }
}
