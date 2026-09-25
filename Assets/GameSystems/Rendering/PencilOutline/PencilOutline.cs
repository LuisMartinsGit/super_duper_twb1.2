using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Borderlands-style pencil ink outlines — an A/B PROTOTYPE, not the adopted
    /// look (docs/Design/Art_Direction.md, "Experiment: pencil ink outlines").
    ///
    /// One full-screen pass between opaques and transparents: edges are found in
    /// the depth + normals buffers and alpha-blended onto the frame as ink. The
    /// pass is injected from code onto the gameplay camera only
    /// (<see cref="PresentationState.MainCamera"/>) rather than added to the
    /// renderer asset, so menu, minimap and prewarm cameras never see it and
    /// deleting this folder removes it entirely.
    ///
    /// Things that stay un-inked, by construction: grass (its shader has no
    /// DepthNormals pass), water and VFX (transparent, drawn after), and fog of
    /// war (Overlay queue, drawn over the lines).
    /// </summary>
    public static class PencilOutline
    {
        static PencilOutlineConfig _cfg;
        static bool _cfgResolved;
        static Material _material;
        static PencilOutlinePass _pass;

        static PencilOutlineConfig Cfg
        {
            get
            {
                if (!_cfgResolved)
                {
                    _cfg = ComponentConfig.Require<PencilOutlineConfig>();
                    _cfgResolved = true;
                }
                return _cfg;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Unsubscribe first: with domain reload off, statics survive Play-mode entry.
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Application.quitting -= Release;
            Application.quitting += Release;
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != PresentationState.MainCamera) return;

            var cfg = Cfg;
            if (cfg == null || !cfg.enabled || cfg.shader == null) return;

            if (_material == null) _material = CoreUtils.CreateEngineMaterial(cfg.shader);
            _pass ??= new PencilOutlinePass();

            ApplyConfig(_material, cfg);
            _pass.material = _material;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData?.scriptableRenderer?.EnqueuePass(_pass);
        }

        static void ApplyConfig(Material m, PencilOutlineConfig c)
        {
            m.SetColor(ShaderIds.InkColor, c.inkColor);
            m.SetFloat(ShaderIds.LineWidth, c.lineWidth);
            m.SetFloat(ShaderIds.MinLineWidth, c.minLineWidth);
            m.SetFloat(ShaderIds.ReferenceDistance, c.referenceDistance);
            m.SetFloat(ShaderIds.FadeStart, c.fadeStart);
            m.SetFloat(ShaderIds.FadeEnd, c.fadeEnd);
            m.SetFloat(ShaderIds.DepthThreshold, c.depthThreshold);
            m.SetFloat(ShaderIds.GrazingCompensation, c.grazingCompensation);
            m.SetFloat(ShaderIds.NormalThreshold, c.normalThreshold);
            m.SetFloat(ShaderIds.NormalWeight, c.normalWeight);
            m.SetFloat(ShaderIds.JitterAmount, c.jitterAmount);
            m.SetFloat(ShaderIds.JitterScale, c.jitterScale);
            m.SetFloat(ShaderIds.BoilFps, c.boilFps);
            m.SetFloat(ShaderIds.StrokeBreakup, c.strokeBreakup);
            m.SetFloat(ShaderIds.StrokeScale, c.strokeScale);
            m.SetFloat(ShaderIds.Grain, c.grain);
            m.SetFloat(ShaderIds.GrainScale, c.grainScale);
        }

        static void Release()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        static class ShaderIds
        {
            public static readonly int InkColor = Shader.PropertyToID("_InkColor");
            public static readonly int LineWidth = Shader.PropertyToID("_LineWidth");
            public static readonly int MinLineWidth = Shader.PropertyToID("_MinLineWidth");
            public static readonly int ReferenceDistance = Shader.PropertyToID("_ReferenceDistance");
            public static readonly int FadeStart = Shader.PropertyToID("_FadeStart");
            public static readonly int FadeEnd = Shader.PropertyToID("_FadeEnd");
            public static readonly int DepthThreshold = Shader.PropertyToID("_DepthThreshold");
            public static readonly int GrazingCompensation = Shader.PropertyToID("_GrazingCompensation");
            public static readonly int NormalThreshold = Shader.PropertyToID("_NormalThreshold");
            public static readonly int NormalWeight = Shader.PropertyToID("_NormalWeight");
            public static readonly int JitterAmount = Shader.PropertyToID("_JitterAmount");
            public static readonly int JitterScale = Shader.PropertyToID("_JitterScale");
            public static readonly int BoilFps = Shader.PropertyToID("_BoilFps");
            public static readonly int StrokeBreakup = Shader.PropertyToID("_StrokeBreakup");
            public static readonly int StrokeScale = Shader.PropertyToID("_StrokeScale");
            public static readonly int Grain = Shader.PropertyToID("_Grain");
            public static readonly int GrainScale = Shader.PropertyToID("_GrainScale");
            public static readonly int PencilScreen = Shader.PropertyToID("_PencilScreen");
        }

        /// <summary>
        /// Draws the ink over the active colour target. Asking for Depth + Normal
        /// input is what makes URP run its depth-normals prepass for this camera.
        /// </summary>
        sealed class PencilOutlinePass : ScriptableRenderPass
        {
            public Material material;

            sealed class PassData
            {
                public Material material;
            }

            public PencilOutlinePass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                profilingSampler = new ProfilingSampler("PencilOutline");
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null) return;

                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                    return;

                var target = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
                material.SetVector(ShaderIds.PencilScreen, new Vector4(target.width, target.height, 1f / target.width, 1f / target.height));

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Pencil Outline", out var data, profilingSampler);
                data.material = material;

                builder.UseTexture(resources.cameraDepthTexture);
                builder.UseTexture(resources.cameraNormalsTexture);
                // ReadWrite, not Write: the ink is alpha-blended over what is already there.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1));
            }
        }
    }
}
