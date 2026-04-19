using System.Collections.Generic;
using UnityEngine;

namespace WalaPaNameHehe
{
    public class PlayerRagdollController : MonoBehaviour
    {
        [SerializeField] private bool autoFindParts = true;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private Rigidbody mainRigidbody;
        [SerializeField] private Collider mainCollider;
        [SerializeField] private Rigidbody[] ragdollBodies;
        [SerializeField] private Collider[] ragdollColliders;
        [SerializeField] private Transform ragdollHead;
        [SerializeField] private Transform ragdollRoot;
        [SerializeField] private Transform visualRoot;
        private Vector3 visualRootLocalPos;
        private Quaternion visualRootLocalRot;
        private bool hasVisualRootCache;
        [SerializeField] private string getUpTrigger = "GetUp";
        [SerializeField] private float getUpBlendSeconds = 0.25f;

        private Transform holdPoint;
        private bool isHolding;
        private bool isDownedRagdoll;
        private bool hasDownedState;
        [SerializeField] private Rigidbody holdRigidbody;
        private FixedJoint holdJoint;
        private Vector3 lastRagdollRootPos;
        private bool hasLastRagdollRootPos;
        private Transform cameraPivot;
        private Vector3 cameraPivotLocalPos;
        private Quaternion cameraPivotLocalRot;
        private bool hasStoredCameraPivot;
        [SerializeField] private string getUpStateName = "Getting Up";
        private bool isBlendingGetUp;
        private float getUpBlendStartTime;
        private Transform[] blendBones;
        private Vector3[] blendRagdollLocalPos;
        private Quaternion[] blendRagdollLocalRot;
        private readonly List<Camera> cachedLocalCameras = new List<Camera>();
        private readonly Dictionary<Camera, bool> cachedLocalCameraStates = new Dictionary<Camera, bool>();
        private readonly List<AudioListener> cachedLocalListeners = new List<AudioListener>();
        private readonly Dictionary<AudioListener, bool> cachedLocalListenerStates = new Dictionary<AudioListener, bool>();
        private Camera externalCamera;
        private AudioListener externalListener;
        private bool externalCameraActive;
        private Transform cachedGetUpHead;
        private bool hasCachedGetUpHead;
        private Transform cachedCameraParent;
        private Vector3 cachedCameraLocalPos;
        private Quaternion cachedCameraLocalRot;
        private bool hasGetUpCameraOverride;
        private bool hasGetUpSuppression;
        private bool wasFollowingCamera;
        [Header("Camera Return")]
        [SerializeField] private float cameraPivotReturnSeconds = 0.15f;
        private bool isReturningCameraPivot;
        private float cameraPivotReturnStartTime;
        private Vector3 cameraPivotReturnFromLocalPos;
        private Quaternion cameraPivotReturnFromLocalRot;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovement>();
            }

            if (mainRigidbody == null)
            {
                mainRigidbody = GetComponent<Rigidbody>();
            }

            if (mainCollider == null)
            {
                mainCollider = GetComponent<Collider>();
            }

            if (autoFindParts)
            {
                if (ragdollBodies == null || ragdollBodies.Length == 0)
                {
                    ragdollBodies = GetComponentsInChildren<Rigidbody>(true);
                }

                if (ragdollColliders == null || ragdollColliders.Length == 0)
                {
                    ragdollColliders = GetComponentsInChildren<Collider>(true);
                }
            }
            ResolveHoldRigidbody();

