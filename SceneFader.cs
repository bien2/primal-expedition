using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WalaPaNameHehe
{
    [DisallowMultipleComponent]
    public sealed class SceneFader : MonoBehaviour
    {
        private static SceneFader instance;
        private CanvasGroup canvasGroup;
        private Coroutine fadeRoutine;
        private bool fadeInOnNextSceneLoad;
        private float nextFadeInDuration;

        public static void FadeIn(float durationSeconds)
        {
            SceneFader fader = EnsureInstance();
            if (fader == null)
            {
                return;
            }

            fader.fadeInOnNextSceneLoad = false;
            fader.StartFade(toAlpha: 0f, durationSeconds: durationSeconds);
        }

        public static void FadeOutAndPrepareFadeIn(float durationSeconds)
        {
            SceneFader fader = EnsureInstance();
            if (fader == null)
            {
                return;
            }

            fader.fadeInOnNextSceneLoad = durationSeconds > 0.01f;
            fader.nextFadeInDuration = durationSeconds;
            fader.StartFade(toAlpha: 1f, durationSeconds: durationSeconds);
        }

        private static SceneFader EnsureInstance()
        {
            if (Application.isBatchMode)
            {
                return null;
            }

            if (instance != null)
            {
                return instance;
            }

            GameObject root = new GameObject("SceneFader");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<SceneFader>();
            return instance;
        }

        private void InitializeCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;

            GameObject imageObject = new GameObject("Fade");
            imageObject.transform.SetParent(transform, false);
            Image img = imageObject.AddComponent<Image>();
            img.color = Color.black;

            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (canvasGroup == null)
            {
                InitializeCanvas();
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!fadeInOnNextSceneLoad)
            {
                return;
            }

            fadeInOnNextSceneLoad = false;
            StartFade(toAlpha: 0f, durationSeconds: nextFadeInDuration);
        }

        private void StartFade(float toAlpha, float durationSeconds)
        {
            if (canvasGroup == null)
            {
                return;
            }

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            float d = Mathf.Max(0f, durationSeconds);
            fadeRoutine = StartCoroutine(FadeRoutine(toAlpha, d));
        }

        private IEnumerator FadeRoutine(float toAlpha, float durationSeconds)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            float fromAlpha = canvasGroup.alpha;
            float t = 0f;
            float d = Mathf.Max(0.01f, durationSeconds);

            if (durationSeconds <= 0.001f)
            {
                canvasGroup.alpha = toAlpha;
                UpdateRaycastBlock();
                fadeRoutine = null;
                yield break;
            }

            while (t < d)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(t / d));
                UpdateRaycastBlock();
                yield return null;
            }

            canvasGroup.alpha = toAlpha;
            UpdateRaycastBlock();
            fadeRoutine = null;
        }

        private void UpdateRaycastBlock()
        {
            if (canvasGroup == null)
            {
                return;
            }

            bool shouldBlock = canvasGroup.alpha > 0.001f;
            canvasGroup.blocksRaycasts = shouldBlock;
            canvasGroup.interactable = false;
        }
    }
}
