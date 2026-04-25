using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DinoAI))]
public class DinoAIEditor : Editor
{
    private SerializedProperty currentStateProp;
    private SerializedProperty aggressionTypeProp;
    private SerializedProperty detectionRadiusProp;
    private SerializedProperty neutralReactDelayMinProp;
    private SerializedProperty neutralReactDelayMaxProp;
    private SerializedProperty soundReactionRadiusProp;
    private SerializedProperty canHearSoundsProp;
    private SerializedProperty viewAngleProp;
    private SerializedProperty requireLineOfSightProp;
    private SerializedProperty eyeHeightProp;
    private SerializedProperty lineOfSightBlockersProp;
    private SerializedProperty showLineOfSightGizmoProp;
    private SerializedProperty playerLayersProp;
    private SerializedProperty walkSpeedProp;
    private SerializedProperty runSpeedProp;
    private SerializedProperty roamRadiusProp;
    private SerializedProperty modelYawOffsetProp;
    private SerializedProperty roamDestinationAttemptsProp;
    private SerializedProperty requireCompleteRoamPathProp;
    private SerializedProperty chaseDurationProp;
    private SerializedProperty loseSightChaseTimeoutProp;
    private SerializedProperty reactionCooldownSecondsProp;
    private SerializedProperty roamerFleeSecondsProp;
    private SerializedProperty roamerPlungeChanceProp;
    private SerializedProperty hunterCueDelayMinProp;
    private SerializedProperty hunterCueDelayMaxProp;
    private SerializedProperty hunterCueCountMinProp;
    private SerializedProperty hunterCueCountMaxProp;
    private SerializedProperty plundererBaseGrabChanceDayProp;
    private SerializedProperty plundererBaseGrabChanceNightProp;
    private SerializedProperty plundererHuntedBonusProp;
    private SerializedProperty plundererGroupedBonusProp;
    private SerializedProperty plundererChanceCapMaxProp;
    private SerializedProperty plundererFlightHeightProp;
    private SerializedProperty plundererWaypointArriveDistanceProp;
    private SerializedProperty plundererCarryHoldSecondsProp;
    private SerializedProperty plundererDropSearchRadiusProp;
    private SerializedProperty plundererDropArriveDistanceProp;
    private SerializedProperty idleTimeProp;
    private SerializedProperty isIdleProp;
    private SerializedProperty snapToGroundProp;
    private SerializedProperty groundLayersProp;
    private SerializedProperty groundRayStartHeightProp;
    private SerializedProperty groundRayLengthProp;
    private SerializedProperty groundOffsetProp;
    private SerializedProperty groundSnapSpeedProp;
    private SerializedProperty showGroundingGizmoProp;
    private SerializedProperty groundingGizmoColorProp;

