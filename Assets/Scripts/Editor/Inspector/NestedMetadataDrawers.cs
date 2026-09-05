using System;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Data.Border;

namespace TheWaningBorder.EditorTools.Inspector
{
    /// <summary>
    /// Chrome for a nested [Serializable] type that is reached as a LIST OR
    /// ARRAY ELEMENT.
    ///
    /// Those cannot go through <see cref="MetadataInspector"/>: it would have
    /// to hand-draw the list to reach the elements, which costs the reorderable
    /// list UI. A PropertyDrawer is called per element by the list itself, so
    /// the list keeps its native behaviour and still gets its tooltips.
    ///
    /// Adding a nested type: derive from this, point <see cref="Target"/> at it,
    /// and tag the subclass [CustomPropertyDrawer(typeof(ThatType))]. A nested
    /// type held as a SINGLE field needs no drawer — MetadataInspector recurses
    /// into those on its own.
    /// </summary>
    public abstract class NestedMetadataDrawer : PropertyDrawer
    {
        /// <summary>Pad above a section header, matching Unity's HeaderAttribute.</summary>
        const float HeaderPad = 8f;

        /// <summary>The type whose chrome this drawer reads.</summary>
        protected abstract Type Target { get; }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return h;

            foreach (var child in MetadataInspector.Children(property))
            {
                if (InspectorMetadataRegistry.Header(Target, child.name) != null)
                    h += HeaderPad + EditorGUIUtility.singleLineHeight;

                h += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);
            }
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = row.yMax;

                foreach (var child in MetadataInspector.Children(property))
                {
                    var header = InspectorMetadataRegistry.Header(Target, child.name);
                    if (header != null)
                    {
                        y += HeaderPad;
                        EditorGUI.LabelField(
                            new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight),
                            header, EditorStyles.boldLabel);
                        y += EditorGUIUtility.singleLineHeight;
                    }

                    y += EditorGUIUtility.standardVerticalSpacing;

                    float height = EditorGUI.GetPropertyHeight(child, true);
                    var childLabel = new GUIContent(
                        child.displayName,
                        InspectorMetadataRegistry.Tooltip(Target, child.name));

                    EditorGUI.PropertyField(
                        new Rect(position.x, y, position.width, height), child, childLabel, true);
                    y += height;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(AISettingsSO.PersonalityBlock))]
    public sealed class PersonalityBlockDrawer : NestedMetadataDrawer
    {
        protected override Type Target => typeof(AISettingsSO.PersonalityBlock);
    }

    [CustomPropertyDrawer(typeof(BorderSettingsSO.ArmyTier))]
    public sealed class ArmyTierDrawer : NestedMetadataDrawer
    {
        protected override Type Target => typeof(BorderSettingsSO.ArmyTier);
    }

    [CustomPropertyDrawer(typeof(BorderSettingsSO.WaveEntry))]
    public sealed class WaveEntryDrawer : NestedMetadataDrawer
    {
        protected override Type Target => typeof(BorderSettingsSO.WaveEntry);
    }
}
