using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class DangerMeterManager : NetworkBehaviour
    {
        public event System.Action ThreatTimerRestarted;
        public event System.Action<ThreatLevel> ThreatLevelUpdated;

        public enum ThreatLevel
        {
            Low = 0,
            Mild = 1,
            Moderate = 2,
            High = 3,
            Extreme = 4
        }

        public static DangerMeterManager Instance { get; private set; }

        [Header("Threat Level")]
        [Min(0f)] [SerializeField] private float mildThresholdSeconds = 30f;
        [Min(0f)] [SerializeField] private float moderateThresholdSeconds = 60f;
        [Min(0f)] [SerializeField] private float highThresholdSeconds = 300f;
        [Min(0f)] [SerializeField] private float extremeThresholdSeconds = 600f;

        private readonly NetworkVariable<int> syncedThreatLevel = new(
            (int)ThreatLevel.Low,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Coroutine timerCoroutine;
        private float sessionStartTime;
        private float stoppedElapsedSeconds;
        private bool isRunning;
        private bool isStopped;
        private ThreatLevel localThreatLevel = ThreatLevel.Low;

        public ThreatLevel CurrentThreat => IsNetworkActive()
            ? IntToThreatLevel(syncedThreatLevel.Value)
            : localThreatLevel;

        public float ElapsedSeconds
        {
            get
            {
                if (!isRunning)
                {
                    return 0f;
                }

                if (isStopped)
                {
                    return stoppedElapsedSeconds;
                }

                return Mathf.Max(0f, Time.time - sessionStartTime);
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
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            syncedThreatLevel.OnValueChanged += HandleSyncedThreatLevelChanged;
        }

        public override void OnNetworkDespawn()
        {
            syncedThreatLevel.OnValueChanged -= HandleSyncedThreatLevelChanged;
            StopThreatTimer();
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void StartThreatTimer()
        {
            if (!CoopGuard.IsServerOrOffline())
            {
                return;
            }

            StopThreatTimer();

            isRunning = true;
            isStopped = false;
            stoppedElapsedSeconds = 0f;
            sessionStartTime = Time.time;

            ThreatTimerRestarted?.Invoke();
            SetThreatLevel(ThreatLevel.Low);

            timerCoroutine = StartCoroutine(RunThreatTimer());
        }

        public void StopThreatTimer()
        {
            if (timerCoroutine != null)
            {
                StopCoroutine(timerCoroutine);
                timerCoroutine = null;
            }

            isRunning = false;
            isStopped = false;
            stoppedElapsedSeconds = 0f;
        }

        private IEnumerator RunThreatTimer()
        {
            float mild = GetSafeThresholdSeconds(mildThresholdSeconds, 0f);
            float moderate = GetSafeThresholdSeconds(moderateThresholdSeconds, mild);
            float high = GetSafeThresholdSeconds(highThresholdSeconds, moderate);
            float extreme = GetSafeThresholdSeconds(extremeThresholdSeconds, high);

            yield return WaitUntilElapsed(mild);
            if (!isRunning) yield break;
            SetThreatLevel(ThreatLevel.Mild);

            yield return WaitUntilElapsed(moderate);
            if (!isRunning) yield break;
            SetThreatLevel(ThreatLevel.Moderate);

            yield return WaitUntilElapsed(high);
            if (!isRunning) yield break;
            SetThreatLevel(ThreatLevel.High);

            yield return WaitUntilElapsed(extreme);
            if (!isRunning) yield break;
            SetThreatLevel(ThreatLevel.Extreme);

            isStopped = true;
            stoppedElapsedSeconds = extreme;
            timerCoroutine = null;
        }

        private YieldInstruction WaitUntilElapsed(float thresholdSeconds)
        {
            float wait = thresholdSeconds - ElapsedSeconds;
            if (wait <= 0f)
            {
                return null;
            }

            return new WaitForSeconds(wait);
        }

        private void SetThreatLevel(ThreatLevel level)
        {
            if (IsNetworkActive())
            {
                NetworkManager nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsServer)
                {
                    return;
                }

                syncedThreatLevel.Value = (int)level;
                ThreatLevelUpdated?.Invoke(level);
            }
            else
            {
                localThreatLevel = level;
                ThreatLevelUpdated?.Invoke(level);
            }
        }

        private static float GetSafeThresholdSeconds(float value, float min)
        {
            return Mathf.Max(min, value);
        }

        private static ThreatLevel IntToThreatLevel(int value)
        {
            if (value < (int)ThreatLevel.Low) return ThreatLevel.Low;
            if (value > (int)ThreatLevel.Extreme) return ThreatLevel.Extreme;
            return (ThreatLevel)value;
        }

        private void HandleSyncedThreatLevelChanged(int previous, int current)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer)
            {
                return;
            }

            ThreatLevelUpdated?.Invoke(IntToThreatLevel(current));
        }

        private static bool IsNetworkActive()
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening;
        }
    }
}
