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
    /// - WASD keyboard movement
    /// - Edge scrolling
    /// - Middle mouse drag panning
    /// - Scroll wheel zoom
    /// - Q/E keyboard rotation
    /// - R/F tilt control
    /// - Smooth damping on all axes
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
            
            // Initialize from current state
            _targetPosition = transform.position;
            _currentZoom = _targetZoom = _camTransform.localPosition.magnitude;
            _currentRotation = _targetRotation = transform.eulerAngles.y;
            _currentTilt = _targetTilt = _arm.localEulerAngles.x;
            _currentHeight = _targetHeight = transform.position.y;

            ClampPositionToBounds(ref _targetPosition);
        }

        void Update()
        {
            HandleKeyboardMovement();
            HandleEdgeScrolling();
            HandleMousePan();
            HandleRotation();
            HandleTilt();
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
                    mainCamera.clearFlags = CameraClearFlags.Skybox;
                    mainCamera.fieldOfView = 40f;
                    mainCamera.nearClipPlane = 0.1f;
                    mainCamera.farClipPlane = 5000f;
                    camGO.AddComponent<AudioListener>();
                }
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
                // Position rig at origin
                transform.position = Vector3.zero;
                transform.rotation = Quaternion.identity;
            }

            // Always ensure arm and camera hierarchy is configured
            if (wasReparented)
            {
                // Tilt arm downward
                _arm.localPosition = Vector3.zero;
                _arm.localRotation = Quaternion.Euler(55f, 0f, 0f);

                // Position camera back from arm
                _camTransform.localPosition = new Vector3(0f, 0f, -40f);
                _camTransform.localRotation = Quaternion.identity;
            }
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
                Debug.Log($"[CameraController] Found terrain: {_terrain.name}");
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
        
        private void HandleKeyboardMovement()
        {
            Vector3 input = Vector3.zero;
            
            if (UnityEngine.Input.GetKey(KeyCode.W)) input.z += 1f;
            if (UnityEngine.Input.GetKey(KeyCode.S)) input.z -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.A)) input.x -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.D)) input.x += 1f;
            
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