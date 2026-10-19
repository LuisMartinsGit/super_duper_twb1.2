// BFME2-style ability targeting mode: a ring follows the mouse across the
// terrain previewing exactly the area an ability will affect; left-click
// casts, right-click / Escape cancels. Shared by the sect god powers
// (ReligionHUD Fire) and the Reliquary's targeted abilities.
//
// An optional validator judges the point under the cursor every frame
// (docs/Design/Spells.md §6.1 — a sect power may only be aimed where the
// caster can see): an invalid point turns the ring red, and a click there
// says why and keeps aiming, spending nothing — the same misclick behaviour
// as EntityTargeting. The simulation re-checks before it charges anything.
//
// The ring is a real ground DECAL (GroundDecals), so it bends over slopes
// instead of hovering as a flat disc at one height. It was a Quad with the
// TWB/GroundTargetRing shader, which drew in the Overlay queue with
// ZTest Always and therefore floated above uneven ground.

using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.UI.World
{
    public class GroundTargeting : MonoBehaviour
    {
        private static GroundTargeting _instance;

        /// <summary>True while an ability is being aimed — input handlers
        /// (RTSInputManager, BuildCommandPannel) must stand down.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Aim-point test: null when the point is a legal aim, else
        /// the (English) reason it is not.</summary>
        public delegate string Refusal(float3 point);

        private static readonly Color InvalidTint = new Color(1f, 0.3f, 0.25f, 0.9f);

        private Action<float3> _onConfirm;
        private Refusal _refusal;
        private DecalProjector _ring;
        private float _radius;
        private Color _validColor;
        private bool _showingInvalid;
        // Swallow the click that pressed the ability button itself.
        private bool _armedThisFrame;

        /// <summary>
        /// Enter targeting mode: the ring previews <paramref name="radius"/>
        /// (world meters) in <paramref name="color"/>; left-click invokes
        /// <paramref name="onConfirm"/> with the ground point.
        /// </summary>
        public static void Begin(float radius, Color color, Action<float3> onConfirm)
            => Begin(radius, color, onConfirm, null);

        /// <summary>
        /// <see cref="Begin(float, Color, Action{float3})"/> with an aim-point
        /// test: where <paramref name="refusal"/> returns a reason the ring
        /// turns red and a click explains it and keeps aiming.
        /// </summary>
        public static void Begin(float radius, Color color, Action<float3> onConfirm, Refusal refusal)
        {
            EntityTargeting.Cancel();
            EnsureInstance();
            _instance._onConfirm = onConfirm;
            _instance._refusal = refusal;
            _instance._validColor = color;
            _instance._showingInvalid = false;
            _instance._radius = math.max(0.5f, radius);
            _instance.EnsureRing();
            GroundDecals.SetShape(_instance._ring, GroundDecals.Ring(), color);
            _instance._ring.gameObject.SetActive(true);
            _instance._armedThisFrame = true;
            IsActive = true;
        }

        public static void Cancel()
        {
            if (_instance == null) return;
            _instance._onConfirm = null;
            _instance._refusal = null;
            if (_instance._ring != null) _instance._ring.gameObject.SetActive(false);
            IsActive = false;
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("GroundTargeting");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GroundTargeting>();
        }

        /// <summary>
        /// One pooled projector for the life of the singleton — it is shown and
        /// hidden rather than rented and returned, since only one ability can
        /// be aimed at a time.
        /// </summary>
        private void EnsureRing()
        {
            if (_ring != null) return;

            _ring = GroundDecals.Rent(GroundDecals.Ring(), Color.white);
            _ring.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsActive) return;

            // Escape / right-click cancels.
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)
                || UnityEngine.Input.GetMouseButtonDown(1))
            {
                // The cancelling right-click must not also order the
                // selection to walk there (AimingInput).
                AimingInput.SwallowCurrentPress();
                Cancel();
                return;
            }

            // Follow the mouse across the terrain.
            if (TryGetMouseGround(out float3 ground))
            {
                // Centred ON the ground, not lifted above it: the decal
                // projects downward onto whatever slope is there.
                GroundDecals.Place(_ring, new Vector3(ground.x, ground.y, ground.z),
                                   _radius * 2f);

                string refusal = _refusal?.Invoke(ground);
                bool invalid = refusal != null;
                if (invalid != _showingInvalid)
                {
                    _showingInvalid = invalid;
                    GroundDecals.SetShape(_ring, GroundDecals.Ring(), invalid ? InvalidTint : _validColor);
                }

                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    if (_armedThisFrame) return; // same click that armed us
                    // A click landing on the uGUI HUD is UI interaction, not
                    // an aim confirm. (EventSystem check replaces the removed
                    // GameplayUIController.IsPointerOverHUD, 2026-07-17.)
                    if (UnityEngine.EventSystems.EventSystem.current != null
                        && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
                    if (invalid)
                    {
                        // Say why, keep aiming, spend nothing.
                        TheWaningBorder.UI.Ingame.PlayerNotificationSystem.NotifyError(
                            TheWaningBorder.Core.Localization.Loc.T(refusal));
                        _armedThisFrame = false;
                        return;
                    }
                    var cb = _onConfirm;
                    // The confirming press must not also click-select what is
                    // under the ring (AimingInput).
                    AimingInput.SwallowCurrentPress();
                    Cancel();
                    cb?.Invoke(ground);
                }
            }

            _armedThisFrame = false;
        }

        private static bool TryGetMouseGround(out float3 point)
        {
            point = default;
            var cam = TheWaningBorder.Core.PresentationState.GameplayCamera;
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
