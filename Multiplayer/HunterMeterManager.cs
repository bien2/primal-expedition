using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class HunterMeterManager : NetworkBehaviour
    {
        public static HunterMeterManager Instance { get; private set; }

        [Header("Meter Settings")]
        [SerializeField, Range(0f, 1f)] private float daytimeThreshold = 0.8f;
        [SerializeField] private float apexSeenCooldownSeconds = 10f;
        [SerializeField] private float roamerEncounterCooldownSeconds = 10f;

        [Header("Meter Gains")]
        [SerializeField] private float gainBloodSample = 0.03f;
        [SerializeField] private float gainDroneUse = 0.01f;
        [SerializeField] private float gainKnockdown = 0.05f;
        [SerializeField] private float gainDeath = 0.07f;
        [SerializeField] private float gainApexSeen = 0.07f;
        [SerializeField] private float gainRoamerEncounter = 0.05f;

        private readonly Dictionary<ulong, float> nextApexSeenTime = new();
        private readonly Dictionary<ulong, float> nextRoamerEncounterTime = new();
        private readonly Dictionary<ulong, float> storedMeterByClient = new();

        private bool huntPlanned;
        private bool isNightRun;
        private ulong huntTargetClientId = ulong.MaxValue;
        private bool runInitialized;
        private readonly NetworkVariable<bool> syncedHuntPlanned = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> syncedHuntTargetClientId = new(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool ShouldHunterHuntThisRun => IsNetworkActive()
            ? syncedHuntPlanned.Value
            : huntPlanned;
        public bool IsNightRun => isNightRun;
        public bool HasActiveHunt => (IsNetworkActive() ? syncedHuntPlanned.Value : huntPlanned)
            && (IsNetworkActive() ? syncedHuntTargetClientId.Value : huntTargetClientId) != ulong.MaxValue;
        public ulong HuntTargetClientId => IsNetworkActive() ? syncedHuntTargetClientId.Value : huntTargetClientId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (CoopGuard.IsServerOrOffline())
            {
                StartCoroutine(InitializeRunNextFrame());
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private System.Collections.IEnumerator InitializeRunNextFrame()
        {
            yield return null;
            InitializeRun();
        }

        public void InitializeRun()
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            DayNightTimer dayNight = FindFirstObjectByType<DayNightTimer>(FindObjectsInactive.Exclude);
            isNightRun = dayNight != null && dayNight.IsNight;
            SelectHuntTarget(isNightRun);
            runInitialized = true;

            HunterAggressionBehavior.ResetSessionState();

            if (!huntPlanned && GetAllPlayers().Count == 0)
            {
                StartCoroutine(RetryInitializeRun());
            }

            if (IsServer)
            {
                syncedHuntPlanned.Value = huntPlanned;
                syncedHuntTargetClientId.Value = huntTargetClientId;
            }
        }

        private System.Collections.IEnumerator RetryInitializeRun()
        {
            yield return new WaitForSeconds(0.5f);
            if (!runInitialized)
            {
                yield break;
            }

            if (GetAllPlayers().Count == 0)
            {
                yield break;
            }

            SelectHuntTarget(isNightRun);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            PlayerMovement player = ResolvePlayerMovement(clientId);
            if (player == null)
            {
                return;
            }

            if (storedMeterByClient.TryGetValue(clientId, out float stored))
            {
                player.ServerAddHunterMeter(stored - player.HunterMeterValue);
            }
        }

        public bool TryGetHuntTarget(out Transform target)
        {
            target = null;
            if (!ShouldHunterHuntThisRun || HuntTargetClientId == ulong.MaxValue)
            {
                return false;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                if (nm.ConnectedClients.TryGetValue(HuntTargetClientId, out NetworkClient client) &&
                    client != null && client.PlayerObject != null)
                {
                    target = client.PlayerObject.transform;
                    return true;
                }
                return false;
            }

            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null)
                {
                    target = players[i].transform;
                    return true;
                }
            }

            return false;
        }

        public void AddMeterForClient(ulong clientId, float delta)
        {
            PlayerMovement player = ResolvePlayerMovement(clientId);
            if (player == null)
            {
                return;
            }

            player.ServerAddHunterMeter(delta);
            storedMeterByClient[clientId] = player.HunterMeterValue;
        }

        public void ReportBloodSampleExtracted(ulong clientId)
        {
            AddMeterForClient(clientId, gainBloodSample);
        }

        public void ReportDroneUse(ulong clientId)
        {
            AddMeterForClient(clientId, gainDroneUse);
        }

        public void ReportKnockdown(PlayerMovement player)
        {
            if (player == null)
            {
                return;
            }

            player.ServerAddHunterMeter(gainKnockdown);
        }

        public void ReportDeath(PlayerMovement player)
        {
            if (player == null)
            {
                return;
            }

            player.ServerAddHunterMeter(gainDeath);
        }

        public void ReportApexSeen(Transform target)
        {
            PlayerMovement player = target != null ? target.GetComponentInParent<PlayerMovement>() : null;
            if (player == null)
            {
                return;
            }

            ulong clientId = player.OwnerClientId;
            if (IsOnCooldown(nextApexSeenTime, clientId, apexSeenCooldownSeconds))
            {
                return;
            }

            player.ServerAddHunterMeter(gainApexSeen);
        }

        public void ReportRoamerEncounter(Transform target)
        {
            PlayerMovement player = target != null ? target.GetComponentInParent<PlayerMovement>() : null;
            if (player == null)
            {
                return;
            }

            ulong clientId = player.OwnerClientId;
            if (IsOnCooldown(nextRoamerEncounterTime, clientId, roamerEncounterCooldownSeconds))
            {
                return;
            }

            player.ServerAddHunterMeter(gainRoamerEncounter);
        }

        public void OnHunterKill()
        {
            ResetAllMeters();
            huntPlanned = false;
            huntTargetClientId = ulong.MaxValue;
            if (IsServer)
            {
                syncedHuntPlanned.Value = false;
                syncedHuntTargetClientId.Value = ulong.MaxValue;
            }
        }

        public void ResetAllMeters()
        {
            foreach (PlayerMovement player in GetAllPlayers())
            {
                if (player != null)
                {
                    player.ServerResetHunterMeter();
                    storedMeterByClient[player.OwnerClientId] = 0f;
                }
            }
        }

        private void SelectHuntTarget(bool night)
        {
            huntPlanned = false;
            huntTargetClientId = ulong.MaxValue;

            List<PlayerMovement> players = GetAllPlayers();
            if (players.Count == 0)
            {
                return;
            }

            List<PlayerMovement> candidates = new();
            if (night)
            {
                candidates.AddRange(players);
            }
            else
            {
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerMovement player = players[i];
                    if (player != null && player.HunterMeterValue >= daytimeThreshold)
                    {
                        candidates.Add(player);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            PlayerMovement target = ChooseWeightedTarget(candidates, night);
            if (target == null)
            {
                return;
            }

            float meter = Mathf.Clamp01(target.HunterMeterValue);
            if (night)
            {
                huntPlanned = true;
            }
            else
            {
                huntPlanned = Random.value <= meter;
            }

            if (huntPlanned)
            {
                huntTargetClientId = target.OwnerClientId;
            }

            if (IsServer)
            {
                syncedHuntPlanned.Value = huntPlanned;
                syncedHuntTargetClientId.Value = huntTargetClientId;
            }
        }

        private PlayerMovement ChooseWeightedTarget(List<PlayerMovement> candidates, bool night)
        {
            List<PlayerMovement> hundred = new();
            for (int i = 0; i < candidates.Count; i++)
            {
                PlayerMovement player = candidates[i];
                if (player != null && player.HunterMeterValue >= 1f)
                {
                    hundred.Add(player);
                }
            }

            if (hundred.Count > 0)
            {
                return hundred[Random.Range(0, hundred.Count)];
            }

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                PlayerMovement player = candidates[i];
                if (player != null)
                {
                    total += Mathf.Max(0f, player.HunterMeterValue);
                }
            }

            if (total <= 0f)
            {
                return candidates[Random.Range(0, candidates.Count)];
            }

            float pick = Random.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                PlayerMovement player = candidates[i];
                if (player == null)
                {
                    continue;
                }

                acc += Mathf.Max(0f, player.HunterMeterValue);
                if (pick <= acc)
                {
                    return player;
                }
            }

            return candidates[0];
        }

        private bool IsOnCooldown(Dictionary<ulong, float> cooldowns, ulong clientId, float duration)
        {
            float now = Time.time;
            if (cooldowns.TryGetValue(clientId, out float nextTime) && now < nextTime)
            {
                return true;
            }

            cooldowns[clientId] = now + Mathf.Max(0f, duration);
            return false;
        }

        private PlayerMovement ResolvePlayerMovement(ulong clientId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                if (nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                    client != null && client.PlayerObject != null)
                {
                    return client.PlayerObject.GetComponentInChildren<PlayerMovement>(true);
                }
                return null;
            }

            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return players.Length > 0 ? players[0] : null;
        }

        private bool IsNetworkActive()
        {
            return NetworkManager != null && NetworkManager.IsListening;
        }

        private List<PlayerMovement> GetAllPlayers()
        {
            List<PlayerMovement> list = new();
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                foreach (var kvp in nm.ConnectedClients)
                {
                    NetworkClient client = kvp.Value;
                    if (client == null || client.PlayerObject == null)
                    {
                        continue;
                    }

                    PlayerMovement player = client.PlayerObject.GetComponentInChildren<PlayerMovement>(true);
                    if (player != null && !player.IsDead)
                    {
                        if (storedMeterByClient.TryGetValue(player.OwnerClientId, out float stored))
                        {
                            if (player.HunterMeterValue != stored)
                            {
                                player.ServerAddHunterMeter(stored - player.HunterMeterValue);
                            }
                        }
                        list.Add(player);
                    }
                }
            }
            else
            {
                PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i] != null && !players[i].IsDead)
                    {
                        list.Add(players[i]);
                    }
                }
            }

            return list;
        }
    }
}
