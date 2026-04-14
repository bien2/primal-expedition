#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WalaPaNameHehe.EditorTools
{
    [InitializeOnLoad]
    public static class SceneTransitionSelectionGuard
    {
        static SceneTransitionSelectionGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SceneManager.activeSceneChanged += HandleActiveSceneChanged;
                SceneManager.sceneLoaded += HandleSceneLoaded;
                ClearSelection();
                return;
            }

            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
                SceneManager.sceneLoaded -= HandleSceneLoaded;
            }
        }

        private static void HandleActiveSceneChanged(Scene _, Scene __)
        {
            ClearSelection();
        }

        private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            ClearSelection();
        }

        private static void ClearSelection()
        {
            if (Selection.activeObject == null && (Selection.objects == null || Selection.objects.Length == 0))
            {
                return;
            }

            Selection.activeObject = null;
            Selection.objects = System.Array.Empty<Object>();
        }
    }
}
#endif

