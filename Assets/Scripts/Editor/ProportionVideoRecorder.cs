// ProportionVideoRecorder.cs
// The frame grabber of ProportionVideoProbe (2026-11-11): drives the REAL
// camera rig along a scripted path (it writes CameraController's pan and zoom
// targets, so the tilt-with-zoom and the damping are the game's own) at a
// fixed capture rate and saves the Game view once a frame. Editor code cannot
// be attached as a component, so this is a plain coroutine that the probe
// starts ON the camera rig (any live MonoBehaviour can host a coroutine).

using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using TheWaningBorder.CameraRig;

namespace TheWaningBorder.EditorTools
{
    public static class ProportionVideoRecorder
    {
        public struct Key { public float T; public Vector3 Focus; public float Zoom; }

        static Key[] Keys;
        const int Fps = 30;
        const int WarmupFrames = 60;
        public static bool Running { get; private set; }

        /// <summary>Starts the take; false when there is no camera rig.</summary>
        public static bool Begin(Key[] keys, string outDir)
        {
            var rig = Object.FindFirstObjectByType<CameraController>();
            if (rig == null || Running) return false;
            Keys = keys;
            rig.StartCoroutine(Run(rig, outDir));
            return true;
        }

        static IEnumerator Run(CameraController rig, string OutDir)
        {
            Running = true;
            var fPos = typeof(CameraController).GetField("_targetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            var fZoom = typeof(CameraController).GetField("_targetZoom", BindingFlags.NonPublic | BindingFlags.Instance);
            if (rig == null || fPos == null || fZoom == null || Keys == null || Keys.Length < 2)
            {
                Debug.LogError("[ProportionVideo] recorder: no camera rig / fields / path");
                Running = false; yield break;
            }
            Directory.CreateDirectory(OutDir);
            foreach (var old in Directory.GetFiles(OutDir, "frame_*.jpg")) File.Delete(old);
            Time.captureFramerate = Fps;
            int total = Mathf.RoundToInt(Keys[Keys.Length - 1].T * Fps);
            var eof = new WaitForEndOfFrame();
            for (int f = -WarmupFrames; f < total; f++)
            {
                Eval(Mathf.Max(0, f) / (float)Fps, out var focus, out float zoom);
                fPos.SetValue(rig, new Vector3(focus.x, 0f, focus.z));
                fZoom.SetValue(rig, zoom);
                yield return eof;
                if (f < 0) continue;
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(OutDir, $"frame_{f:D4}.jpg"), shot.EncodeToJPG(92));
                Object.Destroy(shot);
            }
            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(OutDir, "done.txt"), total + " frames at " + Fps + " fps");
            Debug.Log("[ProportionVideo] recorded " + total + " frames -> " + OutDir);
            Running = false;
        }

        /// <summary>Catmull-Rom through the key focuses (no stop at a key),
        /// smoothstep on the zoom.</summary>
        static void Eval(float t, out Vector3 focus, out float zoom)
        {
            int i = 0;
            while (i < Keys.Length - 2 && t > Keys[i + 1].T) i++;
            var a = Keys[i]; var b = Keys[i + 1];
            float u = Mathf.Clamp01((t - a.T) / Mathf.Max(0.001f, b.T - a.T));
            Vector3 p0 = Keys[Mathf.Max(0, i - 1)].Focus, p3 = Keys[Mathf.Min(Keys.Length - 1, i + 2)].Focus;
            Vector3 p1 = a.Focus, p2 = b.Focus;
            // Ease the very first and the very last segment so the take starts and ends at rest.
            if (i == 0) u = u * u * (2f - u);
            if (i == Keys.Length - 2) u = 1f - (1f - u) * (1f - u) * (1f + u);
            float u2 = u * u, u3 = u2 * u;
            focus = 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
            float s = u * u * (3f - 2f * u);
            zoom = Mathf.Lerp(a.Zoom, b.Zoom, s);
        }
    }
}
