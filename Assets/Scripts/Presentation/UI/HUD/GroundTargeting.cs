// BFME2-style ability targeting mode: a glowing ring decal follows the
// mouse across the terrain previewing exactly the area an ability will
// affect; left-click casts, right-click / Escape cancels. Shared by the
// sect god powers (ReligionHUD Fire) and the Reliquary's targeted
// abilities. The ring shader (TWB/GroundTargetRing) renders in the
// Overlay queue with ZTest Always, so it stays visible on top of the
// fog-of-war overlay.

using System;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.HUD
{
    public class GroundTargeting : MonoBehaviour
    {
        private static GroundTargeting _instance;

        /// <summary>True while an ability is being aimed — input handlers
        /// (RTSInputManager, BuildCommandPannel) must stand down.</summary>
        public static bool IsActive { get; private set; }

        private Action<float3> _onConfirm;
        private GameObject _ring;
        private Material _ringMat;
        private float _radius;
        // Swallow the click that pressed the ability button itself.
        private bool _armedThisFrame;

        /// <summary>
        /// Enter targeting mode: the ring previews <paramref name="radius"/>
        /// (world meters) in <paramref name="color"/>; left-click invokes
        /// <paramref name="onConfirm"/> with the ground point.
        /// </summary>
        public static void Begin(float radius, Color color, Action<float3> onConfirm)
        {
            EnsureInstance();
            _instance._onConfirm = onConfirm;
            _instance._radius = math.max(0.5f, radius);
            _instance.EnsureRing();
            _instance._ringMat.SetColor("_Color", color);
            // The quad's geometry spans its LOCAL X/Y plane (the 90° X
            // rotation is applied after scaling), so the diameter goes on
            // X and Y — scaling Z stretches nothing and leaving Y at 1
            // squashed the ring into a strip.
            _instance._ring.transform.localScale =
                new Vector3(_instance._radius * 2f, _instance._radius * 2f, 1f);
            _instance._ring.SetActive(true);
            _instance._armedThisFrame = true;
            IsActive = true;
        }

        public static void Cancel()
        {
            if (_instance == null) return;
            _instance._onConfirm = null;
            if (_instance._ring != null) _instance._ring.SetActive(false);
            IsActive = false;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("GroundTargeting");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GroundTargeting>();
        }

        private void EnsureRing()
        {
            if (_ring != null) return;

            _ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _ring.name = "GroundTargetRing";
            UnityEngine.Object.Destroy(_ring.GetComponent<Collider>());
            _ring.transform.SetParent(transform, false);
            // Quad faces +Z; lay it flat on the ground.
            _ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var shader = Shader.Find("TWB/GroundTargetRing");
            _ringMat = shader != null
                ? new Material(shader)
                : new Material(Shader.Find("Sprites/Default"));
            var mr = _ring.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _ringMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _ring.SetActive(false);
        }

        private void Update()
        {
            if (!IsActive) return;

            // Escape / right-click cancels.
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)
                || UnityEngine.Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            // Follow the mouse across the terrain.
            if (TryGetMouseGround(out float3 ground))
            {
                _ring.transform.position = new Vector3(ground.x, ground.y + 0.25f, ground.z);

                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    if (_armedThisFrame) return; // same click that armed us
                    // A click landing on the uGUI HUD is UI interaction, not
                    // an aim confirm. (EventSystem check replaces the removed
                    // GameplayUIController.IsPointerOverHUD, 2026-07-17.)
                    if (UnityEngine.EventSystems.EventSystem.current != null
                        && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
                    var cb = _onConfirm;
                    Cancel();
                    cb?.Invoke(ground);
                }
            }

            _armedThisFrame = false;
        }

        private static bool TryGetMouseGround(out float3 point)
        {
            point = default;
            var cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            // Physics first (terrain collider), then analytic fallback:
            // march the ray against the heightfield.
            if (Physics.Raycast(ray, out var hit, 2000f))
            {
                point = new float3(hit.point.x,
                    TerrainUtility.GetHeight(hit.point.x, hit.point.z), hit.point.z);
                return true;
            }

            for (float t = 0f; t < 800f; t += 4f)
            {
                Vector3 p = ray.origin + ray.direction * t;
                float h = TerrainUtility.GetHeight(p.x, p.z);
                if (p.y <= h)
                {
                    point = new float3(p.x, h, p.z);
                    return true;
                }
            }
            return false;
        }
    }
}
