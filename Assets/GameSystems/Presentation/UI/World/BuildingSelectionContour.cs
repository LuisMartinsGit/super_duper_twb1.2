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
        /// <summary>Ring band width for round buildings, as a fraction of the radius.</summary>
        const float RingThickness = 0.14f;

        /// <summary>
        /// Grown slightly past the footprint so the contour reads as being
        /// AROUND the building rather than painted onto its base.
        /// </summary>
        const float Margin = 0.6f;

        const float Alpha = 0.85f;

        /// <summary>
        /// Fallback when a building carries neither BuildingSize nor a usable
        /// Radius. Matches BuildingSizeConfig's own unknown-id default (8 m,
        /// i.e. 4x4 cells) so an unrecognised building is outlined at the size
        /// the rest of the game already gives it.
        /// </summary>
        const float DefaultSize = 8f;

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

                var xf = _em.GetComponentData<LocalTransform>(e);
                var pos = xf.Position;
                Footprint(e, out float width, out float depth);

                // Yaw with the entity. Most buildings stand at identity, but a
                // wall cell is a 1 x 3 m footprint turned to its segment's
                // bearing — an axis-aligned box under a diagonal wall outlined
                // a square of ground the wall only crossed.
                float yaw = ((Quaternion)xf.Rotation).eulerAngles.y;

                // A wall hub is a round tower: ring it, at its Radius, rather
                // than boxing its bounding square.
                bool round = _em.HasComponent<WallHubTag>(e);
                var shape = round ? GroundDecals.Ring(RingThickness) : GroundDecals.RectContour(Thickness);
                if (round && _em.HasComponent<Radius>(e))
                    width = depth = _em.GetComponentData<Radius>(e).Value * 2f + Margin;

                if (!_contours.TryGetValue(e, out var decal))
                {
                    decal = GroundDecals.Rent(shape, OwnerColor(e));
                    _contours[e] = decal;
                }
                else
                {
                    // Retinted every frame: ownership can change under a
                    // standing selection (conversion, capture).
                    GroundDecals.SetShape(decal, shape, OwnerColor(e));
                }

                GroundDecals.Place(decal, new Vector3(pos.x, pos.y, pos.z), width, depth, yaw);
            }
        }

        void OnDestroy()
        {
            foreach (var kv in _contours) GroundDecals.Return(kv.Value);
            _contours.Clear();
        }

        /// <summary>
        /// Footprint in metres.
        ///
        /// BuildingSize IS ALREADY METRES — it is filled straight from
        /// BuildingSizeConfig.GetSize, and the placement validator, the nav
        /// stamps, the terrain flatten and the visual footprint fit all read it
        /// that way. This used to multiply it by BuildGrid.CellSize on the
        /// belief that it held CELLS, which drew every contour at DOUBLE the
        /// building: a 4 m Hut got an 8.6 m box, so neighbouring buildings'
        /// outlines overlapped each other and none of them matched the thing
        /// they were under.
        ///
        /// The belief came from the component's own doc comment, which still
        /// said "grid cells" from before the 2 m build grid existed. That
        /// comment is corrected too — a wrong unit in a doc comment is how this
        /// gets rewritten a third time.
        /// </summary>
        void Footprint(Entity e, out float width, out float depth)
        {
            if (_em.HasComponent<BuildingSize>(e))
            {
                var s = _em.GetComponentData<BuildingSize>(e);
                width = s.Width + Margin;
                depth = s.Height + Margin;
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
