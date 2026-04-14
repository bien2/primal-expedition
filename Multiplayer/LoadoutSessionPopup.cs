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
    public class LoadoutSessionPopup : MonoBehaviour
    {
        [Header("Loadout Station")]
        [SerializeField] private LoadoutStation loadoutStation;
        [SerializeField] private bool findNearestIfMissing = true;
        [SerializeField] private float searchRadius = 25f;
        [SerializeField] private bool autoCreateStationIfMissing = true;
        [SerializeField] private int minimumLoadoutCount = 4;
        [SerializeField] private List<LoadoutEntry> loadouts = new();

        [Header("Timing")]
        [SerializeField] private bool showOnSessionStart = true;
        [SerializeField] private bool showOnDayChange = true;
        [SerializeField] private float retryInterval = 0.5f;

        [Header("Rules")]
        [SerializeField] private bool forceSelection = true;

        [Header("Scenes")]
#if UNITY_EDITOR
        [SerializeField] private SceneAsset[] showInSceneAssets;
#endif
        [SerializeField, HideInInspector] private string[] showInSceneNames = new string[0];

        private int lastShownDay = -1;
        private Coroutine retryRoutine;

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            if (showOnSessionStart)
            {
                TryShowLoadout();
            }
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            if (retryRoutine != null)
            {
                StopCoroutine(retryRoutine);
                retryRoutine = null;
            }
        }

        private void OnActiveSceneChanged(Scene _, Scene __)
        {
            if (retryRoutine != null)
            {
                StopCoroutine(retryRoutine);
                retryRoutine = null;
            }

            lastShownDay = -1;

            if (!ShouldShowInActiveScene())
            {
                InventorySystem inventory = FindLocalInventory();
                if (inventory != null)
                {
                    inventory.RequestCloseLoadoutUi();
                }
                return;
            }

            TryShowLoadout();
        }

        private void Update()
        {
            if (!showOnDayChange)
            {
                return;
            }

            GameManager manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            int day = Mathf.Max(1, manager.currentDay);
            if (day != lastShownDay)
            {
                lastShownDay = day;
                TryShowLoadout();
            }
        }

        private void TryShowLoadout()
        {
            if (!ShouldShowInActiveScene())
            {
                return;
            }

            GameManager manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            lastShownDay = Mathf.Max(1, manager.currentDay);

            if (retryRoutine != null)
            {
                StopCoroutine(retryRoutine);
            }

            retryRoutine = StartCoroutine(ShowWhenReady());
        }

        private bool ShouldShowInActiveScene()
        {
            if (showInSceneNames == null || showInSceneNames.Length == 0)
            {
                return false;
            }

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            for (int i = 0; i < showInSceneNames.Length; i++)
            {
                if (showInSceneNames[i] == sceneName)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator ShowWhenReady()
        {
            while (true)
            {
                InventorySystem inventory = FindLocalInventory();
                if (inventory != null && !inventory.HasSelectedLoadoutForCurrentDay())
                {
                    LoadoutStation station = ResolveStation(inventory.transform.position);
                    if (station != null)
                    {
                        inventory.RequestOpenLoadoutUi(station, forceSelection);
                        retryRoutine = null;
                        yield break;
                    }
                }

                yield return new WaitForSeconds(retryInterval);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (showInSceneAssets == null || showInSceneAssets.Length == 0)
            {
                showInSceneNames = new string[0];
                return;
            }

            showInSceneNames = new string[showInSceneAssets.Length];
            for (int i = 0; i < showInSceneAssets.Length; i++)
            {
                showInSceneNames[i] = showInSceneAssets[i] != null ? showInSceneAssets[i].name : string.Empty;
            }
        }
#endif

        private LoadoutStation ResolveStation(Vector3 origin)
        {
            if (loadoutStation != null)
            {
                return loadoutStation;
            }

            if (autoCreateStationIfMissing)
            {
                LoadoutStation created = GetComponent<LoadoutStation>();
                if (created == null)
                {
                    created = gameObject.AddComponent<LoadoutStation>();
                }

                created.Configure(loadouts, minimumLoadoutCount);
                loadoutStation = created;
                return loadoutStation;
            }

            if (!findNearestIfMissing)
            {
                return null;
            }

            LoadoutStation[] stations = FindObjectsByType<LoadoutStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (stations == null || stations.Length == 0)
            {
                return null;
            }

            LoadoutStation best = null;
            float bestSqr = searchRadius * searchRadius;
            for (int i = 0; i < stations.Length; i++)
            {
                LoadoutStation station = stations[i];
                if (station == null)
                {
                    continue;
                }

                float sqr = (station.transform.position - origin).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = station;
                }
            }

            return best;
        }

        private InventorySystem FindLocalInventory()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                NetworkObject localPlayer = nm.SpawnManager != null ? nm.SpawnManager.GetLocalPlayerObject() : null;
                if (localPlayer != null)
                {
                    return localPlayer.GetComponentInChildren<InventorySystem>(true);
                }
            }

            InventorySystem[] inventories = FindObjectsByType<InventorySystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < inventories.Length; i++)
            {
                InventorySystem inv = inventories[i];
                if (inv != null && inv.IsOwner)
                {
                    return inv;
                }
            }

            return null;
        }
    }
}
