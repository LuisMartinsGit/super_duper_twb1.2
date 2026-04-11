// PresentationSpawnSystem.Walls.cs
// Alanthor wall procedural generation (hubs, segments, instances, towers, gates)
// Extracted from PresentationSpawnSystem.cs — Fix #204

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

public partial class PresentationSpawnSystem
{
    // ═══════════════════════════════════════════════════════════════════════
    // ALANTHOR WALL PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a wall hub: a cylinder tower representing a wall connection point.
    /// </summary>
    private GameObject CreateProceduralWallHub(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallHub_{entity.Index}");
        root.transform.position = center;

        // Main cylinder tower
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "HubCylinder";
        cylinder.transform.SetParent(root.transform, false);
        cylinder.transform.localPosition = Vector3.up * 1.5f;
        cylinder.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f); // Diameter 1.2, height 3 (cylinder is 2 tall * 1.5 scale)

        var renderer = cylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            renderer.material.color = new Color(0.55f, 0.50f, 0.42f); // Stone grey-brown
        }

        // Remove individual collider
        var col = cylinder.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Single collider on root
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.5f, 3f, 1.5f);
        boxCol.center = Vector3.up * 1.5f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Create a wall segment: an elongated tall cube connecting two hubs.
    /// Reads WallConnection to determine length and orientation.
    /// </summary>
    private GameObject CreateProceduralWallSegment(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallSegment_{entity.Index}");
        root.transform.position = center;

        // Calculate segment length from WallConnection
        float length = 5f; // default fallback
        if (_em.HasComponent<WallConnection>(entity))
        {
            var conn = _em.GetComponentData<WallConnection>(entity);
            if (_em.Exists(conn.HubA) && _em.Exists(conn.HubB) &&
                _em.HasComponent<Unity.Transforms.LocalTransform>(conn.HubA) &&
                _em.HasComponent<Unity.Transforms.LocalTransform>(conn.HubB))
            {
                var posA = _em.GetComponentData<Unity.Transforms.LocalTransform>(conn.HubA).Position;
                var posB = _em.GetComponentData<Unity.Transforms.LocalTransform>(conn.HubB).Position;
                length = Unity.Mathematics.math.distance(
                    new Unity.Mathematics.float2(posA.x, posA.z),
                    new Unity.Mathematics.float2(posB.x, posB.z));
            }
        }

        // Wall cube: thin, tall, stretched along local Z
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallCube";
        wall.transform.SetParent(root.transform, false);
        wall.transform.localPosition = Vector3.up * 1.5f;
        wall.transform.localScale = new Vector3(0.6f, 3f, length); // Thin, 3 units tall, spans the distance

        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            renderer.material.color = new Color(0.50f, 0.45f, 0.38f); // Slightly darker stone
        }

        // Remove individual collider
        var col = wall.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Single collider on root
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(0.8f, 3f, length);
        boxCol.center = Vector3.up * 1.5f;

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Apply rotation from ECS entity (segment is rotated to face hub A → hub B)
        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
        {
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        }

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ALANTHOR WALL INSTANCES / TOWERS / GATES PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Force-respawn a visual for an entity whose PresentationId has changed (e.g., wall upgrade).
    /// Destroys the old GO and removes tracking so SpawnMissingVisuals picks it up next frame.
    /// </summary>
    public void ForceRespawn(Entity entity)
    {
        if (EntityViewManager.Instance != null &&
            EntityViewManager.Instance.TryGetView(entity, out var oldGo) && oldGo != null)
        {
            EntityViewManager.Instance.UnregisterView(entity);
            Destroy(oldGo);
        }
        _spawnedEntities.Remove(entity);
    }

    /// <summary>
    /// Create a wall instance: a small stone block, 1m wide, 3m tall, 0.6m deep.
    /// </summary>
    private GameObject CreateProceduralWallInstance(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallInstance_{entity.Index}");
        root.transform.position = center;

        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "WallBlock";
        block.transform.SetParent(root.transform, false);
        block.transform.localPosition = Vector3.up * 1.5f;
        block.transform.localScale = new Vector3(0.6f, 3f, 1.0f); // Thin, 3m tall, 1m along wall

        var renderer = block.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            renderer.material.color = new Color(0.50f, 0.45f, 0.38f); // Stone grey-brown
        }

        var col = block.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(0.8f, 3f, 1.2f);
        boxCol.center = Vector3.up * 1.5f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Apply rotation from ECS entity
        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
        {
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        }

        return root;
    }

    /// <summary>
    /// Create a wall tower: taller, wider cylinder with a platform cap and crenellations.
    /// </summary>
    private GameObject CreateProceduralWallTower(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallTower_{entity.Index}");
        root.transform.position = center;

        // Main cylinder — taller and wider than a wall hub
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "TowerCylinder";
        cylinder.transform.SetParent(root.transform, false);
        cylinder.transform.localPosition = Vector3.up * 2.0f;
        cylinder.transform.localScale = new Vector3(1.6f, 2.0f, 1.6f); // Diameter 1.6, height 4

        var cRend = cylinder.GetComponent<Renderer>();
        if (cRend != null)
        {
            cRend.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            cRend.material.color = new Color(0.45f, 0.42f, 0.36f); // Darker stone
        }
        var cCol = cylinder.GetComponent<Collider>();
        if (cCol != null) Destroy(cCol);

        // Platform cap
        var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cap.name = "TowerCap";
        cap.transform.SetParent(root.transform, false);
        cap.transform.localPosition = Vector3.up * 4.2f;
        cap.transform.localScale = new Vector3(2.0f, 0.3f, 2.0f);

        var capRend = cap.GetComponent<Renderer>();
        if (capRend != null)
        {
            capRend.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            capRend.material.color = new Color(0.35f, 0.35f, 0.38f); // Iron grey
        }
        var capCol = cap.GetComponent<Collider>();
        if (capCol != null) Destroy(capCol);

        // Crenellation nubs (4 small cubes on the corners of the cap)
        for (int i = 0; i < 4; i++)
        {
            var merlon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            merlon.name = $"Merlon_{i}";
            merlon.transform.SetParent(root.transform, false);
            float angle = i * 90f * Mathf.Deg2Rad;
            float offset = 0.75f;
            merlon.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * offset, 4.6f, Mathf.Sin(angle) * offset);
            merlon.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);

            var mRend = merlon.GetComponent<Renderer>();
            if (mRend != null)
            {
                mRend.material = new Material(
                    Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mRend.material.color = new Color(0.35f, 0.35f, 0.38f);
            }
            var mCol = merlon.GetComponent<Collider>();
            if (mCol != null) Destroy(mCol);
        }

        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(2.0f, 5f, 2.0f);
        boxCol.center = Vector3.up * 2.5f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    /// <summary>
    /// Create a wall gate: two stone pillars with an archway lintel.
    /// </summary>
    private GameObject CreateProceduralWallGate(Vector3 center, Entity entity)
    {
        var root = new GameObject($"WallGate_{entity.Index}");
        root.transform.position = center;

        // Left pillar
        var leftPillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftPillar.name = "LeftPillar";
        leftPillar.transform.SetParent(root.transform, false);
        leftPillar.transform.localPosition = new Vector3(-0.5f, 1.75f, 0f);
        leftPillar.transform.localScale = new Vector3(0.3f, 3.5f, 0.8f);

        var lpRend = leftPillar.GetComponent<Renderer>();
        if (lpRend != null)
        {
            lpRend.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            lpRend.material.color = new Color(0.42f, 0.40f, 0.35f);
        }
        var lpCol = leftPillar.GetComponent<Collider>();
        if (lpCol != null) Destroy(lpCol);

        // Right pillar
        var rightPillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightPillar.name = "RightPillar";
        rightPillar.transform.SetParent(root.transform, false);
        rightPillar.transform.localPosition = new Vector3(0.5f, 1.75f, 0f);
        rightPillar.transform.localScale = new Vector3(0.3f, 3.5f, 0.8f);

        var rpRend = rightPillar.GetComponent<Renderer>();
        if (rpRend != null)
        {
            rpRend.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            rpRend.material.color = new Color(0.42f, 0.40f, 0.35f);
        }
        var rpCol = rightPillar.GetComponent<Collider>();
        if (rpCol != null) Destroy(rpCol);

        // Lintel (top beam across the arch)
        var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lintel.name = "Lintel";
        lintel.transform.SetParent(root.transform, false);
        lintel.transform.localPosition = new Vector3(0f, 3.7f, 0f);
        lintel.transform.localScale = new Vector3(1.3f, 0.4f, 0.8f);

        var lRend = lintel.GetComponent<Renderer>();
        if (lRend != null)
        {
            lRend.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            lRend.material.color = new Color(0.38f, 0.36f, 0.32f); // Slightly darker
        }
        var lCol = lintel.GetComponent<Collider>();
        if (lCol != null) Destroy(lCol);

        // Collider on root — smaller than wall instance to allow passage
        var boxCol = root.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.2f, 4f, 1.0f);
        boxCol.center = Vector3.up * 2.0f;

        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        // Apply rotation from ECS entity
        if (_em.HasComponent<Unity.Transforms.LocalTransform>(entity))
        {
            root.transform.rotation = _em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Rotation;
        }

        return root;
    }

}
