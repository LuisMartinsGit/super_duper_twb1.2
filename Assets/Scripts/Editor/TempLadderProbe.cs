using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    /// <summary>Throwaway: launches the Garrison / Archery Range ladder scenario and frames the camera for captures.</summary>
    public static class TempLadderProbe
    {
        [MenuItem("Waning Border/Debug/Temp: Launch Garrison Range Ladder")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Ladder] enter play mode first"); return; }
            TheWaningBorder.UI.Menus.LoadingScreen.Show(ScenarioCatalog.Prepare(ScenarioType.GarrisonRangeLadder));
        }

        [MenuItem("Waning Border/Debug/Temp: Ladder Camera Wide")]
        public static void CameraWide() => SetCamera(new Vector3(0f, 0f, 6f), 0f, 62f);

        [MenuItem("Waning Border/Debug/Temp: Ladder Camera Angled")]
        public static void CameraAngled() => SetCamera(new Vector3(0f, 0f, 0f), 30f, 38f);

        [MenuItem("Waning Border/Debug/Temp: Ladder Camera Garrison Row")]
        public static void CameraGarrison() => SetCamera(new Vector3(0f, 0f, 5f), 20f, 30f);

        [MenuItem("Waning Border/Debug/Temp: Ladder Camera Range Row")]
        public static void CameraRange() => SetCamera(new Vector3(0f, 0f, -5f), 20f, 30f);

        static void SetCamera(Vector3 focus, float yaw, float zoom)
        {
            var cam = Object.FindFirstObjectByType<TheWaningBorder.CameraRig.CameraController>();
            if (cam == null) { Debug.LogWarning("[Ladder] no camera rig"); return; }
            var t = cam.GetType();
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var n in new[] { "_currentZoom", "_targetZoom" }) t.GetField(n, F)?.SetValue(cam, zoom);
            foreach (var n in new[] { "_currentRotation", "_targetRotation" }) t.GetField(n, F)?.SetValue(cam, yaw);
            cam.MoveToPosition(focus, true);
        }
    }
}
