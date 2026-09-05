using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.CameraRig
{
    /// <summary>
    /// Every tunable number the RTS camera uses. The asset is
    /// CameraController.asset, beside CameraController.cs.
    ///
    /// There are DELIBERATELY no field initialisers here. The values live in
    /// the asset and nowhere else — a C# initialiser would be a second source
    /// of truth that silently wins whenever the asset is missing a value, which
    /// is the same trap as the `if (def.hp > 0)` guard the factories used to
    /// carry.
    ///
    /// Note what is NOT here: mainCamera is a scene object the controller
    /// adopts or creates, and worldMin/worldMax are computed from the terrain
    /// at Start. Neither is a designer-tunable, so neither is config.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Camera Controller",
                     fileName = "CameraController")]
    public sealed class CameraControllerConfig : ScriptableObject, IComponentConfig
    {
        public float fieldOfView;
        public float nearClipPlane;
        public float farClipPlane;

        /// <summary>Resting distance of the camera back along the arm.</summary>
        public float cameraDistance;

        public float panSpeed;
        public float moveDamping;
        public float edgeScrollSpeed;
        public float edgeScrollBorder;

        public float zoomSpeed;
        public float minZoom;
        public float maxZoom;
        public float zoomDamping;

        public float rotationPerNotch;
        public float rotationDamping;

        public float tiltZoomedIn;
        public float tiltZoomedOut;

        public bool followTerrain;
        public float heightOffset;
        public float heightDamping;

        public float shakeMaxOffset;
        public float shakeMaxRoll;
        public float traumaDecay;
        public float shakeFrequency;
    }
}
