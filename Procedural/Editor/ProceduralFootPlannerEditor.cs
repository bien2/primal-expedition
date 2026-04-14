using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProceduralFootPlanner))]
public class ProceduralFootPlannerEditor : Editor
{
    private SerializedProperty bodyRootProp;
    private SerializedProperty legsProp;
    private SerializedProperty maxSpeedProp;
    private SerializedProperty moveThresholdProp;
    private SerializedProperty stepDistanceProp;
    private SerializedProperty stepHeightProp;
    private SerializedProperty stepDurationProp;
    private SerializedProperty maxFootReachFromHomeProp;
    private SerializedProperty groundLayersProp;
    private SerializedProperty footGroundOffsetProp;
    private SerializedProperty drawDebugProp;
    private SerializedProperty homeColorProp;
    private SerializedProperty plantedColorProp;
    private SerializedProperty targetColorProp;

    private void OnEnable()
    {
        bodyRootProp = serializedObject.FindProperty("bodyRoot");
        legsProp = serializedObject.FindProperty("legs");
        maxSpeedProp = serializedObject.FindProperty("maxSpeed");
        moveThresholdProp = serializedObject.FindProperty("moveThreshold");
        stepDistanceProp = serializedObject.FindProperty("stepDistance");
        stepHeightProp = serializedObject.FindProperty("stepHeight");
        stepDurationProp = serializedObject.FindProperty("stepDuration");
        maxFootReachFromHomeProp = serializedObject.FindProperty("maxFootReachFromHome");
        groundLayersProp = serializedObject.FindProperty("groundLayers");
        footGroundOffsetProp = serializedObject.FindProperty("footGroundOffset");
        drawDebugProp = serializedObject.FindProperty("drawDebug");
        homeColorProp = serializedObject.FindProperty("homeColor");
        plantedColorProp = serializedObject.FindProperty("plantedColor");
        targetColorProp = serializedObject.FindProperty("targetColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Rig", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(bodyRootProp);
        EditorGUILayout.PropertyField(legsProp, true);
        if (GUILayout.Button("Capture Home Offsets From Targets"))
        {
            foreach (Object target in targets)
            {
                if (target is ProceduralFootPlanner planner)
                {
                    Undo.RecordObject(planner, "Capture Foot Planner Home Offsets");
                    planner.CaptureHomeOffsetsFromTargets();
                    EditorUtility.SetDirty(planner);
                }
            }
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Step", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(maxSpeedProp);
        EditorGUILayout.PropertyField(stepDistanceProp);
        EditorGUILayout.PropertyField(stepHeightProp);
        EditorGUILayout.PropertyField(stepDurationProp);
        EditorGUILayout.PropertyField(maxFootReachFromHomeProp, new GUIContent("Max Foot Reach"));
        EditorGUILayout.PropertyField(moveThresholdProp);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Ground", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(groundLayersProp);
        EditorGUILayout.PropertyField(footGroundOffsetProp);

        EditorGUILayout.Space(4f);
        EditorGUILayout.PropertyField(drawDebugProp);
        if (drawDebugProp.boolValue)
        {
            EditorGUILayout.PropertyField(homeColorProp);
            EditorGUILayout.PropertyField(plantedColorProp);
            EditorGUILayout.PropertyField(targetColorProp);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
