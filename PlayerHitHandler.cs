using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

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
        [SerializeField] private float reviveHoldSeconds = 10f;
        [SerializeField] private float reviveRange = 2f;
        [SerializeField] private float revivePulseInterval = 0.1f;
        [SerializeField] private LayerMask playerMask = 0;
        [SerializeField] private float autoRecoverSeconds = 0f;

        [Header("Instakill Audio")]
        [SerializeField] private AudioSource instakillDeathAudioSource;
        [SerializeField] private AudioClip[] instakillDeathClips;
        [SerializeField, Range(0f, 1f)] private float instakillDeathVolume = 1f;
        [SerializeField] private Vector2 instakillDeathPitchRange = new Vector2(0.95f, 1.05f);

        private readonly NetworkVariable<bool> syncedIsDowned = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private PlayerMovement playerMovement;
        private PlayerRagdollController ragdollController;
        private PlayerDeathBlackout deathBlackout;
        private float nextRevivePulseTime;
        private bool isReviving;
        private PlayerHitHandler currentReviveTarget;

        private ulong activeReviverClientId = ulong.MaxValue;
        private float reviveProgressSeconds;
        private float lastReviveTickTime;
        private Coroutine biteHoldRoutine;
        private bool isGrabbedHold;
        private bool isGrabHoldActive;
        private bool lastHoldUsedRagdoll;
        private Transform grabHoldPoint;
        private bool pendingDropRagdollOnRelease;
        private float pendingDropRagdollDuration;
        private Coroutine dropRagdollRoutine;
        private Coroutine autoRecoverRoutine;
        private Coroutine pendingInstakillRoutine;

        public bool IsDowned => IsNetworkActive() ? syncedIsDowned.Value : false;

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
            if (playerMask == 0)
            {
                playerMask = LayerMask.GetMask("Player");
            }

            if (instakillDeathAudioSource == null)
            {
                instakillDeathAudioSource = GetComponent<AudioSource>();
                if (instakillDeathAudioSource == null)
                {
                    instakillDeathAudioSource = GetComponentInChildren<AudioSource>(true);
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            syncedIsDowned.OnValueChanged += HandleDownedChanged;
            ApplyDownedState(syncedIsDowned.Value);
        }

        public override void OnNetworkDespawn()
        {
            syncedIsDowned.OnValueChanged -= HandleDownedChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (IsNetworkActive() && IsServer && syncedIsDowned.Value && playerMovement != null && playerMovement.IsDead)
            {
                syncedIsDowned.Value = false;
            }

            if (!IsNetworkActive() || !IsOwner)
            {
                return;
            }

            if (playerMovement == null || playerMovement.IsDead || IsDowned)
            {
                CancelReviveIfNeeded();
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            bool wantsRevive = Keyboard.current.eKey.isPressed;
            if (!wantsRevive)
            {
                CancelReviveIfNeeded();
                return;
            }

            PlayerHitHandler target = FindClosestDownedTarget();
            if (target == null)
            {
                CancelReviveIfNeeded();
                return;
            }

            if (currentReviveTarget != target)
            {
                CancelReviveIfNeeded();
                currentReviveTarget = target;
            }

            if (Time.unscaledTime < nextRevivePulseTime)
            {
                return;
            }

            currentReviveTarget.RequestReviveServerRpc(true);
            nextRevivePulseTime = Time.unscaledTime + Mathf.Max(0.02f, revivePulseInterval);
            isReviving = true;
        }

        private void OnDisable()
        {
            CancelReviveIfNeeded();
        }

        private void CancelReviveIfNeeded()
        {
            if (!isReviving || currentReviveTarget == null)
            {
                isReviving = false;
                currentReviveTarget = null;
                return;
            }

            currentReviveTarget.RequestReviveServerRpc(false);
            isReviving = false;
            currentReviveTarget = null;
        }

        private PlayerHitHandler FindClosestDownedTarget()
        {
            float radius = Mathf.Max(0.1f, reviveRange);
            Collider[] hits = Physics.OverlapSphere(transform.position, radius, playerMask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            PlayerHitHandler closest = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                PlayerHitHandler handler = hit.GetComponentInParent<PlayerHitHandler>();
                if (handler == null || handler == this || !handler.IsDowned)
                {
                    continue;
                }

                float sqr = (handler.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    closest = handler;
                }
            }

            return closest;
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

            if (ragdollController == null)
            {
                ragdollController = GetComponent<PlayerRagdollController>();
                if (ragdollController == null)
                {
                    ragdollController = GetComponentInChildren<PlayerRagdollController>(true);
                }
            }

            if (ragdollController != null)
            {
                ragdollController.SetDownedRagdoll(isDowned);
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
                    syncedIsDowned.Value = false;
                }

                TriggerInstakillDeathSound();
                return playerMovement.ServerKill();
            }

            if (playerMovement.IsDead || syncedIsDowned.Value)
            {
                return false;
            }

            syncedIsDowned.Value = true;
            activeReviverClientId = ulong.MaxValue;
            reviveProgressSeconds = 0f;
            lastReviveTickTime = Time.time;
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
                syncedIsDowned.Value = false;
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
                ApplyDeathRagdollClientRpc(impulse, hasAttackerNetworkId, attackerNetworkId);
            }

            TriggerInstakillDeathSound();
            return playerMovement.ServerKill();
        }

        private void StartAutoRecoverIfNeeded()
        {
            if (!IsNetworkActive() || !IsServer)
            {
                return;
            }

            float duration = Mathf.Max(0f, autoRecoverSeconds);
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

            if (!IsNetworkActive() || !IsServer)
            {
                autoRecoverRoutine = null;
                yield break;
            }

            if (syncedIsDowned.Value)
            {
                syncedIsDowned.Value = false;
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

        public void ServerBeginGrabHold(ulong dinoNetworkId, string bitePointPath, float holdSeconds, float dropRagdollDurationSeconds = 0f)
        {
            if (IsNetworkActive() && !IsServer)
            {
                return;
            }

            if (biteHoldRoutine != null)
            {
                StopCoroutine(biteHoldRoutine);
            }

            biteHoldRoutine = StartCoroutine(BiteHoldRoutine(dinoNetworkId, bitePointPath, holdSeconds, HitResult.None, false, dropRagdollDurationSeconds));
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

        private IEnumerator BiteHoldRoutine(ulong dinoNetworkId, string bitePointPath, float holdSeconds, HitResult finalResult, bool useRagdoll, float dropRagdollDurationSeconds = 0f)
        {
            BeginBiteHoldClientRpc(dinoNetworkId, bitePointPath, useRagdoll, dropRagdollDurationSeconds);

            float duration = Mathf.Max(0.05f, holdSeconds);
            yield return new WaitForSeconds(duration);

            EndBiteHoldClientRpc(finalResult == HitResult.Instakill);
            biteHoldRoutine = null;

            if (finalResult == HitResult.Instakill)
            {
                Debug.Log($"PlayerHitHandler: Instakill resolved for '{name}' after bite hold.");
                if (syncedIsDowned.Value)
                {
                    syncedIsDowned.Value = false;
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
                syncedIsDowned.Value = true;
            }
        }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestReviveServerRpc(bool isHolding, ServerRpcParams rpcParams = default)
        {
            if (!syncedIsDowned.Value)
            {
                return;
            }

            ulong senderId = rpcParams.Receive.SenderClientId;
            if (isHolding)
            {
                if (activeReviverClientId != senderId)
                {
                    activeReviverClientId = senderId;
                    reviveProgressSeconds = 0f;
                    lastReviveTickTime = Time.time;
                }

                float now = Time.time;
                float delta = Mathf.Max(0f, now - lastReviveTickTime);
                reviveProgressSeconds += delta;
                lastReviveTickTime = now;

                if (reviveProgressSeconds >= Mathf.Max(0.1f, reviveHoldSeconds))
                {
                    syncedIsDowned.Value = false;
                    activeReviverClientId = ulong.MaxValue;
                    reviveProgressSeconds = 0f;
                }

                return;
            }

            if (activeReviverClientId == senderId)
            {
                activeReviverClientId = ulong.MaxValue;
                reviveProgressSeconds = 0f;
            }
        }

        [ClientRpc]
        private void BeginBiteHoldClientRpc(ulong dinoNetworkId, string bitePointPath, bool useRagdoll, float dropRagdollDurationSeconds)
        {
            Transform bitePoint = ResolveBitePoint(dinoNetworkId, bitePointPath);
            lastHoldUsedRagdoll = useRagdoll;
            if (useRagdoll)
            {
                isGrabHoldActive = false;
                if (ragdollController != null)
                {
                    ragdollController.BeginHold(bitePoint);
                }
            }
            else
            {
                grabHoldPoint = bitePoint;
                isGrabbedHold = grabHoldPoint != null;
                isGrabHoldActive = true;
                pendingDropRagdollOnRelease = dropRagdollDurationSeconds > 0.01f;
                pendingDropRagdollDuration = Mathf.Max(0.05f, dropRagdollDurationSeconds);
                if (playerMovement != null)
                {
                    playerMovement.SetGrabbed(isGrabbedHold);
                }
            }
        }

        [ClientRpc]
        private void EndBiteHoldClientRpc(bool wasInstakill)
        {
            if (wasInstakill)
            {
                PlayInstakillDeathSoundLocal();
            }

            if (isGrabHoldActive)
            {
                ClearGrabHoldState();
                if (pendingDropRagdollOnRelease)
                {
                    ApplyDropRagdollNow(pendingDropRagdollDuration);
                    pendingDropRagdollOnRelease = false;
                }
                return;
            }

            if (lastHoldUsedRagdoll && ragdollController != null)
            {
                ragdollController.EndHold(true);
            }
        }

        private void TriggerInstakillDeathSound()
        {
            if (IsNetworkActive())
            {
                if (!IsServer)
                {
                    return;
                }

                PlayInstakillDeathSoundClientRpc();
                return;
            }

            PlayInstakillDeathSoundLocal();
        }

        [ClientRpc]
        private void PlayInstakillDeathSoundClientRpc()
        {
            PlayInstakillDeathSoundLocal();
        }

        private void PlayInstakillDeathSoundLocal()
        {
            if (instakillDeathAudioSource == null || instakillDeathClips == null || instakillDeathClips.Length == 0)
            {
                return;
            }

            AudioClip clip = instakillDeathClips[Random.Range(0, instakillDeathClips.Length)];
            if (clip == null)
            {
                return;
            }

            float pitchMin = Mathf.Min(instakillDeathPitchRange.x, instakillDeathPitchRange.y);
            float pitchMax = Mathf.Max(instakillDeathPitchRange.x, instakillDeathPitchRange.y);
            instakillDeathAudioSource.pitch = Random.Range(pitchMin, pitchMax);
            instakillDeathAudioSource.PlayOneShot(clip, Mathf.Clamp01(instakillDeathVolume));
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
        }

        private void ApplyDropRagdollNow(float durationSeconds)
        {
            if (ragdollController == null || playerMovement == null)
            {
                return;
            }

            if (syncedIsDowned.Value || playerMovement.IsDead)
            {
                return;
            }

            if (dropRagdollRoutine != null)
            {
                StopCoroutine(dropRagdollRoutine);
            }

            dropRagdollRoutine = StartCoroutine(DropRagdollRoutine(durationSeconds));
        }

        private IEnumerator DropRagdollRoutine(float durationSeconds)
        {
            ragdollController.SetDownedRagdoll(true);
            yield return new WaitForSeconds(durationSeconds);
            yield return null;
            yield return null;
            if (IsNetworkActive())
            {
                if (IsServer)
                {
                    ApplyServerSnapToRagdoll();
                }
            }
            else
            {
                SnapRootToRagdollOnly();
            }
            ragdollController.SetDownedRagdoll(false);
            dropRagdollRoutine = null;
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
            if (IsNetworkActive() && !IsServer)
            {
                return;
            }

            if (IsNetworkActive())
            {
                TriggerBlackoutClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { OwnerClientId }
                    }
                });
            }
            else
            {
                TriggerBlackoutLocal();
            }
        }

        [ClientRpc]
        private void TriggerBlackoutClientRpc(ClientRpcParams clientRpcParams = default)
        {
            TriggerBlackoutLocal();
        }

        private void TriggerBlackoutLocal()
        {
            if (!IsOwner && IsNetworkActive())
            {
                return;
            }

            if (deathBlackout != null)
            {
                // Drop killcam before blackout so we return to local view + black screen.
                ragdollController?.ClearExternalCamera();
                deathBlackout.enabled = true;
                deathBlackout.TriggerImmediateBlackout();
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

            transform.position = grabHoldPoint.position;
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
