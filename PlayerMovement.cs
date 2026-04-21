using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using WalaPaNameHehe.Multiplayer;

namespace WalaPaNameHehe
{
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMovement : NetworkBehaviour
    {
        private enum WaterSurfaceMode
        {
            ColliderTop,
            TransformY
        }

        public enum PovMode
        {
            Main = 0,
            Ragdoll = 1,
            External = 2
        }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintMultiplier = 1.8f;
    [SerializeField] private float movementSmoothing = 14f;
    [SerializeField] private bool useSoftPlayerBlocking = true;
    [SerializeField] private LayerMask softPlayerBlockingLayers = 0;
    [SerializeField] private float softBlockSkin = 0.03f;
    [SerializeField] private float externalSpeedMultiplier = 1f;
    [SerializeField] private float temporarySpeedMultiplier = 1f;

        [Header("Look")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Transform networkPitchPivot;
        [SerializeField] private float lookSensitivity = 0.15f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        [Header("Neck Follow")]
        [SerializeField] private Transform neckBone;
        [SerializeField] private bool autoFindNeckBone = true;
        [SerializeField, Range(0f, 1f)] private float neckFollowAmount = 0.65f;
        [SerializeField] private float neckMinPitch = -45f;
        [SerializeField] private float neckMaxPitch = 45f;
        [SerializeField] private float neckSmoothing = 16f;

        [Header("Camera Bob")]
        [SerializeField] private bool enableCameraBob = true;
        [SerializeField] private float walkBobFrequency = 7.5f;
        [SerializeField] private float walkBobAmplitude = 0.03f;
        [SerializeField] private float sprintBobFrequencyMultiplier = 1.95f;
        [SerializeField] private float sprintBobAmplitudeMultiplier = 1.2f;
        [SerializeField] private float bobSmoothing = 12f;
        [SerializeField] private float bobBlendInSpeed = 10f;
        [SerializeField] private float bobBlendOutSpeed = 7f;

        [Header("Sprint FOV")]
        [SerializeField] private bool enableSprintFov = true;
        [SerializeField] private float sprintFovBoost = 5f;
        [SerializeField] private float sprintFovSmoothing = 8f;

        [Header("Footsteps")]
        [SerializeField] private AudioSource footstepAudioSource;
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] [Range(0f, 1f)] private float footstepVolume = 0.7f;
        [SerializeField] private Vector2 footstepPitchRange = new Vector2(0.95f, 1.05f);
        [SerializeField] private float footstepMinInterval = 0.08f;

        [Header("Hunter Audio")]
        [SerializeField] private AudioSource hunterAudioSource;
        [SerializeField] private AudioClip[] hunterTargetClips;
        [SerializeField] [Range(0f, 1f)] private float hunterTargetVolume = 0.85f;
        [SerializeField] private Vector2 hunterTargetPitchRange = new Vector2(0.96f, 1.04f);
        [SerializeField] private float hunterTargetSoundCooldown = 4f;
        [SerializeField] private float hunterTargetMinDistance = 5f;
        [SerializeField] private float hunterTargetMaxDistance = 12f;
        [SerializeField] private float hunterTargetHeightOffset = 1.2f;
        [Header("Jump")]
        [SerializeField] private float jumpForce = 6f;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float minGroundNormalY = 0.5f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBufferTime = 0.1f;

        [Header("Water")]
        [SerializeField] private bool enableWaterBuoyancy = true;
        [SerializeField, Tooltip("How far below the water surface the player's pivot is allowed to go. Set this so the player sinks about half-body.")]
        private float maxSubmergeDepth = 0.85f;
        [SerializeField, Tooltip("Extra upward force when below the allowed waterline. 0 = hard clamp only.")]
        private float waterBuoyancyForce = 25f;
        [SerializeField, Tooltip("Optional extra drag while in water.")]
        private float waterDrag = 1.5f;
        [SerializeField, Range(0.05f, 1f), Tooltip("Horizontal move speed multiplier while in water.")]
        private float waterMoveSpeedMultiplier = 0.6f;
        [SerializeField, Range(0.05f, 2f), Tooltip("Jump force multiplier while in water.")]
        private float waterJumpForceMultiplier = 0.75f;
        [SerializeField, Min(0f), Tooltip("How far above the water surface the player can be while still considered 'in water'. Helps avoid large trigger volumes slowing you on land.")]
        private float waterAffectAboveSurface = 0.15f;
        [SerializeField, Min(0f), Tooltip("How far above the water surface the player must rise before water effects turn off (hysteresis).")]
        private float waterStopAffectAboveSurface = 0.25f;
        [SerializeField] private WaterSurfaceMode waterSurfaceMode = WaterSurfaceMode.ColliderTop;
        [SerializeField, Tooltip("Optional offset applied to the detected water surface Y.")]
        private float waterSurfaceOffset = 0f;
        [SerializeField, Tooltip("Tag used by water trigger volumes.")]
        private string waterTag = "Water";

        [Header("Isolation")]
        [SerializeField] private float isolationRadius = 6f;
        [SerializeField] private LayerMask isolationDetectionLayers = ~0;
        [SerializeField] private bool hasNearbyPlayer;
        [SerializeField] private bool isIsolated = true;
        [SerializeField] private float isolatedDuration;
        [SerializeField] private bool drawIsolationGizmo = true;
        [SerializeField] private Color isolationGizmoColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        [Header("Death")]
        [SerializeField] private float respawnDelaySeconds = 3f;
        [SerializeField] private bool restoreCursorStateOnRespawn = true;

        [Header("Animation")]
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string isMovingParam = "IsMoving";
        [SerializeField] private string isSprintingParam = "IsSprinting";
        [SerializeField] private string isBoostedParam = "IsBoosted";
        [SerializeField] private string jumpTriggerParam = "Jump";
        [SerializeField] private string onDroneParam = "OnDrone";
        [SerializeField] private float movingThreshold = 0.1f;

        private Rigidbody rb;
        private CapsuleCollider bodyCapsule;
        [SerializeField] private PlayerRagdollController ragdollController;
        private Vector2 moveInput;
        private float pitch;
        private float currentNeckPitch;
        private float targetNeckPitch;
        private bool isSprinting;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private Quaternion neckBaseLocalRotation = Quaternion.identity;
        private Vector3 cameraBaseLocalPosition;
        private float bobTimer;
        private float bobWeight;
        private Camera playerCamera;
        private float baseFov;
        private bool hasBaseFov;
        [Header("POV Cameras")]
        [SerializeField] private Camera mainPovCamera;
        [SerializeField] private Camera ragdollPovCamera;
        private float nextFootstepTime;
        private float nextHunterTargetSoundTime;
        private readonly Collider[] isolationHits = new Collider[32];
        private bool isDeadOffline;
        private Coroutine respawnRoutine;
        private Coroutine temporarySpeedRoutine;
        private float deadSinceLocal = -1f;
        private bool hasDeadSinceLocal;
        private bool hasStoredCursorState;
        private CursorLockMode storedCursorLockState;
        private bool storedCursorVisible;
        private bool isGrabbed;
        private bool isIncapacitated;
        private bool movementSuppressed;
        private bool hasGrabbedPhysicsState;
        private bool grabbedWasKinematic;
        private bool grabbedWasGravity;
        private bool suppressJump;
        private readonly HashSet<int> waterTriggerIds = new();
        private float currentWaterSurfaceY;
        private float cachedDefaultDrag;
        private bool isWaterAffectingCached;
        private PovMode localPovMode = PovMode.Main;
        private readonly NetworkVariable<float> syncedPitch = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> syncedIsDead = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> syncedIsInWater = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private bool lastReportedIsInWater;

        public bool IsGrounded => coyoteTimer > 0f;
        private readonly NetworkVariable<bool> syncedIsIsolated = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<float> syncedIsolatedDuration = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> syncedOnDroneAnimState = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private bool localOnDroneAnimState;
        private bool hasAppliedOnDroneAnimState;
        private bool appliedOnDroneAnimState;
        private float localHunterMeter;
        private readonly NetworkVariable<float> syncedHunterMeter = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> syncedPovMode = new(
            (int)PovMode.Main,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> syncedExternalAttackerNetworkObjectId = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool HasNearbyPlayer => IsNetworkActive() && !IsOwner ? !syncedIsIsolated.Value : hasNearbyPlayer;
        public bool IsDead => IsNetworkActive() ? syncedIsDead.Value : isDeadOffline;
        public bool IsInWater => IsNetworkActive() && !IsOwner ? syncedIsInWater.Value : IsWaterAffecting();
        public bool IsIsolated => IsNetworkActive() && !IsOwner ? syncedIsIsolated.Value : isIsolated;
        public float IsolatedDuration => IsNetworkActive() && !IsOwner ? syncedIsolatedDuration.Value : isolatedDuration;
        public float IsolationRadius => isolationRadius;
        public float HunterMeterValue => IsNetworkActive() ? syncedHunterMeter.Value : localHunterMeter;
        public PovMode CurrentPovMode => IsNetworkActive() ? (PovMode)syncedPovMode.Value : localPovMode;
        public ulong ExternalAttackerNetworkObjectId => IsNetworkActive() ? syncedExternalAttackerNetworkObjectId.Value : 0;
        public float LookSensitivity
        {
            get => lookSensitivity;
            set => lookSensitivity = Mathf.Max(0.01f, value);
        }
        public Transform CameraPivot => cameraPivot;
        public Transform NetworkPitchPivot => networkPitchPivot;

        public bool TryGetMainPovCamera(out Camera cam)
        {
            RefreshPovCameraReferences();
            cam = mainPovCamera;
            return cam != null;
        }

        public bool TryGetRagdollPovCamera(out Camera cam)
        {
            RefreshPovCameraReferences();
            cam = ragdollPovCamera;
            return cam != null;
        }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bodyCapsule = GetComponent<CapsuleCollider>();
        if (ragdollController == null)
        {
            ragdollController = GetComponent<PlayerRagdollController>();
        }
        externalSpeedMultiplier = Mathf.Clamp(externalSpeedMultiplier, 0.01f, 10f);
        temporarySpeedMultiplier = Mathf.Clamp(temporarySpeedMultiplier, 0.01f, 10f);
        if (rb != null)
        {
            cachedDefaultDrag = rb.linearDamping;
        }
    }

        private void Start()
        {
            if (cameraPivot == null)
            {
                Camera childCamera = GetComponentInChildren<Camera>(true);
                if (childCamera != null)
                {
                    cameraPivot = childCamera.transform;
                }
                else if (Camera.main != null && Camera.main.transform != null && Camera.main.transform.IsChildOf(transform))
                {
                    cameraPivot = Camera.main.transform;
                }
            }

            if (networkPitchPivot == null)
            {
                networkPitchPivot = cameraPivot;
            }

            if (footstepAudioSource == null)
            {
                footstepAudioSource = GetComponent<AudioSource>();
            }

            if (hunterAudioSource == null)
            {
                hunterAudioSource = footstepAudioSource != null ? footstepAudioSource : GetComponent<AudioSource>();
            }

            ResolveAnimatorReference();
            ResolveNeckReference();

            if (cameraPivot != null)
            {
                cameraBaseLocalPosition = cameraPivot.localPosition;
            }

            ResolvePlayerCamera();
            RefreshPovCameraReferences();
            ApplyLocalPovCameraState();

            rb.interpolation = RigidbodyInterpolation.Interpolate;
            ApplyPhysicsAuthorityMode();

            if (!IsNetworkActive() || IsOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyPhysicsAuthorityMode();
            RefreshPovCameraReferences();
            ApplyLocalPovCameraState();

            if (IsServer)
            {
                syncedIsDead.Value = false;
            }

            syncedIsDead.OnValueChanged += HandleDeadChanged;
        }

        public override void OnGainedOwnership()
        {
            base.OnGainedOwnership();
            ApplyPhysicsAuthorityMode();
            RefreshPovCameraReferences();
            ApplyLocalPovCameraState();
        }

        public override void OnLostOwnership()
        {
            base.OnLostOwnership();
            ApplyPhysicsAuthorityMode();
            RefreshPovCameraReferences();
            ApplyLocalPovCameraState();
        }

        public override void OnNetworkDespawn()
        {
            syncedIsDead.OnValueChanged -= HandleDeadChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (IsNetworkActive() && !IsOwner)
            {
                ApplyRemotePitch();
                ApplyRemoteAnimationState();
                return;
            }

            if (isGrabbed)
            {
                HandleLook();
                UpdateJumpTimers();
                return;
            }

            if (IsDead)
            {
                StoreAndShowCursorForDeath();
                moveInput = Vector2.zero;
                isSprinting = false;
                return;
            }
            else
            {
                RestoreCursorAfterDeath();
                hasDeadSinceLocal = false;
            }

            if (movementSuppressed)
            {
                moveInput = Vector2.zero;
                isSprinting = false;
                return;
            }

            if (Keyboard.current != null && (Keyboard.current.leftAltKey.wasPressedThisFrame || Keyboard.current.rightAltKey.wasPressedThisFrame))
            {
                ToggleCursorLock();
            }

            ReadMoveInput();
            if (temporarySpeedMultiplier < 0.999f)
            {
                isSprinting = false;
            }
            HandleLook();
            UpdateJumpTimers();
            TryJump();
            UpdateCameraBob();
            UpdateSprintFov();
            UpdateIsolationState();

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void FixedUpdate()
        {
            if (IsNetworkActive() && !IsOwner)
            {
                return;
            }

            if (isGrabbed)
            {
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                return;
            }

            if (movementSuppressed)
            {
                if (rb != null)
                {
                    Vector3 vel = rb.linearVelocity;
                    rb.linearVelocity = new Vector3(0f, vel.y, 0f);
                    rb.angularVelocity = Vector3.zero;
                }
                return;
            }

            if (IsDead)
            {
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                return;
            }

            if (rb == null || rb.isKinematic)
            {
                return;
            }

         if (temporarySpeedMultiplier < 0.999f)
         {
             isSprinting = false;
         }
         float currentMoveSpeed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
         bool isInWater = IsWaterAffecting();
         if (isInWater)
         {
             currentMoveSpeed *= Mathf.Clamp(waterMoveSpeedMultiplier, 0.05f, 1f);
         }
         ReportInWaterState(isInWater);
         float combinedMultiplier = externalSpeedMultiplier * temporarySpeedMultiplier;
         currentMoveSpeed *= Mathf.Max(0.01f, combinedMultiplier);
         Vector3 moveDirection = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized;
             Vector3 targetHorizontalVelocity = moveDirection * currentMoveSpeed;
            targetHorizontalVelocity = ApplySoftPlayerBlock(targetHorizontalVelocity);

            Vector3 currentVelocity = rb.linearVelocity;
            Vector3 currentHorizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
            float t = 1f - Mathf.Exp(-movementSmoothing * Time.fixedDeltaTime);
            Vector3 newHorizontalVelocity = Vector3.Lerp(currentHorizontalVelocity, targetHorizontalVelocity, t);

             rb.linearVelocity = new Vector3(newHorizontalVelocity.x, currentVelocity.y, newHorizontalVelocity.z);
             ApplyWaterBuoyancy();
             UpdateAnimation(newHorizontalVelocity.magnitude);
         }

        private void ApplyWaterBuoyancy()
        {
            if (!enableWaterBuoyancy || rb == null)
            {
                return;
            }

            if (!IsWaterAffecting())
            {
                if (Mathf.Abs(rb.linearDamping - cachedDefaultDrag) > 0.0001f)
                {
                    rb.linearDamping = cachedDefaultDrag;
                }
                return;
            }

            rb.linearDamping = Mathf.Max(cachedDefaultDrag, waterDrag);

            float clampedDepth = Mathf.Max(0f, maxSubmergeDepth);
            float minY = currentWaterSurfaceY - clampedDepth;
            Vector3 pos = rb.position;

            if (pos.y < minY)
            {
                pos.y = minY;
                rb.position = pos;

                Vector3 vel = rb.linearVelocity;
                if (vel.y < 0f)
                {
                    vel.y = 0f;
                    rb.linearVelocity = vel;
                }
            }

            float below = (currentWaterSurfaceY - clampedDepth) - rb.position.y;
            if (below > 0.0001f && waterBuoyancyForce > 0f)
            {
                rb.AddForce(Vector3.up * (waterBuoyancyForce * below), ForceMode.Acceleration);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryEnterWater(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryEnterWater(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || !other.isTrigger || !other.CompareTag(waterTag))
            {
                return;
            }

            waterTriggerIds.Remove(other.GetInstanceID());
            ReportInWaterState(IsWaterAffecting());
        }

        private void TryEnterWater(Collider other)
        {
            if (other == null || !other.isTrigger || !other.CompareTag(waterTag))
            {
                return;
            }

            waterTriggerIds.Add(other.GetInstanceID());
            currentWaterSurfaceY = GetWaterSurfaceY(other);
            ReportInWaterState(IsWaterAffecting());
        }

        private void ReportInWaterState(bool isInWater)
        {
            if (!IsNetworkActive() || !IsOwner)
            {
                return;
            }

            if (isInWater == lastReportedIsInWater)
            {
                return;
            }

            lastReportedIsInWater = isInWater;
            syncedIsInWater.Value = isInWater;
        }

        private float GetWaterSurfaceY(Collider waterCollider)
        {
            if (waterCollider == null)
            {
                return 0f;
            }

            float baseY = waterSurfaceMode == WaterSurfaceMode.TransformY
                ? waterCollider.transform.position.y
                : waterCollider.bounds.max.y;

            return baseY + waterSurfaceOffset;
        }

        private bool IsWaterAffecting()
        {
            if (waterTriggerIds.Count <= 0 || rb == null)
            {
                isWaterAffectingCached = false;
                return false;
            }

            float enterAbove = Mathf.Max(0f, waterAffectAboveSurface);
            float exitAbove = Mathf.Max(enterAbove, waterStopAffectAboveSurface);
            float threshold = isWaterAffectingCached ? exitAbove : enterAbove;
            isWaterAffectingCached = rb.position.y <= currentWaterSurfaceY + threshold;
            return isWaterAffectingCached;
        }

        private Vector3 ApplySoftPlayerBlock(Vector3 targetHorizontalVelocity)
        {
            if (!useSoftPlayerBlocking || bodyCapsule == null || targetHorizontalVelocity.sqrMagnitude <= 0.000001f)
            {
                return targetHorizontalVelocity;
            }

            Vector3 direction = targetHorizontalVelocity.normalized;
            float castDistance = targetHorizontalVelocity.magnitude * Time.fixedDeltaTime + Mathf.Max(0.001f, softBlockSkin);

            Vector3 center = transform.TransformPoint(bodyCapsule.center);
            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleY = Mathf.Abs(transform.lossyScale.y);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float radius = bodyCapsule.radius * Mathf.Max(scaleX, scaleZ);
            float halfHeight = Mathf.Max(radius, (bodyCapsule.height * scaleY * 0.5f) - radius);
            Vector3 axis = transform.up * halfHeight;
            Vector3 p1 = center + axis;
            Vector3 p2 = center - axis;

            RaycastHit[] hits = Physics.CapsuleCastAll(
                p1,
                p2,
                radius,
                direction,
                castDistance,
                softPlayerBlockingLayers,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                return targetHorizontalVelocity;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                if (hitCollider.transform.root == transform.root)
                {
                    continue;
                }

                return Vector3.zero;
            }

            return targetHorizontalVelocity;
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsInGroundLayers(collision.gameObject.layer))
            {
                return;
            }

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y >= minGroundNormalY)
                {
                    coyoteTimer = coyoteTime;
                    return;
                }
            }
        }

        private bool IsInGroundLayers(int layer)
        {
            return (groundLayers.value & (1 << layer)) != 0;
        }

        private void ReadMoveInput()
        {
            if (Keyboard.current == null)
            {
                moveInput = Vector2.zero;
                isSprinting = false;
                return;
            }

            float x = 0f;
            float y = 0f;

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;

            moveInput = new Vector2(x, y).normalized;
            isSprinting = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                jumpBufferTimer = jumpBufferTime;
            }
        }

        private void HandleLook()
        {
            if (Mouse.current == null)
            {
                return;
            }
            if (Cursor.visible || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float yaw = mouseDelta.x * lookSensitivity;
            float pitchDelta = mouseDelta.y * lookSensitivity;

            transform.Rotate(Vector3.up * yaw);

            pitch -= pitchDelta;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            if (cameraPivot != null)
            {
                cameraPivot.localEulerAngles = new Vector3(
                    pitch,
                    0f,
                    0f);
            }

            // Optional world-space pitch pivot so remote clients can see up/down aiming.
            if (networkPitchPivot != null)
            {
                networkPitchPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            targetNeckPitch = Mathf.Clamp(pitch * neckFollowAmount, neckMinPitch, neckMaxPitch);

            if (IsNetworkActive() && IsOwner)
            {
                syncedPitch.Value = pitch;
            }
        }

        private void UpdateJumpTimers()
        {
            coyoteTimer -= Time.deltaTime;
            jumpBufferTimer -= Time.deltaTime;
        }

        private void TryJump()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (suppressJump)
            {
                return;
            }

            if (rb == null || rb.isKinematic)
            {
                return;
            }

            bool canJump = coyoteTimer > 0f || IsWaterAffecting();
            if (jumpBufferTimer > 0f && canJump)
            {
                TriggerJumpAnimation();

                float vertical = rb.linearVelocity.y;
                if (vertical < 0f)
                {
                    vertical = 0f;
                }

                rb.linearVelocity = new Vector3(rb.linearVelocity.x, vertical, rb.linearVelocity.z);
                float force = jumpForce;
                if (IsWaterAffecting())
                {
                    force *= Mathf.Clamp(waterJumpForceMultiplier, 0.05f, 2f);
                }
                rb.AddForce(Vector3.up * force, ForceMode.Impulse);

                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
            }
        }

        private void TriggerJumpAnimation()
        {
            if (string.IsNullOrWhiteSpace(jumpTriggerParam))
            {
                return;
            }

            if (IsServer)
            {
                ResolveAnimatorReference();
                if (characterAnimator == null)
                {
                    return;
                }

                characterAnimator.SetTrigger(jumpTriggerParam);
            }
            else if (IsOwner)
            {
                TriggerJumpServerRpc();
            }
        }

        private void UpdateAnimation(float horizontalSpeed)
        {
            bool isMoving = horizontalSpeed > movingThreshold;
            bool isSprintingNow = isSprinting && isMoving;
            bool isBoosted = temporarySpeedMultiplier > 1.01f;

            if (IsServer)
            {
                ResolveAnimatorReference();

                if (characterAnimator == null)
                {
                    return;
                }

                characterAnimator.SetFloat(speedParam, horizontalSpeed);
                characterAnimator.SetBool(isMovingParam, isMoving);
                characterAnimator.SetBool(isSprintingParam, isSprintingNow);
                if (!string.IsNullOrWhiteSpace(isBoostedParam))
                {
                    characterAnimator.SetBool(isBoostedParam, isBoosted);
                }
                ApplyOnDroneAnimatorParam(localOnDroneAnimState);
            }
            else if (IsOwner)
            {
                SubmitAnimatorStateServerRpc(horizontalSpeed, isMoving, isSprintingNow, isBoosted, localOnDroneAnimState);
            }
        }

        [ServerRpc]
        private void SubmitAnimatorStateServerRpc(float horizontalSpeed, bool isMoving, bool isSprintingNow, bool isBoosted, bool isOnDrone)
        {
            ResolveAnimatorReference();
            if (characterAnimator == null)
            {
                return;
            }

            characterAnimator.SetFloat(speedParam, horizontalSpeed);
            characterAnimator.SetBool(isMovingParam, isMoving);
            characterAnimator.SetBool(isSprintingParam, isSprintingNow);
            if (!string.IsNullOrWhiteSpace(isBoostedParam))
            {
                characterAnimator.SetBool(isBoostedParam, isBoosted);
            }
            ApplyOnDroneAnimatorParam(isOnDrone);
        }

        [ServerRpc]
        private void TriggerJumpServerRpc()
        {
            if (string.IsNullOrWhiteSpace(jumpTriggerParam))
            {
                return;
            }

            ResolveAnimatorReference();
            if (characterAnimator == null)
            {
                return;
            }

            characterAnimator.SetTrigger(jumpTriggerParam);
        }

        private void ResolveAnimatorReference()
        {
            if (characterAnimator != null)
            {
                return;
            }

            Animator[] animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator a = animators[i];
                if (a == null)
                {
                    continue;
                }

                // Prefer a child animator (mesh rig), fallback to root animator.
                if (a.transform != transform)
                {
                    characterAnimator = a;
                    return;
                }
            }

            if (animators.Length > 0)
            {
                characterAnimator = animators[0];
            }
        }

        private void LateUpdate()
        {
            ApplyNeckLook();
        }

        private void UpdateCameraBob()
        {
            if (cameraPivot == null)
            {
                return;
            }

            float targetOffsetY = 0f;
            if (enableCameraBob)
            {
                // Use movement intent + grounded time for a stable local-only bob.
                bool moving = moveInput.sqrMagnitude > 0.001f;
                bool grounded = coyoteTimer > 0f;
                bool shouldBob = moving && grounded;

                float targetWeight = shouldBob ? 1f : 0f;
                float weightSpeed = shouldBob ? bobBlendInSpeed : bobBlendOutSpeed;
                float wt = 1f - Mathf.Exp(-weightSpeed * Time.deltaTime);
                bobWeight = Mathf.Lerp(bobWeight, targetWeight, wt);

                if (shouldBob)
                {
                    float frequency = walkBobFrequency;
                    float amplitude = walkBobAmplitude;
                    if (isSprinting)
                    {
                        frequency *= sprintBobFrequencyMultiplier;
                        amplitude *= sprintBobAmplitudeMultiplier;
                    }

                    bobTimer += Time.deltaTime * frequency;
                    targetOffsetY = Mathf.Sin(bobTimer * Mathf.PI * 2f) * amplitude * bobWeight;
                }
            }

            Vector3 targetLocalPos =
                cameraBaseLocalPosition +
                new Vector3(0f, targetOffsetY, 0f);
            float t = 1f - Mathf.Exp(-bobSmoothing * Time.deltaTime);
            cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, targetLocalPos, t);
        }

