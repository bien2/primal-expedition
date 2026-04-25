using System.Collections;
using UnityEngine;
using Unity.Netcode;
using WalaPaNameHehe.Multiplayer;
using UnityEngine.Serialization;

namespace WalaPaNameHehe
{
    public class PlayerHitHandler : NetworkBehaviour
    {
    public enum HitResult
    {
        None,
        Instakill,
        DownState
    }

        [Header("Down State")]
        [SerializeField] private float downedMoveMultiplier = 0.35f;
        [FormerlySerializedAs("autoRecoverSeconds")]
        [SerializeField] private float downStateDurationSeconds = 0f;

        private readonly NetworkVariable<bool> syncedIsDowned = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private PlayerMovement playerMovement;
        private PlayerRagdollController ragdollController;
        private PlayerDeathBlackout deathBlackout;
        private Coroutine biteHoldRoutine;
        private bool isGrabbedHold;
        private bool isGrabHoldActive;
        private bool isRagdollHoldActive;
        private bool lastHoldUsedRagdoll;
        private Transform grabHoldPoint;
        private bool pendingDownStateOnRelease;
        private bool pendingDownStateOnImpact;
        private Transform pendingDownStateIgnoreRoot;
        private InventorySystem inventorySystem;
        private WeaponDrone weaponDrone;
        private Coroutine autoRecoverRoutine;
        private Coroutine pendingInstakillRoutine;
        private bool downedSubscribed;

        public bool IsDowned => syncedIsDowned.Value;

        private void Awake()
        {
            playerMovement = GetComponent<PlayerMovement>();
            ragdollController = GetComponent<PlayerRagdollController>();
            if (ragdollController == null)
            {
                ragdollController = GetComponentInChildren<PlayerRagdollController>(true);
            }
            deathBlackout = GetComponent<PlayerDeathBlackout>();
            if (deathBlackout == null)
            {
                deathBlackout = GetComponentInChildren<PlayerDeathBlackout>(true);
            }

            SubscribeDownedChanged();
            ApplyDownedState(syncedIsDowned.Value);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyDownedState(syncedIsDowned.Value);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            UnsubscribeDownedChanged();
        }

        private void Update()
        {
            if (syncedIsDowned.Value && playerMovement != null && playerMovement.IsDead && IsAuthoritative())
            {
                SetDowned(false);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!pendingDownStateOnImpact)
            {
                return;
            }

            if (collision == null || collision.contactCount <= 0)
            {
                return;
            }

            if (pendingDownStateIgnoreRoot != null)
            {
                Transform otherRoot = collision.collider != null ? collision.collider.transform.root : collision.transform.root;
                if (otherRoot == pendingDownStateIgnoreRoot)
                {
                    return;
                }
            }

            if (IsNetworkActive() && !IsOwner && !IsServer)
            {
                return;
            }

            if (playerMovement != null && playerMovement.IsGrabbed)
            {
                return;
            }

            pendingDownStateOnImpact = false;
            pendingDownStateIgnoreRoot = null;
            ApplyTemporaryDownStateNow(downStateDurationSeconds);
        }

        private bool IsAuthoritative()
        {
            return !IsNetworkActive() || IsServer;
        }

        private void SubscribeDownedChanged()
        {
            if (downedSubscribed)
            {
                return;
            }

            syncedIsDowned.OnValueChanged += HandleDownedChanged;
            downedSubscribed = true;
        }

        private void UnsubscribeDownedChanged()
        {
            if (!downedSubscribed)
            {
                return;
            }

            syncedIsDowned.OnValueChanged -= HandleDownedChanged;
            downedSubscribed = false;
        }

        private void SetDowned(bool isDowned)
        {
            if (syncedIsDowned.Value == isDowned)
            {
                return;
            }

            syncedIsDowned.Value = isDowned;
            if (!IsNetworkActive())
            {
                ApplyDownedState(isDowned);
            }
        }

        private void HandleDownedChanged(bool previous, bool next)
        {
            ApplyDownedState(next);
        }

        private void ApplyDownedState(bool isDowned)
        {
            if (playerMovement == null)
            {
                return;
            }

            if (isDowned)
            {
                StartAutoRecoverIfNeeded();
            }
            else
            {
                StopAutoRecover();
            }

            if (isDowned)
            {
                playerMovement.SetExternalSpeedMultiplier(Mathf.Clamp(downedMoveMultiplier, 0.05f, 1f));
            }
            else
            {
                playerMovement.SetExternalSpeedMultiplier(1f);
            }

            playerMovement.SetSprintSuppressed(isDowned);
            UpdateIncapacitationLocal();
        }

