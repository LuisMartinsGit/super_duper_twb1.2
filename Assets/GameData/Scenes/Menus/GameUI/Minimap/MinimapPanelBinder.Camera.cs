// MinimapPanelBinder.Camera.cs
// The camera footprint rectangle, and clicks on the map: left snaps
// the camera, right orders the selection. The ORDER itself lives in
// Runtime (SelectionOrders) - a panel decides that a click happened
// and where, not what a unit may be told to do.

using System.Collections;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class MinimapPanelBinder
    {
        private void UpdateCameraViewRect()
        {
            var main = Camera.main;
            if (main == null || _viewLines == null) return;

            Vector3 p00 = RayToGround(main, new Vector2(0f, 0f));
            Vector3 p10 = RayToGround(main, new Vector2(1f, 0f));
            Vector3 p11 = RayToGround(main, new Vector2(1f, 1f));
            Vector3 p01 = RayToGround(main, new Vector2(0f, 1f));

            Vector2 px00 = WorldToOverlayLocal(p00);
            Vector2 px10 = WorldToOverlayLocal(p10);
            Vector2 px11 = WorldToOverlayLocal(p11);
            Vector2 px01 = WorldToOverlayLocal(p01);

            DrawViewLine(0, px00, px10);
            DrawViewLine(1, px10, px11);
            DrawViewLine(2, px11, px01);
            DrawViewLine(3, px01, px00);
        }

        /// <summary>World position to the view-lines' center-anchored local
        /// space. InverseLerp clamps, so a horizon-tilted camera pins its
        /// rectangle to the map edges instead of escaping the panel.</summary>
        private Vector2 WorldToOverlayLocal(Vector3 world)
        {
            float u = Mathf.InverseLerp(_boundsMin.x, _boundsMax.x, world.x);
            float v = Mathf.InverseLerp(_boundsMin.y, _boundsMax.y, world.z);
            Rect r = _overlayRect.rect;
            return new Vector2((u - 0.5f) * r.width, (v - 0.5f) * r.height);
        }

        private void DrawViewLine(int index, Vector2 start, Vector2 end)
        {
            Vector2 diff = end - start;
            var lineRect = _viewLines[index].rectTransform;
            lineRect.anchoredPosition = start;
            lineRect.sizeDelta = new Vector2(diff.magnitude, ViewLineThickness);
            lineRect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);
        }

        private static Vector3 RayToGround(Camera cam, Vector2 viewport01)
        {
            var ground = new Plane(Vector3.up, Vector3.zero);
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0f));
            if (ground.Raycast(ray, out float t)) return ray.GetPoint(t);
            Vector3 p = ray.origin + ray.direction * 1000f;
            return new Vector3(p.x, 0f, p.z);
        }

        internal void OnMinimapClick(PointerEventData eventData)
        {
            if (!TryGetWorldPosition(eventData, out float wx, out float wz)) return;

            if (eventData.button == PointerEventData.InputButton.Right)
                IssueMoveOrders(wx, wz);
            else
                TheWaningBorder.CameraRig.CameraController.FocusOn(new Vector3(wx, 0f, wz), instant: true);
        }

        private bool TryGetWorldPosition(PointerEventData eventData, out float wx, out float wz)
        {
            wx = 0f;
            wz = 0f;
            if (_overlayRect == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _overlayRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return false;

            // Local space depends on the pivot: local ∈ [-pivot*size,
            // (1-pivot)*size] on each axis. Normalize to [0..1].
            Rect r = _overlayRect.rect;
            Vector2 pivot = _overlayRect.pivot;
            float u = Mathf.Clamp01(local.x / r.width + pivot.x);
            float v = Mathf.Clamp01(local.y / r.height + pivot.y);
            wx = Mathf.Lerp(_boundsMin.x, _boundsMax.x, u);
            wz = Mathf.Lerp(_boundsMin.y, _boundsMax.y, v);
            return true;
        }

        /// <summary>
        /// Right-click on the map: send the selection there. WHICH entities may
        /// be ordered is simulation knowledge, so it lives in Runtime — this
        /// only supplies the point that was clicked.
        /// </summary>
        private void IssueMoveOrders(float wx, float wz)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            TheWaningBorder.Core.Commands.Issuing.SelectionOrders.IssueMoveToOwnedUnits(
                world.EntityManager,
                TheWaningBorder.Input.SelectionSystem.CurrentSelection,
                new float3(wx, TerrainUtility.GetHeight(wx, wz), wz),
                GameSettings.LocalPlayerFaction);
        }
    }
}
