// PresentationSpawnSystem.Vault.cs
// Procedural treasury-vault visual for the Vault of Almierra — co-located
// with the entity per the TechTree convention. Partial of PresentationSpawnSystem.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Presentation;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Entities;

using TheWaningBorder.Core;
public partial class PresentationSpawnSystem
{
    /// <summary>
    /// Create a fortified treasury vault: squat thick-walled square stone structure
    /// with iron vault door, ventilation slits, crenellations, corner buttresses,
    /// chest emblem, and faction-colored accents (flags, door trim).
    /// </summary>
    private GameObject CreateProceduralVault(Vector3 center, Entity entity)
    {
        var root = new GameObject($"VaultOfAlmierra_{entity.Index}");
        root.transform.position = center;

        // Get faction color for accents
        Color factionColor = Color.gray;
        if (_em.HasComponent<FactionTag>(entity))
        {
            var faction = _em.GetComponentData<FactionTag>(entity).Value;
            factionColor = FactionColors.Get(faction);
        }

        // Base colors
        var limestone = new Color(0.62f, 0.60f, 0.55f);     // Warm grey limestone
        var ironDark = new Color(0.18f, 0.17f, 0.16f);      // Dark iron
        var ironMedium = new Color(0.30f, 0.28f, 0.26f);    // Medium iron/metal
        var goldTrim = new Color(0.72f, 0.58f, 0.20f);      // Subtle golden trim

        Material MakeMat(Color col, float metallic = 0f, float smoothness = 0.3f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        void DestroyCol(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }

        // ── MAIN BODY: squat thick-walled square vault ──
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "VaultBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up * 1.2f;
        body.transform.localScale = new Vector3(3.6f, 2.4f, 3.6f);
        body.GetComponent<Renderer>().material = MakeMat(limestone, 0.1f, 0.2f);
        DestroyCol(body);

        // ── IRON CLAMP BANDS (horizontal lines on walls) ──
        for (int band = 0; band < 2; band++)
        {
            float bandY = 0.8f + band * 1.2f;
            for (int side = 0; side < 4; side++)
            {
                var clamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                clamp.name = $"IronBand_{band}_{side}";
                clamp.transform.SetParent(root.transform, false);

                float offset = 1.82f;
                switch (side)
                {
                    case 0: clamp.transform.localPosition = new Vector3(0f, bandY, offset); clamp.transform.localScale = new Vector3(3.7f, 0.08f, 0.06f); break;
                    case 1: clamp.transform.localPosition = new Vector3(0f, bandY, -offset); clamp.transform.localScale = new Vector3(3.7f, 0.08f, 0.06f); break;
                    case 2: clamp.transform.localPosition = new Vector3(offset, bandY, 0f); clamp.transform.localScale = new Vector3(0.06f, 0.08f, 3.7f); break;
                    case 3: clamp.transform.localPosition = new Vector3(-offset, bandY, 0f); clamp.transform.localScale = new Vector3(0.06f, 0.08f, 3.7f); break;
                }
                clamp.GetComponent<Renderer>().material = MakeMat(ironMedium, 0.6f, 0.4f);
                DestroyCol(clamp);
            }
        }

        // ── CORNER BUTTRESSES (4 thick vertical pillars at corners) ──
        float bOffset = 1.7f;
        Vector3[] corners = {
            new( bOffset, 1.0f,  bOffset),
            new(-bOffset, 1.0f,  bOffset),
            new( bOffset, 1.0f, -bOffset),
            new(-bOffset, 1.0f, -bOffset)
        };
        foreach (var corner in corners)
        {
            var buttress = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buttress.name = "Buttress";
            buttress.transform.SetParent(root.transform, false);
            buttress.transform.localPosition = corner;
            buttress.transform.localScale = new Vector3(0.6f, 2.0f, 0.6f);
            buttress.GetComponent<Renderer>().material = MakeMat(limestone * 0.9f, 0.15f, 0.2f);
            DestroyCol(buttress);
        }

        // ── FLAT ROOF ──
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(root.transform, false);
        roof.transform.localPosition = Vector3.up * 2.45f;
        roof.transform.localScale = new Vector3(3.8f, 0.15f, 3.8f);
        roof.GetComponent<Renderer>().material = MakeMat(limestone * 0.85f, 0.1f, 0.15f);
        DestroyCol(roof);

        // ── CRENELLATIONS (low merlons around roof edge) ──
        float crenOffset = 1.75f;
        for (int side = 0; side < 4; side++)
        {
            for (int m = -2; m <= 2; m++)
            {
                if (m == 0 && side == 0) continue; // gap above door

                var merlon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                merlon.name = "Merlon";
                merlon.transform.SetParent(root.transform, false);

                float along = m * 0.75f;
                switch (side)
                {
                    case 0: merlon.transform.localPosition = new Vector3(along, 2.72f, crenOffset); break;
                    case 1: merlon.transform.localPosition = new Vector3(along, 2.72f, -crenOffset); break;
                    case 2: merlon.transform.localPosition = new Vector3(crenOffset, 2.72f, along); break;
                    case 3: merlon.transform.localPosition = new Vector3(-crenOffset, 2.72f, along); break;
                }
                merlon.transform.localScale = new Vector3(0.4f, 0.4f, 0.3f);
                merlon.GetComponent<Renderer>().material = MakeMat(limestone * 0.88f, 0.1f, 0.2f);
                DestroyCol(merlon);
            }
        }

        // ── VENTILATION SLITS (narrow openings near roofline, each side) ──
        for (int side = 0; side < 4; side++)
        {
            if (side == 0) continue; // front has door, no slits
            for (int s = -1; s <= 1; s += 2)
            {
                var slit = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slit.name = "VentSlit";
                slit.transform.SetParent(root.transform, false);

                float slitOffset = 1.83f;
                float along = s * 0.9f;
                switch (side)
                {
                    case 1: slit.transform.localPosition = new Vector3(along, 2.0f, -slitOffset); slit.transform.localScale = new Vector3(0.06f, 0.25f, 0.08f); break;
                    case 2: slit.transform.localPosition = new Vector3(slitOffset, 2.0f, along); slit.transform.localScale = new Vector3(0.08f, 0.25f, 0.06f); break;
                    case 3: slit.transform.localPosition = new Vector3(-slitOffset, 2.0f, along); slit.transform.localScale = new Vector3(0.08f, 0.25f, 0.06f); break;
                }
                slit.GetComponent<Renderer>().material = MakeMat(ironDark, 0.3f, 0.1f);
                DestroyCol(slit);
            }
        }

        // ── VAULT DOOR (front face) ──
        // Door recess (dark inset)
        var doorRecess = GameObject.CreatePrimitive(PrimitiveType.Cube);
        doorRecess.name = "DoorRecess";
        doorRecess.transform.SetParent(root.transform, false);
        doorRecess.transform.localPosition = new Vector3(0f, 0.9f, 1.82f);
        doorRecess.transform.localScale = new Vector3(1.0f, 1.6f, 0.08f);
        doorRecess.GetComponent<Renderer>().material = MakeMat(ironDark * 0.6f, 0.2f, 0.1f);
        DestroyCol(doorRecess);

        // Iron door panel
        var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "VaultDoor";
        door.transform.SetParent(root.transform, false);
        door.transform.localPosition = new Vector3(0f, 0.9f, 1.85f);
        door.transform.localScale = new Vector3(0.9f, 1.5f, 0.06f);
        door.GetComponent<Renderer>().material = MakeMat(ironDark, 0.7f, 0.5f);
        DestroyCol(door);

        // Door lock cylinders (multiple locks)
        for (int l = 0; l < 3; l++)
        {
            var lockCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lockCyl.name = $"Lock_{l}";
            lockCyl.transform.SetParent(root.transform, false);
            lockCyl.transform.localPosition = new Vector3(0.3f, 0.5f + l * 0.4f, 1.9f);
            lockCyl.transform.localScale = new Vector3(0.1f, 0.03f, 0.1f);
            lockCyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lockCyl.GetComponent<Renderer>().material = MakeMat(ironMedium, 0.8f, 0.6f);
            DestroyCol(lockCyl);
        }

        // Wheel mechanism (larger cylinder on door)
        var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = "WheelMechanism";
        wheel.transform.SetParent(root.transform, false);
        wheel.transform.localPosition = new Vector3(-0.15f, 0.9f, 1.92f);
        wheel.transform.localScale = new Vector3(0.35f, 0.04f, 0.35f);
        wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        wheel.GetComponent<Renderer>().material = MakeMat(ironMedium, 0.8f, 0.5f);
        DestroyCol(wheel);

        // Wheel spokes (cross bars inside the wheel)
        for (int sp = 0; sp < 4; sp++)
        {
            var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spoke.name = $"Spoke_{sp}";
            spoke.transform.SetParent(root.transform, false);
            spoke.transform.localPosition = new Vector3(-0.15f, 0.9f, 1.93f);
            spoke.transform.localRotation = Quaternion.Euler(0f, 0f, sp * 45f);
            spoke.transform.localScale = new Vector3(0.3f, 0.04f, 0.03f);
            spoke.GetComponent<Renderer>().material = MakeMat(ironMedium, 0.8f, 0.5f);
            DestroyCol(spoke);
        }

        // ── GOLDEN DOOR FRAME TRIM (faction-colored) ──
        // Top trim
        var trimTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trimTop.name = "DoorTrimTop";
        trimTop.transform.SetParent(root.transform, false);
        trimTop.transform.localPosition = new Vector3(0f, 1.72f, 1.86f);
        trimTop.transform.localScale = new Vector3(1.1f, 0.08f, 0.08f);
        trimTop.GetComponent<Renderer>().material = MakeMat(factionColor, 0.4f, 0.6f);
        DestroyCol(trimTop);

        // Side trims
        for (int s = -1; s <= 1; s += 2)
        {
            var trimSide = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trimSide.name = "DoorTrimSide";
            trimSide.transform.SetParent(root.transform, false);
            trimSide.transform.localPosition = new Vector3(s * 0.52f, 0.9f, 1.86f);
            trimSide.transform.localScale = new Vector3(0.08f, 1.7f, 0.08f);
            trimSide.GetComponent<Renderer>().material = MakeMat(factionColor, 0.4f, 0.6f);
            DestroyCol(trimSide);
        }

        // ── CHEST EMBLEM (relief on front wall, above door) ──
        // Chest body
        var emblemBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
        emblemBody.name = "ChestEmblem";
        emblemBody.transform.SetParent(root.transform, false);
        emblemBody.transform.localPosition = new Vector3(0f, 2.05f, 1.84f);
        emblemBody.transform.localScale = new Vector3(0.6f, 0.35f, 0.1f);
        emblemBody.GetComponent<Renderer>().material = MakeMat(ironMedium, 0.6f, 0.5f);
        DestroyCol(emblemBody);

        // Chest lid (slightly raised)
        var emblemLid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        emblemLid.name = "ChestEmblemLid";
        emblemLid.transform.SetParent(root.transform, false);
        emblemLid.transform.localPosition = new Vector3(0f, 2.25f, 1.85f);
        emblemLid.transform.localScale = new Vector3(0.65f, 0.12f, 0.12f);
        emblemLid.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
        emblemLid.GetComponent<Renderer>().material = MakeMat(ironMedium, 0.6f, 0.5f);
        DestroyCol(emblemLid);

        // Chest emblem iron bands
        for (int b = -1; b <= 1; b += 2)
        {
            var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
            band.name = "ChestBand";
            band.transform.SetParent(root.transform, false);
            band.transform.localPosition = new Vector3(b * 0.18f, 2.05f, 1.87f);
            band.transform.localScale = new Vector3(0.04f, 0.4f, 0.06f);
            band.GetComponent<Renderer>().material = MakeMat(goldTrim, 0.5f, 0.7f);
            DestroyCol(band);
        }

        // ── CORNER FLAGS (faction-colored, short poles at each corner) ──
        foreach (var corner in corners)
        {
            // Flag pole
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "FlagPole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(corner.x, 2.85f, corner.z);
            pole.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
            pole.GetComponent<Renderer>().material = MakeMat(new Color(0.3f, 0.25f, 0.2f));
            DestroyCol(pole);

            // Flag (faction color)
            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "FactionFlag";
            flag.transform.SetParent(root.transform, false);
            flag.transform.localPosition = new Vector3(corner.x + 0.15f, 3.2f, corner.z);
            flag.transform.localScale = new Vector3(0.25f, 0.15f, 0.03f);
            flag.GetComponent<Renderer>().material = MakeMat(factionColor, 0f, 0.3f);
            DestroyCol(flag);
        }

        // Single collider for entire building — fitted to meshes
        FitSelectionCollider(root);

        // Add EntityReference
        var entityRef = root.AddComponent<EntityReference>();
        entityRef.Entity = entity;

        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // THE BORDER PROCEDURAL GENERATION
    // ═══════════════════════════════════════════════════════════════════════

}
