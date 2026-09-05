using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.EditorTools.Inspector
{
    /// <summary>
    /// Inspector chrome — section headers and hover tooltips — for a set of
    /// serialized types, held as data instead of as [Header] / [Tooltip]
    /// attributes in the source.
    ///
    /// Editor-only by construction: this type lives in TheWaningBorder.Editor
    /// (includePlatforms: Editor), so none of these strings reach a player
    /// build, which is one thing the attributes could never manage.
    ///
    /// Several of these assets may exist; <see cref="InspectorMetadataRegistry"/>
    /// finds and merges all of them, so they can be split however is convenient
    /// to edit.
    /// </summary>
    public sealed class InspectorMetadataSO : ScriptableObject
    {
        /// <summary>Chrome for one serialized field.</summary>
        [Serializable]
        public sealed class FieldEntry
        {
            /// <summary>Serialized field name, exactly as declared in C#.</summary>
            public string field;

            /// <summary>Bold section label drawn ABOVE this field. Optional.</summary>
            public string header;

            /// <summary>Hover text on this field's label. Optional.</summary>
            [TextArea(1, 4)] public string tooltip;
        }

        /// <summary>Chrome for one type, keyed by reflection name.</summary>
        [Serializable]
        public sealed class TypeEntry
        {
            /// <summary>
            /// Type.FullName of the type that DECLARES the fields — namespace
            /// qualified, with '+' before a nested type
            /// (e.g. "TheWaningBorder.Data.Border.BorderSettingsSO+ArmyTier").
            /// Entries are matched against a component's whole base chain, so
            /// a field declared on a base class is listed under that base.
            /// </summary>
            public string typeName;

            /// <summary>
            /// Fields in declaration order. Order matters only in that a header
            /// is drawn above its own field; the draw order itself comes from
            /// Unity's serialization order, not from this list.
            /// </summary>
            public List<FieldEntry> fields = new List<FieldEntry>();
        }

        public List<TypeEntry> types = new List<TypeEntry>();
    }
}
