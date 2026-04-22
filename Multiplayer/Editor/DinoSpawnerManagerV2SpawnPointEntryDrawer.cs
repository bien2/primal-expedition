using UnityEditor;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer.Editor
{
    [CustomPropertyDrawer(typeof(DinoSpawnerManagerV2.SpawnPointEntry))]
    public class DinoSpawnerManagerV2SpawnPointEntryDrawer : PropertyDrawer
    {
        private const float LinePad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty spawnPointProp = property.FindPropertyRelative("spawnPoint");
            SerializedProperty presetProp = property.FindPropertyRelative("radiusPreset");
            SerializedProperty minProp = property.FindPropertyRelative("minSpawnRadius");
            SerializedProperty maxProp = property.FindPropertyRelative("maxSpawnRadius");

            float line = EditorGUIUtility.singleLineHeight;
            Rect r = new Rect(position.x, position.y, position.width, line);

            EditorGUI.PropertyField(r, spawnPointProp);
            r.y += line + LinePad;

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(r, presetProp);
            bool presetChanged = EditorGUI.EndChangeCheck();

            DinoSpawnerManagerV2.SpawnRadiusPreset preset = (DinoSpawnerManagerV2.SpawnRadiusPreset)presetProp.enumValueIndex;
            if (presetChanged && preset != DinoSpawnerManagerV2.SpawnRadiusPreset.Custom)
            {
                GetPresetRadii(preset, out float presetMin, out float presetMax);
                if (minProp != null) minProp.floatValue = presetMin;
                if (maxProp != null) maxProp.floatValue = presetMax;
            }

            if (preset == DinoSpawnerManagerV2.SpawnRadiusPreset.Custom)
            {
                r.y += line + LinePad;
                EditorGUI.PropertyField(r, minProp);
                r.y += line + LinePad;
                EditorGUI.PropertyField(r, maxProp);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return base.GetPropertyHeight(property, label);
            }

            SerializedProperty presetProp = property.FindPropertyRelative("radiusPreset");
            DinoSpawnerManagerV2.SpawnRadiusPreset preset = presetProp != null
                ? (DinoSpawnerManagerV2.SpawnRadiusPreset)presetProp.enumValueIndex
                : DinoSpawnerManagerV2.SpawnRadiusPreset.Custom;

            int lines = 2; // spawnPoint + preset
            if (preset == DinoSpawnerManagerV2.SpawnRadiusPreset.Custom)
            {
                lines += 2; // min + max
            }

            return (EditorGUIUtility.singleLineHeight + LinePad) * lines - LinePad;
        }

        private static void GetPresetRadii(DinoSpawnerManagerV2.SpawnRadiusPreset preset, out float min, out float max)
        {
            switch (preset)
            {
                case DinoSpawnerManagerV2.SpawnRadiusPreset.Indoors:
                    min = 1f;
                    max = 4f;
                    return;
                case DinoSpawnerManagerV2.SpawnRadiusPreset.Mid:
                    min = 2f;
                    max = 8f;
                    return;
                case DinoSpawnerManagerV2.SpawnRadiusPreset.Wide:
                    min = 4f;
                    max = 12f;
                    return;
                default:
                    min = 0f;
                    max = 0f;
                    return;
            }
        }
    }
}