    private void OnEnable()
    {
        currentStateProp = serializedObject.FindProperty("currentState");
        aggressionTypeProp = serializedObject.FindProperty("aggressionType");
        detectionRadiusProp = serializedObject.FindProperty("detectionRadius");
        neutralReactDelayMinProp = serializedObject.FindProperty("neutralReactDelayMin");
        neutralReactDelayMaxProp = serializedObject.FindProperty("neutralReactDelayMax");
        soundReactionRadiusProp = serializedObject.FindProperty("soundReactionRadius");
        canHearSoundsProp = serializedObject.FindProperty("canHearSounds");
        viewAngleProp = serializedObject.FindProperty("viewAngle");
        requireLineOfSightProp = serializedObject.FindProperty("requireLineOfSight");
        eyeHeightProp = serializedObject.FindProperty("eyeHeight");
        lineOfSightBlockersProp = serializedObject.FindProperty("lineOfSightBlockers");
        showLineOfSightGizmoProp = serializedObject.FindProperty("showLineOfSightGizmo");
        playerLayersProp = serializedObject.FindProperty("playerLayers");
        walkSpeedProp = serializedObject.FindProperty("walkSpeed");
        runSpeedProp = serializedObject.FindProperty("runSpeed");
        roamRadiusProp = serializedObject.FindProperty("roamRadius");
        modelYawOffsetProp = serializedObject.FindProperty("modelYawOffset");
        roamDestinationAttemptsProp = serializedObject.FindProperty("roamDestinationAttempts");
        requireCompleteRoamPathProp = serializedObject.FindProperty("requireCompleteRoamPath");
        chaseDurationProp = serializedObject.FindProperty("chaseDuration");
        loseSightChaseTimeoutProp = serializedObject.FindProperty("loseSightChaseTimeout");
        reactionCooldownSecondsProp = serializedObject.FindProperty("reactionCooldownSeconds");
        roamerFleeSecondsProp = serializedObject.FindProperty("roamerFleeSeconds");
        roamerPlungeChanceProp = serializedObject.FindProperty("roamerPlungeChance");
        hunterCueDelayMinProp = serializedObject.FindProperty("hunterCueDelayMin");
        hunterCueDelayMaxProp = serializedObject.FindProperty("hunterCueDelayMax");
        hunterCueCountMinProp = serializedObject.FindProperty("hunterCueCountMin");
        hunterCueCountMaxProp = serializedObject.FindProperty("hunterCueCountMax");
        plundererBaseGrabChanceDayProp = serializedObject.FindProperty("plundererBaseGrabChanceDay");
        plundererBaseGrabChanceNightProp = serializedObject.FindProperty("plundererBaseGrabChanceNight");
        plundererHuntedBonusProp = serializedObject.FindProperty("plundererHuntedBonus");
        plundererGroupedBonusProp = serializedObject.FindProperty("plundererGroupedBonus");
        plundererChanceCapMaxProp = serializedObject.FindProperty("plundererChanceCapMax");
        plundererFlightHeightProp = serializedObject.FindProperty("plundererFlightHeight");
        plundererWaypointArriveDistanceProp = serializedObject.FindProperty("plundererWaypointArriveDistance");
        plundererCarryHoldSecondsProp = serializedObject.FindProperty("plundererCarryHoldSeconds");
        plundererDropSearchRadiusProp = serializedObject.FindProperty("plundererDropSearchRadius");
        plundererDropArriveDistanceProp = serializedObject.FindProperty("plundererDropArriveDistance");
        idleTimeProp = serializedObject.FindProperty("idleTime");
        isIdleProp = serializedObject.FindProperty("isIdle");
        snapToGroundProp = serializedObject.FindProperty("snapToGround");
        groundLayersProp = serializedObject.FindProperty("groundLayers");
        groundRayStartHeightProp = serializedObject.FindProperty("groundRayStartHeight");
        groundRayLengthProp = serializedObject.FindProperty("groundRayLength");
        groundOffsetProp = serializedObject.FindProperty("groundOffset");
        groundSnapSpeedProp = serializedObject.FindProperty("groundSnapSpeed");
        showGroundingGizmoProp = serializedObject.FindProperty("showGroundingGizmo");
        groundingGizmoColorProp = serializedObject.FindProperty("groundingGizmoColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(currentStateProp);
        EditorGUILayout.PropertyField(aggressionTypeProp);
        DinoAI.AggressionType selectedAggression =
            (DinoAI.AggressionType)aggressionTypeProp.enumValueIndex;
        bool isApex = selectedAggression == DinoAI.AggressionType.Apex;
        bool isHunter = selectedAggression == DinoAI.AggressionType.Hunter;
        bool isNeutral = selectedAggression == DinoAI.AggressionType.Neutral;
        bool isPlunderer = selectedAggression == DinoAI.AggressionType.Plunderer;
        bool isPassive = selectedAggression == DinoAI.AggressionType.Passive;
        bool isRoamer = selectedAggression == DinoAI.AggressionType.Roamer;
        bool showReactionCooldown = (isNeutral || isApex) && !isPlunderer;
        bool showPerceptionCore = !isPassive && !isPlunderer && !isHunter;
        bool showSoundPerception = !isPassive && !isNeutral && !isPlunderer && !isHunter;

        if (showPerceptionCore)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Perception", EditorStyles.boldLabel);
            detectionRadiusProp.floatValue = EditorGUILayout.FloatField("Detection Radius", detectionRadiusProp.floatValue);

            if (!isPlunderer)
            {
                if (isNeutral)
                {
                    neutralReactDelayMinProp.floatValue = EditorGUILayout.FloatField("Neutral React Delay Min", neutralReactDelayMinProp.floatValue);
                    neutralReactDelayMaxProp.floatValue = EditorGUILayout.FloatField("Neutral React Delay Max", neutralReactDelayMaxProp.floatValue);
                }

                if (showSoundPerception)
                {
                    canHearSoundsProp.boolValue = EditorGUILayout.Toggle("React To Sounds", canHearSoundsProp.boolValue);
                    if (canHearSoundsProp.boolValue)
                    {
                        soundReactionRadiusProp.floatValue = EditorGUILayout.FloatField("Sound Reaction Radius", soundReactionRadiusProp.floatValue);
                    }
                }
            }

            viewAngleProp.floatValue = EditorGUILayout.FloatField("View Angle", viewAngleProp.floatValue);
            requireLineOfSightProp.boolValue = EditorGUILayout.Toggle("Require Line Of Sight", requireLineOfSightProp.boolValue);
            if (requireLineOfSightProp.boolValue)
            {
                eyeHeightProp.floatValue = EditorGUILayout.FloatField("Eye Height", eyeHeightProp.floatValue);
                EditorGUILayout.PropertyField(lineOfSightBlockersProp, new GUIContent("Line Of Sight Blockers"));
                showLineOfSightGizmoProp.boolValue = EditorGUILayout.Toggle("Show Line Of Sight Gizmo", showLineOfSightGizmoProp.boolValue);
            }
            EditorGUILayout.PropertyField(playerLayersProp, new GUIContent("Player Layers"));
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
        walkSpeedProp.floatValue = EditorGUILayout.FloatField("Walk Speed", walkSpeedProp.floatValue);
        runSpeedProp.floatValue = EditorGUILayout.FloatField("Run Speed", runSpeedProp.floatValue);
        if (!isApex && !isHunter && !isPlunderer)
        {
            roamRadiusProp.floatValue = EditorGUILayout.FloatField("Roam Radius", roamRadiusProp.floatValue);
        }
        modelYawOffsetProp.floatValue = EditorGUILayout.FloatField("Model Yaw Offset", modelYawOffsetProp.floatValue);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Pathing", EditorStyles.boldLabel);
        roamDestinationAttemptsProp.intValue = EditorGUILayout.IntField("Roam Attempts", roamDestinationAttemptsProp.intValue);
        requireCompleteRoamPathProp.boolValue = EditorGUILayout.Toggle("Require Complete Path", requireCompleteRoamPathProp.boolValue);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
        if (isNeutral || isApex)
        {
            chaseDurationProp.floatValue = EditorGUILayout.FloatField("Chase Duration", chaseDurationProp.floatValue);
        }
        if (isNeutral || isApex)
        {
            loseSightChaseTimeoutProp.floatValue = EditorGUILayout.FloatField("Lose Sight Chase Timeout", loseSightChaseTimeoutProp.floatValue);
        }
        if (showReactionCooldown)
        {
            reactionCooldownSecondsProp.floatValue = EditorGUILayout.FloatField("Reaction Cooldown Seconds", reactionCooldownSecondsProp.floatValue);
        }
        if (isRoamer)
        {
            roamerFleeSecondsProp.floatValue = EditorGUILayout.FloatField("Roamer Flee Seconds", roamerFleeSecondsProp.floatValue);
            if (roamerPlungeChanceProp != null)
            {
                EditorGUILayout.Slider(roamerPlungeChanceProp, 0f, 1f, new GUIContent("Roamer Plunge Chance"));
            }
        }
        if (isHunter)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Hunter Cues", EditorStyles.boldLabel);
            hunterCueDelayMinProp.floatValue = EditorGUILayout.FloatField("HunterCueDelayMin", hunterCueDelayMinProp.floatValue);
            hunterCueDelayMaxProp.floatValue = EditorGUILayout.FloatField("HunterCueDelayMax", hunterCueDelayMaxProp.floatValue);
            hunterCueCountMinProp.intValue = EditorGUILayout.IntField("HunterCueCountMin", hunterCueCountMinProp.intValue);
            hunterCueCountMaxProp.intValue = EditorGUILayout.IntField("HunterCueCountMax", hunterCueCountMaxProp.intValue);
        }
        if (isPlunderer)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Plunderer", EditorStyles.boldLabel);
            EditorGUILayout.Slider(plundererBaseGrabChanceDayProp, 0f, 1f, new GUIContent("Grab Chance (Day)"));
            EditorGUILayout.Slider(plundererBaseGrabChanceNightProp, 0f, 1f, new GUIContent("Grab Chance (Night)"));
            EditorGUILayout.Slider(plundererHuntedBonusProp, 0f, 1f, new GUIContent("Hunted Bonus"));
            EditorGUILayout.Slider(plundererGroupedBonusProp, 0f, 1f, new GUIContent("Grouped Bonus"));
            EditorGUILayout.Slider(plundererChanceCapMaxProp, 0f, 1f, new GUIContent("Chance Cap"));
            EditorGUILayout.PropertyField(plundererFlightHeightProp, new GUIContent("Flight Height"));
            EditorGUILayout.PropertyField(plundererWaypointArriveDistanceProp, new GUIContent("Waypoint Arrive Distance"));
            EditorGUILayout.PropertyField(plundererCarryHoldSecondsProp, new GUIContent("Carry Hold Seconds"));
            EditorGUILayout.PropertyField(plundererDropSearchRadiusProp, new GUIContent("Drop Search Radius"));
            EditorGUILayout.PropertyField(plundererDropArriveDistanceProp, new GUIContent("Drop Arrive Distance"));
        }
        if (!isPlunderer)
        {
            EditorGUILayout.PropertyField(idleTimeProp);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Grounding", EditorStyles.boldLabel);
        snapToGroundProp.boolValue = EditorGUILayout.Toggle("Snap To Ground", snapToGroundProp.boolValue);
        if (snapToGroundProp.boolValue)
        {
            EditorGUILayout.PropertyField(groundLayersProp, new GUIContent("Ground Layers"));
            groundRayStartHeightProp.floatValue = EditorGUILayout.FloatField("Ray Start Height", groundRayStartHeightProp.floatValue);
            groundRayLengthProp.floatValue = EditorGUILayout.FloatField("Ray Length", groundRayLengthProp.floatValue);
            groundOffsetProp.floatValue = EditorGUILayout.FloatField("Ground Offset", groundOffsetProp.floatValue);
            groundSnapSpeedProp.floatValue = EditorGUILayout.FloatField("Snap Speed", groundSnapSpeedProp.floatValue);
            showGroundingGizmoProp.boolValue = EditorGUILayout.Toggle("Show Gizmo", showGroundingGizmoProp.boolValue);
            groundingGizmoColorProp.colorValue = EditorGUILayout.ColorField("Gizmo Color", groundingGizmoColorProp.colorValue);
        }

        if (!isPlunderer && !isPassive && !isNeutral)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(isIdleProp);
        }

        serializedObject.ApplyModifiedProperties();
    }
}

