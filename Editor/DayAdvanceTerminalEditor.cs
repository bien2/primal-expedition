using UnityEditor;
using UnityEngine;

namespace WalaPaNameHehe.EditorTools
{
    [CustomEditor(typeof(DayAdvanceTerminal))]
    public class DayAdvanceTerminalEditor : Editor
    {
        private SerializedProperty interactKeyProp;
        private SerializedProperty holdSecondsProp;
        private SerializedProperty interactDistanceProp;

        private SerializedProperty showUiProp;
        private SerializedProperty promptTextProp;
        private SerializedProperty progressBarSizeProp;
        private SerializedProperty progressBarYOffsetProp;
        private SerializedProperty progressBarBackgroundProp;
        private SerializedProperty progressBarFillProp;

        private SerializedProperty requireExtractionAvailableProp;
        private SerializedProperty proceedPromptProp;
        private SerializedProperty lockedPromptProp;
        private SerializedProperty endRunOnInteractProp;
        private SerializedProperty startRunOnInteractProp;
        private SerializedProperty startRunPromptProp;

        private bool showInteract = true;
        private bool showUi = true;
        private bool showGate = true;

        private enum TerminalMode
        {
            StartRun,
            EndRun
        }

        private void OnEnable()
        {
            interactKeyProp = serializedObject.FindProperty("interactKey");
            holdSecondsProp = serializedObject.FindProperty("holdSeconds");
            interactDistanceProp = serializedObject.FindProperty("interactDistance");

            showUiProp = serializedObject.FindProperty("showUi");
            promptTextProp = serializedObject.FindProperty("promptText");
            progressBarSizeProp = serializedObject.FindProperty("progressBarSize");
            progressBarYOffsetProp = serializedObject.FindProperty("progressBarYOffset");
            progressBarBackgroundProp = serializedObject.FindProperty("progressBarBackground");
            progressBarFillProp = serializedObject.FindProperty("progressBarFill");

            requireExtractionAvailableProp = serializedObject.FindProperty("requireExtractionAvailable");
            proceedPromptProp = serializedObject.FindProperty("proceedPrompt");
            lockedPromptProp = serializedObject.FindProperty("lockedPrompt");
            endRunOnInteractProp = serializedObject.FindProperty("endRunOnInteract");
            startRunOnInteractProp = serializedObject.FindProperty("startRunOnInteract");
            startRunPromptProp = serializedObject.FindProperty("startRunPrompt");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawModeSelector();
            EditorGUILayout.Space(6f);

            showInteract = EditorGUILayout.Foldout(showInteract, "Interact", true);
            if (showInteract)
            {
                EditorGUILayout.PropertyField(interactKeyProp);
                EditorGUILayout.PropertyField(holdSecondsProp);
                EditorGUILayout.PropertyField(interactDistanceProp);
            }

            EditorGUILayout.Space(6f);
            showUi = EditorGUILayout.Foldout(showUi, "UI", true);
            if (showUi)
            {
                EditorGUILayout.PropertyField(showUiProp);
                if (showUiProp.boolValue)
                {
                    DrawPromptFields();
                    EditorGUILayout.PropertyField(progressBarSizeProp);
                    EditorGUILayout.PropertyField(progressBarYOffsetProp);
                    EditorGUILayout.PropertyField(progressBarBackgroundProp);
                    EditorGUILayout.PropertyField(progressBarFillProp);
                }
            }

            EditorGUILayout.Space(6f);
            showGate = EditorGUILayout.Foldout(showGate, "Availability Gate", true);
            if (showGate)
            {
                EditorGUILayout.PropertyField(requireExtractionAvailableProp, new GUIContent("Require Extraction Available"));
                if (requireExtractionAvailableProp.boolValue)
                {
                    EditorGUILayout.PropertyField(lockedPromptProp);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModeSelector()
        {
            TerminalMode mode = GetMode();
            EditorGUILayout.LabelField("Mode", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int selected = GUILayout.Toolbar((int)mode, new[] { "Start Run", "End Run" });
            if (EditorGUI.EndChangeCheck())
            {
                SetMode((TerminalMode)selected);
            }

            if (startRunOnInteractProp.boolValue && endRunOnInteractProp.boolValue)
            {
                EditorGUILayout.HelpBox("Both Start Run and End Run are enabled. Choose only one.", MessageType.Warning);
            }
        }

        private void DrawPromptFields()
        {
            TerminalMode mode = GetMode();
            switch (mode)
            {
                case TerminalMode.StartRun:
                    EditorGUILayout.PropertyField(startRunPromptProp);
                    break;
                case TerminalMode.EndRun:
                    EditorGUILayout.PropertyField(proceedPromptProp);
                    break;
            }
        }

        private TerminalMode GetMode()
        {
            if (endRunOnInteractProp.boolValue)
            {
                return TerminalMode.EndRun;
            }

            return TerminalMode.StartRun;
        }

        private void SetMode(TerminalMode mode)
        {
            startRunOnInteractProp.boolValue = mode == TerminalMode.StartRun;
            endRunOnInteractProp.boolValue = mode == TerminalMode.EndRun;
        }
    }
}
