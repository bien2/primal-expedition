using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WalaPaNameHehe
{
    public class DayNightTimer : MonoBehaviour
    {
        [System.Serializable]
        public struct DayNightChance
        {
            [Min(1)] public int day;
            [Range(0f, 1f)] public float nightChance;
        }

        [Header("Day System")]
        [SerializeField] private int startingDay = 1;
        [SerializeField] private DayNightChance[] dayNightChances =
        {
            new DayNightChance { day = 1, nightChance = 0f },
            new DayNightChance { day = 2, nightChance = 0.2f },
            new DayNightChance { day = 3, nightChance = 0.3f },
            new DayNightChance { day = 4, nightChance = 0.45f },
            new DayNightChance { day = 5, nightChance = 1f }
        };

        [Header("Lighting")]
        [SerializeField] private Light directionalLight;
        [SerializeField] private float dayDirectionalIntensity = 1.0f;
        [SerializeField] private float nightDirectionalIntensity = 0.15f;
        [SerializeField] private Color dayAmbientColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        [SerializeField] private Color nightAmbientColor = new Color(0.08f, 0.1f, 0.15f, 1f);

        [Header("Skybox")]
        [SerializeField] private Material daySkybox;
        [SerializeField] private Material nightSkybox;
        [SerializeField] private bool updateEnvironmentLighting = true;

        [Header("Fog")]
        [SerializeField] private bool controlFog = true;
        [SerializeField] private Color nightFogColor = new Color(0.02f, 0.03f, 0.05f, 1f);
        [SerializeField] private float nightFogDensity = 0.005f;

        private bool hasSwitchedToNight;
        private int currentDay;
        private Coroutine transitionRoutine;
        private bool networkHandlersRegistered;
        private bool useSceneDaySkyboxFallback;
        private bool clientConnectedHandlerRegistered;
        private NetworkManager cachedNetworkManager;
        private Color cachedDayFogColor;
        private float cachedDayFogDensity;
        private const string DayNightStateMessageName = "WalaPaNameHehe.DayNightState";

        public bool IsNight => hasSwitchedToNight;
        public int CurrentDay => currentDay;

        private void Awake()
        {
            useSceneDaySkyboxFallback = daySkybox == null;
        }

        private void Start()
        {
            CacheSceneDefaults();
            TryRegisterNetworkHandlers();

            NetworkManager nm = NetworkManager.Singleton;
            bool networkActive = nm != null && nm.IsListening;
            if (networkActive)
            {
                if (nm.IsServer)
                {
                    BeginDay(startingDay, broadcast: true);
                }
                else
                {
                    currentDay = Mathf.Max(1, startingDay);
                    hasSwitchedToNight = false;
                    ResetToDay();
                }

                return;
            }

            BeginDay(startingDay, broadcast: false);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            UnregisterNetworkHandlers();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Update()
        {
            if (!networkHandlersRegistered)
            {
                TryRegisterNetworkHandlers();
            }
        }

        public void SetDay(int day)
        {
            BeginDay(day, broadcast: true);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CacheSceneDefaults();

            if (hasSwitchedToNight)
            {
                ApplyNightInstant();
            }
            else
            {
                ResetToDay();
            }
        }

        private void CacheSceneDefaults()
        {
            if (directionalLight == null)
            {
                directionalLight = RenderSettings.sun;
            }

            if (directionalLight != null)
            {
                dayDirectionalIntensity = directionalLight.intensity;
            }

            dayAmbientColor = RenderSettings.ambientLight;

            if (useSceneDaySkyboxFallback)
            {
                daySkybox = RenderSettings.skybox;
            }

            if (controlFog)
            {
                cachedDayFogColor = RenderSettings.fogColor;
                cachedDayFogDensity = RenderSettings.fogDensity;
            }
        }

        private void BeginDay(int day, bool broadcast)
        {
            currentDay = Mathf.Max(1, day);
            hasSwitchedToNight = false;
            ResetToDay();

            NetworkManager nm = NetworkManager.Singleton;
            bool canRollNight = nm == null || !nm.IsListening || nm.IsServer;
            if (canRollNight && RollNightChance(currentDay))
            {
                hasSwitchedToNight = true;
                ApplyNightInstant();
            }

            if (broadcast)
            {
                BroadcastDayState();
            }
        }

        private bool RollNightChance(int day)
        {
            float chance = GetNightChanceForDay(day);
            return Random.value <= Mathf.Clamp01(chance);
        }

        private float GetNightChanceForDay(int day)
        {
            if (dayNightChances == null || dayNightChances.Length == 0)
            {
                return 0f;
            }

            float bestChance = 0f;
            int bestDay = -1;
            for (int i = 0; i < dayNightChances.Length; i++)
            {
                DayNightChance entry = dayNightChances[i];
                if (entry.day == day)
                {
                    return entry.nightChance;
                }

                if (entry.day <= day && entry.day > bestDay)
                {
                    bestDay = entry.day;
                    bestChance = entry.nightChance;
                }
            }

            return bestChance;
        }

        private void ResetToDay()
        {
            if (directionalLight != null)
            {
                directionalLight.intensity = dayDirectionalIntensity;
            }

            RenderSettings.ambientLight = dayAmbientColor;

            if (daySkybox != null)
            {
                RenderSettings.skybox = daySkybox;
                if (updateEnvironmentLighting)
                {
                    DynamicGI.UpdateEnvironment();
                }
            }

            if (controlFog)
            {
                RenderSettings.fogColor = cachedDayFogColor;
                RenderSettings.fogDensity = cachedDayFogDensity;
            }
        }

        private void ApplyNightInstant()
        {
            if (nightSkybox != null)
            {
                RenderSettings.skybox = nightSkybox;
                if (updateEnvironmentLighting)
                {
                    DynamicGI.UpdateEnvironment();
                }
            }

            if (directionalLight != null)
            {
                directionalLight.intensity = nightDirectionalIntensity;
            }

            RenderSettings.ambientLight = nightAmbientColor;
            if (controlFog)
            {
                RenderSettings.fogColor = nightFogColor;
                RenderSettings.fogDensity = nightFogDensity;
            }
        }

        private void TryRegisterNetworkHandlers()
        {
            if (networkHandlersRegistered)
            {
                return;
            }

            cachedNetworkManager = NetworkManager.Singleton;
            if (cachedNetworkManager == null || cachedNetworkManager.CustomMessagingManager == null)
            {
                return;
            }

            cachedNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                DayNightStateMessageName,
                (senderId, payload) =>
                {
                    if (!payload.TryBeginRead(sizeof(int) + sizeof(byte)))
                    {
                        return;
                    }

                    payload.ReadValueSafe(out int day);
                    payload.ReadValueSafe(out byte nightFlag);

                    currentDay = Mathf.Max(1, day);
                    hasSwitchedToNight = nightFlag != 0;

                    if (hasSwitchedToNight)
                    {
                        ApplyNightInstant();
                    }
                    else
                    {
                        ResetToDay();
                    }
                });

            networkHandlersRegistered = true;

            if (cachedNetworkManager.IsListening && cachedNetworkManager.IsServer)
            {
                if (!clientConnectedHandlerRegistered)
                {
                    cachedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
                    clientConnectedHandlerRegistered = true;
                }
            }
        }

        private void UnregisterNetworkHandlers()
        {
            if (!networkHandlersRegistered)
            {
                return;
            }

            NetworkManager nm = cachedNetworkManager != null ? cachedNetworkManager : NetworkManager.Singleton;
            if (nm == null)
            {
                networkHandlersRegistered = false;
                return;
            }

            if (nm.CustomMessagingManager != null)
            {
                nm.CustomMessagingManager.UnregisterNamedMessageHandler(DayNightStateMessageName);
            }

            if (clientConnectedHandlerRegistered)
            {
                nm.OnClientConnectedCallback -= HandleClientConnected;
                clientConnectedHandlerRegistered = false;
            }

            networkHandlersRegistered = false;
            cachedNetworkManager = null;
        }

        private void HandleClientConnected(ulong clientId)
        {
            NetworkManager nm = cachedNetworkManager != null ? cachedNetworkManager : NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer || nm.CustomMessagingManager == null)
            {
                return;
            }

            SendStateToClient(nm, clientId);
        }

        private void BroadcastDayState()
        {
            NetworkManager nm = cachedNetworkManager != null ? cachedNetworkManager : NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer || nm.CustomMessagingManager == null)
            {
                return;
            }

            using FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(byte), Allocator.Temp);
            writer.WriteValueSafe(currentDay);
            writer.WriteValueSafe((byte)(hasSwitchedToNight ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessageToAll(DayNightStateMessageName, writer);
        }

        private void SendStateToClient(NetworkManager nm, ulong clientId)
        {
            if (nm == null || !nm.IsListening || !nm.IsServer || nm.CustomMessagingManager == null)
            {
                return;
            }

            using FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(byte), Allocator.Temp);
            writer.WriteValueSafe(currentDay);
            writer.WriteValueSafe((byte)(hasSwitchedToNight ? 1 : 0));
            nm.CustomMessagingManager.SendNamedMessage(DayNightStateMessageName, clientId, writer);
        }
    }
}
