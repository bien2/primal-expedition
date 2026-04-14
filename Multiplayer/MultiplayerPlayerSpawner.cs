using System.Collections;
using System.Collections.Generic;
using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace WalaPaNameHehe
{
    [DisallowMultipleComponent]
    public class MultiplayerPlayerSpawner : MonoBehaviour
    {
        private const string SpawnSyncMessageName = "WalaPaNameHehe.SpawnSync";

        [Header("References")]
        [SerializeField] private NetworkManager networkManager;

        [Header("Spawn Rules")]
        [SerializeField] private bool includeInactiveSpawnPoints = false;


        private bool messageHandlerRegistered;
        private bool sceneEventsRegistered;
        private bool netLoadEventRegistered;
        private string lastLoadedSceneName = string.Empty;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;




        private void OnClientConnected(ulong clientId)
        {
            if (!IsServerActive())
            {
                return;
            }

            StartCoroutine(SpawnClientWhenReady(clientId));
        }


        public void CopyConfigFrom(MultiplayerPlayerSpawner source)
        {
            if (source == null || source == this)
            {
                return;
            }

            includeInactiveSpawnPoints = source.includeInactiveSpawnPoints;
            verboseLogs = source.verboseLogs;
        }

        private void EnsureNetworkManager()
        {
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton;
            }
        }

        private void Awake()
        {
            EnsureNetworkManager();
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton;
            }

            RegisterSpawnSyncHandler();
        }

        private void OnEnable()
        {
            EnsureNetworkManager();

            if (networkManager == null)
            {
                return;
            }

            RegisterSpawnSyncHandler();
            networkManager.OnServerStopped += OnServerStopped;
            networkManager.OnClientStopped += OnClientStopped;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            RegisterSceneEvents();
        }

        private void OnDisable()
        {
            if (networkManager == null)
            {
                return;
            }

            UnregisterSpawnSyncHandler();
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnServerStopped -= OnServerStopped;
            networkManager.OnClientStopped -= OnClientStopped;
            UnregisterSceneEvents();
        }

        private void Update()
        {
            EnsureNetworkManager();
            if (!messageHandlerRegistered && networkManager != null && networkManager.IsListening)
            {
                RegisterSpawnSyncHandler();
            }

            if (sceneEventsRegistered && !netLoadEventRegistered && networkManager != null && networkManager.IsListening)
            {
                TryRegisterNetworkSceneEvents();
            }
        }

        public void RespawnClientAtRandomSpawnPoint(ulong clientId)
        {
            if (!IsServerActive())
            {
                return;
            }

            StartCoroutine(SpawnClientWhenReady(clientId));
        }



        private void OnServerStopped(bool _)
        {
            TryUnregisterNetworkSceneEvents();
        }

        private void OnClientStopped(bool _)
        {
            UnregisterSpawnSyncHandler();
            TryUnregisterNetworkSceneEvents();
        }

        private void RegisterSceneEvents()
        {
            if (sceneEventsRegistered)
            {
                return;
            }

            SceneManager.sceneLoaded += OnUnitySceneLoaded;

            sceneEventsRegistered = true;
            TryRegisterNetworkSceneEvents();
        }

        private void UnregisterSceneEvents()
        {
            if (!sceneEventsRegistered)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnUnitySceneLoaded;

            TryUnregisterNetworkSceneEvents();

            sceneEventsRegistered = false;
        }

        private void TryRegisterNetworkSceneEvents()
        {
            if (netLoadEventRegistered || networkManager == null || networkManager.SceneManager == null)
            {
                return;
            }

            networkManager.SceneManager.OnLoadEventCompleted += OnNetworkLoadEventCompleted;
            netLoadEventRegistered = true;
        }

        private void TryUnregisterNetworkSceneEvents()
        {
            if (!netLoadEventRegistered || networkManager == null || networkManager.SceneManager == null)
            {
                netLoadEventRegistered = false;
                return;
            }

            networkManager.SceneManager.OnLoadEventCompleted -= OnNetworkLoadEventCompleted;
            netLoadEventRegistered = false;
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            lastLoadedSceneName = scene.name;

            if (verboseLogs)
            {
                Debug.Log($"MultiplayerPlayerSpawner: Unity scene loaded '{scene.name}' ({mode}).");
            }
        }

        private void OnNetworkLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServerActive() || networkManager == null)
            {
                return;
            }

            lastLoadedSceneName = sceneName;

            if (verboseLogs)
            {
                int completed = clientsCompleted != null ? clientsCompleted.Count : 0;
                int timedOut = clientsTimedOut != null ? clientsTimedOut.Count : 0;
                Debug.Log($"MultiplayerPlayerSpawner: Network load completed '{sceneName}' ({mode}). Completed={completed} TimedOut={timedOut}");
            }

            StartCoroutine(RespawnAllClientsAfterNetworkLoad());
        }

        private IEnumerator RespawnAllClientsAfterNetworkLoad()
        {
            if (!IsServerActive() || networkManager == null)
            {
                yield break;
            }

            // Allow at least one frame for scene objects to finish enabling (spawn points, colliders, etc.).
            yield return null;

            foreach (var kvp in networkManager.ConnectedClients)
            {
                NetworkClient client = kvp.Value;
                if (client == null || client.PlayerObject == null)
                {
                    continue;
                }

                StartCoroutine(SpawnClientWhenReady(kvp.Key));
            }
        }

        private IEnumerator SpawnClientWhenReady(ulong clientId)
        {
            if (networkManager == null)
            {
                yield break;
            }

            int maxFramesToWait = 180;
            int waitedFrames = 0;
            NetworkObject playerObject = null;

            while (waitedFrames < maxFramesToWait)
            {
                if (TryGetPlayerObject(clientId, out playerObject))
                {
                    break;
                }

                waitedFrames++;
                yield return null;
            }

            if (playerObject == null)
            {
                Debug.LogWarning($"MultiplayerPlayerSpawner: player object not found for client {clientId}.");
                yield break;
            }

            if (!TryGetSessionSpawnPoint(out PlayerSpawnPoint spawnPoint, clientId))
            {
                Debug.LogWarning("MultiplayerPlayerSpawner: no PlayerSpawnPoint found.");
                yield break;
            }

            Vector3 finalPosition = spawnPoint.transform.position;
            Quaternion finalRotation = spawnPoint.transform.rotation;

            TeleportPlayer(playerObject, finalPosition, finalRotation);
            SendSpawnSyncToClient(clientId, finalPosition, finalRotation);
        }

        private bool TryGetPlayerObject(ulong clientId, out NetworkObject playerObject)
        {
            playerObject = null;

            if (networkManager == null || networkManager.ConnectedClients == null)
            {
                return false;
            }

            if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client == null)
            {
                return false;
            }

            playerObject = client.PlayerObject;
            return playerObject != null;
        }

        private bool TryGetSessionSpawnPoint(out PlayerSpawnPoint spawnPoint, ulong? clientId = null)
        {
            spawnPoint = FindFirstObjectByType<PlayerSpawnPoint>(
                includeInactiveSpawnPoints ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);

            return spawnPoint != null;
        }

        private void TeleportPlayer(NetworkObject playerObject, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            if (playerObject == null)
            {
                return;
            }

            Vector3 finalPosition = spawnPosition;
            Transform playerTransform = playerObject.transform;

            if (playerTransform == null)
            {
                return;
            }

            Rigidbody rb = playerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = finalPosition;
                rb.rotation = spawnRotation;
            }

            NetworkTransform netTransform = playerObject.GetComponent<NetworkTransform>();
            if (netTransform != null)
            {
                try
                {
                    netTransform.Teleport(finalPosition, spawnRotation, playerTransform.localScale);
                    return;
                }
                catch (Exception)
                {
                }
            }

            playerTransform.SetPositionAndRotation(finalPosition, spawnRotation);
        }
        private void SendSpawnSyncToClient(ulong clientId, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            if (networkManager == null || !networkManager.IsServer || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            Vector3 finalPosition = spawnPosition;

            using FastBufferWriter writer = new(128, Allocator.Temp);
            writer.WriteValueSafe(finalPosition);
            writer.WriteValueSafe(spawnRotation);
            networkManager.CustomMessagingManager.SendNamedMessage(SpawnSyncMessageName, clientId, writer);
        }

        private void RegisterSpawnSyncHandler()
        {
            if (messageHandlerRegistered || networkManager == null || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            try
            {
                networkManager.CustomMessagingManager.RegisterNamedMessageHandler(SpawnSyncMessageName, OnSpawnSyncMessageReceived);
                messageHandlerRegistered = true;
            }
            catch (Exception)
            {
                // If the handler is already registered (e.g. during domain reloads), avoid hard-failing.
                messageHandlerRegistered = true;
            }
        }

        private void UnregisterSpawnSyncHandler()
        {
            if (!messageHandlerRegistered || networkManager == null || networkManager.CustomMessagingManager == null)
            {
                return;
            }

            try
            {
                networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SpawnSyncMessageName);
            }
            catch (Exception)
            {
                // Ignore if already unregistered by NGO internally.
            }
            messageHandlerRegistered = false;
        }

        private void OnSpawnSyncMessageReceived(ulong _, FastBufferReader reader)
        {
            if (!reader.TryBeginRead(sizeof(float) * 7))
            {
                return;
            }

            reader.ReadValueSafe(out Vector3 spawnPosition);
            reader.ReadValueSafe(out Quaternion spawnRotation);
            StartCoroutine(ApplyLocalSpawnWhenReady(spawnPosition, spawnRotation));
        }

        private IEnumerator ApplyLocalSpawnWhenReady(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                yield break;
            }

            int maxFramesToWait = 180;
            int waitedFrames = 0;
            NetworkObject localPlayerObject = null;

            while (waitedFrames < maxFramesToWait)
            {
                localPlayerObject = manager.SpawnManager != null ? manager.SpawnManager.GetLocalPlayerObject() : null;
                if (localPlayerObject != null)
                {
                    break;
                }

                waitedFrames++;
                yield return null;
            }

            if (localPlayerObject == null)
            {
                yield break;
            }


            Vector3 finalPosition = spawnPosition;

            Rigidbody rb = localPlayerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = finalPosition;
                rb.rotation = spawnRotation;
            }

            NetworkTransform netTransform = localPlayerObject.GetComponent<NetworkTransform>();
            if (netTransform != null)
            {
                try
                {
                    netTransform.Teleport(finalPosition, spawnRotation, localPlayerObject.transform.localScale);
                    yield break;
                }
                catch (Exception)
                {
                    // fallback
                }
            }

            localPlayerObject.transform.SetPositionAndRotation(finalPosition, spawnRotation);
        }


        private bool IsServerActive()
        {
            EnsureNetworkManager();
            return networkManager != null && networkManager.IsListening && networkManager.IsServer;
        }
    }
}
