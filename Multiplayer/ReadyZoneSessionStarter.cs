using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
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

            float duration = Mathf.Max(0f, fadeDuration);
            GameManager manager = GameManager.Instance;
            if (manager != null)
            {
                duration = Mathf.Max(0f, manager.SceneFadeDurationSeconds);
            }

            if (NetworkManager != null && NetworkManager.IsListening)
            {
                if (IsServer)
                {
                    BeginFadeClientRpc(duration);
                }
            }
            else
            {
                WalaPaNameHehe.SceneFader.FadeOutAndPrepareFadeIn(duration);
            }

            float wait = duration + Mathf.Max(0f, startDelay);
            if (wait > 0f)
            {
                yield return new WaitForSecondsRealtime(wait);
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
            WalaPaNameHehe.SceneFader.FadeOutAndPrepareFadeIn(duration);
        }

        private void OnDisable()
        {
            shuttingDown = true;
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            StopAllCoroutines();
        }
    }
}
