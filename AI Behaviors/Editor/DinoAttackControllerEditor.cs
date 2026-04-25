using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DinoAttackController))]
public class DinoAttackControllerEditor : Editor
{
    private SerializedProperty enableAttackProp;
    private SerializedProperty attackRadiusProp;
    private SerializedProperty attackCheckIntervalProp;

    private SerializedProperty enableJumpAttackProp;
    private SerializedProperty jumpAttackRadiusProp;
    private SerializedProperty jumpAttackDurationSecondsProp;
    private SerializedProperty jumpAttackHeightProp;

    private SerializedProperty attackEffectProp;
    private SerializedProperty attackCooldownProp;
    private SerializedProperty attackWindupSecondsProp;
    private SerializedProperty validateRangeProp;
    private SerializedProperty attackStateSecondsProp;
    private SerializedProperty knockbackImpulseProp;
    private SerializedProperty knockbackUpwardProp;

    private SerializedProperty useBlackoutOnInstakillProp;
    private SerializedProperty blackoutDelaySecondsProp;

    private SerializedProperty animatorProp;
    private SerializedProperty attackStateNameProp;
    private SerializedProperty bitePointProp;
    private SerializedProperty biteHoldSecondsProp;

    private void OnEnable()
    {
        enableAttackProp = serializedObject.FindProperty("enableAttack");
        attackRadiusProp = serializedObject.FindProperty("attackRadius");
        attackCheckIntervalProp = serializedObject.FindProperty("attackCheckInterval");

        enableJumpAttackProp = serializedObject.FindProperty("enableJumpAttack");
        jumpAttackRadiusProp = serializedObject.FindProperty("jumpAttackRadius");
        jumpAttackDurationSecondsProp = serializedObject.FindProperty("jumpAttackDurationSeconds");
        jumpAttackHeightProp = serializedObject.FindProperty("jumpAttackHeight");

        attackEffectProp = serializedObject.FindProperty("attackEffect");
        attackCooldownProp = serializedObject.FindProperty("attackCooldown");
        attackWindupSecondsProp = serializedObject.FindProperty("attackWindupSeconds");
        validateRangeProp = serializedObject.FindProperty("validateRange");
        attackStateSecondsProp = serializedObject.FindProperty("attackStateSeconds");
        knockbackImpulseProp = serializedObject.FindProperty("knockbackImpulse");
        knockbackUpwardProp = serializedObject.FindProperty("knockbackUpward");

        useBlackoutOnInstakillProp = serializedObject.FindProperty("useBlackoutOnInstakill");
        blackoutDelaySecondsProp = serializedObject.FindProperty("blackoutDelaySeconds");

        animatorProp = serializedObject.FindProperty("animator");
        attackStateNameProp = serializedObject.FindProperty("attackStateName");
        bitePointProp = serializedObject.FindProperty("bitePoint");
        biteHoldSecondsProp = serializedObject.FindProperty("biteHoldSeconds");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Attack", EditorStyles.boldLabel);

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Normal Attack", EditorStyles.boldLabel);
        {
            EditorGUILayout.PropertyField(enableAttackProp, new GUIContent("Enable Attack"));
            bool enabled = enableAttackProp.boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUILayout.PropertyField(attackRadiusProp, new GUIContent("Attack Radius"));
                EditorGUILayout.PropertyField(attackCheckIntervalProp, new GUIContent("Attack Check Interval"));
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Jump Attack", EditorStyles.boldLabel);
        {
            EditorGUILayout.PropertyField(enableJumpAttackProp, new GUIContent("Enable Jump Attack"));
            bool enabled = enableJumpAttackProp.boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUILayout.PropertyField(jumpAttackRadiusProp, new GUIContent("Jump Attack Radius"));
                EditorGUILayout.PropertyField(jumpAttackDurationSecondsProp, new GUIContent("Jump Attack Duration Seconds"));
                EditorGUILayout.PropertyField(jumpAttackHeightProp, new GUIContent("Jump Attack Height"));
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Effect", EditorStyles.boldLabel);
        {
            EditorGUILayout.PropertyField(attackEffectProp, new GUIContent("Attack Effect"));
            EditorGUILayout.PropertyField(attackCooldownProp, new GUIContent("Attack Cooldown"));
            EditorGUILayout.PropertyField(attackWindupSecondsProp, new GUIContent("Attack Windup Seconds"));
            EditorGUILayout.PropertyField(validateRangeProp, new GUIContent("Validate Range"));
            EditorGUILayout.PropertyField(attackStateSecondsProp, new GUIContent("Attack State Seconds"));
            EditorGUILayout.PropertyField(knockbackImpulseProp, new GUIContent("Knockback Impulse"));
            EditorGUILayout.PropertyField(knockbackUpwardProp, new GUIContent("Knockback Upward"));
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Blackout", EditorStyles.boldLabel);
        {
            EditorGUILayout.PropertyField(useBlackoutOnInstakillProp, new GUIContent("Use Blackout On Instakill"));
            EditorGUILayout.PropertyField(blackoutDelaySecondsProp, new GUIContent("Blackout Delay Seconds"));
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
        {
            EditorGUILayout.PropertyField(animatorProp);
            EditorGUILayout.PropertyField(attackStateNameProp, new GUIContent("Attack State Name"));
            EditorGUILayout.PropertyField(bitePointProp);
            EditorGUILayout.PropertyField(biteHoldSecondsProp, new GUIContent("Bite Hold Seconds"));
        }

        serializedObject.ApplyModifiedProperties();
    }
}
