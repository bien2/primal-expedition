using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System;
using System.Threading.Tasks;
using WalaPaNameHehe.Multiplayer.Core;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    public class NetworkSessionLauncher : MonoBehaviour
    {
        [SerializeField] private bool showGui = true;
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private Camera menuCamera;
        [SerializeField] private string menuCameraName = "MenuCam";
        [SerializeField] private bool disableMenuCameraAudioListener = true;
        [SerializeField] private string defaultConnectAddress = "127.0.0.1";
        [SerializeField] private ushort defaultConnectPort = 7777;
        [SerializeField] private string hostListenAddress = "0.0.0.0";
        [SerializeField] private bool enableRelayJoinCodeFlow = true;
        [SerializeField] private int relayMaxConnections = 3;
        [SerializeField] private bool useCustomServicesEnvironment = true;
        [SerializeField] private string servicesEnvironmentName = "localtest";
        [SerializeField] private string servicesEnvironmentArg = "-servicesEnv";
        [SerializeField] private bool useSecureRelay = true;
        [SerializeField] private PlayerMovement sensitivitySource;
        [SerializeField] private float sensitivityMin = 0.05f;
        [SerializeField] private float sensitivityMax = 0.35f;
        [SerializeField] private bool showFpsCounter = true;
        [SerializeField] private float fpsUpdateInterval = 0.25f;

        private INetworkSessionService sessionService;
        private string connectAddress;
        private string connectPortText;
        private float sensitivityValue;
        private float fpsSampleUntil;
        private int fpsFrameCount;
        private float displayedFps;
        private bool servicesInitialized;
        private bool relayRequestInFlight;
        private string relayJoinCode = string.Empty;
        private string relayJoinCodeInput = string.Empty;
        private string relayStatus = string.Empty;
        private const int MaxLogLines = 6;
        private readonly string[] logLines = new string[MaxLogLines];
        private int logCount;
        private string lastInitEnvironment = string.Empty;

        private void Start()
        {
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton;
            }

            sessionService = CreateSessionService(networkManager);
            if (sessionService == null)
            {
                Debug.LogWarning("NetworkSessionLauncher: Session service could not be created.");
                return;
            }

            sessionService.ClientConnected += OnClientConnected;
            sessionService.SessionStopped += OnSessionStopped;
            RegisterNetworkManagerCallbacks();

            if (menuCamera == null)
            {
                menuCamera = Camera.main;
            }

            connectAddress = string.IsNullOrWhiteSpace(defaultConnectAddress) ? "127.0.0.1" : defaultConnectAddress;
            connectPortText = defaultConnectPort > 0 ? defaultConnectPort.ToString() : "7777";
            sensitivityValue = GetCurrentSensitivity();
            relayJoinCodeInput = string.Empty;
            relayJoinCode = string.Empty;
            relayStatus = string.Empty;
            ClearLogs();
            string envLabel = GetServicesEnvironmentLabel();
            lastInitEnvironment = string.Empty;
            AddLog($"Env: {envLabel}");
            AddLog($"Project: {Application.cloudProjectId}");
            AddLog($"Relay Secure: {useSecureRelay}");

            if (disableMenuCameraAudioListener)
            {
                DisableMenuCameraListeners();
            }
        }

        protected virtual INetworkSessionService CreateSessionService(NetworkManager manager)
        {
            return new NetcodeSessionService(manager);
        }

        private void Update()
        {
            if (sessionService == null || !sessionService.IsReady || networkManager == null)
            {
                return;
            }

            UpdateFpsCounter();

            // Fallback for Multiplayer Play Mode/editor edge cases:
            // if local player exists, force menu camera off.
            bool hasLocalPlayer =
                (sessionService.IsClient || sessionService.IsHost) &&
                networkManager.SpawnManager != null &&
                networkManager.SpawnManager.GetLocalPlayerObject() != null;

            if (hasLocalPlayer)
            {
                SetMenuCameraEnabled(false);
            }
        }

        private void OnDestroy()
        {
            if (sessionService != null)
            {
                sessionService.ClientConnected -= OnClientConnected;
                sessionService.SessionStopped -= OnSessionStopped;

                if (sessionService is System.IDisposable disposable)
                {
                    disposable.Dispose();
                }

                sessionService = null;
            }

            UnregisterNetworkManagerCallbacks();
        }

        private void OnGUI()
        {
            if (!showGui)
            {
                return;
            }

            DrawFpsCounter();

            if (sessionService == null || !sessionService.IsReady)
            {
                GUI.Label(new Rect(20f, 20f, 360f, 24f), "No NetworkManager in scene.");
                return;
            }

            GameManager gm = GameManager.Instance;
            if (gm != null && gm.IsReturningToLobby)
            {
                GUI.Label(new Rect(20f, 20f, 360f, 24f), "Returning to lobby...");
                return;
            }

            float x = 20f;
            float y = 20f;
            float w = 200f;
            float h = 40f;
            float gap = 10f;

            float panelWidth = (w * 2f) + gap;
            float panelX = (Screen.width - panelWidth) * 0.5f;
            float panelY = 90f;
            float fieldHeight = 28f;

            if (!sessionService.IsClient && !sessionService.IsServer)
            {
                float logPanelHeight = (MaxLogLines * 18f) + 8f;
                float boxHeight = enableRelayJoinCodeFlow ? 300f + logPanelHeight + 12f : 300f;
                GUI.Box(new Rect(panelX - 12f, panelY - 14f, panelWidth + 24f, boxHeight), string.Empty);

                float currentY = panelY;
                GUI.Label(new Rect(panelX, currentY, panelWidth, fieldHeight), $"Sensitivity: {sensitivityValue:0.00}");
                currentY += fieldHeight - 4f;
                sensitivityValue = GUI.HorizontalSlider(new Rect(panelX, currentY, panelWidth, 18f), sensitivityValue, sensitivityMin, sensitivityMax);
                ApplySensitivityValue(sensitivityValue);
                currentY += 26f;

                if (enableRelayJoinCodeFlow)
                {
                    GUI.Label(new Rect(panelX, currentY, panelWidth, 24f), "Relay Join Code");
                    currentY += 22f;
                    relayJoinCodeInput = GUI.TextField(new Rect(panelX, currentY, panelWidth, fieldHeight), relayJoinCodeInput).Trim().ToUpperInvariant();
                    currentY += fieldHeight + 8f;

                    if (GUI.Button(new Rect(panelX, currentY, w, h), relayRequestInFlight ? "Working..." : "Host Server"))
                    {
                        _ = TryStartRelayHostAsync();
                    }

                    bool allowJoin = sessionService == null || !sessionService.IsServer;
                    GUI.enabled = allowJoin;
                    if (GUI.Button(new Rect(panelX + w + gap, currentY, w, h), relayRequestInFlight ? "Working..." : "Join Server"))
                    {
                        if (string.IsNullOrWhiteSpace(relayJoinCodeInput))
                        {
                            relayJoinCodeInput = GUIUtility.systemCopyBuffer ?? string.Empty;
                        }

                        _ = TryStartRelayClientAsync();
                    }
                    GUI.enabled = true;
                    currentY += h + 8f;

                    if (!string.IsNullOrWhiteSpace(relayJoinCode))
                    {
                        GUI.Label(new Rect(panelX, currentY, panelWidth, 24f), $"Your Code: {relayJoinCode}");
                        currentY += 22f;
                    }

                    if (!string.IsNullOrWhiteSpace(relayStatus))
                    {
                        GUI.Label(new Rect(panelX, currentY, panelWidth, 40f), relayStatus);
                        currentY += 36f;
                    }

                    bool inSession = sessionService != null && (sessionService.IsClient || sessionService.IsServer);
                    GUI.enabled = !inSession;
                    if (GUI.Button(new Rect(panelX, currentY, w, h), "Local Host (Play Mode)"))
                    {
                        StartLocalHostForPlayMode();
                    }

                    if (GUI.Button(new Rect(panelX + w + gap, currentY, w, h), "Local Client (Play Mode)"))
                    {
                        StartLocalClientForPlayMode();
                    }
                    GUI.enabled = true;
                    currentY += h + 6f;

                    DrawLogPanel(panelX, currentY, panelWidth);
                }
                else
                {
                    GUI.Label(new Rect(panelX, currentY, 80f, fieldHeight), "IP");
                    connectAddress = GUI.TextField(new Rect(panelX + 50f, currentY, panelWidth - 50f, fieldHeight), connectAddress);
                    currentY += fieldHeight + 8f;

                    GUI.Label(new Rect(panelX, currentY, 80f, fieldHeight), "Port");
                    connectPortText = GUI.TextField(new Rect(panelX + 50f, currentY, panelWidth - 50f, fieldHeight), connectPortText);
                    currentY += fieldHeight + 10f;

                    if (GUI.Button(new Rect(panelX, currentY, w, h), "Host"))
                    {
                        ApplyHostConnectionData();
                        sessionService.TryStartHost();
                    }

                    if (GUI.Button(new Rect(panelX + w + gap, currentY, w, h), "Client"))
                    {
                        ApplyClientConnectionData();
                        sessionService.TryStartClient();
                    }
                }

                return;
            }

            float inGamePanelWidth = panelWidth;
            float inGameX = Screen.width - inGamePanelWidth - 20f;
            float inGameY = 20f;
            float inGameLeftX = 20f;
            float inGameLeftY = 20f;

            float sliderX = inGameLeftX;
            float sliderY = inGameLeftY + 70f;
            GUI.Label(new Rect(sliderX, sliderY, inGamePanelWidth, fieldHeight), $"Sensitivity: {sensitivityValue:0.00}");
            sensitivityValue = GUI.HorizontalSlider(new Rect(sliderX, sliderY + 28f, inGamePanelWidth, 18f), sensitivityValue, sensitivityMin, sensitivityMax);
            ApplySensitivityValue(sensitivityValue);

            string mode = sessionService.IsHost ? "Host" : sessionService.IsServer ? "Server" : "Client";
            GUI.Label(new Rect(inGameX, inGameY, 260f, 24f), $"Mode: {mode}");
            DrawInGameRelayCode(inGameX, inGameY + 24f);
            DrawRuntimeIdentity(inGameX, inGameY + 48f);
            DrawLogPanel(Screen.width - 360f - 20f, Screen.height - ((MaxLogLines * 18f) + 8f) - 20f, 360f);

            bool allowShutdown = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby";
            GUI.enabled = allowShutdown;
            if (GUI.Button(new Rect(inGameLeftX, inGameLeftY, w, h), allowShutdown ? "Shutdown" : "Shutdown (Lobby Only)"))
            {
                sessionService.Shutdown();
                SetMenuCameraEnabled(true);
                AddLog("Session shutdown.");
                relayJoinCode = string.Empty;
            }
            GUI.enabled = true;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (sessionService == null || !sessionService.IsReady)
            {
                return;
            }

            // Disable menu camera once this local instance is actually connected.
            if (clientId == sessionService.LocalClientId)
            {
                SetMenuCameraEnabled(false);
                AddLog("Local client connected.");
                return;
            }

            AddLog($"Client joined: {clientId}");
        }

        private void OnSessionStopped(bool _)
        {
            SetMenuCameraEnabled(true);
            AddLog("Session stopped.");
            relayJoinCode = string.Empty;
        }

        private void SetMenuCameraEnabled(bool enabled)
        {
            if (menuCamera != null)
            {
                menuCamera.enabled = enabled;
                if (disableMenuCameraAudioListener)
                {
                    AudioListener directListener = menuCamera.GetComponent<AudioListener>();
                    if (directListener != null)
                    {
                        directListener.enabled = false;
                    }
                }
            }

            // Multiplayer Play Mode can create per-virtual-player scene copies.
            // Disable/enable every menu camera by name as a fallback.
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allCameras.Length; i++)
            {
                Camera cam = allCameras[i];
                if (cam == null)
                {
                    continue;
                }

                if (cam.gameObject.name == menuCameraName)
                {
                    cam.enabled = enabled;
                    AudioListener listener = cam.GetComponent<AudioListener>();
                    if (listener != null)
                    {
                        listener.enabled = disableMenuCameraAudioListener ? false : enabled;
                    }
                }
            }
        }

        private void DisableMenuCameraListeners()
        {
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allCameras.Length; i++)
            {
                Camera cam = allCameras[i];
                if (cam == null)
                {
                    continue;
                }

                if (cam.gameObject.name != menuCameraName)
                {
                    continue;
                }

                AudioListener listener = cam.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }

        private void ApplyClientConnectionData()
        {
            if (networkManager == null)
            {
                return;
            }

            UnityTransport transport = networkManager.NetworkConfig != null
                ? networkManager.NetworkConfig.NetworkTransport as UnityTransport
                : null;
            if (transport == null)
            {
                return;
            }

            string address = string.IsNullOrWhiteSpace(connectAddress) ? "127.0.0.1" : connectAddress.Trim();
            if (!ushort.TryParse(connectPortText, out ushort port) || port == 0)
            {
                port = 7777;
            }

            transport.SetConnectionData(address, port);
        }

        private void ApplyHostConnectionData()
        {
            if (networkManager == null)
            {
                return;
            }

            UnityTransport transport = networkManager.NetworkConfig != null
                ? networkManager.NetworkConfig.NetworkTransport as UnityTransport
                : null;
            if (transport == null)
            {
                return;
            }

            if (!ushort.TryParse(connectPortText, out ushort port) || port == 0)
            {
                port = 7777;
            }

            string listenAddress = string.IsNullOrWhiteSpace(hostListenAddress) ? "0.0.0.0" : hostListenAddress.Trim();
            transport.SetConnectionData("0.0.0.0", port, listenAddress);
        }

        private async Task TryStartRelayHostAsync()
        {
            if (relayRequestInFlight || sessionService == null || !sessionService.IsReady)
            {
                return;
            }

            if (sessionService.IsClient || sessionService.IsServer)
            {
                relayStatus = "Already in session.";
                return;
            }

            if (!TryGetUnityTransport(out UnityTransport transport))
            {
                relayStatus = "UnityTransport missing.";
                return;
            }

            relayRequestInFlight = true;
            relayStatus = "Initializing services...";
            relayJoinCode = string.Empty;
            AddLog("Starting relay host...");

            try
            {
                if (!await EnsureServicesSignedInAsync())
                {
                    return;
                }

                int maxConnections = Mathf.Max(1, relayMaxConnections);
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                AddLog($"Join code: {relayJoinCode}");
                AddLog($"AllocId: {allocation.AllocationId}");
                transport.SetHostRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    useSecureRelay);

                bool started = sessionService.TryStartHost();
                relayStatus = started ? "Relay host started." : "Host start failed.";
                AddLog(started ? "Relay host started." : "Host start failed.");
            }
            catch (Exception ex)
            {
                relayStatus = $"Relay host failed: {ex.Message}";
                Debug.LogException(ex);
                AddLog("Relay host failed.");
            }
            finally
            {
                relayRequestInFlight = false;
            }
        }

        private async Task TryStartRelayClientAsync()
        {
            if (relayRequestInFlight || sessionService == null || !sessionService.IsReady)
            {
                return;
            }

            if (sessionService.IsClient || sessionService.IsServer)
            {
                relayStatus = "Already in session.";
                return;
            }

            string code = string.IsNullOrWhiteSpace(relayJoinCodeInput) ? string.Empty : relayJoinCodeInput.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                relayStatus = "Enter a relay join code.";
                return;
            }

            if (!TryGetUnityTransport(out UnityTransport transport))
            {
                relayStatus = "UnityTransport missing.";
                return;
            }

            relayRequestInFlight = true;
            relayStatus = "Joining relay...";
            AddLog("Joining relay...");

            try
            {
                if (!await EnsureServicesSignedInAsync())
                {
                    return;
                }

                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(code);
                transport.SetClientRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    allocation.HostConnectionData,
                    useSecureRelay);

                bool started = sessionService.TryStartClient();
                relayStatus = started ? "Relay client started." : "Client start failed.";
                AddLog(started ? "Relay client started." : "Client start failed.");
            }
            catch (Exception ex)
            {
                relayStatus = $"Relay join failed: {ex.Message}";
                Debug.LogException(ex);
                AddLog("Relay join failed.");
            }
            finally
            {
                relayRequestInFlight = false;
            }
        }

        private async Task<bool> EnsureServicesSignedInAsync()
        {
            if (servicesInitialized && AuthenticationService.Instance.IsSignedIn)
            {
                return true;
            }

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    InitializationOptions options = new InitializationOptions();
                    if (useCustomServicesEnvironment && !string.IsNullOrWhiteSpace(servicesEnvironmentName))
                    {
                        options.SetEnvironmentName(servicesEnvironmentName.Trim());
                    }

                    await UnityServices.InitializeAsync(options);
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                servicesInitialized = true;
                lastInitEnvironment = GetServicesEnvironmentLabel();
                AddLog("Services init OK.");
                return true;
            }
            catch (Exception ex)
            {
                relayStatus = $"Services init failed: {ex.Message}";
                Debug.LogException(ex);
                return false;
            }
        }

        private bool TryGetUnityTransport(out UnityTransport transport)
        {
            transport = networkManager != null && networkManager.NetworkConfig != null
                ? networkManager.NetworkConfig.NetworkTransport as UnityTransport
                : null;
            return transport != null;
        }

        private void StartLocalHostForPlayMode()
        {
            connectPortText = defaultConnectPort > 0 ? defaultConnectPort.ToString() : "7777";
            ApplyHostConnectionData();
            bool started = sessionService != null && sessionService.TryStartHost();
            relayStatus = started ? "Local host started." : "Local host start failed.";
            AddLog(started ? "Local host started." : "Local host start failed.");
        }

        private void StartLocalClientForPlayMode()
        {
            connectAddress = "127.0.0.1";
            connectPortText = defaultConnectPort > 0 ? defaultConnectPort.ToString() : "7777";
            ApplyClientConnectionData();
            bool started = sessionService != null && sessionService.TryStartClient();
            relayStatus = started ? "Local client started." : "Local client start failed.";
            AddLog(started ? "Local client started." : "Local client start failed.");
        }

        private void DrawLogPanel(float x, float y, float width)
        {
            float height = (MaxLogLines * 18f) + 8f;
            GUI.Box(new Rect(x, y, width, height), string.Empty);

            float lineY = y + 6f;
            for (int i = 0; i < logCount; i++)
            {
                GUI.Label(new Rect(x + 6f, lineY, width - 12f, 18f), logLines[i]);
                lineY += 18f;
            }
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (logCount < MaxLogLines)
            {
                logLines[logCount++] = message;
                return;
            }

            for (int i = 1; i < MaxLogLines; i++)
            {
                logLines[i - 1] = logLines[i];
            }

            logLines[MaxLogLines - 1] = message;
        }

        private void ClearLogs()
        {
            for (int i = 0; i < MaxLogLines; i++)
            {
                logLines[i] = string.Empty;
            }

            logCount = 0;
        }

        private void DrawInGameRelayCode(float x, float y)
        {
            if (string.IsNullOrWhiteSpace(relayJoinCode))
            {
                return;
            }

            GUI.Label(new Rect(x, y, 360f, 24f), $"Join Code: {relayJoinCode}");
        }

        private void DrawRuntimeIdentity(float x, float y)
        {
            string env = GetServicesEnvironmentLabel();
            GUI.Label(new Rect(x, y, 360f, 20f), $"Env: {env}");
            GUI.Label(new Rect(x, y + 18f, 360f, 20f), $"Project: {Application.cloudProjectId}");
            if (!string.IsNullOrWhiteSpace(lastInitEnvironment))
            {
                GUI.Label(new Rect(x, y + 36f, 360f, 20f), $"Init Env: {lastInitEnvironment}");
            }
        }

        private string GetServicesEnvironmentLabel()
        {
            string overrideEnv = GetCommandLineEnvironment();
            if (!string.IsNullOrWhiteSpace(overrideEnv))
            {
                return overrideEnv;
            }

            if (useCustomServicesEnvironment && !string.IsNullOrWhiteSpace(servicesEnvironmentName))
            {
                return servicesEnvironmentName.Trim();
            }

            return "production";
        }

        private string GetCommandLineEnvironment()
        {
            if (string.IsNullOrWhiteSpace(servicesEnvironmentArg))
            {
                return string.Empty;
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (string.Equals(arg, servicesEnvironmentArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        return args[i + 1];
                    }
                }
                else if (arg.StartsWith(servicesEnvironmentArg + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(servicesEnvironmentArg.Length + 1);
                }
            }

            return string.Empty;
        }

        private void RegisterNetworkManagerCallbacks()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnServerStarted += OnServerStarted;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnTransportFailure += OnTransportFailure;
        }

        private void UnregisterNetworkManagerCallbacks()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnTransportFailure -= OnTransportFailure;
        }

        private void OnServerStarted()
        {
            AddLog("Server started.");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            string reason = networkManager != null ? networkManager.DisconnectReason : string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
            {
                AddLog($"Client disconnected: {clientId}");
                return;
            }

            AddLog($"Client disconnected: {clientId} ({reason})");
        }

        private void OnTransportFailure()
        {
            AddLog("Transport failure.");
        }

        private float GetCurrentSensitivity()
        {
            PlayerMovement source = sensitivitySource != null ? sensitivitySource : FindLocalPlayerMovement();
            if (source == null)
            {
                return 0.15f;
            }

            return source.LookSensitivity;
        }

        private void ApplySensitivityValue(float value)
        {
            PlayerMovement source = sensitivitySource != null ? sensitivitySource : FindLocalPlayerMovement();
            if (source == null)
            {
                return;
            }

            source.LookSensitivity = Mathf.Clamp(value, sensitivityMin, sensitivityMax);
        }

        private void UpdateFpsCounter()
        {
            if (!showFpsCounter)
            {
                return;
            }

            float interval = Mathf.Max(0.05f, fpsUpdateInterval);
            if (fpsSampleUntil <= 0f)
            {
                fpsSampleUntil = Time.unscaledTime + interval;
            }

            fpsFrameCount++;
            if (Time.unscaledTime < fpsSampleUntil)
            {
                return;
            }

            float elapsed = interval;
            displayedFps = elapsed > 0f ? fpsFrameCount / elapsed : 0f;
            fpsFrameCount = 0;
            fpsSampleUntil = Time.unscaledTime + interval;
        }

        private void DrawFpsCounter()
        {
            if (!showFpsCounter)
            {
                return;
            }

            const float width = 120f;
            const float height = 24f;
            Rect rect = new Rect(Screen.width - width - 20f, 20f, width, height);
            GUI.Label(rect, $"FPS: {Mathf.RoundToInt(displayedFps)}");
        }

        private static PlayerMovement FindLocalPlayerMovement()
        {
            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerMovement candidate = players[i];
                if (candidate != null && candidate.IsOwner)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
