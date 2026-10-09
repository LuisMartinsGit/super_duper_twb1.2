// PlannedBuildingVisualSystem.cs
// A PLAN reads as a white preview (docs/Design/Planned_Buildings.md): the
// building's own model, every material swapped for a translucent white copy —
// the same look as the placement ghost. Presentation only; the plan's owner is
// the only one who ever sees it (FogVisibilitySyncSystem hides it for others).

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlannedBuildingVisualSystem : SystemBase
    {
        private static readonly Color PlanWhite = new Color(1f, 1f, 1f, 0.55f);
        private readonly HashSet<int> _styled = new HashSet<int>();

        protected override void OnCreate()
        {
            RequireForUpdate<PlannedBuilding>();
        }

        protected override void OnUpdate()
        {
            var views = EntityViewManager.Instance;
            if (views == null) return;

            var me = GameSettings.LocalPlayerFaction;
            foreach (var (_, fac, entity) in SystemAPI.Query<RefRO<PlannedBuilding>, RefRO<FactionTag>>()
                         .WithEntityAccess())
            {
                if (!views.TryGetView(entity, out var go) || go == null) continue;
                // Owner-only, even with fog of war switched off.
                if (fac.ValueRO.Value != me && !GameSettings.IsSpectating)
                {
                    if (go.activeSelf) go.SetActive(false);
                    continue;
                }
                if (!_styled.Add(go.GetInstanceID())) continue;
                Whiten(go);
            }
            if (_styled.Count > 4096) _styled.Clear();   // dead views are never evicted one by one
        }

        private static void Whiten(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) { r.enabled = false; continue; }
                var mats = r.materials;   // per-renderer copies
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", PlanWhite);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", PlanWhite);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                    MakeTransparent(m);
                }
                r.materials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
        }

        /// <summary>URP Lit to alpha-blended transparent (the placement ghost's recipe).</summary>
        private static void MakeTransparent(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }
    }
}
