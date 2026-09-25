using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Every tunable of the pencil ink outline. The asset is PencilOutline.asset,
    /// beside PencilOutline.cs. No field initialisers, per the CameraController
    /// pattern: the asset is the only source of truth.
    ///
    /// Distances are linear eye depth in metres; widths and jitter amount are pixels;
    /// the three noise scales are world metres, so the strokes stay anchored to the
    /// world as the camera moves.
    /// The prototype is an A/B experiment — see docs/Design/Art_Direction.md,
    /// "Experiment: pencil ink outlines".
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Pencil Outline",
                     fileName = "PencilOutline")]
    public sealed class PencilOutlineConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>The A/B switch. Read every frame, so it can be flipped live in Play mode.</summary>
        public bool enabled;

        /// <summary>Hidden/TWB/PencilOutline. Referenced here so it ships in the build.</summary>
        public Shader shader;

        /// <summary>Ink colour; alpha is the overall opacity of the lines.</summary>
        public Color inkColor;

        /// <summary>Line width at <see cref="referenceDistance"/>; lines closer than that never grow past it.</summary>
        public float lineWidth;
        public float minLineWidth;
        public float referenceDistance;

        /// <summary>Lines fade out between these two depths, so far terrain does not turn to scribble.</summary>
        public float fadeStart;
        public float fadeEnd;

        /// <summary>Relative depth jump (fraction of depth) that counts as a silhouette edge.</summary>
        public float depthThreshold;

        /// <summary>Raises the depth threshold on surfaces seen edge-on, which otherwise ink every slope.</summary>
        public float grazingCompensation;

        /// <summary>Normal difference that counts as a crease line, and how strongly creases ink next to silhouettes.</summary>
        public float normalThreshold;
        public float normalWeight;

        /// <summary>How far, in pixels, the stroke wanders, and the world size (metres) of one wander wave.</summary>
        public float jitterAmount;
        public float jitterScale;

        /// <summary>How many times a second the strokes re-draw ("boil"). 0 freezes them.</summary>
        public float boilFps;

        /// <summary>0..1 pencil-pressure breakup along a stroke, and the world length (metres) of one pressure change.</summary>
        public float strokeBreakup;
        public float strokeScale;

        /// <summary>0..1 graphite grain inside the line, and the world size (metres) of one grain.</summary>
        public float grain;
        public float grainScale;
    }
}
