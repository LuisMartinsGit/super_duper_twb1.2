using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="UnitAnimationSync"/> and the render settings
    /// every animated unit view is given at spawn. The asset is
    /// UnitAnimationSync.asset, beside UnitAnimationSync.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    ///
    /// Rendering-only. Nothing here is read by the simulation, so no value can
    /// desync a lockstep match.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Unit Animation Sync",
                     fileName = "UnitAnimationSync")]
    public sealed class UnitAnimationSyncConfig : ScriptableObject, IComponentConfig
    {
        // ── Animator ──────────────────────────────────────────────────

        /// <summary>Culling mode given to every unit Animator. CullCompletely
        /// stops an off-screen rig evaluating at all (state machine AND clip
        /// sampling); CullUpdateTransforms still evaluates both and only skips
        /// the bone writes. On-screen units are unaffected either way. A unit
        /// scrolling into view resumes from where it stopped — at RTS zoom a
        /// looping walk/idle has no visible seam.</summary>
        public AnimatorCullingMode cullingMode;

        /// <summary>Keep the Animator's state and bindings when its view is
        /// deactivated. Fog of war hides an enemy view with SetActive(false);
        /// without this every reveal REBINDS the rig (one of the costlier
        /// Animator calls) and restarts it in its default state.</summary>
        public bool keepStateOnDisable;

        // ── Parameter polling ─────────────────────────────────────────

        /// <summary>Seconds between ECS state polls for an on-screen unit. The
        /// booleans feed animator transitions, which blend over longer than
        /// this anyway.</summary>
        public float onscreenPollInterval;

        /// <summary>Seconds between ECS state polls for an off-screen unit.
        /// A unit that comes into view is polled that same frame, so this
        /// never shows on screen.</summary>
        public float offscreenPollInterval;

        /// <summary>Padding (m) around a unit's last polled position for the
        /// on-screen test. Covers how far a unit can walk between two
        /// off-screen polls plus its own size, so it is re-polled before any
        /// part of it is visible.</summary>
        public float visibilityPad;

        // ── Distance animation LOD ────────────────────────────────────

        /// <summary>Master switch for the distance LOD. A far unit's Animator
        /// is stepped manually every <see cref="lodFrameInterval"/> frames
        /// instead of every frame. NOTE: a manually stepped Animator evaluates
        /// on the MAIN thread, while an enabled one runs in Unity's parallel
        /// animation jobs — profile before turning this on; it only wins when
        /// the animation jobs, not the main thread, are the bottleneck.</summary>
        public bool lodEnabled;

        /// <summary>Camera distance (m) beyond which the LOD applies.</summary>
        public float lodDistance;

        /// <summary>A LOD unit is evaluated once every this many frames (with
        /// the accumulated delta time, so clips keep their real speed).</summary>
        public int lodFrameInterval;

        // ── Skinned mesh renderers ────────────────────────────────────

        /// <summary>Force updateWhenOffscreen off on every unit skinned mesh
        /// (the prefab bounds are used for culling instead of re-skinning
        /// off-screen rigs to recompute them).</summary>
        public bool forceNoUpdateWhenOffscreen;

        /// <summary>Turn off skinned motion vectors. Nothing in the pipeline
        /// consumes per-object motion vectors (motion blur intensity is 0, no
        /// TAA), and leaving them on keeps a second skinning buffer per mesh.
        /// Set false again if TAA or motion blur is ever enabled.</summary>
        public bool disableSkinnedMotionVectors;

        /// <summary>Max bones per vertex for unit skinned meshes. Auto = the
        /// Quality setting (4 on PC). Bone2 is cheaper but can visibly change
        /// deformation at elbows and shoulders — an art call.</summary>
        public SkinQuality skinQuality;

        /// <summary>Camera distance (m) beyond which a unit stops casting a
        /// shadow. 0 = units always cast (the shipped look). An art call:
        /// unit shadows ground the figures, but every caster is re-drawn per
        /// shadow cascade.</summary>
        public float shadowCullDistance;
    }
}
