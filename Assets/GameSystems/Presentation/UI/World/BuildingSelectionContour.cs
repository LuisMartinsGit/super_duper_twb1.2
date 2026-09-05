// BuildingSelectionContour.cs
// A footprint-shaped contour on the ground under every selected building.
//
// Buildings had no selection feedback at all: UnitIndicatorSystem queries
// UnitTag, so only units got a ring, and a selected building was told apart
// only by the panels that lit up. This draws the missing half.
//
// It is a projected DECAL, so the contour follows whatever the building is
// standing on instead of being a flat rectangle floating at one height, and it
// is footprint-SHAPED rather than a circle — a 5x2 wall segment and a 3x3 hall
// read as themselves.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Input;
using TheWaningBorder.Rendering;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    /// <summary>
    /// Draws a ground contour under each selected building, tinted by owner.
    /// </summary>
    public sealed class BuildingSelectionContour : MonoBehaviour
    {
        /// <summary>Contour band width, as a fraction of the footprint.</summary>
        const float Thickness = 0.10f;

        /// <summary>
        /// Grown slightly past the footprint so the contour reads as being
        /// AROUND the building rather than painted onto its base.
        /// </summary>
        const float Margin = 0.6f;

        const float Alpha = 0.85f;

        /// <summary>Fallback when a building carries no BuildingSize.</summary>
        const float DefaultSize = 4f;

        EntityManager _em;
        readonly Dictionary<Entity, DecalProjector> _contours = new();
        readonly List<Entity> _stale = new();

        void Awake() => _em = EntityWorld.DefaultGameObjectInjectionWorld.EntityManager;

        void LateUpdate()
        {
            var selection = SelectionSystem.CurrentSelection;

            // Drop contours whose building is gone or no longer selected. Done
            // first so a reselect in the same frame reuses the projector.
            _stale.Clear();
            foreach (var kv in _contours)
            {
                bool live = _em.Exists(kv.Key)
                         && selection != null && selection.Contains(kv.Key);
                if (!live) _stale.Add(kv.Key);
            }
            foreach (var e in _stale)
            {
                GroundDecals.Return(_contours[e]);
                _contours.Remove(e);
            }

            if (selection == null) return;

            for (int i = 0; i < selection.Count; i++)
            {
                var e = selection[i];
                if (!_em.Exists(e) || !_em.HasComponent<BuildingTag>(e)) continue;
                if (!_em.HasComponent<LocalTransform>(e)) continue;

                var pos = _em.GetComponentData<LocalTransform>(e).Position;
                Footprint(e, out float width, out float depth);

                if (!_contours.TryGetValue(e, out var decal))
                {
                    decal = GroundDecals.Rent(GroundDecals.RectContour(Thickness), OwnerColor(e));
                    _contours[e] = decal;
                }
                else
                {
                    // Retinted every frame: ownership can change under a
                    // standing selection (conversion, capture).
                    GroundDecals.SetShape(decal, GroundDecals.RectContour(Thickness), OwnerColor(e));
                }

                GroundDecals.Place(decal, new Vector3(pos.x, pos.y, pos.z), width, depth);
            }
        }

        void OnDestroy()
        {
            foreach (var kv in _contours) GroundDecals.Return(kv.Value);
            _contours.Clear();
        }

        /// <summary>
        /// Footprint in metres. BuildingSize is in grid CELLS, so it goes
        /// through BuildGrid.CellSize rather than being assumed to be metres.
        /// </summary>
        void Footprint(Entity e, out float width, out float depth)
        {
            if (_em.HasComponent<BuildingSize>(e))
            {
                var s = _em.GetComponentData<BuildingSize>(e);
                width = s.Width * BuildGrid.CellSize + Margin;
                depth = s.Height * BuildGrid.CellSize + Margin;
                return;
            }

            if (_em.HasComponent<Radius>(e))
            {
                float r = _em.GetComponentData<Radius>(e).Value;
                if (r > 0.01f)
                {
                    width = depth = r * 2f + Margin;
                    return;
                }
            }

            width = depth = DefaultSize;
        }

        Color OwnerColor(Entity e)
        {
            if (!_em.HasComponent<FactionTag>(e)) return new Color(0.8f, 0.8f, 0.8f, Alpha);

            var c = FactionColors.Get(_em.GetComponentData<FactionTag>(e).Value);
            c.a = Alpha;
            return c;
        }
    }
}
