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
    private SerializedProperty hunterCueDelayMinProp;
    private SerializedProperty hunterCueDelayMaxProp;
    private SerializedProperty hunterCueCountMinProp;
    private SerializedProperty hunterCueCountMaxProp;
    private SerializedProperty hunterForceHuntTestProp;
    private SerializedProperty hunterForceHuntPlayCuesProp;
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
    private SerializedProperty enableAttackKillProp;
    private SerializedProperty attackKillRadiusProp;
    private SerializedProperty attackCheckIntervalProp;
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
        hunterCueDelayMinProp = serializedObject.FindProperty("hunterCueDelayMin");
        hunterCueDelayMaxProp = serializedObject.FindProperty("hunterCueDelayMax");
        hunterCueCountMinProp = serializedObject.FindProperty("hunterCueCountMin");
        hunterCueCountMaxProp = serializedObject.FindProperty("hunterCueCountMax");
        hunterForceHuntTestProp = serializedObject.FindProperty("hunterForceHuntTest");
        hunterForceHuntPlayCuesProp = serializedObject.FindProperty("hunterForceHuntPlayCues");
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
        enableAttackKillProp = serializedObject.FindProperty("enableAttackKill");
        attackKillRadiusProp = serializedObject.FindProperty("attackKillRadius");
        attackCheckIntervalProp = serializedObject.FindProperty("attackCheckInterval");
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
        bool showAttackSettings = isNeutral || isApex || isHunter || isPlunderer;
        bool showReactionCooldown = (isNeutral || isApex) && !isPlunderer;
        bool showPerceptionCore = !isPassive && !isPlunderer && !isHunter;
        bool showSoundPerception = !isPassive && !isNeutral && !isPlunderer && !isHunter;

        if (showPerceptionCore)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Perception", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(detectionRadiusProp);

            if (!isPlunderer)
            {
                if (isNeutral)
                {
                    EditorGUILayout.PropertyField(neutralReactDelayMinProp, new GUIContent("Neutral React Delay Min"));
                    EditorGUILayout.PropertyField(neutralReactDelayMaxProp, new GUIContent("Neutral React Delay Max"));
                }

                if (showSoundPerception)
                {
                    EditorGUILayout.PropertyField(canHearSoundsProp, new GUIContent("React To Sounds"));
                    if (canHearSoundsProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(soundReactionRadiusProp);
                    }
                }
            }

            EditorGUILayout.PropertyField(viewAngleProp);
            EditorGUILayout.PropertyField(requireLineOfSightProp);
            if (requireLineOfSightProp.boolValue)
            {
                EditorGUILayout.PropertyField(eyeHeightProp);
                EditorGUILayout.PropertyField(lineOfSightBlockersProp);
                EditorGUILayout.PropertyField(showLineOfSightGizmoProp);
            }
            EditorGUILayout.PropertyField(playerLayersProp);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(walkSpeedProp);
        EditorGUILayout.PropertyField(runSpeedProp);
        if (!isApex && !isHunter && !isPlunderer)
        {
            EditorGUILayout.PropertyField(roamRadiusProp);
        }
        EditorGUILayout.PropertyField(modelYawOffsetProp, new GUIContent("Model Yaw Offset"));

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Pathing", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(roamDestinationAttemptsProp, new GUIContent("Roam Attempts"));
        EditorGUILayout.PropertyField(requireCompleteRoamPathProp, new GUIContent("Require Complete Path"));

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
        if (isNeutral || isApex)
        {
            EditorGUILayout.PropertyField(chaseDurationProp);
        }
        if (isNeutral || isApex)
        {
            EditorGUILayout.PropertyField(loseSightChaseTimeoutProp);
        }
        if (showReactionCooldown)
        {
            EditorGUILayout.PropertyField(reactionCooldownSecondsProp, new GUIContent("Reaction Cooldown Seconds"));
        }
        if (isRoamer)
        {
            EditorGUILayout.PropertyField(roamerFleeSecondsProp, new GUIContent("Roamer Flee Seconds"));
        }
        if (isHunter)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Hunter Cues", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(hunterCueDelayMinProp, new GUIContent("HunterCueDelayMin"));
            EditorGUILayout.PropertyField(hunterCueDelayMaxProp, new GUIContent("HunterCueDelayMax"));
            EditorGUILayout.PropertyField(hunterCueCountMinProp, new GUIContent("HunterCueCountMin"));
            EditorGUILayout.PropertyField(hunterCueCountMaxProp, new GUIContent("HunterCueCountMax"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Hunter Test", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(hunterForceHuntTestProp, new GUIContent("Force Hunt Test"));
            if (hunterForceHuntTestProp.boolValue)
            {
                EditorGUILayout.PropertyField(hunterForceHuntPlayCuesProp, new GUIContent("Play Test Cues"));
            }
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

        if (showAttackSettings)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Attack", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableAttackKillProp, new GUIContent("Enable Attack Kill"));
            if (enableAttackKillProp.boolValue)
            {
                EditorGUILayout.PropertyField(attackKillRadiusProp, new GUIContent("Attack Kill Radius"));
                EditorGUILayout.PropertyField(attackCheckIntervalProp, new GUIContent("Attack Check Interval"));
            }
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Grounding", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(snapToGroundProp, new GUIContent("Snap To Ground"));
        if (snapToGroundProp.boolValue)
        {
            EditorGUILayout.PropertyField(groundLayersProp, new GUIContent("Ground Layers"));
            EditorGUILayout.PropertyField(groundRayStartHeightProp, new GUIContent("Ray Start Height"));
            EditorGUILayout.PropertyField(groundRayLengthProp, new GUIContent("Ray Length"));
            EditorGUILayout.PropertyField(groundOffsetProp, new GUIContent("Ground Offset"));
            EditorGUILayout.PropertyField(groundSnapSpeedProp, new GUIContent("Snap Speed"));
            EditorGUILayout.PropertyField(showGroundingGizmoProp, new GUIContent("Show Gizmo"));
            EditorGUILayout.PropertyField(groundingGizmoColorProp, new GUIContent("Gizmo Color"));
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

