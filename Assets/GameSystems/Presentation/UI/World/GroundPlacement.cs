// GroundPlacement.cs
// Placing a BUILDING with an ability (Territory_Claims.md §6.9.7, 2026-11-06):
// the look and feel of WorkerCommandPanel's placement (a ghost, the build grid,
// the footprint outline, white or red, a refused click that says why and keeps
// placing) for a building a unit raises from a distance, with no worker and no
// build order. What is legal is the caster's question (the evaluator), asked
// again by the simulation before anything is charged.
//
// A separate tool rather than a mode of WorkerCommandPanel because that panel
// is the worker's build palette: it prices the placement, walks the selected
// workers there and runs through the router's territory gates, and an ability's
// building answers to none of those.

using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.UI.World
{
    public class GroundPlacement : MonoBehaviour
    {
        private static GroundPlacement _instance;

        /// <summary>True while a building is being placed by an ability; input
        /// handlers stand down (AimingInput), as for GroundTargeting.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Judge the cursor's point: returns null when <paramref name="site"/>
        /// (the point snapped to where it would really stand) is legal, else the
        /// (already localised) reason it is not.</summary>
        public delegate string Evaluator(float3 cursor, out float3 site);

        private static readonly Color InvalidRing = new Color(1f, 0.3f, 0.25f, 0.9f);

        private GameObject _ghost;
        private int2 _size;
        private Evaluator _evaluate;
        private Action<float3> _onConfirm;
        private Func<float3?> _reachCentre;
        private float _reach;
        private float _radius;
        private Color _ringColor;
        private DecalProjector _areaRing;
        private DecalProjector _reachRing;
        private bool _ghostKnown;
        private bool _ghostValid;
        private bool _ringInvalid;
        private bool _armedThisFrame;

        /// <summary>
        /// Enter placement. <paramref name="ghost"/> is a fresh instance this
        /// tool now owns. <paramref name="areaRadius"/> rings the ground the
        /// building will work on; <paramref name="reach"/> (0 = none) rings what
        /// <paramref name="reachCentre"/> can reach, and a null centre (the
        /// caster is gone) ends the placement.
        /// </summary>
        public static void Begin(GameObject ghost, int2 sizeMeters, float areaRadius, Color ringColor,
            float reach, Func<float3?> reachCentre, Evaluator evaluate, Action<float3> onConfirm)
        {
            GroundTargeting.Cancel();
            EntityTargeting.Cancel();
            Cancel();
            if (_instance == null)
            {
                var go = new GameObject("GroundPlacement");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<GroundPlacement>();
            }
            var i = _instance;
            foreach (var c in ghost.GetComponentsInChildren<Collider>()) Destroy(c);   // the cursor ray must pass through
            i._ghost = ghost;
            i._size = sizeMeters;
            i._evaluate = evaluate;
            i._onConfirm = onConfirm;
            i._reachCentre = reachCentre;
            i._reach = reach;
            i._radius = math.max(0.5f, areaRadius);
            i._ringColor = ringColor;
            i._ghostKnown = false;
            i._ringInvalid = false;
            i._armedThisFrame = true;
            i.EnsureRings();
            i._areaRing.gameObject.SetActive(true);
            GroundDecals.SetShape(i._areaRing, GroundDecals.Ring(), ringColor);
            i._reachRing.gameObject.SetActive(reach > 0f);
            GroundDecals.SetShape(i._reachRing, GroundDecals.Ring(0.02f), new Color(1f, 1f, 1f, 0.35f));
            IsActive = true;
        }

        public static void Cancel()
        {
            if (_instance == null) return;
            var i = _instance;
            if (i._ghost != null) Destroy(i._ghost);
            i._ghost = null;
            i._evaluate = null;
            i._onConfirm = null;
            i._reachCentre = null;
            if (i._areaRing != null) i._areaRing.gameObject.SetActive(false);
            if (i._reachRing != null) i._reachRing.gameObject.SetActive(false);
            if (IsActive)
            {
                BuildFootprintOutline.Hide();
                BuildGridOverlay.Hide();
            }
            IsActive = false;
        }

        private void EnsureRings()
        {
            if (_areaRing == null)
            {
                _areaRing = GroundDecals.Rent(GroundDecals.Ring(), Color.white);
                _areaRing.gameObject.SetActive(false);
            }
            if (_reachRing == null)
            {
                _reachRing = GroundDecals.Rent(GroundDecals.Ring(0.02f), Color.white);
                _reachRing.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!IsActive) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetMouseButtonDown(1))
            {
                AimingInput.SwallowCurrentPress();
                Cancel();
                return;
            }

            float3? centre = _reachCentre?.Invoke();
            if (!centre.HasValue) { Cancel(); return; }
            if (_reach > 0f)
                GroundDecals.Place(_reachRing, centre.Value, _reach * 2f);

            if (GroundTargeting.TryGetMouseGround(out float3 ground))
            {
                string refusal = _evaluate(ground, out float3 site);
                bool valid = refusal == null;

                _ghost.transform.position = site;
                if (!_ghostKnown || _ghostValid != valid)
                {
                    TheWaningBorder.UI.Ingame.WorkerCommandPanel.PaintGhost(_ghost, valid);
                    _ghostKnown = true;
                    _ghostValid = valid;
                }
                BuildGridOverlay.Show(site);
                BuildFootprintOutline.Show(site, _size, valid);
                GroundDecals.Place(_areaRing, site, _radius * 2f);
                if (_ringInvalid == valid)
                {
                    _ringInvalid = !valid;
                    GroundDecals.SetShape(_areaRing, GroundDecals.Ring(), valid ? _ringColor : InvalidRing);
                }

                if (UnityEngine.Input.GetMouseButtonDown(0) && !_armedThisFrame
                    && !(UnityEngine.EventSystems.EventSystem.current != null
                         && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
                {
                    if (!valid)
                    {
                        // Say why, keep placing, spend nothing.
                        TheWaningBorder.UI.Ingame.PlayerNotificationSystem.NotifyError(refusal);
                    }
                    else
                    {
                        var cb = _onConfirm;
                        AimingInput.SwallowCurrentPress();
                        Cancel();
                        cb?.Invoke(site);
                        return;
                    }
                }
            }
            _armedThisFrame = false;
        }
    }
}
