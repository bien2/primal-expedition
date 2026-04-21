using UnityEditor;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer.Editor
{
    [CustomPropertyDrawer(typeof(DinoSpawnerManagerV2.ThreatSpawnSettings))]
    public class DinoSpawnerManagerV2ThreatSpawnSettingsDrawer : PropertyDrawer
    {
        private const float LinePad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            bool hideCount = IsHunterProperty(property);

            SerializedProperty spawnAtProp = property.FindPropertyRelative("spawnAt");
            SerializedProperty spawnCountProp = property.FindPropertyRelative("spawnCount");
            SerializedProperty prefabsProp = property.FindPropertyRelative("prefabs");
            SerializedProperty pointsProp = property.FindPropertyRelative("spawnPoints");

            float y = position.y;
            float line = EditorGUIUtility.singleLineHeight;
            Rect r = new Rect(position.x, y, position.width, line);

            if (spawnAtProp != null)
            {
                float h = EditorGUI.GetPropertyHeight(spawnAtProp, includeChildren: true);
                r.height = h;
                EditorGUI.PropertyField(r, spawnAtProp, includeChildren: true);
                y += h + LinePad;
            }

            if (!hideCount && spawnCountProp != null)
            {
                float h = EditorGUI.GetPropertyHeight(spawnCountProp, includeChildren: true);
                r = new Rect(position.x, y, position.width, h);
                EditorGUI.PropertyField(r, spawnCountProp, includeChildren: true);
                y += h + LinePad;
            }

            if (prefabsProp != null)
            {
                float h = EditorGUI.GetPropertyHeight(prefabsProp, includeChildren: true);
                r = new Rect(position.x, y, position.width, h);
                EditorGUI.PropertyField(r, prefabsProp, includeChildren: true);
                y += h + LinePad;
            }

            if (pointsProp != null)
            {
                float h = EditorGUI.GetPropertyHeight(pointsProp, includeChildren: true);
                r = new Rect(position.x, y, position.width, h);
                EditorGUI.PropertyField(r, pointsProp, includeChildren: true);
                y += h + LinePad;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return base.GetPropertyHeight(property, label);
            }

            bool hideCount = IsHunterProperty(property);

            float height = 0f;
            SerializedProperty spawnAtProp = property.FindPropertyRelative("spawnAt");
            SerializedProperty spawnCountProp = property.FindPropertyRelative("spawnCount");
            SerializedProperty prefabsProp = property.FindPropertyRelative("prefabs");
            SerializedProperty pointsProp = property.FindPropertyRelative("spawnPoints");

            if (spawnAtProp != null)
            {
                height += EditorGUI.GetPropertyHeight(spawnAtProp, includeChildren: true) + LinePad;
            }

            if (!hideCount && spawnCountProp != null)
            {
                height += EditorGUI.GetPropertyHeight(spawnCountProp, includeChildren: true) + LinePad;
            }

            if (prefabsProp != null)
            {
                height += EditorGUI.GetPropertyHeight(prefabsProp, includeChildren: true) + LinePad;
            }

            if (pointsProp != null)
            {
                height += EditorGUI.GetPropertyHeight(pointsProp, includeChildren: true) + LinePad;
            }

            return Mathf.Max(0f, height - LinePad);
        }

        private static bool IsHunterProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            if (property.name == "hunter")
            {
                return true;
            }

            string path = property.propertyPath;
            return !string.IsNullOrEmpty(path) && path.Contains(".hunter");
        }
    }
}

