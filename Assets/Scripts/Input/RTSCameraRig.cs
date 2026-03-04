// RTSCameraRig.cs
// Type alias for CameraController - provides the RTSCameraRig name that other assemblies expect
// Location: Assets/Scripts/Input/RTSCameraRig.cs

using UnityEngine;
using TheWaningBorder.Input;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// RTS Camera Rig - type alias for CameraController.
    /// 
    /// This class exists because some code references "RTSCameraRig" while the main
    /// implementation is in CameraController. This provides a convenient wrapper.
    /// 
    /// Usage: World and UI assemblies can reference this type for minimap click-to-move.
    /// </summary>
    public class RTSCameraRig : MonoBehaviour
    {
        [Header("World Bounds")]
        public Vector2 worldMin = new Vector2(-125, -125);
        public Vector2 worldMax = new Vector2(125, 125);

        private CameraController _controller;

        void Awake()
        {
            // Try to find CameraController on this object or parent
            _controller = GetComponent<CameraController>();
            if (_controller == null)
                _controller = GetComponentInParent<CameraController>();
            if (_controller == null)
                _controller = FindObjectOfType<CameraController>();

            // Sync bounds from controller if found
            if (_controller != null)
            {
                worldMin = _controller.worldMin;
                worldMax = _controller.worldMax;
            }
        }

        /// <summary>
        /// Move the camera to a world position with smooth interpolation.
        /// </summary>
        public void MoveToPosition(Vector3 worldPos, bool instant = false)
        {
            if (_controller != null)
            {
                _controller.MoveToPosition(worldPos, instant);
            }
            else
            {
                // Fallback: find controller dynamically
                _controller = FindObjectOfType<CameraController>();
                if (_controller != null)
                    _controller.MoveToPosition(worldPos, instant);
            }
        }

        /// <summary>
        /// Snap the camera instantly to a world position.
        /// </summary>
        public void SnapTo(Vector3 worldPos)
        {
            MoveToPosition(worldPos, instant: true);
        }

        /// <summary>
        /// Get the current camera focus position.
        /// </summary>
        public Vector3 GetFocusPosition()
        {
            if (_controller != null)
                return _controller.GetGroundFocusPoint();
            return transform.position;
        }

        /// <summary>
        /// Get the main camera reference.
        /// </summary>
        public Camera GetCamera()
        {
            if (_controller != null)
                return _controller.mainCamera;
            return Camera.main;
        }
    }
}