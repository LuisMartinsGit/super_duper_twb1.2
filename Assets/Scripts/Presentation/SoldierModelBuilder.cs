// SoldierModelBuilder.cs
// Builds a procedural soldier model from Unity primitive shapes.
// Location: Assets/Scripts/Presentation/SoldierModelBuilder.cs

using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Input;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Builds a humanoid soldier GameObject from primitive shapes (cubes/spheres).
    /// Body parts are organized with pivot parents for animation.
    /// </summary>
    public static class SoldierModelBuilder
    {
        private static readonly Color BrownBody = new Color(0.55f, 0.35f, 0.2f);
        private static readonly Color SkinTone = new Color(0.87f, 0.72f, 0.53f);
        private static readonly Color SwordGrey = new Color(0.7f, 0.7f, 0.75f);
        private static readonly Color SwordHandle = new Color(0.35f, 0.2f, 0.1f);

        /// <summary>
        /// Creates a procedural soldier GameObject at the given position.
        /// </summary>
        public static GameObject Create(Vector3 position, Entity entity)
        {
            var root = new GameObject($"Soldier_{entity.Index}");
            root.transform.position = position;

            // Body center offset - legs are at ground level
            float groundOffset = 0.5f; // half leg height

            // === TORSO ===
            var torso = CreatePart("Torso", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 0.85f, 0f),
                new Vector3(0.4f, 0.5f, 0.25f),
                BrownBody);

            // === HEAD ===
            CreatePart("Head", PrimitiveType.Sphere, root.transform,
                new Vector3(0f, 1.25f, 0f),
                new Vector3(0.22f, 0.22f, 0.22f),
                SkinTone);

            // === LEFT ARM (pivot at shoulder) ===
            var leftArmPivot = CreatePivot("LeftArmPivot", root.transform,
                new Vector3(-0.28f, 1.0f, 0f));
            CreatePart("LeftArm", PrimitiveType.Cube, leftArmPivot,
                new Vector3(0f, -0.2f, 0f),
                new Vector3(0.12f, 0.4f, 0.12f),
                BrownBody);

            // === RIGHT ARM (pivot at shoulder) ===
            var rightArmPivot = CreatePivot("RightArmPivot", root.transform,
                new Vector3(0.28f, 1.0f, 0f));
            CreatePart("RightArm", PrimitiveType.Cube, rightArmPivot,
                new Vector3(0f, -0.2f, 0f),
                new Vector3(0.12f, 0.4f, 0.12f),
                BrownBody);

            // === SWORD (child of right arm pivot) ===
            // Handle
            CreatePart("SwordHandle", PrimitiveType.Cube, rightArmPivot,
                new Vector3(0f, -0.42f, 0f),
                new Vector3(0.04f, 0.1f, 0.04f),
                SwordHandle);
            // Blade
            CreatePart("SwordBlade", PrimitiveType.Cube, rightArmPivot,
                new Vector3(0f, -0.72f, 0f),
                new Vector3(0.035f, 0.5f, 0.02f),
                SwordGrey);

            // === LEFT LEG (pivot at hip) ===
            var leftLegPivot = CreatePivot("LeftLegPivot", root.transform,
                new Vector3(-0.1f, 0.55f, 0f));
            CreatePart("LeftLeg", PrimitiveType.Cube, leftLegPivot,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(0.13f, 0.5f, 0.13f),
                BrownBody);

            // === RIGHT LEG (pivot at hip) ===
            var rightLegPivot = CreatePivot("RightLegPivot", root.transform,
                new Vector3(0.1f, 0.55f, 0f));
            CreatePart("RightLeg", PrimitiveType.Cube, rightLegPivot,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(0.13f, 0.5f, 0.13f),
                BrownBody);

            // === FACTION SHOULDER PAD (accent for faction color) ===
            CreatePart("faction_ShoulderPad", PrimitiveType.Cube, root.transform,
                new Vector3(-0.28f, 1.08f, 0f),
                new Vector3(0.18f, 0.08f, 0.18f),
                Color.white); // Will be colored by ApplyFactionColor

            // Add collider for selection
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.7f, 0f);
            col.size = new Vector3(0.5f, 1.4f, 0.3f);

            // Add EntityReference
            var entityRef = root.AddComponent<EntityReference>();
            entityRef.Entity = entity;

            // Add animator
            var animator = root.AddComponent<SoldierAnimator>();
            animator.Entity = entity;

            return root;
        }

        private static Transform CreatePivot(string name, Transform parent, Vector3 localPos)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPos;
            return pivot.transform;
        }

        private static GameObject CreatePart(string name, PrimitiveType type, Transform parent,
            Vector3 localPos, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;

            // Remove collider from individual parts (root has the selection collider)
            var partCollider = part.GetComponent<Collider>();
            if (partCollider != null)
                Object.Destroy(partCollider);

            // Set color
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
                if (renderer.material.HasProperty("_BaseColor"))
                    renderer.material.SetColor("_BaseColor", color);
            }

            return part;
        }
    }
}
