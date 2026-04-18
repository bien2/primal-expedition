using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WalaPaNameHehe.Multiplayer
{
    [DisallowMultipleComponent]
    public class SpectateController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerMovement player;
        [SerializeField] private bool autoFindPlayer = true;
        [SerializeField] private PlayerDeathBlackout blackout;

        [Header("Input")]
#if ENABLE_INPUT_SYSTEM
        [SerializeField] private Key cycleKey = Key.Tab;
#else
        [SerializeField] private KeyCode cycleKey = KeyCode.Tab;
#endif

        [Header("Settings")]
        [SerializeField] private float refreshInterval = 0.5f;
        [SerializeField] private float followPositionSmoothing = 18f;
        [SerializeField] private float followRotationSmoothing = 24f;
        [SerializeField] private float spectateDelayAfterDeath = 3f;

        private readonly List<PlayerMovement> targets = new();
        private int targetIndex = -1;
        private float nextRefreshTime;
        private bool isSpectating;
        private PlayerMovement currentTarget;
        private Camera currentTargetCamera;
        private AudioListener currentTargetListener;
        private Camera spectateCamera;
        private AudioListener spectateListener;
        private Transform spectateCameraOriginalParent;
        private Vector3 spectateCameraOriginalLocalPosition;
        private Quaternion spectateCameraOriginalLocalRotation;
        private bool hasSpectateCameraOriginalTransform;
        private float spectateCameraOriginalFieldOfView;
        private float spectateCameraOriginalNearClip;
        private float spectateCameraOriginalFarClip;
        private CameraClearFlags spectateCameraOriginalClearFlags;
        private Color spectateCameraOriginalBackgroundColor;
        private int spectateCameraOriginalCullingMask;
        private float spectateCameraOriginalDepth;
        private bool hasSpectateCameraOriginalSettings;
        private readonly Dictionary<Camera, bool> localCameraStates = new();
        private readonly Dictionary<AudioListener, bool> localListenerStates = new();
        private bool localViewDisabled;
        private readonly List<Renderer> hiddenRenderers = new();
        private readonly Dictionary<Renderer, bool> hiddenRendererStates = new();
        private bool wasDead;
        private float deathStartTime;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<PlayerMovement>();
                if (player == null)
                {
                    player = GetComponentInChildren<PlayerMovement>(true);
                }
            }

            if (blackout == null)
            {
                blackout = GetComponent<PlayerDeathBlackout>();
                if (blackout == null)
                {
                    blackout = GetComponentInChildren<PlayerDeathBlackout>(true);
                }
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ResetSpectateState();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResetSpectateState();
        }

        private void Update()
        {
            if (player == null && autoFindPlayer)
            {
                player = FindLocalPlayer();
            }

            if (player == null || !IsLocalOwner(player))
            {
                return;
            }

            if (player.IsDead)
            {
                if (!wasDead)
                {
                    wasDead = true;
                    deathStartTime = Time.unscaledTime;
                }

                float delay = Mathf.Max(0f, spectateDelayAfterDeath);
                if (Time.unscaledTime - deathStartTime < delay)
                {
                    return;
                }

                if (!isSpectating)
                {
                    BeginSpectate();
                }
                else
                {
                    UpdateSpectate();
                }
                return;
            }

            if (isSpectating)
            {
                EndSpectate();
            }

            wasDead = false;
        }

        private void BeginSpectate()
        {
            isSpectating = true;
            targetIndex = -1;
            nextRefreshTime = 0f;
            RefreshTargets(true);
            SetBlackoutEnabled(false);
            EnsureSpectateCamera();
            SetSpectateCameraActive(true);
            SelectNextTarget();
        }

        private void UpdateSpectate()
        {
            if (Time.time >= nextRefreshTime)
            {
                RefreshTargets(false);
            }

            if (currentTarget == null || currentTarget.IsDead)
            {
                SelectNextTarget();
            }

            if (IsCyclePressed())
            {
                SelectNextTarget();
            }

            ApplyTargetCameraSettings();
            UpdateSpectateCameraTransform();
        }

        private void EndSpectate()
        {
            isSpectating = false;
            SetSpectateTarget(null);
            RestoreSpectateCameraTransform();
            EnableLocalView();
            SetBlackoutEnabled(true);
        }

        private void ResetSpectateState()
        {
            if (isSpectating)
            {
                EndSpectate();
            }

            RestoreSpectateCameraTransform();
            spectateCamera = null;
            spectateListener = null;
            hasSpectateCameraOriginalTransform = false;
            hasSpectateCameraOriginalSettings = false;
            spectateCameraOriginalParent = null;
            localCameraStates.Clear();
            localListenerStates.Clear();
            localViewDisabled = false;
            wasDead = false;
        }

        private void RefreshTargets(bool forceResetIndex)
        {
            nextRefreshTime = Time.time + Mathf.Max(0.05f, refreshInterval);
            targets.Clear();

            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                foreach (var kvp in nm.ConnectedClients)
                {
                    var client = kvp.Value;
                    if (client == null || client.PlayerObject == null)
                    {
                        continue;
                    }

                    PlayerMovement candidate = client.PlayerObject.GetComponentInChildren<PlayerMovement>(true);
                    if (candidate == null || candidate == player || candidate.IsDead)
                    {
                        continue;
                    }

                    targets.Add(candidate);
                }
            }
            else
            {
                PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < players.Length; i++)
                {
                    PlayerMovement candidate = players[i];
                    if (candidate == null || candidate == player)
                    {
                        continue;
                    }

                    if (candidate.IsDead)
                    {
                        continue;
                    }

                    targets.Add(candidate);
                }
            }

            if (forceResetIndex || targetIndex >= targets.Count)
            {
                targetIndex = -1;
            }
        }

        private void SelectNextTarget()
        {
            if (targets.Count == 0)
            {
                SetSpectateTarget(null);
                SetBlackoutEnabled(true);
                EnableLocalView();
                return;
            }

            DisableLocalView();
            SetSpectateCameraActive(true);
            targetIndex = (targetIndex + 1) % targets.Count;
            SetSpectateTarget(targets[targetIndex]);
            SetBlackoutEnabled(false);
        }

        private void SetSpectateTarget(PlayerMovement target)
        {
            if (currentTarget == target)
            {
                return;
            }

            DisableTargetView();
            RestoreTargetRenderers();

            currentTarget = target;
            if (currentTarget == null)
            {
                return;
            }

            HideTargetRenderers(currentTarget);

            ApplyTargetCameraSettings();
            UpdateSpectateCameraTransform();
        }

        private void DisableTargetView()
        {
            if (currentTargetCamera != null)
            {
                currentTargetCamera.enabled = false;
            }
            if (currentTargetListener != null)
            {
                currentTargetListener.enabled = false;
            }

            currentTargetCamera = null;
            currentTargetListener = null;
            currentTarget = null;
        }

        private void HideTargetRenderers(PlayerMovement target)
        {
            hiddenRenderers.Clear();
            hiddenRendererStates.Clear();

            if (target == null)
            {
                return;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                hiddenRenderers.Add(renderer);
                hiddenRendererStates[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
        }

        private void RestoreTargetRenderers()
        {
            if (hiddenRenderers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < hiddenRenderers.Count; i++)
            {
                Renderer renderer = hiddenRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (hiddenRendererStates.TryGetValue(renderer, out bool previous))
                {
                    renderer.enabled = previous;
                }
                else
                {
                    renderer.enabled = true;
                }
            }

            hiddenRenderers.Clear();
            hiddenRendererStates.Clear();
        }

        private void DisableLocalView()
        {
            if (localViewDisabled)
            {
                return;
            }

            localCameraStates.Clear();
            localListenerStates.Clear();

            Camera[] localCameras = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < localCameras.Length; i++)
            {
                Camera cam = localCameras[i];
                if (cam == null)
                {
                    continue;
                }

                localCameraStates[cam] = cam.enabled;
                if (cam != spectateCamera)
                {
                    cam.enabled = false;
                }
            }

            AudioListener[] localListeners = GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < localListeners.Length; i++)
            {
                AudioListener listener = localListeners[i];
                if (listener == null)
                {
                    continue;
                }

                localListenerStates[listener] = listener.enabled;
                if (listener != spectateListener)
                {
                    listener.enabled = false;
                }
            }

            localViewDisabled = true;
        }

        private void EnableLocalView()
        {
            if (!localViewDisabled)
            {
                return;
            }

            if (localCameraStates.Count > 0)
            {
                foreach (var kvp in localCameraStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = kvp.Value;
                    }
                }
            }

            if (localListenerStates.Count > 0)
            {
                foreach (var kvp in localListenerStates)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.enabled = kvp.Value;
                    }
                }
            }

            localCameraStates.Clear();
            localListenerStates.Clear();
            localViewDisabled = false;
        }

        private void SetBlackoutEnabled(bool enabled)
        {
            if (blackout != null)
            {
                blackout.enabled = enabled;
            }
        }

        private void EnsureSpectateCamera()
        {
            if (player == null)
            {
                return;
            }

            if (spectateCamera == null)
            {
                spectateCamera = FindFirstCamera(player);
            }
            if (spectateCamera == null)
            {
                return;
            }

            spectateListener = spectateCamera.GetComponent<AudioListener>();

            if (!hasSpectateCameraOriginalTransform)
            {
                spectateCameraOriginalParent = spectateCamera.transform.parent;
                spectateCameraOriginalLocalPosition = spectateCamera.transform.localPosition;
                spectateCameraOriginalLocalRotation = spectateCamera.transform.localRotation;
                hasSpectateCameraOriginalTransform = true;
            }

            if (!hasSpectateCameraOriginalSettings)
            {
                spectateCameraOriginalFieldOfView = spectateCamera.fieldOfView;
                spectateCameraOriginalNearClip = spectateCamera.nearClipPlane;
                spectateCameraOriginalFarClip = spectateCamera.farClipPlane;
                spectateCameraOriginalClearFlags = spectateCamera.clearFlags;
                spectateCameraOriginalBackgroundColor = spectateCamera.backgroundColor;
                spectateCameraOriginalCullingMask = spectateCamera.cullingMask;
                spectateCameraOriginalDepth = spectateCamera.depth;
                hasSpectateCameraOriginalSettings = true;
            }

            spectateCamera.transform.SetParent(null, true);
        }

        private void SetSpectateCameraActive(bool active)
        {
            if (spectateCamera != null)
            {
                spectateCamera.enabled = active;
            }

            if (spectateListener != null)
            {
                spectateListener.enabled = active;
            }
        }

        private void RestoreSpectateCameraTransform()
        {
            if (!hasSpectateCameraOriginalTransform || spectateCamera == null)
            {
                return;
            }

            spectateCamera.transform.SetParent(spectateCameraOriginalParent, false);
            spectateCamera.transform.localPosition = spectateCameraOriginalLocalPosition;
            spectateCamera.transform.localRotation = spectateCameraOriginalLocalRotation;

            if (hasSpectateCameraOriginalSettings)
            {
                spectateCamera.fieldOfView = spectateCameraOriginalFieldOfView;
                spectateCamera.nearClipPlane = spectateCameraOriginalNearClip;
                spectateCamera.farClipPlane = spectateCameraOriginalFarClip;
                spectateCamera.clearFlags = spectateCameraOriginalClearFlags;
                spectateCamera.backgroundColor = spectateCameraOriginalBackgroundColor;
                spectateCamera.cullingMask = spectateCameraOriginalCullingMask;
                spectateCamera.depth = spectateCameraOriginalDepth;
            }
        }

        private void ApplyTargetCameraSettings()
        {
            if (spectateCamera == null)
            {
                return;
            }

            Camera targetCam = ResolveTargetCamera(currentTarget);
            if (targetCam == null)
            {
                return;
            }

            currentTargetCamera = targetCam;
            spectateCamera.fieldOfView = targetCam.fieldOfView;
            spectateCamera.nearClipPlane = targetCam.nearClipPlane;
            spectateCamera.farClipPlane = targetCam.farClipPlane;
            spectateCamera.clearFlags = targetCam.clearFlags;
            spectateCamera.backgroundColor = targetCam.backgroundColor;
            spectateCamera.cullingMask = targetCam.cullingMask;
            spectateCamera.depth = targetCam.depth;
        }

        private void UpdateSpectateCameraTransform()
        {
            if (!isSpectating || spectateCamera == null || currentTarget == null)
            {
                return;
            }

            Transform anchor = ResolveSpectateAnchor(currentTarget);
            if (anchor == null)
            {
                return;
            }

            Transform camTransform = spectateCamera.transform;
            camTransform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        private static Transform ResolveSpectateAnchor(PlayerMovement target)
        {
            if (target == null)
            {
                return null;
            }

            PlayerMovement.PovMode mode = target.CurrentPovMode;
            if (mode == PlayerMovement.PovMode.External)
            {
                Camera cam = TryResolveExternalKillCamera(target);
                if (cam != null)
                {
                    return cam.transform;
                }
            }
            else if (mode == PlayerMovement.PovMode.Ragdoll)
            {
                if (target.TryGetRagdollPovCamera(out Camera ragdollCam))
                {
                    return ragdollCam.transform;
                }
            }
            else
            {
                if (target.TryGetMainPovCamera(out Camera mainCam))
                {
                    return mainCam.transform;
                }
            }

            if (target.NetworkPitchPivot != null)
            {
                return target.NetworkPitchPivot;
            }

            if (target.CameraPivot != null)
            {
                return target.CameraPivot;
            }

            return target.transform;
        }

        private static Camera ResolveTargetCamera(PlayerMovement target)
        {
            if (target == null)
            {
                return null;
            }

            PlayerMovement.PovMode mode = target.CurrentPovMode;
            if (mode == PlayerMovement.PovMode.External)
            {
                Camera cam = TryResolveExternalKillCamera(target);
                return cam != null ? cam : FindFirstCamera(target);
            }

            if (mode == PlayerMovement.PovMode.Ragdoll)
            {
                if (target.TryGetRagdollPovCamera(out Camera ragdollCam))
                {
                    return ragdollCam;
                }

                return FindFirstCamera(target);
            }

            if (target.TryGetMainPovCamera(out Camera mainCam))
            {
                return mainCam;
            }

            return FindFirstCamera(target);
        }

        private static Camera TryResolveExternalKillCamera(PlayerMovement target)
        {
            if (target == null)
            {
                return null;
            }

            ulong attackerId = target.ExternalAttackerNetworkObjectId;
            if (attackerId == 0)
            {
                return null;
            }

            Unity.Netcode.NetworkManager nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null)
            {
                return null;
            }

            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out Unity.Netcode.NetworkObject netObj) || netObj == null)
            {
                return null;
            }

            DinoKillCamera killCam = netObj.GetComponentInChildren<DinoKillCamera>(true);
            return killCam != null ? killCam.KillCamera : null;
        }

        private static Camera FindFirstCamera(PlayerMovement target)
        {
            if (target == null)
            {
                return null;
            }

            Camera[] cams = target.GetComponentsInChildren<Camera>(true);
            if (cams == null || cams.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null)
                {
                    return cams[i];
                }
            }

            return null;
        }

        private static bool IsLocalOwner(PlayerMovement movement)
        {
            return movement != null && CoopGuard.IsLocalOwnerOrOffline(movement);
        }

        private static PlayerMovement FindLocalPlayer()
        {
            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerMovement candidate = players[i];
                if (candidate != null && candidate.IsOwner)
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool IsCyclePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
            {
                return false;
            }

            return Keyboard.current[cycleKey].wasPressedThisFrame;
#else
            return Input.GetKeyDown(cycleKey);
#endif
        }
    }
}