        public bool ServerApplyHit(HitResult hitResult)
        {
            if (IsNetworkActive() && !IsServer)
            {
                return false;
            }

            if (playerMovement == null)
            {
                return false;
            }

            if (hitResult == HitResult.Instakill)
            {
                Debug.Log($"PlayerHitHandler: Instakill applied to '{name}'.");
                if (syncedIsDowned.Value)
                {
                    SetDowned(false);
                }
                return playerMovement.ServerKill();
            }

            if (playerMovement.IsDead || syncedIsDowned.Value)
            {
                return false;
            }

            SetDowned(true);
            WalaPaNameHehe.Multiplayer.HunterMeterManager.Instance?.ReportKnockdown(playerMovement);
            return true;
        }

        public bool ServerApplyInstakillWithRagdoll(Vector3 impulse, bool hasAttackerNetworkId = false, ulong attackerNetworkId = 0, Transform attackerTransform = null)
        {
            if (IsNetworkActive() && !IsServer)
            {
                return false;
            }

            if (playerMovement == null)
            {
                return false;
            }

            if (syncedIsDowned.Value)
            {
                SetDowned(false);
            }

            if (pendingInstakillRoutine != null)
            {
                StopCoroutine(pendingInstakillRoutine);
                pendingInstakillRoutine = null;
            }

            if (ragdollController != null)
            {
                ragdollController.SetDownedRagdoll(true);
                ragdollController.ApplyRagdollImpulse(impulse);
            }

            if (IsOwner)
            {
                Camera killCamera = ResolveAttackerKillCamera(hasAttackerNetworkId, attackerNetworkId, attackerTransform);
                ragdollController?.SetExternalCamera(killCamera);
                if (killCamera != null)
                {
                    ragdollController?.SnapRagdollTo(killCamera.transform);
                }
            }

            if (IsNetworkActive() && IsServer)
            {
                if (hasAttackerNetworkId && attackerNetworkId != 0)
                {
                    playerMovement.ServerSetExternalPov(attackerNetworkId);
                }
                ApplyDeathRagdollClientRpc(impulse, hasAttackerNetworkId, attackerNetworkId);
            }
            return playerMovement.ServerKill();
        }

        private void StartAutoRecoverIfNeeded()
        {
            if (!IsAuthoritative())
            {
                return;
            }

            float duration = Mathf.Max(0f, downStateDurationSeconds);
            if (duration <= 0f)
            {
                return;
            }

            StartAutoRecoverForDuration(duration);
        }

        private void StartAutoRecoverForDuration(float durationSeconds)
        {
            if (!IsAuthoritative())
            {
                return;
            }

            float duration = Mathf.Max(0f, durationSeconds);
            if (duration <= 0f)
            {
                return;
            }

            if (autoRecoverRoutine != null)
            {
                StopCoroutine(autoRecoverRoutine);
            }

            autoRecoverRoutine = StartCoroutine(AutoRecoverRoutine(duration));
        }

        private void StopAutoRecover()
        {
            if (autoRecoverRoutine != null)
            {
                StopCoroutine(autoRecoverRoutine);
                autoRecoverRoutine = null;
            }
        }

        private IEnumerator AutoRecoverRoutine(float durationSeconds)
        {
            yield return new WaitForSeconds(durationSeconds);

            if (!IsAuthoritative())
            {
                autoRecoverRoutine = null;
                yield break;
            }

            if (syncedIsDowned.Value)
            {
                SetDowned(false);
            }

            autoRecoverRoutine = null;
        }

        public void ServerBeginBiteHold(ulong dinoNetworkId, string bitePointPath, float holdSeconds, HitResult finalResult)
        {
            if (IsNetworkActive() && !IsServer)
            {
                return;
            }

            if (biteHoldRoutine != null)
            {
                StopCoroutine(biteHoldRoutine);
            }

            biteHoldRoutine = StartCoroutine(BiteHoldRoutine(dinoNetworkId, bitePointPath, holdSeconds, finalResult, true));
        }

        public void ServerBeginGrabHold(ulong dinoNetworkId, string bitePointPath, float holdSeconds, bool applyDownStateOnRelease = false)
        {
            if (IsNetworkActive() && !IsServer)
            {
                return;
            }

            if (biteHoldRoutine != null)
            {
                StopCoroutine(biteHoldRoutine);
            }

            biteHoldRoutine = StartCoroutine(BiteHoldRoutine(dinoNetworkId, bitePointPath, holdSeconds, HitResult.None, false, applyDownStateOnRelease));
        }

