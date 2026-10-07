// PresentationSpawnSystem.Shardroot.cs
// The Shardroot's visual (PresentationID 383, BorderConstants.ShardrootPresentationID):
// the ground pickup, and the Maw backstop's artifact embedded in the host
// well (Curse_And_Shardroot.md §3). Until 2026-09-26 the id had no mapping and
// fell through to the grey primitive fallback.
//
// Art_Direction.md: cyan is veilstone, purple is the curse. The Shardroot is a
// veilstone artifact, so it is the veilstone gem cluster in cyan, lit at the
// top rung of the emissive ladder (EmissiveLadder.Shardroot) plus a small
// point light so it reads at dusk from across the field. Co-located with the
// Shardroot's components per the TechTree convention.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Rendering;

public partial class PresentationSpawnSystem
{
    private static GameObject _shardrootPrefab;
    private const string ShardrootPrefabPath = "Prefabs/Veilstone/P_VeilstoneOutcropping_GemA";

    /// <summary>Prefab-to-world scale of the ground pickup: about half a
    /// veilstone node (VeilstoneOutcroppingVisualBaseScale fills one cell).</summary>
    private const float ShardrootPickupVisualScale = 3f;
    /// <summary>The Maw's embedded artifact is drawn larger: it has to read
    /// inside a landmark-scale well.</summary>
    private const float ShardrootEmbeddedVisualScale = 5f;
    /// <summary>How far above the well's base the embedded artifact floats,
    /// so it shows out of the well's own crystal mass.</summary>
    private const float ShardrootEmbeddedHeight = 3f;
    private const float ShardrootLightRange = 9f;
    private const float ShardrootLightIntensity = 2.5f;

    private GameObject CreateShardrootVisual(Vector3 center, Entity entity)
    {
        bool embedded = _em.HasComponent<ShardrootEmbedded>(entity);

        var root = new GameObject(embedded ? $"ShardrootEmbedded_{entity.Index}" : $"Shardroot_{entity.Index}");
        root.transform.position = center;

        if (_shardrootPrefab == null)
            _shardrootPrefab = Resources.Load<GameObject>(ShardrootPrefabPath);

        float baseScale = embedded ? ShardrootEmbeddedVisualScale : ShardrootPickupVisualScale;

        // The gem hangs off a child, so SyncTransforms (which drives the
        // root) never undoes the embedded lift. The root carries BaseScale,
        // so the lift is divided by it to land in world metres.
        var holder = new GameObject("Gem");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = embedded
            ? Vector3.up * (ShardrootEmbeddedHeight / baseScale)
            : Vector3.zero;

        if (_shardrootPrefab != null)
        {
            var gem = Instantiate(_shardrootPrefab, holder.transform, false);
            StripThirdPartyControllers(gem);
            EmissiveLadder.ApplyCrystalGlow(gem, EmissiveLadder.Shardroot);
        }

        var light = holder.AddComponent<Light>();
        light.type = LightType.Point;
        // The ladder colour is HDR; a light wants its hue, not its intensity.
        Color hdr = EmissiveLadder.Shardroot;
        light.color = hdr / Mathf.Max(0.0001f, hdr.maxColorComponent);
        light.range = ShardrootLightRange;
        light.intensity = ShardrootLightIntensity;
        light.shadows = LightShadows.None;

        var scaleTag = root.AddComponent<ProceduralScaleTag>();
        scaleTag.BaseScale = baseScale;
        root.transform.localScale = Vector3.one * baseScale;

        // The ground pickup must be clickable: left-click selects it (info
        // panel), right-click orders the selection onto it. ScreenPick only
        // sees colliders, and the gem prefab ships none, so without this box
        // the artifact was invisible to every click (2026-10-06). The Maw's
        // embedded artifact gets NO collider: it floats inside the host
        // well, and a box there would steal the clicks that verb the well.
        if (!embedded)
            FitSelectionCollider(root, entity, _em, minWorldXZ: 1.5f, minWorldY: 1.5f);

        var er = root.AddComponent<EntityReference>();
        er.Entity = entity;
        return root;
    }
}
