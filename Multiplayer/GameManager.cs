using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class GameManager : NetworkBehaviour
    {
        public enum ExpeditionState
        {
            WaitingToStart,
            Exploring,
            ExtractionAvailable,
            ExtractionCalled,
            ExpeditionComplete,
            ExpeditionFailed
        }

        public static GameManager Instance { get; private set; }

        [Header("Expedition Settings")]
        [SerializeField] private int startingDay = 1;
        [SerializeField] private int defaultRequiredSamples = 5;
        [SerializeField] private float helicopterExtractionDelay = 30f;

        [Header("Blood Sample Threshold (Per Day)")]
        [SerializeField] private bool usePerDayRequiredSamples = true;
        [Min(1)] [SerializeField] private int requiredSamplesDay1 = 5;
        [Min(1)] [SerializeField] private int requiredSamplesDay2 = 7;
        [Min(1)] [SerializeField] private int requiredSamplesDay3 = 9;
        [Min(1)] [SerializeField] private int requiredSamplesDay4 = 11;

        [Header("Run Flow")]
        [SerializeField] private bool endRunWhenAllDead = true;
        [SerializeField] private float allDeadGraceSeconds = 2f;
        [SerializeField] private bool resetToDay1WhenAllPlayersDead = true;
        [SerializeField] private bool allowManualExit = true;
        [SerializeField] private bool manualExitCountsAsSuccess = true;
#if ENABLE_INPUT_SYSTEM
        [SerializeField] private UnityEngine.InputSystem.Key manualExitKey = UnityEngine.InputSystem.Key.F10;
#else
        [SerializeField] private KeyCode manualExitKey = KeyCode.F10;
#endif

        [Header("Scene Flow")]
        [SerializeField] private bool returnToLobbyOnRunEnd = true;
#if UNITY_EDITOR
        [SerializeField] private SceneAsset lobbySceneAsset;
#endif
        [SerializeField, HideInInspector] private string lobbySceneName = "Lobby";
        [Min(0f)] [SerializeField] private float returnToLobbyDelaySeconds = 0.25f;
        [Min(0f)] [SerializeField] private float sceneFadeDurationSeconds = 1.25f;

        private readonly NetworkVariable<int> networkCurrentDay = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> networkRequiredSamples = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> networkCollectedSamples = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> networkIsExtractionAvailable = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> networkExtractionTimeRemaining = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ExpeditionState> networkExpeditionState = new(writePerm: NetworkVariableWritePermission.Server);

        private int localCurrentDay;
        private int localRequiredSamples;
        private int localCollectedSamples;
        private bool localIsExtractionAvailable;
        private float localExtractionTimeRemaining;
        private ExpeditionState localExpeditionState;

        private Coroutine extractionCoroutine;
        private float allDeadTimer;
        private bool returningToLobby;

        public int currentDay => UseNetworkState ? networkCurrentDay.Value : localCurrentDay;
        public int requiredSamples => UseNetworkState ? networkRequiredSamples.Value : localRequiredSamples;
        public int collectedSamples => UseNetworkState ? networkCollectedSamples.Value : localCollectedSamples;
        public bool isExtractionAvailable => UseNetworkState ? networkIsExtractionAvailable.Value : localIsExtractionAvailable;
        public float extractionTimeRemaining => UseNetworkState ? networkExtractionTimeRemaining.Value : localExtractionTimeRemaining;
        public ExpeditionState CurrentState => UseNetworkState ? networkExpeditionState.Value : localExpeditionState;
        public bool IsReturningToLobby => returningToLobby;
        public float SceneFadeDurationSeconds => sceneFadeDurationSeconds;

        private NetworkManager ActiveNetworkManager
        {
            get
            {
                // NetworkBehaviour.NetworkManager can be null if this object isn't spawned yet.
                // For scene flow we want the actual session manager.
                return NetworkManager.Singleton != null ? NetworkManager.Singleton : NetworkManager;
            }
        }

        private bool UseNetworkState => ActiveNetworkManager != null && ActiveNetworkManager.IsListening;

        private bool HasAuthority
        {
            get
            {
                if (!UseNetworkState)
                {
                    return true;
                }

                // If we are spawned, rely on NGO's authoritative properties.
                if (IsSpawned)
                {
                    return IsServer;
                }

                // Fallback for early scene transitions while still in a networked session.
                return ActiveNetworkManager != null && ActiveNetworkManager.IsServer;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeLocalState();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkExpeditionState.OnValueChanged += OnExpeditionStateChanged;

            if (IsServer)
            {
                SyncLocalStateToNetwork();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            networkExpeditionState.OnValueChanged -= OnExpeditionStateChanged;
            CacheNetworkStateLocally();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            networkExpeditionState.OnValueChanged -= OnExpeditionStateChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!returningToLobby)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(lobbySceneName) && scene.name == lobbySceneName)
            {
                returningToLobby = false;
                if (UseNetworkState && HasAuthority)
                {
                    StartCoroutine(RespawnPlayersInLobbyRoutine());
                }
            }
        }

        private IEnumerator RespawnPlayersInLobbyRoutine()
        {
            if (!UseNetworkState)
            {
                yield break;
            }

            NetworkManager nm = ActiveNetworkManager;
            if (nm == null || !nm.IsListening || !nm.IsServer)
            {
                yield break;
            }

            // Allow scene objects (spawn point) time to enable.
            yield return null;

            PlayerSpawnPoint spawnPoint = FindLobbySpawnPoint();
            if (spawnPoint == null)
            {
                yield break;
            }

            foreach (var kvp in nm.ConnectedClients)
            {
                NetworkClient client = kvp.Value;
                if (client == null || client.PlayerObject == null)
                {
                    continue;
                }

                NetworkObject playerObject = client.PlayerObject;
                TeleportNetworkObject(playerObject, spawnPoint.transform.position, spawnPoint.transform.rotation);

                PlayerMovement movement = playerObject.GetComponentInChildren<PlayerMovement>(true);
                if (movement != null)
                {
                    movement.ServerForceRevive();
                }
            }
        }

        private PlayerSpawnPoint FindLobbySpawnPoint()
        {
            if (string.IsNullOrWhiteSpace(lobbySceneName))
            {
                return null;
            }

            Scene lobbyScene = SceneManager.GetSceneByName(lobbySceneName);
            PlayerSpawnPoint[] spawnPoints = FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                PlayerSpawnPoint candidate = spawnPoints[i];
                if (candidate == null)
                {
                    continue;
                }

                if (lobbyScene.IsValid() && candidate.gameObject.scene != lobbyScene)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static void TeleportNetworkObject(NetworkObject playerObject, Vector3 position, Quaternion rotation)
        {
            if (playerObject == null)
            {
                return;
            }

            Transform t = playerObject.transform;
            if (t == null)
            {
                return;
            }

            Rigidbody rb = playerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = position;
                rb.rotation = rotation;
            }

            Unity.Netcode.Components.NetworkTransform netTransform = playerObject.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (netTransform != null)
            {
                try
                {
                    netTransform.Teleport(position, rotation, t.localScale);
                    return;
                }
                catch (System.Exception)
                {
                }
            }

            t.SetPositionAndRotation(position, rotation);
        }

        private void Update()
        {
            if (!HasAuthority)
            {
                return;
            }

            if (endRunWhenAllDead)
            {
                EvaluateAllPlayersDead();
            }

            if (allowManualExit && !Application.isBatchMode && IsManualExitPressed())
            {
                EndRunManually();
            }
        }

        public void StartExpedition()
        {
            if (!HasAuthority)
            {
                StartExpeditionServerRpc();
                return;
            }

            returningToLobby = false;

            if (CurrentState != ExpeditionState.WaitingToStart)
            {
                LogMessage($"StartExpedition ignored. Current state: {CurrentState}");
                return;
            }

            StopExtractionTimer();

            SetCurrentDay(Mathf.Max(1, currentDay));
            SetRequiredSamples(GetRequiredSamplesForDay(currentDay));
            SetCollectedSamples(0);
            SetExtractionAvailable(false);
            SetExtractionTimeRemaining(0f);
            SetState(ExpeditionState.Exploring);

            DayNightTimer dayNightTimer = FindFirstObjectByType<DayNightTimer>(FindObjectsInactive.Exclude);
            if (dayNightTimer != null)
            {
                dayNightTimer.SetDay(currentDay);
            }

            HunterMeterManager.Instance?.InitializeRun();
            LogMessage($"Session started. Day {currentDay}. Required samples: {requiredSamples}");
        }

        public void AddSample()
        {
            if (!HasAuthority)
            {
                AddSampleServerRpc();
                return;
            }

            if (CurrentState != ExpeditionState.Exploring && CurrentState != ExpeditionState.ExtractionAvailable)
            {
                LogMessage($"AddSample ignored. Current state: {CurrentState}");
                return;
            }

            int newSampleCount = collectedSamples + 1;
            SetCollectedSamples(newSampleCount);

            LogMessage($"Sample collected: {collectedSamples} / {requiredSamples}");

            if (!isExtractionAvailable && collectedSamples >= requiredSamples)
            {
                SetExtractionAvailable(true);
                SetState(ExpeditionState.ExtractionAvailable);
                LogMessage("Extraction available");
            }
        }

        public void CallExtraction()
        {
            if (!HasAuthority)
            {
                CallExtractionServerRpc();
                return;
            }

            if (!isExtractionAvailable || CurrentState != ExpeditionState.ExtractionAvailable)
            {
                LogMessage("CallExtraction ignored. Extraction is not available yet.");
                return;
            }

            SetState(ExpeditionState.ExtractionCalled);
            StartExtractionTimer();
            LogMessage($"Helicopter arriving in {Mathf.CeilToInt(helicopterExtractionDelay)} seconds");
        }

        public void CompleteExpedition()
        {
            if (!HasAuthority)
            {
                CompleteExpeditionServerRpc();
                return;
            }

            if (CurrentState == ExpeditionState.ExpeditionComplete || CurrentState == ExpeditionState.ExpeditionFailed)
            {
                return;
            }

            StopExtractionTimer();
            SetExtractionTimeRemaining(0f);
            SetState(ExpeditionState.ExpeditionComplete);
            AdvanceDayInternal();
            SetState(ExpeditionState.WaitingToStart);

            LogMessage("Expedition Complete");
            TryReturnToLobby();
        }

        public void FailExpedition()
        {
            FailExpeditionInternal(resetToDay1: false);
        }

        private void FailExpeditionInternal(bool resetToDay1)
        {
            if (!HasAuthority)
            {
                FailExpeditionServerRpc();
                return;
            }

            if (CurrentState == ExpeditionState.ExpeditionComplete || CurrentState == ExpeditionState.ExpeditionFailed)
            {
                return;
            }

            StopExtractionTimer();
            SetExtractionTimeRemaining(0f);
            SetState(ExpeditionState.ExpeditionFailed);
            if (resetToDay1)
            {
                SetCurrentDay(1);
                SetRequiredSamples(GetRequiredSamplesForDay(1));
                SetCollectedSamples(0);
                SetExtractionAvailable(false);
            }
            else
            {
                AdvanceDayInternal();
            }
            SetState(ExpeditionState.WaitingToStart);

            LogMessage("Expedition Failed");
            TryReturnToLobby();
        }

        private void TryReturnToLobby()
        {
            if (!returnToLobbyOnRunEnd || returningToLobby)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(lobbySceneName))
            {
                return;
            }

            returningToLobby = true;
            StopAllCoroutines();
            extractionCoroutine = null;
            StartCoroutine(ReturnToLobbyRoutine());
        }

        private IEnumerator ReturnToLobbyRoutine()
        {
            bool useNetwork = UseNetworkState;
            NetworkManager nm = useNetwork ? ActiveNetworkManager : null;
            if (useNetwork)
            {
                if (nm == null || !nm.IsListening || !nm.IsServer || nm.SceneManager == null)
                {
                    returningToLobby = false;
                    yield break;
                }
            }

            float delay = Mathf.Max(0f, returnToLobbyDelaySeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            ClearInventoriesBeforeLobby();

            float fadeDuration = Mathf.Max(0f, sceneFadeDurationSeconds);
            if (fadeDuration > 0.01f && !Application.isBatchMode)
            {
                if (useNetwork)
                {
                    BeginSceneFadeClientRpc(fadeDuration);
                }
                else
                {
                    WalaPaNameHehe.SceneFader.FadeOutAndPrepareFadeIn(fadeDuration);
                }

                yield return new WaitForSecondsRealtime(fadeDuration);
            }

            if (useNetwork)
            {
                nm = ActiveNetworkManager;
                if (nm == null || !nm.IsListening || !nm.IsServer || nm.SceneManager == null)
                {
                    returningToLobby = false;
                    if (fadeDuration > 0.01f && !Application.isBatchMode)
                    {
                        WalaPaNameHehe.SceneFader.FadeIn(fadeDuration);
                    }
                    yield break;
                }

                nm.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
                yield break;
            }

            SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        }

        [ClientRpc]
        private void BeginSceneFadeClientRpc(float duration)
        {
            WalaPaNameHehe.SceneFader.FadeOutAndPrepareFadeIn(duration);
        }

        private void ClearInventoriesBeforeLobby()
        {
            if (!HasAuthority)
            {
                return;
            }

            InventorySystem[] inventories = FindObjectsByType<InventorySystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (inventories == null)
            {
                return;
            }

            for (int i = 0; i < inventories.Length; i++)
            {
                InventorySystem inv = inventories[i];
                if (inv == null)
                {
                    continue;
                }

                inv.ClearInventoryForLobby();
            }
        }

        public void NextDay()
        {
            if (!HasAuthority)
            {
                NextDayServerRpc();
                return;
            }

            AdvanceDayInternal();
            LogMessage($"Advancing to Day {currentDay}");
        }

        private void EvaluateAllPlayersDead()
        {
            if (CurrentState != ExpeditionState.Exploring &&
                CurrentState != ExpeditionState.ExtractionAvailable &&
                CurrentState != ExpeditionState.ExtractionCalled)
            {
                allDeadTimer = 0f;
                return;
            }

            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (players == null || players.Length == 0)
            {
                allDeadTimer = 0f;
                return;
            }

            bool hasRelevantPlayers = false;
            bool anyAlive = false;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerMovement player = players[i];
                if (player == null)
                {
                    continue;
                }

                if (UseNetworkState)
                {
                    if (!player.IsSpawned)
                    {
                        continue;
                    }
                }
                else if (!player.gameObject.activeInHierarchy)
                {
                    continue;
                }

                hasRelevantPlayers = true;
                if (!player.IsDead)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!hasRelevantPlayers)
            {
                allDeadTimer = 0f;
                return;
            }

            if (anyAlive)
            {
                allDeadTimer = 0f;
                return;
            }

            allDeadTimer += Time.deltaTime;
            if (allDeadTimer >= Mathf.Max(0.1f, allDeadGraceSeconds))
            {
                allDeadTimer = 0f;
                FailExpeditionInternal(resetToDay1WhenAllPlayersDead);
            }
        }

        private void EndRunManually()
        {
            if (CurrentState == ExpeditionState.ExpeditionComplete || CurrentState == ExpeditionState.ExpeditionFailed)
            {
                return;
            }

            if (manualExitCountsAsSuccess)
            {
                CompleteExpedition();
            }
            else
            {
                FailExpedition();
            }
        }

        private bool IsManualExitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (!System.Enum.IsDefined(typeof(UnityEngine.InputSystem.Key), manualExitKey))
            {
                manualExitKey = UnityEngine.InputSystem.Key.F10;
            }

            if (UnityEngine.InputSystem.Keyboard.current == null)
            {
                return false;
            }

            return UnityEngine.InputSystem.Keyboard.current[manualExitKey].wasPressedThisFrame;
