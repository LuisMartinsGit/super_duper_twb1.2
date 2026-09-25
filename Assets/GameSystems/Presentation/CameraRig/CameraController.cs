// CameraController.cs
// RTS camera: a rig that pans, zooms, rotates on Ctrl+wheel, tilts with zoom,
// and shakes when something loud happens.

//
// Hierarchy (auto-created if needed):
//   CameraRig (this script)        <- focus point, moves in world space
//   +- CameraArm (child)           <- tilt, derived from zoom
//      +- Camera (grandchild)      <- distance back along the arm, plus shake
//
// THE CONTROL SCHEME IS THE WHOLE LIST (2026-08-28 directive):
//   wheel               zoom
//   Ctrl + wheel        rotate
//   middle-drag         pan
//   screen edge         pan
//   minimap click       centre here  (CameraController.FocusOn -> MoveToPosition)
//   tilt                NOT a control -- it is a function of zoom


using UnityEngine;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.CameraRig
{
    public class CameraController : MonoBehaviour
    {
        #region Static API
        // The rig is a singleton, built by GameBootstrap at match start. These
        // forward to it so nothing else has to hold the MonoBehaviour. Lived in
        // a separate GameCamera class until it was only forwarding.
        private static CameraController _instance;

        /// <summary>Build the rig. Called once, by GameBootstrap.</summary>
        public static void Ensure() => new GameObject("CameraRig").AddComponent<CameraController>();

        /// <summary>
        /// Drop the reference at match teardown, before the scene unloads.
        /// OnDestroy also clears it, so the static can never outlive the rig.
        /// </summary>
        public static void Cleanup() => _instance = null;

        /// <summary>
        /// Camera heading in degrees. The minimap rotates by this so its "up"
        /// is always the direction the player faces - without it the two frames
        /// of reference disagree the moment the camera is rotated.
        /// </summary>
        public static float Yaw => _instance.transform.eulerAngles.y;

        /// <summary>Centre the camera on a world position.</summary>
        public static void FocusOn(Vector3 worldPosition, bool instant = false)
            => _instance.MoveToPosition(worldPosition, instant);

        /// <summary>Shake from a world position, faded out with distance.</summary>
        public static void ShakeAt(Vector3 worldPos, float trauma, float falloff = 90f)
            => _instance.AddTraumaAt(worldPos, trauma, falloff);
        #endregion

        #region Configuration
        public Camera mainCamera;
        private CameraControllerConfig _cfg;
        private CameraControllerConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<CameraControllerConfig>());
        #endregion

        // Read from the active Terrain in Start — authored nowhere.
        private Vector2 _worldMin;
        private Vector2 _worldMax;

        private Transform _arm;
        private Transform _camTransform;

        private Vector3 _targetPosition;
        private Vector3 _velocity;
        private float _currentHeight, _targetHeight, _heightVelocity;

        private float _currentZoom, _targetZoom, _zoomVelocity;
        private float _currentRotation, _targetRotation, _rotationVelocity;

        private Vector3? _lastMousePanPos;

        private float _trauma;
        private float _shakeSeed;

        public static float ZoomNormalized { get; private set; }

        #region Helpers
        void GetWorldBounds()
        {
            var ut = UnityEngine.Terrain.activeTerrain;
            // Check if terrain exists and has data
            if (ut == null || ut.terrainData == null)
            {
                Debug.LogError("[CameraController] No active Unity Terrain!");
                enabled = false;
                return;
            }
            // Get terrain data
            var origin = ut.transform.position;
            var size = ut.terrainData.size;

            // Define world bounds
            _worldMin = new Vector2(origin.x, origin.z);
            _worldMax = new Vector2(origin.x + size.x, origin.z + size.z);

            return;
        }
        void PlaceCamera()
        {
            //Place camera at new position
            _targetPosition = transform.position;
            _currentZoom = _targetZoom = _camTransform.localPosition.magnitude;
            _currentRotation = _targetRotation = transform.eulerAngles.y;
            _currentHeight = _targetHeight = transform.position.y;
            //Clamp position to world bounds
            ClampPositionToBounds(ref _targetPosition);
            return;
        }
        private void ClampPositionToBounds(ref Vector3 pos)
        {
            pos.x = Mathf.Clamp(pos.x, _worldMin.x, _worldMax.x);
            pos.z = Mathf.Clamp(pos.z, _worldMin.y, _worldMax.y);
        }
        private void ApplySmoothMovement()
        {
            Vector3 currentPos = transform.position;
            Vector3 targetPos = new Vector3(_targetPosition.x, currentPos.y, _targetPosition.z);
            Vector3 newPos = Vector3.SmoothDamp(currentPos, targetPos, ref _velocity, Cfg.moveDamping);

            if (Cfg.followTerrain)
            {
                float sampled = TerrainUtility.GetHeight(newPos.x, newPos.z) + Cfg.heightOffset;
                // Hold the last good height rather than chase a bad sample.
                // TerrainUtility guards its own result, but the rig is the one
                // place where a single NaN is unrecoverable: Unity refuses the
                // assignment, transform.position keeps the NaN it already has,
                // and next frame it is read back in as currentPos and smoothed
                // into everything downstream. Belt and braces on purpose.
                if (!float.IsNaN(sampled) && !float.IsInfinity(sampled))
                    _targetHeight = sampled;
                _currentHeight = Mathf.SmoothDamp(_currentHeight, _targetHeight,
                                                  ref _heightVelocity, Cfg.heightDamping);
                newPos.y = _currentHeight;
            }
            else
            {
                newPos.y = Cfg.heightOffset;
            }
            if (float.IsNaN(newPos.x) || float.IsNaN(newPos.y) || float.IsNaN(newPos.z))
            {
                // Something upstream went non-finite. Re-seed from a position
                // we know is real instead of pushing the NaN into the
                // transform, where it would stick permanently.
                _velocity = Vector3.zero;
                _heightVelocity = 0f;
                _currentHeight = _targetHeight = TerrainUtility.GetHeight(
                    _targetPosition.x, _targetPosition.z) + Cfg.heightOffset;
                newPos = new Vector3(_targetPosition.x, _currentHeight, _targetPosition.z);
            }
            transform.position = newPos;

            // Yaw
            _currentRotation = Mathf.SmoothDampAngle(_currentRotation, _targetRotation,
                                                     ref _rotationVelocity, Cfg.rotationDamping);
            transform.rotation = Quaternion.Euler(0f, _currentRotation, 0f);

            // Zoom
            _currentZoom = Mathf.SmoothDamp(_currentZoom, _targetZoom, ref _zoomVelocity, Cfg.zoomDamping);

            // TILT
            float zoom01 = Cfg.maxZoom > Cfg.minZoom
                ? Mathf.InverseLerp(Cfg.minZoom, Cfg.maxZoom, _currentZoom)
                : 0f;
            _arm.localRotation = Quaternion.Euler(Mathf.Lerp(Cfg.tiltZoomedIn, Cfg.tiltZoomedOut, zoom01), 0f, 0f);

            // Camera distance, plus shake.
            EvaluateShake(out var shakeOffset, out float shakeRoll);
            _camTransform.localPosition = new Vector3(0f, 0f, -_currentZoom) + shakeOffset;
            _camTransform.localRotation = Quaternion.Euler(0f, 0f, shakeRoll);
        }
        public void MoveToPosition(Vector3 worldPos, bool instant = false)
        {
            _targetPosition = new Vector3(worldPos.x, 0f, worldPos.z);
            ClampPositionToBounds(ref _targetPosition);

            if (instant)
            {
                float terrainY = Cfg.followTerrain
                    ? TerrainUtility.GetHeight(worldPos.x, worldPos.z) + Cfg.heightOffset
                    : Cfg.heightOffset;
                transform.position = new Vector3(_targetPosition.x, terrainY, _targetPosition.z);
                _currentHeight = terrainY;
                _velocity = Vector3.zero;
                _heightVelocity = 0f;
            }
        }

        #endregion

        #region Lifecycle
        void Awake() => _instance = this;
        void OnDestroy() => _instance = null;

        void Start()
        {
            _shakeSeed = Random.value * 1000f;
            InitializeCameraRig();
            GetWorldBounds();
            PlaceCamera();
        }
        void Update()
        {
            HandleMousePan();
            HandleEdgeScrolling();
            // Scroll wheel behavior is overruled when placing a building.
            // Read fresh each frame: as a field initialiser this was captured
            // once at construction and never changed again.
            if (!TheWaningBorder.Core.PresentationState.PlacingBuilding)
                {HandleWheel();}
            ApplySmoothMovement();
        }
        #endregion

        private void InitializeCameraRig()
        {
            EvictSceneCameras();

            // The rig owns both objects: a camera sitting back along an arm that
            // tilts with zoom.
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            mainCamera = camGO.AddComponent<Camera>();
            mainCamera.fieldOfView = Cfg.fieldOfView;
            mainCamera.nearClipPlane = Cfg.nearClipPlane;
            mainCamera.farClipPlane = Cfg.farClipPlane;
            camGO.AddComponent<AudioListener>();

            // Solid black, not the skybox: anything beyond the map is backdrop, and
            // a lit sky silhouettes the terrain's outline against it.
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;

            // Publish downward: in-world visuals frame themselves against the
            // gameplay camera and must not name the input layer to find it.
            // Published HERE, not from CameraController.Ensure, which ran before
            // Start had created the camera and so published null forever.
            TheWaningBorder.Core.PresentationState.MainCamera = mainCamera;

            _arm = new GameObject("CameraArm").transform;
            _arm.SetParent(transform, false);

            _camTransform = camGO.transform;
            _camTransform.SetParent(_arm, false);
            _camTransform.localPosition = new Vector3(0f, 0f, -Cfg.cameraDistance);
        }

        /// <summary>
        /// Destroy any camera the SCENE shipped, before the rig makes its own.
        ///
        /// No gameplay scene may ship a camera (2026-09-01) and 35 of them were
        /// stripped then — but a map GENERATOR written afterwards calls
        /// EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects), and
        /// Unity's default objects include a Main Camera. Veilmarch shipped one
        /// again from 2026-09-13.
        ///
        /// The cost is not cosmetic. Two cameras both tagged MainCamera make
        /// <c>Camera.main</c> undefined, and on those maps it resolved to the
        /// SCENE's camera — a fixed transform at (0, 1, -10) that sees nothing
        /// the player sees. Every right-click ray was cast from it, missed the
        /// terrain, and the order was dropped: move orders could not be issued
        /// at all, silently. Input reads PresentationState.GameplayCamera now,
        /// which is immune by identity, but the duplicate also gives two
        /// rendering cameras and two AudioListeners, so it goes.
        ///
        /// Loud on purpose: the fix belongs in the generator, not here.
        /// </summary>
        private static void EvictSceneCameras()
        {
            var strays = FindObjectsByType<Camera>(FindObjectsInactive.Include,
                                                   FindObjectsSortMode.None);
            for (int i = 0; i < strays.Length; i++)
            {
                var cam = strays[i];
                if (cam == null) continue;
                Debug.LogError(
                    $"[CameraController] The scene ships a camera (\"{cam.name}\", tag " +
                    $"\"{cam.tag}\") and no gameplay scene may — the rig builds its own. " +
                    "Destroying it. A map generator using NewSceneSetup.DefaultGameObjects " +
                    "is the usual source; strip the camera there.", cam.gameObject);
                var listener = cam.GetComponent<AudioListener>();
                if (listener != null) Destroy(listener);
                Destroy(cam.gameObject);
            }
        }

        #region Input
        private void HandleMousePan()
        {
            if (UnityEngine.Input.GetMouseButtonDown(2))
            {
                _lastMousePanPos = UnityEngine.Input.mousePosition;
            }
            else if (UnityEngine.Input.GetMouseButton(2) && _lastMousePanPos.HasValue)
            {
                Vector3 delta = UnityEngine.Input.mousePosition - _lastMousePanPos.Value;
                _lastMousePanPos = UnityEngine.Input.mousePosition;

                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

                _targetPosition -= (right * delta.x + forward * delta.y) * Cfg.panSpeed * 0.1f;
                ClampPositionToBounds(ref _targetPosition);
            }
            else if (UnityEngine.Input.GetMouseButtonUp(2))
            {
                _lastMousePanPos = null;
            }
        }
        private void HandleEdgeScrolling()
        {
            if (!Application.isFocused) return;
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            Vector3 mousePos = UnityEngine.Input.mousePosition;

            // A cursor BEYOND the window still pans: out-of-window coordinates
            // fall in the < border / > size-border bands below, which is the
            // classic RTS behaviour — overshooting the edge must not stop the
            // pan (a bounds guard here was tried 2026-09-01 and reverted: it
            // froze the camera the moment the cursor left the game panel).
            // Application.isFocused above covers the genuinely-elsewhere case.
            Vector3 dir = Vector3.zero;
            if (mousePos.x < Cfg.edgeScrollBorder) dir.x = -1f;
            else if (mousePos.x > Screen.width - Cfg.edgeScrollBorder) dir.x = 1f;
            if (mousePos.y < Cfg.edgeScrollBorder) dir.z = -1f;
            else if (mousePos.y > Screen.height - Cfg.edgeScrollBorder) dir.z = 1f;

            if (dir.sqrMagnitude <= 0.01f) return;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

            _targetPosition += (forward * dir.z + right * dir.x).normalized
                             * Cfg.edgeScrollSpeed * Time.deltaTime;
            ClampPositionToBounds(ref _targetPosition);
        }
        private void HandleWheel()
        {
            float scroll = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl)
                         || UnityEngine.Input.GetKey(KeyCode.RightControl);

                if (ctrl)
                {
                    // Wheel notches are ~0.1 per detent; scale to whole degrees.
                    _targetRotation += scroll * 10f * Cfg.rotationPerNotch;
                }
                else
                {
                    _targetZoom = Mathf.Clamp(_targetZoom - scroll * Cfg.zoomSpeed, Cfg.minZoom, Cfg.maxZoom);
                }
            }

            ZoomNormalized = Cfg.maxZoom > Cfg.minZoom
                ? Mathf.InverseLerp(Cfg.minZoom, Cfg.maxZoom, _targetZoom)
                : 0f;
        }

        #endregion

        #region Camera Shake
        public void AddTrauma(float amount)
            => _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
        public void AddTraumaAt(Vector3 worldPos, float amount, float falloff = 90f)
        {
            float d = Vector3.Distance(
                new Vector3(worldPos.x, 0f, worldPos.z),
                new Vector3(transform.position.x, 0f, transform.position.z));
            if (d >= falloff) return;
            AddTrauma(amount * (1f - d / falloff));
        }
        private void EvaluateShake(out Vector3 offset, out float roll)
        {
            offset = Vector3.zero;
            roll = 0f;
            if (_trauma <= 0f) return;

            float shake = _trauma * _trauma;
            float t = Time.unscaledTime * Cfg.shakeFrequency;

            offset = new Vector3(
                (Mathf.PerlinNoise(_shakeSeed, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(_shakeSeed + 17f, t) - 0.5f) * 2f,
                0f) * (shake * Cfg.shakeMaxOffset);

            roll = (Mathf.PerlinNoise(_shakeSeed + 41f, t) - 0.5f) * 2f * shake * Cfg.shakeMaxRoll;

            // Unscaled: a shake should still play at 0.25x speed or paused,
            // and it is presentation, so it cannot affect the simulation.
            _trauma = Mathf.Max(0f, _trauma - Cfg.traumaDecay * Time.unscaledDeltaTime);
        }
        #endregion
    }
}