using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using WalaPaNameHehe.Multiplayer;
using System.Collections.Generic;

namespace WalaPaNameHehe
{
    public class WeaponDrone : NetworkBehaviour
    {
        [Header("Summon")]
        [SerializeField] private Key activationKey = Key.E;
        [SerializeField] private float holdDurationToSummon = 3f;
        [SerializeField] private GameObject dronePrefab;
        [SerializeField] private bool requireSpawnerEquipped = true;
        [SerializeField] private GameObject requiredSpawnerPrefab;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.8f, 1.5f);

        [Header("Drone Runtime")]
        [SerializeField] private float droneLifetime = 10f;
        [SerializeField] private float droneMoveSpeed = 8f;
        [SerializeField] private float droneVerticalSpeed = 6f;
        [SerializeField] private float droneLookSensitivity = 120f;
        [SerializeField] private float minDronePitch = -55f;
        [SerializeField] private float maxDronePitch = 55f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private bool allowLocalFallbackIfNetworkSpawnUnavailable = true;
        [SerializeField] private float returnToPlayerSpeed = 10f;
        [SerializeField] private Vector3 returnToPlayerOffset = new Vector3(0f, 1.2f, 0f);

        [Header("Drone Weapon")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private float projectileSpeed = 35f;
        [SerializeField] private float projectileLifetime = 3f;
        [SerializeField] private float fireCooldown = 0.15f;
        [SerializeField] private Vector3 projectileRotationOffsetEuler = Vector3.zero;

        [Header("Audio")]
        [SerializeField] private AudioSource summonAudioSource;
        [SerializeField] private AudioSource droneSfxAudioSource;
        [SerializeField, Min(1)] private int maxConcurrentDroneSfx = 8;
        [SerializeField] private AudioClip summonLoopClip;
        [SerializeField] [Range(0f, 1f)] private float summonLoopVolume = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float droneActiveLoopVolume = 0.75f;
        [SerializeField] private AudioClip droneShotClip;
        [SerializeField] [Range(0f, 1f)] private float droneShotVolume = 0.8f;

        [Header("UI")]
        [SerializeField] private bool showUi = true;

        private float holdTimer;
        private bool isUsingDrone;
        private float droneTimer;
        private ulong activeDroneNetworkId;
        private GameObject activeDrone;
        private Camera activeDroneCamera;
        private AudioListener activeDroneListener;
        private Rigidbody activeDroneBody;
        private AudioListener playerListener;
        private float droneYaw;
        private float dronePitch;
        private NetworkPlayerOwnership ownershipState;
        private bool renderersOverriddenForDrone;
        private PlayerMovement playerMovement;
        private PlayerExtractor playerExtractor;
        private InventorySystem inventorySystem;
        private Rigidbody playerBody;
        private bool playerBodyLockedForDrone;
        private bool cachedPlayerBodyUseGravity;
        private bool cachedPlayerBodyIsKinematic;
        private RigidbodyConstraints cachedPlayerBodyConstraints;
        private float nextFireTime;
        private float nextDroneHumSoundTime;
        private bool hasFiredThisSummon;
        private bool isDroneActivationPending;
        private bool isReturningDrone;
        private Texture2D uiPixel;
        private readonly List<AudioSource> droneSfxVoices = new List<AudioSource>();
        private const float droneHumEmitInterval = 0.5f;

        public bool IsSummoningDrone => !isUsingDrone && holdTimer > 0f && holdDurationToSummon > 0f;
        public bool IsUsingDrone => isUsingDrone;

        private void Start()
        {
            ResolvePlayerCameraReference();

            ownershipState = GetComponent<NetworkPlayerOwnership>();
            playerMovement = GetComponent<PlayerMovement>();
            playerExtractor = GetComponent<PlayerExtractor>();
            inventorySystem = GetComponent<InventorySystem>();
            playerBody = GetComponent<Rigidbody>();

            if (uiPixel == null)
            {
                uiPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                uiPixel.SetPixel(0, 0, Color.white);
                uiPixel.Apply();
            }

            if (summonAudioSource == null)
            {
                summonAudioSource = GetComponent<AudioSource>();
            }

            if (droneSfxAudioSource == null)
            {
                droneSfxAudioSource = gameObject.AddComponent<AudioSource>();
                droneSfxAudioSource.playOnAwake = false;
                droneSfxAudioSource.loop = false;
                droneSfxAudioSource.spatialBlend = 0f;
            }

            EnsureDroneSfxVoicePool();
        }

        private void Update()
        {
            if (!CoopGuard.IsLocalOwnerOrOffline(this))
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (playerMovement != null && playerMovement.IsInteractionLocked)
            {
                ForceRecallDueToGrab();
                return;
            }

            if (!isUsingDrone)
            {
                UpdateSummonInput();
                return;
            }

            UpdateDroneControl();
        }

        private void OnDisable()
        {
            StopSummonLoopSound();
            StopDroneActiveLoopSound(activeDrone);
            ForceRestorePlayerPov();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            StopSummonLoopSound();
            StopDroneActiveLoopSound(activeDrone);
            ForceRestorePlayerPov();

            if (uiPixel != null)
            {
                Destroy(uiPixel);
                uiPixel = null;
            }
        }

        private void ResolvePlayerCameraReference()
        {
            if (playerCamera != null)
            {
                if (playerListener == null)
                {
                    playerListener = playerCamera.GetComponent<AudioListener>();
                }
                return;
            }

            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate == null)
                {
                    continue;
                }

                playerCamera = candidate;
                playerListener = playerCamera.GetComponent<AudioListener>();
                return;
            }
        }

        private void UpdateSummonInput()
        {
            if (isDroneActivationPending)
            {
                holdTimer = 0f;
                StopSummonLoopSound();
                return;
            }

            if (playerMovement != null && playerMovement.IsInteractionLocked)
            {
                holdTimer = 0f;
                StopSummonLoopSound();
                return;
            }

            if (dronePrefab == null)
            {
                holdTimer = 0f;
                StopSummonLoopSound();
                return;
            }

            if (!CanSummonWithCurrentItem())
            {
                holdTimer = 0f;
                StopSummonLoopSound();
                return;
            }

            // Prevent input conflict: extraction and drone summon are both bound to E.
            // If extraction is currently active, drone summon input is fully suppressed.
            if (playerExtractor != null && playerExtractor.IsExtractingInProgress)
            {
                holdTimer = 0f;
                StopSummonLoopSound();
                return;
            }

            bool keyPressed = Keyboard.current[activationKey].isPressed;
            if (!keyPressed)
            {
                holdTimer = 0f;
                StopSummonLoopSound();
                return;
            }

            holdTimer += Time.deltaTime;
            StartSummonLoopSound();
            if (holdTimer < holdDurationToSummon)
            {
                return;
            }

            holdTimer = 0f;
            StopSummonLoopSound();
            TryActivateDrone();
        }

        private void TryActivateDrone()
        {
            if (isUsingDrone || activeDrone != null)
            {
                return;
            }

            isDroneActivationPending = true;
            ForceStopPlayerMotionImmediate();

            Vector3 spawnPos;
            Quaternion spawnRot;
            if (playerCamera != null)
            {
                spawnPos = playerCamera.transform.TransformPoint(spawnOffset);
                Vector3 forward = playerCamera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = transform.forward;
                }

                spawnRot = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
            else
            {
                spawnPos = transform.position + transform.TransformDirection(spawnOffset);
                spawnRot = transform.rotation;
            }

            if (IsNetworkActive())
            {
                NetworkObject prefabNetworkObject = dronePrefab.GetComponent<NetworkObject>();
                if (prefabNetworkObject == null)
                {
                    isDroneActivationPending = false;
                    Debug.LogWarning("WeaponDrone: Drone prefab has no NetworkObject. Add NetworkObject + register prefab in NetworkManager for multiplayer spawn.");
                    // In multiplayer, never spawn owner-only fallback drones.
                    // They are invisible to other players and hide real network setup errors.
                    if (!IsNetworkActive() && allowLocalFallbackIfNetworkSpawnUnavailable)
                    {
                        activeDrone = Instantiate(dronePrefab, spawnPos, spawnRot);
                        BeginDroneControlFromInstance(activeDrone);
                    }
                    return;
                }

                if (IsServer)
                {
                    SpawnDroneOnServer(spawnPos, spawnRot, OwnerClientId);
                }
                else
                {
                    RequestSpawnDroneServerRpc(spawnPos, spawnRot);
                }
            }
            else
            {
                // Offline fallback.
                activeDrone = Instantiate(dronePrefab, spawnPos, spawnRot);
                BeginDroneControlFromInstance(activeDrone);
            }
        }

        private void UpdateDroneControl()
        {
            if (activeDrone == null)
            {
                EndDroneImmediate();
                return;
            }

            ForceStopPlayerMotionImmediate();

            if (isReturningDrone)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                BeginReturnToPlayer();
                return;
            }

            droneTimer -= Time.deltaTime;

            float mouseX = 0f;
            float mouseY = 0f;
            if (Mouse.current != null)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                mouseX = delta.x;
                mouseY = delta.y;
            }

            droneYaw += mouseX * droneLookSensitivity * Time.deltaTime;
            dronePitch -= mouseY * droneLookSensitivity * Time.deltaTime;
            dronePitch = Mathf.Clamp(dronePitch, minDronePitch, maxDronePitch);
            activeDrone.transform.rotation = Quaternion.Euler(dronePitch, droneYaw, 0f);

            TryEmitDroneHumSound();

            float inputX = 0f;
            float inputY = 0f;
            float inputZ = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed) inputX -= 1f;
                if (Keyboard.current.dKey.isPressed) inputX += 1f;
                if (Keyboard.current.wKey.isPressed) inputZ += 1f;
                if (Keyboard.current.sKey.isPressed) inputZ -= 1f;
                if (Keyboard.current.spaceKey.isPressed) inputY += 1f;
                if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed) inputY -= 1f;
            }

            Vector3 moveInput = new Vector3(inputX, 0f, inputZ);
            if (moveInput.sqrMagnitude > 1f)
            {
                moveInput.Normalize();
            }

            Vector3 worldMove = activeDrone.transform.TransformDirection(moveInput);
            Vector3 verticalMove = Vector3.up * (inputY * droneVerticalSpeed * Time.deltaTime);
            Vector3 moveDelta = worldMove * (droneMoveSpeed * Time.deltaTime) + verticalMove;
            MoveDroneWithCollision(moveDelta);
            HandleDroneShootInput();

            if (droneTimer <= 0f)
            {
                BeginReturnToPlayer();
            }
        }

        private void HandleDroneShootInput()
        {
            if (projectilePrefab == null || Mouse.current == null)
            {
                return;
            }

            if (hasFiredThisSummon)
            {
                return;
            }

            if (!Mouse.current.leftButton.wasPressedThisFrame || Time.time < nextFireTime)
            {
                return;
            }

            nextFireTime = Time.time + Mathf.Max(0.01f, fireCooldown);

            Vector3 spawnPos = activeDrone.transform.position;
            Vector3 shootDir = activeDrone.transform.forward;

            if (activeDroneCamera != null)
            {
                spawnPos = activeDroneCamera.transform.position + (activeDroneCamera.transform.forward * 0.35f);
                shootDir = activeDroneCamera.transform.forward;
            }

            shootDir = shootDir.sqrMagnitude > 0.0001f ? shootDir.normalized : activeDrone.transform.forward;
            EmitWorldSound(spawnPos, 1f);
            PlayDroneShotSound(spawnPos);

            if (IsNetworkActive())
            {
                if (IsServer)
                {
                    SpawnProjectileClientRpc(spawnPos, shootDir);
                }
                else
                {
                    RequestFireProjectileServerRpc(spawnPos, shootDir);
                }
            }
            else
            {
                SpawnProjectileLocal(spawnPos, shootDir);
            }

            hasFiredThisSummon = true;
        }

        private void PlayDroneShotSound(Vector3 position)
        {
            if (droneShotClip == null)
            {
                return;
            }

            PlayDroneSfxOneShot(droneShotClip, Mathf.Clamp01(droneShotVolume), position);
        }

        private void TryEmitDroneHumSound()
        {
            if (activeDrone == null)
            {
                return;
            }

            if (Time.time < nextDroneHumSoundTime)
            {
                return;
            }

            nextDroneHumSoundTime = Time.time + Mathf.Max(0.05f, droneHumEmitInterval);
            EmitWorldSound(activeDrone.transform.position, 1f);
        }

        private void BeginReturnToPlayer()
        {
            if (isReturningDrone)
            {
                return;
            }

            isReturningDrone = true;
            isDroneActivationPending = false;
            DisableDroneCollision(activeDrone);
            StopDroneActiveLoopSound(activeDrone);
            holdTimer = 0f;
            droneTimer = 0f;
            hasFiredThisSummon = false;

            if (activeDrone != null)
            {
                NetworkObject activeDroneNetworkObject = activeDrone.GetComponent<NetworkObject>();
                bool isSpawnedNetworkDrone = activeDroneNetworkObject != null && activeDroneNetworkObject.IsSpawned;
                bool shouldDestroyLocalDrone =
                    !IsNetworkActive() ||
                    (!isSpawnedNetworkDrone && (activeDroneNetworkId == 0 || !TryGetSpawnedDroneById(activeDroneNetworkId, out _)));

                if (shouldDestroyLocalDrone)
                {
                    StartCoroutine(ReturnAndDespawnLocal(activeDrone));
                }

                activeDroneCamera = null;
                activeDroneListener = null;
                activeDroneBody = null;
            }

            if (IsNetworkActive())
            {
                if (IsServer)
                {
                    StartReturnOnServer(OwnerClientId);
                }
                else
                {
                    RequestReturnDroneServerRpc();
                }
            }
        }

        private void EndDroneImmediate()
        {
            isReturningDrone = false;
            isDroneActivationPending = false;
            DisableDroneCollision(activeDrone);
            StopDroneActiveLoopSound(activeDrone);
            SetDronePov(false);
            isUsingDrone = false;
            holdTimer = 0f;
            droneTimer = 0f;
            hasFiredThisSummon = false;

            if (activeDrone != null)
            {
                Destroy(activeDrone);
                activeDrone = null;
                activeDroneCamera = null;
                activeDroneListener = null;
                activeDroneBody = null;
            }

            if (IsNetworkActive() && IsServer)
            {
                DespawnDroneOnServerImmediate();
            }
        }

        public void ForceRecallDueToGrab()
        {
            if (!CoopGuard.IsLocalOwnerOrOffline(this))
            {
                return;
            }

            if (activeDrone == null && !isUsingDrone && !playerBodyLockedForDrone)
            {
                return;
            }

            if (activeDrone != null && !isReturningDrone)
            {
                BeginReturnToPlayer();
            }

            if (isUsingDrone || playerBodyLockedForDrone || renderersOverriddenForDrone)
            {
                SetDronePov(false);
                isUsingDrone = false;
            }
        }

        private static void DisableDroneCollision(GameObject droneObject)
        {
            if (droneObject == null)
            {
                return;
            }

            Collider[] colliders = droneObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c != null)
                {
                    c.enabled = false;
                }
            }

            Rigidbody rb = droneObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        private void ForceRestorePlayerPov()
        {
            ResolvePlayerCameraReference();

            if (activeDroneCamera != null)
            {
                activeDroneCamera.enabled = false;
            }
            if (activeDroneListener != null)
            {
                activeDroneListener.enabled = false;
            }

            SetAllPlayerCamerasEnabled(true);
            if (playerListener != null)
            {
                playerListener.enabled = true;
            }
            EnforceSingleAudioListener(playerListener);

            if (playerMovement != null)
            {
                playerMovement.SetOnDroneAnimationState(false);
                playerMovement.enabled = true;
            }

            if (inventorySystem != null)
            {
                inventorySystem.SetUiSuppressed(false);
            }

            UnlockPlayerBodyAfterDrone();

            if (ownershipState != null && renderersOverriddenForDrone)
            {
                ownershipState.ClearOwnerHiddenRenderersForcedVisible();
                renderersOverriddenForDrone = false;
            }
        }

        private bool SetDronePov(bool useDronePov)
        {
            ResolvePlayerCameraReference();

            if (!useDronePov)
            {
                ForceRestorePlayerPov();
                return true;
            }

            if (activeDroneCamera == null && activeDrone != null)
            {
                activeDroneCamera = activeDrone.GetComponentInChildren<Camera>(true);
                if (activeDroneCamera != null)
                {
                    activeDroneListener = activeDroneCamera.GetComponent<AudioListener>();
                }
            }

            if (activeDroneCamera == null)
            {
                Debug.LogWarning("WeaponDrone: drone camera not found on spawned drone instance. Drone POV aborted.");
                ForceRestorePlayerPov();
                return false;
            }

            // Enable drone POV first, then disable player POV to avoid frame gaps.
            activeDroneCamera.enabled = true;
            if (activeDroneListener != null)
            {
                activeDroneListener.enabled = true;
            }

            SetAllPlayerCamerasEnabled(false);
            if (playerListener != null)
            {
                playerListener.enabled = false;
            }

              if (playerMovement != null)
              {
                  playerMovement.SetOnDroneAnimationState(true);
                  playerMovement.enabled = false;
              }

              if (inventorySystem != null)
              {
                  inventorySystem.SetUiSuppressed(true);
              }

              LockPlayerBodyForDrone();
              EnforceSingleAudioListener(activeDroneListener);

            // Drone POV should temporarily show owner-hidden renderers so you can see your own character.
            if (ownershipState != null)
            {
                if (useDronePov)
                {
                    ownershipState.SetOwnerHiddenRenderersForcedVisible(true);
                    renderersOverriddenForDrone = true;
                }
                else if (renderersOverriddenForDrone)
                {
                    ownershipState.ClearOwnerHiddenRenderersForcedVisible();
                    renderersOverriddenForDrone = false;
                }
            }

            return true;
        }

        private void LockPlayerBodyForDrone()
        {
            if (playerBody == null || playerBodyLockedForDrone)
            {
                return;
            }

            cachedPlayerBodyUseGravity = playerBody.useGravity;
            cachedPlayerBodyIsKinematic = playerBody.isKinematic;
            cachedPlayerBodyConstraints = playerBody.constraints;

            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.useGravity = false;
            playerBody.isKinematic = true;
            playerBody.constraints = RigidbodyConstraints.FreezeAll;
            playerBodyLockedForDrone = true;
        }

        private void UnlockPlayerBodyAfterDrone()
        {
            if (playerBody == null || !playerBodyLockedForDrone)
            {
                return;
            }

            playerBody.isKinematic = cachedPlayerBodyIsKinematic;
            playerBody.useGravity = cachedPlayerBodyUseGravity;
            playerBody.constraints = cachedPlayerBodyConstraints;
            if (!playerBody.isKinematic)
            {
                playerBody.linearVelocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }
            playerBodyLockedForDrone = false;
        }

        private void SetAllPlayerCamerasEnabled(bool enabled)
        {
            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null)
                {
                    continue;
                }

                cam.enabled = enabled;
            }
        }

        private void EnforceSingleAudioListener(AudioListener allowed)
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null)
                {
                    continue;
                }

                listener.enabled = listener == allowed && allowed != null;
            }
        }

        private bool IsNetworkActive()
        {
            return NetworkManager != null && NetworkManager.IsListening;
        }

        private void EmitWorldSound(Vector3 position, float radius)
        {
            float clampedRadius = Mathf.Max(0f, radius);
            if (clampedRadius <= 0f)
            {
                return;
            }

            if (IsNetworkActive())
            {
                if (IsServer)
                {
                    WorldSoundStimulus.Emit(position, clampedRadius);
                }
                else
                {
                    RelayWorldSoundServerRpc(position, clampedRadius);
                }

                return;
            }

            WorldSoundStimulus.Emit(position, clampedRadius);
        }

        private bool CanSummonWithCurrentItem()
        {
            if (!requireSpawnerEquipped)
            {
                return true;
            }

            if (inventorySystem == null)
            {
                return false;
            }

            return inventorySystem.IsSelectedItemMatchingPrefab(requiredSpawnerPrefab);
        }

        private bool TryGetSpawnedDroneById(ulong droneNetworkId, out NetworkObject droneNetworkObject)
        {
            droneNetworkObject = null;
            if (droneNetworkId == 0 || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return false;
            }

            return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(droneNetworkId, out droneNetworkObject);
        }

        private void BeginDroneControlFromInstance(GameObject droneInstance)
        {
            if (droneInstance == null)
            {
                isDroneActivationPending = false;
                return;
            }

            isDroneActivationPending = false;
            StopSummonLoopSound();
            ForceStopPlayerMotionImmediate();

            activeDrone = droneInstance;
            NetworkObject droneNetworkObject = activeDrone.GetComponent<NetworkObject>();
            if (droneNetworkObject != null && droneNetworkObject.IsSpawned)
            {
                activeDroneNetworkId = droneNetworkObject.NetworkObjectId;
            }
            droneYaw = activeDrone.transform.eulerAngles.y;
            dronePitch = Mathf.Clamp(activeDrone.transform.eulerAngles.x, minDronePitch, maxDronePitch);

            activeDroneCamera = activeDrone.GetComponentInChildren<Camera>(true);
            if (activeDroneCamera != null)
            {
                activeDroneListener = activeDroneCamera.GetComponent<AudioListener>();
            }
            activeDroneBody = activeDrone.GetComponent<Rigidbody>();

            if (!SetDronePov(true))
            {
                isUsingDrone = false;
                return;
            }

            isUsingDrone = true;
            droneTimer = droneLifetime;
            hasFiredThisSummon = false;
            StartDroneActiveLoopSound(activeDrone);
            ReportDroneUse();
        }

        private void ReportDroneUse()
        {
            if (IsNetworkActive())
            {
                if (IsServer)
                {
                    HunterMeterManager.Instance?.ReportDroneUse(OwnerClientId);
                }
                else
                {
                    ReportDroneUseServerRpc();
                }

                return;
            }

            HunterMeterManager.Instance?.ReportDroneUse(OwnerClientId);
        }

        [ServerRpc]
        private void ReportDroneUseServerRpc(ServerRpcParams rpcParams = default)
        {
            HunterMeterManager.Instance?.ReportDroneUse(rpcParams.Receive.SenderClientId);
        }

        private void ForceStopPlayerMotionImmediate()
        {
            if (playerBody == null)
            {
                return;
            }

            if (playerBody.isKinematic)
            {
                return;
            }

            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }

        [ServerRpc]
        private void RequestFireProjectileServerRpc(Vector3 spawnPos, Vector3 shootDir)
        {
            SpawnProjectileClientRpc(spawnPos, shootDir);
        }

        [ServerRpc]
        private void RelayWorldSoundServerRpc(Vector3 position, float radius)
        {
            float clampedRadius = Mathf.Max(0f, radius);
            if (clampedRadius <= 0f)
            {
                return;
            }

            WorldSoundStimulus.Emit(position, clampedRadius);
        }

        [ClientRpc]
        private void SpawnProjectileClientRpc(Vector3 spawnPos, Vector3 shootDir)
        {
            SpawnProjectileLocal(spawnPos, shootDir);
        }

        private void SpawnProjectileLocal(Vector3 spawnPos, Vector3 shootDir)
        {
            if (projectilePrefab == null)
            {
                return;
            }

            Vector3 dir = shootDir.sqrMagnitude > 0.0001f ? shootDir.normalized : Vector3.forward;
            Quaternion rotation = shootDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(projectileRotationOffsetEuler)
                : Quaternion.identity;
            GameObject projectile = Instantiate(projectilePrefab, spawnPos, rotation);
            DroneProjectile droneProjectile = projectile.GetComponent<DroneProjectile>();
            if (droneProjectile == null)
            {
                droneProjectile = projectile.AddComponent<DroneProjectile>();
            }
            bool shouldReportHit = !IsNetworkActive() || IsOwner;
            Transform ignoredDroneRoot = activeDrone != null ? activeDrone.transform.root : null;
            droneProjectile.Initialize(this, shouldReportHit, ignoredDroneRoot);

            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.linearVelocity = dir * projectileSpeed;
            }

            Destroy(projectile, Mathf.Max(0.1f, projectileLifetime));
        }

        public void NotifyProjectileHitResource(ExtractableResource resource)
        {
            if (resource == null)
            {
                return;
            }

            NetworkObject networkObject = resource.GetComponentInParent<NetworkObject>();
            bool hasNetworkId = networkObject != null && networkObject.IsSpawned;
            ulong networkId = hasNetworkId ? networkObject.NetworkObjectId : 0;
            Vector3 resourcePosition = resource.transform.position;
            string resourceName = CleanName(resource.name);

            if (IsNetworkActive())
            {
                RequestApplyStunServerRpc(hasNetworkId, networkId, resourcePosition, resourceName);
                return;
            }

            resource.TryApplyStun();
        }

        [ServerRpc]
        private void RequestApplyStunServerRpc(bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName)
        {
            ExtractableResource resource = ResolveResourceReference(hasResourceNetworkId, resourceNetworkId, resourcePosition, resourceName);
            if (resource == null)
            {
                return;
            }

            resource.TryApplyStun();

            // For scene objects without NetworkObject/NetworkVariables replication,
            // mirror the stun event to all clients so state stays consistent.
            if (!hasResourceNetworkId)
            {
                Vector3 resolvedPos = resource.transform.position;
                string resolvedName = CleanName(resource.name);
                ApplyStunClientRpc(false, 0, resolvedPos, resolvedName);
            }
        }

        [ClientRpc]
        private void ApplyStunClientRpc(bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName)
        {
            if (IsServer)
            {
                return;
            }

            ExtractableResource resource = ResolveResourceReference(hasResourceNetworkId, resourceNetworkId, resourcePosition, resourceName);
            if (resource == null)
            {
                return;
            }

            resource.TryApplyStun();
        }

        private ExtractableResource ResolveResourceReference(bool hasResourceNetworkId, ulong resourceNetworkId, Vector3 resourcePosition, string resourceName)
        {
            if (hasResourceNetworkId && NetworkManager != null && NetworkManager.SpawnManager != null)
            {
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(resourceNetworkId, out NetworkObject no) && no != null)
                {
                    ExtractableResource byNetworkId = no.GetComponentInParent<ExtractableResource>();
                    if (byNetworkId == null)
                    {
                        byNetworkId = no.GetComponentInChildren<ExtractableResource>(true);
                    }

                    if (byNetworkId != null)
                    {
                        return byNetworkId;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                return null;
            }

            ExtractableResource[] all = FindObjectsByType<ExtractableResource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ExtractableResource best = null;
            float bestSqrDist = float.MaxValue;
            string wantedName = CleanName(resourceName);
            for (int i = 0; i < all.Length; i++)
            {
                ExtractableResource candidate = all[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.Equals(CleanName(candidate.name), wantedName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float sqr = (candidate.transform.position - resourcePosition).sqrMagnitude;
                if (sqr < bestSqrDist)
                {
                    bestSqrDist = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        private static string CleanName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            string name = rawName;
            int cloneSuffixIndex = name.IndexOf("(Clone)");
            if (cloneSuffixIndex >= 0)
            {
                name = name.Substring(0, cloneSuffixIndex).Trim();
            }

            return name.Trim();
        }

        private void MoveDroneWithCollision(Vector3 moveDelta)
        {
            if (activeDrone == null || moveDelta.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector3 dir = moveDelta.normalized;
            float distance = moveDelta.magnitude;

            // Preferred path: Rigidbody sweep so drone does not tunnel through colliders.
            if (activeDroneBody != null)
            {
                if (activeDroneBody.SweepTest(dir, out RaycastHit hit, distance + collisionSkin, QueryTriggerInteraction.Ignore))
                {
                    distance = Mathf.Max(0f, hit.distance - collisionSkin);
                }

                Vector3 targetPos = activeDroneBody.position + dir * distance;
                activeDroneBody.MovePosition(targetPos);
                return;
            }

            // Fallback for prefabs without Rigidbody.
            if (Physics.Raycast(activeDrone.transform.position, dir, out RaycastHit rayHit, distance + collisionSkin, ~0, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(0f, rayHit.distance - collisionSkin);
            }

            activeDrone.transform.position += dir * distance;
        }

        private void SpawnDroneOnServer(Vector3 spawnPos, Quaternion spawnRot, ulong ownerClientId)
        {
            if (activeDroneNetworkId != 0)
            {
                return;
            }

            GameObject droneObject = Instantiate(dronePrefab, spawnPos, spawnRot);
            NetworkObject droneNetworkObject = droneObject.GetComponent<NetworkObject>();
            if (droneNetworkObject == null)
            {
                Debug.LogWarning("WeaponDrone: drone prefab is missing NetworkObject.");
                Destroy(droneObject);
                TriggerLocalFallbackOnOwnerClient(ownerClientId, spawnPos, spawnRot);
                return;
            }

            try
            {
                droneNetworkObject.SpawnWithOwnership(ownerClientId, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"WeaponDrone: network spawn failed ({ex.Message}). Check Network Prefabs registration.");
                Destroy(droneObject);
                TriggerLocalFallbackOnOwnerClient(ownerClientId, spawnPos, spawnRot);
                return;
            }

            if (!droneNetworkObject.IsSpawned)
            {
                Debug.LogWarning("WeaponDrone: network spawn did not complete. Check Network Prefabs registration.");
                Destroy(droneObject);
                TriggerLocalFallbackOnOwnerClient(ownerClientId, spawnPos, spawnRot);
                return;
            }

            activeDroneNetworkId = droneNetworkObject.NetworkObjectId;
            StartDroneLoopClientRpc(activeDroneNetworkId);

            BeginDroneForOwnerClientRpc(activeDroneNetworkId, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { ownerClientId }
                }
            });
        }

        [ServerRpc]
        private void RequestSpawnDroneServerRpc(Vector3 spawnPos, Quaternion spawnRot, ServerRpcParams serverRpcParams = default)
        {
            SpawnDroneOnServer(spawnPos, spawnRot, serverRpcParams.Receive.SenderClientId);
        }

        [ClientRpc]
        private void BeginDroneForOwnerClientRpc(ulong droneNetworkObjectId, ClientRpcParams clientRpcParams = default)
        {
            if (!IsOwner || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(droneNetworkObjectId, out NetworkObject droneNetworkObject))
            {
                Debug.LogWarning("WeaponDrone: spawned drone network object was not found on owner.");
                return;
            }

            BeginDroneControlFromInstance(droneNetworkObject.gameObject);
        }

        private void TriggerLocalFallbackOnOwnerClient(ulong ownerClientId, Vector3 spawnPos, Quaternion spawnRot)
        {
            // Prevent owner-only fallback in active multiplayer sessions.
            if (IsNetworkActive())
            {
                Debug.LogWarning("WeaponDrone: blocked local fallback during network session. Fix drone NetworkObject/Network Prefab registration.");
                return;
            }

            if (!allowLocalFallbackIfNetworkSpawnUnavailable)
            {
                return;
            }

            BeginLocalFallbackDroneClientRpc(spawnPos, spawnRot, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { ownerClientId }
                }
            });
        }

        [ClientRpc]
        private void BeginLocalFallbackDroneClientRpc(Vector3 spawnPos, Quaternion spawnRot, ClientRpcParams clientRpcParams = default)
        {
            if (!IsOwner || isUsingDrone || activeDrone != null)
            {
                return;
            }

            activeDrone = Instantiate(dronePrefab, spawnPos, spawnRot);
            BeginDroneControlFromInstance(activeDrone);
        }

        [ServerRpc]
        private void RequestReturnDroneServerRpc()
        {
            StartReturnOnServer(OwnerClientId);
        }

        private void StartReturnOnServer(ulong ownerClientId)
        {
            if (!TryGetSpawnedDroneById(activeDroneNetworkId, out NetworkObject droneNetworkObject))
            {
                return;
            }

            Vector3 target = ResolvePlayerReturnTarget(ownerClientId);
            StartCoroutine(ReturnAndDespawnServer(droneNetworkObject, target));
            ReturnDroneClientRpc(activeDroneNetworkId, target, Mathf.Max(0.1f, returnToPlayerSpeed));
        }

        private IEnumerator ReturnAndDespawnLocal(GameObject droneObject)
        {
            if (droneObject == null)
            {
                yield break;
            }

            Vector3 target = transform.position + returnToPlayerOffset;
            float speed = Mathf.Max(0.1f, returnToPlayerSpeed);
            while (Vector3.SqrMagnitude(droneObject.transform.position - target) > 0.01f)
            {
                droneObject.transform.position = Vector3.MoveTowards(
                    droneObject.transform.position,
                    target,
                    speed * Time.deltaTime);
                yield return null;
            }

            Destroy(droneObject);
            if (droneObject == activeDrone)
            {
                activeDrone = null;
            }
            isReturningDrone = false;
        }

        private IEnumerator ReturnAndDespawnServer(NetworkObject droneNetworkObject, Vector3 target)
        {
            if (droneNetworkObject == null)
            {
                yield break;
            }

            Transform droneTransform = droneNetworkObject.transform;
            Rigidbody rb = droneNetworkObject.GetComponent<Rigidbody>();
            float speed = Mathf.Max(0.1f, returnToPlayerSpeed);
            while (Vector3.SqrMagnitude(droneTransform.position - target) > 0.01f)
            {
                Vector3 pos = Vector3.MoveTowards(droneTransform.position, target, speed * Time.deltaTime);
                if (rb != null)
                {
                    rb.MovePosition(pos);
                }
                else
                {
                    droneTransform.position = pos;
                }
                yield return null;
            }

            StopDroneLoopClientRpc(activeDroneNetworkId);
            droneNetworkObject.Despawn(true);
            activeDroneNetworkId = 0;
            isReturningDrone = false;
        }

        [ClientRpc]
        private void ReturnDroneClientRpc(ulong droneNetworkObjectId, Vector3 target, float returnSpeed)
        {
            if (IsServer || NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(droneNetworkObjectId, out NetworkObject droneNetworkObject))
            {
                return;
            }

            StartCoroutine(ReturnAndDespawnClient(droneNetworkObject, target, returnSpeed));
        }

        private IEnumerator ReturnAndDespawnClient(NetworkObject droneNetworkObject, Vector3 target, float returnSpeed)
        {
            if (droneNetworkObject == null)
            {
                yield break;
            }

            Transform droneTransform = droneNetworkObject.transform;
            float speed = Mathf.Max(0.1f, returnSpeed);
            while (droneNetworkObject != null && droneTransform != null)
            {
                Vector3 currentPosition = droneTransform.position;
                if (Vector3.SqrMagnitude(currentPosition - target) <= 0.01f)
                {
                    yield break;
                }

                droneTransform.position = Vector3.MoveTowards(currentPosition, target, speed * Time.deltaTime);
                yield return null;
            }
        }

        private Vector3 ResolvePlayerReturnTarget(ulong ownerClientId)
        {
            if (NetworkManager == null || NetworkManager.ConnectedClients == null)
            {
                return transform.position + returnToPlayerOffset;
            }

            if (NetworkManager.ConnectedClients.TryGetValue(ownerClientId, out NetworkClient client) && client != null && client.PlayerObject != null)
            {
                return client.PlayerObject.transform.position + returnToPlayerOffset;
            }

            return transform.position + returnToPlayerOffset;
        }

        private void DespawnDroneOnServerImmediate()
        {
            if (!TryGetSpawnedDroneById(activeDroneNetworkId, out NetworkObject droneNetworkObject))
            {
                return;
            }

            StopDroneLoopClientRpc(activeDroneNetworkId);
            droneNetworkObject.Despawn(true);
            activeDroneNetworkId = 0;
        }

        private void OnGUI()
        {
            if (!showUi || !CoopGuard.IsLocalOwnerOrOffline(this))
            {
                return;
            }

            if (playerMovement != null && playerMovement.IsInteractionLocked)
            {
                return;
            }

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 20
            };

            GUIStyle infoStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 15
            };

            if (!isUsingDrone)
            {
                if (holdTimer > 0f && holdDurationToSummon > 0f)
                {
                    float progress = Mathf.Clamp01(holdTimer / holdDurationToSummon);
                    float cx = Screen.width * 0.5f;
                    float cy = Screen.height * 0.5f;
                    DrawCircularProgressRing(cx, cy, 34f, 8f, progress, 96, new Color(1f, 1f, 1f, 0.25f), new Color(0.208f, 0.816f, 0.886f, 1f));
                    GUI.Label(new Rect(cx - 180f, cy + 44f, 360f, 22f), "Summoning Drone", infoStyle);
                }

                return;
            }

            {
                float runtime = Mathf.Max(0f, droneTimer);
                float runtimeProgress = droneLifetime > 0f ? Mathf.Clamp01(runtime / droneLifetime) : 0f;
                string shotText = hasFiredThisSummon ? "Shot: Used" : "Shot: Ready (LMB)";

                const float panelW = 420f;
                const float panelH = 82f;
                float panelX = (Screen.width - panelW) * 0.5f;
                const float panelY = 20f;

                GUI.Box(new Rect(panelX, panelY, panelW, panelH), string.Empty);
                GUI.Label(new Rect(panelX, panelY + 4f, panelW, 26f), $"Drone Online: {Mathf.CeilToInt(runtime)}s", titleStyle);
                DrawProgressBar(panelX + 16f, panelY + 34f, panelW - 32f, 18f, runtimeProgress);
                GUI.Label(new Rect(panelX, panelY + 56f, panelW, 20f), shotText, infoStyle);
            }
        }

        private static void DrawProgressBar(float x, float y, float width, float height, float progress)
        {
            float p = Mathf.Clamp01(progress);
            GUI.Box(new Rect(x, y, width, height), string.Empty);
            GUI.Box(new Rect(x, y, width * p, height), string.Empty);
        }

        private void DrawCircularProgressRing(
            float centerX,
            float centerY,
            float radius,
            float thickness,
            float progress,
            int segments,
            Color backgroundColor,
            Color fillColor)
        {
            if (uiPixel == null)
            {
                return;
            }

            int segCount = Mathf.Max(16, segments);
            float clamped = Mathf.Clamp01(progress);
            int fillSegments = Mathf.RoundToInt(segCount * clamped);

            for (int i = 0; i < segCount; i++)
            {
                float t = (float)i / segCount;
                float angle = t * Mathf.PI * 2f - (Mathf.PI * 0.5f);
                float x = centerX + Mathf.Cos(angle) * radius;
                float y = centerY + Mathf.Sin(angle) * radius;

                GUI.color = i < fillSegments ? fillColor : backgroundColor;
                GUI.DrawTexture(new Rect(x - (thickness * 0.5f), y - (thickness * 0.5f), thickness, thickness), uiPixel);
            }

            GUI.color = Color.white;
        }

        private void StartSummonLoopSound()
        {
            if (summonAudioSource == null || summonLoopClip == null)
            {
                return;
            }

            summonAudioSource.clip = summonLoopClip;
            summonAudioSource.loop = true;
            summonAudioSource.volume = Mathf.Clamp01(summonLoopVolume);
            if (!summonAudioSource.isPlaying)
            {
                summonAudioSource.Play();
            }
        }

        private void StopSummonLoopSound()
        {
            if (summonAudioSource == null)
            {
                return;
            }

            if (summonAudioSource.loop && summonAudioSource.clip == summonLoopClip)
            {
                summonAudioSource.Stop();
                summonAudioSource.loop = false;
                summonAudioSource.clip = null;
            }
        }

        private void StartDroneActiveLoopSound(GameObject droneObject)
        {
            if (droneObject == null)
            {
                return;
            }

            AudioSource source = ResolveDroneAudioSource(droneObject);
            if (source == null)
            {
                return;
            }

            source.loop = true;
            source.volume = Mathf.Clamp01(droneActiveLoopVolume);
            if (!source.isPlaying)
            {
                source.Play();
            }
        }

        private void StopDroneActiveLoopSound(GameObject droneObject)
        {
            if (droneObject == null)
            {
                return;
            }

            AudioSource source = ResolveDroneAudioSource(droneObject);
            if (source == null)
            {
                return;
            }

            if (source.loop)
            {
                source.Stop();
                source.loop = false;
            }
        }

        private void EnsureDroneSfxVoicePool()
        {
            if (droneSfxAudioSource == null)
            {
                return;
            }

            if (droneSfxVoices.Count == 0)
            {
                droneSfxVoices.Add(droneSfxAudioSource);
                return;
            }

            bool hasPrimary = false;
            for (int i = 0; i < droneSfxVoices.Count; i++)
            {
                if (droneSfxVoices[i] == droneSfxAudioSource)
                {
                    hasPrimary = true;
                    break;
                }
            }

            if (!hasPrimary)
            {
                droneSfxVoices.Insert(0, droneSfxAudioSource);
            }
        }

        private AudioSource GetFreeDroneSfxVoice()
        {
            EnsureDroneSfxVoicePool();

            for (int i = 0; i < droneSfxVoices.Count; i++)
            {
                AudioSource voice = droneSfxVoices[i];
                if (voice != null && !voice.isPlaying)
                {
                    return voice;
                }
            }

            int maxVoices = Mathf.Max(1, maxConcurrentDroneSfx);
            if (droneSfxVoices.Count < maxVoices)
            {
                AudioSource clone = gameObject.AddComponent<AudioSource>();
                clone.playOnAwake = false;
                clone.loop = false;
                clone.spatialBlend = 0f;
                droneSfxVoices.Add(clone);
                return clone;
            }

            // All voices busy: reuse the first voice to avoid dropping entirely.
            return droneSfxVoices[0];
        }

        private void PlayDroneSfxOneShot(AudioClip clip, float volume, Vector3 worldPosition)
        {
            if (clip == null)
            {
                return;
            }

            AudioSource voice = GetFreeDroneSfxVoice();
            if (voice == null)
            {
                AudioSource.PlayClipAtPoint(clip, worldPosition, volume);
                return;
            }

            voice.PlayOneShot(clip, volume);
        }

        [ClientRpc]
        private void StartDroneLoopClientRpc(ulong droneNetworkObjectId)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(droneNetworkObjectId, out NetworkObject droneNetworkObject))
            {
                return;
            }

            StartDroneActiveLoopSound(droneNetworkObject.gameObject);
        }

        [ClientRpc]
        private void StopDroneLoopClientRpc(ulong droneNetworkObjectId)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
            {
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(droneNetworkObjectId, out NetworkObject droneNetworkObject))
            {
                return;
            }

            StopDroneActiveLoopSound(droneNetworkObject.gameObject);
        }

        private AudioSource ResolveDroneAudioSource(GameObject droneObject)
        {
            if (droneObject == null)
            {
                return null;
            }

            AudioSource source = droneObject.GetComponent<AudioSource>();
            if (source != null)
            {
                return source;
            }

            return droneObject.GetComponentInChildren<AudioSource>(true);
        }

    }
}


