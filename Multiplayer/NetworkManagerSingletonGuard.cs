using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer
{
    /// <summary>
    /// Prevents accidental duplicate NetworkManager instances when reloading scenes (e.g. returning to Lobby).
    /// NGO enforces a singleton; multiple scene-placed NetworkManagers can still exist in DDOL and cause issues.
    /// </summary>
    public static class NetworkManagerSingletonGuard
    {
        private static bool initialized;
        private static NetworkManager firstInstantiatedManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            NetworkManager.OnInstantiated += OnNetworkManagerInstantiated;
        }

        private static void OnNetworkManagerInstantiated(NetworkManager manager)
        {
            if (manager == null)
            {
                return;
            }

            NetworkManager singleton = NetworkManager.Singleton;
            if (singleton == null)
            {
                // During initial startup, Singleton might not be assigned yet (it's set in NetworkManager.OnEnable).
                // Still prevent multiple scene-placed NetworkManagers from initializing in the same frame.
                if (firstInstantiatedManager == null || firstInstantiatedManager == manager)
                {
                    firstInstantiatedManager = manager;
                    return;
                }

                manager.enabled = false;
                Object.Destroy(manager.gameObject);
                return;
            }

            if (singleton == manager)
            {
                firstInstantiatedManager = manager;
                return;
            }

            // A NetworkManager already exists (usually the session one living in DontDestroyOnLoad).
            // Disable this duplicate before it reaches OnEnable (where NGO would call DontDestroyOnLoad),
            // then destroy the whole GameObject.
            manager.enabled = false;
            Object.Destroy(manager.gameObject);
        }
    }
}
