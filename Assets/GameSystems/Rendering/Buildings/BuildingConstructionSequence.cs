// BuildingConstructionSequence.cs
// THE AUTHORED CONSTRUCTION SEQUENCE OF A HAND-BUILT PREFAB (2026-11-12).
//
// Developer, on Garrison_2: "when in construction, the items should appear in
// the order given by the numbers, using the effect named in the [section].
// Make sure there are small gaps of time between events."
//
// A prefab opts in by naming the groups directly under its root
//
//     <number> [<effect>] - <anything>        "3 [rise sequential] - Pillars"
//
// The number is the order, the bracket is how the group appears:
//
//   snap                 there at once, no delay and no animation (logic
//                        groups: markers, the Footprint). Takes no time.
//   rise                 the whole group rises out of the ground together,
//                        shaking, with a dust cloud.
//   rise sequential      its parts (the group's direct children) rise one at
//                        a time, each shaking under its own dust cloud.
//   rise clean           the whole group rises: no shake, no dust.
//   disolve              the group is revealed by the level-up wave
//                        (BuildingLitDissolve + BuildingWaveBand). No dust,
//                        no shake.
//   disolve sequential   the same, one part at a time.
//   fade in              the group goes from 0 to 100 % opacity.
//
// ("dissolve" is read too; "-" and "_" count as spaces, case is ignored. An
// unknown effect snaps and logs once.)
//
// The sequence is laid over the site's CONSTRUCTION PROGRESS, not over a
// clock: every event owns a slice of 0..1 with a small empty slice before the
// next one, so a site nobody is working on stands still mid-event and a
// faster build plays the same sequence faster. Sequential parts are shorter
// events than whole groups (the weights below).
//
// The dust is the Hovl loop the ordinary site dust uses
// (BuildingEffectSystem.asset) — a prefab, nothing is authored here. A
// sequenced site does NOT also get the whole-site dust loop
// (BuildingEffectSystem.UpdateConstructionDust skips it): which groups raise
// dust is the prefab's decision.
//
// BuildingRiseData owns this component and forwards to it, so every caller
// of the rise (PresentationSpawnSystem's sync, the swap) is unchanged.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class BuildingConstructionSequence : MonoBehaviour
    {
        // ── Pacing (shares of the build, relative to each other) ─────────
        private const float GroupWeight = 1f;       // a whole group appearing
        private const float PartWeight = 0.45f;     // one part of a sequential group
        private const float GapWeight = 0.14f;      // the pause between two events
        private const float PartGapWeight = 0.07f;  // the pause between two parts

        // ── Rise ────────────────────────────────────────────────────────
        private const float SinkPad = 0.4f;         // metres below the root plane at the start
        private const float ShakeAmplitude = 0.05f; // metres, at the start of the rise
        private const float ShakeFrequency = 17f;
        private const float StallSeconds = 0.5f;    // no progress this long = nobody is building

        // ── Dissolve (as the level-up wave) ─────────────────────────────
        private const float WaveSpanPad = 0.6f;
        private const float BandFadeFrom = 0.8f;    // the seam glow tapers over the last fifth

        private enum Effect { Snap, Rise, RiseSequential, RiseClean, Dissolve, DissolveSequential, FadeIn }
        private enum State { Unknown, Pending, Active, Done }

        private struct Binding
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Instances;
        }

        private sealed class Step
        {
            public Effect Effect;
            public Transform Target;
            public Vector3 RestLocal;
            public Renderer[] Renderers;
            public float Start, End;          // slice of construction progress
            public float LocalMinY, Height;   // of its meshes, relative to the root
            public Vector3 LocalBase;         // bottom centre, root-local (dust)
            public float Radius;              // half its wider side, metres
            public float Seed;
            public State State;
            public List<Binding> Bindings;
            public List<GameObject> Overlays;
            public List<Material> OverlayMats;
            public GameObject Dust;
            public bool DustEmitting;
        }

        private readonly List<Step> _steps = new List<Step>();
        private float _lastRatio = -1f;
        private float _lastAdvanceAt = float.NegativeInfinity;
        private bool _built;

        // The prefab's own lights: dark until the building is finished, like
        // the lantern (Art_Direction.md 6.3).
        private Light[] _lights = System.Array.Empty<Light>();
        private bool _lightsOn = true;

        private void SetLights(bool on)
        {
            if (_lightsOn == on) return;
            _lightsOn = on;
            foreach (var l in _lights) if (l != null) l.enabled = on;
        }

        public bool HasSteps => _steps.Count > 0;

        // ═════════════════════════════════════════════════════════════════
        // Reading the prefab
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Does any group directly under <paramref name="root"/> carry
        /// the "number [effect]" name?</summary>
        public static bool IsAuthoredOn(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
                if (TryParse(root.GetChild(i).name, out _, out _)) return true;
            return false;
        }

        /// <summary>"12 [rise clean] - Supports" → 12, "rise clean".</summary>
        private static bool TryParse(string name, out int order, out string effect)
        {
            order = 0;
            effect = null;
            if (string.IsNullOrEmpty(name)) return false;
            int i = 0;
            while (i < name.Length && char.IsWhiteSpace(name[i])) i++;
            int digitsFrom = i;
            long parsed = 0;
            while (i < name.Length && char.IsDigit(name[i]))
            {
                parsed = System.Math.Min(parsed * 10 + (name[i] - '0'), int.MaxValue);
                i++;
            }
            if (i == digitsFrom) return false;
            while (i < name.Length && char.IsWhiteSpace(name[i])) i++;
            if (i >= name.Length || name[i] != '[') return false;
            int close = name.IndexOf(']', i + 1);
            if (close < 0) return false;
            order = (int)parsed;
            effect = name.Substring(i + 1, close - i - 1);
            return true;
        }

        private static bool TryEffect(string text, out Effect effect)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            bool space = false;
            foreach (char raw in text.Trim().ToLowerInvariant())
            {
                char c = (raw == '-' || raw == '_') ? ' ' : raw;
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            switch (sb.ToString())
            {
                case "snap": effect = Effect.Snap; return true;
                case "rise": effect = Effect.Rise; return true;
                case "rise sequential": effect = Effect.RiseSequential; return true;
                case "rise clean": effect = Effect.RiseClean; return true;
                case "disolve":
                case "dissolve": effect = Effect.Dissolve; return true;
                case "disolve sequential":
                case "dissolve sequential": effect = Effect.DissolveSequential; return true;
                case "fade in":
                case "fadein": effect = Effect.FadeIn; return true;
            }
            effect = Effect.Snap;
            return false;
        }

        /// <summary>Read the groups and lay the events over 0..1. Call with the
        /// view at rest (every piece where the prefab has it).</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            var root = transform;
            var lit = new List<Light>();
            foreach (var l in GetComponentsInChildren<Light>(true)) if (l.enabled) lit.Add(l);
            _lights = lit.ToArray();
            var groups = new List<(int order, int sibling, Effect effect, Transform t)>();
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                if (!TryParse(child.name, out int order, out string text)) continue;
                if (!TryEffect(text, out var effect))
                    Debug.LogWarning($"[BuildingConstructionSequence] '{child.name}': unknown effect '{text}' — it snaps in.");
                if (effect == Effect.Snap) continue;   // there from the start, untouched
                groups.Add((order, i, effect, child));
            }
            groups.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.sibling.CompareTo(b.sibling));

            // Events in order, each with its weight and the pause after it.
            var weights = new List<float>();
            var gaps = new List<float>();
            foreach (var g in groups)
            {
                bool sequential = g.effect == Effect.RiseSequential || g.effect == Effect.DissolveSequential;
                if (!sequential)
                {
                    if (!TryAddStep(g.effect, g.t)) continue;
                    weights.Add(GroupWeight);
                    gaps.Add(GapWeight);
                    continue;
                }
                int added = 0;
                for (int c = 0; c < g.t.childCount; c++)
                {
                    var part = g.t.GetChild(c);
                    if (!part.gameObject.activeSelf) continue;
                    if (!TryAddStep(g.effect, part)) continue;
                    weights.Add(PartWeight);
                    gaps.Add(PartGapWeight);
                    added++;
                }
                if (added > 0) gaps[gaps.Count - 1] = GapWeight;   // after the group's last part
            }
            if (_steps.Count == 0) return;

            gaps[gaps.Count - 1] = 0f;   // the last event ends the build
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i] + gaps[i];
            float at = 0f;
            for (int i = 0; i < _steps.Count; i++)
            {
                _steps[i].Start = at / total;
                at += weights[i];
                _steps[i].End = at / total;
                at += gaps[i];
            }
        }

        private bool TryAddStep(Effect effect, Transform target)
        {
            var found = target.GetComponentsInChildren<Renderer>(false);
            var renderers = new List<Renderer>(found.Length);
            Bounds b = default;
            foreach (var r in found)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (renderers.Count == 0) b = r.bounds; else b.Encapsulate(r.bounds);
                renderers.Add(r);
            }
            if (renderers.Count == 0) return false;   // nothing to show: no event, no pause

            var root = transform;
            _steps.Add(new Step
            {
                Effect = effect,
                Target = target,
                RestLocal = target.localPosition,
                Renderers = renderers.ToArray(),
                LocalMinY = b.min.y - root.position.y,
                Height = b.size.y,
                LocalBase = root.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z)),
                Radius = Mathf.Max(b.extents.x, b.extents.z),
                Seed = _steps.Count * 7.31f,
                State = State.Unknown,
            });
            return true;
        }

        // ═════════════════════════════════════════════════════════════════
        // Driving it
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Show the site at construction progress <paramref name="ratio"/> (0..1).</summary>
        public void Apply(float ratio)
        {
            if (ratio > _lastRatio) _lastAdvanceAt = Time.time;
            _lastRatio = ratio;
            SetLights(ratio >= 1f);
            bool working = Time.time - _lastAdvanceAt < StallSeconds;

            for (int i = 0; i < _steps.Count; i++)
            {
                var s = _steps[i];
                float p = Mathf.InverseLerp(s.Start, s.End, ratio);
                if (ratio >= 1f) p = 1f;
                if (p <= 0f) { Enter(s, State.Pending); continue; }
                if (p >= 1f) { Enter(s, State.Done); continue; }
                Enter(s, State.Active);
                Animate(s, p, working);
            }
        }

        /// <summary>Construction is over: everything at rest, wearing its own materials.</summary>
        public void Complete()
        {
            SetLights(true);
            bool any = false;
            for (int i = 0; i < _steps.Count; i++)
            {
                any |= _steps[i].State != State.Done;
                Enter(_steps[i], State.Done);
            }
            // The owner's colour is re-asserted once every borrowed material is
            // handed back — never while a group still wears a wave material
            // (Apply would clone it through Renderer.materials).
            if (any) BuildingFactionColorMarker.Reapply(gameObject);
        }

        private void Enter(Step s, State next)
        {
            if (s.State == next) return;
            if (s.State == State.Active) Leave(s);
            s.State = next;

            bool shown = next != State.Pending;
            for (int i = 0; i < s.Renderers.Length; i++)
                if (s.Renderers[i] != null) s.Renderers[i].enabled = shown;
            if (s.Target != null) s.Target.localPosition = s.RestLocal;
            if (next != State.Active) return;

            switch (s.Effect)
            {
                case Effect.Dissolve:
                case Effect.DissolveSequential:
                    BindWave(s, reveal: true);
                    BuildBand(s);
                    break;
                case Effect.FadeIn:
                    BindWave(s, reveal: false);
                    foreach (var b in s.Bindings)
                        foreach (var m in b.Instances)
                            if (m != null) BuildingDissolveTransition.MakeFading(m);
                    break;
                case Effect.Rise:
                case Effect.RiseSequential:
                    SpawnDust(s);
                    break;
            }
        }

        private void Animate(Step s, float p, bool working)
        {
            float eased = p * p * (3f - 2f * p);
            switch (s.Effect)
            {
                case Effect.Rise:
                case Effect.RiseSequential:
                case Effect.RiseClean:
                {
                    if (s.Target == null) break;
                    // Straight down in the world, whatever the parent's frame.
                    float depth = Mathf.Max(0f, s.LocalMinY) + s.Height + SinkPad;
                    Vector3 world = Vector3.down * depth * (1f - eased);
                    if (s.Effect != Effect.RiseClean && working)
                    {
                        float a = ShakeAmplitude * (1f - 0.7f * eased);
                        float t = Time.time * ShakeFrequency;
                        world.x += (Mathf.PerlinNoise(t, s.Seed) - 0.5f) * 2f * a;
                        world.z += (Mathf.PerlinNoise(s.Seed, t) - 0.5f) * 2f * a;
                        world.y += (Mathf.PerlinNoise(t + 31.7f, s.Seed + 5.3f) - 0.5f) * a;
                    }
                    var parent = s.Target.parent;
                    s.Target.localPosition = s.RestLocal + (parent == null ? world : parent.InverseTransformVector(world));
                    SetDustEmitting(s, working);
                    break;
                }
                case Effect.Dissolve:
                case Effect.DissolveSequential:
                {
                    float glow = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BandFadeFrom, 1f, p));
                    foreach (var b in s.Bindings)
                        foreach (var m in b.Instances)
                            if (m != null) m.SetFloat("_DissolveAmount", eased);
                    if (s.OverlayMats != null)
                        foreach (var m in s.OverlayMats)
                        {
                            if (m == null) continue;
                            m.SetFloat("_DissolveAmount", eased);
                            m.SetFloat("_Intensity", glow);
                        }
                    break;
                }
                case Effect.FadeIn:
                    foreach (var b in s.Bindings)
                        foreach (var m in b.Instances)
                            if (m != null) m.SetFloat("_Opacity", eased);
                    break;
            }
        }

        /// <summary>Hand back everything an active event borrowed.</summary>
        private void Leave(Step s)
        {
            if (s.Bindings != null)
            {
                foreach (var b in s.Bindings)
                {
                    if (b.Renderer != null) b.Renderer.sharedMaterials = b.Originals;
                    foreach (var m in b.Instances) if (m != null) Destroy(m);
                }
                s.Bindings = null;
            }
            if (s.Overlays != null)
            {
                foreach (var o in s.Overlays) if (o != null) Destroy(o);
                foreach (var m in s.OverlayMats) if (m != null) Destroy(m);
                s.Overlays = null;
                s.OverlayMats = null;
            }
            if (s.Dust != null)
            {
                BuildingEffectSystem.FadeOutEffect(s.Dust);
                s.Dust = null;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _steps.Count; i++)
                if (_steps[i].State == State.Active) Leave(_steps[i]);
        }

        // ═════════════════════════════════════════════════════════════════
        // The effects' materials and prefabs
        // ═════════════════════════════════════════════════════════════════

        private float WaveOriginY(Step s) => transform.position.y + s.LocalMinY;
        private static float WaveSpan(Step s) => Mathf.Max(s.Height + WaveSpanPad, 1f);

        /// <summary>Dress the event's meshes in the level-up wave material:
        /// revealed bottom-up (<paramref name="reveal"/>) or whole (the fade).</summary>
        private void BindWave(Step s, bool reveal)
        {
            s.Bindings = new List<Binding>(s.Renderers.Length);
            float originY = WaveOriginY(s), span = WaveSpan(s);
            foreach (var r in s.Renderers)
            {
                if (r == null) continue;
                var originals = r.sharedMaterials;
                if (originals == null || originals.Length == 0) continue;
                var instances = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                    instances[i] = BuildingDissolveTransition.CreateLitDissolve(originals[i], reveal ? 1f : 0f, originY, span);
                if (instances[0] == null) return;   // shader missing: the group just stands there
                s.Bindings.Add(new Binding { Renderer = r, Originals = originals, Instances = instances });
                r.sharedMaterials = instances;
            }
        }

        /// <summary>The glowing seam of the wave, on a copy of each mesh.</summary>
        private void BuildBand(Step s)
        {
            s.Overlays = new List<GameObject>();
            s.OverlayMats = new List<Material>();
            var stamp = GetComponent<BuildingFactionColorStamp>();
            Color edge = stamp != null ? stamp.Value : new Color(1f, 0.85f, 0.45f);
            float originY = WaveOriginY(s), span = WaveSpan(s);
            foreach (var r in s.Renderers)
            {
                if (!(r is MeshRenderer)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mat = BuildingDissolveTransition.CreateBand(edge, originY, span, mf.name);
                if (mat == null) return;
                var overlay = BuildingDissolveTransition.CreateBandOverlay(mf, mat);
                s.Overlays.Add(overlay);
                s.OverlayMats.Add(mat);
            }
        }

        private void SpawnDust(Step s)
        {
            float scale = Mathf.Clamp(s.Radius, 0.75f, 10f);
            s.Dust = BuildingEffectSystem.SpawnConstructionDust(transform, transform.TransformPoint(s.LocalBase), scale);
            s.DustEmitting = s.Dust != null;
        }

        private static void SetDustEmitting(Step s, bool emit)
        {
            if (s.Dust == null || s.DustEmitting == emit) return;
            s.DustEmitting = emit;
            foreach (var ps in s.Dust.GetComponentsInChildren<ParticleSystem>())
            {
                if (emit) ps.Play(false);
                else ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