            if (ragdollHead == null)
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
                    if (n.Contains("head"))
                    {
                        ragdollHead = t;
                        break;
                    }
                }
            }

            if (ragdollRoot == null)
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
                    if (n.Contains("hip") || n.Contains("pelvis"))
                    {
                        ragdollRoot = t;
                        break;
                    }
                }
            }

            if (visualRoot == null && animator != null)
            {
                visualRoot = animator.transform;
            }
            if (visualRoot != null && !hasVisualRootCache)
            {
                visualRootLocalPos = visualRoot.localPosition;
                visualRootLocalRot = visualRoot.localRotation;
                hasVisualRootCache = true;
            }
        }

        private void Start()
        {
            SetRagdollActive(false, false);
        }

        private void LateUpdate()
        {
            if (externalCameraActive)
            {
                if (externalCamera == null || !externalCamera.isActiveAndEnabled)
                {
                    ClearExternalCamera();
                }
            }

            // No manual positioning; joint keeps the ragdoll attached during hold.
            if (isDownedRagdoll && ragdollRoot != null)
            {
                lastRagdollRootPos = ragdollRoot.position;
                hasLastRagdollRootPos = true;
            }
            else if (isDownedRagdoll)
            {
                if (TryGetRagdollFallbackPosition(out Vector3 fallbackPos))
                {
                    lastRagdollRootPos = fallbackPos;
                    hasLastRagdollRootPos = true;
                }
            }

            bool followGetUp = ShouldFollowGetUpCamera();
            if (followGetUp)
            {
                ApplyGetUpSuppression(true);
            }
            else
            {
                ApplyGetUpSuppression(false);
            }
            bool followCamera = isHolding || isDownedRagdoll || followGetUp;
            if (!followCamera)
            {
                if (wasFollowingCamera)
                {
                    RestoreGetUpCameraOverride();
                    ApplyGetUpSuppression(false);
                    BeginCameraPivotReturn();
                    wasFollowingCamera = false;
                }

                if (!isReturningCameraPivot)
                {
                    return;
                }

                UpdateCameraPivotReturn();
                return;
            }
            wasFollowingCamera = true;
            isReturningCameraPivot = false;

            if (playerMovement == null)
            {
                return;
            }

            if (playerMovement.IsOwner &&
                (Cursor.visible || Cursor.lockState != CursorLockMode.Locked) &&
                !isHolding &&
                !isDownedRagdoll &&
                !followGetUp)
            {
                return;
            }

            if (cameraPivot == null)
            {
                Transform pivot = playerMovement.CameraPivot;
                if (pivot != null && (pivot == playerMovement.transform || pivot.IsChildOf(playerMovement.transform)))
                {
                    cameraPivot = pivot;
                }
            }
            if (cameraPivot != null && !hasStoredCameraPivot)
            {
                cameraPivotLocalPos = cameraPivot.localPosition;
                cameraPivotLocalRot = cameraPivot.localRotation;
                hasStoredCameraPivot = true;
            }

            Transform headTarget = null;
            if (isHolding || isDownedRagdoll)
            {
                headTarget = ragdollHead;
            }
            else if (followGetUp)
            {
                EnsureGetUpCameraOverride();
            }

              if (cameraPivot != null && headTarget != null)
              {
                  Vector3 targetPos = headTarget.position;
                  int groundMask = LayerMask.GetMask("Ground");
                  if (groundMask == 0)
                  {
                      int ignoreMask = LayerMask.GetMask("Player");
                      groundMask = ~ignoreMask;
                  }

                  Vector3 rayStart = targetPos + Vector3.up * 2f;
                  if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 20f, groundMask, QueryTriggerInteraction.Ignore))
                  {
                      float minY = hit.point.y + 0.05f;
                      if (targetPos.y < minY)
                      {
                          targetPos.y = minY;
                     }
                 }

                  cameraPivot.position = targetPos;
                  cameraPivot.rotation = headTarget.rotation;
              }

            if (isBlendingGetUp)
            {
                BlendGetUpPose();
            }
        }

        private void BeginCameraPivotReturn()
        {
            if (cameraPivot == null || !hasStoredCameraPivot)
            {
                isReturningCameraPivot = false;
                return;
            }

            cameraPivotReturnFromLocalPos = cameraPivot.localPosition;
            cameraPivotReturnFromLocalRot = cameraPivot.localRotation;
            cameraPivotReturnStartTime = Time.unscaledTime;
            isReturningCameraPivot = true;
        }

        private void UpdateCameraPivotReturn()
        {
            if (!isReturningCameraPivot || cameraPivot == null || !hasStoredCameraPivot)
            {
                isReturningCameraPivot = false;
                return;
            }

            float duration = Mathf.Max(0.0001f, cameraPivotReturnSeconds);
            float t = Mathf.Clamp01((Time.unscaledTime - cameraPivotReturnStartTime) / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            cameraPivot.localPosition = Vector3.Lerp(cameraPivotReturnFromLocalPos, cameraPivotLocalPos, eased);
            cameraPivot.localRotation = Quaternion.Slerp(cameraPivotReturnFromLocalRot, cameraPivotLocalRot, eased);

            if (t >= 1f)
            {
                isReturningCameraPivot = false;
            }
        }


        public void BeginHold(Transform bitePoint)
        {
            holdPoint = bitePoint;
            isHolding = holdPoint != null;
            SetRagdollActive(true, false);
            AttachToHoldPoint();
        }

        public void EndHold(bool keepRagdoll)
        {
            isHolding = false;
            holdPoint = null;
            DetachFromHoldPoint();
            RestoreCameraPivot();
            ClearExternalCamera();
            SetRagdollActive(keepRagdoll, false);
        }

        public void ResetRagdoll()
        {
            isHolding = false;
            holdPoint = null;
            isDownedRagdoll = false;
            hasDownedState = false;
            DetachFromHoldPoint();
            RestoreCameraPivot();
            RestoreGetUpCameraOverride();
            ApplyGetUpSuppression(false);
            ClearExternalCamera();
            SetRagdollActive(false, false);
        }

        private void SetRagdollActive(bool active, bool holdKinematic)
        {
            if (playerMovement != null)
            {
                playerMovement.enabled = !active;
            }

            if (animator != null)
            {
                animator.enabled = !active;
            }

            if (mainRigidbody != null)
            {
                mainRigidbody.isKinematic = active;
                mainRigidbody.useGravity = !active;
            }

            if (mainCollider != null)
            {
                mainCollider.enabled = !active;
            }

            if (ragdollBodies != null)
            {
                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null || body == mainRigidbody)
                    {
                        continue;
                    }

                    bool kinematic = !active || holdKinematic;
                    body.isKinematic = kinematic;
                    body.useGravity = active && !holdKinematic;
                }
            }

            if (ragdollColliders != null)
            {
                for (int i = 0; i < ragdollColliders.Length; i++)
                {
                    Collider col = ragdollColliders[i];
                    if (col == null || col == mainCollider)
                    {
                        continue;
                    }

                    col.enabled = active;
                }
            }
        }

        public void SetDownedRagdoll(bool isDowned)
        {
            // Downed should ragdoll without attaching to a hold point.
            bool wasDowned = hasDownedState && isDownedRagdoll;

            if (!isDowned)
            {
                AlignRootToRagdoll();
                SnapRootToGround();
                ForceCapsuleGroundSnap();
                RestoreVisualRootLocal();
            }

            SetRagdollActive(isDowned, false);
            isDownedRagdoll = isDowned;
            hasDownedState = true;
            if (!isDowned)
            {
                if (wasDowned && animator != null && !string.IsNullOrWhiteSpace(getUpTrigger))
                {
                    animator.SetTrigger(getUpTrigger);
                }
                BeginGetUpBlend();
                ClearExternalCamera();
            }
        }

        public void AlignAndSnapToGround()
        {
            AlignRootToRagdoll();
            SnapRootToGround();
        }

        public Transform GetRagdollRoot()
        {
            return ragdollRoot;
        }

        private void SnapRootToGround()
        {
            Transform root = ragdollRoot != null ? ragdollRoot : transform;
            if (mainCollider is CapsuleCollider capsule)
            {
                int ignoreMask = LayerMask.GetMask("Player");
                int groundMask = ~ignoreMask;
                float scaleX = Mathf.Abs(transform.lossyScale.x);
                float scaleY = Mathf.Abs(transform.lossyScale.y);
                float scaleZ = Mathf.Abs(transform.lossyScale.z);
                float radius = capsule.radius * Mathf.Max(scaleX, scaleZ);
                float halfHeight = Mathf.Max(radius, capsule.height * scaleY * 0.5f);
                float cylinderHalf = Mathf.Max(0f, halfHeight - radius);
                Vector3 centerOffset = transform.TransformPoint(capsule.center) - transform.position;

                Vector3 startCenter = new Vector3(root.position.x, root.position.y + 2f, root.position.z) + new Vector3(centerOffset.x, 0f, centerOffset.z);
                Vector3 p1 = startCenter + Vector3.up * cylinderHalf;
                Vector3 p2 = startCenter - Vector3.up * cylinderHalf;
                if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, 10f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 desiredCenter = startCenter + Vector3.down * hit.distance;
                    transform.position = desiredCenter - centerOffset;
                }
                return;
            }

            int ignoreMaskRay = LayerMask.GetMask("Player");
            int groundMaskRay = ~ignoreMaskRay;
            Vector3 origin = root.position + Vector3.up * 1.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hitRay, 4f, groundMaskRay, QueryTriggerInteraction.Ignore))
            {
                float bottomY = GetColliderBottomY();
                float delta = hitRay.point.y - bottomY;
                root.position += Vector3.up * delta;
            }
        }

        private void ForceCapsuleGroundSnap()
        {
            if (mainCollider is not CapsuleCollider capsule)
            {
                return;
            }

            int ignoreMask = LayerMask.GetMask("Player");
            int groundMask = ~ignoreMask;
            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleY = Mathf.Abs(transform.lossyScale.y);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float radius = capsule.radius * Mathf.Max(scaleX, scaleZ);
            float halfHeight = Mathf.Max(radius, capsule.height * scaleY * 0.5f);
            float cylinderHalf = Mathf.Max(0f, halfHeight - radius);
            Vector3 centerOffset = transform.TransformPoint(capsule.center) - transform.position;

            Vector3 startCenter = new Vector3(transform.position.x, transform.position.y + 5f, transform.position.z) + new Vector3(centerOffset.x, 0f, centerOffset.z);
            Vector3 p1 = startCenter + Vector3.up * cylinderHalf;
            Vector3 p2 = startCenter - Vector3.up * cylinderHalf;
            if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, 15f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 desiredCenter = startCenter + Vector3.down * hit.distance;
                transform.position = desiredCenter - centerOffset;
            }

            if (mainRigidbody != null)
            {
                mainRigidbody.linearVelocity = Vector3.zero;
                mainRigidbody.angularVelocity = Vector3.zero;
            }
        }

        private float GetColliderBottomY()
        {
            if (mainCollider == null)
            {
                return transform.position.y;
            }

            if (mainCollider is CapsuleCollider capsule)
            {
                Vector3 center = transform.TransformPoint(capsule.center);
                float scaleX = Mathf.Abs(transform.lossyScale.x);
                float scaleY = Mathf.Abs(transform.lossyScale.y);
                float scaleZ = Mathf.Abs(transform.lossyScale.z);
                float radius = capsule.radius * Mathf.Max(scaleX, scaleZ);
                float halfHeight = Mathf.Max(radius, (capsule.height * scaleY * 0.5f));
                return center.y - halfHeight;
            }

            return mainCollider.bounds.min.y;
        }

        public void SnapRagdollTo(Transform target)
        {
            if (target == null)
            {
                return;
            }

            Transform root = ragdollRoot != null ? ragdollRoot : transform;
            Vector3 delta = target.position - root.position;

            if (ragdollBodies != null)
            {
                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null || body == mainRigidbody)
                    {
                        continue;
                    }

                    Vector3 newPos = body.position + delta;
                    body.position = newPos;
                    body.rotation = target.rotation;
                }
            }

            root.position = target.position;
            root.rotation = target.rotation;
        }

        public void SetExternalCamera(Camera camera)
        {
            if (playerMovement == null || !playerMovement.IsOwner)
            {
                return;
            }

            if (camera == null)
            {
                return;
            }

            if (externalCameraActive && externalCamera == camera)
            {
                return;
            }

            CacheAndDisableLocalView();

            externalCamera = camera;
            externalListener = externalCamera.GetComponent<AudioListener>();

            externalCamera.enabled = true;
            if (externalListener != null)
            {
                externalListener.enabled = true;
            }

            externalCameraActive = true;
        }

        public void ClearExternalCamera()
        {
            if (!externalCameraActive)
            {
                return;
            }

            if (externalCamera != null)
            {
                externalCamera.enabled = false;
            }

            if (externalListener != null)
            {
                externalListener.enabled = false;
            }

            externalCamera = null;
            externalListener = null;
            externalCameraActive = false;

            RestoreLocalView();
        }

        private void CacheAndDisableLocalView()
        {
            cachedLocalCameras.Clear();
            cachedLocalCameraStates.Clear();
            cachedLocalListeners.Clear();
            cachedLocalListenerStates.Clear();

            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null)
                {
                    continue;
                }

                cachedLocalCameras.Add(cam);
                cachedLocalCameraStates[cam] = cam.enabled;
                cam.enabled = false;
            }

            AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null)
                {
                    continue;
                }

                cachedLocalListeners.Add(listener);
                cachedLocalListenerStates[listener] = listener.enabled;
                listener.enabled = false;
            }
        }

        private void RestoreLocalView()
        {
            for (int i = 0; i < cachedLocalCameras.Count; i++)
            {
                Camera cam = cachedLocalCameras[i];
                if (cam == null)
                {
                    continue;
                }

                if (cachedLocalCameraStates.TryGetValue(cam, out bool enabled))
                {
                    cam.enabled = enabled;
                }
            }

            for (int i = 0; i < cachedLocalListeners.Count; i++)
            {
                AudioListener listener = cachedLocalListeners[i];
                if (listener == null)
                {
                    continue;
                }

                if (cachedLocalListenerStates.TryGetValue(listener, out bool enabled))
                {
                    listener.enabled = enabled;
                }
            }

            cachedLocalCameras.Clear();
            cachedLocalCameraStates.Clear();
            cachedLocalListeners.Clear();
            cachedLocalListenerStates.Clear();
        }

        public void ApplyRagdollImpulse(Vector3 impulse)
        {
            if (impulse.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            bool applied = false;
            if (ragdollBodies != null)
            {
                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null || body == mainRigidbody || body.isKinematic)
                    {
                        continue;
                    }

                    body.AddForce(impulse, ForceMode.Impulse);
                    applied = true;
                }
            }

            if (!applied && mainRigidbody != null && !mainRigidbody.isKinematic)
            {
                mainRigidbody.AddForce(impulse, ForceMode.Impulse);
            }
        }
        private bool ShouldFollowGetUpCamera()
        {
            if (animator == null || string.IsNullOrWhiteSpace(getUpStateName))
            {
                return false;
            }

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName(getUpStateName))
            {
                return true;
            }
            return false;
        }

        public bool IsInGetUpState()
        {
            if (animator == null || string.IsNullOrWhiteSpace(getUpStateName))
            {
                return false;
            }

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.IsName(getUpStateName);
        }

        public bool IsGetUpTransitionActive()
        {
            return isBlendingGetUp || hasGetUpCameraOverride || hasGetUpSuppression;
        }

        private void BeginGetUpBlend()
        {
            float blendSeconds = Mathf.Max(0f, getUpBlendSeconds);
            if (blendSeconds <= 0f)
            {
                isBlendingGetUp = false;
                return;
            }

            if (ragdollBodies == null || ragdollBodies.Length == 0)
            {
                isBlendingGetUp = false;
                return;
            }

            int count = 0;
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                if (ragdollBodies[i] != null && ragdollBodies[i] != mainRigidbody)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                isBlendingGetUp = false;
                return;
            }

            blendBones = new Transform[count];
            blendRagdollLocalPos = new Vector3[count];
            blendRagdollLocalRot = new Quaternion[count];

            int index = 0;
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null || body == mainRigidbody)
                {
                    continue;
                }

                Transform t = body.transform;
                blendBones[index] = t;
                blendRagdollLocalPos[index] = t.localPosition;
                blendRagdollLocalRot[index] = t.localRotation;
                index++;
            }

            getUpBlendStartTime = Time.time;
            isBlendingGetUp = true;
        }

        private void BlendGetUpPose()
        {
            if (blendBones == null || blendRagdollLocalPos == null || blendRagdollLocalRot == null)
            {
                isBlendingGetUp = false;
                return;
            }

            float duration = Mathf.Max(0.0001f, getUpBlendSeconds);
            float t = Mathf.Clamp01((Time.time - getUpBlendStartTime) / duration);

            for (int i = 0; i < blendBones.Length; i++)
            {
                Transform bone = blendBones[i];
                if (bone == null)
                {
                    continue;
                }

                Vector3 animPos = bone.localPosition;
                Quaternion animRot = bone.localRotation;
                bone.localPosition = Vector3.Lerp(blendRagdollLocalPos[i], animPos, t);
                bone.localRotation = Quaternion.Slerp(blendRagdollLocalRot[i], animRot, t);
            }

            if (t >= 1f)
            {
                isBlendingGetUp = false;
            }
        }

        private Transform GetGetUpHead()
        {
            if (hasCachedGetUpHead)
            {
                return cachedGetUpHead;
            }

            if (animator != null && animator.isHuman)
            {
                cachedGetUpHead = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (cachedGetUpHead == null)
            {
                cachedGetUpHead = ragdollHead != null ? ragdollHead : visualRoot;
            }

            hasCachedGetUpHead = true;
            return cachedGetUpHead;
        }

        private void EnsureGetUpCameraOverride()
        {
            if (cameraPivot == null)
            {
                return;
            }

            Transform head = GetGetUpHead();
            if (head == null)
            {
                return;
            }

            if (!hasGetUpCameraOverride)
            {
                cachedCameraParent = cameraPivot.parent;
                cachedCameraLocalPos = hasStoredCameraPivot ? cameraPivotLocalPos : cameraPivot.localPosition;
                cachedCameraLocalRot = hasStoredCameraPivot ? cameraPivotLocalRot : cameraPivot.localRotation;
                hasGetUpCameraOverride = true;
            }

            cameraPivot.SetParent(head, false);
            cameraPivot.localPosition = Vector3.zero;
            cameraPivot.localRotation = Quaternion.identity;
        }

        private void RestoreGetUpCameraOverride()
        {
            if (!hasGetUpCameraOverride || cameraPivot == null)
            {
                return;
            }

            cameraPivot.SetParent(cachedCameraParent, false);
            cameraPivot.localPosition = cachedCameraLocalPos;
            cameraPivot.localRotation = cachedCameraLocalRot;
            hasGetUpCameraOverride = false;
        }

        private void ApplyGetUpSuppression(bool shouldSuppress)
        {
            if (playerMovement == null)
            {
                return;
            }

            if (shouldSuppress)
            {
                if (hasGetUpSuppression)
                {
                    return;
                }

                playerMovement.SetMovementSuppressed(true);
                hasGetUpSuppression = true;
                return;
            }

            if (!hasGetUpSuppression)
            {
                return;
            }

            playerMovement.SetMovementSuppressed(false);
            hasGetUpSuppression = false;
        }


        private void AlignRootToRagdoll()
        {
            Transform rootTarget = ragdollRoot != null ? ragdollRoot : (holdRigidbody != null ? holdRigidbody.transform : null);
            Vector3 rootPos = transform.position;

            if (TryGetRagdollFallbackPosition(out Vector3 fallbackPos) && IsValidRagdollPosition(fallbackPos))
            {
                rootPos = fallbackPos;
            }
            else if (rootTarget != null && IsValidRagdollPosition(rootTarget.position))
            {
                rootPos = rootTarget.position;
            }
            else if (hasLastRagdollRootPos && IsValidRagdollPosition(lastRagdollRootPos))
            {
                rootPos = lastRagdollRootPos;
            }

            transform.position = rootPos;

            // Snap to ground so capsule doesn't sink.
            if (mainCollider == null)
            {
                return;
            }

            int ignoreMask = LayerMask.GetMask("Player");
            int groundMask = ~ignoreMask;
            if (TryGetLowestRagdollPoint(out Vector3 lowest))
            {
                Vector3 origin = lowest + Vector3.up * 1.5f;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    float delta = hit.point.y - lowest.y;
                    transform.position += Vector3.up * delta;
                    SyncVisualRootToHip(rootTarget);
                    return;
                }
            }

            Vector3 fallbackOrigin = transform.position + Vector3.up * 2f;
            if (!Physics.Raycast(fallbackOrigin, Vector3.down, out RaycastHit fallbackHit, 6f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            float fallbackDelta = fallbackHit.point.y - mainCollider.bounds.min.y;
            transform.position += Vector3.up * fallbackDelta;
            SyncVisualRootToHip(rootTarget);
        }

        private bool TryGetRagdollFallbackPosition(out Vector3 position)
        {
            position = Vector3.zero;

            return TryGetLowestRagdollPoint(out position);
        }

        private bool TryGetLowestRagdollPoint(out Vector3 position)
        {
            position = Vector3.zero;
            if (ragdollBodies == null || ragdollBodies.Length == 0)
            {
                return false;
            }

            bool found = false;
            float bestY = float.MaxValue;
            float currentSqr = transform.position.sqrMagnitude;
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody body = ragdollBodies[i];
                if (body == null || body == mainRigidbody)
                {
                    continue;
                }

                Vector3 p = body.position;
                if (p == Vector3.zero && currentSqr > 0.01f)
                {
                    continue;
                }
                if (!found || p.y < bestY)
                {
                    bestY = p.y;
                    position = p;
                    found = true;
                }
            }

            return found;
        }

        private bool IsValidRagdollPosition(Vector3 position)
        {
            float currentSqr = transform.position.sqrMagnitude;
            if (position == Vector3.zero && currentSqr > 0.01f)
            {
                return false;
            }

            if (currentSqr > 0.01f && (position - transform.position).sqrMagnitude > 2500f)
            {
                return false;
            }

            return true;
        }

        private void SyncVisualRootToHip(Transform rootTarget)
        {
            if (visualRoot == null || rootTarget == null)
            {
                return;
            }

            Vector3 delta = rootTarget.position - visualRoot.position;
            visualRoot.position += delta;
        }

        private void RestoreVisualRootLocal()
        {
            if (!hasVisualRootCache || visualRoot == null)
            {
                return;
            }

            visualRoot.localPosition = visualRootLocalPos;
            visualRoot.localRotation = visualRootLocalRot;
        }

        private void ResolveHoldRigidbody()
        {
            if (holdRigidbody != null)
            {
                return;
            }

            if (ragdollBodies != null && ragdollBodies.Length > 0)
            {
                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null)
                    {
                        continue;
                    }

                    string n = body.name.ToLowerInvariant();
                    if (n.Contains("neck") || n.Contains("upper") || n.Contains("chest") || n.Contains("spine") || n.Contains("head"))
                    {
                        holdRigidbody = body;
                        break;
                    }
                }

                if (holdRigidbody != null)
                {
                    return;
                }

                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null)
                    {
                        continue;
                    }

                    string n = body.name.ToLowerInvariant();
                    if (n.Contains("hip") || n.Contains("pelvis"))
                    {
                        holdRigidbody = body;
                        break;
                    }
                }

                if (holdRigidbody != null)
                {
                    return;
                }

                for (int i = 0; i < ragdollBodies.Length; i++)
                {
                    Rigidbody body = ragdollBodies[i];
                    if (body == null || body == mainRigidbody)
                    {
                        continue;
                    }

                    holdRigidbody = body;
                    break;
                }
            }

            if (holdRigidbody == null)
            {
                holdRigidbody = mainRigidbody;
            }
        }

        private void AttachToHoldPoint()
        {
            if (!isHolding || holdPoint == null)
            {
                return;
            }

            ResolveHoldRigidbody();
            if (holdRigidbody == null)
            {
                return;
            }

            Rigidbody biteBody = holdPoint.GetComponent<Rigidbody>();
            if (biteBody == null)
            {
                biteBody = holdPoint.GetComponentInParent<Rigidbody>();
            }

            if (biteBody == null)
            {
                biteBody = holdPoint.gameObject.GetComponent<Rigidbody>();
                if (biteBody == null)
                {
                    biteBody = holdPoint.gameObject.AddComponent<Rigidbody>();
                    biteBody.isKinematic = true;
                    biteBody.useGravity = false;
                }
            }

            // Snap the hold body to the bite point so the joint doesn't keep the old offset.
            holdRigidbody.position = holdPoint.position;
            holdRigidbody.rotation = holdPoint.rotation;
            holdRigidbody.linearVelocity = Vector3.zero;
            holdRigidbody.angularVelocity = Vector3.zero;

            if (holdJoint != null)
            {
                Destroy(holdJoint);
            }

            holdJoint = holdRigidbody.gameObject.AddComponent<FixedJoint>();
            holdJoint.connectedBody = biteBody;
            holdJoint.autoConfigureConnectedAnchor = false;
            holdJoint.anchor = Vector3.zero;
            holdJoint.connectedAnchor = biteBody.transform.InverseTransformPoint(holdPoint.position);
            holdJoint.enableCollision = false;
            holdJoint.breakForce = Mathf.Infinity;
            holdJoint.breakTorque = Mathf.Infinity;
        }

        private void DetachFromHoldPoint()
        {
            if (holdJoint != null)
            {
                Destroy(holdJoint);
                holdJoint = null;
            }
        }

        private void RestoreCameraPivot()
        {
            if (cameraPivot == null || !hasStoredCameraPivot)
            {
                return;
            }

            cameraPivot.localPosition = cameraPivotLocalPos;
            cameraPivot.localRotation = cameraPivotLocalRot;
        }
    }
}
