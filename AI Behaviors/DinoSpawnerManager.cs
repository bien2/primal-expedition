using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace WalaPaNameHehe
{
    public class DinoSpawnerManager : MonoBehaviour
    {
        public enum DinoType
        {
            Passive,
            Neutral,
            Roamer
        }

        [System.Serializable]
        public class DinoSpawnConfig
        {
            public DinoType type;
            public GameObject[] prefabs;
            [Min(0)] public int minCount = 3;
            [Min(0)] public int maxCount = 5;
        }

        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;

        [Header("Dino Configs")]
        [SerializeField] private DinoSpawnConfig[] spawnConfigs;

        [Header("Spawn Mode")]
        [SerializeField] private bool allowOfflineSpawn = true;

        [Header("Spawn Spacing")]
        [SerializeField] private bool useAutoSpacing = true;
        [Min(0f)] [SerializeField] private float spacingMultiplier = 1.2f;
        [Min(0f)] [SerializeField] private float minSpacingRadius = 2f;
        [Min(0f)] [SerializeField] private float maxSpacingRadius = 8f;
        [Header("Spawn Reliability")]
        [SerializeField] private bool forceSpawnWhenSpacingFails = true;
        [Min(1)] [SerializeField] private int spawnPointSearchAttempts = 20;
        [SerializeField] private bool snapSpawnToNavMesh = true;
        [Min(0f)] [SerializeField] private float navMeshSampleRadius = 2f;
        [Header("Network Prefabs")]
        [SerializeField] private bool autoRegisterNetworkPrefabs = true;
        [Header("Spawn Gizmos")]
        [SerializeField] private bool showSpacingGizmos = true;

        [Header("Plunderer")]
        [SerializeField] private GameObject plundererPrefab;
        [SerializeField] private Transform plundererSpawnPoint;
        [SerializeField] private Transform[] plundererWaypoints;
        [SerializeField] private bool plundererPreloadHidden = true;
        [SerializeField] private float plundererSpawnIntervalMin = 60f;
        [SerializeField] private float plundererSpawnIntervalMax = 90f;
        [SerializeField] private int plundererWaypointLoopsMin = 0;
        [SerializeField] private int plundererWaypointLoopsMax = 0;

        [Header("Hunter")]
        [SerializeField] private GameObject hunterPrefab;
        [SerializeField] private Transform[] hunterSpawnPoints;

        [Header("Apex")]
        [SerializeField] private GameObject apexPrefab;
        [SerializeField] private Transform apexSpawnPointRoot;
        [SerializeField] private bool apexAutoCollectChildren = true;
        [SerializeField] private Transform[] apexSpawnPoints;
        [Min(1)] [SerializeField] private int apexSpawnCount = 1;
        [System.Serializable]
        public class ApexDayThreshold
        {
            [Min(1)] public int day = 1;
            [Min(0)] public int samplesMin = 2;
            [Min(0)] public int samplesMax = 4;
            public bool spawnOnDayStart = false;
        }
        [SerializeField] private ApexDayThreshold[] apexDayThresholds;

        private readonly Dictionary<int, List<GameObject>> spawnedByConfigIndex = new();
        private int[] prefabRoundRobinIndex;
        private GameObject spawnedPlunderer;
        private GameObject spawnedHunter;
        private Vector3 hunterSpawnPosition;
        private Quaternion hunterSpawnRotation;
        private bool hasHunterSpawnLocation;
        private Coroutine plundererLoop;
        private float nextPlundererSpawnTime;
        private int trackedApexDay = -1;
        private int targetApexSamples = -1;
        private bool apexSpawnedThisDay;
        private readonly List<GameObject> spawnedApex = new();
        private bool spawnLoopsStarted;
        private readonly HashSet<int> warnedScenePrefabInstanceIds = new();

        private void Awake()
        {
            RefreshSpawnPoints();
            RefreshApexSpawnPoints();
            InitializeTracking();
            TryRegisterNetworkPrefabs();
        }

        private void OnEnable()
        {
            TryRegisterNetworkPrefabs();
            if (IsServerActive())
            {
                StartSpawnLoops();
            }
            else if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            }
            else if (allowOfflineSpawn)
            {
                StartSpawnLoops();
            }

            PlundererAggressionBehavior.PlundererDespawned += OnPlundererDespawned;
            ResetApexForDay();
        }

        private void DespawnApex()
        {
            if (spawnedApex == null || spawnedApex.Count == 0)
            {
                return;
            }

            for (int i = spawnedApex.Count - 1; i >= 0; i--)
            {
                GameObject apex = spawnedApex[i];
                if (apex == null)
                {
                    spawnedApex.RemoveAt(i);
                    continue;
                }

                NetworkObject netObj = apex.GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    if (IsServerActive())
                    {
                        netObj.Despawn(true);
                    }
                    else
                    {
                        Destroy(apex);
                    }
                }
                else
                {
                    Destroy(apex);
                }

                spawnedApex.RemoveAt(i);
            }

            apexSpawnedThisDay = false;
        }

        private void HandleServerStarted()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
            }

            StartSpawnLoops();
        }

        private void StartSpawnLoops()
        {
            if (spawnLoopsStarted)
            {
                return;
            }

            if (!IsServerActive() && !allowOfflineSpawn)
            {
                return;
            }

            spawnLoopsStarted = true;
            StartCoroutine(SpawnLoop());
            plundererLoop = StartCoroutine(PlundererSpawnLoop());
            SpawnHunter();
        }

        private void RefreshSpawnPoints()
        {
            if (spawnPoints == null)
            {
                spawnPoints = new Transform[0];
            }
        }

        private void InitializeTracking()
        {
            spawnedByConfigIndex.Clear();
            prefabRoundRobinIndex = spawnConfigs != null ? new int[spawnConfigs.Length] : null;

            if (spawnConfigs == null)
            {
                return;
            }

            for (int i = 0; i < spawnConfigs.Length; i++)
            {
                if (!spawnedByConfigIndex.ContainsKey(i))
                {
                    spawnedByConfigIndex[i] = new List<GameObject>();
                }
            }
        }

        private IEnumerator SpawnLoop()
        {
            yield return null;
            TickSpawns();
        }

        private IEnumerator PlundererSpawnLoop()
        {
            ScheduleNextPlundererSpawn();

            while (true)
            {
                if (spawnedPlunderer == null)
                {
                    if (Time.time >= nextPlundererSpawnTime)
                    {
                        SpawnPlunderer();
                    }
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private void TickSpawns()
        {
            if (spawnConfigs == null || spawnPoints == null || spawnPoints.Length == 0)
            {
                if (spawnConfigs != null && spawnConfigs.Length > 0)
                {
                    Debug.LogWarning("DinoSpawnerManager: No spawn points assigned. Skipping dino spawns.");
                }
                return;
            }

            for (int i = 0; i < spawnConfigs.Length; i++)
            {
                DinoSpawnConfig config = spawnConfigs[i];

                List<GameObject> list = GetSpawnList(i);
                CleanupDestroyed(list);

                int minCount = Mathf.Max(0, config.minCount);
                int maxCount = Mathf.Max(minCount, config.maxCount);
                int targetCount = Random.Range(minCount, maxCount + 1);

                if (list.Count >= targetCount)
                {
                    continue;
                }

                int toSpawn = targetCount - list.Count;
                for (int s = 0; s < toSpawn; s++)
                {
                    GameObject prefab = GetRandomPrefab(config, i);
                    if (prefab == null)
                    {
                        break;
                    }

                    if (prefab.scene.IsValid())
                    {
                        WarnScenePrefabReference(prefab, $"SpawnConfig[{i}]");
                        break;
                    }

                    float spacingRadius = GetSpacingRadiusForConfig(config, prefab);
                    if (!TryGetSpawnPointWithSpacing(spacingRadius, out Transform spawnPoint))
                    {
                        break;
                    }

                    Vector3 spawnPosition = GetSpawnPosition(spawnPoint);
                    GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
                    ApplyAggressionType(instance, config.type);
                    if (IsServerActive())
                    {
                        NetworkObject netObj = instance.GetComponent<NetworkObject>();
                        if (netObj == null)
                        {
                            Debug.LogWarning($"DinoSpawnerManager: Prefab '{prefab.name}' is missing NetworkObject. Destroying spawned instance.");
                            Destroy(instance);
                            continue;
                        }
                        if (!netObj.IsSpawned)
                        {
                            netObj.Spawn(true);
                        }
                    }

                    list.Add(instance);
                }
            }
        }

        private void Update()
        {
            TickApexSpawn();
        }

        private void TickApexSpawn()
        {
            WalaPaNameHehe.Multiplayer.GameManager manager = WalaPaNameHehe.Multiplayer.GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            if (trackedApexDay != manager.currentDay)
            {
                DespawnApex();
                ResetApexForDay();
            }

            if (!apexSpawnedThisDay && ShouldSpawnApexOnDayStart())
            {
                SpawnApex();
                return;
            }

            if (!apexSpawnedThisDay && targetApexSamples > 0 && manager.collectedSamples >= targetApexSamples)
            {
                SpawnApex();
            }
        }

        private void SpawnPlunderer()
        {
            if (plundererPrefab == null || plundererSpawnPoint == null)
            {
                return;
            }

            if (plundererPrefab.scene.IsValid())
            {
                WarnScenePrefabReference(plundererPrefab, "Plunderer");
                return;
            }

            if (spawnedPlunderer != null)
            {
                return;
            }

            GameObject instance = Instantiate(plundererPrefab, plundererSpawnPoint.position, plundererSpawnPoint.rotation);
            if (IsServerActive())
            {
                NetworkObject netObj = instance.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Debug.LogWarning($"DinoSpawnerManager: Plunderer prefab '{plundererPrefab.name}' is missing NetworkObject. Destroying spawned instance.");
                    Destroy(instance);
                    return;
                }
                if (!netObj.IsSpawned)
                {
                    netObj.Spawn(true);
                }
            }

            ConfigurePlunderer(instance);
            spawnedPlunderer = instance;
        }

        private void SpawnHunter()
        {
            if (hunterPrefab == null)
            {
                return;
            }

            if (hunterPrefab.scene.IsValid())
            {
                WarnScenePrefabReference(hunterPrefab, "Hunter");
                return;
            }

            if (spawnedHunter != null)
            {
                return;
            }

            WalaPaNameHehe.Multiplayer.HunterMeterManager hunterManager = WalaPaNameHehe.Multiplayer.HunterMeterManager.Instance;
            if (hunterManager != null && !hunterManager.ShouldHunterHuntThisRun)
            {
                return;
            }

            Transform spawnPoint = GetHunterSpawnPoint();
            Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            GameObject instance = Instantiate(hunterPrefab, position, rotation);
            if (IsServerActive())
            {
                NetworkObject netObj = instance.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Debug.LogWarning($"DinoSpawnerManager: Hunter prefab '{hunterPrefab.name}' is missing NetworkObject. Destroying spawned instance.");
                    Destroy(instance);
                    return;
                }
                if (!netObj.IsSpawned)
                {
                    netObj.Spawn(true);
                }
            }

            spawnedHunter = instance;
            hunterSpawnPosition = position;
            hunterSpawnRotation = rotation;
            hasHunterSpawnLocation = true;
        }

        public bool TryGetHunterSpawnLocation(GameObject hunter, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            if (!hasHunterSpawnLocation || spawnedHunter == null || hunter == null)
            {
                return false;
            }

            if (spawnedHunter != hunter)
            {
                return false;
            }

            position = hunterSpawnPosition;
            rotation = hunterSpawnRotation;
            return true;
        }

        public void DespawnHunter(GameObject hunter)
        {
            if (hunter == null)
            {
                return;
            }

            if (spawnedHunter != null && spawnedHunter != hunter)
            {
                return;
            }

            NetworkObject netObj = hunter.GetComponent<NetworkObject>();
            if (IsServerActive() && netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            else
            {
                Destroy(hunter);
            }

            spawnedHunter = null;
            hasHunterSpawnLocation = false;
        }

        private Transform GetHunterSpawnPoint()
        {
            if (hunterSpawnPoints == null || hunterSpawnPoints.Length == 0)
            {
                return null;
            }

            Transform candidate = hunterSpawnPoints[Random.Range(0, hunterSpawnPoints.Length)];
            return candidate != null ? candidate : null;
        }

        private void SpawnApex()
        {
            if (apexPrefab == null)
            {
                return;
            }

            if (apexPrefab.scene.IsValid())
            {
                WarnScenePrefabReference(apexPrefab, "Apex");
                return;
            }

            CleanupDestroyed(spawnedApex);
            int targetCount = Mathf.Max(1, apexSpawnCount);
            int remaining = targetCount - spawnedApex.Count;
            if (remaining <= 0)
            {
                apexSpawnedThisDay = true;
                return;
            }

            for (int i = 0; i < remaining; i++)
            {
                Transform spawnPoint = GetApexSpawnPoint();
                Vector3 position = GetSpawnPosition(spawnPoint);
                Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

                GameObject instance = Instantiate(apexPrefab, position, rotation);
                if (IsServerActive())
                {
                    NetworkObject netObj = instance.GetComponent<NetworkObject>();
                    if (netObj == null)
                    {
                        Debug.LogWarning($"DinoSpawnerManager: Apex prefab '{apexPrefab.name}' is missing NetworkObject. Destroying spawned instance.");
                        Destroy(instance);
                        continue;
                    }
                    if (!netObj.IsSpawned)
                    {
                        netObj.Spawn(true);
                    }
                }

                spawnedApex.Add(instance);
            }

            apexSpawnedThisDay = spawnedApex.Count >= targetCount;
        }

        private void OnPlundererDespawned(float nextSpawnTime)
        {
            nextPlundererSpawnTime = nextSpawnTime;
        }

        private void ScheduleNextPlundererSpawn()
        {
            float delay = Mathf.Max(0.1f, Random.Range(plundererSpawnIntervalMin, plundererSpawnIntervalMax));
            nextPlundererSpawnTime = Time.time + delay;
        }

        private void ResetApexForDay()
        {
            WalaPaNameHehe.Multiplayer.GameManager manager = WalaPaNameHehe.Multiplayer.GameManager.Instance;
            trackedApexDay = manager != null ? manager.currentDay : -1;
            apexSpawnedThisDay = false;
            targetApexSamples = GetApexTargetSamples();
        }


        private int GetApexTargetSamples()
        {
            ApexDayThreshold dayRule = GetApexDayRule();
            if (dayRule != null)
            {
                if (dayRule.spawnOnDayStart)
                {
                    return 0;
                }

                int min = Mathf.Max(0, dayRule.samplesMin);
                int max = Mathf.Max(min, dayRule.samplesMax);
                if (max <= 0)
                {
                    return -1;
                }

                return Random.Range(min, max + 1);
            }

            return -1;
        }

        private bool ShouldSpawnApexOnDayStart()
        {
            ApexDayThreshold dayRule = GetApexDayRule();
            return dayRule != null && dayRule.spawnOnDayStart;
        }

        private ApexDayThreshold GetApexDayRule()
        {
            if (apexDayThresholds == null || apexDayThresholds.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < apexDayThresholds.Length; i++)
            {
                ApexDayThreshold rule = apexDayThresholds[i];
                if (rule != null && rule.day == trackedApexDay)
                {
                    return rule;
                }
            }

            return null;
        }

        private void ConfigurePlunderer(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            DinoAI ai = instance.GetComponent<DinoAI>();
            if (ai == null)
            {
                return;
            }

            ai.plundererSpawnPoint = plundererSpawnPoint;
            ai.plundererWaypoints = plundererWaypoints;
            ai.plundererPreloadHidden = plundererPreloadHidden;
            ai.plundererSpawnIntervalMin = plundererSpawnIntervalMin;
            ai.plundererSpawnIntervalMax = plundererSpawnIntervalMax;
            ai.plundererWaypointLoopsMin = plundererWaypointLoopsMin;
            ai.plundererWaypointLoopsMax = plundererWaypointLoopsMax;
            ai.plundererDropSearchRadius = Mathf.Max(ai.plundererDropSearchRadius, 9999f);
            if (plundererPreloadHidden)
            {
                ai.SetPlundererHidden(true);
            }
        }

        private void RefreshApexSpawnPoints()
        {
            if (!apexAutoCollectChildren)
            {
                if (apexSpawnPoints == null)
                {
                    apexSpawnPoints = new Transform[0];
                }
                return;
            }

            if (apexSpawnPointRoot == null)
            {
                apexSpawnPointRoot = transform;
            }

            int childCount = apexSpawnPointRoot.childCount;
            if (childCount <= 0)
            {
                apexSpawnPoints = new Transform[0];
                return;
            }

            Transform[] points = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                points[i] = apexSpawnPointRoot.GetChild(i);
            }

            apexSpawnPoints = points;
        }

        private Transform GetApexSpawnPoint()
        {
            if (apexSpawnPoints == null || apexSpawnPoints.Length == 0)
            {
                return apexSpawnPointRoot != null ? apexSpawnPointRoot : transform;
            }

            Transform candidate = apexSpawnPoints[Random.Range(0, apexSpawnPoints.Length)];
            return candidate != null ? candidate : transform;
        }

        private bool TryGetSpawnPointWithSpacing(float spacingRadius, out Transform spawnPoint)
        {
            spawnPoint = null;
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return false;
            }

            int desiredAttempts = Mathf.Max(1, spawnPointSearchAttempts);
            int attempts = Mathf.Max(desiredAttempts, spawnPoints.Length);
            for (int i = 0; i < attempts; i++)
            {
                Transform candidate = spawnPoints[Random.Range(0, spawnPoints.Length)];
                if (candidate == null)
                {
                    continue;
                }

                if (!IsSpawnTooClose(candidate.position, spacingRadius))
                {
                    spawnPoint = candidate;
                    return true;
                }
            }

            if (!forceSpawnWhenSpacingFails)
            {
                return false;
            }

            spawnPoint = GetFallbackSpawnPoint();
            return spawnPoint != null;
        }

        private Transform GetFallbackSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Transform candidate = spawnPoints[i];
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private Vector3 GetSpawnPosition(Transform spawnPoint)
        {
            if (spawnPoint == null)
            {
                return transform.position;
            }

            Vector3 position = spawnPoint.position;
            if (snapSpawnToNavMesh)
            {
                float radius = Mathf.Max(0.1f, navMeshSampleRadius);
                if (NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas))
                {
                    position = hit.position;
                }
                else
                {
                    float fallbackRadius = Mathf.Max(radius, 200f);
                    if (fallbackRadius > radius && NavMesh.SamplePosition(position, out hit, fallbackRadius, NavMesh.AllAreas))
                    {
                        position = hit.position;
                    }
                }
            }

            return position;
        }

        private void ApplyAggressionType(GameObject instance, DinoType type)
        {
            if (instance == null)
            {
                return;
            }

            DinoAI ai = instance.GetComponent<DinoAI>();
            if (ai == null)
            {
                return;
            }

            ai.aggressionType = type switch
            {
                DinoType.Passive => DinoAI.AggressionType.Passive,
                DinoType.Neutral => DinoAI.AggressionType.Neutral,
                DinoType.Roamer => DinoAI.AggressionType.Roamer,
                _ => ai.aggressionType
            };
        }

        private bool IsSpawnTooClose(Vector3 position, float radius)
        {
            if (radius <= 0f)
            {
                return false;
            }

            float radiusSqr = radius * radius;
            for (int i = 0; i < spawnConfigs.Length; i++)
            {
                List<GameObject> list = GetSpawnList(i);
                for (int j = 0; j < list.Count; j++)
                {
                    GameObject obj = list[j];
                    if (obj == null)
                    {
                        continue;
                    }

                    Vector3 diff = obj.transform.position - position;
                    if (diff.sqrMagnitude <= radiusSqr)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsServerActive()
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && nm.IsServer;
        }

        private void TryRegisterNetworkPrefabs()
        {
            if (!autoRegisterNetworkPrefabs)
            {
                return;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.NetworkConfig == null)
            {
                return;
            }

            bool canAdd = !nm.IsListening || !nm.NetworkConfig.ForceSamePrefabs;

            if (spawnConfigs != null)
            {
                for (int i = 0; i < spawnConfigs.Length; i++)
                {
                    DinoSpawnConfig config = spawnConfigs[i];
                    if (config == null || config.prefabs == null)
                    {
                        continue;
                    }

                    for (int p = 0; p < config.prefabs.Length; p++)
                    {
                        RegisterNetworkPrefab(nm, config.prefabs[p], $"SpawnConfig[{i}] {config.type}", canAdd);
                    }
                }
            }

            RegisterNetworkPrefab(nm, plundererPrefab, "Plunderer", canAdd);
            RegisterNetworkPrefab(nm, hunterPrefab, "Hunter", canAdd);
            RegisterNetworkPrefab(nm, apexPrefab, "Apex", canAdd);
        }

        private void RegisterNetworkPrefab(NetworkManager nm, GameObject prefab, string label, bool canAdd)
        {
            if (nm == null || prefab == null)
            {
                return;
            }

            if (prefab.scene.IsValid())
            {
                Debug.LogWarning($"DinoSpawnerManager: {label} prefab '{prefab.name}' is a scene object. Use a prefab asset reference instead.");
                return;
            }

            if (!prefab.TryGetComponent<NetworkObject>(out _))
            {
                if (nm.IsListening)
                {
                    Debug.LogWarning($"DinoSpawnerManager: {label} prefab '{prefab.name}' is missing NetworkObject. It will not spawn in multiplayer.");
                }
                return;
            }

            if (IsPrefabRegistered(nm, prefab))
            {
                return;
            }

            if (!canAdd)
            {
                if (nm.IsListening)
                {
                    Debug.LogWarning($"DinoSpawnerManager: {label} prefab '{prefab.name}' is not registered in NetworkManager prefabs. Add it to the NetworkManager config before starting the session.");
                }
                return;
            }

            nm.AddNetworkPrefab(prefab);
        }

        private static bool IsPrefabRegistered(NetworkManager nm, GameObject prefab)
        {
            if (nm == null || nm.NetworkConfig == null || prefab == null)
            {
                return false;
            }

            if (nm.NetworkConfig.Prefabs.Contains(prefab))
            {
                return true;
            }

            List<NetworkPrefabsList> lists = nm.NetworkConfig.Prefabs.NetworkPrefabsLists;
            if (lists == null)
            {
                return false;
            }

            for (int i = 0; i < lists.Count; i++)
            {
                NetworkPrefabsList list = lists[i];
                if (list != null && list.Contains(prefab))
                {
                    return true;
                }
            }

            return false;
        }

        private List<GameObject> GetSpawnList(int configIndex)
        {
            if (!spawnedByConfigIndex.TryGetValue(configIndex, out List<GameObject> list))
            {
                list = new List<GameObject>();
                spawnedByConfigIndex[configIndex] = list;
            }

            return list;
        }

        private GameObject GetRandomPrefab(DinoSpawnConfig config, int configIndex)
        {
            if (config == null || config.prefabs == null || config.prefabs.Length == 0)
            {
                return null;
            }

            int index = Random.Range(0, config.prefabs.Length);
            GameObject prefab = config.prefabs[index];
            if (prefab != null && !prefab.scene.IsValid())
            {
                return prefab;
            }

            for (int i = 0; i < config.prefabs.Length; i++)
            {
                prefab = config.prefabs[i];
                if (prefab != null && !prefab.scene.IsValid())
                {
                    return prefab;
                }
            }

            if (prefab != null && prefab.scene.IsValid())
            {
                WarnScenePrefabReference(prefab, $"SpawnConfig[{configIndex}]");
            }

            return null;
        }

        private void WarnScenePrefabReference(GameObject sceneObjectReference, string label)
        {
            if (sceneObjectReference == null)
            {
                return;
            }

            int id = sceneObjectReference.GetInstanceID();
            if (warnedScenePrefabInstanceIds.Contains(id))
            {
                return;
            }

            warnedScenePrefabInstanceIds.Add(id);
            Debug.LogWarning(
                $"DinoSpawnerManager: {label} reference '{sceneObjectReference.name}' points to a scene object, not a prefab asset. " +
                "This can cause Netcode scene-object hash collisions (clone errors) on scene transitions. " +
                "Drag the prefab from the Project window (or convert to a prefab) and assign that instead.",
                sceneObjectReference);
        }


        private static void CleanupDestroyed(List<GameObject> list)
        {
            if (list == null)
            {
                return;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                {
                    list.RemoveAt(i);
                }
            }
        }

        private float GetSpacingRadiusForConfig(DinoSpawnConfig config, GameObject selectedPrefab)
        {
            float radius = minSpacingRadius;
            if (useAutoSpacing)
            {
                float prefabRadius = selectedPrefab != null ? GetPrefabRadius(selectedPrefab) : 0f;
                radius = Mathf.Max(radius, prefabRadius * spacingMultiplier);
            }

            if (maxSpacingRadius > 0f)
            {
                radius = Mathf.Min(radius, maxSpacingRadius);
            }

            return Mathf.Max(0f, radius);
        }

        private static float GetPrefabRadius(GameObject prefab)
        {
            if (prefab == null)
            {
                return 0f;
            }

            Bounds bounds = new Bounds();
            bool hasBounds = false;
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds)
            {
                Collider[] cols = prefab.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = c.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(c.bounds);
                    }
                }
            }

            if (!hasBounds)
            {
                return 0f;
            }

            Vector3 size = bounds.size;
            float diameter = Mathf.Max(size.x, size.z);
            return Mathf.Max(0.01f, diameter * 0.5f);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showSpacingGizmos)
            {
                return;
            }

            if (spawnPoints == null || spawnPoints.Length == 0 || spawnConfigs == null)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            for (int i = 0; i < spawnConfigs.Length; i++)
            {
                DinoSpawnConfig config = spawnConfigs[i];
                if (config == null || config.prefabs == null || config.prefabs.Length == 0)
                {
                    continue;
                }

                float bestRadius = minSpacingRadius;
                if (useAutoSpacing)
                {
                    for (int p = 0; p < config.prefabs.Length; p++)
                    {
                        GameObject prefab = config.prefabs[p];
                        if (prefab == null)
                        {
                            continue;
                        }

                        float prefabRadius = GetPrefabRadius(prefab) * spacingMultiplier;
                        if (prefabRadius > bestRadius)
                        {
                            bestRadius = prefabRadius;
                        }
                    }
                }

                if (maxSpacingRadius > 0f)
                {
                    bestRadius = Mathf.Min(bestRadius, maxSpacingRadius);
                }

                for (int s = 0; s < spawnPoints.Length; s++)
                {
                    Transform point = spawnPoints[s];
                    if (point == null)
                    {
                        continue;
                    }

                    Gizmos.DrawWireSphere(point.position, bestRadius);
                }
            }
        }
    }
}
