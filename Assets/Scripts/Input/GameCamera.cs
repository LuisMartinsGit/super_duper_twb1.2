// GameCamera.cs
// Static helper for game camera initialization
// Location: Assets/Scripts/Input/GameCamera.cs

using UnityEngine;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Static helper for ensuring game camera exists and is properly configured.
    /// Used by GameBootstrap to initialize the camera system.
    /// </summary>
    public static class GameCamera
    {
        private static CameraController _controller;

        /// <summary>
        /// Get the active camera controller, if any.
        /// </summary>
        public static CameraController Controller => _controller;

        /// <summary>
        /// Get the main camera used for gameplay.
        /// </summary>
        public static Camera MainCamera => _controller != null ? _controller.mainCamera : Camera.main;

        /// <summary>
        /// Ensure the game camera system is initialized.
        /// Creates CameraRig with CameraController if it doesn't exist.
        /// </summary>
        public static void Ensure()
        {
            // Already have a controller?
            if (_controller != null)
            {
                Debug.Log("[GameCamera] Camera controller already exists");
                return;
            }

            // Find existing controller
            _controller = Object.FindFirstObjectByType<CameraController>();
            if (_controller != null)
            {
                Debug.Log("[GameCamera] Found existing camera controller");
                return;
            }

            // Create new camera rig
            Debug.Log("[GameCamera] Creating new camera rig...");
            
            var rigGO = new GameObject("CameraRig");
            _controller = rigGO.AddComponent<CameraController>();
            
            // Position camera at map center (or origin)
            rigGO.transform.position = new Vector3(0, 0, 0);

            // Make it persist through scene loads if needed
            // Object.DontDestroyOnLoad(rigGO); // Uncomment if camera should persist

            Debug.Log("[GameCamera] Camera rig created successfully");
        }

        /// <summary>
        /// Move camera to focus on a world position.
        /// </summary>
        public static void FocusOn(Vector3 worldPosition, bool instant = false)
        {
            if (_controller != null)
            {
                _controller.MoveToPosition(worldPosition, instant);
            }
        }

        /// <summary>
        /// Get current camera focus position.
        /// </summary>
        public static Vector3 GetFocusPosition()
        {
            if (_controller != null)
            {
                return _controller.transform.position;
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Clean up camera references.
        /// Called when leaving game scene.
        /// </summary>
        public static void Cleanup()
        {
            _controller = null;
        }
    }
}