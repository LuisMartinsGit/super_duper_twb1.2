// HeadlessFpsWatch.Ablate.cs
// -twbAblate (development player): which per-frame scripts cost the most. Every
// script type with an Update or LateUpdate is switched off for a short window,
// with a baseline window between each; ablate.txt lists the main-thread time of
// the script phases in each, so the difference is that type's cost. A test-match
// tool: switching a presentation script off for two seconds is harmless, and the
// simulation's own drivers are left alone.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using TheWaningBorder.Core;

namespace TheWaningBorder.Bootstrap
{
    internal sealed partial class HeadlessFpsWatch
    {
        static readonly string[] Keep = { "HeadlessFpsWatch", "HeadlessBatch", "Lockstep", "Bootstrap", "Replay", "Snapshot", "SimClock", "MatchLog", "MatchMetrics", "FrameHitch", "EventSystem", "InputModule", "Camera" };

        IEnumerator Ablate()
        {
            var upd = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Update.ScriptRunBehaviourUpdate", 1);
            var late = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PreLateUpdate.ScriptRunBehaviourLateUpdate", 1);
            var sim = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Default World Unity.Entities.SimulationSystemGroup", 1);
            var pres = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Default World Unity.Entities.PresentationSystemGroup", 1);
            string file = Path.Combine(_dir, "ablate.txt");
            File.WriteAllText(file, "type (instances): scripts ms = Update + LateUpdate - ECS groups, frame ms; base = the window before\n");

            var types = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(b => b != null && b.isActiveAndEnabled)
                .Select(b => b.GetType()).Distinct()
                .Where(t => !t.Namespace?.StartsWith("UnityEngine") ?? true)
                .Where(t => !Keep.Any(k => t.Name.Contains(k)))
                .Where(t => HasFrameMethod(t))
                .ToList();

            double Scripts(int n, List<double> acc) => acc.Count == 0 ? 0 : acc.Average();
            IEnumerator Window(float seconds, List<double> scripts, List<double> frames)
            {
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 0.35f) yield return null;
                while (Time.unscaledTime - t0 < seconds)
                {
                    yield return null;
                    scripts.Add((upd.LastValue + late.LastValue - sim.LastValue - pres.LastValue) * 1e-6);
                    frames.Add(Time.unscaledDeltaTime * 1000.0);
                }
            }

            foreach (var t in types)
            {
                var b0 = new List<double>(); var f0 = new List<double>();
                yield return Window(1.5f, b0, f0);
                var on = FindObjectsByType(t, FindObjectsSortMode.None).OfType<MonoBehaviour>().Where(b => b.isActiveAndEnabled).ToList();
                foreach (var b in on) b.enabled = false;
                var b1 = new List<double>(); var f1 = new List<double>();
                yield return Window(1.5f, b1, f1);
                foreach (var b in on) if (b != null) b.enabled = true;
                File.AppendAllText(file, FormattableString.Invariant(
                    $"{t.Name} ({on.Count}): scripts {b1.Average():0.00} vs base {b0.Average():0.00} = {b0.Average() - b1.Average():+0.00;-0.00}, frame {f1.Average():0.0} vs {f0.Average():0.0}\n"));
            }
            upd.Dispose(); late.Dispose(); sim.Dispose(); pres.Dispose();
            File.AppendAllText(file, "DONE\n");
        }

        static bool HasFrameMethod(Type t)
        {
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                foreach (var n in new[] { "Update", "LateUpdate" })
                    if (x.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null) != null)
                        return true;
            return false;
        }

        // Who owns the renderers, lights and always-on animators.
        static string Owners()
        {
            static string Root(Component c)
            {
                var n = c.transform.root.name;
                int cut = n.IndexOfAny(new[] { '(', '_' });
                return cut > 0 ? n.Substring(0, cut) : n;
            }
            var sb = new System.Text.StringBuilder();
            var rs = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            sb.Append("  renderers by root:\n");
            foreach (var g in rs.GroupBy(Root).OrderByDescending(g => g.Count()).Take(15))
                sb.Append($"    {g.Count(),7}  {g.Key}  (shadows {g.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}, visible {g.Count(r => r.isVisible)}, first: {g.First().name})\n");
            sb.Append("  lights by root:\n");
            foreach (var g in FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled).GroupBy(Root).OrderByDescending(g => g.Count()).Take(8))
                sb.Append($"    {g.Count(),7}  {g.Key}  ({g.First().type}, range {g.First().range:0.0}, shadows {g.First().shadows})\n");
            sb.Append("  always-animating animators by root:\n");
            foreach (var g in FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(a => a.isActiveAndEnabled && a.cullingMode == AnimatorCullingMode.AlwaysAnimate).GroupBy(Root).OrderByDescending(g => g.Count()).Take(8))
                sb.Append($"    {g.Count(),7}  {g.Key}  ({g.First().name})\n");
            return sb.ToString();
        }
    }
}
