// FormationInput.cs
// Formation shape: the state, the cycle key's effect, the UI entry point, and
// the push down into the order layer.
// Part of: Input/ — split out of RTSInputManager.

using UnityEngine;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Core.Commands.Issuing;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Owns the active formation shape and is the ONLY thing that writes it.
    ///
    /// AoE4 semantics: the shape persists across orders, and changing it
    /// re-slots the current selection immediately, even standing still.
    ///
    /// The shape used to be a static on RTSInputManager while the order layer
    /// carried its own <see cref="SelectionOrders.Shape"/> field that nothing
    /// ever assigned — so cycling the shape moved the banner and the UI
    /// highlight but every formation move still went out as Box, and
    /// FormationSpacing sat at 0 against the 2 m in the config asset. One
    /// owner that pushes on every change is the point of this class.
    /// </summary>
    public sealed class FormationInput
    {
        private const int ShapeCount = 4;   // Box / Line / Wedge / Staggered

        private readonly SelectionOrders _orders;

        private FormationInputConfig _cfg;
        private FormationInputConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<FormationInputConfig>());

        /// <summary>
        /// Active formation shape for group orders (AoE4 set: Box / Line /
        /// Wedge / Staggered). Read by the actions panel.
        /// </summary>
        public static FormationShape CurrentShape { get; private set; }

        // Set by the formations UI; consumed on the next input tick so the
        // re-slot runs on the owning instance (mirrors the cycle key).
        private static FormationShape _requestedShape;
        private static bool _shapeRequested;

        public FormationInput(SelectionOrders orders)
        {
            _orders = orders;

            var cfg = Cfg;
            CurrentShape = cfg != null ? cfg.startingShape : FormationShape.Box;
            _orders.FormationSpacing = cfg != null ? cfg.formationSpacing : 0f;
            _orders.Shape = CurrentShape;
        }

        /// <summary>UI entry point: set the formation shape (and re-slot the
        /// current selection) on the next input tick.</summary>
        public static void RequestShape(FormationShape shape)
        {
            _requestedShape = shape;
            _shapeRequested = true;
        }

        /// <summary>Cycle Box -> Line -> Wedge -> Staggered.</summary>
        public void Cycle()
        {
            Apply((FormationShape)(((byte)CurrentShape + 1) % ShapeCount));
            Debug.Log($"[Formation] {CurrentShape}");
        }

        /// <summary>Drain a shape requested by the UI (the actions panel),
        /// applied here so the re-slot runs exactly like the cycle key.</summary>
        public void ApplyPendingRequest()
        {
            if (!_shapeRequested) return;
            _shapeRequested = false;
            if (CurrentShape != _requestedShape)
                Apply(_requestedShape);
        }

        private void Apply(FormationShape shape)
        {
            CurrentShape = shape;
            _orders.Shape = shape;
            _orders.ReSlotSelectionInPlace();
        }
    }
}
