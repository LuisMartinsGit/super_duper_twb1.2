// PresentationSpawnSystem.SupplyNode.cs
// Supply node visual — a low ring of grain sheaves. Partial of
// PresentationSpawnSystem, co-located with the node per the TechTree
// convention.
//
// Procedural rather than a prefab because the node's whole job is to be read at
// a glance from RTS camera height: "a Gatherer's Hut goes here". It has to be
// legible enough to aim at and quiet enough to sit under the hut that ends up
// covering it.

using UnityEngine;
using Unity.Entities;

public partial class PresentationSpawnSystem
{
    private static readonly Color SupplyNodeSheafColor = new Color(0.78f, 0.66f, 0.30f);
    private static readonly Color SupplyNodeSoilColor  = new Color(0.34f, 0.28f, 0.18f);

    /// <summary>Sheaves in the ring. Enough to read as a field, few enough to
    /// stay cheap — this spawns once per node at match start.</summary>
    private const int SupplyNodeSheafCount = 7;

    private GameObject CreateProceduralSupplyNode(Vector3 center, Entity entity)
    {
        float radius = _em.HasComponent<Radius>(entity)
            ? _em.GetComponentData<Radius>(entity).Value
            : TheWaningBorder.Entities.SupplyNode.NodeRadius;

        var root = new GameObject("SupplyNode");
        root.transform.position = center;

        // Tilled ground: a flat disc so the node still reads once a hut is
        // standing on top of the sheaves.
        var soil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(soil.GetComponent<Collider>());
        soil.transform.SetParent(root.transform, false);
        soil.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        soil.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);
        ProceduralMaterialHelper.SetProperties(
            soil.GetComponent<Renderer>(), SupplyNodeSoilColor, smoothness: 0.05f, metallic: 0f);

        // Sheaves around the rim, leaving the middle clear for the hut.
        for (int i = 0; i < SupplyNodeSheafCount; i++)
        {
            // Deterministic across peers: derived from the entity index and the
            // loop, never from Random — two clients drawing different fields is
            // a desync report waiting to happen even though it is only visual.
            float angle = (i / (float)SupplyNodeSheafCount) * Mathf.PI * 2f
                          + (Mathf.Abs(entity.Index) % 17) * 0.11f;
            float dist = radius * 0.72f;

            var sheaf = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(sheaf.GetComponent<Collider>());
            sheaf.transform.SetParent(root.transform, false);
            sheaf.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * dist, 0.45f, Mathf.Sin(angle) * dist);
            sheaf.transform.localScale = new Vector3(0.42f, 0.45f, 0.42f);
            // Leaned outward, so the ring reads as stooked grain rather than
            // as a row of posts.
            sheaf.transform.localRotation = Quaternion.Euler(
                Mathf.Sin(angle) * 12f, 0f, -Mathf.Cos(angle) * 12f);
            ProceduralMaterialHelper.SetProperties(
                sheaf.GetComponent<Renderer>(), SupplyNodeSheafColor,
                smoothness: 0.1f, metallic: 0f);
        }

        return root;
    }
}
