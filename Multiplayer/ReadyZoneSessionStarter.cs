using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
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

        [Header("Seats")]
        [SerializeField] private Transform[] seatPoints = new Transform[4];
        [SerializeField] private float seatYawOffsetDegrees = -90f;

        [Header("UI - Prompt")]
        [SerializeField] private bool showSeatPrompt = true;
        [SerializeField] private Vector2 promptSize = new Vector2(360f, 36f);
        [SerializeField] private string sitPromptText = "Press F to Sit";
        [SerializeField] private string standPromptText = "Press F to Stand";

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

        [Header("Takeoff")]
        [SerializeField, Min(0f)] private float takeoffSeconds = 3f;
        [SerializeField] private float takeoffUpSpeed = 1.5f;
        [SerializeField] private float takeoffForwardSpeed = 4f;
        [SerializeField] private Transform helicopterRoot;

        [Header("Rotor")]
        [SerializeField] private MotorSpin mainRotor;
        [SerializeField, Min(0f)] private float rotorStartupSeconds = 2f;

        private readonly HashSet<ulong> readyClients = new();
        private readonly HashSet<ulong> clientsInZone = new();
        private readonly Dictionary<ulong, int> seatIndexByClient = new();
        private readonly Dictionary<ulong, int> preferredSeatIndexByClient = new();
        private readonly ulong[] seatClientIds = new ulong[4];
        private readonly bool[] seatHasClient = new bool[4];
        private PlayerMovement localPlayer;
        private bool localIsInZone;
        private bool starting;
        private bool takeoffInProgress;
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

            if (helicopterRoot == null)
            {
                helicopterRoot = transform;
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
                clientsInZone.Add(clientId);
            }

            if (player.IsOwner)
            {
                localPlayer = player;
                localIsInZone = true;
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
                clientsInZone.Remove(clientId);
            }

            if (player.IsOwner && localPlayer == player)
            {
                localIsInZone = false;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestToggleSeatServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!this || shuttingDown || !IsServer)
            {
                return;
            }

            if (starting || takeoffInProgress)
            {
                return;
            }

            ulong clientId = rpcParams.Receive.SenderClientId;
            bool isSeated = seatIndexByClient.ContainsKey(clientId);
            if (!isSeated && !clientsInZone.Contains(clientId))
            {
                return;
            }

            if (isSeated)
            {
                UnseatClient(clientId);
                return;
            }

            SeatClient(clientId);
        }

        private void Update()
        {
            if (!this || shuttingDown || !isActiveAndEnabled)
            {
                return;
            }

            if (starting || takeoffInProgress)
            {
                return;
            }

            if (Keyboard.current == null || !Keyboard.current.fKey.wasPressedThisFrame)
            {
                return;
            }

            if (localPlayer == null)
            {
                return;
            }

            bool canToggle = localIsInZone || localPlayer.IsSeated;
            if (!canToggle)
            {
                return;
            }

            if (localPlayer.IsInteractionLocked)
            {
                return;
            }

            if (NetworkManager != null && NetworkManager.IsListening)
            {
                RequestToggleSeatServerRpc();
            }
        }

        private void OnGUI()
        {
            if (!showSeatPrompt || !this || shuttingDown || !isActiveAndEnabled)
            {
                return;
            }

            if (localPlayer == null || localPlayer.IsInteractionLocked)
            {
                return;
            }

            bool canShow = localPlayer.IsSeated || localIsInZone;
            if (!canShow)
            {
                return;
            }

            string text = localPlayer.IsSeated ? standPromptText : sitPromptText;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            GUIStyle centered = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 20
            };

            float x = (Screen.width - promptSize.x) * 0.5f;
            float y = (Screen.height * 0.5f) + 24f;
            Rect promptArea = new Rect(x, y, promptSize.x, promptSize.y);
            GUI.Label(promptArea, text, centered);
        }

        private void SeatClient(ulong clientId)
        {
            if (!clientsInZone.Contains(clientId))
            {
                return;
            }

            int seatIndex = -1;
            if (preferredSeatIndexByClient.TryGetValue(clientId, out int preferredIndex))
            {
                if (IsSeatIndexAvailable(preferredIndex))
                {
                    seatIndex = preferredIndex;
                }
            }

            if (seatIndex < 0)
            {
                seatIndex = FindAvailableSeatIndex();
            }
            if (seatIndex < 0)
            {
                return;
            }

            ulong playerNetworkObjectId = GetPlayerNetworkObjectId(clientId);
            if (playerNetworkObjectId == 0)
            {
                return;
            }

            seatIndexByClient[clientId] = seatIndex;
            seatClientIds[seatIndex] = clientId;
            seatHasClient[seatIndex] = true;
            preferredSeatIndexByClient[clientId] = seatIndex;

            SetReady(clientId, true);

            ApplySeatClientRpc(playerNetworkObjectId, seatIndex, true);
        }

        private void UnseatClient(ulong clientId)
        {
            if (!seatIndexByClient.TryGetValue(clientId, out int seatIndex))
            {
                return;
            }

            ulong playerNetworkObjectId = GetPlayerNetworkObjectId(clientId);

            seatIndexByClient.Remove(clientId);
            preferredSeatIndexByClient[clientId] = seatIndex;

            if (seatIndex >= 0 && seatIndex < seatHasClient.Length)
            {
                seatHasClient[seatIndex] = false;
                seatClientIds[seatIndex] = 0;
            }

            SetReady(clientId, false);
            if (playerNetworkObjectId == 0)
            {
                return;
            }

            ApplySeatClientRpc(playerNetworkObjectId, seatIndex, false);
        }

        private ulong GetPlayerNetworkObjectId(ulong clientId)
        {
            if (!IsServer)
            {
                return 0;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null)
            {
                return 0;
            }

            NetworkObject playerObject = nm.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject == null)
            {
                return 0;
            }

            return playerObject.NetworkObjectId;
        }

        private int FindAvailableSeatIndex()
        {
            int max = Mathf.Min(4, seatPoints != null ? seatPoints.Length : 0);
            max = Mathf.Min(max, seatHasClient.Length);

            for (int i = 0; i < max; i++)
            {
                if (IsSeatIndexAvailable(i))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsSeatIndexAvailable(int seatIndex)
        {
            if (seatPoints == null)
            {
                return false;
            }

            if (seatIndex < 0 || seatIndex >= seatPoints.Length)
            {
                return false;
            }

            if (seatIndex >= seatHasClient.Length)
            {
                return false;
            }

            if (seatPoints[seatIndex] == null)
            {
                return false;
            }

            return !seatHasClient[seatIndex];
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
            Debug.Log($"Seated players: {readyClients.Count}/{totalPlayers}");

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

            takeoffInProgress = true;

            float startup = Mathf.Max(0f, rotorStartupSeconds);
            if (mainRotor != null)
            {
                if (NetworkManager != null && NetworkManager.IsListening)
                {
                    if (IsServer)
                    {
                        BeginRotorStartupClientRpc(startup);
                    }
                }

                mainRotor.StartStartup(startup);
                if (startup > 0f)
                {
                    yield return new WaitForSecondsRealtime(startup);
                }
            }

            float flySeconds = Mathf.Max(0f, takeoffSeconds);
            if (flySeconds > 0f)
            {
                float upSpeed = takeoffUpSpeed;
                float forwardSpeed = takeoffForwardSpeed;

                if (NetworkManager != null && NetworkManager.IsListening)
                {
                    if (IsServer)
                    {
                        BeginTakeoffClientRpc(flySeconds, upSpeed, forwardSpeed);
                        yield return RunTakeoffRoutine(flySeconds, upSpeed, forwardSpeed);
                    }
                }
                else
                {
                    yield return RunTakeoffRoutine(flySeconds, upSpeed, forwardSpeed);
                }
            }
            takeoffInProgress = false;

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

            int totalPlayers = GetTotalPlayers();
            int required = GetRequiredReadyCount(totalPlayers);
            if (NetworkManager != null && NetworkManager.IsListening && IsServer && readyClients.Count < required)
            {
                starting = false;
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

        private IEnumerator RunTakeoffRoutine(float seconds, float upSpeed, float forwardSpeed)
        {
            Transform root = helicopterRoot != null ? helicopterRoot : transform;
            float time = 0f;
            float duration = Mathf.Max(0f, seconds);
            while (time < duration)
            {
                if (!this || shuttingDown)
                {
                    yield break;
                }

                Vector3 delta = (root.up * upSpeed + root.forward * forwardSpeed) * Time.deltaTime;
                root.position += delta;

                time += Time.deltaTime;
                yield return null;
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

        [ClientRpc]
        private void BeginTakeoffClientRpc(float seconds, float upSpeed, float forwardSpeed)
        {
            if (IsServer)
            {
                return;
            }

            if (helicopterRoot == null)
            {
                helicopterRoot = transform;
            }

            NetworkObject no = helicopterRoot != null ? helicopterRoot.GetComponentInParent<NetworkObject>() : null;
            NetworkTransform nt = no != null ? no.GetComponent<NetworkTransform>() : null;
            if (no != null && no.IsSpawned && nt != null)
            {
                return;
            }

            StartCoroutine(RunTakeoffRoutine(seconds, upSpeed, forwardSpeed));
        }

        [ClientRpc]
        private void BeginRotorStartupClientRpc(float seconds)
        {
            if (IsServer)
            {
                return;
            }

            if (mainRotor == null)
            {
                return;
            }

            mainRotor.StartStartup(seconds);
        }

        [ClientRpc]
        private void ApplySeatClientRpc(ulong playerNetworkObjectId, int seatIndex, bool seated)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null)
            {
                return;
            }

            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerObject) || playerObject == null)
            {
                return;
            }

            PlayerMovement player = playerObject.GetComponent<PlayerMovement>();
            if (player == null)
            {
                return;
            }

            Transform seat = null;
            if (seated && seatPoints != null && seatIndex >= 0 && seatIndex < seatPoints.Length)
            {
                seat = seatPoints[seatIndex];
            }

            player.SetSeated(seated, seat, seatYawOffsetDegrees);
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
