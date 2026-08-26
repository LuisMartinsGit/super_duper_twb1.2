// VeilBreakInputSystem.cs
// PHASE 4 — the player breaks the frontier. Holding LEFT ALT and left-clicking
// the veil punches a break at the cursor: it just appends a VeilBreakRequest
// (the same funnel the debug overlay uses), so VeilFieldSystem clears the field
// there + stamps a regrow cooldown, the crystals vanish (they only mirror the
// field), and the chunk/mesh "swap" happens automatically via reclassification.
//
// Bound to Alt+Click on purpose: it stays clear of the normal RTS left/right
// click commands, so wiring in this demo can't hijack unit control. Swap the
// binding (or route it through a real ability/command) when you design the
// player-facing break — the field write is the only contract.

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Presentation;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VeilBreakInputSystem : SystemBase
    {
        private EntityQuery _fieldQuery;

        protected override void OnCreate()
        {
            _fieldQuery = GetEntityQuery(typeof(VeilField));
            RequireForUpdate<VeilField>();
        }

        protected override void OnUpdate()
        {
            // Dev tool, single-player only: the break request mutates the veil
            // field on the clicking peer alone, and the field drives
            // precipitation spawns — an unreplicated break forks the match.
            if (GameSettings.IsMultiplayer) return;
            if (!UnityEngine.Input.GetKey(KeyCode.LeftAlt)) return;
            if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
            if (!TryCursorToWorld(out float wx, out float wz)) return;

            var entity = _fieldQuery.GetSingletonEntity();
            if (!EntityManager.HasBuffer<VeilBreakRequest>(entity)) return;

            EntityManager.GetBuffer<VeilBreakRequest>(entity).Add(new VeilBreakRequest
            {
                Position = new float2(wx, wz),
                Radius = DefaultBreakRadius,
            });
            VeilDebris.Burst(new Vector3(wx, 0f, wz), DefaultBreakRadius);
        }

        private static bool TryCursorToWorld(out float wx, out float wz)
        {
            wx = wz = 0f;
            var cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);

            if (Physics.Raycast(ray, out var hit, 5000f))
            {
                wx = hit.point.x; wz = hit.point.z; return true;
            }
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            var p = ray.origin + ray.direction * t;
            wx = p.x; wz = p.z; return true;
        }
    }
}