        public void ServerEndBiteHold()
        {
            if (IsNetworkActive() && !IsServer)
            {
                return;
        }

        if (biteHoldRoutine != null)
        {
            StopCoroutine(biteHoldRoutine);
            biteHoldRoutine = null;
        }

            EndBiteHoldClientRpc(false);
        }

        private IEnumerator BiteHoldRoutine(ulong dinoNetworkId, string bitePointPath, float holdSeconds, HitResult finalResult, bool useRagdoll, bool applyDownStateOnRelease = false)
        {
            BeginBiteHoldClientRpc(dinoNetworkId, bitePointPath, useRagdoll, applyDownStateOnRelease);

            float duration = Mathf.Max(0.05f, holdSeconds);
            yield return new WaitForSeconds(duration);

            EndBiteHoldClientRpc(finalResult == HitResult.Instakill);
            biteHoldRoutine = null;

            if (finalResult == HitResult.Instakill)
            {
                Debug.Log($"PlayerHitHandler: Instakill resolved for '{name}' after bite hold.");
                if (syncedIsDowned.Value)
                {
                    SetDowned(false);
                }

                if (playerMovement != null)
            {
                playerMovement.ServerKill();
            }
        }
        else if (finalResult == HitResult.DownState)
        {
            if (!syncedIsDowned.Value)
            {
                SetDowned(true);
            }
        }
        }

        [ClientRpc]
        private void BeginBiteHoldClientRpc(ulong dinoNetworkId, string bitePointPath, bool useRagdoll, bool applyDownStateOnRelease)
        {
            Transform bitePoint = ResolveBitePoint(dinoNetworkId, bitePointPath);
            lastHoldUsedRagdoll = useRagdoll;
            if (useRagdoll)
            {
                isGrabHoldActive = false;
                isRagdollHoldActive = true;
                if (ragdollController != null)
                {
                    ragdollController.BeginHold(bitePoint);
                }
                UpdateIncapacitationLocal();
            }
            else
            {
                isRagdollHoldActive = false;
                grabHoldPoint = bitePoint;
                isGrabbedHold = grabHoldPoint != null;
                isGrabHoldActive = true;
                pendingDownStateOnRelease = applyDownStateOnRelease;
                pendingDownStateIgnoreRoot = applyDownStateOnRelease && bitePoint != null ? bitePoint.root : null;
                if (playerMovement != null)
                {
                    playerMovement.SetGrabbed(isGrabbedHold);
                }

                UpdateIncapacitationLocal();
            }
        }

        [ClientRpc]
        private void EndBiteHoldClientRpc(bool wasInstakill)
        {
            if (isGrabHoldActive)
            {
                ClearGrabHoldState();
                if (pendingDownStateOnRelease)
                {
                    pendingDownStateOnRelease = false;
                    pendingDownStateOnImpact = true;
                }
                return;
            }

            if (lastHoldUsedRagdoll)
            {
                if (ragdollController != null)
                {
                    ragdollController.EndHold(true);
                }

                isRagdollHoldActive = false;
                UpdateIncapacitationLocal();
            }
        }

