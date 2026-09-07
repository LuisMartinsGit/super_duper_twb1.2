// EditorBridge.cs
// A file-based command channel into the OPEN editor, for a tool that cannot
// take the project lock — an agent working beside a running Unity, or a
// script. Unity allows one editor per project, so anything that needs the
// editor while it is open has to ask that editor; this is the asking.
//
// Protocol: write ONE line to Temp/agent-bridge/request.txt. The editor polls
// twice a second, deletes the request, runs it, and writes the outcome to
// Temp/agent-bridge/response.txt ("OK …" or "ERROR …"). Nothing is queued:
// one request in flight at a time.
//
// Commands — a fixed set, nothing is evaluated:
//   ping                       liveness
//   state                      active scene, play mode, unsaved scenes
//   open-scene <asset path>    refused while playing or with unsaved scenes,
//                              so it can never discard someone's edits
//   play | stop                enter / exit play mode
//   menu <Menu/Item/Path>      EditorApplication.ExecuteMenuItem
//   maximize-game | restore-game   the Game view, so a window capture is the
//                              game and nothing else
//   capture <png path>         ScreenCapture of the Game view; written a
//                              frame later, so wait for the file
//   dump <txt path>            the active scene's hierarchy with components
//
// Temp/ is not an asset folder and is git-ignored, so the channel leaves no
// trace in the project. Editor-only by assembly (TheWaningBorder.Editor is
// includePlatforms: Editor), so it cannot ship.

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.EditorTools
{
    [InitializeOnLoad]
    public static class EditorBridge
    {
        static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "agent-bridge");
        static readonly string RequestPath = Path.Combine(Dir, "request.txt");
        static readonly string ResponsePath = Path.Combine(Dir, "response.txt");

        const double PollSeconds = 0.5;
        static double _next;

        static EditorBridge()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + PollSeconds;
            if (!File.Exists(RequestPath)) return;

            string line;
            try { line = File.ReadAllText(RequestPath).Trim(); }
            catch (IOException) { return; }   // still being written; next poll
            try { File.Delete(RequestPath); } catch (IOException) { }

            string result;
            try { result = Execute(line); }
            catch (Exception e) { result = "ERROR " + e.GetType().Name + ": " + e.Message; }

            Directory.CreateDirectory(Dir);
            File.WriteAllText(ResponsePath, result);
            Debug.Log($"[EditorBridge] {line} -> {result}");
        }

        static string Execute(string line)
        {
            int sp = line.IndexOf(' ');
            string cmd = sp < 0 ? line : line.Substring(0, sp);
            string arg = sp < 0 ? string.Empty : line.Substring(sp + 1).Trim();

            switch (cmd)
            {
                case "ping":
                    return "OK pong";

                case "state":
                    return "OK scene=" + SceneManager.GetActiveScene().name
                         + " playing=" + EditorApplication.isPlaying
                         + " unsaved=" + UnsavedScenes();

                case "open-scene":
                // "open-scene!" discards unsaved changes to the scenes
                // currently open instead of refusing. Disk is the truth the
                // build and git see; the caller is expected to have dumped
                // the in-memory hierarchy first if it wanted to compare.
                case "open-scene!":
                {
                    if (EditorApplication.isPlaying) return "ERROR in play mode; stop first";
                    bool discard = cmd.EndsWith("!", StringComparison.Ordinal);
                    string unsaved = UnsavedScenes();
                    if (unsaved.Length > 0 && !discard) return "ERROR unsaved scene(s): " + unsaved;
                    if (!arg.StartsWith("Assets/", StringComparison.Ordinal) || !File.Exists(arg))
                        return "ERROR no such scene asset: " + arg;
                    var scene = EditorSceneManager.OpenScene(arg, OpenSceneMode.Single);
                    return scene.IsValid()
                        ? "OK opened " + scene.name + (unsaved.Length > 0 ? " (discarded: " + unsaved + ")" : "")
                        : "ERROR could not open " + arg;
                }

                // A player build to the given folder, through the same
                // scene gate and options as the batch-mode PlayerBuild. Runs
                // synchronously; the reply lands when the build ends, which
                // for this project is several minutes. Builds from DISK: an
                // unsaved scene in the editor does not reach the player.
                case "build":
                {
                    if (EditorApplication.isPlaying) return "ERROR in play mode; stop first";
                    if (string.IsNullOrEmpty(arg)) return "ERROR build needs an output folder";
                    string error = TheWaningBorder.EditorTools.PlayerBuild.BuildTo(arg);
                    return error == null ? "OK built to " + arg : "ERROR " + error;
                }

                case "play":
                    if (EditorApplication.isPlaying) return "OK already playing";
                    EditorApplication.EnterPlaymode();
                    return "OK entering play mode";

                case "stop":
                    if (!EditorApplication.isPlaying) return "OK not playing";
                    EditorApplication.ExitPlaymode();
                    return "OK exiting play mode";

                case "menu":
                    return EditorApplication.ExecuteMenuItem(arg)
                        ? "OK ran " + arg
                        : "ERROR no such menu item: " + arg;

                case "maximize-game":
                case "restore-game":
                {
                    var view = GameView();
                    if (view == null) return "ERROR no Game view";
                    view.Focus();
                    view.maximized = cmd == "maximize-game";
                    return "OK " + cmd;
                }

                case "capture":
                {
                    if (string.IsNullOrEmpty(arg)) return "ERROR capture needs a path";
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arg)));
                    if (File.Exists(arg)) File.Delete(arg);
                    ScreenCapture.CaptureScreenshot(arg);
                    GameView()?.Repaint();
                    return "OK queued " + arg + " (written after the next Game view frame)";
                }

                case "dump":
                {
                    if (string.IsNullOrEmpty(arg)) return "ERROR dump needs a path";
                    var sb = new StringBuilder();
                    var scene = SceneManager.GetActiveScene();
                    foreach (var root in scene.GetRootGameObjects()) Dump(root.transform, 0, sb);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arg)));
                    File.WriteAllText(arg, sb.ToString());
                    return "OK dumped " + scene.name + " to " + arg;
                }

                default:
                    return "ERROR unknown command: " + cmd;
            }
        }

        static string UnsavedScenes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(s.name);
            }
            return sb.ToString();
        }

        static EditorWindow GameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            return type != null ? EditorWindow.GetWindow(type) : null;
        }

        static void Dump(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.gameObject.activeSelf ? "" : "(off) ").Append(t.name);
            var rt = t as RectTransform;
            if (rt != null)
                sb.Append(" [").Append(rt.rect.width.ToString("0")).Append('x')
                  .Append(rt.rect.height.ToString("0")).Append(']');
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.Append(" :").Append(c.GetType().Name);
            }
            sb.Append('\n');
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, sb);
        }
    }
}
