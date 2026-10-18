// PresentationSpawnSystem.SanctumCrystal.cs
// The Sanctum landmark's view — the authored SanctumCrystal.prefab, turned by a
// yaw drawn from its position (the same on every client), with the crystals'
// glow taken from EmissiveLadder.Sanctum. Partial of PresentationSpawnSystem,
// co-located with the entity per the TechTree convention.
//
// Not selectable: it carries no collider and no EntityReference — it is a
// landmark, there is nothing to order.

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Rendering;

public partial class PresentationSpawnSystem
{
    private GameObject CreateSanctumCrystal(Vector3 center, Entity entity)
    {
        var cfg = SanctumCrystalVisual.Cfg;
        float yaw = Mathf.Repeat(center.x * 37.1f + center.z * 11.3f, 360f);
        var rot = Quaternion.Euler(0f, yaw, 0f);

        GameObject root;
        if (cfg != null && cfg.prefab != null)
        {
            root = Object.Instantiate(cfg.prefab, center, rot);
            root.transform.localScale = Vector3.one * (cfg.scale > 0f ? cfg.scale : 1f);
        }
        else
        {
            // The prefab is missing (logged by ComponentConfig.Require when the
            // config is): a plain spire, so the Sanctum still shows.
            root = new GameObject("SanctumCrystal_missing");
            root.transform.SetPositionAndRotation(center, rot);
            var spire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(spire.GetComponent<Collider>());
            spire.transform.SetParent(root.transform, false);
            spire.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            spire.transform.localScale = new Vector3(0.8f, 2.5f, 0.8f);
            spire.name = "Crystals";
        }
        root.name = $"SanctumCrystal_{entity.Index}";

        // The ladder is the one home of the glow level: the authored material
        // carries it too, this keeps them from drifting apart.
        var crystals = root.transform.Find("Crystals");
        if (crystals != null) EmissiveLadder.ApplyCrystalGlow(crystals.gameObject, EmissiveLadder.Sanctum);
        var rune = root.transform.Find("Plinth/RuneInlay");
        if (rune != null) EmissiveLadder.ApplyCrystalGlow(rune.gameObject, EmissiveLadder.SanctumRune);

        if (root.GetComponent<SanctumCrystalVisual>() == null)
            root.AddComponent<SanctumCrystalVisual>();
        return root;
    }
}
