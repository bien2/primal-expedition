using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class DinoSpawnerManagerV2 : MonoBehaviour
    {
        public enum SpawnRadiusPreset
        {
            Custom = 0,
            Indoors = 1,
            Mid = 2,
            Wide = 3
        }

        [System.Serializable]
        public class SpawnPointEntry
        {
            public Transform spawnPoint;
            public SpawnRadiusPreset radiusPreset = SpawnRadiusPreset.Custom;
            [Min(0f)] public float minSpawnRadius = 0f;
            [Min(0f)] public float maxSpawnRadius = 0f;
        }

        [System.Serializable]
        public class ThreatSpawnSettings
        {
            [Header("Spawn")]
            public DangerMeterManager.ThreatLevel spawnAt = DangerMeterManager.ThreatLevel.Low;
            [Min(0)] public int spawnCount = 0;
            public GameObject[] prefabs;
            public SpawnPointEntry[] spawnPoints;
        }

        [System.Serializable]
        public class ThreatSpawnSettingsRange
        {
            [Header("Spawn")]
            public DangerMeterManager.ThreatLevel spawnAt = DangerMeterManager.ThreatLevel.Low;
            [Min(0)] public int spawnCountMin = 0;
            [Min(0)] public int spawnCountMax = 0;
            public GameObject[] prefabs;
            public SpawnPointEntry[] spawnPoints;
        }

        [Header("Passive / Neutral")]
        [SerializeField] private ThreatSpawnSettingsRange passiveNeutral = new ThreatSpawnSettingsRange();

        [System.Serializable]
        public class RoamerTier
        {
            [Header("Spawn")]
            public DangerMeterManager.ThreatLevel spawnAt = DangerMeterManager.ThreatLevel.Low;
            [Min(0)] public int spawnCountMin = 0;
            [Min(0)] public int spawnCountMax = 0;
        }

        [System.Serializable]
        public class RoamerSpawnSettings
        {
            [SerializeField] private GameObject[] prefabs;
            [SerializeField] private SpawnPointEntry[] spawnPoints;
            [SerializeField] private RoamerTier[] tiers = new RoamerTier[0];

            public GameObject[] Prefabs => prefabs;
            public SpawnPointEntry[] SpawnPoints => spawnPoints;
            public RoamerTier[] Tiers => tiers;
        }

        [Header("Roamer")]
        [SerializeField] private RoamerSpawnSettings roamer = new RoamerSpawnSettings();

        [System.Serializable]
        public class HunterSpawnSettings
        {
            public DangerMeterManager.ThreatLevel spawnAt = DangerMeterManager.ThreatLevel.Low;
            public GameObject prefab;
            public SpawnPointEntry[] spawnPoints;
        }

        [Header("Hunter")]
        [SerializeField] private HunterSpawnSettings hunter = new HunterSpawnSettings();
        [SerializeField] private bool hunterForceHuntTest;
        [SerializeField] private bool hunterForceHuntPlayCues = true;

        [System.Serializable]
        public class ApexTier
        {
            [Header("Spawn")]
            public DangerMeterManager.ThreatLevel spawnAt = DangerMeterManager.ThreatLevel.Low;
            [Min(0)] public int spawnCountMin = 0;
            [Min(0)] public int spawnCountMax = 0;
        }

        [System.Serializable]
        public class ApexSpawnSettings
        {
            [SerializeField] private GameObject[] prefabs;
            [SerializeField] private SpawnPointEntry[] spawnPoints;
            [SerializeField] private ApexTier[] tiers = new ApexTier[0];

            public GameObject[] Prefabs => prefabs;
            public SpawnPointEntry[] SpawnPoints => spawnPoints;
            public ApexTier[] Tiers => tiers;
        }

        [Header("Apex")]
        [SerializeField] private ApexSpawnSettings apex = new ApexSpawnSettings();

        [System.Serializable]
        public class PlundererSpawnSettings
        {
            [System.Serializable]
            public class PlundererThreatSpawnSettings
            {
                [Header("Spawn")]
                public DangerMeterManager.ThreatLevel spawnAt = DangerMeterManager.ThreatLevel.Low;
                public GameObject[] prefabs;
                public SpawnPointEntry[] spawnPoints;
            }

            [SerializeField] private PlundererThreatSpawnSettings spawn = new PlundererThreatSpawnSettings();
            [SerializeField] private Transform spawnPoint;
            [SerializeField] private Transform[] waypoints;
            [SerializeField] private bool preloadHidden = true;
            [SerializeField] private float spawnIntervalMin = 60f;
            [SerializeField] private float spawnIntervalMax = 90f;
            [SerializeField] private int waypointLoopsMin = 0;
            [SerializeField] private int waypointLoopsMax = 0;

            public PlundererThreatSpawnSettings Spawn => spawn;
            public Transform SpawnPoint => spawnPoint;
            public Transform[] Waypoints => waypoints;
            public bool PreloadHidden => preloadHidden;
            public float SpawnIntervalMin => spawnIntervalMin;
            public float SpawnIntervalMax => spawnIntervalMax;
            public int WaypointLoopsMin => waypointLoopsMin;
            public int WaypointLoopsMax => waypointLoopsMax;
        }

        [Header("Plunderer")]
        [SerializeField] private PlundererSpawnSettings plunderer = new PlundererSpawnSettings();

        [Header("Dino Despawn Point")]
        [SerializeField] private Transform dinoDespawnPoint;

        private readonly HashSet<DangerMeterManager.ThreatLevel> processedThreatLevelsThisDay = new();
        private bool hunterMeterSubscribed;
        private GameObject spawnedHunter;
        private Vector3 hunterSpawnPosition;
        private Quaternion hunterSpawnRotation;
        private bool hasHunterSpawnLocation;
        private bool hunterSpawnedThisDay;
        private bool plundererDespawnSubscribed;
        private Coroutine plundererLoop;
        private GameObject spawnedPlunderer;
        private float nextPlundererSpawnTime = float.PositiveInfinity;

        private void Awake()
        {
            if (dinoDespawnPoint != null)
            {
                DinoDespawnService.SetPoint(dinoDespawnPoint);
            }
        }

        private void Start()
        {
            AttachToDangerMeter();
            AttachToHunterMeter();
            StartCoroutine(AttachHunterMeterNextFrame());
            AttachToPlundererDespawn();
            StartPlundererLoopIfEligible();
        }

        private void OnDisable()
        {
            if (dinoDespawnPoint != null)
            {
                DinoDespawnService.ClearPoint(dinoDespawnPoint);
            }
        }

        private void OnDestroy()
        {
            DetachFromDangerMeter();
            DetachFromHunterMeter();
            DetachFromPlundererDespawn();
            StopPlundererLoop();
        }

        private System.Collections.IEnumerator AttachHunterMeterNextFrame()
        {
            yield return null;
            AttachToHunterMeter();
            TrySpawnHunterIfEligible();
        }

        private void AttachToDangerMeter()
        {
            DangerMeterManager dm = DangerMeterManager.Instance;
            if (dm == null)
            {
                return;
            }

            dm.ThreatLevelUpdated -= HandleThreatLevelUpdated;
            dm.ThreatLevelUpdated += HandleThreatLevelUpdated;
        }

        private void DetachFromDangerMeter()
        {
            DangerMeterManager dm = DangerMeterManager.Instance;
            if (dm == null)
            {
                return;
            }

            dm.ThreatLevelUpdated -= HandleThreatLevelUpdated;
        }

        private void HandleThreatLevelUpdated(DangerMeterManager.ThreatLevel level)
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            if (level == DangerMeterManager.ThreatLevel.Low)
            {
                processedThreatLevelsThisDay.Clear();
                hunterSpawnedThisDay = false;
            }

            if (processedThreatLevelsThisDay.Contains(level))
            {
                return;
            }

            processedThreatLevelsThisDay.Add(level);

            SpawnForThreat(passiveNeutral, level, "passive/neutral");
            SpawnRoamerForThreat(level);
            SpawnApexForThreat(level);
            SpawnPlundererForThreat(level);

            TrySpawnHunterIfEligible();
        }

        private void SpawnRoamerForThreat(DangerMeterManager.ThreatLevel level)
        {
            RoamerTier[] tiers = roamer != null ? roamer.Tiers : null;
            if (tiers == null || tiers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < tiers.Length; i++)
            {
                SpawnRoamerTierForThreat(tiers[i], level);
            }
        }

        private void SpawnRoamerTierForThreat(RoamerTier tier, DangerMeterManager.ThreatLevel level)
        {
            if (tier == null || tier.spawnAt != level)
            {
                return;
            }

            int min = Mathf.Max(0, tier.spawnCountMin);
            int max = Mathf.Max(min, tier.spawnCountMax);
            int count = max > 0 ? Random.Range(min, max + 1) : 0;
            if (count <= 0)
            {
                return;
            }

            GameObject[] prefabs = roamer != null ? roamer.Prefabs : null;
            if (prefabs == null || prefabs.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (roamer) - no prefabs");
                return;
            }

            SpawnPointEntry[] spawnPoints = roamer != null ? roamer.SpawnPoints : null;
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (roamer) - no spawn points");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryPickSpawnPoint(spawnPoints, out SpawnPointEntry entry))
                {
                    break;
                }

                GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
                if (prefab == null)
                {
                    continue;
                }

                Vector3 pos = GetSpawnPosition(entry);
                Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : transform.rotation;
                GameObject instance = Instantiate(prefab, pos, rot);

                if (IsServerActive())
                {
                    NetworkObject netObj = instance.GetComponent<NetworkObject>();
                    if (netObj == null)
                    {
                        Debug.LogWarning($"DinoSpawnerManagerV2: Roamer prefab '{prefab.name}' missing NetworkObject. Destroying spawned instance.");
                        Destroy(instance);
                        continue;
                    }
                    if (!netObj.IsSpawned)
                    {
                        netObj.Spawn(true);
                    }
                }

                spawned++;
            }

            if (spawned > 0)
            {
                PostSpawnLog($"spawned V2 roamer - {spawned}");
            }
        }

        private void SpawnApexForThreat(DangerMeterManager.ThreatLevel level)
        {
            ApexTier[] tiers = apex != null ? apex.Tiers : null;
            if (tiers == null || tiers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < tiers.Length; i++)
            {
                SpawnApexTierForThreat(tiers[i], level);
            }
        }

        private void SpawnApexTierForThreat(ApexTier tier, DangerMeterManager.ThreatLevel level)
        {
            if (tier == null || tier.spawnAt != level)
            {
                return;
            }

            int min = Mathf.Max(0, tier.spawnCountMin);
            int max = Mathf.Max(min, tier.spawnCountMax);
            int count = max > 0 ? Random.Range(min, max + 1) : 0;
            if (count <= 0)
            {
                return;
            }

            GameObject[] prefabs = apex != null ? apex.Prefabs : null;
            if (prefabs == null || prefabs.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (apex) - no prefabs");
                return;
            }

            SpawnPointEntry[] spawnPoints = apex != null ? apex.SpawnPoints : null;
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (apex) - no spawn points");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryPickSpawnPoint(spawnPoints, out SpawnPointEntry entry))
                {
                    break;
                }

                GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
                if (prefab == null)
                {
                    continue;
                }

                Vector3 pos = GetSpawnPosition(entry);
                Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : transform.rotation;
                GameObject instance = Instantiate(prefab, pos, rot);

                if (IsServerActive())
                {
                    NetworkObject netObj = instance.GetComponent<NetworkObject>();
                    if (netObj == null)
                    {
                        Debug.LogWarning($"DinoSpawnerManagerV2: Apex prefab '{prefab.name}' missing NetworkObject. Destroying spawned instance.");
                        Destroy(instance);
                        continue;
                    }
                    if (!netObj.IsSpawned)
                    {
                        netObj.Spawn(true);
                    }
                }

                spawned++;
            }

            if (spawned > 0)
            {
                PostSpawnLog($"spawned V2 apex - {spawned}");
            }
        }

        private void SpawnPlundererForThreat(DangerMeterManager.ThreatLevel level)
        {
            CleanupDestroyed(ref spawnedPlunderer);
            if (spawnedPlunderer != null)
            {
                return;
            }

            PlundererSpawnSettings.PlundererThreatSpawnSettings spawn = plunderer != null ? plunderer.Spawn : null;
            if (spawn == null || spawn.spawnAt != level)
            {
                return;
            }

            SpawnPlunderer(spawn);
        }

        private void SpawnPlunderer(PlundererSpawnSettings.PlundererThreatSpawnSettings spawn)
        {
            if (spawn.prefabs == null || spawn.prefabs.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (plunderer) - no prefabs");
                return;
            }

            if (spawn.spawnPoints == null || spawn.spawnPoints.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (plunderer) - no spawn points");
                return;
            }

            if (!TryPickSpawnPoint(spawn.spawnPoints, out SpawnPointEntry entry))
            {
                return;
            }

            GameObject prefab = spawn.prefabs[Random.Range(0, spawn.prefabs.Length)];
            if (prefab == null)
            {
                return;
            }

            Vector3 pos = GetSpawnPosition(entry);
            Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : Quaternion.identity;
            GameObject instance = Instantiate(prefab, pos, rot);

            if (IsServerActive())
            {
                NetworkObject netObj = instance.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Debug.LogWarning($"DinoSpawnerManagerV2: Plunderer prefab '{prefab.name}' missing NetworkObject. Destroying spawned instance.");
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
            nextPlundererSpawnTime = float.PositiveInfinity;
            PostSpawnLog("spawned V2 plunderer - 1");
        }

        private void StartPlundererLoopIfEligible()
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            if (plundererLoop != null)
            {
                return;
            }

            plundererLoop = StartCoroutine(PlundererSpawnLoop());
        }

        private void StopPlundererLoop()
        {
            if (plundererLoop == null)
            {
                return;
            }

            StopCoroutine(plundererLoop);
            plundererLoop = null;
        }

        private System.Collections.IEnumerator PlundererSpawnLoop()
        {
            while (true)
            {
                TrySpawnPlundererFromLoop();
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void TrySpawnPlundererFromLoop()
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            bool wasAlive = spawnedPlunderer != null;
            CleanupDestroyed(ref spawnedPlunderer);
            if (wasAlive && spawnedPlunderer == null)
            {
                ScheduleNextPlundererSpawn();
            }
            if (spawnedPlunderer != null)
            {
                return;
            }

            PlundererSpawnSettings.PlundererThreatSpawnSettings spawn = plunderer != null ? plunderer.Spawn : null;
            if (spawn == null)
            {
                return;
            }

            DangerMeterManager dm = DangerMeterManager.Instance;
            DangerMeterManager.ThreatLevel currentThreat = dm != null ? dm.CurrentThreat : DangerMeterManager.ThreatLevel.Low;
            if (currentThreat < spawn.spawnAt)
            {
                return;
            }

            if (float.IsPositiveInfinity(nextPlundererSpawnTime))
            {
                nextPlundererSpawnTime = Time.time;
            }

            if (Time.time < nextPlundererSpawnTime)
            {
                return;
            }

            SpawnPlunderer(spawn);
        }

        private void AttachToPlundererDespawn()
        {
            if (plundererDespawnSubscribed)
            {
                return;
            }

            PlundererAggressionBehavior.PlundererDespawned -= HandlePlundererDespawned;
            PlundererAggressionBehavior.PlundererDespawned += HandlePlundererDespawned;
            plundererDespawnSubscribed = true;
        }

        private void DetachFromPlundererDespawn()
        {
            if (!plundererDespawnSubscribed)
            {
                return;
            }

            PlundererAggressionBehavior.PlundererDespawned -= HandlePlundererDespawned;
            plundererDespawnSubscribed = false;
        }

        private void HandlePlundererDespawned(float nextSpawnTime)
        {
            spawnedPlunderer = null;
            if (nextSpawnTime > Time.time)
            {
                nextPlundererSpawnTime = nextSpawnTime;
                return;
            }

            ScheduleNextPlundererSpawn();
        }

        private void ScheduleNextPlundererSpawn()
        {
            float min = plunderer != null ? Mathf.Max(0.1f, plunderer.SpawnIntervalMin) : 0.1f;
            float max = plunderer != null ? Mathf.Max(min, plunderer.SpawnIntervalMax) : min;
            float delay = Random.Range(min, max);
            nextPlundererSpawnTime = Time.time + delay;
        }

        private void SpawnForThreat(
            ThreatSpawnSettingsRange settings,
            DangerMeterManager.ThreatLevel current,
            string label)
        {
            if (settings == null || settings.spawnAt != current)
            {
                return;
            }

            int min = Mathf.Max(0, settings.spawnCountMin);
            int max = Mathf.Max(min, settings.spawnCountMax);
            int count = max > 0 ? Random.Range(min, max + 1) : 0;
            if (count <= 0)
            {
                return;
            }

            if (settings.prefabs == null || settings.prefabs.Length == 0)
            {
                PostSpawnLog($"V2 spawn skipped ({label}) - no prefabs");
                return;
            }

            if (settings.spawnPoints == null || settings.spawnPoints.Length == 0)
            {
                PostSpawnLog($"V2 spawn skipped ({label}) - no spawn points");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryPickSpawnPoint(settings.spawnPoints, out SpawnPointEntry entry))
                {
                    break;
                }

                GameObject prefab = settings.prefabs[Random.Range(0, settings.prefabs.Length)];
                if (prefab == null)
                {
                    continue;
                }

                Vector3 pos = GetSpawnPosition(entry);
                Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : Quaternion.identity;
                GameObject instance = Instantiate(prefab, pos, rot);

                if (IsServerActive())
                {
                    NetworkObject netObj = instance.GetComponent<NetworkObject>();
                    if (netObj == null)
                    {
                        Debug.LogWarning($"DinoSpawnerManagerV2: Prefab '{prefab.name}' missing NetworkObject. Destroying spawned instance.");
                        Destroy(instance);
                        continue;
                    }
                    if (!netObj.IsSpawned)
                    {
                        netObj.Spawn(true);
                    }
                }

                spawned++;
            }

            if (spawned > 0)
            {
                PostSpawnLog($"spawned V2 {label} - {spawned}");
            }
        }

        private void SpawnForThreat(
            ThreatSpawnSettings settings,
            DangerMeterManager.ThreatLevel current,
            string label,
            bool configurePlunderer = false)
        {
            if (settings == null || settings.spawnAt != current)
            {
                return;
            }

            int count = Mathf.Max(0, settings.spawnCount);
            if (count <= 0)
            {
                return;
            }

            if (settings.prefabs == null || settings.prefabs.Length == 0)
            {
                PostSpawnLog($"V2 spawn skipped ({label}) - no prefabs");
                return;
            }

            if (settings.spawnPoints == null || settings.spawnPoints.Length == 0)
            {
                PostSpawnLog($"V2 spawn skipped ({label}) - no spawn points");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryPickSpawnPoint(settings.spawnPoints, out SpawnPointEntry entry))
                {
                    break;
                }

                GameObject prefab = settings.prefabs[Random.Range(0, settings.prefabs.Length)];
                if (prefab == null)
                {
                    continue;
                }

                Vector3 pos = GetSpawnPosition(entry);
                Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : Quaternion.identity;
                GameObject instance = Instantiate(prefab, pos, rot);

                if (IsServerActive())
                {
                    NetworkObject netObj = instance.GetComponent<NetworkObject>();
                    if (netObj == null)
                    {
                        Debug.LogWarning($"DinoSpawnerManagerV2: Prefab '{prefab.name}' missing NetworkObject. Destroying spawned instance.");
                        Destroy(instance);
                        continue;
                    }
                    if (!netObj.IsSpawned)
                    {
                        netObj.Spawn(true);
                    }
                }

                if (configurePlunderer)
                {
                    ConfigurePlunderer(instance);
                }

                spawned++;
            }

            if (spawned > 0)
            {
                PostSpawnLog($"spawned V2 {label} - {spawned}");
            }
        }

        private void AttachToHunterMeter()
        {
            if (hunterMeterSubscribed)
            {
                return;
            }

            HunterMeterManager hm = HunterMeterManager.Instance;
            if (hm == null)
            {
                return;
            }

            hm.HuntPlanChanged -= HandleHuntPlanChanged;
            hm.HuntPlanChanged += HandleHuntPlanChanged;
            hunterMeterSubscribed = true;
        }

        private void DetachFromHunterMeter()
        {
            if (!hunterMeterSubscribed)
            {
                return;
            }

            HunterMeterManager hm = HunterMeterManager.Instance;
            if (hm != null)
            {
                hm.HuntPlanChanged -= HandleHuntPlanChanged;
            }

            hunterMeterSubscribed = false;
        }

        private void HandleHuntPlanChanged()
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            TrySpawnHunterIfEligible();
        }

        private void TrySpawnHunterIfEligible()
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            AttachToHunterMeter();
            if (hunterSpawnedThisDay)
            {
                return;
            }

            CleanupDestroyed(ref spawnedHunter);
            if (spawnedHunter != null)
            {
                return;
            }

            HunterMeterManager hm = HunterMeterManager.Instance;
            if (!hunterForceHuntTest && (hm == null || !hm.ShouldHunterHuntThisRun))
            {
                return;
            }

            DangerMeterManager dm = DangerMeterManager.Instance;
            DangerMeterManager.ThreatLevel currentThreat = dm != null ? dm.CurrentThreat : DangerMeterManager.ThreatLevel.Low;
            if (hunter == null || currentThreat < hunter.spawnAt)
            {
                return;
            }

            if (hunter.prefab == null)
            {
                PostSpawnLog("V2 spawn skipped (hunter) - no prefabs");
                return;
            }

            if (hunter.spawnPoints == null || hunter.spawnPoints.Length == 0)
            {
                PostSpawnLog("V2 spawn skipped (hunter) - no spawn points");
                return;
            }

            if (!TryPickSpawnPoint(hunter.spawnPoints, out SpawnPointEntry entry))
            {
                return;
            }

            GameObject prefab = hunter.prefab;

            Vector3 position = GetSpawnPosition(entry);
            Quaternion rotation = entry.spawnPoint != null ? entry.spawnPoint.rotation : transform.rotation;
            GameObject instance = Instantiate(prefab, position, rotation);

            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                Vector3 agentPosition = instance.transform.position;
                const float sampleRadius = 2f;
                if (NavMesh.SamplePosition(agentPosition, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
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

            if (IsServerActive())
            {
                NetworkObject netObj = instance.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Debug.LogWarning($"DinoSpawnerManagerV2: Hunter prefab '{prefab.name}' is missing NetworkObject. Destroying spawned instance.");
                    Destroy(instance);
                    return;
                }
                if (!netObj.IsSpawned)
                {
                    netObj.Spawn(true);
                }
            }

            spawnedHunter = instance;
            hunterSpawnPosition = instance.transform.position;
            hunterSpawnRotation = instance.transform.rotation;
            hasHunterSpawnLocation = true;
            hunterSpawnedThisDay = true;
            PostSpawnLog("spawned V2 hunter");
        }

        public bool TryGetHunterSpawnLocation(GameObject hunterInstance, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            CleanupDestroyed(ref spawnedHunter);
            if (!hasHunterSpawnLocation || spawnedHunter == null || hunterInstance == null)
            {
                return false;
            }

            if (spawnedHunter != hunterInstance)
            {
                return false;
            }

            position = hunterSpawnPosition;
            rotation = hunterSpawnRotation;
            return true;
        }

        public void DespawnHunter(GameObject hunterInstance)
        {
            if (hunterInstance == null)
            {
                return;
            }

            CleanupDestroyed(ref spawnedHunter);
            if (spawnedHunter != null && spawnedHunter != hunterInstance)
            {
                return;
            }

            DinoAI ai = hunterInstance.GetComponent<DinoAI>();
            if (ai != null && CoopGuard.IsServerOrOffline())
            {
                ai.StartFleeToDespawnPoint(false);
                StartCoroutine(ClearHunterWhenGone(hunterInstance));
                return;
            }

            NetworkObject netObj = hunterInstance.GetComponent<NetworkObject>();
            if (IsServerActive() && netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            else
            {
                Destroy(hunterInstance);
            }

            if (spawnedHunter == hunterInstance)
            {
                spawnedHunter = null;
                hasHunterSpawnLocation = false;
            }
        }

        private System.Collections.IEnumerator ClearHunterWhenGone(GameObject hunterInstance)
        {
            float timeout = Time.time + 30f;
            while (hunterInstance != null && Time.time < timeout)
            {
                yield return null;
            }

            if (spawnedHunter == hunterInstance || spawnedHunter == null)
            {
                spawnedHunter = null;
                hasHunterSpawnLocation = false;
            }
        }

        private static void CleanupDestroyed(ref GameObject obj)
        {
            if (obj == null)
            {
                obj = null;
            }
        }

        private static bool TryPickSpawnPoint(SpawnPointEntry[] spawnPoints, out SpawnPointEntry entry)
        {
            entry = null;
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return false;
            }

            int attempts = Mathf.Max(1, spawnPoints.Length * 2);
            for (int i = 0; i < attempts; i++)
            {
                SpawnPointEntry candidate = spawnPoints[Random.Range(0, spawnPoints.Length)];
                if (candidate == null || candidate.spawnPoint == null)
                {
                    continue;
                }

                entry = candidate;
                return true;
            }

            return false;
        }

        private static Vector3 GetSpawnPosition(SpawnPointEntry entry)
        {
            if (entry == null || entry.spawnPoint == null)
            {
                return Vector3.zero;
            }

            GetResolvedSpawnRadii(entry, out float min, out float max);
            if (max <= 0f)
            {
                return entry.spawnPoint.position;
            }

            float dist = max > min ? Random.Range(min, max) : min;
            Vector2 dir = Random.insideUnitCircle;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }
            dir.Normalize();

            Vector3 offset = new Vector3(dir.x, 0f, dir.y) * dist;
            return entry.spawnPoint.position + offset;
        }

        private static void GetResolvedSpawnRadii(SpawnPointEntry entry, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (entry == null)
            {
                return;
            }

            switch (entry.radiusPreset)
            {
                case SpawnRadiusPreset.Indoors:
                    min = 1f;
                    max = 4f;
                    break;
                case SpawnRadiusPreset.Mid:
                    min = 2f;
                    max = 8f;
                    break;
                case SpawnRadiusPreset.Wide:
                    min = 4f;
                    max = 12f;
                    break;
                default:
                    min = Mathf.Max(0f, entry.minSpawnRadius);
                    max = Mathf.Max(min, entry.maxSpawnRadius);
                    break;
            }
        }

        private void OnDrawGizmosSelected()
        {
            DrawSpawnPointGizmos(passiveNeutral != null ? passiveNeutral.spawnPoints : null);
            DrawSpawnPointGizmos(roamer != null ? roamer.SpawnPoints : null);
            DrawSpawnPointGizmos(hunter != null ? hunter.spawnPoints : null);
            DrawSpawnPointGizmos(apex != null ? apex.SpawnPoints : null);
            DrawSpawnPointGizmos(plunderer != null && plunderer.Spawn != null ? plunderer.Spawn.spawnPoints : null);
        }

        private static void DrawSpawnPointGizmos(SpawnPointEntry[] spawnPoints)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return;
            }

            Color minColor = new Color(0.2f, 0.6f, 1f, 0.85f); // blue
            Color maxColor = new Color(1f, 0.25f, 0.25f, 0.85f); // red

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                SpawnPointEntry entry = spawnPoints[i];
                if (entry == null || entry.spawnPoint == null)
                {
                    continue;
                }

                GetResolvedSpawnRadii(entry, out float min, out float max);
                Vector3 pos = entry.spawnPoint.position;

                if (max > 0f)
                {
                    Gizmos.color = maxColor;
                    Gizmos.DrawWireSphere(pos, max);
                }

                if (min > 0f)
                {
                    Gizmos.color = minColor;
                    Gizmos.DrawWireSphere(pos, min);
                }
            }
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

            ai.plundererSpawnPoint = plunderer != null ? plunderer.SpawnPoint : null;
            ai.plundererWaypoints = plunderer != null ? plunderer.Waypoints : null;
            ai.plundererPreloadHidden = plunderer != null && plunderer.PreloadHidden;
            ai.plundererSpawnIntervalMin = plunderer != null ? plunderer.SpawnIntervalMin : 0f;
            ai.plundererSpawnIntervalMax = plunderer != null ? plunderer.SpawnIntervalMax : 0f;
            ai.plundererWaypointLoopsMin = plunderer != null ? plunderer.WaypointLoopsMin : 0;
            ai.plundererWaypointLoopsMax = plunderer != null ? plunderer.WaypointLoopsMax : 0;
            ai.plundererDropSearchRadius = Mathf.Max(ai.plundererDropSearchRadius, 9999f);
            if (plunderer != null && plunderer.PreloadHidden)
            {
                ai.SetPlundererHidden(true);
            }
        }

        private static void PostSpawnLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            DangerMeterManager dm = DangerMeterManager.Instance;
            if (dm != null)
            {
                int secs = Mathf.FloorToInt(dm.ElapsedSeconds);
                message = $"{message} | Threat Level: {dm.CurrentThreat} (T+{secs}s)";
            }

            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.BroadcastHudLog(message);
                return;
            }

            SessionHud.PostLog(message);
        }

        private static bool IsServerActive()
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && nm.IsServer;
        }
    }
}
