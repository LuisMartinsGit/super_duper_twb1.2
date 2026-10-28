// CameraYawProbe.cs
// Editor-only: turn the camera rig by 45 degrees from a menu item, so the
// minimap's rotation can be captured over the EditorBridge without input.
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class CameraYawProbe
    {
        [MenuItem("Waning Border/Debug/Camera Yaw +45")]
        public static void Yaw45()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[CameraYawProbe] play mode only"); return; }
            var rig = Object.FindFirstObjectByType<TheWaningBorder.CameraRig.CameraController>();
            if (rig == null) { Debug.LogWarning("[CameraYawProbe] no CameraController"); return; }
            // The controller re-applies its own yaw every frame, so turn
            // the state it integrates (private fields), not the transform.
            var t = rig.GetType();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cur = t.GetField("_currentRotation", flags);
            var tgt = t.GetField("_targetRotation", flags);
            if (cur == null || tgt == null) { Debug.LogWarning("[CameraYawProbe] rotation fields not found"); return; }
            float yaw = (float)cur.GetValue(rig) + 45f;
            cur.SetValue(rig, yaw); tgt.SetValue(rig, yaw);
            Debug.Log("[CameraYawProbe] yaw set to " + yaw);
        }
    }
}
