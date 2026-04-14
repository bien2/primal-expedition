using System.Collections.Generic;
using UnityEngine;
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
        private Camera[] localCameras;
        private AudioListener[] localListeners;
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

            UpdateSpectateCameraTransform();
        }

        private void EndSpectate()
        {
            isSpectating = false;
            SetSpectateTarget(null);
            EnableLocalView();
            SetBlackoutEnabled(true);
            SetSpectateCameraActive(false);
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
                SetSpectateCameraActive(false);
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

            localCameras = GetComponentsInChildren<Camera>(true);
            localListeners = GetComponentsInChildren<AudioListener>(true);

            for (int i = 0; i < localCameras.Length; i++)
            {
                localCameras[i].enabled = false;
            }

            for (int i = 0; i < localListeners.Length; i++)
            {
                localListeners[i].enabled = false;
            }

            localViewDisabled = true;
        }

        private void EnableLocalView()
        {
            if (!localViewDisabled)
            {
                return;
            }

            if (localCameras != null)
            {
                for (int i = 0; i < localCameras.Length; i++)
                {
                    if (localCameras[i] != null)
                    {
                        localCameras[i].enabled = true;
                    }
                }
            }

            if (localListeners != null)
            {
                for (int i = 0; i < localListeners.Length; i++)
                {
                    if (localListeners[i] != null)
                    {
                        localListeners[i].enabled = true;
                    }
                }
            }

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
            if (spectateCamera != null)
            {
                return;
            }

            GameObject cameraObject = new GameObject("SpectateCamera");
            cameraObject.transform.SetParent(transform, false);
            spectateCamera = cameraObject.AddComponent<Camera>();
            spectateCamera.enabled = false;
            spectateListener = cameraObject.AddComponent<AudioListener>();
            spectateListener.enabled = false;
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

        private void ApplyTargetCameraSettings()
        {
            if (spectateCamera == null)
            {
                return;
            }

            Camera targetCam = FindFirstCamera(currentTarget);
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

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float posT = 1f - Mathf.Exp(-Mathf.Max(0.01f, followPositionSmoothing) * dt);
            float rotT = 1f - Mathf.Exp(-Mathf.Max(0.01f, followRotationSmoothing) * dt);

            Transform camTransform = spectateCamera.transform;
            camTransform.position = Vector3.Lerp(camTransform.position, anchor.position, posT);
            camTransform.rotation = Quaternion.Slerp(camTransform.rotation, anchor.rotation, rotT);
        }

        private static Transform ResolveSpectateAnchor(PlayerMovement target)
        {
            if (target == null)
            {
                return null;
            }

            if (target.NetworkPitchPivot != null)
            {
                return target.NetworkPitchPivot;
            }

            if (target.CameraPivot != null)
            {
                return target.CameraPivot;
            }

            Camera cam = FindFirstCamera(target);
            if (cam != null)
            {
                return cam.transform;
            }

            return target.transform;
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