        private void UpdateSprintFov()
        {
            if (!enableSprintFov)
            {
                return;
            }

            if (!ResolvePlayerCamera())
            {
                return;
            }

            float targetFov = baseFov + (isSprinting ? sprintFovBoost : 0f);
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, sprintFovSmoothing) * Time.deltaTime);
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, t);
        }

        private bool ResolvePlayerCamera()
        {
            if (playerCamera == null)
            {
                if (cameraPivot != null)
                {
                    playerCamera = cameraPivot.GetComponent<Camera>();
                }

                if (playerCamera == null)
                {
                    playerCamera = GetComponentInChildren<Camera>(true);
                }
            }

            if (playerCamera != null && !hasBaseFov)
            {
                baseFov = playerCamera.fieldOfView;
                hasBaseFov = true;
            }

            return playerCamera != null;
        }

        private void RefreshPovCameraReferences()
        {
            if (mainPovCamera == null)
            {
                mainPovCamera = playerCamera != null ? playerCamera : GetComponentInChildren<Camera>(true);
            }

            if (ragdollPovCamera == null && cameraPivot != null)
            {
                Camera[] cams = cameraPivot.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cams.Length; i++)
                {
                    Camera cam = cams[i];
                    if (cam != null && cam != mainPovCamera)
                    {
                        ragdollPovCamera = cam;
                        break;
                    }
                }
            }
        }

        public void SetRagdollPovActive(bool active)
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            RefreshPovCameraReferences();

            AudioListener mainListener = mainPovCamera != null ? mainPovCamera.GetComponent<AudioListener>() : null;
            AudioListener ragdollListener = ragdollPovCamera != null ? ragdollPovCamera.GetComponent<AudioListener>() : null;

            if (active)
            {
                if (mainPovCamera != null && ragdollPovCamera != null)
                {
                    ragdollPovCamera.transform.localPosition = mainPovCamera.transform.localPosition;
                    ragdollPovCamera.transform.localRotation = mainPovCamera.transform.localRotation;
                }

                if (ragdollPovCamera != null)
                {
                    ragdollPovCamera.enabled = true;
                }
                if (ragdollListener != null)
                {
                    ragdollListener.enabled = true;
                }

                if (mainPovCamera != null)
                {
                    mainPovCamera.enabled = false;
                }
                if (mainListener != null)
                {
                    mainListener.enabled = false;
                }

                SetLocalPovMode(PovMode.Ragdoll);
                return;
            }

            if (mainPovCamera != null && ragdollPovCamera != null)
            {
                mainPovCamera.transform.localPosition = ragdollPovCamera.transform.localPosition;
                mainPovCamera.transform.localRotation = ragdollPovCamera.transform.localRotation;
            }

            if (ragdollPovCamera != null)
            {
                Vector3 forward = ragdollPovCamera.transform.forward;
                Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
                if (flatForward.sqrMagnitude > 0.0001f)
                {
                    float yawAngle = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0f, yawAngle, 0f);

                    Quaternion yawRot = Quaternion.Euler(0f, yawAngle, 0f);
                    Vector3 localForward = Quaternion.Inverse(yawRot) * forward;
                    float pitchAngle = -Mathf.Atan2(localForward.y, localForward.z) * Mathf.Rad2Deg;
                    pitch = Mathf.Clamp(pitchAngle, minPitch, maxPitch);

                    if (cameraPivot != null)
                    {
                        cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
                    }

                    if (networkPitchPivot != null)
                    {
                        networkPitchPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
                    }

                    if (IsNetworkActive() && IsOwner)
                    {
                        syncedPitch.Value = pitch;
                    }
                }
            }

            if (mainPovCamera != null)
            {
                mainPovCamera.enabled = true;
            }
            if (mainListener != null)
            {
                mainListener.enabled = true;
            }

            if (ragdollPovCamera != null)
            {
                ragdollPovCamera.enabled = false;
            }
            if (ragdollListener != null)
            {
                ragdollListener.enabled = false;
            }

            SetLocalPovMode(PovMode.Main);
        }

        public void ServerSetExternalPov(ulong attackerNetworkObjectId)
        {
            if (!IsNetworkActive() || !IsServer)
            {
                return;
            }

            syncedExternalAttackerNetworkObjectId.Value = attackerNetworkObjectId;
            syncedPovMode.Value = (int)PovMode.External;
        }

        public void ServerClearExternalPov()
        {
            if (!IsNetworkActive() || !IsServer)
            {
                return;
            }

            syncedExternalAttackerNetworkObjectId.Value = 0;
            syncedPovMode.Value = (int)PovMode.Main;
        }

        private void SetLocalPovMode(PovMode mode)
        {
            localPovMode = mode;

            if (!IsNetworkActive() || !IsOwner)
            {
                return;
            }

            RequestSetPovModeServerRpc((int)mode);
        }

        [ServerRpc]
        private void RequestSetPovModeServerRpc(int mode, ServerRpcParams serverRpcParams = default)
        {
            PovMode safe = System.Enum.IsDefined(typeof(PovMode), mode) ? (PovMode)mode : PovMode.Main;
            syncedPovMode.Value = (int)safe;
            if (safe != PovMode.External)
            {
                syncedExternalAttackerNetworkObjectId.Value = 0;
            }
        }

        private void ApplyLocalPovCameraState()
        {
            bool local = IsLocallyControlled();
            RefreshPovCameraReferences();

            if (mainPovCamera != null)
            {
                mainPovCamera.enabled = local;
            }
            if (ragdollPovCamera != null)
            {
                ragdollPovCamera.enabled = false;
            }

            AudioListener mainListener = mainPovCamera != null ? mainPovCamera.GetComponent<AudioListener>() : null;
            if (mainListener != null)
            {
                mainListener.enabled = local;
            }

            AudioListener ragdollListener = ragdollPovCamera != null ? ragdollPovCamera.GetComponent<AudioListener>() : null;
            if (ragdollListener != null)
            {
                ragdollListener.enabled = false;
            }
        }

        private void ResolveNeckReference()
        {
            if (neckBone == null && autoFindNeckBone)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    Transform t = children[i];
                    if (t == null)
                    {
                        continue;
                    }

                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("neck"))
                    {
                        neckBone = t;
                        break;
                    }
                }
            }

            if (neckBone != null)
            {
                neckBaseLocalRotation = neckBone.localRotation;
            }
        }

        private void ApplyNeckLook()
        {
            if (neckBone == null)
            {
                return;
            }

            float t = 1f - Mathf.Exp(-neckSmoothing * Time.deltaTime);
            currentNeckPitch = Mathf.Lerp(currentNeckPitch, targetNeckPitch, t);
            neckBone.localRotation = neckBaseLocalRotation * Quaternion.Euler(currentNeckPitch, 0f, 0f);
        }

        private void ApplyRemotePitch()
        {
            pitch = Mathf.Clamp(syncedPitch.Value, minPitch, maxPitch);
            targetNeckPitch = Mathf.Clamp(pitch * neckFollowAmount, neckMinPitch, neckMaxPitch);

            if (networkPitchPivot != null)
            {
                networkPitchPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }
        }

        private void ApplyRemoteAnimationState()
        {
            // OnDrone animation is temporarily disabled.
        }

        public bool ServerKill()
        {
            if (IsNetworkActive())
            {
                if (!IsServer)
                {
                    return false;
                }

                if (syncedIsDead.Value)
                {
                    return false;
                }

                syncedIsDead.Value = true;
                WalaPaNameHehe.Multiplayer.HunterMeterManager.Instance?.ReportDeath(this);
                ClearDinoTargets();
                return true;
            }

            if (isDeadOffline)
            {
                return false;
            }

            isDeadOffline = true;
            WalaPaNameHehe.Multiplayer.HunterMeterManager.Instance?.ReportDeath(this);
            ClearDinoTargets();
            return true;
        }

        public void ServerForceRevive()
        {
            if (IsNetworkActive())
            {
                if (!IsServer)
                {
                    return;
                }

                if (!syncedIsDead.Value)
                {
                    return;
                }

                syncedIsDead.Value = false;
            }
            else
            {
                if (!isDeadOffline)
                {
                    return;
                }

                isDeadOffline = false;
            }

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }

            if (ragdollController != null)
            {
                ragdollController.ResetRagdoll();
            }
        }

        public void DebugRequestKillSelf()
        {
            if (IsDead)
            {
                return;
            }

            if (IsNetworkActive())
            {
                if (!IsOwner)
                {
                    return;
                }

                DebugKillSelfServerRpc();
                return;
            }

            ServerKill();
        }

        [ServerRpc(RequireOwnership = true)]
        private void DebugKillSelfServerRpc(ServerRpcParams serverRpcParams = default)
        {
            ServerKill();
        }

        public void SetExternalSpeedMultiplier(float multiplier)
        {
            externalSpeedMultiplier = Mathf.Clamp(multiplier, 0.01f, 10f);
        }

        public void SetMovementSuppressed(bool suppressed)
        {
            movementSuppressed = suppressed;
            if (suppressed)
            {
                moveInput = Vector2.zero;
                isSprinting = false;
                jumpBufferTimer = 0f;
            }
        }

        public float GetExternalSpeedMultiplier()
        {
            return externalSpeedMultiplier;
        }

        public CapsuleCollider GetCapsuleCollider()
        {
            return bodyCapsule;
        }

        public void ApplyTemporarySpeedMultiplier(float multiplier, float durationSeconds)
        {
            float clampedMultiplier = Mathf.Clamp(multiplier, 0.01f, 10f);
            float clampedDuration = Mathf.Max(0f, durationSeconds);

            if (temporarySpeedRoutine != null)
            {
                StopCoroutine(temporarySpeedRoutine);
                temporarySpeedRoutine = null;
            }

            temporarySpeedMultiplier = clampedMultiplier;

            if (clampedDuration > 0f)
            {
                suppressJump = clampedMultiplier < 0.999f;
                temporarySpeedRoutine = StartCoroutine(TemporarySpeedMultiplierRoutine(clampedDuration));
            }
        }

        private IEnumerator TemporarySpeedMultiplierRoutine(float durationSeconds)
        {
            yield return new WaitForSeconds(durationSeconds);
            temporarySpeedMultiplier = 1f;
            suppressJump = false;
            temporarySpeedRoutine = null;
        }

        public void RequestRespawn()
        {
            if (!CanRespawnNow())
            {
                return;
            }

            if (IsNetworkActive())
            {
                if (!IsOwner)
                {
                    return;
                }

                RequestRespawnServerRpc();
                return;
            }

            StartRespawnOffline();
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestRespawnServerRpc(ServerRpcParams serverRpcParams = default)
        {
            if (!syncedIsDead.Value)
            {
                return;
            }

            if (!IsRespawnAllowedNow())
            {
                return;
            }

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(RespawnRoutine(0f));
        }

        private IEnumerator RespawnRoutine(float delaySeconds)
        {
            float delay = Mathf.Max(0f, delaySeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (IsNetworkActive())
            {
                if (!IsServer)
                {
                    yield break;
                }

                MultiplayerPlayerSpawner spawner = Object.FindObjectOfType<MultiplayerPlayerSpawner>();
                if (spawner != null)
                {
                    spawner.RespawnClientAtRandomSpawnPoint(OwnerClientId);
                }

                syncedIsDead.Value = false;
            }
            else
            {
                isDeadOffline = false;
                if (ragdollController != null)
                {
                    ragdollController.ResetRagdoll();
                }
            }

            respawnRoutine = null;
        }

        private void StartRespawnOffline()
        {
            if (!isDeadOffline)
            {
                return;
            }

            if (!IsRespawnAllowedNow())
            {
                return;
            }

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(RespawnRoutine(0f));
        }

        private void HandleDeadChanged(bool previous, bool next)
        {
            if (!next && ragdollController != null)
            {
                ragdollController.ResetRagdoll();
            }

            if (!next)
            {
                localPovMode = PovMode.Main;
                if (IsNetworkActive() && IsServer)
                {
                    syncedExternalAttackerNetworkObjectId.Value = 0;
                    syncedPovMode.Value = (int)PovMode.Main;
                }
            }
        }

        private void ClearDinoTargets()
        {
            DinoAI[] dinos = Object.FindObjectsByType<DinoAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Transform root = transform.root;
            for (int i = 0; i < dinos.Length; i++)
            {
                DinoAI dino = dinos[i];
                if (dino == null)
                {
                    continue;
                }

                dino.ClearTargetIfMatches(root);
            }
        }

        private void StoreAndShowCursorForDeath()
        {
            if (!hasDeadSinceLocal)
            {
                deadSinceLocal = Time.unscaledTime;
                hasDeadSinceLocal = true;
            }

            if (!restoreCursorStateOnRespawn)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (!hasStoredCursorState)
            {
                storedCursorLockState = Cursor.lockState;
                storedCursorVisible = Cursor.visible;
                hasStoredCursorState = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreCursorAfterDeath()
        {
            if (!restoreCursorStateOnRespawn)
            {
                return;
            }

            if (!hasStoredCursorState)
            {
                return;
            }

            Cursor.lockState = storedCursorLockState;
            Cursor.visible = storedCursorVisible;
            hasStoredCursorState = false;
        }

        private void ToggleCursorLock()
        {
            bool show = Cursor.lockState != CursorLockMode.None || !Cursor.visible;
            if (show)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private bool CanRespawnNow()
        {
            return IsRespawnAllowedNow() && GetRespawnRemainingSeconds() <= 0f;
        }

        private bool IsRespawnAllowedNow()
        {
            WalaPaNameHehe.Multiplayer.GameManager gm = WalaPaNameHehe.Multiplayer.GameManager.Instance;
            if (gm == null)
            {
                return true;
            }

            // During expeditions, deaths should be final (match dino instakill behavior).
            return gm.CurrentState == WalaPaNameHehe.Multiplayer.GameManager.ExpeditionState.WaitingToStart;
        }

        private float GetRespawnRemainingSeconds()
        {
            if (!IsDead)
            {
                return 0f;
            }

            float elapsed = hasDeadSinceLocal ? (Time.unscaledTime - deadSinceLocal) : 0f;
            return Mathf.Max(0f, respawnDelaySeconds - elapsed);
        }

        private void ApplyOnDroneAnimatorParam(bool isOnDrone)
        {
            // OnDrone animation is temporarily disabled.
        }

        public void SetOnDroneAnimationState(bool isOnDrone)
        {
            // OnDrone animation is temporarily disabled.
        }

        private bool IsNetworkActive()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        private void ApplyPhysicsAuthorityMode()
        {
            if (rb == null)
            {
                return;
            }

            bool isRemoteProxy = IsNetworkActive() && !IsOwner;
            if (isRemoteProxy)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
                return;
            }

            rb.isKinematic = false;
            rb.useGravity = true;
        }
        // Animation Event hook. Add an event named "step" on foot contact frames.
        public void Step()
        {
            PlayFootstepFromAnimationEvent();
        }

        private void PlayFootstepFromAnimationEvent()
        {
            if (!CanPlayFootstepNow())
            {
                return;
            }

            int clipIndex = Random.Range(0, footstepClips.Length);
            AudioClip clip = footstepClips[clipIndex];
            if (clip == null)
            {
                return;
            }

            float pitchMin = Mathf.Min(footstepPitchRange.x, footstepPitchRange.y);
            float pitchMax = Mathf.Max(footstepPitchRange.x, footstepPitchRange.y);
            float pitch = Random.Range(pitchMin, pitchMax);
            footstepAudioSource.pitch = pitch;
            footstepAudioSource.PlayOneShot(clip, Mathf.Clamp01(footstepVolume));
            nextFootstepTime = Time.time + Mathf.Max(0.01f, footstepMinInterval);
        }

        private bool CanPlayFootstepNow()
        {
            if (Time.time < nextFootstepTime)
            {
                return false;
            }

            if (footstepAudioSource == null || footstepClips == null || footstepClips.Length == 0)
            {
                return false;
            }

            // Only play local-owner footsteps from this controller.
            if (IsNetworkActive() && !IsOwner)
            {
                return false;
            }

            bool grounded = coyoteTimer > 0f;
            if (!grounded)
            {
                return false;
            }

            if (moveInput.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            return true;
        }

        public void TriggerHunterTargetSound()
        {
            if (!IsNetworkActive())
            {
                PlayHunterTargetSoundLocally();
                return;
            }

            if (!IsServer)
            {
                return;
            }

            if (OwnerClientId == NetworkManager.ServerClientId)
            {
                PlayHunterTargetSoundLocally();
                return;
            }

            PlayHunterTargetSoundClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            });
        }

        [ClientRpc]
        private void PlayHunterTargetSoundClientRpc(ClientRpcParams clientRpcParams = default)
        {
            PlayHunterTargetSoundLocally();
        }

        private void PlayHunterTargetSoundLocally()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            if (Time.time < nextHunterTargetSoundTime)
            {
                return;
            }

            if (hunterAudioSource == null || hunterTargetClips == null || hunterTargetClips.Length == 0)
            {
                return;
            }

            int clipIndex = Random.Range(0, hunterTargetClips.Length);
            AudioClip clip = hunterTargetClips[clipIndex];
            if (clip == null)
            {
                return;
            }

            float pitchMin = Mathf.Min(hunterTargetPitchRange.x, hunterTargetPitchRange.y);
            float pitchMax = Mathf.Max(hunterTargetPitchRange.x, hunterTargetPitchRange.y);
            float pitch = Random.Range(pitchMin, pitchMax);

            Vector2 horizontal = Random.insideUnitCircle.normalized;
            if (horizontal.sqrMagnitude <= 0.0001f)
            {
                horizontal = Vector2.right;
            }

            float distance = Random.Range(
                Mathf.Max(0.1f, hunterTargetMinDistance),
                Mathf.Max(hunterTargetMinDistance + 0.1f, hunterTargetMaxDistance));

            Vector3 soundPosition = transform.position +
                new Vector3(horizontal.x, 0f, horizontal.y) * distance +
                Vector3.up * hunterTargetHeightOffset;

            GameObject audioObject = new GameObject("HunterTargetCueAudio");
            audioObject.transform.position = soundPosition;

            AudioSource tempSource = audioObject.AddComponent<AudioSource>();
            tempSource.clip = clip;
            tempSource.volume = Mathf.Clamp01(hunterTargetVolume);
            tempSource.pitch = pitch;
            tempSource.spatialBlend = 1f;
            tempSource.playOnAwake = false;
            tempSource.minDistance = Mathf.Max(0.1f, hunterTargetMinDistance * 0.5f);
            tempSource.maxDistance = Mathf.Max(tempSource.minDistance + 0.1f, hunterTargetMaxDistance * 2f);
            tempSource.rolloffMode = AudioRolloffMode.Linear;

            if (hunterAudioSource != null)
            {
                tempSource.outputAudioMixerGroup = hunterAudioSource.outputAudioMixerGroup;
                tempSource.dopplerLevel = hunterAudioSource.dopplerLevel;
                tempSource.spread = hunterAudioSource.spread;
                tempSource.priority = hunterAudioSource.priority;
                tempSource.reverbZoneMix = hunterAudioSource.reverbZoneMix;
                tempSource.bypassEffects = hunterAudioSource.bypassEffects;
                tempSource.bypassListenerEffects = hunterAudioSource.bypassListenerEffects;
                tempSource.bypassReverbZones = hunterAudioSource.bypassReverbZones;
            }

            tempSource.Play();
            Destroy(audioObject, Mathf.Max(clip.length / Mathf.Max(0.01f, pitch), 0.2f) + 0.25f);
            nextHunterTargetSoundTime = Time.time + Mathf.Max(0.1f, hunterTargetSoundCooldown);
        }

        private void UpdateIsolationState()
        {
            float radius = Mathf.Max(0f, isolationRadius);
            if (radius <= 0f)
            {
                SetIsolationState(foundOtherPlayer: false);
                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                radius,
                isolationHits,
                isolationDetectionLayers,
                QueryTriggerInteraction.Ignore);

            bool foundOtherPlayer = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = isolationHits[i];
                if (hit == null)
                {
                    continue;
                }

                if (hit.transform.root == transform.root)
                {
                    continue;
                }

                PlayerMovement otherPlayer = hit.GetComponentInParent<PlayerMovement>();
                if (otherPlayer == null || otherPlayer == this)
                {
                    continue;
                }

                foundOtherPlayer = true;
                break;
            }

            SetIsolationState(foundOtherPlayer);

            for (int i = 0; i < hitCount; i++)
            {
                isolationHits[i] = null;
            }
        }

        private void SetIsolationState(bool foundOtherPlayer)
        {
            hasNearbyPlayer = foundOtherPlayer;
            isIsolated = !foundOtherPlayer;
            isolatedDuration = isIsolated ? isolatedDuration + Time.deltaTime : 0f;

            if (IsNetworkActive() && IsOwner)
            {
                syncedIsIsolated.Value = isIsolated;
                syncedIsolatedDuration.Value = isolatedDuration;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawIsolationGizmo)
            {
                return;
            }

            Gizmos.color = isolationGizmoColor;
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, isolationRadius));
        }

        private bool IsLocallyControlled()
        {
            return CoopGuard.IsLocalOwnerOrOffline(this);
        }

        public void ServerAddHunterMeter(float delta)
        {
            if (IsNetworkActive())
            {
                if (!IsServer)
                {
                    return;
                }

                syncedHunterMeter.Value = Mathf.Clamp01(syncedHunterMeter.Value + delta);
                return;
            }

            localHunterMeter = Mathf.Clamp01(localHunterMeter + delta);
        }

        public void ServerResetHunterMeter()
        {
            if (IsNetworkActive())
            {
                if (!IsServer)
                {
                    return;
                }

                syncedHunterMeter.Value = 0f;
                return;
            }

            localHunterMeter = 0f;
        }

        public void SetGrabbed(bool grabbed)
        {
            if (isGrabbed == grabbed)
            {
                return;
            }

            isGrabbed = grabbed;

            if (rb == null)
            {
                rb = GetComponent<Rigidbody>();
            }

            if (rb == null)
            {
                return;
            }

            if (grabbed)
            {
                if (!hasGrabbedPhysicsState)
                {
                    grabbedWasKinematic = rb.isKinematic;
                    grabbedWasGravity = rb.useGravity;
                    hasGrabbedPhysicsState = true;
                }

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            else
            {
                if (hasGrabbedPhysicsState)
                {
                    rb.isKinematic = grabbedWasKinematic;
                    rb.useGravity = grabbedWasGravity;
                    hasGrabbedPhysicsState = false;
                }
            }
        }

        public bool IsGrabbed => isGrabbed;
        public bool IsIncapacitated => isIncapacitated;
        public bool IsInteractionLocked => isGrabbed || isIncapacitated;

        public void SetIncapacitated(bool incapacitated)
        {
            isIncapacitated = incapacitated;
        }

    }
}