#else
            return Input.GetKeyDown(manualExitKey);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
#if ENABLE_INPUT_SYSTEM
            if (!System.Enum.IsDefined(typeof(UnityEngine.InputSystem.Key), manualExitKey))
            {
                manualExitKey = UnityEngine.InputSystem.Key.F10;
            }
#endif

            if (lobbySceneAsset != null)
            {
                lobbySceneName = lobbySceneAsset.name;
            }
        }
#endif

        private void InitializeLocalState()
        {
            localCurrentDay = Mathf.Max(1, startingDay);
            localRequiredSamples = GetRequiredSamplesForDay(localCurrentDay);
            localCollectedSamples = 0;
            localIsExtractionAvailable = false;
            localExtractionTimeRemaining = 0f;
            localExpeditionState = ExpeditionState.WaitingToStart;
        }

        private int GetRequiredSamplesForDay(int day)
        {
            if (!usePerDayRequiredSamples)
            {
                return Mathf.Max(1, defaultRequiredSamples);
            }

            int safeDay = Mathf.Max(1, day);
            int clampedDay = Mathf.Min(4, safeDay);
            return clampedDay switch
            {
                1 => Mathf.Max(1, requiredSamplesDay1),
                2 => Mathf.Max(1, requiredSamplesDay2),
                3 => Mathf.Max(1, requiredSamplesDay3),
                _ => Mathf.Max(1, requiredSamplesDay4),
            };
        }

        private void SyncLocalStateToNetwork()
        {
            networkCurrentDay.Value = localCurrentDay;
            networkRequiredSamples.Value = localRequiredSamples;
            networkCollectedSamples.Value = localCollectedSamples;
            networkIsExtractionAvailable.Value = localIsExtractionAvailable;
            networkExtractionTimeRemaining.Value = localExtractionTimeRemaining;
            networkExpeditionState.Value = localExpeditionState;
        }

        private void CacheNetworkStateLocally()
        {
            localCurrentDay = networkCurrentDay.Value;
            localRequiredSamples = networkRequiredSamples.Value;
            localCollectedSamples = networkCollectedSamples.Value;
            localIsExtractionAvailable = networkIsExtractionAvailable.Value;
            localExtractionTimeRemaining = networkExtractionTimeRemaining.Value;
            localExpeditionState = networkExpeditionState.Value;
        }

        private void StartExtractionTimer()
        {
            StopExtractionTimer();
            extractionCoroutine = StartCoroutine(ExtractionCountdownRoutine());
        }

        private void StopExtractionTimer()
        {
            if (extractionCoroutine == null)
            {
                return;
            }

            StopCoroutine(extractionCoroutine);
            extractionCoroutine = null;
        }

        private IEnumerator ExtractionCountdownRoutine()
        {
            SetExtractionTimeRemaining(helicopterExtractionDelay);

            while (extractionTimeRemaining > 0f)
            {
                SetExtractionTimeRemaining(Mathf.Max(0f, extractionTimeRemaining - Time.deltaTime));
                yield return null;
            }

            extractionCoroutine = null;
            CompleteExpedition();
        }

        private void OnExpeditionStateChanged(ExpeditionState previousState, ExpeditionState newState)
        {
            Debug.Log($"Expedition state changed: {previousState} -> {newState}");
        }

        private void LogMessage(string message)
        {
            Debug.Log(message);

            if (UseNetworkState && IsServer)
            {
                LogMessageClientRpc(message);
            }
        }

        private void SetCurrentDay(int value)
        {
            if (UseNetworkState)
            {
                networkCurrentDay.Value = value;
            }

            localCurrentDay = value;
        }

        private void SetRequiredSamples(int value)
        {
            if (UseNetworkState)
            {
                networkRequiredSamples.Value = value;
            }

            localRequiredSamples = value;
        }

        private void SetCollectedSamples(int value)
        {
            if (UseNetworkState)
            {
                networkCollectedSamples.Value = value;
            }

            localCollectedSamples = value;
        }

        private void SetExtractionAvailable(bool value)
        {
            if (UseNetworkState)
            {
                networkIsExtractionAvailable.Value = value;
            }

            localIsExtractionAvailable = value;
        }

        private void SetExtractionTimeRemaining(float value)
        {
            if (UseNetworkState)
            {
                networkExtractionTimeRemaining.Value = value;
            }

            localExtractionTimeRemaining = value;
        }

        private void SetState(ExpeditionState value)
        {
            if (UseNetworkState)
            {
                networkExpeditionState.Value = value;
            }

            localExpeditionState = value;
        }

        private void AdvanceDayInternal()
        {
            int nextDay = Mathf.Max(1, currentDay + 1);
            SetCurrentDay(nextDay);
            SetRequiredSamples(GetRequiredSamplesForDay(nextDay));

            DayNightTimer dayNightTimer = FindFirstObjectByType<DayNightTimer>(FindObjectsInactive.Exclude);
            if (dayNightTimer != null)
            {
                dayNightTimer.SetDay(currentDay);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void StartExpeditionServerRpc()
        {
            StartExpedition();
        }

        [ServerRpc(RequireOwnership = false)]
        private void AddSampleServerRpc()
        {
            AddSample();
        }

        [ServerRpc(RequireOwnership = false)]
        private void CallExtractionServerRpc()
        {
            CallExtraction();
        }

        [ServerRpc(RequireOwnership = false)]
        private void CompleteExpeditionServerRpc()
        {
            CompleteExpedition();
        }

        [ServerRpc(RequireOwnership = false)]
        private void FailExpeditionServerRpc()
        {
            FailExpedition();
        }

        [ServerRpc(RequireOwnership = false)]
        private void NextDayServerRpc()
        {
            NextDay();
        }

        [ClientRpc]
        private void LogMessageClientRpc(string message)
        {
            if (IsServer)
            {
                return;
            }

            Debug.Log(message);
        }
    }
}
