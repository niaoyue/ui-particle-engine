using UnityEditor;
using UnityEngine;

namespace UIParticleEngine.Editor
{
    /// <summary>
    /// Draws a RangeF as "Label [min] [max]" on a single line. Each field is drawn
    /// through PropertyField so multi-object editing shows mixed values, prefab
    /// overrides bold correctly, and edits only write the field the user touched.
    /// </summary>
    [CustomPropertyDrawer(typeof(RangeF))]
    public class RangeFDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            SerializedProperty min = property.FindPropertyRelative("Min");
            SerializedProperty max = property.FindPropertyRelative("Max");

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            const float spacing = 4f;
            float fieldWidth = (position.width - spacing) * 0.5f;
            var minRect = new Rect(position.x, position.y, fieldWidth, position.height);
            var maxRect = new Rect(position.x + fieldWidth + spacing, position.y, fieldWidth, position.height);

            EditorGUI.PropertyField(minRect, min, GUIContent.none);
            EditorGUI.PropertyField(maxRect, max, GUIContent.none);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
