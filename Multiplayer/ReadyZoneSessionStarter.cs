using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class ReadyZoneSessionStarter : NetworkBehaviour
    {
        [Header("Ready Zone")]
        [SerializeField] private Collider readyZone;
        [SerializeField] private bool requireAllConnectedPlayers = true;
        [SerializeField, Min(1)] private int requiredReadyCount = 1;

        [Header("Scene Selection")]
        [SerializeField, HideInInspector] private string guaranteedFirstScene = "Map1";
        [SerializeField, HideInInspector] private string[] randomScenes = new[] { "Map2", "Map3", "Map4" };
#if UNITY_EDITOR
        [SerializeField] private SceneAsset guaranteedFirstSceneAsset;
        [SerializeField] private SceneAsset[] randomSceneAssets;
#endif
        [SerializeField, Min(1)] private int switchAfterDay = 4;

        [Header("Transition")]
        [SerializeField, Min(0f)] private float fadeDuration = 1.25f;
        [SerializeField, Min(0f)] private float startDelay = 0.5f;

        private readonly HashSet<ulong> readyClients = new();
        private bool starting;
        private CanvasGroup fadeGroup;
        private GameObject fadeRoot;
        private bool shuttingDown;

        private void Reset()
        {
            readyZone = GetComponent<Collider>();
            if (readyZone != null)
            {
                readyZone.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (readyZone == null)
            {
                readyZone = GetComponent<Collider>();
            }

            if (readyZone != null)
            {
                readyZone.isTrigger = true;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (guaranteedFirstSceneAsset != null)
            {
                guaranteedFirstScene = guaranteedFirstSceneAsset.name;
            }

            if (randomSceneAssets != null)
            {
                randomScenes = new string[randomSceneAssets.Length];
                for (int i = 0; i < randomSceneAssets.Length; i++)
                {
                    randomScenes[i] = randomSceneAssets[i] != null ? randomSceneAssets[i].name : string.Empty;
                }
            }
        }
#endif

        private void OnTriggerEnter(Collider other)
        {
            if (!this || shuttingDown || !isActiveAndEnabled)
            {
                return;
            }

            PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
            if (player == null)
            {
                return;
            }

            ulong clientId = player.OwnerClientId;
            if (IsServer)
            {
                SetReady(clientId, true);
            }
            else if (IsClient)
            {
                SubmitReadyServerRpc(clientId, true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!this || shuttingDown || !isActiveAndEnabled)
            {
                return;
            }

            PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
            if (player == null)
            {
                return;
            }

            ulong clientId = player.OwnerClientId;
            if (IsServer)
            {
                SetReady(clientId, false);
            }
            else if (IsClient)
            {
                SubmitReadyServerRpc(clientId, false);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitReadyServerRpc(ulong clientId, bool ready)
        {
            if (!this || shuttingDown)
            {
                return;
            }

            SetReady(clientId, ready);
        }

        private void SetReady(ulong clientId, bool ready)
        {
            if (!this || shuttingDown || !IsServer)
            {
                return;
            }

            if (ready)
            {
                readyClients.Add(clientId);
            }
            else
            {
                readyClients.Remove(clientId);
            }

            int totalPlayers = GetTotalPlayers();
            int required = GetRequiredReadyCount(totalPlayers);
            Debug.Log($"Ready players: {readyClients.Count}/{totalPlayers}");

            if (!starting && readyClients.Count >= required)
            {
                starting = true;
                StartCoroutine(StartSessionRoutine());
            }
        }

        private int GetTotalPlayers()
        {
            if (NetworkManager != null && NetworkManager.IsListening)
            {
                return NetworkManager.ConnectedClientsList.Count;
            }

            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return players != null ? players.Length : 0;
        }

        private int GetRequiredReadyCount(int totalPlayers)
        {
            if (requireAllConnectedPlayers)
            {
                return Mathf.Max(1, totalPlayers);
            }

            return Mathf.Clamp(requiredReadyCount, 1, Mathf.Max(1, totalPlayers));
        }

        private IEnumerator StartSessionRoutine()
        {
            if (!this || shuttingDown)
            {
                yield break;
            }

            BeginFadeClientRpc(fadeDuration);

            float wait = Mathf.Max(0f, fadeDuration) + Mathf.Max(0f, startDelay);
            if (wait > 0f)
            {
                yield return new WaitForSeconds(wait);
            }

            if (!this || shuttingDown)
            {
                yield break;
            }

            string sceneName = ResolveTargetScene();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning("ReadyZoneSessionStarter: No valid scene name selected.");
                starting = false;
                yield break;
            }

            GameManager.Instance?.StartExpedition();

            if (NetworkManager != null && NetworkManager.IsListening)
            {
                if (!IsServer)
                {
                    Debug.LogWarning("ReadyZoneSessionStarter: Scene load requested from a non-server instance. Ignoring.");
                    starting = false;
                    yield break;
                }

                if (NetworkManager.SceneManager == null)
                {
                    Debug.LogError("ReadyZoneSessionStarter: Network scene manager is missing. Cannot load scenes in multiplayer.");
                    starting = false;
                    yield break;
                }

                NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            }
        }

        private string ResolveTargetScene()
        {
            int currentDay = 1;
            GameManager manager = GameManager.Instance;
            if (manager != null)
            {
                currentDay = Mathf.Max(1, manager.currentDay);
            }

            if (currentDay < switchAfterDay)
            {
                return guaranteedFirstScene;
            }

            if (randomScenes == null || randomScenes.Length == 0)
            {
                return guaranteedFirstScene;
            }

            int index = Random.Range(0, randomScenes.Length);
            return randomScenes[index];
        }

        [ClientRpc]
        private void BeginFadeClientRpc(float duration)
        {
            StartCoroutine(FadeRoutine(duration));
        }

        private IEnumerator FadeRoutine(float duration)
        {
            if (!this || shuttingDown)
            {
                yield break;
            }

            if (fadeGroup == null)
            {
                fadeGroup = CreateFadeCanvas();
            }

            if (fadeGroup == null)
            {
                yield break;
            }

            fadeGroup.alpha = 0f;
            float t = 0f;
            float d = Mathf.Max(0.01f, duration);
            while (t < d)
            {
                if (!this || shuttingDown || fadeGroup == null)
                {
                    yield break;
                }

                t += Time.unscaledDeltaTime;
                fadeGroup.alpha = Mathf.Clamp01(t / d);
                yield return null;
            }

            fadeGroup.alpha = 1f;
        }

        private CanvasGroup CreateFadeCanvas()
        {
            if (!this || shuttingDown)
            {
                return null;
            }

            fadeRoot = new GameObject("ReadyZoneFade");
            DontDestroyOnLoad(fadeRoot);
            Canvas canvas = fadeRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            CanvasGroup group = fadeRoot.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject imageObject = new GameObject("Fade");
            imageObject.transform.SetParent(fadeRoot.transform, false);
            Image img = imageObject.AddComponent<Image>();
            img.color = Color.black;
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return group;
        }

        private void OnDisable()
        {
            shuttingDown = true;
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            StopAllCoroutines();
            if (fadeRoot != null)
            {
                Destroy(fadeRoot);
                fadeRoot = null;
                fadeGroup = null;
            }
        }
    }
}
