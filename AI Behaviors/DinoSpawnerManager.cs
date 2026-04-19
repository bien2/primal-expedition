using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using UnityEngine.Serialization;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    public class DinoSpawnerManager : MonoBehaviour
    {
        public enum DinoType
        {
            Passive,
            Neutral,
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

        [Header("Day Scaling (Passive/Neutral)")]
        [Header("Day 2 Increase (%)")]
        [Range(0f, 2f)] [SerializeField] private float day2MinIncrease = 0.10f;
        [Range(0f, 2f)] [SerializeField] private float day2MaxIncrease = 0.10f;
        [Header("Day 3 Increase (%)")]
        [Range(0f, 2f)] [SerializeField] private float day3MinIncrease = 0.30f;
        [Range(0f, 2f)] [SerializeField] private float day3MaxIncrease = 0.30f;
        [Header("Day 4+ Increase (%)")]
        [Range(0f, 2f)] [SerializeField] private float day4MinIncrease = 0.65f;
        [Range(0f, 2f)] [SerializeField] private float day4MaxIncrease = 0.65f;

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
        [Header("Spawn Loop")]
        [Min(0.05f)] [SerializeField] private float spawnTickIntervalSeconds = 0.5f;
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
        [SerializeField] private bool hunterForceHuntTest;
        [SerializeField] private bool hunterForceHuntPlayCues = true;

        [Header("Roamer")]
        [SerializeField] private GameObject[] roamerPrefabs;

        [System.Serializable]
        public class RoamerSampleThreshold
        {
            [Min(1)] public int day = 1;
            [FormerlySerializedAs("extractsMin")]
            [Min(0)] public int samplesMin = 3;
            [FormerlySerializedAs("extractsMax")]
            [Min(0)] public int samplesMax = 5;
        }

        [Header("Roamer Sample Thresholds (Blood Samples)")]
        [FormerlySerializedAs("roamerExtractThresholds")]
        [SerializeField] private RoamerSampleThreshold[] roamerSampleThresholds = new RoamerSampleThreshold[]
        {
            new RoamerSampleThreshold { day = 1, samplesMin = 3, samplesMax = 5 }
        };

        [Header("Apex")]
        [SerializeField] private GameObject apexPrefab;
        [SerializeField] private Transform apexSpawnPointRoot;
        [SerializeField] private bool apexAutoCollectChildren = true;
        [SerializeField] private Transform[] apexSpawnPoints;
        [System.Serializable]
        public class ApexDayThreshold
        {
            [Min(1)] public int day = 1;
            [Min(1)] public int spawnCount = 1;
            [Min(0)] public int samplesMin = 2;
            [Min(0)] public int samplesMax = 4;
            public bool spawnOnDayStart = false;
        }
        [SerializeField] private ApexDayThreshold[] apexDayThresholds;

        [Header("Abyss")]
        [SerializeField] private GameObject abyssPrefab;
        [Min(0f)] [SerializeField] private float abyssSpawnpoint = 3f;
        [Min(0.1f)] [SerializeField] private float abyssWaterSeconds = 30f;
        [Min(0f)] [SerializeField] private float abyssKillCooldownSeconds = 60f;
        [Min(0.05f)] [SerializeField] private float abyssCheckIntervalSeconds = 0.5f;
        [SerializeField] private bool abyssDebugSpawnOnly;

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
        private int apexSampleBaselineThisDay;
        private int nextApexSampleTarget = -1;
        private int apexSpawnedCountThisDay;
        private int trackedRoamerDay = -1;
        private int nextRoamerSampleTarget = -1;
        private readonly List<GameObject> spawnedApex = new();
        private readonly List<GameObject> spawnedRoamers = new();
        private bool spawnLoopsStarted;
        private readonly HashSet<int> warnedScenePrefabInstanceIds = new();
        private Coroutine abyssLoop;
        private float nextAbyssAllowedTime;
        private bool abyssWasOnCooldown;
        private readonly Dictionary<ulong, float> abyssWaterEnterTimeByClientId = new();
        private readonly Dictionary<ulong, PlayerMovement> abyssPlayersByClientId = new();
        private readonly HashSet<ulong> abyssCurrentWaterClientIds = new();
        private readonly List<ulong> abyssTieCandidates = new(4);
        private readonly List<ulong> abyssRemoveCandidates = new(8);
        private readonly Dictionary<int, float> abyssWaterEnterTimeByInstanceId = new();
        private readonly Dictionary<int, PlayerMovement> abyssPlayersByInstanceId = new();
        private readonly HashSet<int> abyssCurrentWaterInstanceIds = new();
        private readonly List<int> abyssTieCandidatesOffline = new(4);
        private readonly List<int> abyssRemoveCandidatesOffline = new(8);

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

            int despawnCount = spawnedApex.Count;
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

            if (despawnCount > 0)
            {
                SessionHud.PostLog("despawned apex");
            }
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
            abyssLoop = StartCoroutine(AbyssLoop());
            SpawnHunter();
        }

        private IEnumerator AbyssLoop()
        {
            while (true)
            {
                TickAbyss();
                yield return new WaitForSeconds(Mathf.Max(0.05f, abyssCheckIntervalSeconds));
            }
        }

        private void TickAbyss()
        {
            if (abyssPrefab == null)
            {
                return;
            }

            if (!IsServerActive() && !allowOfflineSpawn)
            {
                return;
            }

            float now = Time.time;
            bool onCooldown = now < nextAbyssAllowedTime;

            NetworkManager nm = NetworkManager.Singleton;
            if (IsServerActive() && nm != null)
            {
                abyssPlayersByClientId.Clear();
                abyssCurrentWaterClientIds.Clear();

                IReadOnlyList<NetworkClient> clients = nm.ConnectedClientsList;
                if (clients != null)
                {
                    for (int i = 0; i < clients.Count; i++)
                    {
                        NetworkClient client = clients[i];
                        if (client == null || client.PlayerObject == null)
                        {
                            continue;
                        }

                        PlayerMovement player = client.PlayerObject.GetComponent<PlayerMovement>();
                        if (player == null || player.IsDead)
                        {
                            continue;
                        }

                        ulong clientId = player.OwnerClientId;
                        abyssPlayersByClientId[clientId] = player;

                        if (!player.IsInWater)
                        {
                            continue;
                        }

                        abyssCurrentWaterClientIds.Add(clientId);
                        if (!abyssWaterEnterTimeByClientId.ContainsKey(clientId))
                        {
                            abyssWaterEnterTimeByClientId[clientId] = now;
                        }
                    }
                }

                abyssRemoveCandidates.Clear();
                foreach (KeyValuePair<ulong, float> kvp in abyssWaterEnterTimeByClientId)
                {
                    if (!abyssCurrentWaterClientIds.Contains(kvp.Key))
                    {
                        abyssRemoveCandidates.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < abyssRemoveCandidates.Count; i++)
                {
                    abyssWaterEnterTimeByClientId.Remove(abyssRemoveCandidates[i]);
                }

                if (onCooldown)
                {
                    abyssWasOnCooldown = true;
                    return;
                }

                if (abyssWasOnCooldown)
                {
                    abyssWasOnCooldown = false;
                    foreach (ulong clientId in abyssCurrentWaterClientIds)
                    {
                        abyssWaterEnterTimeByClientId[clientId] = now;
                    }
                }

                if (abyssCurrentWaterClientIds.Count <= 0)
                {
                    return;
                }

                float bestEnterTime = float.MaxValue;
                abyssTieCandidates.Clear();
                foreach (ulong clientId in abyssCurrentWaterClientIds)
                {
                    if (!abyssWaterEnterTimeByClientId.TryGetValue(clientId, out float enterTime))
                    {
                        continue;
                    }

                    if (enterTime < bestEnterTime - 0.0001f)
                    {
                        bestEnterTime = enterTime;
                        abyssTieCandidates.Clear();
                        abyssTieCandidates.Add(clientId);
                    }
                    else if (Mathf.Abs(enterTime - bestEnterTime) <= 0.0001f)
                    {
                        abyssTieCandidates.Add(clientId);
                    }
                }

                if (abyssTieCandidates.Count <= 0)
                {
                    return;
                }

                if (now - bestEnterTime < Mathf.Max(0.05f, abyssWaterSeconds))
                {
                    return;
                }

                ulong chosenClientId = abyssTieCandidates.Count == 1
                    ? abyssTieCandidates[0]
                    : abyssTieCandidates[Random.Range(0, abyssTieCandidates.Count)];

                if (!abyssPlayersByClientId.TryGetValue(chosenClientId, out PlayerMovement target) || target == null || target.IsDead)
                {
                    return;
                }

                Vector3 spawnPosition = target.transform.position + Vector3.down * Mathf.Max(0f, abyssSpawnpoint);
                GameObject instance = Instantiate(abyssPrefab, spawnPosition, Quaternion.Euler(-90f, 0f, 0f));
                if (IsServerActive())
                {
                    NetworkObject netObj = instance.GetComponent<NetworkObject>();
                    if (netObj == null)
                    {
                        Debug.LogWarning($"DinoSpawnerManager: Abyss prefab '{abyssPrefab.name}' is missing NetworkObject. Destroying spawned instance.");
                        Destroy(instance);
                        return;
                    }
                    if (!netObj.IsSpawned)
                    {
                        netObj.Spawn(true);
                    }
                }

                if (!abyssDebugSpawnOnly)
                {
                    DinoAttackController attackController = instance.GetComponent<DinoAttackController>();
                    if (attackController != null)
                    {
                        attackController.ForceInstakill(target);
                    }
                    else
                    {
                        PlayerHitHandler hitHandler = target.GetComponent<PlayerHitHandler>();
                        if (hitHandler == null)
                        {
                            hitHandler = target.GetComponentInChildren<PlayerHitHandler>(true);
                        }
                        hitHandler?.ServerApplyInstakillWithRagdoll(Vector3.zero);
                    }
                }

                nextAbyssAllowedTime = now + Mathf.Max(0f, abyssKillCooldownSeconds);
                return;
            }

            PlayerMovement[] offlinePlayers = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (offlinePlayers == null || offlinePlayers.Length == 0)
            {
                return;
            }

            abyssPlayersByInstanceId.Clear();
            abyssCurrentWaterInstanceIds.Clear();
            for (int i = 0; i < offlinePlayers.Length; i++)
            {
                PlayerMovement offlinePlayer = offlinePlayers[i];
                if (offlinePlayer == null || offlinePlayer.IsDead)
                {
                    continue;
                }

                int instanceId = offlinePlayer.GetInstanceID();
                abyssPlayersByInstanceId[instanceId] = offlinePlayer;

                if (!offlinePlayer.IsInWater)
                {
                    continue;
                }

                abyssCurrentWaterInstanceIds.Add(instanceId);
                if (!abyssWaterEnterTimeByInstanceId.ContainsKey(instanceId))
                {
                    abyssWaterEnterTimeByInstanceId[instanceId] = now;
                }
            }

            abyssRemoveCandidatesOffline.Clear();
            foreach (KeyValuePair<int, float> kvpOffline in abyssWaterEnterTimeByInstanceId)
            {
                if (!abyssCurrentWaterInstanceIds.Contains(kvpOffline.Key))
                {
                    abyssRemoveCandidatesOffline.Add(kvpOffline.Key);
                }
            }
            for (int i = 0; i < abyssRemoveCandidatesOffline.Count; i++)
            {
                abyssWaterEnterTimeByInstanceId.Remove(abyssRemoveCandidatesOffline[i]);
            }

            if (onCooldown)
            {
                abyssWasOnCooldown = true;
                return;
            }

            if (abyssWasOnCooldown)
            {
                abyssWasOnCooldown = false;
                foreach (int instanceId in abyssCurrentWaterInstanceIds)
                {
                    abyssWaterEnterTimeByInstanceId[instanceId] = now;
                }
            }

            if (abyssCurrentWaterInstanceIds.Count <= 0)
            {
                return;
            }

            float bestEnterTimeOffline = float.MaxValue;
            abyssTieCandidatesOffline.Clear();
            foreach (int instanceId in abyssCurrentWaterInstanceIds)
            {
                if (!abyssWaterEnterTimeByInstanceId.TryGetValue(instanceId, out float enterTimeOffline))
                {
                    continue;
                }

                if (enterTimeOffline < bestEnterTimeOffline - 0.0001f)
                {
                    bestEnterTimeOffline = enterTimeOffline;
                    abyssTieCandidatesOffline.Clear();
                    abyssTieCandidatesOffline.Add(instanceId);
                }
                else if (Mathf.Abs(enterTimeOffline - bestEnterTimeOffline) <= 0.0001f)
                {
                    abyssTieCandidatesOffline.Add(instanceId);
                }
            }

            if (abyssTieCandidatesOffline.Count <= 0)
            {
                return;
            }

            if (now - bestEnterTimeOffline < Mathf.Max(0.05f, abyssWaterSeconds))
            {
                return;
            }

            int chosenInstanceId = abyssTieCandidatesOffline.Count == 1
                ? abyssTieCandidatesOffline[0]
                : abyssTieCandidatesOffline[Random.Range(0, abyssTieCandidatesOffline.Count)];

            if (!abyssPlayersByInstanceId.TryGetValue(chosenInstanceId, out PlayerMovement targetOffline) || targetOffline == null || targetOffline.IsDead)
            {
                return;
            }

            Vector3 spawnPositionOffline = targetOffline.transform.position + Vector3.down * Mathf.Max(0f, abyssSpawnpoint);
            GameObject instanceOffline = Instantiate(abyssPrefab, spawnPositionOffline, Quaternion.Euler(-90f, 0f, 0f));

            if (!abyssDebugSpawnOnly)
            {
                DinoAttackController attackControllerOffline = instanceOffline.GetComponent<DinoAttackController>();
                if (attackControllerOffline != null)
                {
                    attackControllerOffline.ForceInstakill(targetOffline);
                }
                else
                {
                    PlayerHitHandler hitHandlerOffline = targetOffline.GetComponent<PlayerHitHandler>();
                    if (hitHandlerOffline == null)
                    {
                        hitHandlerOffline = targetOffline.GetComponentInChildren<PlayerHitHandler>(true);
                    }
                    hitHandlerOffline?.ServerApplyInstakillWithRagdoll(Vector3.zero);
                }
            }

            nextAbyssAllowedTime = now + Mathf.Max(0f, abyssKillCooldownSeconds);
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

            while (true)
            {
                TickSpawns();
                yield return new WaitForSeconds(Mathf.Max(0.05f, spawnTickIntervalSeconds));
            }
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

            int day = 1;
            WalaPaNameHehe.Multiplayer.GameManager manager = WalaPaNameHehe.Multiplayer.GameManager.Instance;
            if (manager != null)
            {
                day = Mathf.Max(1, manager.currentDay);
            }

            bool hasRoamerSettings = roamerPrefabs != null && roamerPrefabs.Length > 0;
            bool allowRoamerSpawns = hasRoamerSettings && ShouldAllowRoamerSpawns(day, manager);
            Dictionary<DinoType, int> spawnedByType = null;

            for (int i = 0; i < spawnConfigs.Length; i++)
            {
                DinoSpawnConfig config = spawnConfigs[i];

                List<GameObject> list = GetSpawnList(i);
                int beforeCleanupCount = list.Count;
                CleanupDestroyed(list);
                int removedCount = beforeCleanupCount - list.Count;
                if (config.type != DinoType.Passive && removedCount > 0)
                {
                    SessionHud.PostLog($"despawned {config.type.ToString().ToLowerInvariant()} - {removedCount}");
                }

                int minCount = Mathf.Max(0, config.minCount);
                int maxCount = Mathf.Max(minCount, config.maxCount);

                if (config.type == DinoType.Passive || config.type == DinoType.Neutral)
                {
                    int clampedDay = Mathf.Clamp(day, 1, 4);
                    float minIncrease = clampedDay switch
                    {
                        2 => Mathf.Max(0f, day2MinIncrease),
                        3 => Mathf.Max(0f, day3MinIncrease),
                        4 => Mathf.Max(0f, day4MinIncrease),
                        _ => 0f,
                    };
                    float maxIncrease = clampedDay switch
                    {
                        2 => Mathf.Max(0f, day2MaxIncrease),
                        3 => Mathf.Max(0f, day3MaxIncrease),
                        4 => Mathf.Max(0f, day4MaxIncrease),
                        _ => 0f,
                    };

                    int scaledMin = Mathf.RoundToInt(minCount * (1f + minIncrease));
                    int scaledMax = Mathf.RoundToInt(maxCount * (1f + maxIncrease));

                    minCount = Mathf.Max(0, scaledMin);
                    maxCount = Mathf.Max(minCount, scaledMax);
                }
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

                    spawnedByType ??= new Dictionary<DinoType, int>();
                    spawnedByType.TryGetValue(config.type, out int count);
                    spawnedByType[config.type] = count + 1;
                }
            }

            if (spawnedByType != null)
            {
                foreach (KeyValuePair<DinoType, int> kvp in spawnedByType)
                {
                    if (kvp.Value <= 0)
                    {
                        continue;
                    }

                    if (kvp.Key == DinoType.Passive)
                    {
                        continue;
                    }

                    SessionHud.PostLog($"spawned {kvp.Key.ToString().ToLowerInvariant()} - {kvp.Value}");
                }
            }

            bool spawnedRoamerThisRun = false;
            if (allowRoamerSpawns)
            {
                spawnedRoamerThisRun = SpawnRoamers();
            }

            if (spawnedRoamerThisRun)
            {
                ConsumeRoamerSpawn(manager);
            }
        }

        private bool SpawnRoamers()
        {
            if (roamerPrefabs == null || roamerPrefabs.Length == 0)
            {
                return false;
            }

            int beforeCleanupCount = spawnedRoamers.Count;
            CleanupDestroyed(spawnedRoamers);
            int removedCount = beforeCleanupCount - spawnedRoamers.Count;
            if (removedCount > 0)
            {
                SessionHud.PostLog($"despawned roamer - {removedCount}");
            }

            GameObject prefab = GetRandomRoamerPrefab();
            if (prefab == null)
            {
                return false;
            }

            if (prefab.scene.IsValid())
            {
                WarnScenePrefabReference(prefab, "Roamer");
                return false;
            }

            float spacingRadius = GetSpacingRadiusForRoamer(prefab);
            if (!TryGetSpawnPointWithSpacing(spacingRadius, out Transform spawnPoint))
            {
                return false;
            }

            Vector3 position = GetSpawnPosition(spawnPoint);
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
            GameObject instance = Instantiate(prefab, position, rotation);
            if (IsServerActive())
            {
                NetworkObject netObj = instance.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Debug.LogWarning($"DinoSpawnerManager: Roamer prefab '{prefab.name}' is missing NetworkObject. Destroying spawned instance.");
                    Destroy(instance);
                    return false;
                }
                if (!netObj.IsSpawned)
                {
                    netObj.Spawn(true);
                }
            }

            spawnedRoamers.Add(instance);
            SessionHud.PostLog("spawned roamer");
            return true;
        }

        private GameObject GetRandomRoamerPrefab()
        {
            if (roamerPrefabs == null || roamerPrefabs.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < roamerPrefabs.Length; i++)
            {
                GameObject prefab = roamerPrefabs[Random.Range(0, roamerPrefabs.Length)];
                if (prefab != null)
                {
                    return prefab;
                }
            }

            return null;
        }

        private float GetSpacingRadiusForRoamer(GameObject prefab)
        {
            float radius = minSpacingRadius;
            if (useAutoSpacing)
            {
                float prefabRadius = prefab != null ? GetPrefabRadius(prefab) : 0f;
                radius = Mathf.Max(radius, prefabRadius * spacingMultiplier);
            }

            if (maxSpacingRadius > 0f)
            {
                radius = Mathf.Min(radius, maxSpacingRadius);
            }

            return Mathf.Max(0f, radius);
        }

        private bool ShouldAllowRoamerSpawns(int day, WalaPaNameHehe.Multiplayer.GameManager manager)
        {
            if (manager == null)
            {
                return true;
            }

            if (trackedRoamerDay != day)
            {
                trackedRoamerDay = day;
                nextRoamerSampleTarget = GetRoamerTargetSamples(day);
            }

            if (nextRoamerSampleTarget <= 0)
            {
                return false;
            }

            return manager.collectedSamples >= nextRoamerSampleTarget;
        }

        private void ConsumeRoamerSpawn(WalaPaNameHehe.Multiplayer.GameManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.ResetCollectedSamples();
            nextRoamerSampleTarget = GetRoamerTargetSamples(trackedRoamerDay);
        }

        private int GetRoamerTargetSamples(int day)
        {
            RoamerSampleThreshold rule = GetRoamerRuleForDay(day);
            if (rule == null)
            {
                return -1;
            }

            int min = Mathf.Max(1, rule.samplesMin);
            int max = Mathf.Max(min, rule.samplesMax);
            return Random.Range(min, max + 1);
        }

        private RoamerSampleThreshold GetRoamerRuleForDay(int day)
        {
            if (roamerSampleThresholds == null || roamerSampleThresholds.Length == 0)
            {
                return null;
            }

            int safeDay = Mathf.Max(1, day);
            RoamerSampleThreshold best = null;
            for (int i = 0; i < roamerSampleThresholds.Length; i++)
            {
                RoamerSampleThreshold rule = roamerSampleThresholds[i];
                if (rule == null)
                {
                    continue;
                }

                if (rule.day <= safeDay && (best == null || rule.day > best.day))
                {
                    best = rule;
                }
            }

            if (best != null)
            {
                return best;
            }

            for (int i = 0; i < roamerSampleThresholds.Length; i++)
            {
                if (roamerSampleThresholds[i] != null)
                {
                    return roamerSampleThresholds[i];
                }
            }

            return null;
        }

        private void Update()
        {
            if (!ShouldRunSpawnLogic())
            {
                return;
            }

            TickApexSpawn();
        }

        private bool ShouldRunSpawnLogic()
        {
            if (IsServerActive())
            {
                return true;
            }

            // If Netcode session exists but we are not server, never run local spawn/despawn logic.
            if (NetworkManager.Singleton != null)
            {
                return false;
            }

            return allowOfflineSpawn;
        }

        private void TickApexSpawn()
        {
            if (!ShouldRunSpawnLogic())
            {
                return;
            }

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

            ApexDayThreshold dayRule = GetApexDayRule();
            int maxSpawns = dayRule != null ? Mathf.Max(1, dayRule.spawnCount) : 0;
            while (apexSpawnedCountThisDay < maxSpawns)
            {
                if (nextApexSampleTarget < 0)
                {
                    break;
                }

                if (manager.collectedSamples < nextApexSampleTarget)
                {
                    break;
                }

                SpawnApex();
                apexSpawnedCountThisDay += 1;

                if (apexSpawnedCountThisDay >= maxSpawns)
                {
                    break;
                }

                int interval = RollApexSampleInterval(dayRule);
                if (interval < 0)
                {
                    nextApexSampleTarget = -1;
                    break;
                }

                nextApexSampleTarget += interval;
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
            SessionHud.PostLog("spawned plunderer");
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
            if (!hunterForceHuntTest && hunterManager != null && !hunterManager.ShouldHunterHuntThisRun)
            {
                return;
            }

            Transform spawnPoint = GetHunterSpawnPoint();
            Vector3 position = GetSpawnPosition(spawnPoint);
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            GameObject instance = Instantiate(hunterPrefab, position, rotation);
            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                Vector3 agentPosition = instance.transform.position;
                float radius = Mathf.Max(0.1f, navMeshSampleRadius);
                if (!NavMesh.SamplePosition(agentPosition, out NavMeshHit hit, radius, NavMesh.AllAreas))
                {
                    float fallbackRadius = Mathf.Max(radius, 200f);
                    if (fallbackRadius > radius && NavMesh.SamplePosition(agentPosition, out hit, fallbackRadius, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                    }
                }
                else
                {
                    agent.Warp(hit.position);
                }
            }
            DinoAI ai = instance.GetComponent<DinoAI>();
            if (ai != null)
            {
                ai.hunterForceHuntTest = hunterForceHuntTest;
                ai.hunterForceHuntPlayCues = hunterForceHuntPlayCues;
            }
            else
            {
                Debug.LogWarning("DinoSpawnerManager: Hunter prefab missing DinoAI component. Force hunt test settings not applied.");
            }
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
            SessionHud.PostLog("spawned hunter");
        }

        public bool TryGetHunterSpawnLocation(GameObject hunter, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            if (!hasHunterSpawnLocation || spawnedHunter == null || hunter == null)
            {
                return false;
            }

            if (spawnedHunter == null)
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
            SessionHud.PostLog("despawned hunter");
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
                    return;
                }
                if (!netObj.IsSpawned)
                {
                    netObj.Spawn(true);
                }
            }

            spawnedApex.Add(instance);
            SessionHud.PostLog("spawned apex");
        }

        private void OnPlundererDespawned(float nextSpawnTime)
        {
            nextPlundererSpawnTime = nextSpawnTime;
            SessionHud.PostLog("Plunderer Flee");
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
            apexSpawnedCountThisDay = 0;
            apexSampleBaselineThisDay = manager != null ? manager.collectedSamples : 0;

            ApexDayThreshold dayRule = GetApexDayRule();
            if (dayRule == null)
            {
                nextApexSampleTarget = -1;
                return;
            }

            if (dayRule.spawnOnDayStart)
            {
                nextApexSampleTarget = apexSampleBaselineThisDay;
                return;
            }

            int interval = RollApexSampleInterval(dayRule);
            nextApexSampleTarget = interval >= 0 ? apexSampleBaselineThisDay + interval : -1;
        }

        private static int RollApexSampleInterval(ApexDayThreshold dayRule)
        {
            if (dayRule == null)
            {
                return -1;
            }

            int min = Mathf.Max(0, dayRule.samplesMin);
            int max = Mathf.Max(min, dayRule.samplesMax);
            if (max <= 0)
            {
                return -1;
            }

            return Random.Range(min, max + 1);
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

            if (roamerPrefabs != null)
            {
                for (int p = 0; p < roamerPrefabs.Length; p++)
                {
                    RegisterNetworkPrefab(nm, roamerPrefabs[p], "Roamer", canAdd);
                }
            }

            RegisterNetworkPrefab(nm, plundererPrefab, "Plunderer", canAdd);
            RegisterNetworkPrefab(nm, hunterPrefab, "Hunter", canAdd);
            RegisterNetworkPrefab(nm, apexPrefab, "Apex", canAdd);
            RegisterNetworkPrefab(nm, abyssPrefab, "Abyss", canAdd);
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
