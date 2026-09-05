using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.Inspector
{
    /// <summary>
    /// Draws the default inspector, but with section headers and tooltips read
    /// from an <see cref="InspectorMetadataSO"/> instead of from [Header] /
    /// [Tooltip] attributes on the fields.
    ///
    /// Registered as a catch-all for MonoBehaviour and ScriptableObject, so a
    /// type never has to opt in. That breadth is safe because a type with no
    /// authored chrome falls straight through to DrawDefaultInspector — which
    /// is exactly what Unity would have drawn — leaving every third-party and
    /// package component untouched. Anything with its own [CustomEditor] is
    /// more derived and still wins.
    /// </summary>
    public abstract class MetadataInspector : UnityEditor.Editor
    {
        /// <summary>Pad above a section header, matching Unity's HeaderAttribute.</summary>
        const float HeaderPad = 8f;

        public override void OnInspectorGUI()
        {
            var type = target.GetType();
            if (!InspectorMetadataRegistry.HasAnyFor(type))
            {
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            var it = serializedObject.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (it.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(it, true);
                    continue;
                }

                Draw(it.Copy(), type);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draw one property with whatever chrome its declaring type authored.</summary>
        internal static void Draw(SerializedProperty prop, Type declaringType)
        {
            var header = InspectorMetadataRegistry.Header(declaringType, prop.name);
            if (header != null)
            {
                GUILayout.Space(HeaderPad);
                EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
            }

            var label = new GUIContent(
                prop.displayName,
                InspectorMetadataRegistry.Tooltip(declaringType, prop.name));

            // A nested serializable type that carries chrome of its own has to be
            // expanded by hand, since Unity's default drawing would not consult
            // the registry for the children. Nested types reached as LIST ELEMENTS
            // go through a PropertyDrawer instead — see NestedMetadataDrawers —
            // because hand-drawing a list would cost the reorderable list UI.
            var nested = NestedTypeWithChrome(declaringType, prop);
            if (nested != null)
            {
                prop.isExpanded = EditorGUILayout.Foldout(prop.isExpanded, label, true);
                if (prop.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var child in Children(prop)) Draw(child, nested);
                    EditorGUI.indentLevel--;
                }
                return;
            }

            EditorGUILayout.PropertyField(prop, label, true);
        }

        /// <summary>
        /// The declared type of a single (non-list) nested serializable field
        /// that has chrome authored for it, or null.
        /// </summary>
        static Type NestedTypeWithChrome(Type declaringType, SerializedProperty prop)
        {
            if (prop.propertyType != SerializedPropertyType.Generic || prop.isArray) return null;

            var field = InspectorMetadataRegistry.FindField(declaringType, prop.name);
            var type = field?.FieldType;
            if (type == null) return null;

            return InspectorMetadataRegistry.HasAnyFor(type) ? type : null;
        }

        /// <summary>The immediate visible children of a property.</summary>
        internal static IEnumerable<SerializedProperty> Children(SerializedProperty prop)
        {
            var end = prop.GetEndProperty();
            var it = prop.Copy();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                yield return it.Copy();
            }
        }
    }

    [CustomEditor(typeof(MonoBehaviour), true), CanEditMultipleObjects]
    public sealed class MonoBehaviourMetadataInspector : MetadataInspector { }

    [CustomEditor(typeof(ScriptableObject), true), CanEditMultipleObjects]
    public sealed class ScriptableObjectMetadataInspector : MetadataInspector { }
}