        private void ApplyTemporaryDownStateNow(float durationSeconds)
        {
            float duration = Mathf.Max(0f, durationSeconds);
            if (duration <= 0f)
            {
                return;
            }

            if (IsNetworkActive())
            {
                if (!IsOwner)
                {
                    return;
                }

                RequestTemporaryDownStateServerRpc(duration);
                return;
            }

            if (!syncedIsDowned.Value)
            {
                SetDowned(true);
            }

            StartAutoRecoverForDuration(duration);
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestTemporaryDownStateServerRpc(float durationSeconds, ServerRpcParams rpcParams = default)
        {
            float duration = Mathf.Max(0f, durationSeconds);
            if (duration <= 0f)
            {
                return;
            }

            if (!syncedIsDowned.Value)
            {
                SetDowned(true);
            }

            StartAutoRecoverForDuration(duration);
        }

        private void ClearGrabHoldState()
        {
            isGrabbedHold = false;
            isGrabHoldActive = false;
            grabHoldPoint = null;
            if (playerMovement != null)
            {
                playerMovement.SetGrabbed(false);
            }
            UpdateIncapacitationLocal();
        }

        private void UpdateIncapacitationLocal()
        {
            if (playerMovement == null)
            {
                return;
            }

            bool shouldSuppress = syncedIsDowned.Value || isRagdollHoldActive || playerMovement.IsGrabbed;
            playerMovement.SetIncapacitated(shouldSuppress);
            ApplyIncapacitationSuppression(shouldSuppress);
        }

        private void ApplyIncapacitationSuppression(bool suppress)
        {
            if (!CoopGuard.IsLocalOwnerOrOffline(this))
            {
                return;
            }

            if (inventorySystem == null)
            {
                inventorySystem = GetComponent<InventorySystem>();
            }

            if (weaponDrone == null)
            {
                weaponDrone = GetComponent<WeaponDrone>();
            }

            if (inventorySystem != null)
            {
                inventorySystem.SetUiSuppressed(suppress);
            }

            if (suppress && weaponDrone != null)
            {
                weaponDrone.ForceRecallDueToGrab();
            }
        }

        private void SnapRootToRagdollOnly()
        {
            if (ragdollController == null || playerMovement == null)
            {
                return;
            }

            Transform ragdollRoot = ragdollController.GetRagdollRoot();
            if (ragdollRoot == null)
            {
                return;
            }

            Vector3 target = ragdollRoot.position;

            CapsuleCollider capsule = playerMovement.GetCapsuleCollider();
            if (capsule == null)
            {
                transform.position = target;
                return;
            }

            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleY = Mathf.Abs(transform.lossyScale.y);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float radius = capsule.radius * Mathf.Max(scaleX, scaleZ);
            float halfHeight = Mathf.Max(radius, capsule.height * scaleY * 0.5f);
            float cylinderHalf = Mathf.Max(0f, halfHeight - radius);
            Vector3 centerOffset = transform.TransformPoint(capsule.center) - transform.position;

            int ignoreMask = LayerMask.GetMask("Player");
            int groundMask = ~ignoreMask;
            Vector3 startCenter = new Vector3(target.x, target.y + 2f, target.z) + new Vector3(centerOffset.x, 0f, centerOffset.z);
            Vector3 p1 = startCenter + Vector3.up * cylinderHalf;
            Vector3 p2 = startCenter - Vector3.up * cylinderHalf;
            if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, 10f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 desiredCenter = startCenter + Vector3.down * hit.distance;
                transform.position = desiredCenter - centerOffset;
                return;
            }

            transform.position = target;
        }

        [ClientRpc]
        private void ApplyServerSnapToRagdollClientRpc(Vector3 position)
        {
            if (IsServer)
            {
                return;
            }

            transform.position = position;
        }

        private void ApplyServerSnapToRagdoll()
        {
            SnapRootToRagdollOnly();

            if (IsNetworkActive())
            {
                ApplyServerSnapToRagdollClientRpc(transform.position);
            }
        }

        [ClientRpc]
        private void ApplyDeathRagdollClientRpc(Vector3 impulse, bool hasAttackerNetworkId, ulong attackerNetworkId)
        {
            if (!IsServer)
            {
                if (ragdollController != null)
                {
                    ragdollController.SetDownedRagdoll(true);
                    ragdollController.ApplyRagdollImpulse(impulse);
                }
            }

            Camera killCameraAll = ResolveAttackerKillCamera(hasAttackerNetworkId, attackerNetworkId, null);
            if (killCameraAll != null)
            {
                ragdollController?.SnapRagdollTo(killCameraAll.transform);
            }

            if (IsOwner)
            {
                ragdollController?.SetExternalCamera(killCameraAll);
            }
        }

        public void ServerTriggerBlackoutForOwner()
        {
            ServerTriggerBlackoutForOwner(true);
        }

