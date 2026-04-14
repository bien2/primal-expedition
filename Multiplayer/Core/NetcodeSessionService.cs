using Unity.Netcode;

namespace WalaPaNameHehe.Multiplayer.Core
{
    public sealed class NetcodeSessionService : INetworkSessionService, System.IDisposable
    {
        private readonly NetworkManager networkManager;
        private bool callbacksRegistered;

        public event System.Action<ulong> ClientConnected;
        public event System.Action<bool> SessionStopped;

        public bool IsReady => networkManager != null;
        public bool IsClient => networkManager != null && networkManager.IsClient;
        public bool IsServer => networkManager != null && networkManager.IsServer;
        public bool IsHost => networkManager != null && networkManager.IsHost;
        public ulong LocalClientId => networkManager != null ? networkManager.LocalClientId : 0;

        public NetcodeSessionService(NetworkManager manager)
        {
            networkManager = manager;
            RegisterCallbacks();
        }

        public bool TryStartHost()
        {
            if (networkManager == null || networkManager.IsClient || networkManager.IsServer)
            {
                return false;
            }

            RegisterCallbacks();
            return networkManager.StartHost();
        }

        public bool TryStartClient()
        {
            if (networkManager == null || networkManager.IsClient || networkManager.IsServer)
            {
                return false;
            }

            RegisterCallbacks();
            return networkManager.StartClient();
        }

        public bool TryStartServer()
        {
            if (networkManager == null || networkManager.IsClient || networkManager.IsServer)
            {
                return false;
            }

            RegisterCallbacks();
            return networkManager.StartServer();
        }

        public void Shutdown()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.Shutdown();
        }

        public void Dispose()
        {
            UnregisterCallbacks();
        }

        private void RegisterCallbacks()
        {
            if (networkManager == null || callbacksRegistered)
            {
                return;
            }

            networkManager.OnClientConnectedCallback += OnClientConnectedInternal;
            networkManager.OnServerStopped += OnSessionStoppedInternal;
            networkManager.OnClientStopped += OnSessionStoppedInternal;
            callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (networkManager == null || !callbacksRegistered)
            {
                return;
            }

            networkManager.OnClientConnectedCallback -= OnClientConnectedInternal;
            networkManager.OnServerStopped -= OnSessionStoppedInternal;
            networkManager.OnClientStopped -= OnSessionStoppedInternal;
            callbacksRegistered = false;
        }

        private void OnClientConnectedInternal(ulong clientId)
        {
            ClientConnected?.Invoke(clientId);
        }

        private void OnSessionStoppedInternal(bool wasHost)
        {
            SessionStopped?.Invoke(wasHost);
        }
    }
}
