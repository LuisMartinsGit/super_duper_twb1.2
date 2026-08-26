// CameraController.cs
// Modern RTS Camera System with Rig Architecture and Terrain Following
// Location: Assets/Scripts/Input/CameraController.cs

using UnityEngine;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// RTS Camera Controller using a rig architecture.
    /// 
    /// Hierarchy (auto-created if needed):
    ///   CameraRig (this script)        ← Focus point, moves in world space
    ///   └─ CameraArm (child)           ← Offsets back and up for tilt
    ///      └─ Camera (grandchild)      ← Actual camera, looks at rig center
    ///
    /// Features:
    /// - Arrow-key movement (NOT WASD — see the Update() note below)
    /// - Edge scrolling
    /// - Middle mouse drag panning
    /// - Scroll wheel zoom
    /// - Smooth damping on all axes
    ///
    /// HandleRotation (Q/E) and HandleTilt (R/F) exist below but are NEVER
    /// CALLED — the camera holds a fixed angle by design. They are kept only
    /// so the behaviour can be restored if that decision is revisited; do not
    /// document them to the player as working controls.
    /// - World bounds clamping
    /// - Terrain height following
    /// - Minimap click support (MoveToPosition)
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        
        [Header("References")]
        [Tooltip("Auto-created if null")]
        public Camera mainCamera;
        
        [Header("Movement")]
        public float keyboardSpeed = 25f;
        public float edgeScrollSpeed = 30f;
        public float edgeScrollBorder = 15f;
        public float panSpeed = 1f;
        public float moveDamping = 0.15f;
        
        [Header("Zoom")]
        public float zoomSpeed = 10f;
        public float minZoom = 15f;
        public float maxZoom = 80f;
        public float zoomDamping = 0.2f;
        
        [Header("Rotation")]
        public float rotationSpeed = 100f;
        public float mouseRotationSpeed = 0.3f;
        public float rotationDamping = 0.15f;
        
        [Header("Tilt")]
        public float tiltSpeed = 30f;
        public float minTilt = 30f;
        public float maxTilt = 75f;
        public float tiltDamping = 0.15f;
        
        [Header("Terrain Following")]
        public bool followTerrain = true;
        public float heightOffset = 2f;
        public float heightDamping = 0.1f;
        
        [Header("World Bounds")]
        // Defaults are placeholders; Start() pulls the real bounds from
        // GameSettings.MapHalfSize so the camera can reach the edge of
        // whatever map the lobby selected (largest preset goes to ±512).
        public Vector2 worldMin = new Vector2(-256, -256);
        public Vector2 worldMax = new Vector2(256, 256);
        
        [Header("Debug")]
        public bool showDebugInfo = false;

        // ═══════════════════════════════════════════════════════════════════════
        // INTERNAL STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        private Transform _arm;
        private Transform _camTransform;
        private Terrain _terrain;
        
        // Position
        private Vector3 _targetPosition;
        private Vector3 _velocity = Vector3.zero;
        private float _currentHeight;
        private float _targetHeight;
        private float _heightVelocity;
        
        // Zoom
        private float _currentZoom;
        private float _targetZoom;
        private float _zoomVelocity;

        /// <summary>
        /// Current zoom as 0..1 between minZoom and maxZoom. Exposed so UI can
        /// tell whether the player has actually used the control — the tutorial
        /// completes its "zoom and angle" step off this rather than trying to
        /// poll the scroll axis, which would only be true on the frames the
        /// wheel is moving.
        /// </summary>
        public static float ZoomNormalized { get; private set; }
        
        // Rotation (Y-axis)
        private float _currentRotation;
        private float _targetRotation;
        private float _rotationVelocity;
        
        // Tilt (X-axis pitch)
        private float _currentTilt;
        private float _targetTilt;
        private float _tiltVelocity;
        
        // Mouse pan state
        private Vector3? _lastMousePanPos;
        private bool _isRotatingWithMouse;

        // Minimap smooth pan (ease-in/out)
        private bool _isMinimapPanning;
        private Vector3 _minimapPanStart;
        private Vector3 _minimapPanTarget;
        private float _minimapPanElapsed;
        private float _minimapPanDuration;

        // ═══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════════
        
        void Start()
        {
            InitializeCameraRig();
            FindTerrain();

            // Bounds priority:
            //   1. Active Unity Terrain extents — covers hand-authored maps
            //      whose terrain may sit far from origin (MapMagic etc.) and
            //      whose size doesn't match the lobby's MapHalfSize.
            //   2. Otherwise fall back to the lobby-selected MapHalfSize box.
            //      (Procedural maps build their Terrain centred on the origin
            //      with extents ±MapHalfSize, so the two match anyway.)
            var ut = UnityEngine.Terrain.activeTerrain;
            if (ut != null && ut.terrainData != null)
            {
                var origin = ut.transform.position;
                var size = ut.terrainData.size;
                worldMin = new Vector2(origin.x, origin.z);
                worldMax = new Vector2(origin.x + size.x, origin.z + size.z);
            }
            else
            {
                int half = GameSettings.MapHalfSize;
                if (half > 0)
                {
                    worldMin = new Vector2(-half, -half);
                    worldMax = new Vector2( half,  half);
                }
            }

            // Initialize from current state
            _targetPosition = transform.position;
            _currentZoom = _targetZoom = _camTransform.localPosition.magnitude;
            _currentRotation = _targetRotation = transform.eulerAngles.y;
            _currentTilt = _targetTilt = _arm.localEulerAngles.x;
            _currentHeight = _targetHeight = transform.position.y;

            ClampPositionToBounds(ref _targetPosition);
        }

        // Locked-down camera controls — design decision: the RTS camera
        // stays at a fixed angle / fixed tilt, only its position pans.
        // Q/E rotation, R/F tilt and WASD pan are intentionally disabled.
        //  • A used to mean "pan left" AND "attack-move". With WASD
        //    disabled, A is now unambiguously attack-move (RTSInputManager).
        //  • Default Y rotation comes from GameCamera.
        //  • Scroll-wheel ZOOM re-enabled by design request (2026-08-03) —
        //    but the wheel belongs to building rotation while placement is
        //    active, so zoom pauses then (BuilderCommandPanel owns it).
        // Player still pans via arrow keys / edge-scroll / middle-mouse-drag.
        void Update()
        {
            // A cinematic (or anything else) can take control away via
            // Core.PresentationState.CameraControlSuspended. Honoured here
            // rather than by letting the caller disable this component:
            // a caller that forgets to re-enable it leaves the player
            // unable to move the camera for the rest of the match.
            if (TheWaningBorder.Core.PresentationState.CameraControlSuspended) return;

            HandleArrowKeyMovement();
            HandleEdgeScrolling();
            HandleMousePan();

            if (!TheWaningBorder.UI.Panels.BuilderCommandPanel.IsPlacingBuilding)
                HandleZoom();

            ApplySmoothMovement();
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private void InitializeCameraRig()
        {
            // Create or find camera
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    var camGO = new GameObject("Main Camera");
                    camGO.tag = "MainCamera";
                    mainCamera = camGO.AddComponent<Camera>();
                    // Solid black, not the skybox: anything drawn BEYOND the
                    // map is backdrop, and a lit sky silhouettes the map's
                    // outline against it — so the shape and extent of the
                    // terrain read clearly even where nothing has been
                    // explored. Black makes the edge indistinguishable from
                    // unexplored fog, which is also pure black.
                    mainCamera.clearFlags = CameraClearFlags.SolidColor;
                    mainCamera.backgroundColor = Color.black;
                    mainCamera.fieldOfView = 40f;
                    mainCamera.nearClipPlane = 0.1f;
                    mainCamera.farClipPlane = 5000f;
                    camGO.AddComponent<AudioListener>();
                }
            }

            // Black backdrop on the ADOPTED camera too. Map scenes ship their
            // own Camera.main, so setting this only in the creation branch
            // above would leave every real map still clearing to the skybox —
            // i.e. the change would appear to do nothing in the actual game.
            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
                mainCamera.backgroundColor = Color.black;
            }

            // Belt-and-suspenders AudioListener guarantee — if we adopted an
            // existing Camera.main (lobby scene's, for instance) it may not
            // carry an AudioListener of its own, and Unity then spams "no
            // audio listeners in the scene" every frame. Add one if neither
            // the adopted camera nor anything else in the scene has one.
            if (mainCamera != null && mainCamera.GetComponent<AudioListener>() == null)
            {
                var existing = Object.FindFirstObjectByType<AudioListener>();
                if (existing == null)
                    mainCamera.gameObject.AddComponent<AudioListener>();
            }

            _camTransform = mainCamera.transform;

            // Create arm if needed
            _arm = transform.Find("CameraArm");
            if (_arm == null)
            {
                var armGO = new GameObject("CameraArm");
                _arm = armGO.transform;
                _arm.SetParent(transform, false);
            }

            // Parent camera under arm
            bool wasReparented = false;
            if (_camTransform.parent != _arm)
            {
                _camTransform.SetParent(_arm, true);
                wasReparented = true;
            }

            // Set initial configuration if needed.
            // Only reset position to origin when the rig is newly created AND hasn't
            // been positioned yet (e.g., by FocusCameraOnHall before Start runs).
            if (wasReparented && transform.position.sqrMagnitude < 0.1f)
            {
                transform.position = Vector3.zero;
                // Default Y yaw 45° — points the player's view toward the
                // top-right (NE) corner of the map. Matches the rotated
                // minimap diamond and the spec'd "facing the top corner of
                // the map" framing. Without this, the rig defaults to
                // Quaternion.identity (north-facing) and the player starts
                // looking straight up the map instead of along the diagonal.
                transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            }

            // Always apply the RTS arm tilt + camera distance, regardless of
            // whether we just reparented. Previously this block was gated on
            // wasReparented, which meant a scene with a pre-configured Main
            // Camera (e.g. a hand-authored map saved with the camera looking
            // anywhere) would keep that camera's transform and the player
            // would start with a broken view. The RTS view is a fixed mode —
            // tilt the arm 60° down, place the camera 75m back along it,
            // every game.
            _arm.localPosition = Vector3.zero;
            _arm.localRotation = Quaternion.Euler(60f, 0f, 0f);
            // Arm length halved (75 → 37.5) per user feedback — the previous
            // distance put the camera ~65 m above ground (75·sin(60°)) which
            // read as "too high" on the larger hand-authored maps. 37.5 puts
            // it at ~32 m above, still angled down, more typical RTS feel.
            _camTransform.localPosition = new Vector3(0f, 0f, -37.5f);
            _camTransform.localRotation = Quaternion.identity;

            // Belt-and-suspenders: ensure followTerrain is on so the rig
            // pivot tracks ground height every frame (the "snapped to
            // ground + clearance + angled down" RTS behaviour). Without
            // this the pivot floats at heightOffset above world origin.
            followTerrain = true;
        }
        
        private void FindTerrain()
        {
            // Try to find the procedural terrain
            var go = GameObject.Find("ProcTerrain");
            if (go != null)
            {
                _terrain = go.GetComponent<Terrain>();
            }
            
            // Fallback to active terrain
            if (_terrain == null)
            {
                _terrain = Terrain.activeTerrain;
            }
            
            if (_terrain != null)
            {
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // TERRAIN HEIGHT
        // ═══════════════════════════════════════════════════════════════════════
        
        private float GetTerrainHeight(float x, float z)
        {
            // Try cached terrain first
            if (_terrain != null && _terrain.terrainData != null)
            {
                return _terrain.SampleHeight(new Vector3(x, 0, z)) + _terrain.transform.position.y;
            }
            
            // Find terrain if not cached
            if (_terrain == null)
            {
                FindTerrain();
                if (_terrain != null && _terrain.terrainData != null)
                {
                    return _terrain.SampleHeight(new Vector3(x, 0, z)) + _terrain.transform.position.y;
                }
            }
            
            // Fallback: raycast
            if (Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out RaycastHit hit, 1000f))
            {
                return hit.point.y;
            }
            
            return 0f;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // MOVEMENT
        // ═══════════════════════════════════════════════════════════════════════
        
        // Arrow keys only — WASD removed so A no longer collides with the
        // attack-move binding in RTSInputManager. Edge-scroll + middle-mouse
        // drag still work; the arrow keys give keyboard pan without
        // stomping on any unit command shortcut.
        private void HandleArrowKeyMovement()
        {
            Vector3 input = Vector3.zero;

            if (UnityEngine.Input.GetKey(KeyCode.UpArrow))    input.z += 1f;
            if (UnityEngine.Input.GetKey(KeyCode.DownArrow))  input.z -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.LeftArrow))  input.x -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.RightArrow)) input.x += 1f;

            if (input.sqrMagnitude > 0.01f)
            {
                _isMinimapPanning = false; // Cancel minimap pan on keyboard input

                // Move relative to camera rotation
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

                Vector3 moveDir = (forward * input.z + right * input.x).normalized;
                _targetPosition += moveDir * keyboardSpeed * Time.deltaTime;
                ClampPositionToBounds(ref _targetPosition);
            }
        }
        
        private void HandleEdgeScrolling()
        {
            if (!Application.isFocused) return;
            
            Vector3 mousePos = UnityEngine.Input.mousePosition;
            Vector3 moveDir = Vector3.zero;
            
            if (mousePos.x < edgeScrollBorder)
                moveDir.x = -1f;
            else if (mousePos.x > Screen.width - edgeScrollBorder)
                moveDir.x = 1f;
            
            if (mousePos.y < edgeScrollBorder)
                moveDir.z = -1f;
            else if (mousePos.y > Screen.height - edgeScrollBorder)
                moveDir.z = 1f;
            
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                
                Vector3 worldMove = (forward * moveDir.z + right * moveDir.x).normalized;
                _targetPosition += worldMove * edgeScrollSpeed * Time.deltaTime;
                ClampPositionToBounds(ref _targetPosition);
            }
        }
        
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
                
                _targetPosition -= (right * delta.x + forward * delta.y) * panSpeed * 0.1f;
                ClampPositionToBounds(ref _targetPosition);
            }
            else if (UnityEngine.Input.GetMouseButtonUp(2))
            {
                _lastMousePanPos = null;
            }
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // ROTATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleRotation()
        {
            if (UnityEngine.Input.GetKey(KeyCode.Q))
                _targetRotation -= rotationSpeed * Time.deltaTime;
            
            if (UnityEngine.Input.GetKey(KeyCode.E))
                _targetRotation += rotationSpeed * Time.deltaTime;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // TILT
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleTilt()
        {
            if (UnityEngine.Input.GetKey(KeyCode.R))
                _targetTilt = Mathf.Clamp(_targetTilt - tiltSpeed * Time.deltaTime, minTilt, maxTilt);
            
            if (UnityEngine.Input.GetKey(KeyCode.F))
                _targetTilt = Mathf.Clamp(_targetTilt + tiltSpeed * Time.deltaTime, minTilt, maxTilt);
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // ZOOM
        // ═══════════════════════════════════════════════════════════════════════
        
        private void HandleZoom()
        {
            float scroll = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetZoom -= scroll * zoomSpeed;
                _targetZoom = Mathf.Clamp(_targetZoom, minZoom, maxZoom);
            }
            ZoomNormalized = maxZoom > minZoom
                ? Mathf.InverseLerp(minZoom, maxZoom, _targetZoom)
                : 0f;
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // SMOOTH MOVEMENT APPLICATION
        // ═══════════════════════════════════════════════════════════════════════
        
        private void ApplySmoothMovement()
        {
            Vector3 currentPos = transform.position;
            Vector3 newPos;

            if (_isMinimapPanning)
            {
                // Ease-in/out interpolation for minimap pan
                _minimapPanElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_minimapPanElapsed / _minimapPanDuration);
                // Smoothstep: ease-in/out
                t = t * t * (3f - 2f * t);

                float newX = Mathf.Lerp(_minimapPanStart.x, _minimapPanTarget.x, t);
                float newZ = Mathf.Lerp(_minimapPanStart.z, _minimapPanTarget.z, t);
                newPos = new Vector3(newX, currentPos.y, newZ);

                // Keep target in sync so SmoothDamp doesn't fight after pan ends
                _targetPosition = _minimapPanTarget;
                _velocity = Vector3.zero;

                if (t >= 1f) _isMinimapPanning = false;
            }
            else
            {
                // Normal SmoothDamp movement
                Vector3 targetPos = new Vector3(_targetPosition.x, currentPos.y, _targetPosition.z);
                newPos = Vector3.SmoothDamp(currentPos, targetPos, ref _velocity, moveDamping);
            }
            
            // Terrain following
            if (followTerrain)
            {
                _targetHeight = GetTerrainHeight(newPos.x, newPos.z) + heightOffset;
                _currentHeight = Mathf.SmoothDamp(_currentHeight, _targetHeight, ref _heightVelocity, heightDamping);
                newPos.y = _currentHeight;
            }
            else
            {
                newPos.y = heightOffset;
            }
            
            transform.position = newPos;
            
            // Rotation (Y-axis)
            _currentRotation = Mathf.SmoothDampAngle(_currentRotation, _targetRotation, ref _rotationVelocity, rotationDamping);
            transform.rotation = Quaternion.Euler(0f, _currentRotation, 0f);
            
            // Tilt (X-axis on arm)
            _currentTilt = Mathf.SmoothDampAngle(_currentTilt, _targetTilt, ref _tiltVelocity, tiltDamping);
            _arm.localRotation = Quaternion.Euler(_currentTilt, 0f, 0f);
            
            // Zoom (camera distance)
            _currentZoom = Mathf.SmoothDamp(_currentZoom, _targetZoom, ref _zoomVelocity, zoomDamping);
            _camTransform.localPosition = new Vector3(0f, 0f, -_currentZoom);
        }

        private void ClampPositionToBounds(ref Vector3 pos)
        {
            pos.x = Mathf.Clamp(pos.x, worldMin.x, worldMax.x);
            pos.z = Mathf.Clamp(pos.z, worldMin.y, worldMax.y);
        }
        
        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Move the camera to a world position (used by minimap clicks).
        /// </summary>
        public void MoveToPosition(Vector3 worldPos, bool instant = false)
        {
            _targetPosition = new Vector3(worldPos.x, 0f, worldPos.z);
            ClampPositionToBounds(ref _targetPosition);

            if (instant)
            {
                float terrainY = followTerrain ? GetTerrainHeight(worldPos.x, worldPos.z) + heightOffset : heightOffset;
                transform.position = new Vector3(_targetPosition.x, terrainY, _targetPosition.z);
                _currentHeight = terrainY;
                _velocity = Vector3.zero;
                _heightVelocity = 0f;
            }
        }

        /// <summary>
        /// Move camera with smooth ease-in/out over a duration (for minimap clicks).
        /// </summary>
        public void MoveToPositionSmooth(Vector3 worldPos, float duration = 0.5f)
        {
            _minimapPanStart = new Vector3(transform.position.x, 0f, transform.position.z);
            _minimapPanTarget = new Vector3(worldPos.x, 0f, worldPos.z);
            ClampPositionToBounds(ref _minimapPanTarget);
            _minimapPanDuration = duration;
            _minimapPanElapsed = 0f;
            _isMinimapPanning = true;
            _velocity = Vector3.zero;
            _targetPosition = _minimapPanTarget;
        }

        /// <summary>
        /// Get the ground focus point (rig center projected to terrain).
        /// </summary>
        public Vector3 GetGroundFocusPoint()
        {
            float terrainY = GetTerrainHeight(transform.position.x, transform.position.z);
            return new Vector3(transform.position.x, terrainY, transform.position.z);
        }

        /// <summary>
        /// Screen-space ray from camera through mouse position.
        /// </summary>
        public Ray GetMouseRay()
        {
            return mainCamera.ScreenPointToRay(UnityEngine.Input.mousePosition);
        }
        
        void OnGUI()
        {
            if (!showDebugInfo) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Label($"Position: {transform.position}");
            GUILayout.Label($"Target Height: {_targetHeight:F1}");
            GUILayout.Label($"Current Height: {_currentHeight:F1}");
            GUILayout.Label($"Terrain: {(_terrain != null ? _terrain.name : "None")}");
            GUILayout.Label($"Zoom: {_currentZoom:F1}");
            GUILayout.Label($"Tilt: {_currentTilt:F1}°");
            GUILayout.EndArea();
        }
    }
}