        public void ServerTriggerBlackoutForOwner(bool useFade)
        {
            if (IsNetworkActive() && !IsServer)
            {
                return;
            }

            if (IsNetworkActive())
            {
                TriggerBlackoutClientRpc(useFade, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { OwnerClientId }
                    }
                });
            }
            else
            {
                TriggerBlackoutLocal(useFade);
            }
        }

        [ClientRpc]
        private void TriggerBlackoutClientRpc(bool useFade, ClientRpcParams clientRpcParams = default)
        {
            TriggerBlackoutLocal(useFade);
        }

        private void TriggerBlackoutLocal(bool useFade)
        {
            if (!IsOwner && IsNetworkActive())
            {
                return;
            }

            // Always drop killcam before blackout so we return to local view (or a black screen).
            ragdollController?.ClearExternalCamera();

            if (deathBlackout != null)
            {
                deathBlackout.enabled = true;
                deathBlackout.TriggerBlackout(useFade);
            }
        }

        private Camera ResolveAttackerKillCamera(bool hasAttackerNetworkId, ulong attackerNetworkId, Transform attackerTransform)
        {
            Transform attacker = attackerTransform;
            if (hasAttackerNetworkId && NetworkManager != null && NetworkManager.SpawnManager != null)
            {
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerNetworkId, out NetworkObject netObj) && netObj != null)
                {
                    attacker = netObj.transform;
                }
            }

            if (attacker == null)
            {
                // Fallback for non-networked or unresolved dinos.
                DinoKillCamera[] killCameras = FindObjectsByType<DinoKillCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (killCameras != null && killCameras.Length > 0)
                {
                    Transform self = transform;
                    float bestSqr = float.MaxValue;
                    DinoKillCamera best = null;
                    for (int i = 0; i < killCameras.Length; i++)
                    {
                        DinoKillCamera candidate = killCameras[i];
                        if (candidate == null || candidate.KillCamera == null)
                        {
                            continue;
                        }

                        float sqr = (candidate.transform.position - self.position).sqrMagnitude;
                        if (sqr < bestSqr)
                        {
                            bestSqr = sqr;
                            best = candidate;
                        }
                    }

                    if (best != null)
                    {
                        return best.KillCamera;
                    }
                }

                return null;
            }

            DinoKillCamera killCamera = attacker.GetComponentInChildren<DinoKillCamera>(true);
            if (killCamera != null && killCamera.KillCamera != null)
            {
                return killCamera.KillCamera;
            }

            return attacker.GetComponentInChildren<Camera>(true);
        }

        private Transform ResolveBitePoint(ulong dinoNetworkId, string bitePointPath)
        {
            if (!IsNetworkActive() || NetworkManager == null)
            {
                return null;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(dinoNetworkId, out NetworkObject netObj))
            {
                return null;
            }

            if (netObj == null)
            {
                return null;
            }

            return FindChildByPath(netObj.transform, bitePointPath);
        }

        private void LateUpdate()
        {
            if (!isGrabbedHold || grabHoldPoint == null)
            {
                return;
            }

            Vector3 target = grabHoldPoint.position;

            if (playerMovement == null)
            {
                transform.position = target;
                return;
            }

            CapsuleCollider capsule = playerMovement.GetCapsuleCollider();
            if (capsule == null)
            {
                transform.position = target;
                return;
            }

            if (TryClampGrabHoldToGround(target, capsule, out Vector3 clampedPosition))
            {
                transform.position = clampedPosition;
                return;
            }

            transform.position = target;
        }

        private bool TryClampGrabHoldToGround(Vector3 holdPosition, CapsuleCollider capsule, out Vector3 clampedPosition)
        {
            clampedPosition = holdPosition;

            int ignoreMask = LayerMask.GetMask("Player");
            int groundMask = ~ignoreMask;

            Vector3 rayStart = new Vector3(holdPosition.x, holdPosition.y + 2f, holdPosition.z);
            if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit groundHit, 5f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // Only clamp if the hold point is below (or effectively inside) the ground.
            if (holdPosition.y >= groundHit.point.y + 0.02f)
            {
                return false;
            }

            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleY = Mathf.Abs(transform.lossyScale.y);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);
            float radius = capsule.radius * Mathf.Max(scaleX, scaleZ);
            float halfHeight = Mathf.Max(radius, capsule.height * scaleY * 0.5f);
            float cylinderHalf = Mathf.Max(0f, halfHeight - radius);
            Vector3 centerOffset = transform.TransformPoint(capsule.center) - transform.position;

            Vector3 startCenter = new Vector3(
                holdPosition.x,
                groundHit.point.y + halfHeight + 0.5f,
                holdPosition.z) + new Vector3(centerOffset.x, 0f, centerOffset.z);

            Vector3 p1 = startCenter + Vector3.up * cylinderHalf;
            Vector3 p2 = startCenter - Vector3.up * cylinderHalf;
            if (Physics.CapsuleCast(p1, p2, radius, Vector3.down, out RaycastHit hit, 10f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 desiredCenter = startCenter + Vector3.down * hit.distance;
                clampedPosition = desiredCenter - centerOffset;
                return true;
            }

            clampedPosition = new Vector3(holdPosition.x, groundHit.point.y + 0.05f, holdPosition.z);
            return true;
        }

        private static Transform FindChildByPath(Transform root, string path)
        {
            if (root == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return root;
            }

            string[] parts = path.Split('/');
            Transform current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                if (current == null)
                {
                    return null;
                }

                current = current.Find(parts[i]);
            }

            return current;
        }

        private bool IsNetworkActive()
        {
            return NetworkManager != null && IsSpawned;
        }
    }
}
